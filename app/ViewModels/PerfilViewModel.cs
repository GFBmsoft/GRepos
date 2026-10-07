using System;
using System.Collections.Generic;
using System.Globalization;
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

    public PerfilViewModel(string login, IReadOnlyList<Repo> repos, ContagemDeLinhas? contagem = null)
    {
        _login = login;
        _repos = repos;
        Titulo = login.Length > 0 ? login + "@github" : "Perfil";

        // a última contagem volta junto com o cartão: contar de novo é caro
        if (contagem is not null)
        {
            _linhasAdicionadas = contagem.Adicionadas;
            _linhasRemovidas = contagem.Removidas;
            _contadoEm = DateTime.TryParse(contagem.Quando, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var q)
                ? q.ToLocalTime()
                : null;
            _linhasContadas = true;
        }
    }

    /// <summary>Quem grava a contagem no workspace (o MainViewModel).</summary>
    public Action<ContagemDeLinhas>? Guardar { get; init; }

    private DateTime? _contadoEm;

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

    /// <summary>Demais contas cadastradas, para trocar o perfil mostrado no cartão.</summary>
    public IReadOnlyList<string> OutrasContas { get; init; } = Array.Empty<string>();

    public bool TemOutrasContas => OutrasContas.Count > 0;

    /// <summary>Quem sabe montar o perfil de outra conta (o MainViewModel).</summary>
    public Action<string>? Trocar { get; init; }

    [RelayCommand]
    private void TrocarConta(string? login)
    {
        if (!string.IsNullOrWhiteSpace(login)) Trocar?.Invoke(login);
    }

    /// <summary>Foto da conta; até chegar (ou se não vier), o cartão mostra a inicial.</summary>
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _foto;

    public bool TemFoto => Foto is not null;
    public bool SemFoto => Foto is null;
    public string Inicial => _login.Length > 0 ? _login[..1].ToUpperInvariant() : "?";

    partial void OnFotoChanged(Avalonia.Media.Imaging.Bitmap? value)
    {
        OnPropertyChanged(nameof(TemFoto));
        OnPropertyChanged(nameof(SemFoto));
    }

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
        ? $"{LinhasAdicionadas:N0}++, {LinhasRemovidas:N0}--" +
          (_contadoEm is { } q ? $" · em {q:dd/MM}" : "")
        : "seus commits nos repositórios desta máquina";

    // ------------------------------------------------------- contribuições

    /// <summary>Quadriculado do último ano; nulo sem token ou enquanto não chega.</summary>
    [ObservableProperty] private IReadOnlyList<DiaContribuicao>? _dias;
    [ObservableProperty] private int _totalContribuicoes;

    public bool TemContribuicoes => Dias is { Count: > 0 };

    public string ContribuicoesTexto => TotalContribuicoes == 1
        ? "1 contribuição no último ano"
        : $"{TotalContribuicoes:N0} contribuições no último ano";

    partial void OnDiasChanged(IReadOnlyList<DiaContribuicao>? value) =>
        OnPropertyChanged(nameof(TemContribuicoes));

    partial void OnTotalContribuicoesChanged(int value) => OnPropertyChanged(nameof(ContribuicoesTexto));

    public bool PodeRecontar => LinhasContadas && !ContandoLinhas;

    partial void OnContandoLinhasChanged(bool value) => OnPropertyChanged(nameof(PodeRecontar));

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
        OnPropertyChanged(nameof(PodeRecontar));
    }

    [RelayCommand]
    public Task CarregarAsync() => CarregarAsync(forcar: false);

    /// <summary>
    /// Mostra na hora o que ficou guardado da última vez e só então vai ao GitHub — e nem
    /// vai, se o guardado é recente. Abrir o painel repetia quatro consultas a cada vez.
    /// </summary>
    /// <param name="forcar">O botão Atualizar do painel: busca mesmo com o guardado recente.</param>
    public async Task CarregarAsync(bool forcar)
    {
        if (Carregando) return;

        var guardado = PerfilCache.Ler(_login);
        if (guardado is not null)
        {
            Aplicar(guardado);
            if (!forcar && PerfilCache.Atual(guardado)) return;
        }

        Carregando = true;
        Erro = "";
        try
        {
            // tudo junto: o cartão esperava a conta, depois a foto, e as contribuições à parte
            var contribuicoes = BuscarContribuicoesAsync();
            var perfil = await GitHubService.PerfilAsync(_login);
            Perfil = perfil;

            var foto = await GitHubService.FotoAsync(perfil.AvatarUrl);
            if (foto is not null) Foto = Imagem(foto) ?? Foto;

            var c = await contribuicoes;
            if (c is not null)
            {
                TotalContribuicoes = c.Total;
                Dias = c.Dias;
            }

            PerfilCache.Guardar(_login, new PerfilGuardado
            {
                Perfil = perfil,
                // sem resposta nova, as contribuições e a foto de antes continuam valendo
                TotalContribuicoes = c?.Total ?? guardado?.TotalContribuicoes ?? 0,
                Dias = (c?.Dias ?? guardado?.Dias ?? (IReadOnlyList<DiaContribuicao>)Array.Empty<DiaContribuicao>()).ToList(),
                Foto = foto is not null ? Convert.ToBase64String(foto) : guardado?.Foto ?? "",
                Quando = DateTime.UtcNow,
            });
        }
        catch (Exception e)
        {
            // com o cartão já preenchido pelo guardado, falha de rede não vira erro na tela
            if (guardado is null) Erro = e.Message;
        }
        finally
        {
            Carregando = false;
        }
    }

    /// <summary>Põe na tela o que estava guardado. Separado para ser testado sem rede.</summary>
    public void Aplicar(PerfilGuardado guardado)
    {
        Perfil = guardado.Perfil;
        TotalContribuicoes = guardado.TotalContribuicoes;
        if (guardado.Dias.Count > 0) Dias = guardado.Dias;

        if (guardado.Foto.Length > 0)
        {
            try
            {
                Foto = Imagem(Convert.FromBase64String(guardado.Foto)) ?? Foto;
            }
            catch (FormatException)
            {
                // foto guardada ilegível: fica a inicial até a nova chegar
            }
        }
    }

    /// <summary>Falha aqui é silenciosa: o quadriculado é enfeite, o perfil não depende dele.</summary>
    private async Task<Contribuicoes?> BuscarContribuicoesAsync()
    {
        try
        {
            return await GitHubService.ContribuicoesAsync(_login);
        }
        catch (Exception)
        {
            return null; // sem rede ou sem permissão: o cartão fica sem o quadriculado
        }
    }

    private static Avalonia.Media.Imaging.Bitmap? Imagem(byte[] bytes)
    {
        try
        {
            using var ms = new System.IO.MemoryStream(bytes);
            return new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch (Exception)
        {
            return null; // imagem ilegível: fica a inicial
        }
    }

    /// <summary>
    /// Percorre o histórico de cada repositório local somando as linhas dos seus commits. É caro — daí ser
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

        // só os commits do usuário, e cada commit uma vez: o par Origem × Destino
        // compartilha o histórico e somava tudo em dobro
        var contados = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            ProgressoLinhas = "identificando seus commits…";
            var autores = await GitService.IdentidadesAsync(
                _repos.Select(r => r.Path), OutrasContas.Prepend(_login));

            foreach (var repo in _repos)
            {
                ProgressoLinhas = $"{++feitos}/{_repos.Count} — {repo.Name}";
                try
                {
                    var (a, r) = await GitService.ContarLinhasAsync(repo.Path, autores, contados);
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
            _contadoEm = DateTime.Now;
            LinhasContadas = true;
            OnPropertyChanged(nameof(LinhasTexto));
            OnPropertyChanged(nameof(LinhasDetalhe));
            ProgressoLinhas = "";

            Guardar?.Invoke(new ContagemDeLinhas
            {
                Adicionadas = mais,
                Removidas = menos,
                Quando = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            });
        }
        finally
        {
            ContandoLinhas = false;
        }
    }
}
