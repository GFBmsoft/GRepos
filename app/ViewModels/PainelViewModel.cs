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

    public string Tooltip => TemErro ? $"{Caminho}\n{Erro}" : Caminho;

    partial void OnStatusChanged(RepoStatus? value) => OnPropertyChanged(string.Empty);

    partial void OnCiSituacaoChanged(string value)
    {
        foreach (var p in new[] { nameof(ConsultandoCi), nameof(SemCi), nameof(TemCi),
                                  nameof(CiTexto), nameof(CiCor), nameof(CiSimbolo) })
            OnPropertyChanged(p);
    }

    [RelayCommand]
    private void Abrir() => _main?.SelecionarRepositorio(Repo.Id);

    [RelayCommand]
    private void AbrirNoGitHub()
    {
        try
        {
            if (CiUrl.Length > 0) ShellService.AbrirUrl(CiUrl);
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

    public bool Vazio => Cartoes.Count == 0;

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

            partes.Add($"{Cartoes.Count} repositório(s)");
            if (sujos > 0) partes.Add($"{sujos} com alterações");
            if (enviar > 0) partes.Add($"{enviar} a enviar");
            if (receber > 0) partes.Add($"{receber} a receber");
            if (quebrados > 0) partes.Add($"{quebrados} com esteira quebrada");
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

            var run = await GitHubService.UltimaExecucaoAsync(
                slug, cartao.Status?.Branch ?? "", GitHubService.Usuario(remoto));

            cartao.CiSituacao = run.Situacao;
            cartao.CiDetalhe = run.Detalhe;
            cartao.CiUrl = run.Url;
        }
        catch (Exception)
        {
            cartao.CiSituacao = "nenhum";
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
