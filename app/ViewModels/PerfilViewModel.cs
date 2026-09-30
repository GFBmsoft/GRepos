using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>
/// Cartão de perfil no topo do painel geral: a conta do GitHub de um lado e, do outro,
/// o que dá para medir nos repositórios que estão aqui na máquina.
///
/// A separação é proposital. Estrelas e seguidores são da conta; linhas de código são
/// do que você tem clonado. Misturar as duas origens num número só seria mentira.
/// </summary>
public sealed partial class PerfilViewModel : ObservableObject
{
    private readonly IReadOnlyList<Repo> _repos;
    private readonly string _login;

    public PerfilViewModel(string login, IReadOnlyList<Repo> repos)
    {
        _login = login;
        _repos = repos;
        Titulo = login.Length > 0 ? login + "@github" : "Perfil";
    }

    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private Perfil? _perfil;
    [ObservableProperty] private bool _carregando;
    [ObservableProperty] private string _erro = "";

    [ObservableProperty] private long _linhasAdicionadas;
    [ObservableProperty] private long _linhasRemovidas;
    [ObservableProperty] private bool _contandoLinhas;
    [ObservableProperty] private string _progressoLinhas = "";
    [ObservableProperty] private bool _linhasContadas;

    public bool TemPerfil => Perfil is not null;
    public bool TemErro => Erro.Length > 0;

    public string Nome => Perfil?.Nome is { Length: > 0 } n ? n : Perfil?.Login ?? "";
    public string Bio => Perfil?.Bio ?? "";
    public bool TemBio => Bio.Length > 0;
    public string Local => Perfil?.Local ?? "";
    public bool TemLocal => Local.Length > 0;
    public string Empresa => Perfil?.Empresa ?? "";
    public bool TemEmpresa => Empresa.Length > 0;

    public string Repositorios => (Perfil?.RepositoriosPublicos ?? 0).ToString("N0");
    public string Seguidores => (Perfil?.Seguidores ?? 0).ToString("N0");
    public string Estrelas => Perfil is { Estrelas: >= 0 } p ? p.Estrelas.ToString("N0") : "—";

    public string Linguagens => Perfil is null || Perfil.Linguagens.Count == 0
        ? "—"
        : string.Join(", ", Perfil.Linguagens.Take(4));

    public string ReposLocais => _repos.Count.ToString("N0");

    /// <summary>Saldo: o número que o cartão do EV02 mostra em destaque.</summary>
    public string LinhasTexto => LinhasContadas
        ? (LinhasAdicionadas - LinhasRemovidas).ToString("N0")
        : "—";

    public string LinhasDetalhe => LinhasContadas
        ? $"{LinhasAdicionadas:N0}++, {LinhasRemovidas:N0}--"
        : "nos repositórios desta máquina";

    partial void OnPerfilChanged(Perfil? value)
    {
        foreach (var p in new[] { nameof(TemPerfil), nameof(Nome), nameof(Bio), nameof(TemBio),
                                  nameof(Local), nameof(TemLocal), nameof(Empresa), nameof(TemEmpresa),
                                  nameof(Repositorios), nameof(Seguidores), nameof(Estrelas),
                                  nameof(Linguagens) })
            OnPropertyChanged(p);
    }

    partial void OnErroChanged(string value) => OnPropertyChanged(nameof(TemErro));

    partial void OnLinhasContadasChanged(bool value)
    {
        OnPropertyChanged(nameof(LinhasTexto));
        OnPropertyChanged(nameof(LinhasDetalhe));
    }

    [RelayCommand]
    public async Task CarregarAsync()
    {
        if (Carregando) return;

        Carregando = true;
        Erro = "";
        try
        {
            Perfil = await GitHubService.PerfilAsync(_login);
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>
    /// Percorre o histórico de cada repositório local somando linhas. É caro — daí ser
    /// um botão, e não algo que acontece ao abrir o painel — e mostra o progresso
    /// porque em repositório grande passa de alguns segundos.
    /// </summary>
    [RelayCommand]
    private async Task ContarLinhasAsync()
    {
        if (ContandoLinhas) return;

        ContandoLinhas = true;
        long mais = 0, menos = 0;
        var feitos = 0;

        try
        {
            foreach (var repo in _repos)
            {
                ProgressoLinhas = $"{++feitos}/{_repos.Count} — {repo.Name}";
                try
                {
                    var (a, r) = await GitService.ContarLinhasAsync(repo.Path);
                    mais += a;
                    menos += r;
                }
                catch (Exception)
                {
                    // repositório indisponível não interrompe a soma dos outros
                }
            }

            LinhasAdicionadas = mais;
            LinhasRemovidas = menos;
            LinhasContadas = true;
            ProgressoLinhas = "";
        }
        finally
        {
            ContandoLinhas = false;
        }
    }
}
