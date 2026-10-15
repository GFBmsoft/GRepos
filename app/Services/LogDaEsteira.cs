using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GRepos.Services;

/// <summary>O trecho do log de um passo, pronto para a tela.</summary>
public sealed class LogDoPasso
{
    public string Texto { get; init; } = "";

    /// <summary>As linhas de erro (##[error]), que vão em destaque acima do log.</summary>
    public List<string> Erros { get; init; } = new();

    /// <summary>Quantas linhas ficaram de fora do começo, por o passo ser longo demais.</summary>
    public int Cortadas { get; init; }
}

/// <summary>
/// Log de um job do GitHub Actions. A API entrega o job inteiro num texto só, cada
/// linha começando pelo horário; o passo é recortado pelos horários de início dos
/// passos — é a mesma informação que a tela já tem.
///
/// Lógica pura, sem rede, para ser testada com um log de verdade.
/// </summary>
public static class LogDaEsteira
{
    /// <summary>Mais que isso trava a tela e não ajuda: o erro está no fim.</summary>
    public const int LimiteDeLinhas = 500;

    private static readonly Regex Horario = new(
        @"^\uFEFF?(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z) ?(.*)$", RegexOptions.Compiled);

    /// <summary>Cores e cursor do terminal (ESC[...m) — no log em texto são só sujeira.</summary>
    private static readonly Regex Ansi = new(@"\x1B\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    public readonly record struct Linha(DateTime? Quando, string Texto);

    /// <summary>Linha sem horário (texto que quebrou) herda o horário da anterior.</summary>
    public static List<Linha> Ler(string log)
    {
        var linhas = new List<Linha>();
        DateTime? ultimo = null;
        foreach (var bruta in log.Replace("\r\n", "\n").Split('\n'))
        {
            var m = Horario.Match(bruta);
            if (m.Success &&
                DateTime.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var quando))
            {
                ultimo = quando;
                linhas.Add(new Linha(quando, m.Groups[2].Value));
            }
            else if (bruta.Length > 0)
            {
                linhas.Add(new Linha(ultimo, bruta));
            }
        }
        return linhas;
    }

    /// <summary>
    /// As linhas do passo: do início dele até o início do próximo passo que rodou. A API
    /// dá os horários dos passos em segundos inteiros; o corte é no segundo.
    /// </summary>
    public static LogDoPasso DoPasso(string log, IReadOnlyList<CiEtapa> etapas, int numero)
    {
        var etapa = etapas.FirstOrDefault(e => e.Numero == numero);
        if (etapa?.Inicio is not { } inicio) return new LogDoPasso();

        var proximo = etapas
            .Where(e => e.Numero > numero && e.Inicio is not null)
            .OrderBy(e => e.Numero)
            .Select(e => e.Inicio)
            .FirstOrDefault();

        var de = Segundo(inicio);
        var ate = proximo is { } p ? Segundo(p) : (DateTime?)null;

        var linhas = Ler(log)
            .Where(l => l.Quando is { } q && q >= de && (ate is null || q < ate))
            .Select(l => l.Texto);
        return Formatar(linhas);
    }

    /// <summary>O job inteiro, limpo, para abrir no editor.</summary>
    public static string Inteiro(string log) =>
        string.Join(Environment.NewLine, Ler(log).Select(l => Limpar(l.Texto)).Where(t => t is not null));

    private static DateTime Segundo(DateTime d) =>
        new(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond, d.Kind);

    private static LogDoPasso Formatar(IEnumerable<string> linhas)
    {
        var limpas = new List<string>();
        var erros = new List<string>();
        foreach (var l in linhas)
        {
            var texto = Limpar(l);
            if (texto is null) continue;
            limpas.Add(texto);
            if (texto.StartsWith("✕ ", StringComparison.Ordinal)) erros.Add(texto[2..]);
        }

        var cortadas = Math.Max(0, limpas.Count - LimiteDeLinhas);
        var sb = new StringBuilder();
        foreach (var t in limpas.Skip(cortadas)) sb.AppendLine(t);
        return new LogDoPasso { Texto = sb.ToString().TrimEnd(), Erros = erros, Cortadas = cortadas };
    }

    /// <summary>
    /// Os comandos do runner viram símbolos legíveis; o fim de grupo some. Nulo quando a
    /// linha não deve aparecer.
    /// </summary>
    public static string? Limpar(string linha)
    {
        var t = Ansi.Replace(linha, "");
        if (t.StartsWith("##[endgroup]", StringComparison.Ordinal)) return null;
        if (t.StartsWith("##[group]", StringComparison.Ordinal)) return "▸ " + t[9..];
        if (t.StartsWith("##[error]", StringComparison.Ordinal)) return "✕ " + t[9..];
        if (t.StartsWith("##[warning]", StringComparison.Ordinal)) return "⚠ " + t[11..];
        if (t.StartsWith("##[notice]", StringComparison.Ordinal)) return "ℹ " + t[10..];
        if (t.StartsWith("##[command]", StringComparison.Ordinal)) return "$ " + t[11..];
        if (t.StartsWith("##[debug]", StringComparison.Ordinal)) return t[9..];
        return t;
    }
}
