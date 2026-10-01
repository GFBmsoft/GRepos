using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Várias contas do GitHub: a principal vale por padrão, e cada repositório pode
/// escolher outra — que é a que o git e a API usam para ele.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class ContasTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string t, string l, string i = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel m, Repo r) => Task.CompletedTask;
    }

    /// <summary>Roda com o workspace numa pasta própria e devolve a conta global no fim.</summary>
    private static async Task ComWorkspace(Func<MainViewModel, string, Task> teste)
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-contas-" + Path.GetRandomFileName());
        var repo = Path.Combine(home, "repo");
        Directory.CreateDirectory(repo);

        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        var contaAntes = GitService.CredentialUser;
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            await GitService.RunAsync(repo, new[] { "init", "-q", "-b", "main" });
            await teste(new MainViewModel(new FakeDialogs()), repo);
        }
        finally
        {
            GitService.DefinirConta(repo, null);
            GitService.CredentialUser = contaAntes;
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(home, true); } catch (Exception) { /* temporária */ }
        }
    }

    [Fact]
    public Task Principal_vem_primeiro_e_vale_para_o_git() => ComWorkspace((main, _) =>
    {
        main.SetContas(new[] { "bmsoftsistemas", "GFBmsoft" }, "GFBmsoft");

        Assert.Equal(new[] { "GFBmsoft", "bmsoftsistemas" }, main.Contas);
        Assert.Equal("GFBmsoft", main.Settings.GithubUser);
        Assert.Equal("GFBmsoft", GitService.CredentialUser);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Principal_fora_da_lista_entra_nela_e_lista_sem_principal_elege_a_primeira() => ComWorkspace((main, _) =>
    {
        main.SetContas(new[] { "bmsoftsistemas" }, "GFBmsoft");
        Assert.Contains("GFBmsoft", main.Contas);

        main.SetContas(new[] { "bmsoftsistemas", " ", "BMSOFTSISTEMAS" }, "");
        Assert.Equal("bmsoftsistemas", main.Settings.GithubUser);
        Assert.Single(main.Contas); // vazio e repetido não viram conta
        return Task.CompletedTask;
    });

    [Fact]
    public Task Repositorio_com_conta_propria_leva_ela_ao_git() => ComWorkspace((main, pasta) =>
    {
        main.SetContas(new[] { "GFBmsoft", "bmsoftsistemas" }, "GFBmsoft");
        main.AddRepository(pasta, "Financeiro", null);
        var repo = main.Repos.Single();

        main.UpdateRepository(repo, repo.Name, null, null, null, null, "bmsoftsistemas");
        Assert.Equal("bmsoftsistemas", repo.Conta);
        Assert.Equal("bmsoftsistemas", GitService.ContaDe(pasta));

        // de volta à automática: vale a principal
        main.UpdateRepository(repo, repo.Name, null, null, null, null, "");
        Assert.Null(repo.Conta);
        Assert.Equal("GFBmsoft", GitService.ContaDe(pasta));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Remover_a_conta_devolve_os_repositorios_dela_para_a_automatica() => ComWorkspace((main, pasta) =>
    {
        main.SetContas(new[] { "GFBmsoft", "bmsoftsistemas" }, "GFBmsoft");
        main.AddRepository(pasta, "Financeiro", null);
        var repo = main.Repos.Single();
        main.UpdateRepository(repo, repo.Name, null, null, null, null, "bmsoftsistemas");

        main.SetContas(new[] { "GFBmsoft" }, "GFBmsoft");

        Assert.Null(repo.Conta);
        Assert.Equal("GFBmsoft", GitService.ContaDe(pasta));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Configuracao_antiga_com_uma_conta_so_vira_a_principal_da_lista() => ComWorkspace((main, _) =>
    {
        // workspace salvo antes das várias contas: só GithubUser preenchido
        main.Settings.GithubUser = "GFBmsoft";
        main.Settings.GithubContas.Clear();

        typeof(MainViewModel)
            .GetMethod("MigrarContas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(main, null);

        Assert.Equal(new[] { "GFBmsoft" }, main.Contas);
        return Task.CompletedTask;
    });
}
