using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>Uma versão na lista do changelog.</summary>
public sealed class VersaoViewModel
{
    public Release Release { get; init; } = new();

    public string Tag => Release.Tag;
    public string Titulo => Release.Nome.Length > 0 ? Release.Nome : Release.Tag;
    public string Quando => Rotulos.Quando(Release.Publicada);
    public string Notas => Release.Notas;

    /// <summary>A versão em uso ganha um selo: é a referência para ler o resto.</summary>
    public bool EmUso { get; init; }
}

/// <summary>
/// Changelog do próprio GRepos, lido das Releases do GitHub. Não há arquivo a manter:
/// as notas já são geradas pelo workflow a partir dos assuntos dos commits.
/// </summary>
public sealed partial class NovidadesViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<VersaoViewModel> _versoes = new();
    [ObservableProperty] private VersaoViewModel? _selecionada;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private string _erro = "";

    public bool TemErro => Erro.Length > 0;
    public bool SemSelecao => Selecionada is null;
    public bool Vazio => !Carregando && Erro.Length == 0 && Versoes.Count == 0;
    public string VersaoAtual => MainViewModel.VersaoEmUso.Length > 0
        ? "Você está na " + MainViewModel.VersaoEmUso
        : "Build local, sem versão publicada";

    partial void OnErroChanged(string value)
    {
        OnPropertyChanged(nameof(TemErro));
        OnPropertyChanged(nameof(Vazio));
    }

    partial void OnCarregandoChanged(bool value) => OnPropertyChanged(nameof(Vazio));

    partial void OnSelecionadaChanged(VersaoViewModel? value) => OnPropertyChanged(nameof(SemSelecao));

    [RelayCommand]
    public async Task CarregarAsync()
    {
        if (Carregando) return;

        Carregando = true;
        Erro = "";
        try
        {
            var releases = await GitHubService.ReleasesAsync(Atualizador.Slug);
            var emUso = MainViewModel.VersaoEmUso;

            Versoes = new ObservableCollection<VersaoViewModel>(releases.Select(r => new VersaoViewModel
            {
                Release = r,
                EmUso = emUso.Length > 0 && r.Tag.TrimStart('v', 'V') == emUso.Split('-')[0],
            }));

            Selecionada = Versoes.FirstOrDefault(v => v.EmUso) ?? Versoes.FirstOrDefault();
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Carregando = false;
            OnPropertyChanged(nameof(Vazio));
        }
    }

    [RelayCommand]
    private void AbrirNoGitHub()
    {
        try
        {
            var url = Selecionada?.Release.Url;
            ShellService.AbrirUrl(string.IsNullOrEmpty(url)
                ? $"https://github.com/{Atualizador.Slug}/releases"
                : url);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }
}
