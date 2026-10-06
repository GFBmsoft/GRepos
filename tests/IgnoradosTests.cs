using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Ignorar alterações só nesta máquina: skip-worktree no arquivo rastreado, info/exclude no
/// novo, e a volta dos dois — num repositório real. Mais a barra de título do Windows 10.
/// </summary>
public class IgnoradosTests
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
        var dir = Path.Combine(Path.GetTempPath(), "grepos-ign-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        await Git(dir, "config", "core.autocrlf", "false");

        Directory.CreateDirectory(Path.Combine(dir, "Impressão"));
        foreach (var nome in new[] { "a.txt", "b.txt", "Impressão/Relatório.txt" })
            File.WriteAllText(Path.Combine(dir, nome), "inicial\n");
        await Git(dir, "add", ".");
        await Git(dir, "commit", "-qm", "inicial");
        return dir;
    }

    private static Repo Repo(string dir) => new() { Id = "r1", Name = "Demo", Path = dir };

    private static ChangesViewModel Changes(string dir) =>
        new(Repo(dir), new MainViewModel(new FakeDialogs()), split: true);

    // ------------------------------------------------------------ lógica pura

    [Theory]
    [InlineData("config.ini", "/config.ini")]
    [InlineData("src/Unit1.pas", "/src/Unit1.pas")]
    [InlineData("pasta/", "/pasta/")]
    [InlineData("a[1]*.txt", "/a\\[1]\\*.txt")]
    [InlineData("fim ", "/fim\\ ")]
    public void Padrao_prende_a_raiz_e_escapa_curingas(string caminho, string esperado) =>
        Assert.Equal(esperado, Ignorados.Padrao(caminho));

    [Fact]
    public void Acrescentar_nao_duplica_e_respeita_a_quebra_do_arquivo()
    {
        Assert.Equal("# local\r\n/a.txt\r\n/b.txt\r\n",
            Ignorados.Acrescentar("# local\r\n/a.txt", new[] { "/a.txt", "/b.txt" }));
        Assert.Equal("/a.txt\n", Ignorados.Acrescentar("", new[] { "/a.txt" }));
    }

    [Fact]
    public void Remover_mantem_comentarios_e_os_outros_padroes()
    {
        Assert.Equal("# local\n*.tmp\n", Ignorados.Remover("# local\n/a.txt\n*.tmp\n", new[] { "/a.txt" }));
        Assert.Equal("", Ignorados.Remover("/a.txt\n", new[] { "/a.txt" }));
    }

    [Fact]
    public void Ls_files_devolve_so_os_marcados_com_skip_worktree() =>
        Assert.Equal(new[] { "b.txt", "pasta/c d.txt" },
            Ignorados.ParseLsFiles("H a.txt\0S b.txt\0h x.txt\0s pasta/c d.txt\0"));

    [Theory]
    [InlineData(17134, null)] // 1803: o atributo ainda não existia
    [InlineData(17763, 19)]   // 1809
    [InlineData(18363, 19)]   // 1909
    [InlineData(19045, 20)]   // 22H2, o Windows 10 de hoje
    [InlineData(22000, null)] // Windows 11: o Avalonia já faz
    [InlineData(26100, null)]
    public void Barra_de_titulo_escolhe_o_atributo_pela_versao(int build, int? esperado) =>
        Assert.Equal(esperado, BarraDeTitulo.Atributo(build));

    // --------------------------------------------------------- repositório real

    [Fact]
    public async Task Ignorar_tudo_esvazia_a_lista_sem_tocar_nos_arquivos()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "Impressão", "Relatório.txt"), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "novo [1].txt"), "novo\n");

            var vm = Changes(dir);
            await vm.ReloadAsync();
            Assert.Equal(3, vm.Unstaged.Count);
            Assert.Equal("Ignorar tudo", vm.IgnorarRotulo);

            await vm.IgnorarTodosCommand.ExecuteAsync(null);

            Assert.Empty(vm.Unstaged);
            Assert.Equal(3, vm.TotalIgnorados);
            Assert.Equal("Ver ignorados (3)…", vm.IgnoradosRotulo);
            Assert.Equal("alterado\n", File.ReadAllText(Path.Combine(dir, "a.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "novo [1].txt")));
            // nada versionado mudou: o .gitignore nem existe
            Assert.False(File.Exists(Path.Combine(dir, ".gitignore")));
            Assert.Equal("", (await Git(dir, "status", "--porcelain")).Trim());
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Com_dois_marcados_ignora_so_eles()
    {
        var dir = await NovoRepo();
        try
        {
            foreach (var nome in new[] { "a.txt", "b.txt" })
                File.WriteAllText(Path.Combine(dir, nome), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "novo.txt"), "novo\n");

            var vm = Changes(dir);
            await vm.ReloadAsync();
            vm.DefinirSelecao(false, vm.Unstaged.Where(f => f.Path != "b.txt"));
            Assert.Equal("Ignorar selecionados (2)", vm.IgnorarRotulo);

            await vm.IgnorarTodosCommand.ExecuteAsync(null);
            Assert.Equal("b.txt", Assert.Single(vm.Unstaged).Path);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Menu_ignora_o_arquivo_aberto_e_voltar_o_traz_de_novo()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "alterado\n");

            var vm = Changes(dir);
            await vm.ReloadAsync();
            vm.SelectedUnstaged = vm.Unstaged.First(f => f.Path == "a.txt");
            await vm.IgnorarArquivoCommand.ExecuteAsync(null);
            Assert.Equal("b.txt", Assert.Single(vm.Unstaged).Path);

            var ignorados = await Ignorados.ListarAsync(dir);
            Assert.Equal(new Ignorado("a.txt", true), Assert.Single(ignorados));

            await Ignorados.VoltarAsync(dir, ignorados);
            await vm.ReloadAsync();
            Assert.Equal(new[] { "a.txt", "b.txt" }, vm.Unstaged.Select(f => f.Path).OrderBy(p => p));
            Assert.Equal(0, vm.TotalIgnorados);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Arquivo_novo_vai_para_o_exclude_e_sai_dele_na_volta()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "novo.txt"), "novo\n");
            await Ignorados.IgnorarAsync(dir, Array.Empty<string>(), new[] { "novo.txt" });

            var exclude = Path.Combine(dir, ".git", "info", "exclude");
            Assert.Contains("/novo.txt", File.ReadAllLines(exclude));
            Assert.Equal("", (await Git(dir, "status", "--porcelain")).Trim());

            await Ignorados.VoltarAsync(dir, new[] { new Ignorado("/novo.txt", false) });
            Assert.DoesNotContain("/novo.txt", File.ReadAllLines(exclude));
            Assert.StartsWith("??", (await Git(dir, "status", "--porcelain")).Trim());
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Gitignore_so_recebe_arquivo_novo()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "novo.txt"), "novo\n");

            var vm = Changes(dir);
            await vm.ReloadAsync();

            // rastreado: o .gitignore não teria efeito, então nem é criado
            vm.SelectedUnstaged = vm.Unstaged.First(f => f.Path == "a.txt");
            await vm.AdicionarAoGitignoreCommand.ExecuteAsync(null);
            Assert.False(File.Exists(Path.Combine(dir, ".gitignore")));

            vm.SelectedUnstaged = vm.Unstaged.First(f => f.Path == "novo.txt");
            await vm.AdicionarAoGitignoreCommand.ExecuteAsync(null);
            Assert.Equal("/novo.txt\n", File.ReadAllText(Path.Combine(dir, ".gitignore")));
            Assert.Equal(new[] { ".gitignore", "a.txt" }, vm.Unstaged.Select(f => f.Path).OrderBy(p => p));
        }
        finally { Limpar(dir); }
    }

    // --------------------------------------------------------------------- tela

    [AvaloniaFact]
    public async Task Janela_de_ignorados_monta_com_a_lista()
    {
        var dir = await NovoRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "alterado\n");
            File.WriteAllText(Path.Combine(dir, "novo.txt"), "novo\n");
            await Ignorados.IgnorarAsync(dir, new[] { "a.txt" }, new[] { "novo.txt" });

            var janela = new IgnoradosWindow(new MainViewModel(new FakeDialogs()), Repo(dir));
            janela.Show();
            await janela.Carga;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // o template de cada linha só é construído com a lista populada
            Assert.Equal(2, janela.GetVisualDescendants().OfType<Button>()
                .Count(b => b.Content as string == "Voltar a acompanhar"));
            var textos = janela.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("a.txt", textos);
            Assert.Contains("/novo.txt", textos);
            janela.Close();
        }
        finally { Limpar(dir); }
    }
}
