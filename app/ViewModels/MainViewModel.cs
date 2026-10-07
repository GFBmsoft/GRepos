using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
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

    /// <summary>
    /// Nome, cor e o grupo em que este fica dentro (nulo: nível principal). Quem não
    /// implementa cai no diálogo antigo e mantém o pai que veio.
    /// </summary>
    async Task<(string Nome, string Cor, string? PaiId)?> ShowGrupoAsync(
        string titulo, string nome, string cor, string? paiId, IReadOnlyList<GrupoNaArvore> pais)
    {
        var r = await ShowGroupAsync(titulo, nome, cor);
        return r is null ? null : (r.Value.Nome, r.Value.Cor, paiId);
    }
    Task ShowAddRepoAsync(MainViewModel main);
    Task ShowRepoConfigAsync(MainViewModel main, Repo repo);
    Task ShowSettingsAsync(MainViewModel main);
    Task ShowBranchesAsync(MainViewModel main, Repo repo);

    /// <summary>Janela da esteira: execuções do GitHub Actions e seus passos.</summary>
    Task ShowEsteiraAsync(string slug, string branch, string usuario, string repoNome, int visiveis);

    /// <summary>Changelog do aplicativo, lido das releases publicadas.</summary>
    Task ShowNovidadesAsync();
    Task ShowStashAsync(MainViewModel main, Repo repo);

    /// <summary>Commits que mexeram no arquivo e autoria por linha (blame).</summary>
    Task ShowFileHistoryAsync(Repo repo, string caminho, bool blame, bool split) => Task.CompletedTask;

    /// <summary>Rebase interativo dos commits desde <paramref name="hash"/> até o HEAD.</summary>
    Task ShowRebaseAsync(MainViewModel main, Repo repo, string hash) => Task.CompletedTask;

    /// <summary>Arquivos ignorados só nesta máquina, com a opção de voltar a acompanhar.</summary>
    Task ShowIgnoradosAsync(MainViewModel main, Repo repo) => Task.CompletedTask;

    /// <summary>Issues do repositório: abertas, fechadas e as etiquetas.</summary>
    Task ShowIssuesAsync(IssuesViewModel vm) => Task.CompletedTask;

    /// <summary>Comparação entre duas branches, tags ou commits.</summary>
    Task ShowCompararAsync(CompararViewModel vm) => Task.CompletedTask;

    /// <summary>A mesma operação em vários repositórios (grupo ou par).</summary>
    Task ShowLoteAsync(LoteViewModel vm) => Task.CompletedTask;

    /// <summary>Pastas de trabalho (git worktree) do repositório.</summary>
    Task ShowWorktreesAsync(MainViewModel main, Repo repo) => Task.CompletedTask;

    /// <summary>Paleta de comandos (Ctrl+P); ao fechar, o escolhido está em <c>vm.Escolhido</c>.</summary>
    Task ShowPaletaAsync(PaletaViewModel vm) => Task.CompletedTask;

    /// <summary>Resolução de conflito bloco a bloco: o meu, o deles e o resultado.</summary>
    Task ShowConflitoAsync(ConflitoViewModel vm) => Task.CompletedTask;

    /// <summary>Pull requests do repositório: lista, detalhe, criar e mesclar.</summary>
    Task ShowPullRequestsAsync(PullRequestsViewModel vm) => Task.CompletedTask;
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

    /// <summary>Linha em destaque do aviso ("Puxar concluído"); vazia quando o aviso é só um texto.</summary>
    [ObservableProperty] private string _statusTitulo = "";

    /// <summary>"info", "sucesso", "erro" ou "andamento": decide a cor e o ícone do aviso.</summary>
    [ObservableProperty] private string _statusTipo = "info";
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
    /// <summary>De qual grupo é o painel aberto (nulo: o geral). Serve para refazê-lo quando a árvore muda.</summary>
    private string? _grupoDoPainel;

    public void MostrarPainel(string? grupoId)
    {
        SelectedNode = null;
        _grupoDoPainel = grupoId;

        var repos = grupoId is null
            ? _ws.Repos.ToList()
            : ReposDoGrupo(grupoId);

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
        // grupo com subgrupos também ganha uma seção por subgrupo, com título; as seções
        // seguem a ordem da árvore
        var ordem = GruposEmArvore().Select((g, i) => (g.Grupo.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var geral = grupoId is null || painel.Cartoes.Select(c => c.Repo.GroupId ?? "").Distinct().Count() > 1;
        painel.Secoes = new ObservableCollection<SecaoPainelViewModel>(
            painel.Cartoes
                .GroupBy(c => c.Repo.GroupId ?? "")
                .OrderBy(g => ordem.TryGetValue(g.Key, out var posicao) ? posicao : int.MaxValue)
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

        // a esteira é consultada pela branch de cada repositório: com a varredura ainda
        // em curso (abertura do app), espera o status chegar para perguntar pela branch certa
        if (_varrendo && _varredura is { } emCurso) await emCurso;
        if (!ReferenceEquals(painel, Painel)) return;

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

    /// <summary>O nome com o caminho ("Pasta / Subpasta"): no painel não há recuo que mostre o nível.</summary>
    private string NomeDoGrupo(string? grupoId) => GrupoArvore.Caminho(_ws.Groups, grupoId);

    /// <summary>Os grupos em ordem de árvore, com nível e caminho — para as caixas de escolha.</summary>
    public IReadOnlyList<GrupoNaArvore> GruposEmArvore() => GrupoArvore.EmOrdem(_ws.Groups);

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

            // qualquer nível recolhido acima dele o esconde: abre todos até a raiz
            var recolhidos = GrupoArvore.Ancestrais(_ws.Groups, repo.GroupId).Where(g => g.Collapsed).ToList();
            foreach (var g in recolhidos) g.Collapsed = false;
            if (recolhidos.Count > 0) Persist();

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
        "pulado" => "Esteira pulada",
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
        foreach (var p in new[] { nameof(TemCi), nameof(CiCor), nameof(CiRotulo), nameof(CiTooltip),
                                  nameof(TemCiOuGitHub) })
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
        TemGitHub = false;
        PrsAbertos = 0;

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
            TemGitHub = true;

            var run = await GitHubService.UltimaExecucaoAsync(
                slug, CurrentStatus?.Branch ?? "", usuario);
            if (SelectedNode?.Repo.Id != repo.Id) return;

            CiSituacao = run.Situacao;
            CiDetalhe = run.Detalhe;
            CiUrl = run.Url;

            // mesma consulta (e mesmo cache) do cartão do painel
            var prs = await GitHubService.PullRequestsAsync(slug, usuario);
            if (SelectedNode?.Repo.Id != repo.Id) return;
            PrsAbertos = prs.Count(p => p.Aberto);
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

    // -------------------------------------------------------- pull requests

    /// <summary>PRs abertos do repositório selecionado; o selo do botão da barra.</summary>
    [ObservableProperty] private int _prsAbertos;

    /// <summary>Remoto no GitHub: é o que faz existir botão de pull requests.</summary>
    [ObservableProperty] private bool _temGitHub;

    public string PrBadge => PrsAbertos > 0 ? PrsAbertos.ToString() : "";

    partial void OnPrsAbertosChanged(int value) => OnPropertyChanged(nameof(PrBadge));

    partial void OnTemGitHubChanged(bool value) => OnPropertyChanged(nameof(TemCiOuGitHub));

    /// <summary>O separador da barra aparece se houver qualquer um dos dois botões.</summary>
    public bool TemCiOuGitHub => TemCi || TemGitHub;

    [RelayCommand]
    private async Task AbrirPullRequestsAsync()
    {
        if (CurrentRepo is not { } repo || _ciSlug.Length == 0) return;
        await AbrirPullRequestsDeAsync(repo, _ciSlug, _ciUsuario, CurrentStatus?.Branch ?? "");
    }

    /// <summary>
    /// Abre os pull requests de um repositório — o selecionado ou o de um cartão do
    /// painel. Se algo foi mesclado ou fechado por lá, obtém do remoto na volta: a
    /// branch de destino local continuaria mostrando o estado de antes.
    /// </summary>
    public async Task AbrirPullRequestsDeAsync(Repo repo, string slug, string usuario, string branch)
    {
        try
        {
            var vm = await MontarPullRequestsAsync(repo, slug, usuario, branch);
            await _dialogs.ShowPullRequestsAsync(vm);
            await DepoisDosPullRequestsAsync(repo, vm);
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    /// <summary>A janela de pull requests de uma branch, já com o que o repositório local sabe dela.</summary>
    public async Task<PullRequestsViewModel> MontarPullRequestsAsync(
        Repo repo, string slug, string usuario, string branch, string destino = "", IDialogService? dialogos = null)
    {
        if (usuario.Length == 0) usuario = _ws.Settings.GithubUser;

        var contexto = new ContextoPr(slug, branch, usuario, repo.Name) { DestinoInicial = destino };
        try
        {
            var s = await GitService.SituacaoParaPrAsync(repo.Path, branch);
            contexto = contexto with
            {
                BranchEnviada = s.Enviada,
                NaoEnviados = s.NaoEnviados,
                TituloSugerido = s.Assunto,
                Destinos = s.Destinos,
            };
        }
        catch (GitException)
        {
            // sem o lado local a janela ainda lista e mescla; só o PR novo fica sem sugestões
        }

        return new PullRequestsViewModel(contexto, dialogos ?? _dialogs);
    }

    public async Task DepoisDosPullRequestsAsync(Repo repo, PullRequestsViewModel vm)
    {
        if (CurrentRepo?.Id != repo.Id) return;

        PrsAbertos = vm.Lista.Count(p => p.Pr.Aberto);
        if (vm.Alterou && !Busy) await FetchCommand.ExecuteAsync(null);
    }

    /// <summary>"owner/repo" e conta do GitHub de um repositório; null se o remoto não é do GitHub.</summary>
    public async Task<(string Slug, string Usuario)?> GitHubDeAsync(Repo repo)
    {
        try
        {
            var remoto = await GitService.RemoteUrlAsync(repo.Path);
            if (GitHubService.Slug(remoto) is not { } slug) return null;

            var usuario = GitHubService.ContaDoRepositorio(repo.Conta, remoto);
            return (slug, usuario.Length > 0 ? usuario : _ws.Settings.GithubUser);
        }
        catch (GitException)
        {
            return null; // sem remoto
        }
    }

    // ------------------------------------------- soltar uma branch sobre outra

    /// <summary>
    /// Roda a opção escolhida ao soltar <paramref name="origem"/> sobre
    /// <paramref name="destino"/>. Merge e rebase confirmam antes, mostrando os comandos;
    /// o erro do git sobe para quem chamou, que sabe onde mostrá-lo.
    /// </summary>
    /// <returns>Falso quando o usuário desistiu na confirmação.</returns>
    public async Task<bool> ExecutarArrasteAsync(
        Repo repo, OpcaoDeArraste opcao, RefDeBranch origem, RefDeBranch destino,
        Func<string, string, Task<bool>>? confirmar = null)
    {
        if (!opcao.Disponivel || opcao.Acao == AcaoDeArraste.PullRequest) return false;

        confirmar ??= _dialogs.ConfirmAsync;
        var titulo = opcao.Acao == AcaoDeArraste.Mesclar ? "Mesclar" : "Rebase";
        if (!await confirmar(titulo, ArrasteDeBranch.Confirmacao(opcao, origem, destino))) return false;

        try
        {
            if (opcao.Acao == AcaoDeArraste.Mesclar)
                await GitService.MesclarEmAsync(repo.Path, origem.Nome, destino.Nome);
            else
                await GitService.RebaseSobreAsync(repo.Path, origem.Nome, destino.Nome);
        }
        catch (GitException)
        {
            // o git sai com erro e deixa a operação em andamento: não é falha, é o
            // conflito esperando resolução
            if (GitService.OperacaoEmAndamento(repo.Path) != GitService.Operacao.Nenhuma)
                throw new GitException(
                    $"{titulo} parou em conflito. Resolva os arquivos na aba Alterações e use " +
                    "Continuar; para desistir, Abortar.");
            throw;
        }
        finally
        {
            // mesmo com erro: um merge em conflito já trocou de branch e mexeu nos arquivos
            try { await RefreshRepoAsync(repo.Id); } catch (Exception) { /* só os contadores */ }
        }
        return true;
    }

    // ---------------------------------------------------------------- issues

    [RelayCommand]
    private Task AbrirIssues() => CurrentRepo is { } repo && _ciSlug.Length > 0
        ? AbrirIssuesDeAsync(repo, _ciSlug, _ciUsuario)
        : Task.CompletedTask;

    /// <summary>As issues de um repositório — o selecionado ou o de um cartão do painel.</summary>
    public async Task AbrirIssuesDeAsync(Repo repo, string slug, string usuario)
    {
        try
        {
            if (usuario.Length == 0) usuario = _ws.Settings.GithubUser;
            await _dialogs.ShowIssuesAsync(new IssuesViewModel(slug, usuario, repo.Name));
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    // -------------------------------------------------------------- comparar

    /// <summary>
    /// Compara duas pontas do repositório. Sem a segunda, vale a branch atual: é a
    /// pergunta de sempre, "o que esta branch tem de diferente da minha?".
    /// </summary>
    public async Task CompararAsync(Repo repo, string a = "", string b = "")
    {
        try
        {
            if (b.Length == 0 && CurrentRepo?.Id == repo.Id) b = CurrentStatus?.Branch ?? "";
            await _dialogs.ShowCompararAsync(new CompararViewModel(repo, a, b, SplitDiff));
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    [RelayCommand]
    private Task Comparar() => CurrentRepo is { } repo ? CompararAsync(repo) : Task.CompletedTask;

    // ----------------------------------------------------- operações em lote

    /// <summary>
    /// Obter, puxar, enviar ou trocar de branch em vários repositórios de uma vez. Na
    /// volta a árvore e o painel são atualizados: o lote mexeu em todos eles.
    /// </summary>
    public async Task AbrirLoteAsync(string titulo, IEnumerable<Repo> repos)
    {
        try
        {
            var vm = MontarLote(titulo, repos);
            if (vm.Itens.Count == 0) return;

            await _dialogs.ShowLoteAsync(vm);
            if (!vm.Executou) return;

            await RefreshAllAsync();
            AtualizarCartoesDoPainel();
            if (CurrentRepo is not null) await LoadTabAsync();
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    public LoteViewModel MontarLote(string titulo, IEnumerable<Repo> repos) =>
        new(titulo, repos
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new LoteItemViewModel
            {
                Repo = r,
                CorDoGrupo = CorDoGrupo(r.GroupId),
                Status = _nodes.TryGetValue(r.Id, out var no) ? no.Status : null,
            }), this);

    /// <summary>Pastas de trabalho do repositório aberto. Uma delas pode ter entrado na árvore.</summary>
    [RelayCommand]
    private async Task OpenWorktrees()
    {
        if (CurrentRepo is not { } repo) return;
        await _dialogs.ShowWorktreesAsync(this, repo);
    }

    // ------------------------------------------------- paleta de comandos

    /// <summary>
    /// Ctrl+P: repositórios, branches do repositório aberto e ações, numa busca só. Com
    /// dezenas de repositórios é o caminho mais curto até qualquer um deles.
    /// </summary>
    [RelayCommand]
    private async Task AbrirPaletaAsync()
    {
        try
        {
            var vm = new PaletaViewModel(ItensDaPaleta());

            // as branches saem do git: entram quando chegam, sem atrasar a abertura
            var branches = BranchesDaPaletaAsync().ContinueWith(
                t => { if (t.Status == TaskStatus.RanToCompletion) vm.Acrescentar(t.Result); },
                TaskScheduler.FromCurrentSynchronizationContext());

            await _dialogs.ShowPaletaAsync(vm);
            if (vm.Escolhido is { } item) await item.Executar();
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
    }

    /// <summary>Repositórios e ações, na ordem em que aparecem sem nada digitado.</summary>
    public List<ItemDaPaleta> ItensDaPaleta()
    {
        var itens = new List<ItemDaPaleta>();

        void Acao(string titulo, string detalhe, System.Windows.Input.ICommand comando, bool cabe = true)
        {
            if (!cabe) return;
            itens.Add(new ItemDaPaleta
            {
                Tipo = "ação", Titulo = titulo, Detalhe = detalhe, Cor = "Accent",
                Executar = () =>
                {
                    if (comando.CanExecute(null)) comando.Execute(null);
                    return Task.CompletedTask;
                },
            });
        }

        // o que vale para o repositório aberto vem primeiro: é o que mais se usa
        if (CurrentRepo is { } atual && !Busy)
        {
            var nome = atual.Name;
            Acao("Obter", $"fetch em {nome}", FetchCommand);
            Acao("Puxar", $"pull em {nome}", PullCommand);
            Acao("Enviar", $"push de {nome}", PushCommand);
            Acao("Branches…", $"trocar, criar, mesclar em {nome}", OpenBranchesCommand);
            Acao("Pull requests…", $"ver, criar e mesclar em {nome}", AbrirPullRequestsCommand, TemGitHub);
            Acao("Issues…", $"abertas, fechadas e etiquetas de {nome}", AbrirIssuesCommand, TemGitHub);
            Acao("Esteira…", $"execuções do GitHub Actions de {nome}", AbrirEsteiraCommand, TemCi);
            Acao("Comparar branches ou commits…", $"o que muda de uma ponta para a outra em {nome}", CompararCommand);
            Acao("Pastas de trabalho (worktrees)…", $"outra branch de {nome} em outra pasta", OpenWorktreesCommand);
            Acao("Esconder (stash)…", $"guardar ou recuperar alterações de {nome}", OpenStashCommand);
            Acao("Desfazer a última ação", nome, DesfazerCommand);
            Acao("Terminal", $"Git Bash na pasta de {nome}", AlternarTerminalCommand);
            Acao("Abrir a pasta", atual.Path, AbrirPastaCommand);
            Acao("Abrir no GitHub", RemoteWebUrl, AbrirRemotoCommand, TemRemoto);
            Acao("Configurar repositório…", nome, OpenRepoConfigCommand);
        }

        foreach (var repo in _ws.Repos.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var grupo = _ws.Groups.FirstOrDefault(g => g.Id == (repo.GroupId ?? ""));
            var branch = _nodes.TryGetValue(repo.Id, out var no) ? no.Status?.Branch ?? "" : "";
            var id = repo.Id;

            itens.Add(new ItemDaPaleta
            {
                Tipo = "repositório",
                Titulo = repo.Name,
                Detalhe = string.Join(" · ", new[] { grupo?.Name ?? "", branch }.Where(p => p.Length > 0)),
                Cor = grupo?.Color ?? "TextDim",
                Executar = () =>
                {
                    SelecionarRepositorio(id);
                    return Task.CompletedTask;
                },
            });
        }

        foreach (var grupo in _ws.Groups)
        {
            var id = grupo.Id;
            itens.Add(new ItemDaPaleta
            {
                Tipo = "painel", Titulo = "Painel: " + grupo.Name, Detalhe = "cartões dos repositórios do grupo",
                Cor = grupo.Color,
                Executar = () =>
                {
                    MostrarPainel(id);
                    return Task.CompletedTask;
                },
            });
        }

        foreach (var grupo in _ws.Groups)
        {
            var doGrupo = ReposDoGrupo(grupo.Id);
            if (doGrupo.Count < 2) continue;

            var nome = NomeDoGrupo(grupo.Id);
            itens.Add(new ItemDaPaleta
            {
                Tipo = "lote", Titulo = "Em lote: " + nome,
                Detalhe = $"obter, puxar, enviar ou trocar de branch em {doGrupo.Count} repositórios",
                Cor = grupo.Color,
                Executar = () => AbrirLoteAsync(nome, doGrupo),
            });
        }

        if (_ws.Repos.Count > 1)
            itens.Add(new ItemDaPaleta
            {
                Tipo = "lote", Titulo = "Em lote: todos os repositórios", Cor = "Accent",
                Detalhe = "obter, puxar, enviar ou trocar de branch em todos",
                Executar = () => AbrirLoteAsync("todos os repositórios", _ws.Repos.ToList()),
            });

        itens.Add(new ItemDaPaleta
        {
            Tipo = "painel", Titulo = "Painel: todos os repositórios", Cor = "Accent",
            Executar = () =>
            {
                MostrarPainel(null);
                return Task.CompletedTask;
            },
        });

        Acao("Atualizar todos os repositórios", "varre o status de todos", RefreshAllCommand);
        Acao("Adicionar repositório…", "pasta local ou clone do GitHub", AddRepoCommand);
        Acao("Novo grupo…", "", AddGroupCommand);
        Acao("Preferências…", "", OpenSettingsCommand);
        Acao("Novidades", "o que mudou em cada versão", AbrirNovidadesCommand);
        Acao("Procurar atualização", "", ProcurarAtualizacaoCommand);
        return itens;
    }

    /// <summary>As branches do repositório aberto: escolher uma é trocar para ela.</summary>
    public async Task<List<ItemDaPaleta>> BranchesDaPaletaAsync()
    {
        var itens = new List<ItemDaPaleta>();
        if (CurrentRepo is not { } repo) return itens;

        var branches = await GitService.BranchesAsync(repo.Path);
        var locais = branches.Where(b => !b.IsRemote).Select(b => b.Name).ToHashSet();

        foreach (var b in branches.Where(b => !b.IsHead))
        {
            // remota que já tem a local de mesmo nome seria a mesma troca, repetida
            if (b.IsRemote && locais.Contains(new RefDeBranch(b.Name, true).NomeLocal)) continue;

            var alvo = b;
            itens.Add(new ItemDaPaleta
            {
                Tipo = "branch",
                Titulo = b.Name,
                Detalhe = b.IsRemote ? "trocar para ela, criando a local" : "trocar para ela",
                Cor = b.IsRemote ? "TextDim" : "Green",
                Executar = () => TrocarDeBranchAsync(repo, alvo),
            });
        }
        return itens;
    }

    private async Task TrocarDeBranchAsync(Repo repo, Branch branch)
    {
        if (CurrentRepo?.Id != repo.Id) return;

        await RunAsync(p => branch.IsRemote
            ? GitService.CheckoutRemotaAsync(p, branch.Name)
            : GitService.CheckoutAsync(p, branch.Name), "Troca de branch", "trocar de branch",
            (_, depois) => new Aviso("Branch trocada", depois is null ? "" : $"Agora em {depois.Branch}."));
        await LoadTabAsync();
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

    /// <summary>
    /// O workspace do disco já foi lido. Antes disso o que está na memória é um workspace
    /// vazio, e gravá-lo apagaria o do usuário.
    /// </summary>
    private bool _carregado;

    public async Task InitAsync()
    {
        _ws = WorkspaceStore.Load();
        _carregado = true;
        GitService.CredentialUser = _ws.Settings.GithubUser;
        MigrarContas();
        AplicarContasDosRepositorios();

        // sobra da atualização anterior: o .exe antigo já não está em uso agora
        Atualizador.LimparAntigo();
        _ = VerificarAtualizacaoAsync();

        RebuildTree();
        ApplyTimer();
        var varredura = RefreshAllAsync();

        // abre no painel: a visão de todos os repositórios diz mais, logo de cara, do
        // que um repositório escolhido por ordem alfabética. Ele aparece já, sem esperar
        // a varredura: os cartões se preenchem conforme cada repositório responde.
        if (_ws.Repos.Count > 0) MostrarPainel(null);
        else SelectedNode = Tree.OfType<RepoNode>().FirstOrDefault();

        await varredura;
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

    /// <summary>Verde deu certo, vermelho falhou, amarelo ainda está rodando; o resto é informação.</summary>
    public string StatusAccent => StatusTipo switch
    {
        "sucesso" => "Green",
        "erro" => "Red",
        "andamento" => "Yellow",
        _ => "Accent",
    };

    public string StatusIcone => StatusTipo switch
    {
        "sucesso" => "✓",
        "erro" => "✕",
        "andamento" => "●",
        _ => "i",
    };

    public bool TemStatusTitulo => StatusTitulo.Length > 0;
    public bool TemStatusDetalhe => StatusMessage.Length > 0;

    partial void OnStatusTipoChanged(string value)
    {
        OnPropertyChanged(nameof(StatusAccent));
        OnPropertyChanged(nameof(StatusIcone));
    }

    partial void OnStatusTituloChanged(string value) => OnPropertyChanged(nameof(TemStatusTitulo));
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(TemStatusDetalhe));

    /// <summary>Quanto um aviso de sucesso ou de informação fica na tela antes de sumir sozinho.</summary>
    public static readonly TimeSpan DuracaoDoAviso = TimeSpan.FromSeconds(7);

    private DispatcherTimer? _sumirAviso;

    /// <summary>Aviso simples, de uma linha só. Erro fica até ser fechado.</summary>
    public void Notify(string message, bool isError = false)
    {
        // erro que veio cru do git (em inglês, com "fatal:") ganha a explicação em português
        if (isError && MensagensGit.PareceDoGit(message))
        {
            var aviso = MensagensGit.Erro("concluir a operação", message);
            Avisar("erro", aviso.Titulo, aviso.Detalhe);
            return;
        }

        Avisar(isError ? "erro" : "info", "", message);
    }

    /// <summary>
    /// Mostra o aviso no cartão do rodapé. Sucesso e informação somem sozinhos; erro fica
    /// até o usuário fechar, e "andamento" fica até a operação trocar por outro.
    /// </summary>
    public void Avisar(string tipo, string titulo, string detalhe = "")
    {
        StatusTipo = tipo;
        StatusTitulo = titulo;
        StatusMessage = detalhe;
        StatusIsError = tipo == "erro";
        HasStatusMessage = true;

        _sumirAviso?.Stop();
        if (tipo is "erro" or "andamento") return;

        _sumirAviso ??= new DispatcherTimer { Interval = DuracaoDoAviso };
        _sumirAviso.Tick -= SumirAviso;
        _sumirAviso.Tick += SumirAviso;
        _sumirAviso.Start();
    }

    private void SumirAviso(object? sender, EventArgs e)
    {
        _sumirAviso?.Stop();

        // um erro ou uma operação que entrou depois não some por causa do aviso anterior
        if (StatusTipo is not ("erro" or "andamento")) HasStatusMessage = false;
    }

    public Task<bool> ConfirmAsync(string title, string message) => _dialogs.ConfirmAsync(title, message);

    public Task<string?> PromptAsync(string title, string label, string initial = "") =>
        _dialogs.PromptAsync(title, label, initial);

    public Task MostrarHistoricoDoArquivoAsync(Repo repo, string caminho, bool blame) =>
        _dialogs.ShowFileHistoryAsync(repo, caminho, blame, SplitDiff);

    public Task MostrarIgnoradosAsync(Repo repo) => _dialogs.ShowIgnoradosAsync(this, repo);

    /// <summary>Depois do rebase a branch mudou: barra e aba visível são recarregadas.</summary>
    public async Task MostrarRebaseAsync(Repo repo, string hash)
    {
        await _dialogs.ShowRebaseAsync(this, repo, hash);
        await RefreshRepoAsync(repo.Id);
        await LoadTabAsync();
    }

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

        var minimalista = _ws.Settings.ArvoreMinimalista;
        var conhecidos = _ws.Groups.Select(g => g.Id).ToHashSet();

        // os repositórios de um grupo, com os pares sob um título só — é o que evita a
        // duplicação de abas quando o mesmo módulo existe em dois bancos
        void Repositorios(List<Repo> list, string color, int nivel)
        {
            var pairs = list.Where(r => !string.IsNullOrEmpty(r.PairKey))
                            .GroupBy(r => r.PairKey!)
                            .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase);

            foreach (var pair in pairs)
            {
                nodes.Add(new PairNode { Key = pair.Key, Nivel = nivel });
                foreach (var r in pair.OrderBy(RoleOrder))
                    nodes.Add(MakeNode(r, true, color, nivel));
            }

            foreach (var r in list.Where(r => string.IsNullOrEmpty(r.PairKey)))
                nodes.Add(MakeNode(r, false, color, nivel));
        }

        // pasta, subpasta e repositórios: cada grupo desenha os subgrupos e depois os
        // repositórios que estão direto nele. Recolhido, esconde tudo que está abaixo
        void Grupos(string? paiId, int nivel)
        {
            foreach (var g in GrupoArvore.Filhos(_ws.Groups, paiId))
            {
                var abaixo = GrupoArvore.ComDescendentes(_ws.Groups, g.Id);
                var total = visible.Count(r => r.GroupId is { } gid && abaixo.Contains(gid));

                var showCollapsed = g.Collapsed && string.IsNullOrEmpty(q);
                nodes.Add(new GroupNode
                {
                    Id = g.Id, Name = g.Name, Color = g.Color, Collapsed = showCollapsed,
                    Count = total, Minimalista = minimalista, Nivel = nivel,
                });
                if (showCollapsed) continue;

                Grupos(g.Id, nivel + 1);
                Repositorios(visible.Where(r => r.GroupId == g.Id)
                    .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), g.Color, nivel);
            }
        }

        Grupos(null, 0);

        // sem grupo: os que nunca tiveram um, e os de um grupo que não existe mais
        var soltos = visible.Where(r => string.IsNullOrEmpty(r.GroupId) || !conhecidos.Contains(r.GroupId))
            .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (soltos.Count > 0)
        {
            nodes.Add(new GroupNode { Id = "", Name = "Sem grupo", Color = "#5D6675", Count = soltos.Count, Minimalista = minimalista });
            Repositorios(soltos, "#5D6675", 0);
        }

        Tree = nodes;
        OnPropertyChanged(nameof(RepoCountText));
    }

    private static int RoleOrder(Repo r) => r.Role switch { "origem" => 0, "destino" => 1, _ => 2 };

    private RepoNode MakeNode(Repo r, bool paired, string groupColor, int nivel = 0)
    {
        var node = new RepoNode
        {
            Repo = r,
            Nivel = nivel,
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

    /// <summary>
    /// Quantos <c>git status</c> rodam ao mesmo tempo. Cem de uma vez disputam disco e
    /// processador entre si e com a tela; em fila curta terminam no mesmo tempo e o
    /// aplicativo continua respondendo enquanto isso.
    /// </summary>
    private static readonly int GitsSimultaneos = Math.Clamp(Environment.ProcessorCount, 4, 8);

    private Task? _varredura;

    private Task VarrerAsync(bool automatica)
    {
        if (_ws.Repos.Count == 0) return Task.CompletedTask;

        // Quem chega no meio de uma varredura espera a mesma, em vez de voltar na hora de
        // mãos vazias: era assim que "Atualizar" clicado durante a varredura automática
        // não mostrava nada, e só o segundo clique trazia o status novo.
        if (_varrendo && _varredura is { } emCurso) return emCurso;
        return _varredura = VarrerDeFatoAsync(automatica);
    }

    private async Task VarrerDeFatoAsync(bool automatica)
    {
        _varrendo = true;
        if (!automatica) Scanning = true;
        try
        {
            using var vagas = new SemaphoreSlim(GitsSimultaneos);
            var pendentes = _ws.Repos.ToList().Select(r => StatusNaFilaAsync(r, vagas)).ToList();

            // cada repositório aparece assim que o git dele responde: com muitos, esperar
            // o último para mostrar o primeiro deixava a árvore vazia por segundos
            while (pendentes.Count > 0)
            {
                var pronta = await Task.WhenAny(pendentes);
                pendentes.Remove(pronta);
                var (repo, status) = await pronta;
                if (status is null) continue;

                // guardado mesmo sem nó: o repositório pode estar num grupo recolhido
                _statusConhecido[repo.Id] = status;
                if (_nodes.TryGetValue(repo.Id, out var node))
                {
                    node.Status = status;
                    node.Refreshed();
                }
                if (CurrentRepo?.Id == repo.Id) RefreshHeaderBindings();
            }

            RefreshHeaderBindings();
            // o painel aberto acompanha: antes só o botão dele repassava o status novo
            AtualizarCartoesDoPainel();
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

    /// <summary>Status de um repositório, fora da thread da tela; null se o git nem chegou a rodar.</summary>
    private static async Task<(Repo Repo, RepoStatus? Status)> StatusNaFilaAsync(Repo repo, SemaphoreSlim vagas)
    {
        await vagas.WaitAsync().ConfigureAwait(false);
        try
        {
            return (repo, await Task.Run(() => GitService.StatusAsync(repo.Path)).ConfigureAwait(false));
        }
        catch (Exception)
        {
            return (repo, null); // um repositório com problema não derruba a varredura dos outros
        }
        finally
        {
            vagas.Release();
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

    /// <param name="nome">O nome do botão, como substantivo do aviso: "Obter", "Puxar"…</param>
    /// <param name="verbo">O mesmo no infinitivo minúsculo, para o erro: "obter", "puxar"…</param>
    /// <param name="resumo">
    /// O que dizer no fim, a partir do repositório antes e depois. Sem ele vale a última
    /// linha que o git escreveu, traduzida.
    /// </param>
    private async Task RunAsync(Func<string, Task<string>> action, string nome, string verbo,
        Func<RepoStatus?, RepoStatus?, Aviso>? resumo = null)
    {
        var repo = CurrentRepo;
        if (repo is null) return;
        Busy = true;
        var antes = CurrentStatus;

        // a barra desabilitada precisa dizer por quê
        Avisar("andamento", $"{nome} em andamento…", repo.Name);
        try
        {
            var outp = await action(repo.Path);
            await RefreshRepoAsync(repo.Id);
            if (SelectedTab == 0 && Changes is not null) await Changes.ReloadAsync();

            var aviso = resumo?.Invoke(antes, CurrentRepo?.Id == repo.Id ? CurrentStatus : null)
                        ?? new Aviso($"{nome} concluído", MensagensGit.Traduzir(outp));

            // a sincronização das branches acrescenta a própria linha ao que o git escreveu
            var extra = outp.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
            var sincronizou = extra.EndsWith("criada(s).") || extra.EndsWith("atualizada(s).");
            var detalhe = resumo is not null && sincronizou
                ? $"{aviso.Detalhe} {char.ToUpperInvariant(extra[0])}{extra[1..]}".Trim()
                : aviso.Detalhe;

            Avisar("sucesso", aviso.Titulo, detalhe);
        }
        catch (Exception e)
        {
            var aviso = MensagensGit.Erro(verbo, e.Message);
            Avisar("erro", aviso.Titulo, aviso.Detalhe);
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private Task Fetch() => RunAsync(
        p => ComSincronizacaoAsync(p, GitService.FetchAsync(p, BuscarTodasAsTags)),
        "Obter", "obter", (_, depois) => MensagensGit.Obtido(depois));

    [RelayCommand]
    private Task Pull() => RunAsync(
        p => ComSincronizacaoAsync(p, PullComTagsAsync(p)),
        "Puxar", "puxar", MensagensGit.Puxado);

    private async Task<string> PullComTagsAsync(string repo)
    {
        if (BuscarTodasAsTags) await GitService.FetchAsync(repo, todasAsTags: true);
        return await GitService.PullAsync(repo, false);
    }

    /// <summary>Roda a operação e, com a opção ligada, deixa as branches locais em dia com as remotas.</summary>
    private async Task<string> ComSincronizacaoAsync(string repo, Task<string> operacao)
    {
        var saida = await operacao;
        if (!SincronizarTodasAsBranches) return saida;

        var (criadas, atualizadas) = await GitService.SincronizarBranchesLocaisAsync(repo);
        if (criadas + atualizadas == 0) return saida;
        var partes = new List<string>();
        if (criadas > 0) partes.Add($"{criadas} branch(es) local(is) criada(s)");
        if (atualizadas > 0) partes.Add($"{atualizadas} atualizada(s)");
        return saida.TrimEnd() + "\n" + string.Join(", ", partes) + ".";
    }

    /// <summary>Obter traz todas as tags do remoto (opção também no clique direito de Obter/Puxar).</summary>
    public bool BuscarTodasAsTags
    {
        get => _ws.Settings.BuscarTodasAsTags;
        set
        {
            if (_ws.Settings.BuscarTodasAsTags == value) return;
            _ws.Settings.BuscarTodasAsTags = value;
            Persist();
            OnPropertyChanged();
        }
    }

    // o item do menu só mostra a marca; quem alterna é o comando — não depende de o
    // MenuItem alternar sozinho (o que muda entre versões do Avalonia)
    [RelayCommand]
    private void AlternarTodasAsTags() => BuscarTodasAsTags = !BuscarTodasAsTags;

    [RelayCommand]
    private void AlternarTodasAsBranches() => SincronizarTodasAsBranches = !SincronizarTodasAsBranches;

    /// <summary>Obter e Puxar criam e avançam as branches locais de todas as remotas.</summary>
    public bool SincronizarTodasAsBranches
    {
        get => _ws.Settings.SincronizarTodasAsBranches;
        set
        {
            if (_ws.Settings.SincronizarTodasAsBranches == value) return;
            _ws.Settings.SincronizarTodasAsBranches = value;
            Persist();
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private Task Push() => RunAsync(
        p => GitService.PushAsync(p, string.IsNullOrEmpty(CurrentStatus?.Upstream)),
        "Enviar", "enviar", MensagensGit.Enviado);

    /// <summary>Desfaz a última ação do repositório (commit, reset, merge, checkout…), lida do reflog.</summary>
    [RelayCommand]
    private async Task Desfazer()
    {
        if (CurrentRepo is not { } repo) return;
        PlanoDesfazer? plano;
        try
        {
            plano = await Services.Desfazer.PlanejarAsync(repo.Path);
        }
        catch (Exception)
        {
            plano = null;
        }
        if (plano is null)
        {
            Notify("Nada a desfazer neste repositório.");
            return;
        }

        var ok = await ConfirmAsync("Desfazer",
            plano.Descricao +
            (plano.JaEnviado
                ? "\n\nAtenção: isso já foi enviado ao remoto. Depois de desfazer, o próximo envio vai exigir push forçado."
                : "") +
            "\n\nClicar em Desfazer de novo refaz o que foi desfeito.");
        if (!ok) return;

        await RunAsync(p => GitService.RunAsync(p, plano.Args), "Desfazer", "desfazer");
        await LoadTabAsync();
    }

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
        await NovoGrupoAsync(null);
    }

    /// <summary>Novo grupo no nível principal ou, com <paramref name="paiId"/>, dentro de outro.</summary>
    public async Task NovoGrupoAsync(string? paiId)
    {
        var pai = _ws.Groups.FirstOrDefault(g => g.Id == paiId);

        // subgrupo nasce com a cor do pai: a pasta inteira fica do mesmo tom, e quem quiser muda
        var sugerida = pai?.Color ?? GroupPalette.ProximaLivre(_ws.Groups.Select(g => g.Color));
        var r = await _dialogs.ShowGrupoAsync(
            pai is null ? "Novo grupo" : $"Novo subgrupo de {pai.Name}", "", sugerida, pai?.Id, GruposEmArvore());
        if (r is null) return;

        CreateGroup(r.Value.Nome, r.Value.Cor, r.Value.PaiId);

        // pai recolhido esconderia o subgrupo que acabou de nascer
        foreach (var g in GrupoArvore.Ancestrais(_ws.Groups, r.Value.PaiId).Where(g => g.Collapsed)) g.Collapsed = false;
        Persist();
        RebuildTree();
    }

    /// <summary>Edita nome e cor de um grupo existente.</summary>
    public async Task EditGroupAsync(string id)
    {
        var g = _ws.Groups.FirstOrDefault(x => x.Id == id);
        if (g is null) return;

        // ele mesmo e o que está abaixo dele não podem ser o pai
        var abaixo = GrupoArvore.ComDescendentes(_ws.Groups, id);
        var pais = GruposEmArvore().Where(x => !abaixo.Contains(x.Grupo.Id)).ToList();

        var r = await _dialogs.ShowGrupoAsync("Editar grupo", g.Name, g.Color, g.ParentId, pais);
        if (r is null) return;

        UpdateGroup(id, r.Value.Nome, r.Value.Cor, r.Value.PaiId, mudarPai: true);
    }

    [RelayCommand]
    private void ToggleSplit() => SplitDiff = !SplitDiff;

    [RelayCommand]
    private void ToggleWrap() => WrapDiff = !WrapDiff;

    // ------------------------------------------------ mutações do workspace

    /// <summary>Repositórios do grupo e de tudo que está dentro dele. Vazio ("") são os sem grupo.</summary>
    public List<Repo> ReposDoGrupo(string grupoId)
    {
        if (grupoId.Length == 0)
        {
            var conhecidos = _ws.Groups.Select(g => g.Id).ToHashSet();
            return _ws.Repos.Where(r => string.IsNullOrEmpty(r.GroupId) || !conhecidos.Contains(r.GroupId)).ToList();
        }

        var abaixo = GrupoArvore.ComDescendentes(_ws.Groups, grupoId);
        return _ws.Repos.Where(r => r.GroupId is { } gid && abaixo.Contains(gid)).ToList();
    }

    /// <param name="paiId">Grupo em que o novo fica dentro; nulo é o nível principal.</param>
    public string CreateGroup(string name, string? cor = null, string? paiId = null)
    {
        var g = new Group
        {
            Id = NewId(),
            ParentId = string.IsNullOrEmpty(paiId) || _ws.Groups.All(x => x.Id != paiId) ? null : paiId,
            Name = name,
            Color = GroupPalette.Normalizar(cor) ?? GroupPalette.ProximaLivre(_ws.Groups.Select(x => x.Color)),
        };
        _ws.Groups.Add(g);
        Persist();
        return g.Id;
    }

    /// <param name="conta">Conta do GitHub do repositório; vazio fica com a principal.</param>
    public void AddRepository(string path, string name, string? groupId, string? conta = null)
    {
        var norm = path.Replace('\\', '/').TrimEnd('/');
        if (_ws.Repos.Any(r => r.Path.Replace('\\', '/').TrimEnd('/')
                .Equals(norm, StringComparison.OrdinalIgnoreCase)))
        {
            Notify("Este repositório já está no workspace.");
            return;
        }

        var repo = new Repo
        {
            Id = NewId(), Name = name, Path = path, GroupId = groupId,
            Conta = string.IsNullOrWhiteSpace(conta) ? null : conta.Trim(),
        };
        GitService.DefinirConta(repo.Path, repo.Conta);
        _ws.Repos.Add(repo);
        Persist();
        RebuildTree();
        SelectRepo(repo.Id);
        _ = RefreshRepoAsync(repo.Id);
        _ = PreencherModeloDoRemotoAsync(repo);
    }

    /// <summary>
    /// Repositório que já chega com remoto ganha o link no padrão do GRepos ({{user}} no
    /// lugar da credencial). Só o modelo: o .git/config muda quando o usuário salva a
    /// configuração do repositório.
    /// </summary>
    private async Task PreencherModeloDoRemotoAsync(Repo repo)
    {
        if (string.IsNullOrEmpty(Settings.GithubUser)) return;
        var modelo = await RemotoConfig.SugerirAsync(repo.Path);
        if (modelo.Length == 0 || repo.RemoteTemplate is not null) return;
        repo.RemoteTemplate = modelo;
        Persist();
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

    public void UpdateGroup(string id, string name, string color) => UpdateGroup(id, name, color, null, false);

    /// <param name="mudarPai">Sem isto o grupo fica onde está, e <paramref name="paiId"/> é ignorado.</param>
    public void UpdateGroup(string id, string name, string color, string? paiId, bool mudarPai)
    {
        var g = _ws.Groups.FirstOrDefault(x => x.Id == id);
        if (g is null) return;
        g.Name = name;
        g.Color = color;

        // dentro de si mesmo ou de um descendente viraria um ciclo: fica onde estava
        if (mudarPai && GrupoArvore.PodeFicarDentro(_ws.Groups, id, paiId))
            g.ParentId = string.IsNullOrEmpty(paiId) || _ws.Groups.All(x => x.Id != paiId) ? null : paiId;
        Persist();
        RebuildTree();
    }

    // ------------------------------------------ arrastar e soltar na árvore

    /// <summary>
    /// Para onde vai o que foi arrastado, se couber: um repositório solto num grupo (ou
    /// num repositório, que vale o grupo dele) muda de grupo; um grupo solto noutro vira
    /// subgrupo. Soltar um grupo no Painel o leva ao nível principal.
    /// </summary>
    /// <returns>
    /// Falso quando não há o que fazer — o destino é onde ele já está, ou um grupo iria
    /// para dentro de si mesmo. <paramref name="destino"/> nulo é "sem grupo" (ou a raiz).
    /// </returns>
    public bool PodeSoltar(SidebarNode? origem, SidebarNode? alvo, out string? destino)
    {
        destino = null;
        if (origem is null || alvo is null || ReferenceEquals(origem, alvo)) return false;

        var conhecidos = _ws.Groups.Select(g => g.Id).ToHashSet();
        string? Existente(string? id) => !string.IsNullOrEmpty(id) && conhecidos.Contains(id) ? id : null;

        switch (alvo)
        {
            case GroupNode g:
                destino = Existente(g.Id); // "Sem grupo" tem Id vazio
                break;
            case RepoNode r:
                destino = Existente(r.Repo.GroupId);
                break;
            case PainelNode when origem is GroupNode:
                break; // raiz
            default:
                return false;
        }

        switch (origem)
        {
            case RepoNode repo:
                return Existente(repo.Repo.GroupId) != destino;

            case GroupNode grupo when grupo.Id.Length > 0:
                var atual = _ws.Groups.FirstOrDefault(x => x.Id == grupo.Id);
                return atual is not null &&
                       Existente(atual.ParentId) != destino &&
                       GrupoArvore.PodeFicarDentro(_ws.Groups, grupo.Id, destino);

            default:
                return false;
        }
    }

    /// <summary>Move o que foi arrastado. O destino é aberto, para o resultado ficar à vista.</summary>
    public bool Soltar(SidebarNode? origem, SidebarNode? alvo)
    {
        if (!PodeSoltar(origem, alvo, out var destino)) return false;

        string nome;
        if (origem is RepoNode repo)
        {
            repo.Repo.GroupId = destino;
            nome = repo.Name;
        }
        else
        {
            var grupo = _ws.Groups.First(g => g.Id == ((GroupNode)origem!).Id);
            grupo.ParentId = destino;
            nome = grupo.Name;
        }

        foreach (var g in GrupoArvore.Ancestrais(_ws.Groups, destino).Where(g => g.Collapsed)) g.Collapsed = false;
        Persist();
        RebuildTree();

        // o painel aberto agrupa pelos mesmos grupos: refeito, mostra o repositório no lugar novo
        if (Painel is not null) MostrarPainel(_grupoDoPainel);

        Notify(destino is null
            ? origem is RepoNode ? $"{nome} ficou sem grupo." : $"{nome} foi para o nível principal."
            : $"{nome} foi para {NomeDoGrupo(destino)}.");
        return true;
    }

    public void RemoveGroup(string id)
    {
        // o que estava dentro sobe um nível: subgrupos e repositórios vão para o pai dele
        var pai = _ws.Groups.FirstOrDefault(g => g.Id == id)?.ParentId;
        _ws.Groups.RemoveAll(g => g.Id == id);
        foreach (var g in _ws.Groups.Where(g => g.ParentId == id)) g.ParentId = pai;
        foreach (var r in _ws.Repos.Where(r => r.GroupId == id)) r.GroupId = pai;
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
        catch (Exception e)
        {
            // dizer "você já está na mais recente" quando a consulta falhou era mentira:
            // o usuário clicava de novo, a segunda passava e só aí a versão nova aparecia
            Notify("Não foi possível consultar a versão mais recente: " + e.Message, true);
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
        catch (Exception) when (!forcar)
        {
            // atualização é conveniência: sem rede ou fora da cota, o app segue igual.
            // Pedida pelo usuário, a falha sobe para quem chamou dizer o que houve.
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

    public void SetDiffExterno(string caminho, string argumentos)
    {
        var valor = (caminho ?? "").Trim().Trim('"');
        var args = (argumentos ?? "").Trim();
        if (_ws.Settings.DiffExternoPath == valor && _ws.Settings.DiffExternoArgs == args) return;

        _ws.Settings.DiffExternoPath = valor;
        _ws.Settings.DiffExternoArgs = args;
        Persist();
    }

    /// <summary>Compara as duas versões na ferramenta externa das preferências.</summary>
    public async Task AbrirDiffExternoAsync(Repo repo, VersaoDeArquivo esquerda, VersaoDeArquivo direita)
    {
        try
        {
            await DiffExterno.AbrirAsync(repo.Path, esquerda, direita,
                _ws.Settings.DiffExternoPath, _ws.Settings.DiffExternoArgs);
        }
        catch (Exception e)
        {
            Notify(e.Message, true);
        }
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
    /// <summary>Guarda como a janela ficou, para a próxima abertura. Só grava se mudou.</summary>
    public void SetJanela(double largura, double altura, bool maximizada)
    {
        // janela fechada antes de o workspace ser lido: não há onde guardar sem apagar o resto
        if (!_carregado) return;

        var s = _ws.Settings;
        if (largura <= 0 || altura <= 0) (largura, altura) = (s.JanelaLargura, s.JanelaAltura);

        if (Math.Abs(s.JanelaLargura - largura) < 1 && Math.Abs(s.JanelaAltura - altura) < 1 &&
            s.JanelaMaximizada == maximizada)
            return;

        s.JanelaLargura = largura;
        s.JanelaAltura = altura;
        s.JanelaMaximizada = maximizada;
        Persist();
    }

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
