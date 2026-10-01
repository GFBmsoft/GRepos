using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Interações que dependem da janela (diálogos e seletor de pasta).</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> PickFolderAsync(string title);
    Task<string?> PromptAsync(string title, string label, string initial = "");

    /// <summary>Nome e cor do grupo; null quando o usuário cancela.</summary>
    Task<(string Nome, string Cor)?> ShowGroupAsync(string titulo, string nome, string cor);
    Task ShowAddRepoAsync(MainViewModel main);
    Task ShowRepoConfigAsync(MainViewModel main, Repo repo);
    Task ShowSettingsAsync(MainViewModel main);
    Task ShowBranchesAsync(MainViewModel main, Repo repo);

    /// <summary>Janela da esteira: execuções do GitHub Actions e seus passos.</summary>
    Task ShowEsteiraAsync(string slug, string branch, string usuario, string repoNome, int visiveis);

    /// <summary>Changelog do aplicativo, lido das releases publicadas.</summary>
    Task ShowNovidadesAsync();
    Task ShowStashAsync(MainViewModel main, Repo repo);
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer = new();
    private Workspace _ws = new();
    private RepoWatcher? _watcher;
    private bool _reloading;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
        _timer.Tick += async (_, _) => await RefreshAllSilenciosoAsync();
    }

    // -------------------------------------------------------------- estado

    [ObservableProperty] private ObservableCollection<SidebarNode> _tree = new();
    [ObservableProperty] private RepoNode? _selectedNode;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _scanning;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private bool _busy;

    [ObservableProperty] private ChangesViewModel? _changes;
    [ObservableProperty] private HistoryViewModel? _history;
    [ObservableProperty] private PairViewModel? _pair;
    [ObservableProperty] private int _selectedTab;

    private readonly Dictionary<string, RepoNode> _nodes = new();
    private readonly Dictionary<string, RepoStatus> _statusConhecido = new();

    public Settings Settings => _ws.Settings;
    public IReadOnlyList<Group> Groups => _ws.Groups;
    public IReadOnlyList<Repo> Repos => _ws.Repos;

    public Repo? CurrentRepo => SelectedNode?.Repo;
    public RepoStatus? CurrentStatus => SelectedNode?.Status;

    public bool HasSelection => SelectedNode is not null;
    public bool NoSelection => SelectedNode is null;

    // ---------------------------------------------------------------- painel

    /// <summary>
    /// Painel de um grupo (ou de todos). Enquanto ele existe, as abas do repositório
    /// saem da tela: clicar num grupo estava deixando à mostra as alterações de um
    /// repositório que não era mais o selecionado.
    /// </summary>
    [ObservableProperty] private PainelViewModel? _painel;

    public bool PainelAtivo => Painel is not null;

    /// <summary>Convite de "nada selecionado": some quando o painel ocupa a tela.</summary>
    public bool SemNadaSelecionado => SelectedNode is null && Painel is null;

    partial void OnPainelChanged(PainelViewModel? value)
    {
        OnPropertyChanged(nameof(PainelAtivo));
        OnPropertyChanged(nameof(SemNadaSelecionado));
    }

    // cartão expandido consulta a esteira de tempos em tempos; painel que saiu de cena
    // não pode continuar gastando a cota da API do GitHub
    partial void OnPainelChanged(PainelViewModel? oldValue, PainelViewModel? newValue) =>
        oldValue?.PararEsteiras();

    /// <summary>Diálogos do app, para quem confirma ações fora da janela principal.</summary>
    public IDialogService Dialogos => _dialogs;

    /// <summary>
    /// Monta o painel de um grupo — ou do workspace inteiro, com grupoId nulo.
    /// Some com a seleção de repositório: são duas visões do mesmo espaço.
    /// </summary>
    public void MostrarPainel(string? grupoId)
    {
        SelectedNode = null;

        var repos = grupoId is null
            ? _ws.Repos.ToList()
            : _ws.Repos.Where(r => (r.GroupId ?? "") == grupoId).ToList();

        var grupo = grupoId is null ? null : _ws.Groups.FirstOrDefault(g => g.Id == grupoId);
        var titulo = grupo?.Name ?? (grupoId is null ? "Todos os repositórios" : "Sem grupo");

        var cartoes = repos
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new CartaoRepoViewModel(
                r,
                _statusConhecido.GetValueOrDefault(r.Id),
                CorDoGrupo(r.GroupId),
                this));

        var painel = new PainelViewModel(titulo, "", cartoes, this);
        painel.Subtitulo = painel.Resumo;

        // no painel geral, cada grupo é uma seção; no de um grupo, uma seção só e sem
        // título, que seria repetir o cabeçalho logo acima
        var geral = grupoId is null;
        painel.Secoes = new ObservableCollection<SecaoPainelViewModel>(
            painel.Cartoes
                .GroupBy(c => c.Repo.GroupId ?? "")
                .OrderBy(g => NomeDoGrupo(g.Key), StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new SecaoPainelViewModel
                {
                    GrupoId = g.Key,
                    Titulo = NomeDoGrupo(g.Key),
                    Cor = CorDoGrupo(g.Key),
                    MostraTitulo = geral,
                    Cartoes = new ObservableCollection<CartaoRepoViewModel>(g),
                    // recolhido na árvore, recolhido no painel: é o mesmo grupo
                    Recolhido = geral && _ws.Groups.Any(x => x.Id == g.Key && x.Collapsed),
                    AoAlternar = AlternarGrupoDoPainel,
                }));

        Painel = painel;

        // o cartão de perfil é da conta inteira, não de um grupo
        if (grupoId is null && _ws.Settings.GithubUser.Length > 0)
            MostrarPerfil(_perfilEscolhido ?? _ws.Settings.GithubUser);

        _ = CarregarPainelAsync();
    }

    /// <summary>Conta que o cartão de perfil está mostrando; nula é a principal.</summary>
    private string? _perfilEscolhido;

    /// <summary>
    /// Mostra no cartão de perfil a conta pedida. Com várias contas, as outras aparecem
    /// no próprio cartão para trocar — o painel lembra a escolha enquanto o app está aberto.
    /// </summary>
    public void MostrarPerfil(string login)
    {
        if (Painel is null) return;

        var contas = Contas;
        if (!contas.Contains(login, StringComparer.OrdinalIgnoreCase)) login = _ws.Settings.GithubUser;
        _perfilEscolhido = login;

        _ws.Settings.Linhas ??= new();
        var chave = login.ToLowerInvariant();

        Painel.Perfil = new PerfilViewModel(login, _ws.Repos.ToList(), _ws.Settings.Linhas.GetValueOrDefault(chave))
        {
            OutrasContas = contas.Where(c => !string.Equals(c, login, StringComparison.OrdinalIgnoreCase)).ToList(),
            Trocar = MostrarPerfil,
            Guardar = contagem =>
            {
                _ws.Settings.Linhas ??= new();
                _ws.Settings.Linhas[chave] = contagem;
                Persist();
            },
        };
        _ = Painel.Perfil.CarregarAsync();
    }

    private async Task CarregarPainelAsync()
    {
        var painel = Painel;
        if (painel is null) return;

        await painel.CarregarEsteirasAsync();
        if (ReferenceEquals(painel, Painel)) painel.Subtitulo = painel.Resumo;
    }

    /// <summary>Repassa ao painel o status recém-varrido, sem refazer os cartões.</summary>
    public void AtualizarCartoesDoPainel()
    {
        if (Painel is null) return;

        foreach (var cartao in Painel.Cartoes)
            if (_statusConhecido.TryGetValue(cartao.Repo.Id, out var status))
                cartao.Status = status;

        Painel.Subtitulo = Painel.Resumo;
        Painel.AtualizarPendencias();
    }

    /// <summary>Changelog do próprio app, montado a partir das releases publicadas.</summary>
    [RelayCommand]
    private Task AbrirNovidades() => _dialogs.ShowNovidadesAsync();

    /// <summary>Abre a janela da esteira de um repositório qualquer, vindo do painel.</summary>
    public Task AbrirEsteiraDeAsync(string slug, string branch, string usuario, string nome) =>
        _dialogs.ShowEsteiraAsync(slug, branch, usuario, nome, _ws.Settings.EsteirasVisiveis);

    private string NomeDoGrupo(string? grupoId) =>
        _ws.Groups.FirstOrDefault(g => g.Id == (grupoId ?? ""))?.Name ?? "Sem grupo";

    private string CorDoGrupo(string? grupoId) =>
        _ws.Groups.FirstOrDefault(g => g.Id == (grupoId ?? ""))?.Color ?? "#5D6675";

    /// <summary>
    /// Leva o foco a um repositório a partir do painel. O grupo dele pode estar recolhido
    /// — inclusive porque clicar no grupo é o que abre o painel e recolhe ao mesmo tempo —
    /// e aí o nó nem existe na árvore. Nesse caso o grupo é aberto antes.
    /// </summary>
    public void SelecionarRepositorio(string id)
    {
        var no = Tree.OfType<RepoNode>().FirstOrDefault(n => n.Id == id);
        if (no is null)
        {
            var repo = _ws.Repos.FirstOrDefault(r => r.Id == id);
            if (repo is null) return;

            var grupo = _ws.Groups.FirstOrDefault(g => g.Id == (repo.GroupId ?? ""));
            if (grupo is { Collapsed: true })
            {
                grupo.Collapsed = false;
                Persist();
            }

            // o filtro também esconde nós; limpá-lo garante que o repositório apareça
            if (Filter.Length > 0) Filter = "";
            RebuildTree();

            no = Tree.OfType<RepoNode>().FirstOrDefault(n => n.Id == id);
            if (no is null) return;
        }

        SelectedNode = no;
    }
    public bool HasPair => PairRepoOf(CurrentRepo) is not null;
    public bool ShowError => CurrentStatus?.Error is { Length: > 0 };
    public string ErrorText => CurrentStatus?.Error ?? "";

    public string RepoTitle => CurrentRepo?.Name ?? "";
    public string RepoPath => CurrentRepo?.Path ?? "";
    public string BranchCaption => Rotulos.Branch(CurrentStatus?.Branch ?? "");

    /// <summary>O nome inteiro da branch, que não cabe no botão, vive aqui.</summary>
    public string BranchTooltip
    {
        get
        {
            var s = CurrentStatus;
            if (s is null || s.Branch.Length == 0) return "Branches: trocar ou criar";

            var linhas = "Branch atual: " + s.Branch;
            if (!string.IsNullOrEmpty(s.Upstream)) linhas += "\nAcompanha: " + s.Upstream;
            if (s.Ahead > 0 || s.Behind > 0) linhas += $"\n↑{s.Ahead} à frente · ↓{s.Behind} atrás";
            return linhas + "\nClique para trocar ou criar branch";
        }
    }
    public string PairTabHeader => CurrentRepo?.PairKey is { Length: > 0 } k ? $"Par: {k}" : "Par";

    /// <summary>README do repositório selecionado, renderizado na aba "Leia-me".</summary>
    [ObservableProperty] private string _leiameTexto = "";

    public bool TemLeiame => LeiameTexto.Length > 0;

    partial void OnLeiameTextoChanged(string value) => OnPropertyChanged(nameof(TemLeiame));

    // conta arquivos, não situações: um arquivo preparado e alterado de novo é um só,
    // e conflito também precisa entrar na conta
    public string ChangesTabHeader => CurrentStatus is { PendingFiles: > 0 } s
        ? $"Alterações ({s.PendingFiles})"
        : "Alterações";

    // ------------------------------------------------- remoto, pasta e esteira

    [ObservableProperty] private string _remoteWebUrl = "";
    [ObservableProperty] private string _ciSituacao = "";
    [ObservableProperty] private string _ciDetalhe = "";
    [ObservableProperty] private string _ciUrl = "";

    /// <summary>"owner/repo" e conta do remoto: é o que a janela da esteira consulta.</summary>
    private string _ciSlug = "";
    private string _ciUsuario = "";

    public bool TemRemoto => RemoteWebUrl.Length > 0;
    public bool TemCi => CiSituacao.Length > 0 && CiSituacao != "nenhum";

    /// <summary>Verde passou, vermelho quebrou, amarelo rodando — a cor é o recado.</summary>
    public string CiCor => CiSituacao switch
    {
        "sucesso" => "Green",
        "falha" => "Red",
        "rodando" => "Yellow",
        "cancelado" => "TextDim",
        _ => "TextDim",
    };

    public string CiRotulo => CiSituacao switch
    {
        "sucesso" => "Esteira ok",
        "falha" => "Esteira quebrou",
        "rodando" => "Esteira rodando",
        "cancelado" => "Esteira cancelada",
        _ => "Esteira",
    };

    public string CiTooltip => CiDetalhe.Length > 0
        ? $"{CiRotulo} — {CiDetalhe}\nClique para ver as execuções e o passo a passo"
        : "Status do GitHub Actions";

    /// <summary>
    /// Destaque quando a branch atual não é a principal: é o lembrete de que o
    /// trabalho está numa feature, não na main.
    /// </summary>
    public bool ForaDaPrincipal =>
        CurrentStatus?.Branch is { Length: > 0 } b &&
        !b.Equals("main", StringComparison.OrdinalIgnoreCase) &&
        !b.Equals("master", StringComparison.OrdinalIgnoreCase);

    partial void OnRemoteWebUrlChanged(string value) => OnPropertyChanged(nameof(TemRemoto));

    partial void OnCiSituacaoChanged(string value)
    {
        foreach (var p in new[] { nameof(TemCi), nameof(CiCor), nameof(CiRotulo), nameof(CiTooltip) })
            OnPropertyChanged(p);
    }

    partial void OnCiDetalheChanged(string value) => OnPropertyChanged(nameof(CiTooltip));

    /// <summary>
    /// Garante o status antes de montar a barra: repositório ainda não varrido deixava
    /// o botão de branch mostrando "—".
    /// </summary>
    private async Task PrepararRepoAsync(Repo repo)
    {
        if (_nodes.TryGetValue(repo.Id, out var node) && node.Status is null)
            await RefreshRepoAsync(repo.Id);

        await AtualizarRemotoAsync(repo);
    }

    /// <summary>Descobre o remoto e o status da esteira do repositório selecionado.</summary>
    private async Task AtualizarRemotoAsync(Repo repo)
    {
        RemoteWebUrl = "";
        CiSituacao = "";
        CiDetalhe = "";
        CiUrl = "";
        _ciSlug = "";
        _ciUsuario = "";

        try
        {
            var remoto = await GitService.RemoteUrlAsync(repo.Path);
            if (SelectedNode?.Repo.Id != repo.Id) return; // trocou de repositório no meio
            RemoteWebUrl = GitService.WebUrl(remoto);

            var slug = GitHubService.Slug(remoto);
            if (slug is null) return;

            // a conta escolhida para o repositório vem na frente; depois a da URL; sem
            // nenhuma, a principal das preferências
            var usuario = GitHubService.ContaDoRepositorio(repo.Conta, remoto);
            if (usuario.Length == 0) usuario = _ws.Settings.GithubUser;
            _ciSlug = slug;
            _ciUsuario = usuario;

            var run = await GitHubService.UltimaExecucaoAsync(
                slug, CurrentStatus?.Branch ?? "", usuario);
            if (SelectedNode?.Repo.Id != repo.Id) return;

            CiSituacao = run.Situacao;
            CiDetalhe = run.Detalhe;
            CiUrl = run.Url;
        }
        catch (Exception)
        {
            // remoto/esteira são informativos: falha aqui não atrapalha o resto
        }
    }

    [RelayCommand]
    private void AbrirRemoto()
    {
        try
        {
            ShellService.AbrirUrl(RemoteWebUrl);
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    [RelayCommand]
    private void AbrirPasta()
    {
        try
        {
            if (CurrentRepo is not null) ShellService.AbrirPasta(CurrentRepo.Path);
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    [RelayCommand]
    private void AbrirTerminal()
    {
        if (CurrentRepo is not null) AbrirTerminalEm(CurrentRepo.Path);
    }

    // ------------------------------------------------------- terminal embutido

    /// <summary>Uma sessão por repositório já aberto no terminal; trocar não as encerra.</summary>
    public ObservableCollection<TerminalSessao> Terminais { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalVisivel))]
    private bool _terminalAberto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalVisivel))]
    private TerminalSessao? _terminalAtual;

    /// <summary>O painel só aparece com um repositório selecionado.</summary>
    public bool TerminalVisivel => TerminalAberto && TerminalAtual is not null;

    [RelayCommand]
    private void AlternarTerminal()
    {
        TerminalAberto = !TerminalAberto;
        SincronizarTerminal();
    }

    /// <summary>Mostra a sessão do repositório atual, criando-a na primeira vez.</summary>
    private void SincronizarTerminal()
    {
        TerminalSessao? atual = null;
        if (TerminalAberto && CurrentRepo is { } repo)
        {
            atual = Terminais.FirstOrDefault(t => t.RepoId == repo.Id);
            if (atual is null)
            {
                var nova = new TerminalSessao(repo.Id, repo.Name, repo.Path, () => _ws.Settings.GitBashPath);
                try
                {
                    nova.Iniciar();
                    Terminais.Add(nova);
                    atual = nova;
                }
                catch (Exception e)
                {
                    nova.Dispose();
                    TerminalAberto = false;
                    Notify(e.Message, true);
                }
            }
        }

        foreach (var t in Terminais) t.Ativa = t == atual;
        TerminalAtual = atual;
    }

    [RelayCommand]
    private void EncerrarTerminal()
    {
        if (TerminalAtual is not { } sessao) return;
        Terminais.Remove(sessao);
        sessao.Dispose();
        TerminalAberto = false;
        TerminalAtual = null;
    }

    [RelayCommand]
    private void ReiniciarTerminal()
    {
        try
        {
            TerminalAtual?.Iniciar();
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    /// <summary>Ao fechar o app: sem isto os bash ficariam órfãos.</summary>
    public void EncerrarTerminais()
    {
        foreach (var t in Terminais) t.Dispose();
        Terminais.Clear();
    }

    /// <summary>Git Bash na pasta dada; também usado pelo cartão do painel.</summary>
    public void AbrirTerminalEm(string pasta)
    {
        try
        {
            GitBash.Abrir(pasta, _ws.Settings.GitBashPath);
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    /// <summary>
    /// Abre a esteira dentro do app: cartões das execuções e o passo a passo de cada
    /// job. Sem slug do GitHub não há API a consultar, e aí vale a página no navegador.
    /// </summary>
    [RelayCommand]
    private async Task AbrirEsteiraAsync()
    {
        try
        {
            if (_ciSlug.Length > 0)
            {
                await _dialogs.ShowEsteiraAsync(
                    _ciSlug, CurrentStatus?.Branch ?? "", _ciUsuario, CurrentRepo?.Name ?? "",
                    _ws.Settings.EsteirasVisiveis);
                return;
            }

            ShellService.AbrirUrl(CiUrl.Length > 0 ? CiUrl : RemoteWebUrl + "/actions");
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    // selos da barra: vazio esconde o contador
    public string BehindBadge => CurrentStatus is { Behind: > 0 } s ? s.Behind.ToString() : "";
    public string AheadBadge => CurrentStatus is { Ahead: > 0 } s ? s.Ahead.ToString() : "";
    public string StashBadge => CurrentStatus is { Stashes: > 0 } s ? s.Stashes.ToString() : "";
    public string RepoCountText => $"{_ws.Repos.Count} repositório(s)";

    /// <summary>
    /// Data do executável em uso. Serve para saber, olhando a tela, se o que está
    /// rodando é mesmo a versão recém-publicada.
    /// </summary>
    public static string VersaoTexto
    {
        get
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) return "";

                var quando = System.IO.File.GetLastWriteTime(exe);

                // A data é a do arquivo — serve para conferir que o build recém-publicado
                // é o que está aberto. Num .exe baixado da Release ela é a do download,
                // por isso a versão carimbada pelo workflow vem na frente quando existe.
                var versao = VersaoEmUso;

                return versao.Length > 0
                    ? $"versão {versao} · build {quando:dd/MM HH:mm}"
                    : $"build {quando:dd/MM HH:mm}";
            }
            catch (Exception)
            {
                return "";
            }
        }
    }

    public string StatusLine
    {
        get
        {
            var s = CurrentStatus;
            if (s is null) return "";
            var up = string.IsNullOrEmpty(s.Upstream) ? "" : $"  ↔ {s.Upstream}";
            return $"{(string.IsNullOrEmpty(s.Branch) ? "sem branch" : s.Branch)}{up}   " +
                   $"↑{s.Ahead} ↓{s.Behind} · {s.Staged} preparado(s), {s.Unstaged} local(is), {s.Untracked} novo(s)";
        }
    }

    public bool SplitDiff
    {
        get => _ws.Settings.SplitDiff;
        set
        {
            if (_ws.Settings.SplitDiff == value) return;
            _ws.Settings.SplitDiff = value;
            if (Changes is not null) Changes.Diff.Split = value;
            if (History is not null) History.Diff.Split = value;
            OnPropertyChanged();
            Persist();
        }
    }

    /// <summary>Quebra de linha no diff; a preferência vale para Alterações e Histórico.</summary>
    public bool WrapDiff
    {
        get => _ws.Settings.WrapDiff;
        set
        {
            if (_ws.Settings.WrapDiff == value) return;
            _ws.Settings.WrapDiff = value;
            if (Changes is not null) Changes.Diff.Wrap = value;
            if (History is not null) History.Diff.Wrap = value;
            OnPropertyChanged();
            Persist();
        }
    }

    // ---------------------------------------------------------- inicialização

    public async Task InitAsync()
    {
        _ws = WorkspaceStore.Load();
        GitService.CredentialUser = _ws.Settings.GithubUser;
        MigrarContas();
        AplicarContasDosRepositorios();

        // sobra da atualização anterior: o .exe antigo já não está em uso agora
        Atualizador.LimparAntigo();
        _ = VerificarAtualizacaoAsync();

        RebuildTree();
        ApplyTimer();
        await RefreshAllAsync();

        // abre no painel: a visão de todos os repositórios diz mais, logo de cara, do
        // que um repositório escolhido por ordem alfabética
        if (_ws.Repos.Count > 0) MostrarPainel(null);
        else SelectedNode = Tree.OfType<RepoNode>().FirstOrDefault();
    }

    private void ApplyTimer()
    {
        _timer.Stop();
        if (_ws.Settings.AutoRefreshSeconds > 0)
        {
            _timer.Interval = TimeSpan.FromSeconds(_ws.Settings.AutoRefreshSeconds);
            _timer.Start();
        }
    }

    public void Persist()
    {
        try
        {
            WorkspaceStore.Save(_ws);
        }
        catch (Exception e)
        {
            Notify($"Não foi possível salvar o workspace: {e.Message}", true);
        }
    }

    public string StatusAccent => StatusIsError ? "Red" : "Accent";

    partial void OnStatusIsErrorChanged(bool value) => OnPropertyChanged(nameof(StatusAccent));

    public void Notify(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
        HasStatusMessage = true;
    }

    public Task<bool> ConfirmAsync(string title, string message) => _dialogs.ConfirmAsync(title, message);

    [RelayCommand]
    private void DismissStatus() => HasStatusMessage = false;

    // ------------------------------------------------------------- sidebar

    public void RebuildTree()
    {
        // os nós são recriados, mas o status já conhecido vai junto: sem isso, recolher e
        // abrir um grupo apagava a pílula da branch e os contadores até a próxima varredura
        foreach (var (id, no) in _nodes)
            if (no.Status is not null) _statusConhecido[id] = no.Status;

        _nodes.Clear();
        var nodes = new ObservableCollection<SidebarNode>();

        var q = Filter.Trim();
        var visible = string.IsNullOrEmpty(q)
            ? _ws.Repos
            : _ws.Repos.Where(r =>
                r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                r.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        // painel geral no topo, separado dos grupos por uma linha
        if (_ws.Repos.Count > 0)
        {
            nodes.Add(new PainelNode
            {
                Titulo = "Painel",
                Subtitulo = $"{_ws.Repos.Count} repositório(s)",
            });
            nodes.Add(new SeparadorNode());
        }

        var groups = _ws.Groups.Select(g => (g.Id, g.Name, g.Color, g.Collapsed)).ToList();
        groups.Add(("", "Sem grupo", "#5D6675", false));

        foreach (var (id, name, color, collapsed) in groups)
        {
            var list = visible.Where(r => (r.GroupId ?? "") == id)
                              .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                              .ToList();
            if (list.Count == 0 && id == "") continue;

            var showCollapsed = collapsed && string.IsNullOrEmpty(q);
            nodes.Add(new GroupNode { Id = id, Name = name, Color = color, Collapsed = showCollapsed, Count = list.Count, Minimalista = _ws.Settings.ArvoreMinimalista });
            if (showCollapsed) continue;

            // repositórios pareados aparecem sob um único título — é o que evita a
            // duplicação de abas quando o mesmo módulo existe em dois bancos
            var pairs = list.Where(r => !string.IsNullOrEmpty(r.PairKey))
                            .GroupBy(r => r.PairKey!)
                            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

            foreach (var pair in pairs)
            {
                nodes.Add(new PairNode { Key = pair.Key });
                foreach (var r in pair.OrderBy(RoleOrder))
                    nodes.Add(MakeNode(r, true, color));
            }

            foreach (var r in list.Where(r => string.IsNullOrEmpty(r.PairKey)))
                nodes.Add(MakeNode(r, false, color));
        }

        Tree = nodes;
        OnPropertyChanged(nameof(RepoCountText));
    }

    private static int RoleOrder(Repo r) => r.Role switch { "origem" => 0, "destino" => 1, _ => 2 };

    private RepoNode MakeNode(Repo r, bool paired, string groupColor)
    {
        var node = new RepoNode
        {
            Repo = r,
            IsPaired = paired,
            GroupColor = groupColor,
            Minimalista = _ws.Settings.ArvoreMinimalista,
            Status = _statusConhecido.GetValueOrDefault(r.Id),
        };
        _nodes[r.Id] = node;
        if (SelectedNode?.Id == r.Id) SelectedNode = node;
        return node;
    }

    partial void OnFilterChanged(string value) => RebuildTree();

    partial void OnSelectedNodeChanged(RepoNode? value)
    {
        foreach (var n in new[] { nameof(CurrentRepo), nameof(CurrentStatus), nameof(HasSelection),
                                  nameof(NoSelection), nameof(HasPair), nameof(RepoTitle), nameof(RepoPath),
                                  nameof(BranchCaption), nameof(BranchTooltip), nameof(StatusLine), nameof(ChangesTabHeader),
                                  nameof(PairTabHeader), nameof(BehindBadge), nameof(AheadBadge),
                                  nameof(StashBadge), nameof(ShowError), nameof(ErrorText),
                                  nameof(ForaDaPrincipal), nameof(SemNadaSelecionado) })
            OnPropertyChanged(n);

        // repositório e painel são visões concorrentes: escolher um fecha o outro
        if (value is not null) Painel = null;

        SincronizarTerminal();

        Changes = null;
        History = null;
        Pair = null;

        _watcher?.Dispose();
        _watcher = null;

        if (value is null) return;

        var repo = value.Repo;

        // acompanha o repositório aberto: salvar um arquivo no editor atualiza a lista
        _watcher = new RepoWatcher(repo.Path, () => Dispatcher.UIThread.Post(() => _ = OnDiskChangedAsync(repo.Id)));
        // leitura de arquivo é barata, mas não na thread da UI a cada troca de repositório.
        // A volta é pelo Dispatcher: FromCurrentSynchronizationContext não existe fora da
        // thread de UI, e quebrava todo teste que só exercita o ViewModel.
        LeiameTexto = "";
        var alvo = repo;
        _ = Task.Run(() =>
        {
            var texto = Leiame.Ler(alvo.Path);
            Dispatcher.UIThread.Post(() =>
            {
                if (SelectedNode?.Repo.Id == alvo.Id) LeiameTexto = texto;
            });
        });

        Changes = new ChangesViewModel(repo, this, SplitDiff);
        History = new HistoryViewModel(repo, this, _ws.Settings.LogLimit, SplitDiff);
        Changes.Diff.Wrap = WrapDiff;
        History.Diff.Wrap = WrapDiff;

        var other = PairRepoOf(repo);
        if (other is not null) Pair = new PairViewModel(repo, other, this, _ws.Settings.LogLimit);

        _ = PrepararRepoAsync(repo);

        // abre na aba que o usuário escolheu nas preferências
        SelectedTab = _ws.Settings.DefaultTab == "historico" ? 1 : 0;
        _ = LoadTabAsync();
    }

    partial void OnSelectedTabChanged(int value) => _ = LoadTabAsync();

    /// <summary>
    /// Recarrega o que está na tela quando o disco muda. Ignora eventos enquanto uma
    /// recarga está em andamento: build gerando arquivos não pode virar fila de gits.
    /// </summary>
    private async Task OnDiskChangedAsync(string repoId)
    {
        if (_reloading || SelectedNode?.Id != repoId) return;
        _reloading = true;
        try
        {
            await RefreshRepoAsync(repoId);
            if (SelectedTab == 0 && Changes is not null) await Changes.ReloadAsync(silent: true);
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
        finally
        {
            _reloading = false;
        }
    }

    /// <summary>Recarrega a aba visível — usado quando a paleta do tema muda.</summary>
    public void ReloadCurrentTab() => _ = LoadTabAsync();

    private async Task LoadTabAsync()
    {
        try
        {
            switch (SelectedTab)
            {
                case 0 when Changes is not null: await Changes.ReloadAsync(); break;
                case 1 when History is not null: await History.LoadAsync(); break;
                case 2 when Pair is not null: await Pair.LoadAsync(); break;
            }
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    public Repo? PairRepoOf(Repo? repo) =>
        repo?.PairKey is { Length: > 0 } key
            ? _ws.Repos.FirstOrDefault(r => r.Id != repo.Id && r.PairKey == key)
            : null;

    public void SelectRepo(string? id)
    {
        if (id is null) return;
        if (_nodes.TryGetValue(id, out var node)) SelectedNode = node;
    }

    [RelayCommand]
    private void ToggleGroup(GroupNode node)
    {
        var g = _ws.Groups.FirstOrDefault(x => x.Id == node.Id);
        if (g is null) return;
        g.Collapsed = !g.Collapsed;
        Persist();
        RebuildTree();

        // o painel geral aberto acompanha a árvore
        if (Painel?.Secoes.FirstOrDefault(s => s.GrupoId == g.Id && s.MostraTitulo) is { } secao)
            secao.Recolhido = g.Collapsed;
    }

    /// <summary>
    /// Seção do painel recolhida ou aberta: vale também para a árvore, como se o clique
    /// tivesse sido no grupo de lá. "Sem grupo" não existe no workspace e fica só no painel.
    /// </summary>
    private void AlternarGrupoDoPainel(SecaoPainelViewModel secao)
    {
        var g = _ws.Groups.FirstOrDefault(x => x.Id == secao.GrupoId);
        if (g is null) return;

        g.Collapsed = secao.Recolhido;
        Persist();
        RebuildTree();
    }

    // -------------------------------------------------------------- status

    /// <summary>
    /// Guarda de reentrada separada do <see cref="Scanning"/>: o segundo é estado de
    /// tela e só vale para a varredura que o usuário pediu.
    /// </summary>
    private bool _varrendo;

    [RelayCommand]
    public Task RefreshAllAsync() => VarrerAsync(automatica: false);

    /// <summary>
    /// Varredura do cronômetro. Não mexe no <see cref="Scanning"/> nem mostra erro: ela
    /// acontece a cada minuto sozinha, e piscar o botão de atualizar ou abrir um aviso
    /// por causa de uma oscilação de rede faz o app parecer estar sendo operado por
    /// outra pessoa.
    /// </summary>
    public Task RefreshAllSilenciosoAsync() => VarrerAsync(automatica: true);

    private async Task VarrerAsync(bool automatica)
    {
        if (_varrendo || _ws.Repos.Count == 0) return;

        _varrendo = true;
        if (!automatica) Scanning = true;
        try
        {
            var repos = _ws.Repos.ToList();
            var tasks = repos.Select(r => GitService.StatusAsync(r.Path)).ToList();
            var results = await Task.WhenAll(tasks);

            for (var i = 0; i < repos.Count; i++)
            {
                // guardado mesmo sem nó: o repositório pode estar num grupo recolhido
                _statusConhecido[repos[i].Id] = results[i];
                if (_nodes.TryGetValue(repos[i].Id, out var node))
                {
                    node.Status = results[i];
                    node.Refreshed();
                }
            }

            RefreshHeaderBindings();
        }
        catch (Exception e)
        {
            if (!automatica) Notify(e.Message, true);
        }
        finally
        {
            _varrendo = false;
            if (!automatica) Scanning = false;
        }
    }

    public async Task RefreshRepoAsync(string id)
    {
        var repo = _ws.Repos.FirstOrDefault(r => r.Id == id);
        if (repo is null) return;
        ApplyStatus(id, await GitService.StatusAsync(repo.Path));
    }

    /// <summary>Aplica um status já obtido, sem chamar o git de novo.</summary>
    public void ApplyStatus(string id, RepoStatus status)
    {
        _statusConhecido[id] = status;
        if (_nodes.TryGetValue(id, out var node))
        {
            node.Status = status;
            node.Refreshed();
        }
        RefreshHeaderBindings();
    }

    private void RefreshHeaderBindings()
    {
        foreach (var n in new[] { nameof(CurrentStatus), nameof(BranchCaption), nameof(BranchTooltip), nameof(StatusLine),
                                  nameof(ChangesTabHeader), nameof(BehindBadge), nameof(AheadBadge),
                                  nameof(StashBadge), nameof(ShowError), nameof(ErrorText) })
            OnPropertyChanged(n);
    }

    // ----------------------------------------------------- comandos do repo

    private async Task RunAsync(Func<string, Task<string>> action, string label)
    {
        var repo = CurrentRepo;
        if (repo is null) return;
        Busy = true;
        Notify($"{label} em andamento…"); // a barra desabilitada precisa dizer por quê
        try
        {
            var outp = await action(repo.Path);
            await RefreshRepoAsync(repo.Id);
            if (SelectedTab == 0 && Changes is not null) await Changes.ReloadAsync();
            var tail = outp.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
            Notify(tail.Length > 0 ? $"{label}: {tail}" : $"{label} concluído.");
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private Task Fetch() => RunAsync(GitService.FetchAsync, "Fetch");

    [RelayCommand]
    private Task Pull() => RunAsync(p => GitService.PullAsync(p, false), "Pull");

    [RelayCommand]
    private Task Push() => RunAsync(p => GitService.PushAsync(p, string.IsNullOrEmpty(CurrentStatus?.Upstream)), "Push");

    [RelayCommand]
    private async Task OpenBranches()
    {
        if (CurrentRepo is null) return;
        await _dialogs.ShowBranchesAsync(this, CurrentRepo);
        await RefreshRepoAsync(CurrentRepo.Id);
        await LoadTabAsync();
    }

    /// <summary>
    /// Pílula da branch na árvore: abre as branches daquele repositório sem precisar
    /// selecioná-lo antes nem passar pelo botão da barra.
    /// </summary>
    [RelayCommand]
    private async Task OpenBranchesFor(RepoNode? node)
    {
        if (node is null) return;
        await _dialogs.ShowBranchesAsync(this, node.Repo);
        await RefreshRepoAsync(node.Repo.Id);
        if (CurrentRepo?.Id == node.Repo.Id) await LoadTabAsync();
    }

    [RelayCommand]
    private async Task OpenStash()
    {
        if (CurrentRepo is null) return;
        await _dialogs.ShowStashAsync(this, CurrentRepo);
        await RefreshRepoAsync(CurrentRepo.Id);
        await LoadTabAsync();
    }

    [RelayCommand]
    private async Task OpenRepoConfig()
    {
        if (CurrentRepo is null) return;
        await _dialogs.ShowRepoConfigAsync(this, CurrentRepo);
    }

    [RelayCommand]
    private Task OpenSettings() => _dialogs.ShowSettingsAsync(this);

    [RelayCommand]
    private Task AddRepo() => _dialogs.ShowAddRepoAsync(this);

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        // já abre numa cor livre: com vários grupos, repetir a mesma cor não ajuda ninguém
        var sugerida = GroupPalette.ProximaLivre(_ws.Groups.Select(g => g.Color));
        var r = await _dialogs.ShowGroupAsync("Novo grupo", "", sugerida);
        if (r is null) return;

        CreateGroup(r.Value.Nome, r.Value.Cor);
        RebuildTree();
    }

    /// <summary>Edita nome e cor de um grupo existente.</summary>
    public async Task EditGroupAsync(string id)
    {
        var g = _ws.Groups.FirstOrDefault(x => x.Id == id);
        if (g is null) return;

        var r = await _dialogs.ShowGroupAsync("Editar grupo", g.Name, g.Color);
        if (r is null) return;

        UpdateGroup(id, r.Value.Nome, r.Value.Cor);
    }

    [RelayCommand]
    private void ToggleSplit() => SplitDiff = !SplitDiff;

    [RelayCommand]
    private void ToggleWrap() => WrapDiff = !WrapDiff;

    // ------------------------------------------------ mutações do workspace

    public string CreateGroup(string name, string? cor = null)
    {
        var g = new Group
        {
            Id = NewId(),
            Name = name,
            Color = GroupPalette.Normalizar(cor) ?? GroupPalette.ProximaLivre(_ws.Groups.Select(x => x.Color)),
        };
        _ws.Groups.Add(g);
        Persist();
        return g.Id;
    }

    public void AddRepository(string path, string name, string? groupId)
    {
        var norm = path.Replace('\\', '/').TrimEnd('/');
        if (_ws.Repos.Any(r => r.Path.Replace('\\', '/').TrimEnd('/')
                .Equals(norm, StringComparison.OrdinalIgnoreCase)))
        {
            Notify("Este repositório já está no workspace.");
            return;
        }

        var repo = new Repo { Id = NewId(), Name = name, Path = path, GroupId = groupId };
        _ws.Repos.Add(repo);
        Persist();
        RebuildTree();
        SelectRepo(repo.Id);
        _ = RefreshRepoAsync(repo.Id);
    }

    public void UpdateRepository(Repo repo, string name, string? groupId, string? pairKey, string? role,
        string? remoteTemplate = null, string? conta = null)
    {
        repo.RemoteTemplate = string.IsNullOrWhiteSpace(remoteTemplate) ? null : remoteTemplate.Trim();
        repo.Conta = string.IsNullOrWhiteSpace(conta) ? null : conta.Trim();
        GitService.DefinirConta(repo.Path, repo.Conta);
        repo.Name = name;
        repo.GroupId = groupId;
        repo.PairKey = string.IsNullOrWhiteSpace(pairKey) ? null : pairKey.Trim();
        repo.Role = repo.PairKey is null ? null : role ?? "origem";
        Persist();
        RebuildTree();
        SelectRepo(repo.Id);
        OnPropertyChanged(nameof(HasPair));
        OnPropertyChanged(nameof(PairTabHeader));
    }

    public void RemoveRepository(Repo repo)
    {
        _ws.Repos.Remove(repo);
        Persist();
        if (SelectedNode?.Id == repo.Id) SelectedNode = null;
        RebuildTree();
    }

    public void UpdateGroup(string id, string name, string color)
    {
        var g = _ws.Groups.FirstOrDefault(x => x.Id == id);
        if (g is null) return;
        g.Name = name;
        g.Color = color;
        Persist();
        RebuildTree();
    }

    public void RemoveGroup(string id)
    {
        _ws.Groups.RemoveAll(g => g.Id == id);
        foreach (var r in _ws.Repos.Where(r => r.GroupId == id)) r.GroupId = null;
        Persist();
        RebuildTree();
    }

    /// <summary>Usuário do GitHub; o token correspondente fica no gerenciador do Windows.</summary>
    public void SetGithubUser(string usuario) =>
        SetContas(Contas.Append(usuario.Trim()), usuario.Trim());

    /// <summary>Contas do GitHub cadastradas, a principal primeiro.</summary>
    public IReadOnlyList<string> Contas
    {
        get
        {
            var principal = _ws.Settings.GithubUser;
            return _ws.Settings.GithubContas
                .OrderByDescending(c => string.Equals(c, principal, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>
    /// Troca a lista de contas e a principal. A principal é a que o git e a API usam
    /// quando o repositório não escolhe outra.
    /// </summary>
    public void SetContas(IEnumerable<string> contas, string principal)
    {
        var lista = contas.Select(c => c.Trim()).Where(c => c.Length > 0)
                          .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        principal = principal.Trim();
        if (principal.Length > 0 && !lista.Contains(principal, StringComparer.OrdinalIgnoreCase))
            lista.Insert(0, principal);
        if (principal.Length == 0 && lista.Count > 0) principal = lista[0];

        _ws.Settings.GithubContas = lista;
        _ws.Settings.GithubUser = principal;

        // repositório preso a uma conta que saiu da lista volta para a automática
        foreach (var r in _ws.Repos.Where(r => r.Conta is { Length: > 0 } c &&
                                               !lista.Contains(c, StringComparer.OrdinalIgnoreCase)))
            r.Conta = null;

        // sem repassar para o GitService o usuário só existia no arquivo: o git ia ao
        // credential manager sem conta, não achava o token e abria a janela de login
        GitService.CredentialUser = principal;
        AplicarContasDosRepositorios();
        GitHubService.EsquecerTokens();
        Persist();
    }

    /// <summary>Quem só tinha a conta única de antes passa a tê-la na lista.</summary>
    private void MigrarContas()
    {
        var s = _ws.Settings;
        if (s.GithubUser.Length > 0 &&
            !s.GithubContas.Contains(s.GithubUser, StringComparer.OrdinalIgnoreCase))
            s.GithubContas.Insert(0, s.GithubUser);
    }

    /// <summary>Repassa ao git a conta escolhida para cada repositório.</summary>
    private void AplicarContasDosRepositorios()
    {
        foreach (var r in _ws.Repos) GitService.DefinirConta(r.Path, r.Conta);
    }

    // ------------------------------------------------------------ atualização

    private Release? _release;

    [ObservableProperty] private string _atualizacaoTag = "";
    [ObservableProperty] private string _atualizacaoAviso = "";
    [ObservableProperty] private bool _atualizando;
    [ObservableProperty] private bool _verificandoAtualizacao;

    public bool TemAtualizacao => AtualizacaoTag.Length > 0;

    /// <summary>Com versão nova o ícone acende; sem ela fica apagado como o resto do rodapé.</summary>
    public string CorDoIconeAtualizacao => TemAtualizacao ? "Accent" : "TextFaint";

    public string DicaDoIconeAtualizacao => TemAtualizacao
        ? $"Versão {AtualizacaoTag} disponível"
        : "Procurar uma versão nova";

    /// <summary>
    /// Busca pedida pelo ícone do rodapé. Diferente da automática, esta sempre responde
    /// alguma coisa: silêncio depois de clicar não diz se procurou ou se deu errado.
    /// </summary>
    [RelayCommand]
    private async Task ProcurarAtualizacaoAsync()
    {
        if (VerificandoAtualizacao) return;

        VerificandoAtualizacao = true;
        try
        {
            if (VersaoEmUso.Length == 0)
            {
                Notify("Este é um build local, sem versão carimbada para comparar com a release.");
                return;
            }

            await VerificarAtualizacaoAsync(forcar: true);
            Notify(TemAtualizacao
                ? $"Versão {AtualizacaoTag} disponível — o aviso está aqui na barra."
                : $"Você já está na versão mais recente ({VersaoEmUso}).");
        }
        finally
        {
            VerificandoAtualizacao = false;
        }
    }

    partial void OnAtualizacaoTagChanged(string value)
    {
        foreach (var p in new[] { nameof(TemAtualizacao), nameof(CorDoIconeAtualizacao),
                                  nameof(DicaDoIconeAtualizacao) })
            OnPropertyChanged(p);

        if (value.Length > 0 && AtualizacaoAviso.Length == 0)
            AtualizacaoAviso = $"Atualização {value} disponível";
    }

    /// <summary>Versão em execução, quando carimbada pelo workflow; vazia em build local.</summary>
    public static string VersaoEmUso => Rotulos.VersaoPublicada(
        typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>
    /// Procura release nova. Build local não é avisado (não tem versão para comparar), e a
    /// API só é consultada uma vez por dia — o resultado anterior fica no workspace.
    /// </summary>
    public async Task VerificarAtualizacaoAsync(bool forcar = false)
    {
        // pedida pelo usuário, a busca acontece mesmo com o aviso automático desligado
        if (!forcar && !_ws.Settings.AvisarAtualizacao) return;

        var atual = VersaoEmUso;
        if (atual.Length == 0) return;

        try
        {
            var recente =
                DateTime.TryParse(_ws.Settings.UltimaChecagem, null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var quando) &&
                DateTime.UtcNow - quando < Atualizador.IntervaloDeChecagem;

            var tag = _ws.Settings.UltimaTagVista;

            if (forcar || !recente)
            {
                _release = await GitHubService.UltimaReleaseAsync(Atualizador.Slug, _ws.Settings.GithubUser);
                tag = _release?.Tag ?? "";

                _ws.Settings.UltimaChecagem = DateTime.UtcNow.ToString("o");
                _ws.Settings.UltimaTagVista = tag;
                Persist();
            }

            if (tag.Length > 0 && Atualizador.TemNovidade(atual, tag)) AtualizacaoTag = tag;
        }
        catch (Exception)
        {
            // atualização é conveniência: sem rede ou fora da cota, o app segue igual
        }
    }

    /// <summary>
    /// Baixa a release e troca o executável. Só o de arquivo único pode ser trocado por
    /// aqui; no build de pasta resta abrir a página, onde o usuário escolhe o que baixar.
    /// </summary>
    [RelayCommand]
    private async Task AtualizarAgoraAsync()
    {
        if (Atualizando) return;

        try
        {
            _release ??= await GitHubService.UltimaReleaseAsync(Atualizador.Slug, _ws.Settings.GithubUser);

            var exe = Atualizador.CaminhoDoExe();
            var arquivo = _release?.Standalone;

            if (arquivo is null || !Atualizador.PodeTrocarSozinho(exe))
            {
                ShellService.AbrirUrl(_release?.Url is { Length: > 0 } u
                    ? u
                    : $"https://github.com/{Atualizador.Slug}/releases/latest");
                return;
            }

            var mb = arquivo.Tamanho / 1024d / 1024d;
            var ok = await _dialogs.ConfirmAsync(
                "Atualizar o GRepos",
                $"Baixar a versão {AtualizacaoTag} ({mb:N0} MB) e reiniciar o aplicativo?\n\n" +
                "O executável atual é guardado como cópia e volta sozinho se algo falhar.");
            if (!ok) return;

            Atualizando = true;
            var progresso = new Progress<double>(p => AtualizacaoAviso = $"Baixando… {p:P0}");
            var pasta = System.IO.Path.GetDirectoryName(exe!)!;
            var baixado = await Atualizador.BaixarAsync(arquivo, pasta, progresso);

            AtualizacaoAviso = "Instalando…";
            Atualizador.Trocar(exe!, baixado);
            Atualizador.Reabrir(exe!);

            // o novo processo já está subindo; este sai para liberar o arquivo
            if (Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime vida)
                vida.Shutdown();
        }
        catch (Exception e)
        {
            Notify("Não foi possível atualizar: " + e.Message, true);
            AtualizacaoAviso = $"Atualização {AtualizacaoTag} disponível";
        }
        finally
        {
            Atualizando = false;
        }
    }

    /// <summary>
    /// Troca o estilo da árvore. O estilo chega pronto em cada nó, então mudar exige
    /// remontar a árvore — não é um estilo de XAML que se aplica sozinho.
    /// </summary>
    public void SetArvoreMinimalista(bool minimalista)
    {
        if (_ws.Settings.ArvoreMinimalista == minimalista) return;

        _ws.Settings.ArvoreMinimalista = minimalista;
        Persist();
        RebuildTree();
    }

    /// <summary>Vale para a próxima janela de esteira aberta; as abertas seguem como estão.</summary>
    public void SetGitBashPath(string caminho)
    {
        var valor = (caminho ?? "").Trim().Trim('"');
        if (_ws.Settings.GitBashPath == valor) return;

        _ws.Settings.GitBashPath = valor;
        Persist();
    }

    public void SetEsteirasVisiveis(int quantas)
    {
        var valor = Math.Clamp(quantas, 1, 50);
        if (_ws.Settings.EsteirasVisiveis == valor) return;

        _ws.Settings.EsteirasVisiveis = valor;
        Persist();
    }

    /// <summary>Liga ou desliga o aviso de versão nova; desligar some com o item da barra.</summary>
    public void SetAvisarAtualizacao(bool avisar)
    {
        if (_ws.Settings.AvisarAtualizacao == avisar) return;

        _ws.Settings.AvisarAtualizacao = avisar;
        Persist();

        if (!avisar) AtualizacaoTag = "";
        else _ = VerificarAtualizacaoAsync(forcar: true);
    }

    /// <summary>Largura da sidebar escolhida no divisor; volta assim na próxima abertura.</summary>
    public void SetSidebarWidth(double largura)
    {
        if (largura <= 0 || Math.Abs(_ws.Settings.SidebarWidth - largura) < 1) return;
        _ws.Settings.SidebarWidth = largura;
        Persist();
    }

    public void ApplySettings(string theme, string accent, string density, int autoRefresh, int logLimit,
        string defaultTab)
    {
        _ws.Settings.DefaultTab = defaultTab;
        _ws.Settings.Theme = theme;
        _ws.Settings.Accent = accent;
        _ws.Settings.Density = density;
        _ws.Settings.AutoRefreshSeconds = autoRefresh;
        _ws.Settings.LogLimit = logLimit;
        Persist();
        ApplyTimer();
    }

    public static string NewId() => Guid.NewGuid().ToString("n")[..8];
}
