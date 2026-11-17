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

    public PerfilViewModel(string login, IReadOnlyList<Repo> repos, ContagemDeLinhas? contagem = null,
                           string corDosQuadros = "github")
    {
        _login = login;
        _repos = repos;
        _corDosQuadros = CoresDeDestaque.De(corDosQuadros).Id; // direto no campo: abrir não é escolher
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
            _linguagensLocais = new Dictionary<string, long>(contagem.Linguagens ?? new(), StringComparer.OrdinalIgnoreCase);
            Fatias = EstatisticasDoPerfil.Fatias(_linguagensLocais);
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

    partial void OnDiasChanged(IReadOnlyList<DiaContribuicao>? value)
    {
        _ritmo = EstatisticasDoPerfil.Ritmo(value);
        _sequencias = EstatisticasDoPerfil.Sequencias(value);
        BarrasDaSemana = Barras(_ritmo);

        OnPropertyChanged(nameof(TemContribuicoes));
        AvisarQuadros();
    }

    // ---------------------------------------------- quadros (github-cool-readme)
    //
    // A segunda faixa do cartão, no desenho dos cartões do github-cool-readme: resumo, linguagens,
    // ritmo semanal e sequências. O resumo, o ritmo e as sequências são da conta (o calendário do
    // GitHub, que conta também os repositórios privados); as linguagens são das linhas dos seus
    // commits aqui na máquina. O GitHub só detalharia as linguagens dos repositórios que o token
    // enxerga, e sem os da organização a barra saía C# e Go, sem o Pascal do dia a dia.

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private RitmoSemanal? _ritmo;
    private Sequencias _sequencias = EstatisticasDoPerfil.Sequencias(null);
    private Dictionary<string, long> _linguagensLocais = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private ResumoDeContribuicoes? _resumo;

    partial void OnResumoChanged(ResumoDeContribuicoes? value) => AvisarQuadros();

    /// <summary>A faixa aparece quando há o que mostrar em algum dos quadros.</summary>
    public bool TemQuadros => TemResumo || TemRitmo || TemSequencias || TemLinguagensLocais;

    // cor de destaque

    /// <summary>Id de <see cref="CoresDeDestaque"/>; muda pelo clique direito do cartão.</summary>
    [ObservableProperty] private string _corDosQuadros = "github";

    /// <summary>Quem grava a cor escolhida no workspace (o MainViewModel).</summary>
    public Action<string>? GuardarCor { get; init; }

    /// <summary>Recurso do tema das barras do ritmo e do destaque das sequências.</summary>
    public string CorDasBarras => CoresDeDestaque.De(CorDosQuadros).Barras;

    /// <summary>Recurso do tema do anel dos dias ativos.</summary>
    public string CorDoAnel => CoresDeDestaque.De(CorDosQuadros).Anel;

    [RelayCommand]
    private void EscolherCor(string? id)
    {
        var cor = CoresDeDestaque.De(id).Id;
        if (cor == CorDosQuadros) return;

        CorDosQuadros = cor;
        GuardarCor?.Invoke(cor);
    }

    partial void OnCorDosQuadrosChanged(string value)
    {
        OnPropertyChanged(nameof(CorDasBarras));
        OnPropertyChanged(nameof(CorDoAnel));
    }

    // resumo

    public bool TemResumo => Resumo is not null;
    public string CommitsTexto => (Resumo?.Commits ?? 0).ToString("N0", PtBr);
    public string PullRequestsTexto => (Resumo?.PullRequests ?? 0).ToString("N0", PtBr);
    public string RevisoesTexto => (Resumo?.Revisoes ?? 0).ToString("N0", PtBr);
    public string IssuesTexto => (Resumo?.Issues ?? 0).ToString("N0", PtBr);
    public string PrivadasTexto => (Resumo?.Privadas ?? 0).ToString("N0", PtBr);
    public bool TemPrivadas => Resumo is { Privadas: > 0 };

    /// <summary>O anel: a parte do ano com alguma contribuição.</summary>
    public string AtivosTexto => Percentual(_sequencias.FracaoAtivos, 0);

    /// <summary>Em graus, para o arco. Fechado em 360 o arco some: para um pouco antes.</summary>
    public double AtivosAngulo => Math.Clamp(_sequencias.FracaoAtivos * 360, 0, 359.9);

    public string AtivosDica => $"{_sequencias.DiasAtivos.ToString("N0", PtBr)} de " +
                                $"{_sequencias.DiasNoPeriodo.ToString("N0", PtBr)} dias com alguma contribuição";

    // ritmo semanal

    public bool TemRitmo => _ritmo is not null;

    [ObservableProperty] private IReadOnlyList<BarraDaSemana> _barrasDaSemana = Array.Empty<BarraDaSemana>();

    public string MaisCheioNome => _ritmo?.MaisCheio.NomeCompleto ?? "";
    public string MaisCheioRotulo => $"mais cheio · {(_ritmo?.MaisCheio.Quantidade ?? 0).ToString("N0", PtBr)}";
    public string MaisVazioNome => _ritmo?.MaisVazio.NomeCompleto ?? "";
    public string MaisVazioRotulo => $"mais vazio · {(_ritmo?.MaisVazio.Quantidade ?? 0).ToString("N0", PtBr)}";
    public string UteisTexto => Percentual(_ritmo?.FracaoUteis ?? 0, 0);

    /// <summary>Altura máxima da barra do ritmo semanal, em pixels.</summary>
    public const double AlturaDaBarra = 52;

    private static IReadOnlyList<BarraDaSemana> Barras(RitmoSemanal? ritmo)
    {
        if (ritmo is null) return Array.Empty<BarraDaSemana>();

        var maximo = Math.Max(1, ritmo.MaisCheio.Quantidade);
        var media = ritmo.Dias.Average(d => d.Quantidade);

        // acima da média na cor cheia, abaixo dela mais apagada, como no cartão de referência
        return ritmo.Dias.Select(d => new BarraDaSemana(
                d.Nome,
                Math.Max(2, AlturaDaBarra * d.Quantidade / maximo),
                d.Quantidade >= media ? 1.0 : 0.5,
                $"{d.NomeCompleto}: {d.Quantidade.ToString("N0", PtBr)} " +
                (d.Quantidade == 1 ? "contribuição" : "contribuições") + " no último ano"))
            .ToList();
    }

    // sequências

    public bool TemSequencias => _sequencias.DiasAtivos > 0;
    public string AtualTexto => EmDias(_sequencias.Atual);
    public string MaiorTexto => EmDias(_sequencias.Maior);
    public string MelhorDiaNumero => _sequencias.NoMelhorDia.ToString("N0", PtBr);
    public string MelhorDiaRotulo => _sequencias.MelhorDia is { } d ? $"melhor dia · {d:dd/MM}" : "melhor dia";
    public string MelhorDiaDica => _sequencias.MelhorDia is { } d
        ? $"{_sequencias.NoMelhorDia.ToString("N0", PtBr)} contribuições em {d.ToString("dddd, dd/MM/yyyy", PtBr)}"
        : "";
    public string MelhorMesNumero => _sequencias.NoMelhorMes.ToString("N0", PtBr);
    public string MelhorMesRotulo => _sequencias.MelhorMes is { } m
        ? $"melhor mês · {m.ToString("MMM/yy", PtBr).Replace(".", "")}"
        : "melhor mês";
    public string MelhorMesDica => _sequencias.MelhorMes is { } m
        ? $"{_sequencias.NoMelhorMes.ToString("N0", PtBr)} contribuições em {m.ToString("MMMM 'de' yyyy", PtBr)}"
        : "";

    private static string EmDias(int n) => n == 1 ? "1 dia" : $"{n.ToString("N0", PtBr)} dias";

    // linguagens

    [ObservableProperty] private IReadOnlyList<FatiaDeLinguagem> _fatias = Array.Empty<FatiaDeLinguagem>();

    public bool TemLinguagensLocais => Fatias.Count > 0;

    public IReadOnlyList<LegendaDeLinguagem> Legenda => Fatias
        .Select(f => new LegendaDeLinguagem(f.Nome, f.Cor, Percentual(f.Fracao, 1)))
        .ToList();

    /// <summary>O convite do quadro vazio: ainda não contou, ou contou numa versão sem as linguagens.</summary>
    public string LinguagensConvite => LinhasContadas
        ? "Conte de novo as linhas para ver as linguagens dos seus commits."
        : "Conte as linhas para ver as linguagens dos seus commits.";

    partial void OnFatiasChanged(IReadOnlyList<FatiaDeLinguagem> value)
    {
        OnPropertyChanged(nameof(TemLinguagensLocais));
        OnPropertyChanged(nameof(Legenda));
        OnPropertyChanged(nameof(TemQuadros));
    }

    private static string Percentual(double fracao, int casas) =>
        (fracao * 100).ToString(casas == 0 ? "0" : "0.0", PtBr) + "%";

    private void AvisarQuadros()
    {
        foreach (var p in new[]
                 {
                     nameof(TemQuadros), nameof(TemResumo), nameof(CommitsTexto), nameof(PullRequestsTexto),
                     nameof(RevisoesTexto), nameof(IssuesTexto), nameof(PrivadasTexto), nameof(TemPrivadas),
                     nameof(AtivosTexto), nameof(AtivosAngulo), nameof(AtivosDica),
                     nameof(TemRitmo), nameof(MaisCheioNome), nameof(MaisCheioRotulo), nameof(MaisVazioNome),
                     nameof(MaisVazioRotulo), nameof(UteisTexto),
                     nameof(TemSequencias), nameof(AtualTexto), nameof(MaiorTexto), nameof(MelhorDiaNumero),
                     nameof(MelhorDiaRotulo), nameof(MelhorDiaDica), nameof(MelhorMesNumero),
                     nameof(MelhorMesRotulo), nameof(MelhorMesDica),
                 })
            OnPropertyChanged(p);
    }

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
        OnPropertyChanged(nameof(LinguagensConvite));
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
                if (c.Resumo is not null) Resumo = c.Resumo;
            }

            PerfilCache.Guardar(_login, new PerfilGuardado
            {
                Perfil = perfil,
                // sem resposta nova, as contribuições e a foto de antes continuam valendo
                TotalContribuicoes = c?.Total ?? guardado?.TotalContribuicoes ?? 0,
                Dias = (c?.Dias ?? guardado?.Dias ?? (IReadOnlyList<DiaContribuicao>)Array.Empty<DiaContribuicao>()).ToList(),
                Resumo = c?.Resumo ?? guardado?.Resumo,
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
        if (guardado.Resumo is not null) Resumo = guardado.Resumo;

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
        var porLinguagem = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

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
                    var (a, r) = await GitService.ContarLinhasAsync(repo.Path, autores, contados, porLinguagem);
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
            _linguagensLocais = porLinguagem;
            Fatias = EstatisticasDoPerfil.Fatias(porLinguagem);
            OnPropertyChanged(nameof(LinhasTexto));
            OnPropertyChanged(nameof(LinhasDetalhe));
            ProgressoLinhas = "";

            Guardar?.Invoke(new ContagemDeLinhas
            {
                Adicionadas = mais,
                Removidas = menos,
                Quando = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Linguagens = porLinguagem,
            });
        }
        finally
        {
            ContandoLinhas = false;
        }
    }
}

/// <summary>Uma barra do ritmo semanal: a altura já em pixels e a força da cor.</summary>
public sealed record BarraDaSemana(string Nome, double Altura, double Opacidade, string Dica);

/// <summary>Um item da legenda de linguagens: a bolinha, o nome e o percentual.</summary>
public sealed record LegendaDeLinguagem(string Nome, string Cor, string Percentual);
