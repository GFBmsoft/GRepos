using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Pastas de trabalho (git worktree): leitura da lista, criar e remover de verdade.</summary>
[Collection(WorkspaceGlobal.Nome)]
public class WorktreesTests
{
    [Fact]
    public void Le_a_lista_do_git_com_principal_solta_travada_e_orfa()
    {
        var lista = Worktrees.Ler(
            "worktree D:/Repos/Financeiro\nHEAD 1111111aaaa\nbranch refs/heads/develop\n\n" +
            "worktree D:/Repos/Financeiro-imp-boleto\nHEAD 2222222bbbb\nbranch refs/heads/imp/boleto\nlocked\n\n" +
            "worktree D:/Repos/solto\nHEAD 3333333cccc\ndetached\n\n" +
            "worktree D:/Repos/sumiu\nHEAD 4444444dddd\nbranch refs/heads/x\nprunable gitdir file points to non-existent location\n");

        Assert.Equal(4, lista.Count);
        Assert.True(lista[0].Principal);
        Assert.Equal("develop", lista[0].Branch);
        Assert.Equal("imp/boleto", lista[1].Branch);
        Assert.True(lista[1].Travada);
        Assert.False(lista[1].Principal);
        Assert.Equal("", lista[2].Branch);
        Assert.True(lista[3].Orfa);

        Assert.Equal("(HEAD solto)", new WorktreeItemViewModel { Worktree = lista[2] }.Branch);
        Assert.False(new WorktreeItemViewModel { Worktree = lista[0] }.PodeRemover);
    }

    [Theory]
    [InlineData("imp/boleto", "Financeiro-imp-boleto")]
    [InlineData("feat/emissão nova", "Financeiro-feat-emissão-nova")]
    public void Pasta_sugerida_fica_ao_lado_do_repositorio(string branch, string esperado)
    {
        var pasta = Worktrees.PastaSugerida(Path.Combine("D:", "Repos", "Financeiro"), branch);

        Assert.Equal(esperado, Path.GetFileName(pasta));
        Assert.Equal(Path.Combine("D:", "Repos"), Path.GetDirectoryName(pasta));
        Assert.Equal("", Worktrees.PastaSugerida("D:/Repos/Financeiro", "  "));
    }

    // ----------------------------------------------------------- no git

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

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (Exception) { /* pasta temporária */ }
    }

    private static async Task<(string Raiz, string Repo)> Cenario()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-wt-" + Path.GetRandomFileName());
        var repo = Path.Combine(raiz, "Financeiro");
        Directory.CreateDirectory(repo);

        await Git(repo, "init", "-q", "-b", "main");
        await Git(repo, "config", "user.email", "t@t");
        await Git(repo, "config", "user.name", "Teste");
        File.WriteAllText(Path.Combine(repo, "a.txt"), "base\n");
        await Git(repo, "add", "-A");
        await Git(repo, "commit", "-qm", "inicial");
        await Git(repo, "branch", "develop");
        return (raiz, repo);
    }

    [Fact]
    public async Task Cria_para_branch_existente_e_para_branch_nova()
    {
        var (raiz, repo) = await Cenario();
        try
        {
            var existente = Worktrees.PastaSugerida(repo, "develop");
            await Worktrees.CriarAsync(repo, existente, "develop");

            var nova = Worktrees.PastaSugerida(repo, "imp/boleto");
            await Worktrees.CriarAsync(repo, nova, "imp/boleto");

            var lista = await Worktrees.ListarAsync(repo);
            Assert.Equal(new[] { "main", "develop", "imp/boleto" }, lista.Select(w => w.Branch));
            Assert.True(File.Exists(Path.Combine(nova, "a.txt")));

            // a pasta nova é um repositório para o resto do app: o status funciona nela
            Assert.Equal("imp/boleto", (await GitService.StatusAsync(nova)).Branch);
        }
        finally
        {
            Limpar(raiz);
        }
    }

    [Fact]
    public async Task Pasta_ocupada_e_recusada_antes_de_chamar_o_git()
    {
        var (raiz, repo) = await Cenario();
        try
        {
            var ocupada = Path.Combine(raiz, "ocupada");
            Directory.CreateDirectory(ocupada);
            File.WriteAllText(Path.Combine(ocupada, "meu.txt"), "x");

            var e = await Assert.ThrowsAsync<GitException>(() => Worktrees.CriarAsync(repo, ocupada, "develop"));
            Assert.Contains("não está vazia", e.Message);
        }
        finally
        {
            Limpar(raiz);
        }
    }

    [Fact]
    public async Task Remover_pasta_com_alteracao_pergunta_de_novo_antes_de_forcar()
    {
        var (raiz, repo) = await Cenario();
        var home = Path.Combine(raiz, "home");
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            var pasta = Worktrees.PastaSugerida(repo, "develop");
            await Worktrees.CriarAsync(repo, pasta, "develop");
            File.WriteAllText(Path.Combine(pasta, "novo.txt"), "não commitado");

            var main = new MainViewModel(new FakeDialogs());
            var vm = new WorktreesViewModel(new Repo { Id = "r", Name = "Financeiro", Path = repo }, main);
            await vm.CarregarAsync();

            var perguntas = 0;
            var respostas = new[] { true, false }; // aceita remover, desiste ao saber das alterações
            vm.Confirmar = (_, _) => Task.FromResult(respostas[perguntas++]);

            await vm.RemoverCommand.ExecuteAsync(vm.Lista.Single(w => w.Branch == "develop"));
            Assert.Equal(2, perguntas);
            Assert.True(Directory.Exists(pasta));

            perguntas = 0;
            respostas = new[] { true, true };
            await vm.RemoverCommand.ExecuteAsync(vm.Lista.Single(w => w.Branch == "develop"));

            Assert.False(Directory.Exists(pasta));
            Assert.Single(vm.Lista);
            Assert.Contains("develop", vm.Branches); // voltou a poder ser aberta numa pasta
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    [Fact]
    public async Task Branch_ja_aberta_numa_pasta_sai_das_sugestoes_e_a_pasta_acompanha_o_nome()
    {
        var (raiz, repo) = await Cenario();
        var home = Path.Combine(raiz, "home");
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            var main = new MainViewModel(new FakeDialogs());
            var vm = new WorktreesViewModel(new Repo { Id = "r", Name = "Financeiro", Path = repo }, main);
            await vm.CarregarAsync();

            Assert.Equal(new[] { "develop" }, vm.Branches); // a main está aberta na principal
            Assert.False(vm.PodeCriar);

            vm.NovaBranch = "develop";
            Assert.Equal("Financeiro-develop", Path.GetFileName(vm.NovaPasta));
            Assert.True(vm.PodeCriar);

            // pasta escolhida à mão não é trocada quando a branch muda
            vm.NovaPasta = Path.Combine(raiz, "outra");
            vm.NovaBranch = "imp/x";
            Assert.Equal("outra", Path.GetFileName(vm.NovaPasta));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }
}
