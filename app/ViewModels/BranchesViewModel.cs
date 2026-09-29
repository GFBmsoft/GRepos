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

        Locais = new ObservableCollection<BranchItemViewModel>(
            _todas.Where(b => !b.IsRemote && Combina(b))
                  .OrderByDescending(b => b.IsHead)
                  .ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                  .Select(b => new BranchItemViewModel { Branch = b }));

        Remotas = new ObservableCollection<BranchItemViewModel>(
            _todas.Where(b => b.IsRemote && Combina(b))
                  .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                  .Select(b => new BranchItemViewModel { Branch = b }));

        foreach (var p in new[] { nameof(SemLocais), nameof(SemRemotas), nameof(TotalLocais), nameof(TotalRemotas) })
            OnPropertyChanged(p);
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

    [RelayCommand]
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
