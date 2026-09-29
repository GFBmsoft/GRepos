using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GRepos.Services;

public enum DiffLineKind { Context, Add, Del, NoNewline }

public sealed class DiffLine
{
    public DiffLineKind Kind { get; init; }
    public string Text { get; init; } = "";
    public int? OldNo { get; init; }
    public int? NewNo { get; init; }

    public string OldNoText => OldNo?.ToString() ?? "";
    public string NewNoText => NewNo?.ToString() ?? "";
    public string Marker => Kind switch { DiffLineKind.Add => "+", DiffLineKind.Del => "-", _ => " " };
}

public sealed class Hunk
{
    public string Header { get; init; } = "";
    public List<DiffLine> Lines { get; } = new();
}

public sealed class ParsedDiff
{
    /// <summary>Linhas "diff --git", "index", "---", "+++" — necessárias para o git apply.</summary>
    public List<string> Head { get; } = new();
    public List<Hunk> Hunks { get; } = new();
    public bool Binary { get; set; }
}

public sealed class SideRow
{
    public DiffLine? Left { get; init; }
    public DiffLine? Right { get; init; }
}

/// <summary>Parser de diff unificado: o suficiente para exibir e para montar patch de bloco.</summary>
public static class DiffParser
{
    private static readonly Regex HunkRe =
        new(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@", RegexOptions.Compiled);

    public static ParsedDiff Parse(string raw)
    {
        var result = new ParsedDiff();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        Hunk? current = null;
        var oldNo = 0;
        var newNo = 0;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var m = HunkRe.Match(line);
            if (m.Success)
            {
                current = new Hunk { Header = line };
                result.Hunks.Add(current);
                oldNo = int.Parse(m.Groups[1].Value);
                newNo = int.Parse(m.Groups[3].Value);
                continue;
            }

            if (current is null)
            {
                if (line.StartsWith("Binary files") || line.StartsWith("GIT binary patch")) result.Binary = true;
                if (line.Length > 0) result.Head.Add(line);
                continue;
            }

            if (line.StartsWith("\\"))
            {
                current.Lines.Add(new DiffLine { Kind = DiffLineKind.NoNewline, Text = line.Length > 2 ? line[2..] : "" });
                continue;
            }
            if (line.Length == 0) continue;

            var text = line[1..];
            switch (line[0])
            {
                case '+':
                    current.Lines.Add(new DiffLine { Kind = DiffLineKind.Add, Text = text, NewNo = newNo++ });
                    break;
                case '-':
                    current.Lines.Add(new DiffLine { Kind = DiffLineKind.Del, Text = text, OldNo = oldNo++ });
                    break;
                case ' ':
                    current.Lines.Add(new DiffLine { Kind = DiffLineKind.Context, Text = text, OldNo = oldNo++, NewNo = newNo++ });
                    break;
            }
        }

        return result;
    }

    /// <summary>Patch com um único bloco, pronto para git apply [--cached] [--reverse].</summary>
    public static string BuildHunkPatch(ParsedDiff diff, Hunk hunk)
    {
        var m = HunkRe.Match(hunk.Header);
        if (!m.Success) return "";

        var sb = new StringBuilder();
        var oldCount = 0;
        var newCount = 0;

        foreach (var l in hunk.Lines)
        {
            switch (l.Kind)
            {
                case DiffLineKind.NoNewline:
                    sb.Append("\\ ").Append(l.Text).Append('\n');
                    continue;
                case DiffLineKind.Add:
                    newCount++;
                    sb.Append('+');
                    break;
                case DiffLineKind.Del:
                    oldCount++;
                    sb.Append('-');
                    break;
                default:
                    oldCount++;
                    newCount++;
                    sb.Append(' ');
                    break;
            }
            sb.Append(l.Text).Append('\n');
        }

        var header = $"@@ -{m.Groups[1].Value},{oldCount} +{m.Groups[3].Value},{newCount} @@";
        return string.Join('\n', diff.Head) + "\n" + header + "\n" + sb.ToString();
    }

    /// <summary>Agrupa as linhas de um bloco em pares esquerda/direita para a visão lado a lado.</summary>
    public static List<SideRow> SideBySide(Hunk hunk)
    {
        var rows = new List<SideRow>();
        var ls = hunk.Lines.Where(l => l.Kind != DiffLineKind.NoNewline).ToList();
        var i = 0;

        while (i < ls.Count)
        {
            if (ls[i].Kind == DiffLineKind.Context)
            {
                rows.Add(new SideRow { Left = ls[i], Right = ls[i] });
                i++;
                continue;
            }

            var dels = new List<DiffLine>();
            var adds = new List<DiffLine>();
            while (i < ls.Count && ls[i].Kind == DiffLineKind.Del) dels.Add(ls[i++]);
            while (i < ls.Count && ls[i].Kind == DiffLineKind.Add) adds.Add(ls[i++]);

            var n = Math.Max(dels.Count, adds.Count);
            for (var k = 0; k < n; k++)
                rows.Add(new SideRow
                {
                    Left = k < dels.Count ? dels[k] : null,
                    Right = k < adds.Count ? adds[k] : null,
                });

            if (dels.Count == 0 && adds.Count == 0) i++; // segurança contra laço infinito
        }

        return rows;
    }
}
