using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed partial class FileItemViewModel : ObservableObject
{
    public FileChange Change { get; init; } = new();
    public bool Staged { get; init; }

    public string Path => Change.Path;
    public string Tooltip => Change.OrigPath is { Length: > 0 } o ? $"{o} → {Change.Path}" : Change.Path;

    public string Code => Staged
        ? Change.Index
        : Change.Kind == ChangeKind.Untracked ? "?" : Change.Worktree;

    /// <summary>Cor do código de status, no padrão do git (M amarelo, A verde, D vermelho…).</summary>
    public string CodeColor => Code switch
    {
        "M" => "Yellow",
        "A" or "?" => "Green",
        "D" => "Red",
        "R" or "C" => "Purple",
        "U" => "Red",
        _ => "TextDim",
    };

    public bool CanDiscard => Change.Kind != ChangeKind.Conflict;
    public bool IsConflito => Change.Kind == ChangeKind.Conflict;
    public bool PodePreparar => !IsConflito;

    /// <summary>
    /// Os dois lados têm o arquivo (UU, AA): há marcadores para resolver bloco a bloco.
    /// Com um D no XY um lado apagou, e a escolha é só ficar ou não com o arquivo.
    /// </summary>
    public bool ConflitoDeConteudo => IsConflito && !Change.Conflito.Contains('D') && Change.Conflito is not ("AU" or "UA");
}

public sealed partial class ChangesViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly MainViewModel _main;

    public ChangesViewModel(Repo repo, MainViewModel main, bool split)
    {
        _repo = repo;
        _main = main;
        Diff = new DiffViewModel { Split = split, Externo = AbrirNoDiffExternoAsync, Recarregar = RecarregarDiffAsync };
    }

    public DiffViewModel Diff { get; }

    [ObservableProperty] private ObservableCollection<FileItemViewModel> _staged = new();
    [ObservableProperty] private ObservableCollection<FileItemViewModel> _unstaged = new();
    [ObservableProperty] private FileItemViewModel? _selectedStaged;
    [ObservableProperty] private FileItemViewModel? _selectedUnstaged;
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private bool _amend;
    [ObservableProperty] private bool _busy;

    public bool CanCommit => !Busy && !string.IsNullOrWhiteSpace(CommitMessage) && (Staged.Count > 0 || Amend);
    public string CommitCaption => Staged.Count > 0 ? $"Commit ({Staged.Count})" : "Commit";

    partial void OnCommitMessageChanged(string value) => OnPropertyChanged(nameof(CanCommit));
    partial void OnAmendChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCommit));
        _ = CarregarMensagemDoUltimoAsync(value);
    }

    /// <summary>Mensagem posta pelo "Emendar último", para saber se o usuário mexeu nela.</summary>
    private string? _mensagemDoUltimo;

    /// <summary>
    /// Emendar é quase sempre para corrigir a mensagem: ela vem preenchida com a do último
    /// commit. Ao desmarcar, sai — a menos que já tenha sido editada.
    /// </summary>
    private async Task CarregarMensagemDoUltimoAsync(bool emendar)
    {
        if (!emendar)
        {
            if (_mensagemDoUltimo is not null && CommitMessage == _mensagemDoUltimo) CommitMessage = "";
            _mensagemDoUltimo = null;
            return;
        }
        if (!string.IsNullOrWhiteSpace(CommitMessage)) return;
        try
        {
            var msg = await GitService.UltimaMensagemAsync(_repo.Path);
            if (!Amend || !string.IsNullOrWhiteSpace(CommitMessage)) return;
            _mensagemDoUltimo = msg;
            CommitMessage = msg;
        }
        catch (Exception)
        {
            // repositório sem commits: não há o que emendar, e o git avisa no commit
        }
    }
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanCommit));

    partial void OnSelectedStagedChanged(FileItemViewModel? value)
    {
        if (value is null) return;
        SelectedUnstaged = null;
        _ = ShowDiffAsync(value);
    }

    partial void OnSelectedUnstagedChanged(FileItemViewModel? value)
    {
        if (value is null) return;
        SelectedStaged = null;
        _ = ShowDiffAsync(value);
    }

    /// <param name="silent">
    /// Recarga disparada pelo disco: o status vem junto da mesma chamada e erros não
    /// viram aviso na tela, senão o usuário é interrompido enquanto digita.
    /// </param>
    public async Task ReloadAsync(bool silent = false)
    {
        try
        {
            // o contador de ignorados custa um processo a mais: corre junto do status, e
            // fica de fora da recarga disparada pelo disco, que acontece a cada salvamento
            var ignorados = silent ? null : Ignorados.ListarAsync(_repo.Path);
            var (status, list) = await GitService.StatusAndChangesAsync(_repo.Path);
            if (ignorados is not null) TotalIgnorados = (await ignorados).Count;

            // conflito aparece só em "alterações locais" — é onde se resolve. Antes ele
            // entrava nas duas listas, porque tem índice e working tree marcados.
            var staged = list.Where(f => f.Kind == ChangeKind.Tracked && f.Index != ".")
                .Select(f => new FileItemViewModel { Change = f, Staged = true });
            var unstaged = list.Where(f => f.Kind != ChangeKind.Tracked || f.Index == "." || f.Worktree != ".")
                .Select(f => new FileItemViewModel { Change = f, Staged = false });

            var keepStaged = SelectedStaged?.Path;
            var keepUnstaged = SelectedUnstaged?.Path;

            // atualiza no lugar: recriar as coleções faz a lista piscar a cada stage
            ListaSync.Aplicar(Staged, staged.ToList(), f => f.Path, MesmoItem);
            ListaSync.Aplicar(Unstaged, unstaged.ToList(), f => f.Path, MesmoItem);
            OnPropertyChanged(nameof(CanCommit));
            OnPropertyChanged(nameof(CommitCaption));

            // mantém o arquivo que estava aberto; se não houver, abre o primeiro —
            // entrar na aba e ver o painel vazio não ajuda ninguém
            var again = Staged.FirstOrDefault(f => f.Path == keepStaged)
                        ?? (FileItemViewModel?)Unstaged.FirstOrDefault(f => f.Path == keepUnstaged)
                        ?? Unstaged.FirstOrDefault()
                        ?? Staged.FirstOrDefault();

            if (again is null) LimparDiff();
            else if (again.Staged) SelectedStaged = again;
            else SelectedUnstaged = again;

            // o status saiu da mesma chamada: aplicar direto evita um segundo "git status"
            _main.ApplyStatus(_repo.Id, status);
            GuardarRemoto(status);
            Operacao = GitService.OperacaoEmAndamento(_repo.Path);
            OnPropertyChanged(nameof(OperacaoTexto));
        }
        catch (Exception e)
        {
            if (!silent) _main.Notify(e.Message, true);
        }
    }

    /// <summary>Dois itens são o mesmo quando caminho e situação coincidem.</summary>
    private static bool MesmoItem(FileItemViewModel a, FileItemViewModel b) =>
        a.Path == b.Path && a.Code == b.Code && a.Change.Kind == b.Change.Kind;

    private string _lastDiffKey = "";
    private string _lastDiffRaw = "";

    /// <summary>
    /// Sem pendência nenhuma o painel fica vazio de verdade. O título do último arquivo
    /// continuava no cabeçalho — um arquivo que o Delphi grava e desfaz sozinho, como o
    /// .delphilsp.json, parecia alterado num repositório limpo.
    /// </summary>
    private void LimparDiff()
    {
        SelectedStaged = null;
        SelectedUnstaged = null;
        _lastDiffKey = "";
        _lastDiffRaw = "";
        Diff.Title = "";
        Diff.Clear("Nenhuma alteração pendente neste repositório.");
    }

    /// <summary>O item ainda é o que está selecionado (a lista pode ter mudado durante o git)?</summary>
    private bool AindaSelecionado(FileItemViewModel item)
    {
        // a seleção sozinha não basta: sem a lista da tela por trás, ela continua apontando
        // para o item que acabou de sair
        var atual = item.Staged ? SelectedStaged : SelectedUnstaged;
        return atual is not null && atual.Path == item.Path &&
               (item.Staged ? Staged : Unstaged).Any(f => f.Path == item.Path);
    }

    private async Task ShowDiffAsync(FileItemViewModel item)
    {
        try
        {
            var raw = item.IsConflito
                ? await GitService.DiffConflitoAsync(_repo.Path, item.Path)
                : await GitService.DiffFileAsync(
                    _repo.Path, item.Path, item.Staged, item.Change.Kind == ChangeKind.Untracked,
                    Diff.LinhasDeContexto);

            // o arquivo saiu da lista enquanto o git respondia: o diff dele chegaria
            // depois de o painel ter sido limpo e ficaria lá, sem dono
            if (!AindaSelecionado(item)) return;

            var key = $"{item.Staged}|{item.Path}|{Diff.LinhasDeContexto}";

            // diff igual ao que já está na tela não é remontado: evita piscar e
            // perder a posição de rolagem a cada salvamento de arquivo
            if (key == _lastDiffKey && raw == _lastDiffRaw) return;
            _lastDiffKey = key;
            _lastDiffRaw = raw;

            if (item.IsConflito)
            {
                // somente leitura: preparar bloco de arquivo em conflito não faz sentido
                Diff.Title = item.Path + "  (em conflito: marcadores <<<<<<< e >>>>>>> mostram os dois lados)";
                Diff.Load(raw);
                return;
            }

            Diff.Title = item.Path + (item.Staged ? "  (preparado)" : "  (local)");
            Diff.Load(
                raw,
                item.Staged ? "Remover bloco" : "Preparar bloco",
                patch => RunAsync(() => GitService.ApplyPatchAsync(_repo.Path, patch, true, item.Staged)),
                reverso: item.Staged);
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        Busy = true;
        try
        {
            await action();
            await ReloadAsync();
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
        finally
        {
            Busy = false;
        }
    }

    // AllowConcurrentExecutions: por padrão o comando fica indisponível enquanto roda.
    // Como os botões de TODAS as linhas apontam para o mesmo comando, a lista inteira
    // acinzentava e voltava a cada clique — era a piscada do grid.

    // ------------------------------------------- histórico do arquivo

    private string? ArquivoAtual => (SelectedStaged ?? SelectedUnstaged)?.Path;

    [RelayCommand]
    private Task HistoricoDoArquivo() =>
        ArquivoAtual is { } p ? _main.MostrarHistoricoDoArquivoAsync(_repo, p, blame: false) : Task.CompletedTask;

    [RelayCommand]
    private Task AutoriaDoArquivo() =>
        ArquivoAtual is { } p ? _main.MostrarHistoricoDoArquivoAsync(_repo, p, blame: true) : Task.CompletedTask;

    /// <summary>"Arquivo inteiro" do painel: o mesmo arquivo, com o contexto novo.</summary>
    private Task RecarregarDiffAsync() =>
        (SelectedStaged ?? SelectedUnstaged) is { } item ? ShowDiffAsync(item) : Task.CompletedTask;

    // ------------------------------------------------------- diff externo

    /// <summary>
    /// Preparado: último commit × índice. Local: índice × o arquivo do disco, que a
    /// ferramenta abre no lugar e pode salvar. Conflito: a minha versão × a que chega.
    /// </summary>
    [RelayCommand]
    private Task AbrirNoDiffExternoAsync()
    {
        if ((SelectedStaged ?? SelectedUnstaged) is not { } item)
        {
            _main.Notify("Selecione um arquivo para comparar.");
            return Task.CompletedTask;
        }
        var p = item.Path;
        if (p.EndsWith('/'))
        {
            _main.Notify("É uma pasta nova: prepare-a para ver os arquivos e compare um a um.");
            return Task.CompletedTask;
        }

        var antigo = item.Change.OrigPath is { Length: > 0 } o ? o : p;
        var (esquerda, direita) =
            item.IsConflito ? (VersaoDeArquivo.Em(":2", p, "meu"), VersaoDeArquivo.Em(":3", p, "deles"))
            : item.Staged ? (VersaoDeArquivo.Em("HEAD", antigo, "HEAD"), VersaoDeArquivo.NoIndice(p))
            : (VersaoDeArquivo.NoIndice(p), VersaoDeArquivo.NoDisco(p));
        return _main.AbrirDiffExternoAsync(_repo, esquerda, direita);
    }

    // ---------------------------------------------------------- conflitos

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemOperacao), nameof(OperacaoTexto), nameof(ContinuarRotulo))]
    private GitService.Operacao _operacao;

    public bool TemOperacao => Operacao != GitService.Operacao.Nenhuma;

    public string OperacaoTexto
    {
        get
        {
            var nome = Operacao switch
            {
                GitService.Operacao.Merge => "Merge",
                GitService.Operacao.Rebase => "Rebase",
                GitService.Operacao.CherryPick => "Cherry-pick",
                GitService.Operacao.Revert => "Revert",
                _ => "",
            };
            var conflitos = Unstaged.Count(f => f.IsConflito);
            return conflitos > 0
                ? $"{nome} em andamento: {conflitos} arquivo(s) em conflito. Use Resolver para decidir bloco a bloco, ou fique com um lado inteiro (Meu / Deles)."
                : $"{nome} em andamento, sem conflitos pendentes. Continue para concluir.";
        }
    }

    public string ContinuarRotulo => Operacao == GitService.Operacao.Merge ? "Concluir merge" : "Continuar";

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ManterMeu(FileItemViewModel item) =>
        RunAsync(() => GitService.ResolverConflitoAsync(_repo.Path, item.Change, manterMeu: true));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task ManterDeles(FileItemViewModel item) =>
        RunAsync(() => GitService.ResolverConflitoAsync(_repo.Path, item.Change, manterMeu: false));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task MarcarResolvido(FileItemViewModel item) =>
        RunAsync(() => GitService.MarcarResolvidoAsync(_repo.Path, item.Path));

    /// <summary>
    /// Abre a resolução bloco a bloco. Só serve a conflito de conteúdo (os dois lados
    /// mexeram no arquivo); quando um lado apagou, não há marcadores e valem Meu e Deles.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ResolverConflito(FileItemViewModel item)
    {
        try
        {
            var vm = new ConflitoViewModel(_repo.Path, item.Path, Operacao == GitService.Operacao.Rebase);
            await _main.Dialogos.ShowConflitoAsync(vm);
            if (vm.Salvou) await ReloadAsync();
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    [RelayCommand]
    private Task ContinuarOperacao() =>
        RunAsync(() => GitService.ContinuarOperacaoAsync(_repo.Path, Operacao));

    [RelayCommand]
    private async Task AbortarOperacao()
    {
        var ok = await _main.ConfirmAsync("Abortar",
            "Desistir da operação e voltar o repositório ao estado de antes dela?\n\n" +
            "As resoluções de conflito feitas até aqui serão perdidas.");
        if (!ok) return;
        await RunAsync(() => GitService.AbortarOperacaoAsync(_repo.Path, Operacao));
    }

    // ---------------------------------------------------- seleção múltipla
    // Ctrl/Shift+clique marcam vários arquivos; com dois ou mais marcados, os botões do
    // cabeçalho passam a agir só sobre eles. Com um só, continuam valendo para a lista
    // inteira: um item está sempre selecionado (é o que mostra o diff), e "Preparar tudo"
    // não pode virar "preparar este" sem o usuário perceber.

    private List<FileItemViewModel> _marcadosStaged = new();
    private List<FileItemViewModel> _marcadosUnstaged = new();

    /// <summary>Chamado pela view a cada mudança de seleção de uma das listas.</summary>
    public void DefinirSelecao(bool staged, IEnumerable<FileItemViewModel> itens)
    {
        if (staged) _marcadosStaged = itens.ToList();
        else _marcadosUnstaged = itens.ToList();
        OnPropertyChanged(nameof(PrepararRotulo));
        OnPropertyChanged(nameof(RemoverRotulo));
        OnPropertyChanged(nameof(DescartarRotulo));
        OnPropertyChanged(nameof(IgnorarRotulo));
    }

    /// <summary>Alvo dos botões do cabeçalho: os marcados (se 2+) ou a lista toda.</summary>
    private static List<FileItemViewModel> Alvo(List<FileItemViewModel> marcados, IEnumerable<FileItemViewModel> todos) =>
        (marcados.Count >= 2 ? marcados : todos).ToList();

    public string PrepararRotulo => _marcadosUnstaged.Count >= 2 ? $"Preparar selecionados ({_marcadosUnstaged.Count})" : "Preparar tudo";
    public string RemoverRotulo => _marcadosStaged.Count >= 2 ? $"Remover selecionados ({_marcadosStaged.Count})" : "Remover tudo";
    public string DescartarRotulo => _marcadosUnstaged.Count >= 2 ? $"Descartar selecionados ({_marcadosUnstaged.Count})" : "Descartar tudo";

    // ------------------------------------------------------------- ignorar
    // Só nesta máquina: skip-worktree no arquivo rastreado, .git/info/exclude no novo.
    // Nada disso entra em commit, então não precisa de confirmação — e tem volta.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemIgnorados), nameof(IgnoradosRotulo))]
    private int _totalIgnorados;

    public bool TemIgnorados => TotalIgnorados > 0;
    public string IgnoradosRotulo => TotalIgnorados > 0 ? $"Ver ignorados ({TotalIgnorados})…" : "Ver ignorados…";
    public string IgnorarRotulo => _marcadosUnstaged.Count >= 2 ? $"Ignorar selecionados ({_marcadosUnstaged.Count})" : "Ignorar tudo";

    /// <summary>Alvo do menu de contexto: os marcados (se 2+) ou o arquivo aberto.</summary>
    private List<FileItemViewModel> AlvoDoMenu() =>
        _marcadosUnstaged.Count >= 2 ? _marcadosUnstaged.ToList()
        : SelectedUnstaged is { } s ? new List<FileItemViewModel> { s }
        : new List<FileItemViewModel>();

    [RelayCommand]
    private Task IgnorarTodos() => IgnorarAsync(Alvo(_marcadosUnstaged, Unstaged));

    [RelayCommand]
    private Task IgnorarArquivo() => IgnorarAsync(AlvoDoMenu());

    private Task IgnorarAsync(List<FileItemViewModel> itens)
    {
        // conflito não se esconde: fica na lista até ser resolvido
        var alvo = itens.Where(f => !f.IsConflito).ToList();
        if (alvo.Count == 0)
        {
            _main.Notify("Nenhum arquivo para ignorar.");
            return Task.CompletedTask;
        }

        return RunAsync(async () =>
        {
            await Ignorados.IgnorarAsync(
                _repo.Path,
                alvo.Where(f => f.Change.Kind != ChangeKind.Untracked).Select(f => f.Path),
                alvo.Where(f => f.Change.Kind == ChangeKind.Untracked).Select(f => f.Path));
            _main.Notify($"{alvo.Count} arquivo(s) ignorado(s) só nesta máquina. Para voltar: Ignorar ▾ → Ver ignorados.");
        });
    }

    [RelayCommand]
    private Task AdicionarAoGitignore()
    {
        var novos = AlvoDoMenu().Where(f => f.Change.Kind == ChangeKind.Untracked).Select(f => f.Path).ToList();
        if (novos.Count == 0)
        {
            _main.Notify("O .gitignore só vale para arquivo novo. Num arquivo que já está no repositório, " +
                         "use \"Ignorar alterações (só nesta máquina)\".", true);
            return Task.CompletedTask;
        }

        return RunAsync(() =>
        {
            Ignorados.AdicionarAoGitignore(_repo.Path, novos);
            _main.Notify($"{novos.Count} arquivo(s) no .gitignore. Faça o commit dele para valer para todos.");
            return Task.CompletedTask;
        });
    }

    [RelayCommand]
    private async Task VerIgnorados()
    {
        await _main.MostrarIgnoradosAsync(_repo);
        await ReloadAsync();
    }

    // ------------------------------------------------------------ reverter

    private string? _upstream;
    private int _ahead;

    public bool TemRemoto => _upstream is not null;
    public string VoltarAoRemotoRotulo => _upstream is null ? "Voltar ao remoto" : $"Voltar ao remoto ({_upstream})";

    private void GuardarRemoto(RepoStatus status)
    {
        _upstream = string.IsNullOrEmpty(status.Upstream) ? null : status.Upstream;
        _ahead = status.Ahead;
        OnPropertyChanged(nameof(TemRemoto));
        OnPropertyChanged(nameof(VoltarAoRemotoRotulo));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StageAll() =>
        // conflito fica de fora: "git add" nele marcaria como resolvido, com os marcadores dentro
        RunAsync(() => GitService.StageAsync(_repo.Path,
            Alvo(_marcadosUnstaged, Unstaged).Where(f => f.PodePreparar).Select(f => f.Path)));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task UnstageAll() =>
        RunAsync(() => GitService.UnstageAsync(_repo.Path, Alvo(_marcadosStaged, Staged).Select(f => f.Path)));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DiscardAll()
    {
        // conflito não se descarta daqui: resolve-se no arquivo
        var alvo = Alvo(_marcadosUnstaged, Unstaged).Where(f => f.CanDiscard).ToList();
        if (alvo.Count == 0) return;

        var novos = alvo.Count(f => f.Change.Kind == ChangeKind.Untracked);
        var ok = await _main.ConfirmAsync(
            "Descartar alterações",
            $"Descartar as alterações locais de {alvo.Count} arquivo(s)?\n\n" +
            ListaCurta(alvo) +
            (novos > 0 ? $"\n\n{novos} arquivo(s) novo(s) serão apagados do disco." : "") +
            "\n\nO que já está preparado para commit fica. Esta ação não pode ser desfeita.");
        if (!ok) return;

        await RunAsync(() => GitService.DiscardAsync(
            _repo.Path,
            alvo.Where(f => f.Change.Kind != ChangeKind.Untracked).Select(f => f.Path),
            alvo.Where(f => f.Change.Kind == ChangeKind.Untracked).Select(f => f.Path)));
    }

    private static string ListaCurta(List<FileItemViewModel> itens)
    {
        const int max = 8;
        var linhas = itens.Take(max).Select(f => "  " + f.Path).ToList();
        if (itens.Count > max) linhas.Add($"  … e mais {itens.Count - max}");
        return string.Join("\n", linhas);
    }

    [RelayCommand]
    private async Task VoltarAoUltimoCommit()
    {
        if (Staged.Count == 0 && Unstaged.Count == 0)
        {
            _main.Notify("Nada a reverter: não há alterações pendentes.");
            return;
        }
        var ok = await _main.ConfirmAsync(
            "Voltar ao último commit",
            "Deixar o repositório exatamente como está no último commit da branch?\n\n" +
            "Tudo o que estiver preparado ou alterado será descartado, e os arquivos novos " +
            "(não rastreados) serão apagados. Arquivos ignorados pelo .gitignore ficam.\n\n" +
            "Esta ação não pode ser desfeita.");
        if (!ok) return;

        await RunAsync(() => GitService.ReverterTudoAsync(_repo.Path, "HEAD"));
    }

    [RelayCommand]
    private async Task VoltarAoRemoto()
    {
        if (_upstream is null)
        {
            _main.Notify("Esta branch não tem remoto configurado.", true);
            return;
        }
        var perdidos = _ahead > 0
            ? $"Os {_ahead} commit(s) locais ainda não enviados também serão descartados.\n\n"
            : "";
        var ok = await _main.ConfirmAsync(
            "Voltar ao remoto",
            $"Deixar a branch igual a {_upstream}, como estava na última busca?\n\n" +
            "Tudo o que estiver preparado ou alterado será descartado, e os arquivos novos " +
            "(não rastreados) serão apagados. Arquivos ignorados pelo .gitignore ficam.\n\n" +
            perdidos + "Esta ação não pode ser desfeita.");
        if (!ok) return;

        await RunAsync(() => GitService.ReverterTudoAsync(_repo.Path, "@{upstream}"));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Stage(FileItemViewModel item) => RunAsync(() => GitService.StageAsync(_repo.Path, new[] { item.Path }));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Unstage(FileItemViewModel item) => RunAsync(() => GitService.UnstageAsync(_repo.Path, new[] { item.Path }));

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Discard(FileItemViewModel item)
    {
        var ok = await _main.ConfirmAsync(
            "Descartar alterações",
            $"Descartar as alterações de \"{item.Path}\"?\n\nEsta ação não pode ser desfeita.");
        if (!ok) return;

        var untracked = item.Change.Kind == ChangeKind.Untracked;
        await RunAsync(() => GitService.DiscardAsync(
            _repo.Path,
            untracked ? Array.Empty<string>() : new[] { item.Path },
            untracked ? new[] { item.Path } : Array.Empty<string>()));
    }

    [RelayCommand]
    private async Task Commit()
    {
        if (!CanCommit) return;
        Busy = true;
        try
        {
            await GitService.CommitAsync(_repo.Path, CommitMessage, Amend);
            CommitMessage = "";
            Amend = false;
            _main.Notify("Commit criado.");
            await ReloadAsync();
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
        finally
        {
            Busy = false;
        }
    }
}
