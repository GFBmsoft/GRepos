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

/// <summary>Um repositório no painel: estado do git de um lado, esteira do outro.</summary>
public sealed partial class CartaoRepoViewModel : ObservableObject
{
    private readonly MainViewModel? _main;

    public CartaoRepoViewModel(Repo repo, RepoStatus? status, string corDoGrupo, MainViewModel? main = null)
    {
        Repo = repo;
        _main = main;
        CorDoGrupo = corDoGrupo;
        Status = status;
    }

    public Repo Repo { get; }
    public string CorDoGrupo { get; }

    [ObservableProperty] private RepoStatus? _status;

    /// <summary>"", "nenhum" ou o que a API devolveu. Vazio é "ainda consultando".</summary>
    [ObservableProperty] private string _ciSituacao = "";
    [ObservableProperty] private string _ciDetalhe = "";
    [ObservableProperty] private string _ciUrl = "";
    [ObservableProperty] private string _ciWorkflow = "";

    /// <summary>"owner/repo" e conta, guardados para abrir a esteira deste repositório.</summary>
    [ObservableProperty] private string _slug = "";
    public string Usuario { get; set; } = "";

    /// <summary>Remoto no GitHub: dá para abrir a janela de pull requests.</summary>
    public bool TemGitHub => Slug.Length > 0;

    partial void OnSlugChanged(string value) => OnPropertyChanged(nameof(TemGitHub));

    public string Nome => Repo.Name;
    public string Caminho => Repo.Path;

    public string Branch => Status?.Branch is { Length: > 0 } b ? b : "—";
    public bool TemErro => Status?.Error is { Length: > 0 };
    public string Erro => Status?.Error ?? "";

    public bool MostraAhead => Status is { Ahead: > 0 };
    public bool MostraBehind => Status is { Behind: > 0 };
    public bool MostraConflito => Status is { Conflicted: > 0 };
    public bool MostraSujo => Status is { IsDirty: true, Conflicted: 0 };

    public string AheadTexto => $"↑{Status?.Ahead ?? 0}";
    public string BehindTexto => $"↓{Status?.Behind ?? 0}";
    public string ConflitoTexto => $"!{Status?.Conflicted ?? 0}";
    public string SujoTexto => $"●{Status?.PendingFiles ?? 0}";

    /// <summary>Nada pendente e nada a enviar ou receber: o repositório está em dia.</summary>
    public bool EmDia => !TemErro && Status is not null &&
                         !MostraAhead && !MostraBehind && !MostraConflito && !MostraSujo;

    // ------------------------------------------------------------- esteira

    public bool ConsultandoCi => CiSituacao.Length == 0;

    /// <summary>
    /// Repositório sem GitHub Actions não é erro nem pendência: a maioria dos
    /// repositórios locais não tem esteira, e marcá-los em vermelho seria ruído.
    /// </summary>
    public bool SemCi => CiSituacao is "nenhum" or "indisponivel";

    public bool TemCi => !ConsultandoCi && !SemCi;

    public string CiTexto => CiSituacao switch
    {
        "sucesso" => "esteira ok",
        "falha" => "esteira quebrou",
        "rodando" => "esteira rodando",
        "cancelado" => "esteira cancelada",
        _ => "sem esteira",
    };

    public string CiCor => CiVisual.Cor(CiSituacao);
    public string CiSimbolo => CiVisual.Simbolo(CiSituacao);

    /// <summary>O que rodou, abaixo da situação: "build · Painel de repositórios".</summary>
    public string CiDetalheTexto
    {
        get
        {
            if (!TemCi) return "";
            var partes = new[] { CiWorkflow, CiDetalhe }.Where(p => p.Length > 0);
            return string.Join(" · ", partes);
        }
    }

    // ------------------------------------------------------- pull requests

    [ObservableProperty] private int _prsAbertos;
    [ObservableProperty] private string _prUltimo = "";
    [ObservableProperty] private bool _prConsultado;

    public bool TemPrs => PrConsultado && PrsAbertos > 0;

    public string PrTexto => PrsAbertos switch
    {
        0 => "",
        1 => "1 PR aberto",
        _ => $"{PrsAbertos} PRs abertos",
    };

    partial void OnPrsAbertosChanged(int value)
    {
        OnPropertyChanged(nameof(TemPrs));
        OnPropertyChanged(nameof(PrTexto));
        OnPropertyChanged(nameof(Tooltip));
    }

    partial void OnPrConsultadoChanged(bool value) => OnPropertyChanged(nameof(TemPrs));

    /// <summary>Pull requests dentro do app, como a esteira: o navegador fica para o detalhe.</summary>
    [RelayCommand]
    private async Task AbrirPrsAsync()
    {
        try
        {
            if (Slug.Length == 0) return;

            if (_main is null) ShellService.AbrirUrl($"https://github.com/{Slug}/pulls");
            else await _main.AbrirPullRequestsDeAsync(Repo, Slug, Usuario, Status?.Branch ?? "");
        }
        catch (Exception e)
        {
            _main?.Notify(e.Message, true);
        }
    }

    [RelayCommand]
    private Task AbrirIssues() => _main is null || Slug.Length == 0
        ? Task.CompletedTask
        : _main.AbrirIssuesDeAsync(Repo, Slug, Usuario);

    public string Tooltip
    {
        get
        {
            if (TemErro) return $"{Caminho}\n{Erro}";

            var linhas = new List<string> { Caminho };
            var detalhe = CiDetalheTexto;
            if (detalhe.Length > 0) linhas.Add($"{CiTexto} — {detalhe}");
            if (PrUltimo.Length > 0) linhas.Add("último PR: " + PrUltimo);

            return string.Join("\n", linhas);
        }
    }

    partial void OnStatusChanged(RepoStatus? value) => OnPropertyChanged(string.Empty);

    partial void OnCiSituacaoChanged(string value)
    {
        foreach (var p in new[] { nameof(ConsultandoCi), nameof(SemCi), nameof(TemCi),
                                  nameof(CiTexto), nameof(CiCor), nameof(CiSimbolo),
                                  nameof(CiDetalheTexto), nameof(Tooltip) })
            OnPropertyChanged(p);

        // expandiu antes de a consulta voltar: a esteira entra assim que se sabe que existe
        if (Expandido && TemCi) _ = AcompanharEsteiraAsync();
    }

    // ------------------------------------------------------------ expandido

    /// <summary>
    /// Clicar no cartão o abre ali mesmo, com o passo a passo da última execução da
    /// branch e as ações da esteira. Abrir o repositório virou um botão dentro dele.
    /// </summary>
    [ObservableProperty] private bool _expandido;

    /// <summary>Esteira do cartão aberto; nasce na primeira expansão e é reaproveitada.</summary>
    [ObservableProperty] private EsteiraViewModel? _esteira;
    private string _branchDaEsteira = "";

    public string Seta => Expandido ? "" : "";

    public bool MostraEsteira => Expandido && Esteira is not null;
    public bool MostraSemEsteira => Expandido && SemCi;
    public bool MostraConsultando => Expandido && ConsultandoCi;

    partial void OnEsteiraChanged(EsteiraViewModel? value) => OnPropertyChanged(nameof(MostraEsteira));

    partial void OnExpandidoChanged(bool value)
    {
        foreach (var p in new[] { nameof(Seta), nameof(MostraEsteira),
                                  nameof(MostraSemEsteira), nameof(MostraConsultando) })
            OnPropertyChanged(p);

        if (value && TemCi) _ = AcompanharEsteiraAsync();
        else if (!value) Esteira?.Parar();
    }

    [RelayCommand]
    private void Alternar() => Expandido = !Expandido;

    private async Task AcompanharEsteiraAsync()
    {
        try
        {
            if (Slug.Length == 0) return;

            // trocou de branch desde a última vez: a esteira antiga olhava outra branch
            var branch = Status?.Branch ?? "";
            if (Esteira is not null && _branchDaEsteira != branch)
            {
                Esteira.Parar();
                Esteira = null;
            }

            // uma execução só — a mais recente da branch; o histórico fica em "Ver todas"
            _branchDaEsteira = branch;
            Esteira ??= new EsteiraViewModel(Slug, branch, Usuario, Nome,
                _main?.Dialogos, visiveis: 1, somenteBranch: true);

            await Esteira.IniciarAsync();
        }
        catch (Exception e)
        {
            _main?.Notify(e.Message, true);
        }
    }

    /// <summary>Para a consulta periódica; o painel chama quando sai de cena.</summary>
    public void PararEsteira() => Esteira?.Parar();

    [RelayCommand]
    private void Abrir() => _main?.SelecionarRepositorio(Repo.Id);

    /// <summary>
    /// Abre a esteira deste repositório dentro do app, com o passo a passo — e não a
    /// página do GitHub: o cartão mostra a situação, o detalhe fica na janela.
    /// </summary>
    [RelayCommand]
    private async Task AbrirEsteiraAsync()
    {
        try
        {
            if (_main is null) return;

            if (Slug.Length > 0)
                await _main.AbrirEsteiraDeAsync(Slug, Status?.Branch ?? "", Usuario, Nome);
            else if (CiUrl.Length > 0)
                ShellService.AbrirUrl(CiUrl);
        }
        catch (Exception e)
        {
            _main?.Notify(e.Message, true);
        }
    }

    [RelayCommand]
    private void AbrirPasta()
    {
        try
        {
            ShellService.AbrirPasta(Repo.Path);
        }
        catch (Exception e)
        {
            _main?.Notify(e.Message, true);
        }
    }

    [RelayCommand]
    private void AbrirTerminal() => _main?.AbrirTerminalEm(Repo.Path);
}

/// <summary>
/// Um grupo dentro do painel. No painel geral cada grupo vira uma seção com seu próprio
/// título; no painel de um grupo só existe uma, e o título seria repetir o cabeçalho.
/// </summary>
public sealed partial class SecaoPainelViewModel : ObservableObject
{
    public string GrupoId { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Cor { get; init; } = "#5D6675";
    public bool MostraTitulo { get; init; }

    public ObservableCollection<CartaoRepoViewModel> Cartoes { get; init; } = new();

    public string Contagem => Cartoes.Count.ToString();

    /// <summary>
    /// Seção fechada, como o grupo recolhido na árvore — e sincronizada com ela. Só o
    /// painel geral tem título para clicar; o de um grupo nunca recolhe.
    /// </summary>
    [ObservableProperty] private bool _recolhido;

    public bool MostraCartoes => !Recolhido;

    /// <summary>Mesmas setas da árvore: para a direita fechado, para baixo aberto.</summary>
    public string Chevron => Recolhido ? "" : "";

    /// <summary>Recolhida, a seção ainda diz se tem algo pedindo atenção lá dentro.</summary>
    public string Pendencias
    {
        get
        {
            var partes = new List<string>();
            var sujos = Cartoes.Count(c => c.MostraSujo || c.MostraConflito);
            var enviar = Cartoes.Count(c => c.MostraAhead);
            var receber = Cartoes.Count(c => c.MostraBehind);
            var quebrados = Cartoes.Count(c => c.CiSituacao == "falha");

            if (sujos > 0) partes.Add($"{sujos} com alterações");
            if (enviar > 0) partes.Add($"{enviar} a enviar");
            if (receber > 0) partes.Add($"{receber} a receber");
            if (quebrados > 0) partes.Add($"{quebrados} com esteira quebrada");

            return string.Join(" · ", partes);
        }
    }

    public bool MostraPendencias => Recolhido && Pendencias.Length > 0;

    /// <summary>Quem leva a mudança para a árvore e o workspace (o MainViewModel).</summary>
    public Action<SecaoPainelViewModel>? AoAlternar { get; init; }

    partial void OnRecolhidoChanged(bool value)
    {
        foreach (var p in new[] { nameof(MostraCartoes), nameof(Chevron), nameof(MostraPendencias) })
            OnPropertyChanged(p);

        // cartão aberto dentro de seção fechada continuaria consultando a esteira à toa
        if (value)
            foreach (var c in Cartoes) c.Expandido = false;
    }

    /// <summary>Status ou esteira mudaram: o resumo da seção fechada acompanha.</summary>
    public void AtualizarPendencias()
    {
        OnPropertyChanged(nameof(Pendencias));
        OnPropertyChanged(nameof(MostraPendencias));
    }

    [RelayCommand]
    private void Alternar()
    {
        if (!MostraTitulo) return;

        Recolhido = !Recolhido;
        AoAlternar?.Invoke(this);
    }
}

/// <summary>
/// Painel com os repositórios de um grupo — ou de todos. É o que aparece ao clicar num
/// grupo, no lugar das abas de um repositório que não está mais selecionado.
/// </summary>
public sealed partial class PainelViewModel : ObservableObject
{
    private readonly MainViewModel? _main;

    public PainelViewModel(string titulo, string subtitulo, IEnumerable<CartaoRepoViewModel> cartoes,
        MainViewModel? main = null)
    {
        _main = main;
        Titulo = titulo;
        Subtitulo = subtitulo;
        Cartoes = new ObservableCollection<CartaoRepoViewModel>(cartoes);

        // seção única por padrão; quem sabe os grupos (o MainViewModel) substitui depois.
        // Sem isto, um painel montado direto ficaria sem nada na tela.
        Secoes = new ObservableCollection<SecaoPainelViewModel>
        {
            new() { Cartoes = new ObservableCollection<CartaoRepoViewModel>(Cartoes) },
        };
    }

    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string _subtitulo = "";
    [ObservableProperty] private ObservableCollection<CartaoRepoViewModel> _cartoes = new();

    /// <summary>
    /// Os mesmos cartões, repartidos por grupo. A lista achatada continua existindo
    /// porque resumo e atualização olham o painel inteiro, não seção por seção.
    /// </summary>
    [ObservableProperty] private ObservableCollection<SecaoPainelViewModel> _secoes = new();

    /// <summary>Cartão de perfil; só o painel geral tem um, os de grupo não.</summary>
    [ObservableProperty] private PerfilViewModel? _perfil;

    public bool TemPerfil => Perfil is not null;

    partial void OnPerfilChanged(PerfilViewModel? value) => OnPropertyChanged(nameof(TemPerfil));

    public bool Vazio => Cartoes.Count == 0;

    /// <summary>Com um repositório só não há lote: valem os botões dele.</summary>
    public bool PodeLote => _main is not null && Cartoes.Count > 1;

    /// <summary>A mesma operação em todos os repositórios deste painel.</summary>
    [RelayCommand]
    private Task EmLote() => _main is null
        ? Task.CompletedTask
        : _main.AbrirLoteAsync(Titulo, Cartoes.Select(c => c.Repo).ToList());

    /// <summary>Painel saiu de cena: nenhum cartão aberto continua consultando a API.</summary>
    public void PararEsteiras()
    {
        foreach (var c in Cartoes) c.PararEsteira();
    }

    public string Resumo
    {
        get
        {
            if (Vazio) return "";

            var partes = new List<string>();
            var sujos = Cartoes.Count(c => c.MostraSujo || c.MostraConflito);
            var enviar = Cartoes.Count(c => c.MostraAhead);
            var receber = Cartoes.Count(c => c.MostraBehind);
            var quebrados = Cartoes.Count(c => c.CiSituacao == "falha");
            var comPr = Cartoes.Count(c => c.PrsAbertos > 0);

            partes.Add($"{Cartoes.Count} repositório(s)");
            if (sujos > 0) partes.Add($"{sujos} com alterações");
            if (enviar > 0) partes.Add($"{enviar} a enviar");
            if (receber > 0) partes.Add($"{receber} a receber");
            if (quebrados > 0) partes.Add($"{quebrados} com esteira quebrada");
            if (comPr > 0) partes.Add($"{comPr} com PR aberto");
            if (partes.Count == 1) partes.Add("tudo em dia");

            return string.Join(" · ", partes);
        }
    }

    /// <summary>
    /// Consulta a esteira de cada repositório em paralelo. Falha de um não atrapalha os
    /// outros, e quem não tem Actions termina como "sem esteira", não como erro.
    /// </summary>
    public async Task CarregarEsteirasAsync()
    {
        // em fila curta: cem repositórios de uma vez eram cem processos do git e cem
        // consultas à API no mesmo instante, e a tela ficava presa esperando por eles
        using var vagas = new System.Threading.SemaphoreSlim(6);
        await Task.WhenAll(Cartoes.Select(async cartao =>
        {
            await vagas.WaitAsync();
            try { await CarregarEsteiraAsync(cartao); }
            finally { vagas.Release(); }
        }));
        OnPropertyChanged(nameof(Resumo));
        AtualizarPendencias();
    }

    public void AtualizarPendencias()
    {
        foreach (var s in Secoes) s.AtualizarPendencias();
    }

    private static async Task CarregarEsteiraAsync(CartaoRepoViewModel cartao)
    {
        try
        {
            var remoto = await GitService.RemoteUrlAsync(cartao.Repo.Path);
            var slug = GitHubService.Slug(remoto);
            if (slug is null)
            {
                cartao.CiSituacao = "nenhum";
                return;
            }

            cartao.Slug = slug;
            cartao.Usuario = GitHubService.ContaDoRepositorio(cartao.Repo.Conta, remoto);

            var run = await GitHubService.UltimaExecucaoAsync(
                slug, cartao.Status?.Branch ?? "", cartao.Usuario);

            cartao.CiDetalhe = run.Detalhe;
            cartao.CiWorkflow = run.Workflow;
            cartao.CiUrl = run.Url;
            cartao.CiSituacao = run.Situacao; // por último: é ele que reavisa a tela

            await CarregarPrsAsync(cartao);
        }
        catch (Exception)
        {
            cartao.CiSituacao = "nenhum";
            cartao.PrConsultado = true;
        }
    }

    /// <summary>
    /// PRs do repositório. Falha aqui é silenciosa e separada da esteira: repositório
    /// sem PR nenhum é o caso normal, não um problema a relatar.
    /// </summary>
    private static async Task CarregarPrsAsync(CartaoRepoViewModel cartao)
    {
        try
        {
            var prs = await GitHubService.PullRequestsAsync(cartao.Slug, cartao.Usuario);

            cartao.PrsAbertos = prs.Count(p => p.Aberto);

            var ultimo = prs.FirstOrDefault();
            cartao.PrUltimo = ultimo is null
                ? ""
                : $"#{ultimo.Numero} {ultimo.Titulo} ({ultimo.Estado})";
        }
        catch (Exception)
        {
            // sem permissão ou fora da cota: o cartão só não mostra PR
        }
        finally
        {
            cartao.PrConsultado = true;
        }
    }

    [RelayCommand]
    private async Task AtualizarAsync()
    {
        if (_main is null) return;

        GitHubService.LimparCache();
        await _main.RefreshAllAsync();
        _main.AtualizarCartoesDoPainel();
        await CarregarEsteirasAsync();
    }
}
