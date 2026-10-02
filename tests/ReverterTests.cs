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
/// Preparar/descartar só os marcados, reverter o repositório inteiro e as ações de
/// commit do histórico (branch, tag, cherry-pick, revert, reset), num repositório real.
/// </summary>
public class ReverterTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public string? Resposta { get; set; }
        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string t, string l, string i = "") => Task.FromResult(Resposta);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel m, Repo r) => Task.CompletedTask;
    }

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

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task<string> NovoRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-rev-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        await Git(dir, "config", "core.autocrlf", "false");

        File.WriteAllText(Path.Combine(dir, ".gitignore"), "*.log\n");
        foreach (var nome in new[] { "a.txt", "b.txt", "c.txt" })
            File.WriteAllText(Path.Combine(dir, nome), "inicial\n");
        await Git(dir, "add", ".");
        await Git(dir, "commit", "-qm", "inicial");
        return dir;
    }

    private static string Ler(string dir, string nome) => File.ReadAllText(Path.Combine(dir, nome));

    private static async Task<string> Head(string dir) => (await Git(dir, "rev-parse", "HEAD")).Trim();

    private static (ChangesViewModel Vm, MainViewModel Main) Changes(string dir)
    {
        var main = new MainViewModel(new FakeDialogs());
        return (new ChangesViewModel(new Repo { Id = "r1", Name = "Demo", Path = dir }, main, split: true), main);
    }

    [Fact]
    public async Task Preparar_com_dois_marcados_prepara_so_eles()
    {
        var dir = await NovoRepo();
        try
        {
            foreach (var nome in new[] { "a.txt", "b.txt", "c.txt" })
                File.WriteAllText(Path.Combine(dir, nome), "alterado\n");

            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            Assert.Equal("Preparar tudo", vm.PrepararRotulo);

            vm.DefinirSelecao(false, vm.Unstaged.Where(f => f.Path != "b.txt"));
            Assert.Equal("Preparar selecionados (2)", vm.PrepararRotulo);

            await vm.StageAllCommand.ExecuteAsync(null);
            Assert.Equal(new[] { "a.txt", "c.txt" }, vm.Staged.Select(f => f.Path).OrderBy(p => p));
            Assert.Equal("b.txt", Assert.Single(vm.Unstaged).Path);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Com_um_so_marcado_o_botao_continua_valendo_para_todos()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "novo.txt"), "novo\n");

            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            vm.DefinirSelecao(false, vm.Unstaged.Take(1));

            await vm.DiscardAllCommand.ExecuteAsync(null);
            Assert.Empty(vm.Unstaged);
            Assert.Equal("inicial\n", Ler(dir, "a.txt"));
            Assert.False(File.Exists(Path.Combine(dir, "novo.txt")));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Descartar_marcados_preserva_os_demais_e_o_preparado()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "preparado\n");
            await Git(dir, "add", "a.txt");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "preparado e alterado\n");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "c.txt"), "alterado\n");

            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            vm.DefinirSelecao(false, vm.Unstaged.Where(f => f.Path is "a.txt" or "b.txt"));

            await vm.DiscardAllCommand.ExecuteAsync(null);
            Assert.Equal("preparado\n", Ler(dir, "a.txt"));
            Assert.Equal("inicial\n", Ler(dir, "b.txt"));
            Assert.Equal("alterado\n", Ler(dir, "c.txt"));
            Assert.Equal("a.txt", Assert.Single(vm.Staged).Path);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Voltar_ao_ultimo_commit_limpa_tudo_menos_ignorados()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "preparado\n");
            await Git(dir, "add", "a.txt");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "alterado\n");
            Directory.CreateDirectory(Path.Combine(dir, "pasta"));
            File.WriteAllText(Path.Combine(dir, "pasta", "novo.txt"), "novo\n");
            File.WriteAllText(Path.Combine(dir, "build.log"), "ignorado\n");

            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            await vm.VoltarAoUltimoCommitCommand.ExecuteAsync(null);

            Assert.Empty(vm.Staged);
            Assert.Empty(vm.Unstaged);
            Assert.Equal("inicial\n", Ler(dir, "a.txt"));
            Assert.Equal("inicial\n", Ler(dir, "b.txt"));
            Assert.False(Directory.Exists(Path.Combine(dir, "pasta")));
            Assert.True(File.Exists(Path.Combine(dir, "build.log")));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Voltar_ao_remoto_descarta_tambem_os_commits_nao_enviados()
    {
        var remoto = await NovoRepo();
        var dir = Path.Combine(Path.GetTempPath(), "grepos-rev-" + Path.GetRandomFileName());
        try
        {
            // sem isso o autocrlf global da máquina devolve "\r\n" no checkout
            await Git(Path.GetTempPath(), "clone", "-q", "-c", "core.autocrlf=false", remoto, dir);
            await Git(dir, "config", "user.email", "t@t");
            await Git(dir, "config", "user.name", "Teste");
            var doRemoto = await Head(dir);

            File.WriteAllText(Path.Combine(dir, "a.txt"), "commit local\n");
            await Git(dir, "commit", "-qam", "local");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "alterado\n");

            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            Assert.True(vm.TemRemoto);
            Assert.Equal("Voltar ao remoto (origin/main)", vm.VoltarAoRemotoRotulo);

            await vm.VoltarAoRemotoCommand.ExecuteAsync(null);
            Assert.Equal(doRemoto, await Head(dir));
            Assert.Equal("inicial\n", Ler(dir, "a.txt"));
            Assert.Empty(vm.Unstaged);
        }
        finally
        {
            Limpar(dir);
            Limpar(remoto);
        }
    }

    [Fact]
    public async Task Sem_remoto_o_item_fica_desabilitado()
    {
        var dir = await NovoRepo();
        try
        {
            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            Assert.False(vm.TemRemoto);
        }
        finally { Limpar(dir); }
    }

    private static async Task<(HistoryViewModel Vm, FakeDialogs Dialogos)> HistoricoEm(string dir, string assunto)
    {
        var dialogos = new FakeDialogs();
        var main = new MainViewModel(dialogos);
        var vm = new HistoryViewModel(new Repo { Id = "r1", Name = "Demo", Path = dir }, main, 50, split: true);
        await vm.LoadAsync();
        vm.SelectedCommit = vm.Commits.First(c => c.Subject == assunto);
        return (vm, dialogos);
    }

    [Fact]
    public async Task Cherry_pick_e_revert_criam_commits_na_branch_atual()
    {
        var dir = await NovoRepo();
        try
        {
            await Git(dir, "checkout", "-qb", "outra");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "da outra\n");
            await Git(dir, "commit", "-qam", "na outra");
            await Git(dir, "checkout", "-q", "main");

            var (vm, _) = await HistoricoEm(dir, "na outra");
            await vm.CherryPickCommand.ExecuteAsync(null);
            Assert.Equal("da outra\n", Ler(dir, "a.txt"));
            Assert.Equal("na outra", (await Git(dir, "log", "-1", "--format=%s")).Trim());

            vm.SelectedCommit = vm.Commits.First(c => c.Subject == "na outra" && c.Commit.Refs.Any(r => r.Contains("main")));
            await vm.ReverterCommitCommand.ExecuteAsync(null);
            Assert.Equal("inicial\n", Ler(dir, "a.txt"));
            Assert.StartsWith("Revert", (await Git(dir, "log", "-1", "--format=%s")).Trim());
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Reset_soft_mixed_e_hard()
    {
        var dir = await NovoRepo();
        try
        {
            var inicial = await Head(dir);
            File.WriteAllText(Path.Combine(dir, "a.txt"), "segundo\n");
            await Git(dir, "commit", "-qam", "segundo");

            var (vm, _) = await HistoricoEm(dir, "inicial");
            await vm.ResetSoftCommand.ExecuteAsync(null);
            Assert.Equal(inicial, await Head(dir));
            Assert.StartsWith("M ", (await Git(dir, "status", "--porcelain")).TrimEnd());

            await Git(dir, "commit", "-qm", "segundo de novo");
            vm.SelectedCommit = vm.Commits.First(c => c.Subject == "inicial");
            await vm.ResetMixedCommand.ExecuteAsync(null);
            Assert.StartsWith(" M", (await Git(dir, "status", "--porcelain")).TrimEnd());

            vm.SelectedCommit = vm.Commits.First(c => c.Subject == "inicial");
            await vm.ResetHardCommand.ExecuteAsync(null);
            Assert.Equal("", (await Git(dir, "status", "--porcelain")).Trim());
            Assert.Equal("inicial\n", Ler(dir, "a.txt"));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Criar_branch_e_tag_no_commit_selecionado()
    {
        var dir = await NovoRepo();
        try
        {
            var inicial = await Head(dir);
            File.WriteAllText(Path.Combine(dir, "a.txt"), "segundo\n");
            await Git(dir, "commit", "-qam", "segundo");

            var (vm, dialogos) = await HistoricoEm(dir, "inicial");
            dialogos.Resposta = "feat/aqui";
            await vm.CriarBranchAquiCommand.ExecuteAsync(null);
            dialogos.Resposta = "1.0.0.1";
            vm.SelectedCommit = vm.Commits.First(c => c.Subject == "inicial");
            await vm.CriarTagAquiCommand.ExecuteAsync(null);

            Assert.Equal(inicial, (await Git(dir, "rev-parse", "feat/aqui")).Trim());
            Assert.Equal(inicial, (await Git(dir, "rev-parse", "1.0.0.1^{commit}")).Trim());
            // criar não troca de branch
            Assert.Equal("main", (await Git(dir, "branch", "--show-current")).Trim());
        }
        finally { Limpar(dir); }
    }

    [Theory]
    [InlineData("error: could not apply abc1234... x", true)]
    [InlineData("CONFLICT (content): Merge conflict in a.txt", true)]
    [InlineData("fatal: bad revision", false)]
    public void Falha_de_conflito_vira_instrucao(string erro, bool conflito)
    {
        var texto = HistoryViewModel.ExplicarFalha(erro);
        Assert.Equal(conflito, texto.StartsWith("Parou em conflito"));
    }
}
