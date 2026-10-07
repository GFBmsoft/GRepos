using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed class RefChipViewModel
{
    public string Text { get; init; } = "";
    public string Color { get; init; } = "TextDim";
    public string Tooltip { get; init; } = "";
}

public sealed class CommitRowViewModel
{
    public Commit Commit { get; init; } = new();
    public GraphRow Row { get; init; } = new();
    public int MaxLanes { get; init; }

    /// <summary>Largura da coluna do grafo: 14px por raia, com teto para não comer a lista.</summary>
    public double GraphWidth => Math.Min(MaxLanes * 14 + 12, 180);

    public string Subject => Commit.Subject;
    public string Author => Commit.Author;
    public string Short => Commit.Short;
    public string DateText => FormatDate(Commit.Date, false);

    /// <summary>
    /// Só o chip mais relevante fica na lista; o resto vira "+N". As refs completas
    /// estão no painel lateral, e repetir tudo em cada linha polui a leitura.
    /// </summary>
    private const int MaxChips = 1;

    /// <summary>Data curta e relativa: "3 h", "ontem", "12/08". O completo fica no painel.</summary>
    public string ShortDate
    {
        get
        {
            if (!DateTimeOffset.TryParse(Commit.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return "";

            var local = d.ToLocalTime();
            var agora = DateTimeOffset.Now;
            var diff = agora - local;

            if (diff.TotalMinutes < 1) return "agora";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min";
            if (diff.TotalHours < 24 && local.Date == agora.Date) return $"{(int)diff.TotalHours} h";
            if (local.Date == agora.Date.AddDays(-1)) return "ontem";
            // o formato já fixa dia/mês; pedir a cultura pt-BR pelo nome só acrescenta
            // um jeito de falhar (ela não existe em modo globalização-invariante)
            if (local.Year == agora.Year) return local.ToString("dd/MM", CultureInfo.InvariantCulture);
            return local.ToString("dd/MM/yy", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Tag em laranja (o mesmo do app), branch atual no azul de destaque e remotos
    /// apagados: o olho acha a versão sem ler chip por chip. O excesso vira "+N",
    /// senão o último chip fica cortado ao meio e o assunto perde espaço.
    /// </summary>
    public List<RefChipViewModel> Refs
    {
        get
        {
            var ordenados = Commit.Refs.OrderBy(Prioridade).ToList();
            var chips = ordenados.Take(MaxChips).Select(r => new RefChipViewModel
            {
                Text = r.Replace("tag: ", ""),
                Color = RefColor(r),
            }).ToList();

            var resto = ordenados.Count - chips.Count;
            if (resto > 0)
                chips.Add(new RefChipViewModel
                {
                    Text = $"+{resto}",
                    Color = "TextDim",
                    Tooltip = string.Join("\n", ordenados.Skip(MaxChips).Select(r => r.Replace("tag: ", ""))),
                });

            return chips;
        }
    }

    public static string RefColor(string r) =>
        r.StartsWith("tag:") ? "Orange" : r.StartsWith("HEAD") ? "Accent" : "TextDim";

    private static int Prioridade(string r) =>
        r.StartsWith("HEAD") ? 0 : r.StartsWith("tag:") ? 1 : r.Contains('/') ? 3 : 2;

    public static string FormatDate(string iso, bool full)
    {
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return iso;
        var local = d.ToLocalTime();
        return full
            ? local.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo("pt-BR"))
            : local.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
    }
}

public sealed class CommitFileViewModel
{
    public CommitFile File { get; init; } = new();
    public string Path => File.Path;
    public string Status => File.Status;
    public string AddedText => $"+{File.Added}";
    public string RemovedText => $"-{File.Removed}";

    public string StatusColor => Status switch
    {
        "M" => "Yellow",
        "A" => "Green",
        "D" => "Red",
        "R" or "C" => "Purple",
        _ => "TextDim",
    };
}

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly MainViewModel _main;
    private readonly int _limit;

    public HistoryViewModel(Repo repo, MainViewModel main, int limit, bool split)
    {
        _repo = repo;
        _main = main;
        _limit = limit;
        Diff = new DiffViewModel { Split = split, Externo = AbrirNoDiffExternoAsync, Recarregar = RecarregarDiffAsync };
    }

    public DiffViewModel Diff { get; }

    [ObservableProperty] private ObservableCollection<CommitRowViewModel> _commits = new();
    [ObservableProperty] private CommitRowViewModel? _selectedCommit;
    [ObservableProperty] private ObservableCollection<CommitFileViewModel> _files = new();
    [ObservableProperty] private CommitFileViewModel? _selectedFile;
    [ObservableProperty] private bool _allBranches = true;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private string _detailSubject = "";
    [ObservableProperty] private string _detailAuthor = "";
    [ObservableProperty] private string _detailDate = "";
    [ObservableProperty] private string _detailHash = "";
    [ObservableProperty] private string _detailParents = "";
    [ObservableProperty] private string _detailBody = "";

    /// <summary>Refs completas do commit — na lista fica só a principal.</summary>
    [ObservableProperty] private ObservableCollection<RefChipViewModel> _detailRefs = new();

    public bool HasRefs => DetailRefs.Count > 0;

    partial void OnDetailRefsChanged(ObservableCollection<RefChipViewModel> value) =>
        OnPropertyChanged(nameof(HasRefs));

    public int Count => Commits.Count;

    public bool HasBody => !string.IsNullOrWhiteSpace(DetailBody);
    public string DetailParentsText => string.IsNullOrEmpty(DetailParents) ? "" : $"pais: {DetailParents}";

    partial void OnDetailBodyChanged(string value) => OnPropertyChanged(nameof(HasBody));

    partial void OnDetailParentsChanged(string value) => OnPropertyChanged(nameof(DetailParentsText));

    partial void OnAllBranchesChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedCommitChanged(CommitRowViewModel? value) => _ = LoadDetailAsync(value);

    partial void OnSelectedFileChanged(CommitFileViewModel? value) => _ = LoadFileDiffAsync(value);

    // ---------------------------------------------------------------- busca

    /// <summary>Texto da busca: mensagem, autor ou hash. Vazio volta ao histórico normal.</summary>
    [ObservableProperty] private string _busca = "";

    public bool Buscando => !string.IsNullOrWhiteSpace(Busca);
    public string ContagemTexto => Buscando ? $"{Count} encontrado(s)" : Count.ToString();

    private System.Threading.CancellationTokenSource? _esperaBusca;

    partial void OnBuscaChanged(string value)
    {
        OnPropertyChanged(nameof(Buscando));
        // espera a digitação parar: um git log por tecla deixaria a lista pulando
        _esperaBusca?.Cancel();
        var cts = _esperaBusca = new System.Threading.CancellationTokenSource();
        _ = Task.Delay(300, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = LoadAsync());
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private void LimparBusca() => Busca = "";

    public async Task LoadAsync()
    {
        try
        {
            var termo = Busca;
            List<GraphRow> rows;
            if (string.IsNullOrWhiteSpace(termo))
            {
                rows = GraphBuilder.Build(await GitService.LogAsync(_repo.Path, _limit, AllBranches));
            }
            else
            {
                // resultado da busca não é contínuo: sem grafo, só o ponto de cada commit
                rows = (await GitService.SearchLogAsync(_repo.Path, termo, _limit, AllBranches))
                    .Select(c => new GraphRow { Commit = c }).ToList();
            }
            if (termo != Busca) return; // a busca mudou enquanto o git respondia
            var max = Math.Max(1, GraphBuilder.MaxLanes(rows));

            Commits = new ObservableCollection<CommitRowViewModel>(
                rows.Select(r => new CommitRowViewModel { Commit = r.Commit, Row = r, MaxLanes = max }));
            OnPropertyChanged(nameof(ContagemTexto));

            OnPropertyChanged(nameof(Count));
            SelectedCommit = null;
            HasDetail = false;
            Files = new ObservableCollection<CommitFileViewModel>();
            Diff.Clear("Selecione um commit para ver os detalhes.");
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private async Task LoadDetailAsync(CommitRowViewModel? row)
    {
        if (row is null)
        {
            HasDetail = false;
            return;
        }
        try
        {
            var d = await GitService.CommitDetailAsync(_repo.Path, row.Commit.Hash);
            DetailSubject = d.Commit.Subject;
            DetailAuthor = $"{d.Commit.Author} <{d.Commit.Email}>";
            DetailDate = CommitRowViewModel.FormatDate(d.Commit.Date, true);
            DetailHash = d.Commit.Short;
            DetailParents = d.Commit.Parents.Count == 0
                ? "—"
                : string.Join(", ", d.Commit.Parents.Select(p => p.Length >= 7 ? p[..7] : p));
            DetailBody = d.Body;
            DetailRefs = new ObservableCollection<RefChipViewModel>(
                d.Commit.Refs.Select(r => new RefChipViewModel
                {
                    Text = r.Replace("tag: ", ""),
                    Color = CommitRowViewModel.RefColor(r),
                }));
            HasDetail = true;

            Files = new ObservableCollection<CommitFileViewModel>(
                d.Files.Select(f => new CommitFileViewModel { File = f }));
            SelectedFile = Files.FirstOrDefault();
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    // ------------------------------------------------------ ações no commit
    // As do menu de contexto de commit do GitKraken: criar branch/tag ali, cherry-pick,
    // reverter e resetar a branch atual. Todas agem sobre o commit selecionado.

    [ObservableProperty] private bool _busy;

    private async Task AcaoAsync(Func<Commit, Task> acao, string? aviso = null)
    {
        if (SelectedCommit is not { } row || Busy) return;
        Busy = true;
        try
        {
            await acao(row.Commit);
            if (aviso is not null) _main.Notify(aviso);
        }
        catch (Exception e)
        {
            _main.Notify(ExplicarFalha(e.Message), true);
        }
        finally
        {
            Busy = false;
            // mesmo com erro: um cherry-pick em conflito já mexeu no repositório
            await LoadAsync();
            try { await _main.RefreshRepoAsync(_repo.Id); } catch (Exception) { /* só o contador */ }
        }
    }

    /// <summary>Conflito em cherry-pick/revert vira instrução, não só a saída crua do git.</summary>
    public static string ExplicarFalha(string erro) =>
        erro.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
        erro.Contains("could not apply", StringComparison.OrdinalIgnoreCase) ||
        erro.Contains("could not revert", StringComparison.OrdinalIgnoreCase)
            ? "Parou em conflito. Resolva os arquivos na aba Alterações e faça o commit; " +
              "para desistir, use Alterações → Reverter → Voltar ao último commit."
            : erro;

    private static bool EhMerge(Commit c) => c.Parents.Count > 1;

    // ------------------------------------- soltar uma branch sobre outra no grafo

    /// <summary>O remoto é do GitHub: o menu de soltar oferece pull request.</summary>
    public bool TemGitHub => _main.TemGitHub;

    /// <summary>
    /// Arrastar o commit de uma branch sobre o de outra: mesclar, rebase ou pull request.
    /// Vale a branch que aponta para cada commit; linha sem branch não arrasta nem recebe.
    /// </summary>
    public async Task ExecutarOpcaoAsync(OpcaoDeArraste opcao, RefDeBranch origem, RefDeBranch destino)
    {
        if (!opcao.Disponivel || Busy) return;

        Busy = true;
        try
        {
            if (opcao.Acao == AcaoDeArraste.PullRequest)
            {
                if (await _main.GitHubDeAsync(_repo) is not { } gh) return;

                var vm = await _main.MontarPullRequestsAsync(
                    _repo, gh.Slug, gh.Usuario, origem.NomeLocal, destino.NomeLocal);
                await _main.Dialogos.ShowPullRequestsAsync(vm);
                await _main.DepoisDosPullRequestsAsync(_repo, vm);
                return;
            }

            if (await _main.ExecutarArrasteAsync(_repo, opcao, origem, destino))
                _main.Notify(opcao.Acao == AcaoDeArraste.Mesclar
                    ? $"{origem.Nome} mesclada em {destino.Nome}."
                    : $"{origem.Nome} reaplicada sobre {destino.Nome}.");
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
        finally
        {
            Busy = false;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task HistoricoDoArquivo() =>
        SelectedFile is { } f ? _main.MostrarHistoricoDoArquivoAsync(_repo, f.Path, blame: false) : Task.CompletedTask;

    [RelayCommand]
    private Task AutoriaDoArquivo() =>
        SelectedFile is { } f ? _main.MostrarHistoricoDoArquivoAsync(_repo, f.Path, blame: true) : Task.CompletedTask;

    /// <summary>"Arquivo inteiro" do painel: o mesmo arquivo, com o contexto novo.</summary>
    private Task RecarregarDiffAsync() => LoadFileDiffAsync(SelectedFile);

    /// <summary>O arquivo antes e depois do commit selecionado, na ferramenta externa.</summary>
    [RelayCommand]
    private Task AbrirNoDiffExternoAsync()
    {
        if (SelectedCommit is not { } c || SelectedFile is not { } f)
        {
            _main.Notify("Selecione um arquivo do commit para comparar.");
            return Task.CompletedTask;
        }
        return _main.AbrirDiffExternoAsync(_repo,
            VersaoDeArquivo.Em(c.Commit.Hash + "^", f.Path, "antes-de-" + c.Short),
            VersaoDeArquivo.Em(c.Commit.Hash, f.Path, c.Short));
    }

    /// <summary>O que muda do commit selecionado até a branch atual.</summary>
    [RelayCommand]
    private Task CompararComAtual() =>
        SelectedCommit is { } row ? _main.CompararAsync(_repo, row.Commit.Hash[..Math.Min(10, row.Commit.Hash.Length)]) : Task.CompletedTask;

    /// <summary>Rebase interativo do commit selecionado até o HEAD.</summary>
    [RelayCommand]
    private async Task Reorganizar()
    {
        if (SelectedCommit is not { } row) return;
        await _main.MostrarRebaseAsync(_repo, row.Commit.Hash);
    }

    [RelayCommand]
    private async Task CriarBranchAqui()
    {
        if (SelectedCommit is not { } row) return;
        var nome = await _main.PromptAsync("Criar branch", $"Nome da nova branch a partir de {row.Short}:");
        if (string.IsNullOrWhiteSpace(nome)) return;
        await AcaoAsync(c => GitService.CreateBranchAtAsync(_repo.Path, nome.Trim(), c.Hash),
            $"Branch {nome.Trim()} criada em {row.Short}.");
    }

    [RelayCommand]
    private async Task CriarTagAqui()
    {
        if (SelectedCommit is not { } row) return;
        var nome = await _main.PromptAsync("Criar tag", $"Nome da tag em {row.Short}:");
        if (string.IsNullOrWhiteSpace(nome)) return;
        await AcaoAsync(c => GitService.CreateTagAsync(_repo.Path, nome.Trim(), c.Hash),
            $"Tag {nome.Trim()} criada em {row.Short}. Ela só vai ao remoto com um push da tag.");
    }

    [RelayCommand]
    private async Task CherryPick()
    {
        if (SelectedCommit is not { } row) return;
        var ok = await _main.ConfirmAsync("Cherry-pick",
            $"Aplicar o commit {row.Short} \"{row.Subject}\" na branch atual?\n\n" +
            "Um commit novo, com as mesmas alterações, será criado." +
            (EhMerge(row.Commit) ? "\n\nÉ um merge: as alterações são tomadas em relação ao primeiro pai." : ""));
        if (!ok) return;
        await AcaoAsync(c => GitService.CherryPickAsync(_repo.Path, c.Hash, EhMerge(c)),
            $"Commit {row.Short} aplicado na branch atual.");
    }

    [RelayCommand]
    private async Task ReverterCommit()
    {
        if (SelectedCommit is not { } row) return;
        var ok = await _main.ConfirmAsync("Reverter commit",
            $"Criar um commit que desfaz {row.Short} \"{row.Subject}\"?\n\n" +
            "O histórico não é reescrito: dá para enviar normalmente, mesmo que o commit já esteja no remoto.");
        if (!ok) return;
        await AcaoAsync(c => GitService.RevertCommitAsync(_repo.Path, c.Hash, EhMerge(c)),
            $"Commit {row.Short} revertido.");
    }

    [RelayCommand]
    private Task ResetSoft() => ResetarAsync(GitService.ModoReset.Soft,
        "As alterações dos commits desfeitos ficam preparadas para um novo commit.");

    [RelayCommand]
    private Task ResetMixed() => ResetarAsync(GitService.ModoReset.Mixed,
        "As alterações dos commits desfeitos voltam para alterações locais (não preparadas).");

    [RelayCommand]
    private Task ResetHard() => ResetarAsync(GitService.ModoReset.Hard,
        "As alterações dos commits desfeitos E as alterações locais pendentes serão DESCARTADAS.\n\n" +
        "Esta ação não pode ser desfeita.");

    private async Task ResetarAsync(GitService.ModoReset modo, string efeito)
    {
        if (SelectedCommit is not { } row) return;
        var ok = await _main.ConfirmAsync("Resetar branch",
            $"Mover a branch atual para {row.Short} \"{row.Subject}\"?\n\n{efeito}\n\n" +
            "Se os commits desfeitos já estiverem no remoto, o próximo envio vai exigir push forçado.");
        if (!ok) return;
        await AcaoAsync(c => GitService.ResetAsync(_repo.Path, c.Hash, modo),
            $"Branch movida para {row.Short}.");
    }

    private async Task LoadFileDiffAsync(CommitFileViewModel? file)
    {
        if (file is null || SelectedCommit is null)
        {
            Diff.Clear("Selecione um arquivo do commit.");
            return;
        }
        try
        {
            var raw = await GitService.CommitFileDiffAsync(
                _repo.Path, SelectedCommit.Commit.Hash, file.Path, Diff.LinhasDeContexto);
            Diff.Title = file.Path;
            Diff.Load(raw);
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }
}
