using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed class BranchItemViewModel
{
    public Branch Branch { get; init; } = new();

    public string Name => Branch.Name;
    public bool IsHead => Branch.IsHead;
    public bool IsRemote => Branch.IsRemote;
    public string Subject => Branch.Subject;

    public string UpstreamText => Branch.Upstream is { Length: > 0 } u ? u : "";
    public bool HasUpstream => !string.IsNullOrEmpty(Branch.Upstream);

    public bool CanCheckout => !IsHead;

    /// <summary>Branch local que ainda não existe no remoto — trabalho só desta máquina.</summary>
    public bool SoLocal => !IsRemote && !HasUpstream;

    /// <summary>
    /// Verde: a branch atual. Amarelo: existe só aqui, ainda não foi enviada.
    /// Cinza: remota. Texto normal: local rastreando o remoto.
    /// </summary>
    public string NameColor => IsHead ? "Green" : SoLocal ? "Yellow" : IsRemote ? "TextDim" : "Text";

    public string NameWeight => IsHead ? "SemiBold" : "Normal";

    public string Tooltip => Branch.Upstream is { Length: > 0 } u
        ? $"{Name}\nrastreando {u}\n{Subject}"
        : $"{Name}\n{Subject}";

    /// <summary>Nome sem o remoto: é por ele que se agrupa e classifica.</summary>
    public string NomeLocal => GitFlow.SemRemoto(Name, IsRemote);

    /// <summary>Só a parte depois da pasta — a pasta já está no cabeçalho do grupo.</summary>
    public string Curto => GitFlow.Pasta(NomeLocal) is { Length: > 0 } p ? NomeLocal[(p.Length + 1)..] : NomeLocal;

    public TipoBranch Tipo { get; init; }
}

/// <summary>Branches de uma mesma pasta (feat/, fix/...), ou as principais; recolhível.</summary>
public sealed partial class BranchGrupoViewModel : ObservableObject
{
    public string Chave { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Cor { get; init; } = "TextDim";
    public ObservableCollection<BranchItemViewModel> Itens { get; init; } = new();
    public int Total => Itens.Count;
    internal int Ordem { get; init; }

    [ObservableProperty] private bool _recolhido;

    public bool Aberto => !Recolhido;
    public string Chevron => Recolhido ? "" : "";

    partial void OnRecolhidoChanged(bool value)
    {
        OnPropertyChanged(nameof(Aberto));
        OnPropertyChanged(nameof(Chevron));
    }

    [RelayCommand]
    private void Alternar() => Recolhido = !Recolhido;
}

public sealed partial class BranchesViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly MainViewModel _main;
    private Branch[] _todas = Array.Empty<Branch>();

    public BranchesViewModel(Repo repo, MainViewModel main)
    {
        _repo = repo;
        _main = main;
        Title = $"Branches — {repo.Name}";
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private ObservableCollection<BranchItemViewModel> _locais = new();
    [ObservableProperty] private ObservableCollection<BranchItemViewModel> _remotas = new();
    [ObservableProperty] private string _filtro = "";
    [ObservableProperty] private string _novaBranch = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _erro = "";
    [ObservableProperty] private bool _temErro;

    /// <summary>Erro de dono do repositório tem conserto de um clique — por isso o botão.</summary>
    [ObservableProperty] private bool _podeConfiar;

    /// <summary>As mesmas listas, agrupadas por pasta — é o que a tela mostra.</summary>
    [ObservableProperty] private ObservableCollection<BranchGrupoViewModel> _gruposLocais = new();
    [ObservableProperty] private ObservableCollection<BranchGrupoViewModel> _gruposRemotos = new();

    // ------------------------------------------------------------- git-flow

    [ObservableProperty] private GitFlowConfig _fluxo = new();

    /// <summary>Pedidos ao usuário; a janela preenche com os diálogos dela.</summary>
    public Func<string, string, Task<string?>>? PedirTexto { get; set; }
    public Func<string, string, Task<bool>>? Confirmar { get; set; }
    public Func<GitFlowConfig, Task<GitFlowConfig?>>? EditarFluxo { get; set; }

    public string BranchAtual => _todas.FirstOrDefault(b => b.IsHead && !b.IsRemote)?.Name ?? "";

    public bool PodeFinalizar => GitFlow.PassosFinalizar(BranchAtual, Fluxo).Count > 0;
    public string FinalizarRotulo => PodeFinalizar ? $"Finalizar {BranchAtual}" : "Finalizar (troque para uma feat/ ou fix/)";
    public string ConfigurarRotulo => Fluxo.Inicializado ? "Configurar git-flow…" : "Inicializar git-flow…";
    public string NovaFeatureRotulo => $"Nova feature ({Fluxo.Feature}) a partir da {Fluxo.Develop}…";
    public string NovoFixRotulo => $"Novo fix ({Fluxo.Hotfix}) a partir da {Fluxo.Master}…";
    public string NovaReleaseRotulo => $"Nova release ({Fluxo.Release}) a partir da {Fluxo.Develop}…";

    partial void OnFluxoChanged(GitFlowConfig value)
    {
        foreach (var p in new[] { nameof(PodeFinalizar), nameof(FinalizarRotulo), nameof(ConfigurarRotulo),
                     nameof(NovaFeatureRotulo), nameof(NovoFixRotulo), nameof(NovaReleaseRotulo) })
            OnPropertyChanged(p);
    }

    public bool SemLocais => Locais.Count == 0;
    public bool SemRemotas => Remotas.Count == 0;
    public int TotalLocais => Locais.Count;
    public int TotalRemotas => Remotas.Count;
    public bool PodeCriar => !Busy && !string.IsNullOrWhiteSpace(NovaBranch);

    partial void OnFiltroChanged(string value) => Aplicar();
    partial void OnNovaBranchChanged(string value) => OnPropertyChanged(nameof(PodeCriar));
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(PodeCriar));

    public async Task CarregarAsync()
    {
        Busy = true;
        try
        {
            _todas = (await GitService.BranchesAsync(_repo.Path)).ToArray();
            Fluxo = await GitFlow.LerAsync(_repo.Path);
            OnFluxoChanged(Fluxo); // a branch atual pode ter mudado com o mesmo fluxo
            TemErro = false;
            PodeConfiar = false;
            Erro = "";
            Aplicar();
        }
        catch (Exception e)
        {
            _todas = Array.Empty<Branch>();
            Aplicar();
            MostrarErro(e.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private void MostrarErro(string mensagem)
    {
        TemErro = true;

        // o git recusa repositórios de outro dono; a saída dele é longa e técnica
        if (mensagem.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase) ||
            mensagem.Contains("safe.directory", StringComparison.OrdinalIgnoreCase))
        {
            PodeConfiar = true;
            Erro = "Este repositório pertence a outro usuário do Windows, e o git bloqueia o acesso " +
                   "por segurança. Se a pasta é sua, marque-a como confiável.";
            return;
        }

        PodeConfiar = false;
        Erro = mensagem;
    }

    private void Aplicar()
    {
        var q = Filtro.Trim();

        bool Combina(Branch b) =>
            q.Length == 0 ||
            b.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            b.Subject.Contains(q, StringComparison.OrdinalIgnoreCase);

        BranchItemViewModel Item(Branch b) =>
            new() { Branch = b, Tipo = GitFlow.Classificar(GitFlow.SemRemoto(b.Name, b.IsRemote), Fluxo) };

        Locais = new ObservableCollection<BranchItemViewModel>(
            _todas.Where(b => !b.IsRemote && Combina(b))
                  .OrderByDescending(b => b.IsHead)
                  .ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                  .Select(Item));

        Remotas = new ObservableCollection<BranchItemViewModel>(
            _todas.Where(b => b.IsRemote && Combina(b))
                  .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                  .Select(Item));

        GruposLocais = Agrupar(Locais, GruposLocais);
        GruposRemotos = Agrupar(Remotas, GruposRemotos);

        foreach (var p in new[] { nameof(SemLocais), nameof(SemRemotas), nameof(TotalLocais), nameof(TotalRemotas) })
            OnPropertyChanged(p);
    }

    /// <summary>
    /// Um grupo por pasta (feat, fix...); sem pasta, master/main e develop ficam juntas
    /// em "principais" e o resto em "sem pasta". Quem estava recolhido continua recolhido
    /// depois de recarregar ou filtrar.
    /// </summary>
    public static ObservableCollection<BranchGrupoViewModel> Agrupar(
        System.Collections.Generic.IEnumerable<BranchItemViewModel> itens,
        System.Collections.Generic.IEnumerable<BranchGrupoViewModel>? anteriores = null)
    {
        var recolhidos = (anteriores ?? Array.Empty<BranchGrupoViewModel>())
            .Where(g => g.Recolhido).Select(g => g.Chave).ToHashSet(StringComparer.OrdinalIgnoreCase);

        string Chave(BranchItemViewModel i) =>
            GitFlow.Pasta(i.NomeLocal) is { Length: > 0 } p ? p
            : i.Tipo is TipoBranch.Principal or TipoBranch.Develop ? "" : "~";

        var grupos = itens
            .GroupBy(Chave, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                // o tipo do grupo é o do item mais "importante": principal antes de develop
                var tipo = g.Select(i => i.Tipo).OrderBy(GitFlow.Ordem).First();
                return new BranchGrupoViewModel
                {
                    Chave = g.Key,
                    Titulo = g.Key switch { "" => "principais", "~" => "sem pasta", var p => p + "/" },
                    Cor = GitFlow.Cor(g.Key == "~" ? TipoBranch.Outra : tipo),
                    Itens = new ObservableCollection<BranchItemViewModel>(
                        g.OrderByDescending(i => i.IsHead)
                         .ThenBy(i => GitFlow.Ordem(i.Tipo))
                         .ThenBy(i => i.Curto, StringComparer.CurrentCultureIgnoreCase)),
                    Recolhido = recolhidos.Contains(g.Key),
                    Ordem = g.Key switch { "" => -1, "~" => 99, _ => GitFlow.Ordem(tipo) },
                };
            })
            .OrderBy(g => g.Ordem)
            .ThenBy(g => g.Titulo, StringComparer.CurrentCultureIgnoreCase);

        return new ObservableCollection<BranchGrupoViewModel>(grupos);
    }

    private async Task<bool> ConfirmarAsync(string titulo, string mensagem) =>
        Confirmar is null || await Confirmar(titulo, mensagem);

    private async Task IniciarAsync(TipoBranch tipo, string titulo)
    {
        if (PedirTexto is null) return;

        var nome = await PedirTexto(titulo, $"Nome (vai como {GitFlow.Prefixo(tipo, Fluxo)}nome, a partir da {GitFlow.Base(tipo, Fluxo)})");
        if (string.IsNullOrWhiteSpace(nome)) return;

        await ExecutarAsync(() => GitFlow.IniciarAsync(_repo.Path, tipo, nome, Fluxo));
    }

    [RelayCommand]
    private Task NovaFeature() => IniciarAsync(TipoBranch.Feature, "Nova feature");

    [RelayCommand]
    private Task NovoFix() => IniciarAsync(TipoBranch.Fix, "Novo fix");

    [RelayCommand]
    private Task NovaRelease() => IniciarAsync(TipoBranch.Release, "Nova release");

    /// <summary>Merge de volta e remoção da branch; mostra os comandos antes de rodar.</summary>
    [RelayCommand]
    private async Task Finalizar()
    {
        var branch = BranchAtual;
        var passos = GitFlow.PassosFinalizar(branch, Fluxo);
        if (passos.Count == 0) return;

        var ok = await ConfirmarAsync($"Finalizar {branch}",
            "Estes comandos vão rodar, nesta ordem. Se um merge der conflito, o processo para " +
            "ali e a branch não é apagada.\n\n" + GitFlow.Descrever(passos));
        if (!ok) return;

        await ExecutarAsync(() => GitFlow.FinalizarAsync(_repo.Path, branch, Fluxo));
    }

    [RelayCommand]
    private async Task ConfigurarFluxo()
    {
        if (EditarFluxo is null) return;

        var novo = await EditarFluxo(Fluxo);
        if (novo is null) return;

        var locais = _todas.Where(b => !b.IsRemote).Select(b => b.Name).ToList();
        await ExecutarAsync(() => GitFlow.InicializarAsync(_repo.Path, novo, locais));
    }

    private async Task ExecutarAsync(Func<Task> acao)
    {
        Busy = true;
        try
        {
            await acao();
            await _main.RefreshRepoAsync(_repo.Id);
            await CarregarAsync();
        }
        catch (Exception e)
        {
            MostrarErro(e.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    // o botão "Trocar" existe em cada linha e aponta para este mesmo comando: se ele
    // ficar indisponível durante a execução, a lista inteira acinzenta (piscada)
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Trocar(BranchItemViewModel item) =>
        ExecutarAsync(() => GitService.CheckoutAsync(_repo.Path, item.Name));

    [RelayCommand]
    private Task Criar() => ExecutarAsync(async () =>
    {
        await GitService.CreateBranchAsync(_repo.Path, NovaBranch.Trim(), true);
        NovaBranch = "";
    });

    [RelayCommand]
    private Task Confiar() => ExecutarAsync(() => GitService.TrustRepositoryAsync(_repo.Path));
}
