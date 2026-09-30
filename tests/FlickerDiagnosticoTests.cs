using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace GRepos.Tests;

/// <summary>Conta quantas vezes a tela é remontada num único clique de "preparar".</summary>
public class FlickerDiagnosticoTests
{
    private readonly ITestOutputHelper _saida;

    public FlickerDiagnosticoTests(ITestOutputHelper saida) => _saida = saida;

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

    [Fact]
    public async Task Quantas_remontagens_um_stage_provoca()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-flick-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
            await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
            foreach (var n in new[] { "a.pas", "b.pas", "c.pas" })
                File.WriteAllText(Path.Combine(dir, n), "inicial\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });
            foreach (var n in new[] { "a.pas", "b.pas", "c.pas" })
                File.WriteAllText(Path.Combine(dir, n), "alterado\n");

            var vm = new ChangesViewModel(
                new Repo { Id = "r1", Name = "Demo", Path = dir },
                new MainViewModel(new FakeDialogs()), split: true);

            await vm.ReloadAsync();

            var eventosLista = 0;
            var trocasDeRows = 0;
            var mudancasDeSelecao = 0;

            void Lista(object? s, NotifyCollectionChangedEventArgs e) => eventosLista++;
            void Props(object? s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(DiffViewModel.Rows)) trocasDeRows++;
            }
            void Selecao(object? s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is nameof(ChangesViewModel.SelectedStaged) or nameof(ChangesViewModel.SelectedUnstaged))
                    mudancasDeSelecao++;
            }

            // os botões de TODAS as linhas usam o mesmo comando: se ele fica
            // indisponível durante a execução, a lista inteira acinzenta e volta
            var trocasDeDisponibilidade = 0;
            void Disponibilidade(object? s, EventArgs e) => trocasDeDisponibilidade++;

            vm.Unstaged.CollectionChanged += Lista;
            vm.Staged.CollectionChanged += Lista;
            vm.Diff.PropertyChanged += Props;
            vm.PropertyChanged += Selecao;
            vm.StageCommand.CanExecuteChanged += Disponibilidade;
            vm.UnstageCommand.CanExecuteChanged += Disponibilidade;
            vm.DiscardCommand.CanExecuteChanged += Disponibilidade;

            await vm.StageCommand.ExecuteAsync(vm.Unstaged.Single(f => f.Path == "b.pas"));

            _saida.WriteLine($"eventos de lista .......... {eventosLista}");
            _saida.WriteLine($"trocas de Diff.Rows ....... {trocasDeRows}");
            _saida.WriteLine($"mudanças de seleção ....... {mudancasDeSelecao}");
            _saida.WriteLine($"botões acinzentando ....... {trocasDeDisponibilidade}");

            Assert.True(trocasDeDisponibilidade == 0,
                $"os botões das linhas mudaram de estado {trocasDeDisponibilidade} vezes — é a piscada");

            // um stage mexe em duas listas (sai de uma, entra na outra): 2 é o mínimo
            Assert.True(eventosLista <= 2, $"lista remontada demais: {eventosLista} eventos");
            Assert.True(trocasDeRows <= 1, $"diff remontado {trocasDeRows} vezes");
        }
        finally { Limpar(dir); }
    }

    /// <summary>
    /// A varredura do cronômetro acontece sozinha a cada minuto. Se ela mexer no
    /// Scanning, o botão "Atualizar todos" acinzenta e volta por conta própria — e de
    /// fora parece que alguém clicou nele.
    /// </summary>
    [Fact]
    public async Task Varredura_automatica_nao_pisca_o_botao_de_atualizar()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-auto-" + Path.GetRandomFileName());
        var repo = Path.Combine(home, "repo");
        Directory.CreateDirectory(repo);

        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            await GitService.RunAsync(repo, new[] { "init", "-q", "-b", "main" });

            var main = new MainViewModel(new FakeDialogs());
            main.AddRepository(repo, "Demo", null);

            var trocas = 0;
            main.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.Scanning)) trocas++;
            };

            await main.RefreshAllSilenciosoAsync();
            _saida.WriteLine($"trocas de Scanning na automática ... {trocas}");
            Assert.True(trocas == 0, $"a varredura automática mexeu no botão {trocas} vezes");

            // a que o usuário pediu continua sinalizando: ele quer ver que está rodando
            await main.RefreshAllAsync();
            Assert.Equal(2, trocas);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(home);
        }
    }
}
