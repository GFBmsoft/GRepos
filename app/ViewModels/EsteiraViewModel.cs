using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Cores e símbolos de situação, iguais em cartão, job e passo.</summary>
public static class CiVisual
{
    public static string Cor(string situacao) => situacao switch
    {
        "sucesso" => "Green",
        "falha" => "Red",
        "rodando" => "Yellow",
        "cancelado" => "TextDim",
        _ => "TextDim",
    };

    public static string Simbolo(string situacao) => situacao switch
    {
        "sucesso" => "✓",
        "falha" => "✕",
        "rodando" => "●",
        "cancelado" => "—",
        _ => "·",
    };

    public static string Texto(string situacao) => situacao switch
    {
        "sucesso" => "passou",
        "falha" => "quebrou",
        "rodando" => "rodando",
        "cancelado" => "cancelada",
        _ => "sem informação",
    };
}

public sealed class CiEtapaViewModel
{
    public CiEtapa Etapa { get; init; } = new();

    public string Nome => $"{Etapa.Numero}. {Etapa.Nome}";
    public string Simbolo => CiVisual.Simbolo(Etapa.Situacao);
    public string Cor => CiVisual.Cor(Etapa.Situacao);
    public string DuracaoTexto => Rotulos.Duracao(Etapa.Duracao);
    public bool Quebrou => Etapa.Situacao == "falha";

    /// <summary>O passo que quebrou vem em negrito: é o que se procura ao abrir a janela.</summary>
    public string Peso => Quebrou ? "SemiBold" : "Normal";
}

public sealed class CiJobViewModel
{
    public CiJob Job { get; init; } = new();

    public string Nome => Job.Nome;
    public string Simbolo => CiVisual.Simbolo(Job.Situacao);
    public string Cor => CiVisual.Cor(Job.Situacao);
    public string DuracaoTexto => Rotulos.Duracao(Job.Duracao);

    public ObservableCollection<CiEtapaViewModel> Etapas { get; init; } = new();
}

/// <summary>Cartão de uma execução na coluna da esquerda.</summary>
public sealed class CiExecucaoViewModel
{
    public CiExecucao Execucao { get; init; } = new();

    public string Workflow => Execucao.Workflow.Length > 0 ? Execucao.Workflow : "Workflow";
    public string Titulo => Execucao.Titulo.Length > 0 ? Execucao.Titulo : "(sem título)";
    public string Numero => Execucao.Numero > 0 ? "#" + Execucao.Numero : "";
    public string Simbolo => CiVisual.Simbolo(Execucao.Situacao);
    public string Cor => CiVisual.Cor(Execucao.Situacao);
    public bool Rodando => Execucao.Situacao == "rodando";

    public string Rodape
    {
        get
        {
            var partes = new System.Collections.Generic.List<string> { CiVisual.Texto(Execucao.Situacao) };
            if (Execucao.Branch.Length > 0) partes.Add(Execucao.Branch);
            if (Execucao.Autor.Length > 0) partes.Add(Execucao.Autor);

            var quando = Rotulos.Quando(Execucao.Criada);
            if (quando.Length > 0) partes.Add(quando);
            return string.Join(" · ", partes);
        }
    }

    public string Tooltip => $"{Workflow} {Numero}\n{Titulo}\n{Rodape}";
}

/// <summary>
/// Janela da esteira: os cartões das últimas execuções à esquerda e, para o cartão
/// escolhido, o passo a passo de cada job à direita.
/// </summary>
public sealed partial class EsteiraViewModel : ObservableObject
{
    private readonly string _slug;
    private readonly string _branch;
    private readonly string _usuario;

    public EsteiraViewModel(string slug, string branch, string usuario, string repoNome = "")
    {
        _slug = slug;
        _branch = branch;
        _usuario = usuario;
        Title = repoNome.Length > 0 ? $"Esteira — {repoNome}" : "Esteira";
        Subtitulo = branch.Length > 0 ? $"{slug} · branch {branch}" : slug;
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitulo = "";
    [ObservableProperty] private ObservableCollection<CiExecucaoViewModel> _execucoes = new();
    [ObservableProperty] private ObservableCollection<CiJobViewModel> _jobs = new();
    [ObservableProperty] private CiExecucaoViewModel? _selecionada;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private bool _carregandoJobs;
    [ObservableProperty] private string _erro = "";

    public bool TemErro => Erro.Length > 0;
    public bool SemExecucoes => !Carregando && Erro.Length == 0 && Execucoes.Count == 0;
    public bool SemSelecao => Selecionada is null;
    public bool SemJobs => !CarregandoJobs && Selecionada is not null && Jobs.Count == 0;

    partial void OnErroChanged(string value)
    {
        OnPropertyChanged(nameof(TemErro));
        OnPropertyChanged(nameof(SemExecucoes));
    }

    partial void OnCarregandoChanged(bool value) => OnPropertyChanged(nameof(SemExecucoes));

    partial void OnCarregandoJobsChanged(bool value) => OnPropertyChanged(nameof(SemJobs));

    partial void OnSelecionadaChanged(CiExecucaoViewModel? value)
    {
        OnPropertyChanged(nameof(SemSelecao));
        OnPropertyChanged(nameof(SemJobs));
        if (value is not null) _ = CarregarJobsAsync(value);
    }

    [RelayCommand]
    public async Task CarregarAsync()
    {
        if (Carregando) return;

        Carregando = true;
        Erro = "";
        try
        {
            var lista = await GitHubService.ExecucoesAsync(_slug, _branch, _usuario);
            var anterior = Selecionada?.Execucao.Id;

            Execucoes = new ObservableCollection<CiExecucaoViewModel>(
                lista.Select(e => new CiExecucaoViewModel { Execucao = e }));

            // mantém o cartão aberto entre atualizações; só cai no primeiro se ele sumiu
            Selecionada = Execucoes.FirstOrDefault(c => c.Execucao.Id == anterior)
                          ?? Execucoes.FirstOrDefault();
        }
        catch (Exception e)
        {
            Erro = e.Message;
            Execucoes = new ObservableCollection<CiExecucaoViewModel>();
            Jobs = new ObservableCollection<CiJobViewModel>();
        }
        finally
        {
            Carregando = false;
            OnPropertyChanged(nameof(SemExecucoes));
        }
    }

    private async Task CarregarJobsAsync(CiExecucaoViewModel cartao)
    {
        CarregandoJobs = true;
        Jobs = new ObservableCollection<CiJobViewModel>();
        try
        {
            var jobs = await GitHubService.JobsAsync(_slug, cartao.Execucao.Id, _usuario);
            if (Selecionada?.Execucao.Id != cartao.Execucao.Id) return; // trocou de cartão no meio

            Jobs = new ObservableCollection<CiJobViewModel>(jobs.Select(j => new CiJobViewModel
            {
                Job = j,
                Etapas = new ObservableCollection<CiEtapaViewModel>(
                    j.Etapas.Select(s => new CiEtapaViewModel { Etapa = s })),
            }));
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            CarregandoJobs = false;
            OnPropertyChanged(nameof(SemJobs));
        }
    }

    /// <summary>Abre no navegador a execução escolhida — ou a página de Actions.</summary>
    [RelayCommand]
    private void AbrirNoGitHub()
    {
        try
        {
            var url = Selecionada?.Execucao.Url;
            ShellService.AbrirUrl(string.IsNullOrEmpty(url) ? $"https://github.com/{_slug}/actions" : url);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }
}
