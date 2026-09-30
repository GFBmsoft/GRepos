using System.Linq;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

public class DiffLayoutTests
{
    private const string LinhaLonga =
        "        OnChanged(nameof(HasWingetUpdates)); // comentario bem comprido para estourar a largura da janela";

    private static string Diff(string added) => $"""
        diff --git a/a.cs b/a.cs
        --- a/a.cs
        +++ b/a.cs
        @@ -1,2 +1,2 @@
         contexto
        -curto
        +{added}
        """;

    [Fact]
    public void Largura_acompanha_a_linha_mais_longa()
    {
        var vm = new DiffViewModel { CharWidth = 7 };
        vm.Load(Diff(LinhaLonga));

        // a coluna precisa comportar a linha inteira, senão o texto é cortado
        Assert.True(vm.RightWidth >= LinhaLonga.Length * 7);
    }

    [Fact]
    public void Trocar_de_modo_recalcula_as_larguras()
    {
        var vm = new DiffViewModel { CharWidth = 7 };
        vm.Load(Diff(LinhaLonga));
        var larguraSplit = vm.RightWidth;

        vm.Split = false;
        Assert.True(vm.UnifiedWidth >= larguraSplit);
    }

    [Fact]
    public void Medir_a_fonte_depois_redimensiona_as_colunas()
    {
        var vm = new DiffViewModel { CharWidth = 7 };
        vm.Load(Diff(LinhaLonga));
        var antes = vm.RightWidth;

        vm.CharWidth = 14; // fonte maior descoberta pela View
        Assert.True(vm.RightWidth > antes);
    }

    [Fact]
    public void Com_quebra_de_linha_a_coluna_para_na_largura_do_painel()
    {
        var vm = new DiffViewModel { CharWidth = 7, ViewportWidth = 400 };
        vm.Load(Diff(LinhaLonga));
        Assert.True(vm.RightWidth > 400); // sem quebra, a coluna acompanha o texto

        vm.Wrap = true;

        // ligada a quebra, nada pode passar do painel: é o que tira a rolagem horizontal
        Assert.True(vm.RightWidth <= 400);
        Assert.True(vm.LeftWidth <= 400);
        Assert.True(vm.UnifiedWidth <= 400);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, vm.Wrapping);

        vm.Wrap = false;
        Assert.Equal(Avalonia.Media.TextWrapping.NoWrap, vm.Wrapping);
        Assert.True(vm.RightWidth > 400);
    }

    [Fact]
    public void Quebra_de_linha_sobrevive_a_troca_de_modo()
    {
        var vm = new DiffViewModel { CharWidth = 7, ViewportWidth = 400, Wrap = true };
        vm.Load(Diff(LinhaLonga));

        vm.Split = false; // Rebuild chama MeasureColumns de novo
        Assert.True(vm.UnifiedWidth <= 400);
    }

    [Fact]
    public void Tabulacao_vira_espacos_para_nao_desalinhar()
    {
        var vm = new DiffViewModel { CharWidth = 7 };
        vm.Load("@@ -1 +1 @@\n-\tum\n+\t\tdois\n");

        var linhas = vm.Rows.OfType<DiffSplitRow>().ToList();
        Assert.DoesNotContain(linhas, l => l.LeftText.Contains('\t') || l.RightText.Contains('\t'));
        Assert.Equal("    um", linhas[0].LeftText);
        Assert.Equal("        dois", linhas[0].RightText);
    }
}
