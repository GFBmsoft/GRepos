using System.Collections.Generic;
using System.Linq;
using GRepos.Models;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class DiffParserTests
{
    private const string Diff = """
diff --git a/a.txt b/a.txt
index 1111111..2222222 100644
--- a/a.txt
+++ b/a.txt
@@ -1,3 +1,4 @@
 um
-dois
+DOIS
+dois e meio
 tres
@@ -10,2 +11,2 @@
 dez
-onze
+ONZE
""";

    [Fact]
    public void Parse_separa_cabecalho_hunks_e_numera_as_linhas()
    {
        var d = DiffParser.Parse(Diff);

        Assert.Equal(4, d.Head.Count);
        Assert.Equal(2, d.Hunks.Count);

        var first = d.Hunks[0].Lines;
        Assert.Equal(
            new[] { DiffLineKind.Context, DiffLineKind.Del, DiffLineKind.Add, DiffLineKind.Add, DiffLineKind.Context },
            first.Select(l => l.Kind));

        // contexto inicial é a linha 1 dos dois lados; "tres" cai em 3 (velho) e 4 (novo)
        Assert.Equal(1, first[0].OldNo);
        Assert.Equal(1, first[0].NewNo);
        Assert.Equal(3, first[4].OldNo);
        Assert.Equal(4, first[4].NewNo);
    }

    [Fact]
    public void BuildHunkPatch_isola_um_bloco_e_recalcula_o_cabecalho()
    {
        var d = DiffParser.Parse(Diff);
        var patch = DiffParser.BuildHunkPatch(d, d.Hunks[0]);
        var lines = patch.Split('\n');

        Assert.Equal("diff --git a/a.txt b/a.txt", lines[0]);
        Assert.Equal("@@ -1,3 +1,4 @@", lines[4]); // 3 linhas antigas, 4 novas
        Assert.DoesNotContain("ONZE", patch);       // o segundo bloco não entra
        Assert.EndsWith("\n", patch);               // git apply exige quebra final
    }

    [Fact]
    public void SideBySide_pareia_remocoes_com_adicoes_e_preenche_o_lado_que_falta()
    {
        var d = DiffParser.Parse(Diff);
        var rows = DiffParser.SideBySide(d.Hunks[0]);

        Assert.Equal(4, rows.Count); // contexto + 2 pares + contexto
        Assert.Equal("dois", rows[1].Left!.Text);
        Assert.Equal("DOIS", rows[1].Right!.Text);
        Assert.Null(rows[2].Left);   // adição sem par à esquerda
        Assert.Equal("dois e meio", rows[2].Right!.Text);
    }
}

public class GraphBuilderTests
{
    private static Commit C(string hash, params string[] parents) => new()
    {
        Hash = hash,
        Parents = parents.ToList(),
        Author = "a",
        Email = "a@b",
        Date = "2026-01-01T00:00:00Z",
        Subject = hash,
    };

    [Fact]
    public void Build_mantem_a_raia_do_primeiro_pai_e_abre_raia_para_o_merge()
    {
        // m (merge) -> pais f1 e f2; f1 -> base; f2 -> base
        var rows = GraphBuilder.Build(new List<Commit>
        {
            C("m", "f1", "f2"),
            C("f1", "base"),
            C("f2", "base"),
            C("base"),
        });

        Assert.Equal(0, rows[0].Lane); // merge na raia 0
        Assert.Equal(0, rows[1].Lane); // primeiro pai continua na raia 0
        Assert.Equal(1, rows[2].Lane); // segundo pai ganhou raia própria
        Assert.Equal(0, rows[3].Lane); // base volta para a raia 0
        Assert.Empty(rows[3].LanesAfter); // sem pais, nada em aberto
    }

    [Fact]
    public void Build_liga_cada_linha_a_seguinte_pelos_segmentos()
    {
        var rows = GraphBuilder.Build(new List<Commit>
        {
            C("m", "f1", "f2"),
            C("f1", "base"),
            C("f2", "base"),
            C("base"),
        });

        // a linha do merge abre dois segmentos: um para f1 (raia 0), outro para f2
        Assert.Equal(2, rows[0].Edges.Count);
        Assert.Contains(rows[0].Edges, e => e.To == rows[1].Lane);
        Assert.Equal(rows[0].Edges, rows[1].EdgesUp);
    }
}
