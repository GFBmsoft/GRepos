using System;
using System.Collections.Generic;
using System.Linq;

namespace GRepos.Services;

/// <summary>
/// O papel de cada ponto aceso. A cor de cada um sai da aparência escolhida
/// (<see cref="CoresDoDisplay"/>): o display só sabe o papel.
/// </summary>
public enum Tom
{
    /// <summary>Texto principal.</summary>
    Aceso,

    /// <summary>Texto secundário: a mesma cor, mais apagada.</summary>
    Apagado,

    /// <summary>Anéis e barras.</summary>
    Destaque,

    /// <summary>O brilho que corre sobre o número.</summary>
    Brilho,
}

/// <summary>Um punhado de pontos acesos com o mesmo papel e a mesma força.</summary>
public sealed record Camada(IReadOnlyList<Ponto> Pontos, Tom Tom, double Opacidade = 1);

/// <summary>Uma coisa num slide: texto na fonte de pontos ou pontos prontos (barras e afins).</summary>
public abstract record ItemDeSlide;

public sealed record TextoNoDisplay(
    string Texto, int Col, int Lin, int Escala = 1, bool Apagado = false, bool Brilha = false) : ItemDeSlide;

public sealed record PontosNoDisplay(IReadOnlyList<Ponto> Pontos, Tom Tom, double Opacidade = 1) : ItemDeSlide;

/// <summary>Um retângulo do display, em pontos.</summary>
public sealed record Regiao(int Col, int Lin, int Cols, int Lins)
{
    public bool Contem(Ponto p) => p.Col >= Col && p.Col < Col + Cols && p.Lin >= Lin && p.Lin < Lin + Lins;
}

/// <summary>Como o slide entra, como num fliperama. Tudo anda de ponto em ponto.</summary>
public enum Efeito
{
    /// <summary>Acende em grupos de pontos ao acaso.</summary>
    Faisca,

    /// <summary>Sobe de baixo, empurrando o anterior para cima.</summary>
    Empurra,

    /// <summary>Uma barra varre da esquerda, trocando o anterior pelo novo.</summary>
    Varre,

    /// <summary>Pisca antes de ficar.</summary>
    Pisca,

    /// <summary>O texto aparece letra por letra.</summary>
    Digita,
}

/// <summary>Algo que muda com o tempo num display e se desenha sozinho.</summary>
public interface IParteDoDisplay
{
    /// <summary>Um número que só muda quando o desenho muda: com ele a tela não redesenha à toa.</summary>
    long Estado(double t);

    /// <summary>Desenha no tempo <paramref name="t"/>; nulo é o display parado, sem animação.</summary>
    void Desenhar(double? t, List<Camada> saida);
}

/// <summary>Pontos que não mudam: títulos, a linha de base, o fundo das barras.</summary>
public sealed class PartesFixas : IParteDoDisplay
{
    private readonly IReadOnlyList<Camada> _camadas;

    public PartesFixas(IReadOnlyList<Camada> camadas) => _camadas = camadas;

    public long Estado(double t) => 0;

    public void Desenhar(double? t, List<Camada> saida) => saida.AddRange(_camadas);
}

/// <summary>
/// Os slides se revezam numa região, cada um entrando com um efeito, sem fim. Com um slide só,
/// ele fica parado. É o "slideshow" do github-cool-readme, refeito em casas em vez de CSS.
/// </summary>
public sealed class Slideshow : IParteDoDisplay
{
    /// <summary>Segundos de cada slide na tela.</summary>
    public const double Exibe = 3.6;

    /// <summary>Segundos que a entrada leva.</summary>
    public const double Entra = 0.6;

    /// <summary>A máquina de escrever leva um pouco mais que as outras entradas.</summary>
    public const double Digitacao = Entra * 1.6;

    private static readonly double Inclinacao = Math.Tan(25 * Math.PI / 180); // o brilho passa inclinado

    private readonly List<SlidePronto> _slides;
    private readonly IReadOnlyList<Efeito> _efeitos;

    public Regiao Regiao { get; }

    public int Quantidade => _slides.Count;

    public double Ciclo => _slides.Count * Exibe;

    public Slideshow(IReadOnlyList<IReadOnlyList<ItemDeSlide>> slides, Regiao regiao, IReadOnlyList<Efeito> efeitos)
    {
        Regiao = regiao;
        _efeitos = efeitos.Count > 0 ? efeitos : new[] { Efeito.Empurra };
        _slides = slides.Select(s => new SlidePronto(s)).ToList();
    }

    /// <summary>O slide na tela no tempo <paramref name="t"/>, e quanto tempo faz que ele entrou.</summary>
    public (int Indice, double Local) Momento(double t)
    {
        if (_slides.Count == 0) return (0, 0);
        var tt = ((t % Ciclo) + Ciclo) % Ciclo;
        var i = Math.Min(_slides.Count - 1, (int)(tt / Exibe));
        return (i, tt - i * Exibe);
    }

    public Efeito EfeitoDe(int indice) => _efeitos[indice % _efeitos.Count];

    public long Estado(double t)
    {
        if (_slides.Count <= 1) return 0;

        var (i, local) = Momento(t);
        long passo;
        if (local < Digitacao) passo = 1 + (long)(local / 0.02);
        else if (PassoDoBrilho(i, local) is { } b) passo = 10_000 + b;
        else passo = 99_999;
        return i * 1_000_000L + passo;
    }

    public void Desenhar(double? t, List<Camada> saida)
    {
        if (_slides.Count == 0) return;
        if (t is null || _slides.Count == 1)
        {
            _slides[0].Inteiro(saida, Regiao);
            return;
        }

        var (i, local) = Momento(t.Value);
        var efeito = EfeitoDe(i);
        var p = Math.Clamp(local / Entra, 0, 1);

        // quem sai: só no empurrão (sobe) e na varrida (some atrás da barra). Na volta ao
        // primeiro slide o último já saiu, como na referência.
        if (i > 0 && local < Entra && efeito is Efeito.Empurra or Efeito.Varre)
            _slides[i - 1].Saindo(saida, Regiao, efeito, p);

        _slides[i].Entrando(saida, Regiao, efeito, local, p);

        if (PassoDoBrilho(i, local) is { } passo)
            _slides[i].Brilhar(saida, Regiao, PosicaoDoBrilho(passo));
    }

    // o brilho: uma faixa de 5 pontos que passa uma vez, meio segundo depois da entrada
    private int De => Regiao.Col - 10;
    private int Ate => Regiao.Col + Regiao.Cols + (int)Math.Ceiling(DisplayDeLeds.Linhas * Inclinacao);
    private int PassosDoBrilho => Math.Max(1, (int)Math.Round((Ate - De) / 3.0));
    private double DuracaoDoBrilho => 0.6 * (Ate - De) / (Regiao.Cols + 10.0);

    private int? PassoDoBrilho(int indice, double local)
    {
        if (!_slides[indice].TemBrilho) return null;
        var inicio = Entra + 0.5;
        if (local < inicio || local >= inicio + DuracaoDoBrilho) return null;
        return (int)((local - inicio) / DuracaoDoBrilho * PassosDoBrilho);
    }

    private double PosicaoDoBrilho(int passo) => De + (Ate - De) * (double)passo / PassosDoBrilho;

    /// <summary>
    /// A faixa do brilho na linha <paramref name="lin"/>: inclinada, o pé atrás do topo, como o
    /// skewX(-25°) da referência.
    /// </summary>
    internal static bool NoBrilho(Ponto p, double posicao) =>
        p.Col >= posicao - p.Lin * Inclinacao && p.Col < posicao - p.Lin * Inclinacao + 5;

    // ----------------------------------------------------------------------------------------

    /// <summary>Um slide com as casas de cada item já calculadas.</summary>
    private sealed class SlidePronto
    {
        private readonly List<(List<List<Ponto>> Letras, Tom Tom, double Opacidade, bool Texto, bool Brilha)> _itens = new();

        public bool TemBrilho { get; }
        private readonly int _letras;

        public SlidePronto(IReadOnlyList<ItemDeSlide> itens)
        {
            foreach (var item in itens)
            {
                switch (item)
                {
                    case TextoNoDisplay t:
                        _itens.Add((FonteDePontos.CelulasPorLetra(t.Texto, t.Col, t.Lin, t.Escala),
                            t.Apagado ? Tom.Apagado : Tom.Aceso, 1, true, t.Brilha));
                        break;
                    case PontosNoDisplay c:
                        _itens.Add((new List<List<Ponto>> { c.Pontos.ToList() }, c.Tom, c.Opacidade, false, false));
                        break;
                }
            }
            TemBrilho = _itens.Any(i => i.Brilha);
            _letras = _itens.Where(i => i.Texto).Sum(i => i.Letras.Count);
        }

        public void Inteiro(List<Camada> saida, Regiao regiao) =>
            Emitir(saida, regiao, _ => true, 0);

        public void Saindo(List<Camada> saida, Regiao regiao, Efeito efeito, double p)
        {
            if (efeito == Efeito.Empurra)
                Emitir(saida, regiao, _ => true, -(int)Math.Floor(p * regiao.Lins));
            else // varrida: o que a barra já passou some
            {
                var frente = regiao.Col + (int)Math.Floor(p * regiao.Cols);
                Emitir(saida, regiao, c => c.Col >= frente, 0);
            }
        }

        public void Entrando(List<Camada> saida, Regiao regiao, Efeito efeito, double local, double p)
        {
            if (local >= Digitacao || (local >= Entra && efeito != Efeito.Digita))
            {
                Inteiro(saida, regiao);
                return;
            }

            switch (efeito)
            {
                case Efeito.Empurra:
                    Emitir(saida, regiao, _ => true, regiao.Lins - (int)Math.Floor(p * regiao.Lins));
                    break;

                case Efeito.Varre:
                {
                    var frente = regiao.Col + (int)Math.Floor(p * regiao.Cols);
                    Emitir(saida, regiao, c => c.Col < frente, 0);

                    // a barra acesa na frente da varrida
                    var col = regiao.Col + (int)Math.Floor(p * (regiao.Cols - 2));
                    var barra = new List<Ponto>();
                    for (var r = regiao.Lin + 1; r < regiao.Lin + regiao.Lins - 1; r++)
                    {
                        barra.Add(new Ponto(col, r));
                        barra.Add(new Ponto(col + 1, r));
                    }
                    saida.Add(new Camada(barra, Tom.Aceso));
                    break;
                }

                case Efeito.Pisca:
                    if ((int)Math.Floor(p * 5) % 2 == 0) Inteiro(saida, regiao);
                    break;

                case Efeito.Faisca:
                {
                    var grupos = (int)Math.Floor(local / (Entra / 6)); // quantos grupos já acenderam
                    Emitir(saida, regiao, c => GrupoDaFaisca(c) < grupos, 0);
                    break;
                }

                case Efeito.Digita:
                {
                    var passo = Digitacao / Math.Max(1, _letras);
                    var mostradas = (int)Math.Floor(local / passo);
                    var n = 0;
                    foreach (var item in _itens)
                    {
                        if (!item.Texto)
                        {
                            Adicionar(saida, regiao, item.Letras[0], item.Tom, item.Opacidade, _ => true, 0);
                            continue;
                        }
                        foreach (var letra in item.Letras)
                            if (n++ < mostradas)
                                Adicionar(saida, regiao, letra, item.Tom, item.Opacidade, _ => true, 0);
                    }
                    break;
                }
            }
        }

        /// <summary>O texto que brilha, de novo, na cor do brilho onde a faixa está passando.</summary>
        public void Brilhar(List<Camada> saida, Regiao regiao, double posicao)
        {
            foreach (var item in _itens.Where(i => i.Brilha))
            {
                var casas = item.Letras.SelectMany(l => l).Where(c => regiao.Contem(c) && NoBrilho(c, posicao)).ToList();
                if (casas.Count > 0) saida.Add(new Camada(casas, Tom.Brilho));
            }
        }

        private void Emitir(List<Camada> saida, Regiao regiao, Func<Ponto, bool> mostra, int deslocamento)
        {
            foreach (var item in _itens)
                Adicionar(saida, regiao, item.Letras.SelectMany(l => l), item.Tom, item.Opacidade, mostra, deslocamento);
        }

        private static void Adicionar(List<Camada> saida, Regiao regiao, IEnumerable<Ponto> casas, Tom tom,
                                      double opacidade, Func<Ponto, bool> mostra, int deslocamento)
        {
            var lista = new List<Ponto>();
            foreach (var c in casas)
            {
                var d = new Ponto(c.Col, c.Lin + deslocamento);
                if (regiao.Contem(d) && mostra(d)) lista.Add(d);
            }
            if (lista.Count > 0) saida.Add(new Camada(lista, tom, opacidade));
        }
    }

    // ------------------------------------------------------------------------------- faísca

    private static readonly int[] TabelaDaFaisca = MontarFaisca();

    /// <summary>Um ladrilho de 16×16 repartido ao acaso (mas sempre igual) em 6 grupos.</summary>
    private static int[] MontarFaisca()
    {
        var semente = 20261117u;
        var tabela = new int[256];
        for (var i = 0; i < 256; i++)
        {
            semente = semente * 1664525u + 1013904223u;
            tabela[i] = (int)((semente >> 16) % 6);
        }
        return tabela;
    }

    /// <summary>Em que grupo da faísca a casa acende (0 a 5).</summary>
    public static int GrupoDaFaisca(Ponto p) => TabelaDaFaisca[(p.Lin & 15) * 16 + (p.Col & 15)];
}

/// <summary>
/// O anel dos dias ativos: sempre aceso por baixo, bem fraco, e a parte que conta enchendo uma
/// vez ao abrir, do topo, no sentido do relógio.
/// </summary>
public sealed class AnelQueEnche : IParteDoDisplay
{
    private const double Comeca = 0.2, Dura = 1.4;
    private const int Passos = 40;

    private readonly List<(Ponto Casa, double Angulo)> _casas;
    private readonly double _fracao;

    public AnelQueEnche(double cx, double cy, double raio, double espessura, double fracao)
    {
        _fracao = Math.Clamp(fracao, 0, 1);
        _casas = Casas(cx, cy, raio, espessura)
            .Select(c => (c, Angulo(c, cx, cy)))
            .ToList();
    }

    /// <summary>As casas de um anel de raio externo e espessura dados, em pontos.</summary>
    public static List<Ponto> Casas(double cx, double cy, double raio, double espessura)
    {
        var casas = new List<Ponto>();
        for (var r = (int)Math.Floor(cy - raio); r <= (int)Math.Ceiling(cy + raio); r++)
            for (var c = (int)Math.Floor(cx - raio); c <= (int)Math.Ceiling(cx + raio); c++)
            {
                var d = Math.Sqrt(Math.Pow(c + 0.5 - cx, 2) + Math.Pow(r + 0.5 - cy, 2));
                if (d <= raio && d > raio - espessura) casas.Add(new Ponto(c, r));
            }
        return casas;
    }

    /// <summary>Ângulo da casa a partir do topo, no sentido do relógio, de 0 a 360.</summary>
    private static double Angulo(Ponto c, double cx, double cy)
    {
        var a = Math.Atan2(c.Col + 0.5 - cx, -(c.Lin + 0.5 - cy)) * 180 / Math.PI;
        return a < 0 ? a + 360 : a;
    }

    private int Passo(double? t) => t is null
        ? Passos
        : (int)Math.Clamp(Math.Floor((t.Value - Comeca) / Dura * Passos), 0, Passos);

    public long Estado(double t) => Passo(t);

    public void Desenhar(double? t, List<Camada> saida)
    {
        var limite = 360 * _fracao * Passo(t) / Passos;
        saida.Add(new Camada(_casas.Select(c => c.Casa).ToList(), Tom.Destaque, 0.22));
        var cheias = _casas.Where(c => c.Angulo < limite).Select(c => c.Casa).ToList();
        if (cheias.Count > 0) saida.Add(new Camada(cheias, Tom.Destaque));
    }
}

/// <summary>
/// A barra de linguagens: uma só, que cresce ou encolhe até a fatia de cada linguagem, de ponto
/// em ponto, junto com a entrada do slide.
/// </summary>
public sealed class BarraQueCresce : IParteDoDisplay
{
    private readonly Regiao _barra;
    private readonly int[] _larguras;

    public BarraQueCresce(Regiao barra, IReadOnlyList<double> fracoes)
    {
        _barra = barra;
        _larguras = fracoes.Select(f => Math.Max(1, (int)Math.Round(f * barra.Cols))).ToArray();
    }

    public int Largura(double? t)
    {
        if (_larguras.Length == 0) return 0;
        if (t is null) return _larguras[0];

        var ciclo = _larguras.Length * Slideshow.Exibe;
        var tt = ((t.Value % ciclo) + ciclo) % ciclo;
        var i = Math.Min(_larguras.Length - 1, (int)(tt / Slideshow.Exibe));
        var local = tt - i * Slideshow.Exibe;
        var antes = i == 0 ? 0 : _larguras[i - 1];
        var p = Math.Clamp(local / Slideshow.Entra, 0, 1);
        return antes + (int)Math.Round((_larguras[i] - antes) * p);
    }

    public long Estado(double t) => Largura(t);

    public void Desenhar(double? t, List<Camada> saida)
    {
        var fundo = new List<Ponto>();
        var acesa = new List<Ponto>();
        var largura = Largura(t);
        for (var r = _barra.Lin; r < _barra.Lin + _barra.Lins; r++)
            for (var c = _barra.Col; c < _barra.Col + _barra.Cols; c++)
                (c < _barra.Col + largura ? acesa : fundo).Add(new Ponto(c, r));

        saida.Add(new Camada(fundo, Tom.Destaque, 0.22));
        if (acesa.Count > 0) saida.Add(new Camada(acesa, Tom.Destaque));
    }
}

/// <summary>
/// Um quadro inteiro que acende aos poucos, em grupos de pontos ao acaso, fica e apaga de uma
/// vez para acender de novo: o ritmo semanal, que não tem slides.
/// </summary>
public sealed class AcendeAosPoucos : IParteDoDisplay
{
    private const double Acende = 0.6, Fica = 10;
    private readonly IReadOnlyList<Camada> _conteudo;

    public AcendeAosPoucos(IReadOnlyList<Camada> conteudo) => _conteudo = conteudo;

    public static double Ciclo => Acende + Fica;

    private static int Grupos(double? t)
    {
        if (t is null) return 6;
        var tt = t.Value % Ciclo;
        return (int)Math.Min(6, Math.Floor(tt / (Acende / 6)));
    }

    public long Estado(double t) => Grupos(t);

    public void Desenhar(double? t, List<Camada> saida)
    {
        var grupos = Grupos(t);
        if (grupos >= 6)
        {
            saida.AddRange(_conteudo);
            return;
        }
        foreach (var c in _conteudo)
        {
            var casas = c.Pontos.Where(p => Slideshow.GrupoDaFaisca(p) < grupos).ToList();
            if (casas.Count > 0) saida.Add(c with { Pontos = casas });
        }
    }
}

/// <summary>
/// Um quadro retrô: 150 × 73 pontos, o tamanho dos cartões do github-cool-readme, montado de
/// partes que se desenham sozinhas. Quem o mostra só precisa do tempo.
/// </summary>
public sealed class DisplayDeLeds
{
    public const int Colunas = 150;
    public const int Linhas = 73;

    /// <summary>A linha do título, no alto de todo quadro.</summary>
    public const int LinhaDoTitulo = 3;

    public IReadOnlyList<IParteDoDisplay> Partes { get; }

    /// <summary>O que o quadro diz, por extenso: vai na dica, já que os números se revezam.</summary>
    public string Descricao { get; }

    public DisplayDeLeds(IReadOnlyList<IParteDoDisplay> partes, string descricao)
    {
        Partes = partes;
        Descricao = descricao;
    }

    public long Estado(double t)
    {
        var h = new HashCode();
        foreach (var p in Partes) h.Add(p.Estado(t));
        return h.ToHashCode();
    }

    /// <summary>Tudo o que está aceso em <paramref name="t"/>; nulo é o quadro parado.</summary>
    public List<Camada> Camadas(double? t)
    {
        var saida = new List<Camada>();
        foreach (var p in Partes) p.Desenhar(t, saida);
        return saida;
    }

    /// <summary>O título apagado na linha de cima, cortado para caber.</summary>
    public static Camada Titulo(string titulo) =>
        new(FonteDePontos.Celulas(FonteDePontos.Caber(titulo.ToUpperInvariant(), Colunas - 8), 4, LinhaDoTitulo),
            Tom.Apagado);
}
