using System.Linq;
using GRepos.Models;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class PorcelainV2Tests
{
    // saída real de "git status --porcelain=v2 --untracked-files=all"
    private const string Raw = """
1 .M N... 100644 100644 100644 e1a7f86369e236e52d3c98777a0598a1b4c2f3d9 e1a7f86369e236e52d3c98777a0598a1b4c2f3d9 a.txt
1 M. N... 100644 100644 100644 aaaaaaa1111111111111111111111111111111aa bbbbbbb22222222222222222222222222222222b src/Unit1.pas
2 R. N... 100644 100644 100644 cccc dddd R100 novo/Caminho.pas	antigo/Caminho.pas
u UU N... 100644 100644 100644 100644 e1 e2 e3 conflito.txt
? c.txt
? pasta com espaco/arquivo novo.txt
""";

    [Fact]
    public void Le_o_caminho_e_nao_o_hash()
    {
        var files = GitService.ParsePorcelainV2(Raw);

        Assert.Contains(files, f => f.Path == "a.txt");
        Assert.Contains(files, f => f.Path == "src/Unit1.pas");
        Assert.DoesNotContain(files, f => f.Path.StartsWith("e1a7f863"));
    }

    [Fact]
    public void Separa_index_de_worktree()
    {
        var files = GitService.ParsePorcelainV2(Raw);

        var local = files.Single(f => f.Path == "a.txt");
        Assert.Equal(".", local.Index);      // nada preparado
        Assert.Equal("M", local.Worktree);   // modificado no working tree

        var staged = files.Single(f => f.Path == "src/Unit1.pas");
        Assert.Equal("M", staged.Index);
        Assert.Equal(".", staged.Worktree);
    }

    [Fact]
    public void Entende_renomeado_conflito_e_nao_rastreado()
    {
        var files = GitService.ParsePorcelainV2(Raw);

        var renamed = files.Single(f => f.Path == "novo/Caminho.pas");
        Assert.Equal("antigo/Caminho.pas", renamed.OrigPath);
        Assert.Equal("R", renamed.Index);

        Assert.Equal(ChangeKind.Conflict, files.Single(f => f.Path == "conflito.txt").Kind);
        Assert.Equal(ChangeKind.Untracked, files.Single(f => f.Path == "c.txt").Kind);

        // caminho com espaços não pode ser truncado
        Assert.Contains(files, f => f.Path == "pasta com espaco/arquivo novo.txt");
    }
}
