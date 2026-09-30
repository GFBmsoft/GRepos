using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace GRepos.Tests;

/// <summary>
/// O número na aba "Alterações" tem de bater com o que o painel lista. Eles vêm de
/// contagens diferentes do mesmo "git status" e podem divergir.
/// </summary>
public class ContadorAbaTests
{
    private readonly ITestOutputHelper _saida;

    public ContadorAbaTests(ITestOutputHelper saida) => _saida = saida;

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

    private async Task Conferir(string cenario, Func<string, Task> preparar)
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-cont-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
            await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
            File.WriteAllText(Path.Combine(dir, "base.txt"), "base\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });

            await preparar(dir);

            var (status, _) = await GitService.StatusAndChangesAsync(dir);
            var vm = new ChangesViewModel(new Repo { Id = "r", Name = "x", Path = dir },
                new MainViewModel(new FakeDialogs()), split: true);
            await vm.ReloadAsync();

            var naAba = status.PendingFiles;
            // o painel pode listar o mesmo arquivo em duas seções (preparado e alterado):
            // a aba conta arquivos, então compara-se por caminho distinto
            var noPainel = vm.Staged.Select(f => f.Path)
                .Concat(vm.Unstaged.Select(f => f.Path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            _saida.WriteLine($"{cenario,-28} aba={naAba}  painel={noPainel}" +
                             (naAba == noPainel ? "" : "   <<< DIVERGE"));

            Assert.True(naAba == noPainel,
                $"{cenario}: a aba mostra {naAba} e o painel lista {noPainel}");
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public Task Arquivo_modificado() => Conferir("modificado", dir =>
    {
        File.WriteAllText(Path.Combine(dir, "base.txt"), "mudou\n");
        return Task.CompletedTask;
    });

    [Fact]
    public Task Arquivo_preparado() => Conferir("preparado", async dir =>
    {
        File.WriteAllText(Path.Combine(dir, "base.txt"), "mudou\n");
        await GitService.RunAsync(dir, new[] { "add", "base.txt" });
    });

    [Fact]
    public Task Preparado_e_alterado_de_novo() => Conferir("preparado + alterado", async dir =>
    {
        File.WriteAllText(Path.Combine(dir, "base.txt"), "mudou\n");
        await GitService.RunAsync(dir, new[] { "add", "base.txt" });
        File.WriteAllText(Path.Combine(dir, "base.txt"), "mudou de novo\n");
    });

    [Fact]
    public Task Arquivo_novo() => Conferir("novo (não rastreado)", dir =>
    {
        File.WriteAllText(Path.Combine(dir, "novo.txt"), "novo\n");
        return Task.CompletedTask;
    });

    [Fact]
    public Task Arquivo_apagado() => Conferir("apagado", dir =>
    {
        File.Delete(Path.Combine(dir, "base.txt"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Arquivo_renomeado() => Conferir("renomeado", async dir =>
    {
        File.Move(Path.Combine(dir, "base.txt"), Path.Combine(dir, "outro.txt"));
        await GitService.RunAsync(dir, new[] { "add", "-A" });
    });

    [Fact]
    public Task Arquivo_em_conflito() => Conferir("em conflito", async dir =>
    {
        await GitService.RunAsync(dir, new[] { "checkout", "-qb", "outra" });
        File.WriteAllText(Path.Combine(dir, "base.txt"), "versao da outra\n");
        await GitService.RunAsync(dir, new[] { "commit", "-qam", "outra" });
        await GitService.RunAsync(dir, new[] { "checkout", "-q", "main" });
        File.WriteAllText(Path.Combine(dir, "base.txt"), "versao da main\n");
        await GitService.RunAsync(dir, new[] { "commit", "-qam", "main" });
        try { await GitService.RunAsync(dir, new[] { "merge", "outra" }); }
        catch (GitException) { /* o conflito é o objetivo */ }
    });
}
