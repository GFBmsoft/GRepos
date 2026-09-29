using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Preparar um arquivo pelo botão "+" não pode recriar a lista inteira: era o que
/// fazia o grid piscar (e ainda perdia rolagem e seleção).
/// </summary>
public class ChangesReloadTests
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

    private static async Task<string> RepoComTresAlteracoes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-chg-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
        await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
        await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });

        foreach (var nome in new[] { "Unit1.pas", "Unit2.pas", "Unit3.pas" })
            File.WriteAllText(Path.Combine(dir, nome), "inicial\n");

        await GitService.RunAsync(dir, new[] { "add", "." });
        await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });

        foreach (var nome in new[] { "Unit1.pas", "Unit2.pas", "Unit3.pas" })
            File.WriteAllText(Path.Combine(dir, nome), "alterado\n");

        return dir;
    }

    private static ChangesViewModel Vm(string dir) =>
        new(new Repo { Id = "r1", Name = "Demo", Path = dir }, new MainViewModel(new FakeDialogs()), split: true);

    [Fact]
    public async Task Preparar_um_arquivo_preserva_os_itens_dos_outros()
    {
        var dir = await RepoComTresAlteracoes();
        try
        {
            var vm = Vm(dir);
            await vm.ReloadAsync();
            Assert.Equal(3, vm.Unstaged.Count);

            var unit1 = vm.Unstaged.Single(f => f.Path == "Unit1.pas");
            var unit3 = vm.Unstaged.Single(f => f.Path == "Unit3.pas");
            var colecaoAntes = vm.Unstaged;

            await vm.StageCommand.ExecuteAsync(vm.Unstaged.Single(f => f.Path == "Unit2.pas"));

            Assert.Same(colecaoAntes, vm.Unstaged);   // a coleção é a mesma instância
            Assert.Equal(2, vm.Unstaged.Count);
            Assert.Same(unit1, vm.Unstaged.Single(f => f.Path == "Unit1.pas"));
            Assert.Same(unit3, vm.Unstaged.Single(f => f.Path == "Unit3.pas"));
            Assert.Single(vm.Staged);
            Assert.Equal("Unit2.pas", vm.Staged[0].Path);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Recarga_sem_mudanca_nao_mexe_na_lista()
    {
        var dir = await RepoComTresAlteracoes();
        try
        {
            var vm = Vm(dir);
            await vm.ReloadAsync();

            var eventos = 0;
            void Handler(object? s, NotifyCollectionChangedEventArgs e) => eventos++;
            vm.Unstaged.CollectionChanged += Handler;

            // é o que o watcher dispara logo após qualquer operação
            await vm.ReloadAsync(silent: true);

            vm.Unstaged.CollectionChanged -= Handler;
            Assert.Equal(0, eventos);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Arquivo_novo_no_disco_entra_sem_recriar_a_lista()
    {
        var dir = await RepoComTresAlteracoes();
        try
        {
            var vm = Vm(dir);
            await vm.ReloadAsync();
            var unit1 = vm.Unstaged.Single(f => f.Path == "Unit1.pas");

            File.WriteAllText(Path.Combine(dir, "Novo.pas"), "novo\n");
            await vm.ReloadAsync(silent: true);

            Assert.Equal(4, vm.Unstaged.Count);
            Assert.Same(unit1, vm.Unstaged.Single(f => f.Path == "Unit1.pas"));
        }
        finally { Limpar(dir); }
    }
}
