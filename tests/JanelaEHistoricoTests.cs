using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// A janela reabre como ficou da última vez, e o histórico abre já num commit, sem
/// precisar clicar para ver alguma coisa.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class JanelaEHistoricoTests
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

    /// <summary>Workspace numa pasta temporária, restaurando o GREPOS_HOME que havia antes.</summary>
    private static async Task Com(Func<string, Task> teste)
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-janela-" + Path.GetRandomFileName());
        Directory.CreateDirectory(home);
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            await teste(home);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try
            {
                foreach (var f in Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(home, true);
            }
            catch (Exception) { /* pasta temporária */ }
        }
    }

    // ----------------------------------------------------------------- janela

    [Fact]
    public void Sem_nada_guardado_vale_o_tamanho_padrao()
    {
        Assert.Equal((1440, 900), JanelaGuardada.Tamanho(0, 0, 1440, 900, 960, 600));
    }

    [Fact]
    public void Tamanho_guardado_volta_mas_cabe_no_monitor_de_agora()
    {
        Assert.Equal((1200, 800), JanelaGuardada.Tamanho(1200, 800, 1440, 900, 960, 600, 1920, 1040));

        // guardado num monitor grande, aberto num notebook: encolhe para caber, com folga
        Assert.Equal((1326, 688), JanelaGuardada.Tamanho(2400, 1300, 1440, 900, 960, 600, 1366, 728));

        // e nunca abaixo do mínimo da janela, mesmo num monitor menor que ele
        Assert.Equal((960, 600), JanelaGuardada.Tamanho(1200, 800, 1440, 900, 960, 600, 800, 500));
    }

    [Fact]
    public Task Guardar_a_janela_antes_de_ler_o_workspace_nao_grava_nada() => Com(home =>
    {
        // um workspace com repositório no disco; a janela fecha antes do InitAsync
        var ws = new Workspace { Repos = { new Repo { Id = "r", Name = "Financeiro", Path = @"C:\repos\fin" } } };
        WorkspaceStore.Save(ws);

        new MainViewModel(new FakeDialogs()).SetJanela(1200, 800, false);

        Assert.Single(WorkspaceStore.Load().Repos); // o workspace do usuário continua lá
        Assert.Equal(0, WorkspaceStore.Load().Settings.JanelaLargura);
        _ = home;
        return Task.CompletedTask;
    });

    [Fact]
    public Task Janela_guardada_sobrevive_a_fechar_e_abrir() => Com(async _ =>
    {
        var main = new MainViewModel(new FakeDialogs());
        await main.InitAsync();

        main.SetJanela(1200, 800, maximizada: true);
        var lido = WorkspaceStore.Load().Settings;
        Assert.Equal((1200, 800, true), (lido.JanelaLargura, lido.JanelaAltura, lido.JanelaMaximizada));

        // maximizada, o tamanho informado pode vir zerado: fica o normal de antes
        main.SetJanela(0, 0, maximizada: false);
        lido = WorkspaceStore.Load().Settings;
        Assert.Equal((1200, 800, false), (lido.JanelaLargura, lido.JanelaAltura, lido.JanelaMaximizada));
    });

    [AvaloniaFact]
    public Task Janela_abre_no_tamanho_guardado_centralizada_e_guarda_ao_fechar() => Com(async _ =>
    {
        var ws = new Workspace();
        ws.Settings.JanelaLargura = 1100;
        ws.Settings.JanelaAltura = 700;
        WorkspaceStore.Save(ws);

        var janela = new MainWindow();
        Assert.Equal((1100, 700), (janela.Width, janela.Height));
        Assert.Equal(WindowStartupLocation.CenterScreen, janela.WindowStartupLocation);
        Assert.Equal(WindowState.Normal, janela.WindowState);

        await ((MainViewModel)janela.DataContext!).InitAsync();
        janela.Show();
        janela.WindowState = WindowState.Maximized;
        janela.Close();

        // fechou maximizada: reabre maximizada, e o tamanho normal de antes fica guardado
        var lido = WorkspaceStore.Load().Settings;
        Assert.True(lido.JanelaMaximizada);
        Assert.Equal((1100, 700), (lido.JanelaLargura, lido.JanelaAltura));

        var outra = new MainWindow();
        Assert.Equal(WindowState.Maximized, outra.WindowState);
        Assert.Equal((1100, 700), (outra.Width, outra.Height));
    });

    // -------------------------------------------------------------- histórico

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task<string> RepoComTresCommits(string home)
    {
        var dir = Path.Combine(home, "repo");
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        foreach (var msg in new[] { "primeiro", "segundo", "terceiro" })
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), msg + "\n");
            await Git(dir, "add", "-A");
            await Git(dir, "commit", "-qm", msg);
        }
        return dir;
    }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 100 && !condicao(); i++) await Task.Delay(50);
    }

    [Fact]
    public Task Historico_abre_no_commit_mais_recente_com_o_detalhe_carregado() => Com(async home =>
    {
        var dir = await RepoComTresCommits(home);
        var vm = new HistoryViewModel(new Repo { Id = "r", Name = "Demo", Path = dir }, new MainViewModel(new FakeDialogs()), 50, split: true);

        await vm.LoadAsync();

        Assert.Equal("terceiro", vm.SelectedCommit?.Subject);
        await Esperar(() => vm.HasDetail);
        Assert.True(vm.HasDetail);
        Assert.Equal("terceiro", vm.DetailSubject);
    });

    [Fact]
    public Task Recarregar_o_historico_mantem_o_commit_que_estava_escolhido() => Com(async home =>
    {
        var dir = await RepoComTresCommits(home);
        var vm = new HistoryViewModel(new Repo { Id = "r", Name = "Demo", Path = dir }, new MainViewModel(new FakeDialogs()), 50, split: true);
        await vm.LoadAsync();

        vm.SelectedCommit = vm.Commits.Single(c => c.Subject == "primeiro");
        await vm.LoadAsync();
        Assert.Equal("primeiro", vm.SelectedCommit?.Subject);

        // se ele saiu da lista (a busca não o encontra), vale o mais recente dos que ficaram
        vm.Busca = "segundo";
        await vm.LoadAsync();
        Assert.Equal("segundo", vm.SelectedCommit?.Subject);
    });

    [Fact]
    public Task Repositorio_sem_commits_nao_escolhe_nada_e_diz_isso() => Com(async home =>
    {
        var dir = Path.Combine(home, "vazio");
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "main");

        var vm = new HistoryViewModel(new Repo { Id = "r", Name = "Vazio", Path = dir }, new MainViewModel(new FakeDialogs()), 50, split: true);
        await vm.LoadAsync();

        Assert.Null(vm.SelectedCommit);
        Assert.False(vm.HasDetail);
    });
}
