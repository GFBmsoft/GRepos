using System.Collections.Generic;
using System.Linq;
using GRepos.Models;

namespace GRepos.Services;

public sealed class GraphRow
{
    public Commit Commit { get; init; } = new();
    public int Lane { get; init; }

    /// <summary>Raias ocupadas depois deste commit: índice -> hash esperado (null = livre).</summary>
    public List<string?> LanesAfter { get; init; } = new();

    /// <summary>Segmentos desta linha até a próxima (metade de baixo).</summary>
    public List<(int From, int To)> Edges { get; } = new();

    /// <summary>Segmentos que chegam da linha anterior (metade de cima).</summary>
    public List<(int From, int To)> EdgesUp { get; internal set; } = new();
}

/// <summary>Layout de raias para o grafo de commits (lista já em --date-order).</summary>
public static class GraphBuilder
{
    public static readonly string[] LaneColors =
    {
        "#4F8CFF", "#F0883E", "#3FB950", "#D2A8FF",
        "#FF7B72", "#79C0FF", "#E3B341", "#56D4BC",
    };

    public static string LaneColor(int lane) => LaneColors[lane % LaneColors.Length];

    public static List<GraphRow> Build(IReadOnlyList<Commit> commits)
    {
        var lanes = new List<string?>();
        var rows = new List<GraphRow>();

        int FirstFree()
        {
            var i = lanes.IndexOf(null);
            return i == -1 ? lanes.Count : i;
        }

        foreach (var commit in commits)
        {
            var lane = lanes.IndexOf(commit.Hash);
            if (lane == -1)
            {
                lane = FirstFree();
                if (lane == lanes.Count) lanes.Add(commit.Hash);
                else lanes[lane] = commit.Hash;
            }

            // libera raias duplicadas apontando para este mesmo commit (merges convergindo)
            for (var i = 0; i < lanes.Count; i++)
                if (i != lane && lanes[i] == commit.Hash) lanes[i] = null;

            var first = commit.Parents.Count > 0 ? commit.Parents[0] : null;
            lanes[lane] = first;

            foreach (var p in commit.Parents.Skip(1))
            {
                if (lanes.Contains(p)) continue;
                var free = FirstFree();
                if (free == lanes.Count) lanes.Add(p);
                else lanes[free] = p;
            }

            while (lanes.Count > 0 && lanes[^1] is null) lanes.RemoveAt(lanes.Count - 1);

            rows.Add(new GraphRow { Commit = commit, Lane = lane, LanesAfter = new List<string?>(lanes) });
        }

        for (var i = 0; i < rows.Count - 1; i++)
        {
            var next = rows[i + 1];
            for (var lane = 0; lane < rows[i].LanesAfter.Count; lane++)
            {
                var hash = rows[i].LanesAfter[lane];
                if (hash is null) continue;
                rows[i].Edges.Add((lane, hash == next.Commit.Hash ? next.Lane : lane));
            }
            next.EdgesUp = rows[i].Edges;
        }

        return rows;
    }

    public static int MaxLanes(IReadOnlyList<GraphRow> rows)
    {
        var max = 1;
        foreach (var r in rows)
        {
            if (r.Lane + 1 > max) max = r.Lane + 1;
            if (r.LanesAfter.Count > max) max = r.LanesAfter.Count;
        }
        return max;
    }
}
