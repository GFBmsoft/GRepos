using GRepos.Models;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

public class GroupNodeTests
{
    [Fact]
    public void Chevron_acompanha_o_estado_do_grupo()
    {
        var node = new GroupNode { Collapsed = false };
        var aberto = node.Chevron;

        node.Collapsed = true;

        Assert.NotEqual(aberto, node.Chevron);
        Assert.False(string.IsNullOrEmpty(node.Chevron));
    }

    [Fact]
    public void Grupo_nasce_com_cor_da_paleta()
    {
        Assert.Contains(new GroupNode().Color, GRepos.Services.GroupPalette.Cores);
    }
}

public class RepoNodeTests
{
    private static RepoNode Node(RepoStatus? status = null) => new()
    {
        Repo = new Repo { Id = "r1", Name = "Financeiro", Path = @"C:\tmp\fin" },
        Status = status,
    };

    [Fact]
    public void Repositorio_carrega_a_cor_do_grupo_para_o_traco_lateral()
    {
        var node = new RepoNode { Repo = new Repo { Name = "A" }, GroupColor = "#DB4C9B" };
        Assert.Equal("#DB4C9B", node.GroupColor);
    }

    [Fact]
    public void Badges_aparecem_conforme_o_status()
    {
        var limpo = Node(new RepoStatus { Branch = "main" });
        Assert.False(limpo.ShowAhead);
        Assert.False(limpo.ShowBehind);
        Assert.False(limpo.ShowDirty);
        Assert.False(limpo.HasError);

        var sujo = Node(new RepoStatus { Branch = "main", Ahead = 2, Behind = 1, Unstaged = 3 });
        Assert.True(sujo.ShowAhead);
        Assert.True(sujo.ShowBehind);
        Assert.True(sujo.ShowDirty);
        Assert.Equal("\u21912", sujo.AheadText);
        Assert.Equal("\u21931", sujo.BehindText);
    }

    [Fact]
    public void Conflito_esconde_o_badge_de_alteracoes()
    {
        var node = Node(new RepoStatus { Branch = "main", Conflicted = 2, Unstaged = 1 });
        Assert.True(node.ShowConflict);
        Assert.False(node.ShowDirty); // conflito é o que importa mostrar
    }

    [Fact]
    public void Erro_do_repositorio_vira_badge_proprio()
    {
        var node = Node(new RepoStatus { Error = "pasta não é um repositório git" });
        Assert.True(node.HasError);
        Assert.Contains("não é um repositório", node.Tooltip);
    }
}
