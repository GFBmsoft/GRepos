using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>
/// Um bloco de conflito na tela: o meu lado, o deles e o resultado, que começa vazio e
/// é preenchido por um dos botões — ou digitado, quando nenhum dos lados serve inteiro.
/// </summary>
public sealed partial class BlocoDeConflitoViewModel : ObservableObject
{
    private readonly ConflitoViewModel _dono;
    private bool _preenchendo;

    public BlocoDeConflitoViewModel(ConflitoViewModel dono, int numero, int total, int linha,
        string antes, string meu, string deles, string depois)
    {
        _dono = dono;
        Titulo = $"Conflito {numero} de {total}";
        Linha = $"linha {linha}";
        Antes = antes;
        Meu = meu;
        Deles = deles;
        Depois = depois;
    }

    public string Titulo { get; }
    public string Linha { get; }

    /// <summary>Linhas iguais logo antes e logo depois, para situar o bloco no arquivo.</summary>
    public string Antes { get; }
    public string Depois { get; }
    public bool TemAntes => Antes.Length > 0;
    public bool TemDepois => Depois.Length > 0;

    public string Meu { get; }
    public string Deles { get; }

    /// <summary>
    /// Lado vazio precisa aparecer dito, não em branco. Sem a base não dá para saber se
    /// ele apagou as linhas ou se foi o outro que as acrescentou.
    /// </summary>
    public string MeuVisivel => Meu.Length > 0 ? Meu : "(nada deste lado: as linhas só existem no outro)";
    public string DelesVisivel => Deles.Length > 0 ? Deles : "(nada deste lado: as linhas só existem no outro)";

    /// <summary>"", "meu", "deles", "ambos", "invertido" ou "manual".</summary>
    [ObservableProperty] private string _escolha = "";
    [ObservableProperty] private string _resultado = "";

    public bool Resolvido => Escolha.Length > 0;
    public bool Pendente => !Resolvido;

    public string EscolhaTexto => Escolha switch
    {
        "meu" => "ficou o meu",
        "deles" => "ficou o deles",
        "ambos" => "ficaram os dois, o meu primeiro",
        "invertido" => "ficaram os dois, o deles primeiro",
        "manual" => "editado à mão",
        _ => "pendente",
    };

    public string EscolhaCor => Resolvido ? "Green" : "Yellow";
    public bool EhMeu => Escolha == "meu";
    public bool EhDeles => Escolha == "deles";
    public bool EhAmbos => Escolha == "ambos";
    public bool EhInvertido => Escolha == "invertido";

    partial void OnEscolhaChanged(string value)
    {
        OnPropertyChanged(string.Empty);
        _dono.AvisarPendentes();
    }

    // digitar no resultado é uma resolução: o bloco deixa de estar pendente
    partial void OnResultadoChanged(string value)
    {
        if (!_preenchendo) Escolha = "manual";
    }

    private void Preencher(string escolha, string texto)
    {
        _preenchendo = true;
        try
        {
            Resultado = texto;
            Escolha = escolha;
        }
        finally
        {
            _preenchendo = false;
        }
    }

    private static string Juntar(string a, string b) =>
        a.Length == 0 ? b : b.Length == 0 ? a : a + "\n" + b;

    [RelayCommand] public void UsarMeu() => Preencher("meu", Meu);
    [RelayCommand] public void UsarDeles() => Preencher("deles", Deles);
    [RelayCommand] public void UsarAmbos() => Preencher("ambos", Juntar(Meu, Deles));
    [RelayCommand] public void UsarInvertido() => Preencher("invertido", Juntar(Deles, Meu));

    /// <summary>Volta a pendente, para quem escolheu errado e quer pensar de novo.</summary>
    [RelayCommand] public void Limpar() => Preencher("", "");
}

/// <summary>
/// Resolução de conflito bloco a bloco, sobre o arquivo com marcadores que o git deixou
/// na pasta de trabalho. Salvar regrava o arquivo sem os marcadores, no encoding e com
/// os fins de linha que ele tinha, e o marca como resolvido.
/// </summary>
public sealed partial class ConflitoViewModel : ObservableObject
{
    /// <summary>Linhas iguais mostradas em volta de cada bloco.</summary>
    private const int Contexto = 3;

    private readonly string _repo;
    private readonly string _caminho;
    private readonly bool _rebase;

    private Conflitos.ArquivoLido? _arquivo;
    private List<TrechoDeConflito> _trechos = new();
    private string _quebra = "\n";

    /// <param name="rebase">
    /// No rebase o git inverte os lados: o primeiro do marcador é a base onde o commit
    /// está sendo reaplicado, e o segundo é o seu. A tela desfaz a inversão.
    /// </param>
    public ConflitoViewModel(string repo, string caminho, bool rebase = false)
    {
        _repo = repo;
        _caminho = caminho;
        _rebase = rebase;
        Title = "Resolver conflito — " + Path.GetFileName(caminho);
    }

    public string Title { get; }
    public string Caminho => _caminho;

    public string RotuloMeu => _rebase ? "MEU (o commit sendo reaplicado)" : "MEU (a branch atual)";
    public string RotuloDeles => _rebase ? "DELES (a branch de destino)" : "DELES (o que está chegando)";

    public ObservableCollection<BlocoDeConflitoViewModel> Blocos { get; } = new();

    [ObservableProperty] private string _erro = "";
    [ObservableProperty] private bool _salvando;

    public bool TemErro => Erro.Length > 0;
    public bool SemBlocos => Blocos.Count == 0 && Erro.Length == 0;

    partial void OnErroChanged(string value)
    {
        OnPropertyChanged(nameof(TemErro));
        OnPropertyChanged(nameof(SemBlocos));
    }

    public int Pendentes => Blocos.Count(b => b.Pendente);

    public string Resumo => Blocos.Count == 0
        ? ""
        : Pendentes == 0
            ? $"{Blocos.Count} de {Blocos.Count} resolvidos — pode salvar"
            : $"{Blocos.Count - Pendentes} de {Blocos.Count} resolvidos";

    public bool PodeSalvar => !Salvando && Blocos.Count > 0 && Pendentes == 0;

    partial void OnSalvandoChanged(bool value) => OnPropertyChanged(nameof(PodeSalvar));

    public void AvisarPendentes()
    {
        foreach (var p in new[] { nameof(Pendentes), nameof(Resumo), nameof(PodeSalvar) })
            OnPropertyChanged(p);
    }

    /// <summary>A janela fecha quando isto vira verdadeiro.</summary>
    public bool Salvou { get; private set; }

    public event Action? Fechar;

    private string Completo => Path.Combine(_repo, _caminho.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Lê o arquivo do disco e monta os blocos. A janela chama ao abrir.</summary>
    public void Carregar()
    {
        Blocos.Clear();
        Erro = "";
        try
        {
            _arquivo = Conflitos.LerArquivo(Completo);
            Montar(_arquivo.Texto);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Erro = "Não foi possível ler o arquivo: " + e.Message;
        }
    }

    /// <summary>Monta os blocos a partir do texto; separado do disco para ser testado.</summary>
    public void Montar(string texto)
    {
        Blocos.Clear();
        _trechos = Conflitos.Ler(texto);
        _quebra = Conflitos.QuebraDoArquivo(texto);

        var total = _trechos.Count(t => t.Conflito);
        var numero = 0;
        for (var i = 0; i < _trechos.Count; i++)
        {
            var t = _trechos[i];
            if (!t.Conflito) continue;

            var antes = i > 0 && !_trechos[i - 1].Conflito
                ? _trechos[i - 1].Linhas.TakeLast(Contexto)
                : Enumerable.Empty<string>();
            var depois = i + 1 < _trechos.Count && !_trechos[i + 1].Conflito
                ? _trechos[i + 1].Linhas.Take(Contexto)
                : Enumerable.Empty<string>();

            var (meu, deles) = _rebase ? (t.Deles, t.Nosso) : (t.Nosso, t.Deles);
            Blocos.Add(new BlocoDeConflitoViewModel(this, ++numero, total, t.LinhaInicial,
                Conflitos.ParaTela(antes), Conflitos.ParaTela(meu), Conflitos.ParaTela(deles),
                Conflitos.ParaTela(depois)));
        }

        if (total == 0)
            Erro = "Este arquivo não tem marcadores de conflito. Se já foi acertado no editor, " +
                   "use o ✓ da lista para marcar como resolvido.";

        OnPropertyChanged(nameof(SemBlocos));
        AvisarPendentes();
    }

    /// <summary>O arquivo como vai para o disco com as resoluções de agora.</summary>
    public string Resultado() => Conflitos.Montar(
        _trechos, Blocos.Select(b => (IReadOnlyList<string>)Conflitos.DaTela(b.Resultado, _quebra)).ToList());

    [RelayCommand]
    private void TudoMeu()
    {
        foreach (var b in Blocos.Where(b => b.Pendente)) b.UsarMeu();
    }

    [RelayCommand]
    private void TudoDeles()
    {
        foreach (var b in Blocos.Where(b => b.Pendente)) b.UsarDeles();
    }

    [RelayCommand]
    private async Task SalvarAsync()
    {
        if (!PodeSalvar || _arquivo is null) return;

        Salvando = true;
        Erro = "";
        try
        {
            // alguém mexeu no arquivo com a janela aberta (o editor, outro git): gravar por
            // cima apagaria o que foi feito lá
            var agora = Conflitos.LerArquivo(Completo);
            if (agora.Texto != _arquivo.Texto)
            {
                Erro = "O arquivo mudou no disco depois que esta janela abriu. Feche e abra de novo para resolver sobre a versão atual.";
                return;
            }

            Conflitos.GravarArquivo(Completo, Resultado(), _arquivo);
            await GitService.MarcarResolvidoAsync(_repo, _caminho);

            Salvou = true;
            Fechar?.Invoke();
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
        finally
        {
            Salvando = false;
        }
    }
}
