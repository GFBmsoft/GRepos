using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GRepos.Services;

/// <summary>
/// As cores de um display retrô. Cada valor é um recurso do tema ("Text", "Lane0") ou um hex:
/// "GitHub" segue o tema do app; as outras cores são as de um display de verdade, claras sobre o
/// painel escuro, iguais no tema claro e no escuro.
/// </summary>
public sealed record CoresDoDisplay(
    string Aceso, string Destaque, string Brilho, double Apagado,
    string? Painel, string Apagados, double ForcaDosApagados)
{
    /// <summary>Uma cor de display: o aceso, o brilho mais claro e o painel quase preto.</summary>
    public static CoresDoDisplay DePainel(string aceso, string brilho, string painel, double apagados) =>
        new(aceso, aceso, brilho, 0.55, painel, aceso, apagados);
}

/// <summary>
/// Os quadros do cartão de perfil no estilo retrô, como os "dot matrix" e "OLED" do
/// github-cool-readme: o mesmo desenho de 150 × 73 pontos e os mesmos lugares de cada coisa, com
/// os textos em português.
/// </summary>
public static class DisplaysDoPerfil
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(long n) => n.ToString("N0", PtBr);

    private static string Percentual(double fracao, int casas = 0) =>
        (fracao * 100).ToString(casas == 0 ? "0" : "0.0", PtBr) + "%";

    private static string EmDias(int n) => n == 1 ? "1 DIA" : $"{N(n)} DIAS";

    private static IReadOnlyList<ItemDeSlide> Slide(params ItemDeSlide[] itens) => itens;

    // ------------------------------------------------------------------------- resumo

    /// <summary>Os números se revezam à esquerda; o anel dos dias ativos fica aceso à direita.</summary>
    public static DisplayDeLeds? DoResumo(ResumoDeContribuicoes? resumo, int estrelas, Sequencias sequencias)
    {
        if (resumo is null) return null;

        var numeros = new List<(string Rotulo, long Valor, string Detalhe)>
        {
            ("COMMITS", resumo.Commits, "12 MESES"),
            ("PULL REQUESTS", resumo.PullRequests, "12 MESES"),
            ("REVISÕES", resumo.Revisoes, "12 MESES"),
            ("ISSUES", resumo.Issues, "12 MESES"),
        };
        if (resumo.Privadas > 0) numeros.Add(("PRIVADAS", resumo.Privadas, "SEM DETALHE"));
        if (estrelas >= 0) numeros.Add(("ESTRELAS", estrelas, "NA CONTA"));

        // o nome do número fica na linha do título, como os títulos dos outros quadros
        var slides = numeros.Select(n => Slide(
                new TextoNoDisplay(n.Rotulo, 4, DisplayDeLeds.LinhaDoTitulo),
                new TextoNoDisplay(N(n.Valor), 4, 23, 2, Brilha: true),
                new TextoNoDisplay(n.Detalhe, 4, 45, Apagado: true)))
            .ToList();
        var show = new Slideshow(slides, new Regiao(0, 1, 80, 71),
            new[] { Efeito.Faisca, Efeito.Empurra, Efeito.Varre, Efeito.Pisca, Efeito.Digita });

        const double cx = 113.5, cy = 41.5;
        const string titulo = "DIAS ATIVOS";
        var pct = Percentual(sequencias.FracaoAtivos);
        var fixas = new[]
        {
            new Camada(FonteDePontos.Celulas(titulo,
                (int)Math.Round(cx - FonteDePontos.Largura(titulo) / 2.0), DisplayDeLeds.LinhaDoTitulo), Tom.Apagado),
            new Camada(FonteDePontos.Celulas(pct,
                (int)Math.Round(cx - FonteDePontos.Largura(pct)), (int)Math.Round(cy - 7), 2), Tom.Aceso),
        };

        var descricao = string.Join(" ", numeros.Select(n => $"{Capitalizar(n.Rotulo)}: {N(n.Valor)}.")) +
                        $" Dias ativos: {pct} do último ano.";
        return new DisplayDeLeds(
            new IParteDoDisplay[] { show, new AnelQueEnche(cx, cy, 24.5, 3, sequencias.FracaoAtivos), new PartesFixas(fixas) },
            descricao);
    }

    // --------------------------------------------------------------------- linguagens

    /// <summary>Uma linguagem por vez, e uma barra só que cresce e encolhe até a fatia de cada uma.</summary>
    public static DisplayDeLeds? DasLinguagens(IReadOnlyList<FatiaDeLinguagem> fatias)
    {
        if (fatias.Count == 0) return null;

        var slides = fatias.Select((f, i) =>
        {
            var contador = $"{i + 1}/{fatias.Count}";
            var colContador = DisplayDeLeds.Colunas - 4 - FonteDePontos.Largura(contador);
            return Slide(
                new TextoNoDisplay(FonteDePontos.Caber(f.Nome.ToUpperInvariant(), colContador - 8), 4, 21),
                new TextoNoDisplay(contador, colContador, 21, Apagado: true),
                new TextoNoDisplay(Percentual(f.Fracao, 1), 4, 32, 2, Brilha: true));
        }).ToList();

        var show = new Slideshow(slides, new Regiao(0, 18, DisplayDeLeds.Colunas, 31), new[] { Efeito.Empurra, Efeito.Varre });
        var barra = new BarraQueCresce(new Regiao(4, 54, DisplayDeLeds.Colunas - 8, 6), fatias.Select(f => f.Fracao).ToList());

        return new DisplayDeLeds(
            new IParteDoDisplay[] { new PartesFixas(new[] { DisplayDeLeds.Titulo("Linguagens") }), show, barra },
            string.Join(", ", fatias.Select(f => $"{f.Nome} {Percentual(f.Fracao, 1)}")));
    }

    // ------------------------------------------------------------------ ritmo semanal

    private static readonly string[] Iniciais = { "S", "T", "Q", "Q", "S", "S", "D" };

    /// <summary>
    /// As barras dos sete dias à esquerda e, à direita, o dia mais cheio, o mais vazio e quanto cai
    /// em dias úteis. Acende aos poucos, fica e apaga para acender de novo.
    /// </summary>
    public static DisplayDeLeds? DoRitmo(RitmoSemanal? ritmo)
    {
        if (ritmo is null) return null;

        const int slot = 12, larguraDaBarra = 8, baseDasBarras = 58, alturaMaxima = 43, linhaDasIniciais = 61;
        const int colDosNumeros = 4 + 7 * slot + 8;
        var maximo = Math.Max(1, ritmo.MaisCheio.Quantidade);

        var barras = new List<Ponto>();
        var iniciais = new List<Ponto>();
        for (var k = 0; k < ritmo.Dias.Count; k++)
        {
            var centro = 4 + k * slot + slot / 2.0;
            var q = ritmo.Dias[k].Quantidade;
            var linhas = q == 0 ? 0 : Math.Max(1, (int)Math.Round((double)q / maximo * alturaMaxima));
            var col = (int)Math.Round(centro - larguraDaBarra / 2.0);
            for (var r = baseDasBarras - linhas; r < baseDasBarras; r++)
                for (var c = 0; c < larguraDaBarra; c++)
                    barras.Add(new Ponto(col + c, r));

            var letra = Iniciais[k];
            iniciais.AddRange(FonteDePontos.Celulas(letra,
                (int)Math.Round(centro - FonteDePontos.Largura(letra) / 2.0), linhaDasIniciais));
        }

        // o nome apagado e o valor embaixo, com espaço para o acento de "SÁB" e "ÚTEIS"
        var nomes = new List<Ponto>();
        var valores = new List<Ponto>();
        var linhas3 = new[] { 8, 26, 44 };
        var numeros = new[]
        {
            ("CHEIO", ritmo.MaisCheio.Nome.ToUpperInvariant()),
            ("VAZIO", ritmo.MaisVazio.Nome.ToUpperInvariant()),
            ("ÚTEIS", Percentual(ritmo.FracaoUteis)),
        };
        for (var i = 0; i < numeros.Length; i++)
        {
            nomes.AddRange(FonteDePontos.Celulas(numeros[i].Item1, colDosNumeros, linhas3[i]));
            valores.AddRange(FonteDePontos.Celulas(numeros[i].Item2, colDosNumeros, linhas3[i] + 9));
        }

        var conteudo = new[]
        {
            new Camada(barras, Tom.Destaque),
            new Camada(iniciais, Tom.Apagado),
            new Camada(valores, Tom.Aceso),
            new Camada(nomes, Tom.Apagado),
        };

        return new DisplayDeLeds(
            new IParteDoDisplay[] { new PartesFixas(new[] { DisplayDeLeds.Titulo("Ritmo semanal") }), new AcendeAosPoucos(conteudo) },
            $"Mais cheio: {ritmo.MaisCheio.NomeCompleto}. Mais vazio: {ritmo.MaisVazio.NomeCompleto}. " +
            $"Em dias úteis: {Percentual(ritmo.FracaoUteis)}.");
    }

    // ---------------------------------------------------------------------- sequências

    /// <summary>Quantos dias a faixa de baixo mostra: um por coluna, de ponta a ponta.</summary>
    public const int DiasNaFaixa = DisplayDeLeds.Colunas - 8;

    private const int BaseDaFaixa = 64, AlturaDaFaixa = 7;

    /// <summary>
    /// Um número por vez, como o resumo, cada um sobre uma faixa de dias que mostra do que ele
    /// fala: a sequência atual, a maior onde quer que tenha sido, os dias ativos, o melhor dia e o
    /// melhor mês.
    /// </summary>
    public static DisplayDeLeds? DasSequencias(IReadOnlyList<DiaContribuicao>? dias, Sequencias s)
    {
        if (dias is null || dias.Count == 0 || s.DiasAtivos == 0) return null;

        var ordenados = dias.OrderBy(d => d.Data).ToList();
        var n = ordenados.Count;
        var recentes = Math.Max(0, n - DiasNaFaixa);
        int Indice(DateTime? data) => data is { } d ? ordenados.FindIndex(x => x.Data.Date == d.Date) : -1;

        int Janela(int primeiro, int ultimo) =>
            Math.Max(0, Math.Min(n - DiasNaFaixa, (int)Math.Round((primeiro + ultimo) / 2.0 - DiasNaFaixa / 2.0)));

        Func<int, int, bool> Entre(int inicio, int dias) => (i, _) => inicio >= 0 && i >= inicio && i < inicio + dias;

        var atual = Indice(s.InicioDaAtual);
        var maior = Indice(s.InicioDaMaior);
        var melhor = Indice(s.MelhorDia);
        var mes = s.MelhorMes is { } m
            ? Enumerable.Range(0, n).Where(i => ordenados[i].Data.Year == m.Year && ordenados[i].Data.Month == m.Month).ToList()
            : new List<int>();

        var faixas = new[]
        {
            Faixa(ordenados, recentes, Entre(atual, s.Atual)),
            Faixa(ordenados, maior >= 0 ? Janela(maior, maior + s.Maior - 1) : recentes, Entre(maior, s.Maior)),
            Faixa(ordenados, recentes, (_, q) => q > 0),
            Faixa(ordenados, melhor >= 0 ? Janela(melhor, melhor) : recentes, (i, _) => i == melhor),
            Faixa(ordenados, mes.Count > 0 ? Janela(mes[0], mes[^1]) : recentes, (i, _) => mes.Contains(i)),
        };

        var mesTexto = s.MelhorMes is { } mm
            ? mm.ToString("MMM", PtBr).Replace(".", "").ToUpperInvariant() + "/" + mm.Year
            : null;
        var numeros = new (string Rotulo, string Valor, string? Detalhe)[]
        {
            ("SEQUÊNCIA ATUAL", EmDias(s.Atual), null),
            ("MAIOR SEQUÊNCIA", EmDias(s.Maior), null),
            ("DIAS ATIVOS", N(s.DiasAtivos), $"DE {N(s.DiasNoPeriodo)} DIAS"),
            ("MELHOR DIA", N(s.NoMelhorDia), s.MelhorDia?.ToString("dd/MM/yyyy", PtBr)),
            ("MELHOR MÊS", N(s.NoMelhorMes), mesTexto),
        };

        var slides = numeros.Select((t, i) =>
        {
            var contador = $"{i + 1}/{numeros.Length}";
            var colContador = DisplayDeLeds.Colunas - 4 - FonteDePontos.Largura(contador);
            var itens = new List<ItemDeSlide>
            {
                new TextoNoDisplay(FonteDePontos.Caber(t.Rotulo, colContador - 8), 4, 17),
                new TextoNoDisplay(contador, colContador, 17, Apagado: true),
                new TextoNoDisplay(FonteDePontos.Caber(t.Valor, (DisplayDeLeds.Colunas - 8) / 2), 4, 28, 2, Brilha: true),
            };
            if (t.Detalhe is not null)
                itens.Add(new TextoNoDisplay(FonteDePontos.Caber(t.Detalhe, DisplayDeLeds.Colunas - 8), 4, 45, Apagado: true));
            itens.AddRange(faixas[i]);
            return (IReadOnlyList<ItemDeSlide>)itens;
        }).ToList();

        var show = new Slideshow(slides, new Regiao(0, 14, DisplayDeLeds.Colunas, 52),
            new[] { Efeito.Empurra, Efeito.Varre, Efeito.Pisca });

        // a linha de base da faixa fica parada
        var linhaDeBase = Enumerable.Range(4, DiasNaFaixa).Select(c => new Ponto(c, BaseDaFaixa)).ToList();

        return new DisplayDeLeds(
            new IParteDoDisplay[]
            {
                new PartesFixas(new[] { DisplayDeLeds.Titulo("Sequências"), new Camada(linhaDeBase, Tom.Destaque, 0.22) }),
                show,
            },
            string.Join(" ", numeros.Select(t => $"{Capitalizar(t.Rotulo)}: {t.Valor.ToLowerInvariant()}" +
                                                 (t.Detalhe is null ? "." : $" ({t.Detalhe.ToLowerInvariant()})."))));
    }

    /// <summary>
    /// Uma faixa de dias a partir de <paramref name="inicio"/>: a barra da altura do dia (pela raiz,
    /// para uns poucos dias cheios não achatarem o resto), acesa onde <paramref name="acesa"/> diz e
    /// fraca no resto.
    /// </summary>
    private static ItemDeSlide[] Faixa(IReadOnlyList<DiaContribuicao> dias, int inicio, Func<int, int, bool> acesa)
    {
        inicio = Math.Max(0, inicio);
        var trecho = dias.Skip(inicio).Take(DiasNaFaixa).ToList();
        var maximo = Math.Max(1, trecho.Count == 0 ? 1 : trecho.Max(d => d.Quantidade));

        var acesos = new List<Ponto>();
        var fracos = new List<Ponto>();
        for (var i = 0; i < trecho.Count; i++)
        {
            var q = trecho[i].Quantidade;
            var altura = q == 0 ? 0 : Math.Max(1, (int)Math.Round(Math.Sqrt((double)q / maximo) * AlturaDaFaixa));
            var col = 4 + DiasNaFaixa - trecho.Count + i;
            var destino = acesa(inicio + i, q) ? acesos : fracos;
            for (var r = 0; r < altura; r++) destino.Add(new Ponto(col, BaseDaFaixa - 1 - r));
        }

        return new ItemDeSlide[]
        {
            new PontosNoDisplay(acesos, Tom.Destaque),
            new PontosNoDisplay(fracos, Tom.Destaque, 0.25),
        };
    }

    private static string Capitalizar(string s) =>
        s.Length == 0 ? s : s[0] + s[1..].ToLowerInvariant();
}
