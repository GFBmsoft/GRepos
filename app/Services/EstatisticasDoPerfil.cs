using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GRepos.Services;

/// <summary>
/// Os totais do último ano como o GitHub conta, com as mesmas regras do quadriculado do perfil
/// (commit só conta na branch padrão, por exemplo).
/// </summary>
public sealed class ResumoDeContribuicoes
{
    public int Commits { get; set; }
    public int PullRequests { get; set; }
    public int Revisoes { get; set; }
    public int Issues { get; set; }

    /// <summary>
    /// O que o GitHub conta mas não detalha: contribuições em repositórios que o token não
    /// enxerga. O token do Git Credential Manager não tem <c>read:org</c>, e os commits nos
    /// repositórios da organização chegam só como este total. Entram no quadriculado, não
    /// nas linhas acima.
    /// </summary>
    public int Privadas { get; set; }
}

/// <summary>Um dia da semana no ritmo semanal: o total do ano naquele dia.</summary>
public sealed record DiaDaSemana(string Nome, string NomeCompleto, int Quantidade, bool Util);

public sealed record RitmoSemanal(
    IReadOnlyList<DiaDaSemana> Dias, DiaDaSemana MaisCheio, DiaDaSemana MaisVazio, double FracaoUteis);

/// <summary>
/// Sequências de dias com contribuição, contadas até o último dia do calendário. A atual não
/// quebra por hoje ainda estar vazio: o dia não acabou, como no GitHub.
/// </summary>
public sealed record Sequencias(
    int Atual, DateTime? InicioDaAtual,
    int Maior, DateTime? InicioDaMaior,
    DateTime? MelhorDia, int NoMelhorDia,
    DateTime? MelhorMes, int NoMelhorMes,
    int DiasAtivos, int DiasNoPeriodo)
{
    public double FracaoAtivos => DiasNoPeriodo == 0 ? 0 : (double)DiasAtivos / DiasNoPeriodo;

    /// <summary>Se o dia faz parte da sequência atual ou da maior: são os destacados no gráfico.</summary>
    public bool EmSequencia(DateTime dia) =>
        Dentro(dia, InicioDaAtual, Atual) || Dentro(dia, InicioDaMaior, Maior);

    private static bool Dentro(DateTime dia, DateTime? inicio, int dias) =>
        inicio is { } i && dia.Date >= i && dia.Date < i.AddDays(dias);
}

/// <summary>Uma fatia da barra de linguagens: a fração das linhas e a cor do GitHub.</summary>
public sealed record FatiaDeLinguagem(string Nome, string Cor, double Fracao);

/// <summary>
/// A cor de destaque dos quadros, como o <c>color:</c> do github-cool-readme: pinta as barras do
/// ritmo semanal, o destaque das sequências e o anel dos dias ativos. As cores são recursos do
/// tema, para valer no claro e no escuro.
/// </summary>
public sealed record CorDeDestaque(string Id, string Nome, string Barras, string Anel);

public static class CoresDeDestaque
{
    /// <summary>
    /// "GitHub" é a de fábrica: o verde do quadriculado nas barras e o azul no anel. O azul é o
    /// da trilha 0 do grafo, não o destaque do app, que o usuário troca nas preferências.
    /// </summary>
    public static readonly IReadOnlyList<CorDeDestaque> Todas = new[]
    {
        new CorDeDestaque("github", "GitHub", "Green", "Lane0"),
        new CorDeDestaque("ambar", "Âmbar", "Yellow", "Yellow"),
        new CorDeDestaque("verde", "Verde", "Green", "Green"),
        new CorDeDestaque("roxo", "Roxo", "Purple", "Purple"),
        new CorDeDestaque("ciano", "Ciano", "Lane7", "Lane7"),
        new CorDeDestaque("vermelho", "Vermelho", "Red", "Red"),
        new CorDeDestaque("azul", "Azul", "Lane0", "Lane0"),
    };

    /// <summary>A cor pelo id; desconhecido ou vazio (workspace de versão anterior) é a GitHub.</summary>
    public static CorDeDestaque De(string? id) =>
        Todas.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Todas[0];
}

/// <summary>
/// Os quadros do cartão de perfil inspirados no github-cool-readme (ritmo semanal, sequências
/// e linguagens), calculados do que já se tem: o calendário de contribuições e as linhas dos
/// seus commits. Lógica pura, sem rede nem tela.
/// </summary>
public static class EstatisticasDoPerfil
{
    private static readonly (string Nome, string Completo)[] NomesDaSemana =
    {
        ("seg", "segunda"), ("ter", "terça"), ("qua", "quarta"), ("qui", "quinta"),
        ("sex", "sexta"), ("sáb", "sábado"), ("dom", "domingo"),
    };

    /// <summary>
    /// O total de cada dia da semana no ano, de segunda a domingo. Nulo quando o ano está vazio:
    /// sem nada feito, "dia mais cheio" seria só o primeiro da lista.
    /// </summary>
    public static RitmoSemanal? Ritmo(IReadOnlyList<DiaContribuicao>? dias)
    {
        if (dias is null || dias.Count == 0) return null;

        var totais = new int[7];
        foreach (var d in dias)
            totais[((int)d.Data.DayOfWeek + 6) % 7] += d.Quantidade; // segunda é o 0

        var total = totais.Sum();
        if (total == 0) return null;

        var semana = Enumerable.Range(0, 7)
            .Select(i => new DiaDaSemana(NomesDaSemana[i].Nome, NomesDaSemana[i].Completo, totais[i], i < 5))
            .ToList();

        // no empate vale o primeiro da semana, para o resultado não depender de ordenação
        var maisCheio = semana.Aggregate((a, b) => b.Quantidade > a.Quantidade ? b : a);
        var maisVazio = semana.Aggregate((a, b) => b.Quantidade < a.Quantidade ? b : a);

        return new RitmoSemanal(semana, maisCheio, maisVazio, (double)totais.Take(5).Sum() / total);
    }

    /// <summary>
    /// Sequência atual e maior, melhor dia e melhor mês. A referência é o último dia do
    /// calendário (o GitHub manda até hoje), não o relógio: com o calendário guardado de
    /// ontem, a conta continua coerente com o que está na tela.
    /// </summary>
    public static Sequencias Sequencias(IReadOnlyList<DiaContribuicao>? dias)
    {
        if (dias is null || dias.Count == 0)
            return new Sequencias(0, null, 0, null, null, 0, null, 0, 0, 0);

        var porData = dias.GroupBy(d => d.Data.Date)
                          .ToDictionary(g => g.Key, g => g.Sum(d => d.Quantidade));
        var ordenados = porData.OrderBy(p => p.Key).ToList();
        var ultimo = ordenados[^1].Key;

        // a maior: no empate fica a mais recente, que é a que o gráfico mostra mais à direita
        int maior = 0, corrida = 0;
        DateTime? inicioDaMaior = null, inicioDaCorrida = null, anterior = null;
        foreach (var (data, q) in ordenados)
        {
            if (q > 0)
            {
                if (corrida > 0 && anterior == data.AddDays(-1)) corrida++;
                else { corrida = 1; inicioDaCorrida = data; }

                if (corrida >= maior) { maior = corrida; inicioDaMaior = inicioDaCorrida; }
            }
            else corrida = 0;

            anterior = data;
        }

        // a atual: hoje vazio ainda não quebra, conta-se a partir de ontem
        var dia = ultimo;
        if (porData.GetValueOrDefault(dia) == 0) dia = dia.AddDays(-1);
        var atual = 0;
        while (porData.TryGetValue(dia, out var q) && q > 0)
        {
            atual++;
            dia = dia.AddDays(-1);
        }

        var ativos = ordenados.Where(p => p.Value > 0).ToList();
        var melhorDia = ativos.OrderByDescending(p => p.Value).ThenByDescending(p => p.Key)
                              .Select(p => (KeyValuePair<DateTime, int>?)p).FirstOrDefault();

        var melhorMes = ativos.GroupBy(p => new DateTime(p.Key.Year, p.Key.Month, 1))
                              .Select(g => (Mes: g.Key, Total: g.Sum(p => p.Value)))
                              .OrderByDescending(m => m.Total).ThenByDescending(m => m.Mes)
                              .Select(m => ((DateTime, int)?)m).FirstOrDefault();

        return new Sequencias(
            atual, atual > 0 ? dia.AddDays(1) : null,
            maior, inicioDaMaior,
            melhorDia?.Key, melhorDia?.Value ?? 0,
            melhorMes?.Item1, melhorMes?.Item2 ?? 0,
            ativos.Count, ordenados.Count);
    }

    // ------------------------------------------------------------ linguagens

    /// <summary>
    /// Extensão para linguagem, com os nomes do GitHub (linguist). Só código: Markdown, JSON,
    /// XML, projetos e formulários de relatório ficam de fora, como na barra do GitHub. O .dfm
    /// e o .inc contam como Pascal, como o linguist conta.
    /// </summary>
    private static readonly Dictionary<string, string> PorExtensao = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pas"] = "Pascal", [".dpr"] = "Pascal", [".dpk"] = "Pascal", [".dfm"] = "Pascal",
        [".inc"] = "Pascal", [".lpr"] = "Pascal", [".pp"] = "Pascal",
        [".cs"] = "C#", [".csx"] = "C#",
        [".go"] = "Go",
        [".sql"] = "SQL",
        [".ps1"] = "PowerShell", [".psm1"] = "PowerShell", [".psd1"] = "PowerShell",
        [".sh"] = "Shell", [".bash"] = "Shell",
        [".bat"] = "Batchfile", [".cmd"] = "Batchfile",
        [".js"] = "JavaScript", [".mjs"] = "JavaScript", [".cjs"] = "JavaScript",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript",
        [".html"] = "HTML", [".htm"] = "HTML",
        [".css"] = "CSS",
        [".py"] = "Python",
        [".lua"] = "Lua",
        [".c"] = "C", [".h"] = "C",
        [".cpp"] = "C++", [".cc"] = "C++", [".cxx"] = "C++", [".hpp"] = "C++",
        [".java"] = "Java",
        [".php"] = "PHP",
        [".rs"] = "Rust",
        [".kt"] = "Kotlin",
        [".dart"] = "Dart",
        [".rb"] = "Ruby",
    };

    /// <summary>
    /// As cores que o GitHub dá a cada linguagem (linguist). PowerShell (#012456) e Lua (#000080)
    /// são quase pretas e sumiam no tema escuro: vão com o mesmo azul, mais claro.
    /// </summary>
    private static readonly Dictionary<string, string> Cores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pascal"] = "#E3F171", ["C#"] = "#178600", ["Go"] = "#00ADD8", ["SQL"] = "#E38C00",
        ["PowerShell"] = "#5391FE", ["Shell"] = "#89E051", ["Batchfile"] = "#C1F12E",
        ["JavaScript"] = "#F1E05A", ["TypeScript"] = "#3178C6", ["HTML"] = "#E34C26",
        ["CSS"] = "#663399", ["Python"] = "#3572A5", ["Lua"] = "#5A6FD6", ["C"] = "#555555",
        ["C++"] = "#F34B7D", ["Java"] = "#B07219", ["PHP"] = "#4F5D95", ["Rust"] = "#DEA584",
        ["Kotlin"] = "#A97BFF", ["Dart"] = "#00B4AB", ["Ruby"] = "#701516",
    };

    /// <summary>A cor de "Outras" e de linguagem sem cor conhecida.</summary>
    public const string CorNeutra = "#8B949E";

    public static string CorDe(string linguagem) => Cores.GetValueOrDefault(linguagem, CorNeutra);

    /// <summary>
    /// A linguagem de um caminho do <c>git log --numstat</c>, ou nulo se não é código. Entende
    /// o caminho renomeado ("pasta/{velho.pas => novo.pas}") e o caminho entre aspas que o git
    /// usa para nome com acento.
    /// </summary>
    public static string? LinguagemDoCaminho(string caminho)
    {
        var p = caminho.Trim();
        var seta = p.LastIndexOf("=>", StringComparison.Ordinal);
        if (seta >= 0) p = p[(seta + 2)..];

        p = p.Replace("}", "").Trim().Trim('"');
        return PorExtensao.GetValueOrDefault(Path.GetExtension(p));
    }

    /// <summary>
    /// As fatias da barra, da maior para a menor. Passando de <paramref name="maximo"/>, as
    /// menores viram "Outras", e a barra continua somando 100%.
    /// </summary>
    public static List<FatiaDeLinguagem> Fatias(IReadOnlyDictionary<string, long>? linhas, int maximo = 6)
    {
        if (linhas is null) return new List<FatiaDeLinguagem>();

        var ordem = linhas.Where(p => p.Value > 0)
                          .OrderByDescending(p => p.Value)
                          .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                          .ToList();
        double total = ordem.Sum(p => p.Value);
        if (total <= 0) return new List<FatiaDeLinguagem>();

        var mostradas = ordem.Count <= maximo ? ordem : ordem.Take(maximo - 1).ToList();
        var fatias = mostradas.Select(p => new FatiaDeLinguagem(p.Key, CorDe(p.Key), p.Value / total)).ToList();

        if (mostradas.Count < ordem.Count)
            fatias.Add(new FatiaDeLinguagem("Outras", CorNeutra,
                ordem.Skip(mostradas.Count).Sum(p => p.Value) / total));

        return fatias;
    }
}
