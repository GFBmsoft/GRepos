using System.Linq;
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

    /// <summary>
    /// O estilo chega pronto em cada nó — a cor do nome vem do dado, e estilo de XAML
    /// não troca isso —, então mudar a preferência exige remontar a árvore.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task Trocar_o_estilo_da_arvore_remonta_os_nos()
    {
        var home = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "grepos-estilo-" + System.IO.Path.GetRandomFileName());
        var repo = System.IO.Path.Combine(home, "repo");
        System.IO.Directory.CreateDirectory(repo);

        var antes = System.Environment.GetEnvironmentVariable("GREPOS_HOME");
        System.Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            await GRepos.Services.GitService.RunAsync(repo, new[] { "init", "-q", "-b", "main" });

            var main = new MainViewModel(new FakeDialogs());
            main.CreateGroup("Módulos");
            main.AddRepository(repo, "Financeiro", main.Groups[0].Id);
            main.RebuildTree();

            // o padrão é o minimalista, que é o que o EV03 pede
            Assert.True(main.Tree.OfType<GroupNode>().First().Minimalista);
            Assert.False(main.Tree.OfType<GroupNode>().First().MostraPilula);
            Assert.True(main.Tree.OfType<RepoNode>().First().MostraPonto);

            main.SetArvoreMinimalista(false);

            Assert.False(main.Tree.OfType<GroupNode>().First().Minimalista);
            Assert.True(main.Tree.OfType<GroupNode>().First().MostraPilula);
            Assert.True(main.Tree.OfType<RepoNode>().First().MostraTraco);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { System.IO.Directory.Delete(home, true); } catch (System.Exception) { /* temporária */ }
        }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public System.Threading.Tasks.Task<bool> ConfirmAsync(string t, string m) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<string?> PickFolderAsync(string t) => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<string?> PromptAsync(string t, string l, string i = "") => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => System.Threading.Tasks.Task.FromResult<(string, string)?>(null);
        public System.Threading.Tasks.Task ShowAddRepoAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowRepoConfigAsync(MainViewModel m, GRepos.Models.Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowSettingsAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowBranchesAsync(MainViewModel m, GRepos.Models.Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowEsteiraAsync(string s, string b, string u, string n) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowNovidadesAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowStashAsync(MainViewModel m, GRepos.Models.Repo r) => System.Threading.Tasks.Task.CompletedTask;
    }
}
