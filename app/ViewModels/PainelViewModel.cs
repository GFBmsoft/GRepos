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
    public string Slug { get; set; } = "";
    public string Usuario { get; set; } = "";

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

    [RelayCommand]
    private void AbrirPrs()
    {
        try
        {
            if (Slug.Length > 0) ShellService.AbrirUrl($"https://github.com/{Slug}/pulls");
        }
        catch (Exception e)
        {
            _main?.Notify(e.Message, true);
        }
    }

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
    }

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
    }

    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string _subtitulo = "";
    [ObservableProperty] private ObservableCollection<CartaoRepoViewModel> _cartoes = new();

    /// <summary>Cartão de perfil; só o painel geral tem um, os de grupo não.</summary>
    [ObservableProperty] private PerfilViewModel? _perfil;

    public bool TemPerfil => Perfil is not null;

    partial void OnPerfilChanged(PerfilViewModel? value) => OnPropertyChanged(nameof(TemPerfil));

    public bool Vazio => Cartoes.Count == 0;

    /// <summary>Espaço entre cartões; entra na conta da largura.</summary>
    private const double Vao = 10;

    /// <summary>Abaixo disso o cartão fica ilegível, e é melhor ter menos colunas.</summary>
    private const double LarguraMinima = 250;

    /// <summary>Largura útil do painel, informada pela View a cada redimensionamento.</summary>
    [ObservableProperty] private double _larguraDisponivel;

    /// <summary>
    /// Largura de cada cartão: as colunas que couberem, divididas por igual. Com largura
    /// fixa sobrava um vão à direita e a fileira não alinhava com o cartão de perfil,
    /// que ocupa a linha inteira.
    /// </summary>
    public double LarguraDoCartao
    {
        get
        {
            if (LarguraDisponivel <= 0) return LarguraMinima;

            var colunas = Math.Max(1, (int)((LarguraDisponivel + Vao) / (LarguraMinima + Vao)));
            return (LarguraDisponivel - (colunas - 1) * Vao) / colunas;
        }
    }

    partial void OnLarguraDisponivelChanged(double value) => OnPropertyChanged(nameof(LarguraDoCartao));

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
        await Task.WhenAll(Cartoes.Select(CarregarEsteiraAsync));
        OnPropertyChanged(nameof(Resumo));
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
            cartao.Usuario = GitHubService.Usuario(remoto);

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
