using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
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

public sealed partial class CiEtapaViewModel : ObservableObject
{
    [ObservableProperty] private CiEtapa _etapa = new();

    public string Nome => $"{Etapa.Numero}. {Etapa.Nome}";
    public string Simbolo => CiVisual.Simbolo(Etapa.Situacao);
    public string Cor => CiVisual.Cor(Etapa.Situacao);
    public string DuracaoTexto => Rotulos.Duracao(Etapa.Duracao);
    public bool Quebrou => Etapa.Situacao == "falha";

    /// <summary>O passo que quebrou vem em negrito: é o que se procura ao abrir a janela.</summary>
    public string Peso => Quebrou ? "SemiBold" : "Normal";

    partial void OnEtapaChanged(CiEtapa value)
    {
        foreach (var p in new[] { nameof(Nome), nameof(Simbolo), nameof(Cor), nameof(DuracaoTexto),
                                  nameof(Quebrou), nameof(Peso) })
            OnPropertyChanged(p);
    }
}

public sealed partial class CiJobViewModel : ObservableObject
{
    [ObservableProperty] private CiJob _job = new();

    public string Nome => Job.Nome;
    public string Simbolo => CiVisual.Simbolo(Job.Situacao);
    public string Cor => CiVisual.Cor(Job.Situacao);
    public string DuracaoTexto => Rotulos.Duracao(Job.Duracao);

    public ObservableCollection<CiEtapaViewModel> Etapas { get; } = new();

    partial void OnJobChanged(CiJob value)
    {
        foreach (var p in new[] { nameof(Nome), nameof(Simbolo), nameof(Cor), nameof(DuracaoTexto) })
            OnPropertyChanged(p);

        SincronizarEtapas(value);
    }

    public void SincronizarEtapas(CiJob job) => ListaSync.AplicarModelos(
        Etapas, job.Etapas,
        item => item.Etapa.Numero.ToString(),
        modelo => modelo.Numero.ToString(),
        (item, modelo) => item.Etapa = modelo,
        modelo => new CiEtapaViewModel { Etapa = modelo });
}

/// <summary>Cartão de uma execução na coluna da esquerda.</summary>
public sealed partial class CiExecucaoViewModel : ObservableObject
{
    [ObservableProperty] private CiExecucao _execucao = new();

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
            var partes = new List<string> { CiVisual.Texto(Execucao.Situacao) };
            if (Execucao.Branch.Length > 0) partes.Add(Execucao.Branch);
            if (Execucao.Autor.Length > 0) partes.Add(Execucao.Autor);

            var quando = Rotulos.Quando(Execucao.Criada);
            if (quando.Length > 0) partes.Add(quando);
            return string.Join(" · ", partes);
        }
    }

    public string Tooltip => $"{Workflow} {Numero}\n{Titulo}\n{Rodape}";

    partial void OnExecucaoChanged(CiExecucao value)
    {
        foreach (var p in new[] { nameof(Workflow), nameof(Titulo), nameof(Numero), nameof(Simbolo),
                                  nameof(Cor), nameof(Rodando), nameof(Rodape), nameof(Tooltip) })
            OnPropertyChanged(p);
    }
}

/// <summary>
/// Janela da esteira: os cartões das últimas execuções à esquerda e, para o cartão
/// escolhido, o passo a passo de cada job à direita.
///
/// A tela se atualiza sozinha enquanto está aberta — acompanhar um build sem ficar
/// clicando em Atualizar é o motivo de ela existir. As listas são atualizadas **no
/// lugar**: trocar as coleções recriaria as linhas a cada ciclo e roubaria a rolagem
/// e o cartão selecionado bem na hora em que o usuário está lendo.
/// </summary>
public sealed partial class EsteiraViewModel : ObservableObject
{
    /// <summary>Ritmo com algo rodando: é quando o passo a passo muda de verdade.</summary>
    public static readonly TimeSpan IntervaloRodando = TimeSpan.FromSeconds(8);

    /// <summary>Ritmo com tudo parado: serve só para notar uma execução nova começando.</summary>
    public static readonly TimeSpan IntervaloParado = TimeSpan.FromSeconds(30);

    private readonly string _slug;
    private readonly string _branch;
    private readonly string _usuario;
    private readonly IDialogService? _dialogs;
    private readonly DispatcherTimer _timer;

    private bool _ocupado;
    private long _jobsCarregadosDe;

    public EsteiraViewModel(string slug, string branch, string usuario, string repoNome = "",
        IDialogService? dialogs = null, int visiveis = 6)
    {
        Visiveis = Math.Max(1, visiveis);
        _slug = slug;
        _branch = branch;
        _usuario = usuario;
        _dialogs = dialogs;
        Referencia = branch;
        Title = repoNome.Length > 0 ? $"Esteira — {repoNome}" : "Esteira";
        Subtitulo = slug;

        _timer = new DispatcherTimer { Interval = IntervaloParado };
        _timer.Tick += (_, _) => _ = AtualizarAsync();
    }

    /// <summary>
    /// Filtrar pela branch esconde justamente os builds de tag: o GitHub põe o nome da
    /// tag no head_branch dessas execuções, então um build da 1.0.0.10 não é "main".
    /// Por isso o padrão é mostrar tudo, e a branch vem escrita em cada cartão.
    /// </summary>
    [ObservableProperty] private bool _somenteBranch;

    public bool PodeFiltrarPorBranch => _branch.Length > 0;

    public string FiltroTexto => _branch.Length > 0
        ? $"só a branch {_branch}"
        : "só a branch atual";

    partial void OnSomenteBranchChanged(bool value) => _ = CarregarAsync();

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

    // -------------------------------------------------- filtro e recolhimento

    /// <summary>Quantas execuções aparecem antes de recolher o resto.</summary>
    public int Visiveis { get; }

    /// <summary>Todas as execuções trazidas, antes de filtrar e recortar.</summary>
    private IReadOnlyList<CiExecucao> _todas = Array.Empty<CiExecucao>();

    /// <summary>"" mostra tudo; "sucesso", "falha" ou "rodando" restringem.</summary>
    [ObservableProperty] private string _filtro = "";

    [ObservableProperty] private bool _expandido;

    public int TotalTodas => _todas.Count;
    public int TotalSucesso => _todas.Count(e => e.Situacao == "sucesso");
    public int TotalFalha => _todas.Count(e => e.Situacao == "falha");
    public int TotalRodando => _todas.Count(e => e.Situacao == "rodando");

    public string RotuloTodas => $"todas {TotalTodas}";
    public string RotuloSucesso => $"✓ {TotalSucesso}";
    public string RotuloFalha => $"✕ {TotalFalha}";
    public string RotuloRodando => $"● {TotalRodando}";

    /// <summary>Quantas ficaram de fora do recorte; zero esconde o botão de expandir.</summary>
    [ObservableProperty] private int _recolhidas;

    public bool TemRecolhidas => Recolhidas > 0;

    public string TextoRecolhidas => Expandido
        ? "mostrar menos"
        : $"mostrar mais {Recolhidas}";

    partial void OnRecolhidasChanged(int value)
    {
        OnPropertyChanged(nameof(TemRecolhidas));
        OnPropertyChanged(nameof(TextoRecolhidas));
    }

    partial void OnFiltroChanged(string value) => AplicarFiltro();

    partial void OnExpandidoChanged(bool value)
    {
        OnPropertyChanged(nameof(TextoRecolhidas));
        AplicarFiltro();
    }

    [RelayCommand]
    private void Filtrar(string? situacao) => Filtro = Filtro == situacao ? "" : situacao ?? "";

    [RelayCommand]
    private void AlternarRecolhidas() => Expandido = !Expandido;

    /// <summary>
    /// Recorta o que vai para a tela. A lista é atualizada no lugar mesmo aqui: trocar a
    /// coleção perderia o cartão selecionado a cada ciclo automático.
    /// </summary>
    private void AplicarFiltro()
    {
        var filtradas = Filtro.Length == 0
            ? _todas
            : _todas.Where(e => e.Situacao == Filtro).ToList();

        var mostrar = Expandido ? filtradas : filtradas.Take(Visiveis).ToList();
        Recolhidas = filtradas.Count - mostrar.Count;

        ListaSync.AplicarModelos(
            Execucoes, mostrar,
            item => item.Execucao.Id.ToString(),
            modelo => modelo.Id.ToString(),
            (item, modelo) => item.Execucao = modelo,
            modelo => new CiExecucaoViewModel { Execucao = modelo });

        foreach (var p in new[] { nameof(TotalTodas), nameof(TotalSucesso), nameof(TotalFalha),
                                  nameof(TotalRodando), nameof(RotuloTodas), nameof(RotuloSucesso),
                                  nameof(RotuloFalha), nameof(RotuloRodando), nameof(SemExecucoes) })
            OnPropertyChanged(p);

        // o cartão aberto pode ter saído pelo filtro
        if (Selecionada is not null && !Execucoes.Contains(Selecionada)) Selecionada = null;
        Selecionada ??= Execucoes.FirstOrDefault();
    }

    /// <summary>Recado do rodapé, para a atualização sozinha não parecer mágica.</summary>
    public string RitmoTexto => _timer.IsEnabled
        ? $"atualiza sozinha a cada {(int)_timer.Interval.TotalSeconds}s"
        : "";

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
        AvisarBotoes();

        // só recarrega ao trocar de cartão; a atualização periódica cuida do resto
        if (value is not null && value.Execucao.Id != _jobsCarregadosDe)
            _ = CarregarJobsAsync(value);
    }

    // ------------------------------------------------ disparar e reexecutar

    [ObservableProperty] private ObservableCollection<GitHubService.Workflow> _workflows = new();
    [ObservableProperty] private GitHubService.Workflow? _workflowEscolhido;
    [ObservableProperty] private string _referencia = "";
    [ObservableProperty] private bool _executando;

    public bool PodeDisparar => Workflows.Count > 0 && !Executando;

    /// <summary>Reexecutar só faz sentido numa execução que terminou.</summary>
    public bool PodeReexecutar => Selecionada is { Rodando: false } && !Executando;

    /// <summary>E "só as que falharam" só numa execução que de fato falhou.</summary>
    public bool PodeReexecutarFalhas =>
        PodeReexecutar && Selecionada?.Execucao.Situacao == "falha";

    partial void OnExecutandoChanged(bool value) => AvisarBotoes();

    private void AvisarBotoes()
    {
        foreach (var p in new[] { nameof(PodeDisparar), nameof(PodeReexecutar), nameof(PodeReexecutarFalhas) })
            OnPropertyChanged(p);
    }

    private async Task CarregarWorkflowsAsync()
    {
        try
        {
            var lista = await GitHubService.WorkflowsAsync(_slug, _usuario);
            Workflows = new ObservableCollection<GitHubService.Workflow>(lista.Where(w => w.Ativo));
            WorkflowEscolhido ??= Workflows.FirstOrDefault();
            AvisarBotoes();
        }
        catch (Exception)
        {
            // sem permissão de leitura de workflows a tela segue: só não dispara nada
        }
    }

    /// <summary>
    /// Dispara o workflow escolhido. Confirma antes: é uma ação que **escreve** no
    /// repositório e gasta minutos de execução — nada disso deve sair de um clique só.
    /// </summary>
    [RelayCommand]
    private async Task DispararAsync()
    {
        if (Executando || WorkflowEscolhido is null) return;

        var alvo = Referencia.Trim();
        if (alvo.Length == 0)
        {
            Erro = "Informe a branch ou tag para executar.";
            return;
        }

        if (_dialogs is not null && !await _dialogs.ConfirmAsync(
                "Executar workflow",
                $"Executar \"{WorkflowEscolhido.Nome}\" em {alvo}, no repositório {_slug}?\n\n" +
                "Isso inicia uma execução de verdade no GitHub Actions."))
            return;

        Executando = true;
        Erro = "";
        try
        {
            await GitHubService.DispararWorkflowAsync(_slug, WorkflowEscolhido.Id, alvo, _usuario);

            // o GitHub leva alguns segundos para a execução aparecer na listagem
            await Task.Delay(TimeSpan.FromSeconds(3));
            await CarregarAsync(silencioso: true);
            AjustarRitmo();
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Executando = false;
        }
    }

    [RelayCommand]
    private Task ReexecutarAsync() => ReexecutarInternoAsync(somenteFalhas: false);

    [RelayCommand]
    private Task ReexecutarFalhasAsync() => ReexecutarInternoAsync(somenteFalhas: true);

    private async Task ReexecutarInternoAsync(bool somenteFalhas)
    {
        var cartao = Selecionada;
        if (Executando || cartao is null) return;

        var oque = somenteFalhas ? "os jobs que falharam" : "a execução inteira";
        if (_dialogs is not null && !await _dialogs.ConfirmAsync(
                "Reexecutar",
                $"Reexecutar {oque} de \"{cartao.Workflow} {cartao.Numero}\"?"))
            return;

        Executando = true;
        Erro = "";
        try
        {
            await GitHubService.ReexecutarAsync(_slug, cartao.Execucao.Id, _usuario, somenteFalhas);

            await Task.Delay(TimeSpan.FromSeconds(3));
            await CarregarAsync(silencioso: true);
            AjustarRitmo();
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Executando = false;
        }
    }

    /// <summary>Começa a acompanhar. A janela chama ao abrir.</summary>
    public async Task IniciarAsync()
    {
        await CarregarAsync();
        _ = CarregarWorkflowsAsync();
        AjustarRitmo();
        _timer.Start();
        OnPropertyChanged(nameof(RitmoTexto));
    }

    /// <summary>Para de consultar a API. A janela chama ao fechar.</summary>
    public void Parar()
    {
        _timer.Stop();
        OnPropertyChanged(nameof(RitmoTexto));
    }

    /// <summary>
    /// Rápido com algo rodando, lento com tudo parado. Sem isso seria escolher entre
    /// gastar cota da API à toa ou demorar a mostrar o passo que acabou de rodar.
    /// </summary>
    private void AjustarRitmo()
    {
        var rodando = _todas.Any(e => e.Situacao == "rodando");
        var alvo = rodando ? IntervaloRodando : IntervaloParado;

        if (_timer.Interval == alvo) return;
        _timer.Interval = alvo;
        OnPropertyChanged(nameof(RitmoTexto));
    }

    /// <summary>Ciclo automático: sem "Carregando…" na tela e sem pisar no ciclo anterior.</summary>
    private async Task AtualizarAsync()
    {
        if (_ocupado) return;

        _ocupado = true;
        try
        {
            await CarregarAsync(silencioso: true);
            if (Selecionada is not null) await CarregarJobsAsync(Selecionada, silencioso: true);
            AjustarRitmo();
        }
        finally
        {
            _ocupado = false;
        }
    }

    [RelayCommand]
    public async Task CarregarAsync() => await CarregarAsync(silencioso: false);

    private async Task CarregarAsync(bool silencioso)
    {
        if (!silencioso)
        {
            if (Carregando) return;
            Carregando = true;
        }

        try
        {
            // busca mais do que cabe na tela: o recorte e os contadores por situação
            // precisam enxergar além das que estão visíveis
            var lista = await GitHubService.ExecucoesAsync(
                _slug, SomenteBranch ? _branch : "", _usuario, Math.Max(30, Visiveis * 3));
            Erro = "";

            _todas = lista;
            AplicarFiltro();
            AjustarRitmo();
        }
        catch (Exception e)
        {
            // falha de rede no ciclo automático não apaga o que já está na tela
            if (!silencioso)
            {
                Erro = e.Message;
                Execucoes.Clear();
                Jobs.Clear();
            }
        }
        finally
        {
            if (!silencioso) Carregando = false;
            OnPropertyChanged(nameof(SemExecucoes));
        }
    }

    private async Task CarregarJobsAsync(CiExecucaoViewModel cartao, bool silencioso = false)
    {
        if (!silencioso) CarregandoJobs = true;

        try
        {
            var jobs = await GitHubService.JobsAsync(_slug, cartao.Execucao.Id, _usuario);
            if (Selecionada?.Execucao.Id != cartao.Execucao.Id) return; // trocou de cartão no meio

            ListaSync.AplicarModelos(
                Jobs, jobs,
                item => item.Job.Nome,
                modelo => modelo.Nome,
                (item, modelo) => item.Job = modelo,
                modelo =>
                {
                    var novo = new CiJobViewModel { Job = modelo };
                    novo.SincronizarEtapas(modelo);
                    return novo;
                });

            _jobsCarregadosDe = cartao.Execucao.Id;
        }
        catch (Exception e)
        {
            if (!silencioso) Erro = e.Message;
        }
        finally
        {
            if (!silencioso) CarregandoJobs = false;
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
