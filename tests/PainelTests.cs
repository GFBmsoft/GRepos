using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Painel de repositórios: seleção a partir do cartão e o texto de cada situação.</summary>
public class PainelTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(false);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string t, string l, string i = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel m, Repo r) => Task.CompletedTask;
    }

    /// <summary>
    /// Clicar num grupo abre o painel e **recolhe** o grupo, então os nós daquele grupo
    /// saem da árvore. O "Abrir" do cartão procurava um nó que não existia mais e não
    /// fazia nada — precisa reabrir o grupo antes de selecionar.
    /// </summary>
    [Fact]
    public async Task Abrir_funciona_com_o_grupo_recolhido()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-painel-" + Path.GetRandomFileName());
        var repo = Path.Combine(home, "repo");
        Directory.CreateDirectory(repo);

        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            await GitService.RunAsync(repo, new[] { "init", "-q", "-b", "main" });

            var main = new MainViewModel(new FakeDialogs());
            var grupoId = main.CreateGroup("Módulos");
            main.AddRepository(repo, "Financeiro", grupoId);
            main.RebuildTree();

            var no = main.Tree.OfType<RepoNode>().Single();
            main.SelecionarRepositorio(no.Id);
            Assert.Equal("Financeiro", main.SelectedNode?.Name);

            // recolhe o grupo, como o clique nele faz
            main.ToggleGroupCommand.Execute(main.Tree.OfType<GroupNode>().First(g => g.Id == grupoId));
            Assert.Empty(main.Tree.OfType<RepoNode>());

            main.SelectedNode = null;
            main.SelecionarRepositorio(no.Id);

            Assert.NotNull(main.SelectedNode);
            Assert.Equal("Financeiro", main.SelectedNode!.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(home, true); } catch (Exception) { /* temporária */ }
        }
    }

    [Fact]
    public void Cartao_sem_actions_nao_vira_erro_e_nao_oferece_esteira()
    {
        var cartao = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "Backup" }, new RepoStatus { Branch = "main" }, "#5D6675");

        // enquanto a API não respondeu, o cartão não pode afirmar que não tem esteira
        Assert.True(cartao.ConsultandoCi);
        Assert.False(cartao.SemCi);
        Assert.False(cartao.TemCi);

        cartao.CiSituacao = "nenhum";
        Assert.False(cartao.ConsultandoCi);
        Assert.True(cartao.SemCi);
        Assert.False(cartao.TemCi);
        Assert.Equal("sem esteira", cartao.CiTexto);
        Assert.Equal("", cartao.CiDetalheTexto);
    }

    [Fact]
    public void Cartao_com_esteira_mostra_o_que_rodou()
    {
        var cartao = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "GRepos", Path = @"D:\repo" },
            new RepoStatus { Branch = "main" }, "#4F8CFF")
        {
            CiWorkflow = "build",
            CiDetalhe = "Painel de repositórios",
            CiSituacao = "falha",
        };

        Assert.True(cartao.TemCi);
        Assert.Equal("esteira quebrou", cartao.CiTexto);
        Assert.Equal("build · Painel de repositórios", cartao.CiDetalheTexto);
        Assert.Contains("build · Painel de repositórios", cartao.Tooltip);
    }

    [Fact]
    public void Resumo_conta_o_que_precisa_de_atencao()
    {
        var cartoes = new[]
        {
            new CartaoRepoViewModel(new Repo { Id = "1" }, new RepoStatus { Branch = "main" }, "#fff"),
            new CartaoRepoViewModel(new Repo { Id = "2" },
                new RepoStatus { Branch = "main", Ahead = 1, Unstaged = 2, PendingFiles = 2 }, "#fff"),
            new CartaoRepoViewModel(new Repo { Id = "3" },
                new RepoStatus { Branch = "main", Behind = 3 }, "#fff") { CiSituacao = "falha" },
        };

        var painel = new PainelViewModel("Módulos", "", cartoes);

        Assert.Contains("3 repositório(s)", painel.Resumo);
        Assert.Contains("1 com alterações", painel.Resumo);
        Assert.Contains("1 a enviar", painel.Resumo);
        Assert.Contains("1 a receber", painel.Resumo);
        Assert.Contains("1 com esteira quebrada", painel.Resumo);
    }

    [Fact]
    public void Painel_sem_repositorios_diz_que_esta_vazio()
    {
        var painel = new PainelViewModel("Grupo novo", "", Array.Empty<CartaoRepoViewModel>());

        Assert.True(painel.Vazio);
        Assert.Equal("", painel.Resumo);
    }
}
