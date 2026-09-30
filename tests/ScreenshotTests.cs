using System;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Renderiza as telas de verdade e grava PNG quando GREPOS_SHOTS aponta uma pasta.
/// Serve para conferir layout sem abrir janela na máquina de quem está trabalhando.
/// </summary>
public class ScreenshotTests
{
    private static string? OutDir => Environment.GetEnvironmentVariable("GREPOS_SHOTS");

    private static void Shot(Control view, object dataContext, string nome, int w = 1280, int h = 800)
    {
        var window = new Window { Width = w, Height = h, Content = view };
        view.DataContext = dataContext;
        window.Show();
        window.Measure(new Size(w, h));
        window.Arrange(new Rect(0, 0, w, h));

        var dir = OutDir;
        if (dir is null) return; // sem pasta configurada o teste só valida a montagem

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(dir, nome + ".png"));
    }

    private static HistoryViewModel HistoricoDeExemplo()
    {
        var vm = new HistoryViewModel(new Repo { Id = "r1", Name = "Financeiro", Path = "." },
            new MainViewModel(new FakeDialogs()), 100, split: true);

        var commits = new[]
        {
            Commit("4372d2c",
                "feat: O pacote que nunca atualiza pode ser calado, e atualizar não abre mais janela — assunto bem longo para forçar o corte",
                new[] { "8fb0c29" }, "HEAD -> main", "tag: 1.0.0.27", "origin/main", "origin/HEAD"),
            Commit("8fb0c29", "Merge da feature de juros no main", new[] { "f4d81d8", "023b4e7" }),
            Commit("023b4e7", "Adiciona calculo de juros compostos", new[] { "c2bbdc4" }, "feature/juros"),
            Commit("f4d81d8", "Adiciona parametros de configuracao", new[] { "c2bbdc4" }),
            Commit("c2bbdc4", "Estrutura inicial do modulo financeiro", Array.Empty<string>()),
        };

        var rows = GraphBuilder.Build(commits);
        var max = GraphBuilder.MaxLanes(rows);
        foreach (var r in rows)
            vm.Commits.Add(new CommitRowViewModel { Commit = r.Commit, Row = r, MaxLanes = max });

        vm.DetailSubject = "Merge da feature de juros no main";
        vm.DetailAuthor = "Gabriel Ferreira <gabriel@bmsoft.com.br>";
        vm.DetailDate = "29/09/2026 10:49:06";
        vm.DetailHash = "8fb0c29";
        vm.DetailParents = "f4d81d8, 023b4e7";
        vm.DetailBody = "Traz o cálculo de juros compostos para o main.";
        vm.HasDetail = true;

        vm.Files.Add(new CommitFileViewModel { File = new CommitFile { Path = "Financeiro.pas", Added = 2, Removed = 0, Status = "M" } });
        vm.Files.Add(new CommitFileViewModel { File = new CommitFile { Path = "Config.ini", Added = 1, Removed = 0, Status = "A" } });

        vm.Diff.CharWidth = 7.2;
        vm.Diff.ViewportWidth = 560;
        vm.Diff.Title = "Financeiro.pas";
        vm.Diff.Load("""
        diff --git a/Financeiro.pas b/Financeiro.pas
        --- a/Financeiro.pas
        +++ b/Financeiro.pas
        @@ -1,7 +1,9 @@
         unit Financeiro;

         interface
        +
        +function CalcularJuros(Valor: Currency): Currency;

         implementation

         end.
        """);
        return vm;
    }

    private static Commit Commit(string hash, string subject, string[] parents, params string[] refs) => new()
    {
        Hash = hash + new string('0', 33),
        Subject = subject,
        Parents = new System.Collections.Generic.List<string>(Array.ConvertAll(parents, p => p + new string('0', 33))),
        Author = "Gabriel Ferreira",
        Email = "gabriel@bmsoft.com.br",
        Date = "2026-09-29T10:49:06-03:00",
        Refs = new System.Collections.Generic.List<string>(refs),
    };

    private sealed class FakeDialogs : IDialogService
    {
        public System.Threading.Tasks.Task<bool> ConfirmAsync(string t, string m) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<string?> PickFolderAsync(string t) => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<string?> PromptAsync(string t, string l, string i = "") => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => System.Threading.Tasks.Task.FromResult<(string, string)?>(null);
        public System.Threading.Tasks.Task ShowAddRepoAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowRepoConfigAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowSettingsAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowBranchesAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowEsteiraAsync(string s, string b, string u, string n) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowStashAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Janela inteira, para conferir moldura, seções e rodapé.</summary>
    private static void ShotJanela(Window window, string nome, int w, int h)
    {
        window.Width = w;
        window.Height = h;
        window.Show();
        window.Measure(new Size(w, h));
        window.Arrange(new Rect(0, 0, w, h));

        var dir = OutDir;
        if (dir is null) return;

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(dir, nome + ".png"));
    }

    /// <summary>
    /// Preferências com grupos: são as linhas de grupo, com botões encostados à direita,
    /// que denunciam a barra de rolagem passando por cima do conteúdo.
    /// </summary>
    [AvaloniaFact]
    public void Preferencias_com_secoes_e_grupos()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-prefs-" + Path.GetRandomFileName());
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            var main = new MainViewModel(new FakeDialogs());
            main.CreateGroup("Manuais - BMSoft", "#DB4C9B");
            main.CreateGroup("Módulos", "#1F9D55");

            ShotJanela(new SettingsWindow(main), "preferencias", 480, 720);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", null);
            try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }

    [AvaloniaFact]
    public void Historico_com_commit_selecionado()
    {
        Shot(new HistoryView(), HistoricoDeExemplo(), "historico");
    }

    [AvaloniaFact]
    public void Historico_no_tema_claro()
    {
        var app = Application.Current!;
        var antes = app.RequestedThemeVariant;
        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        try
        {
            Shot(new HistoryView(), HistoricoDeExemplo(), "historico-claro");
        }
        finally
        {
            app.RequestedThemeVariant = antes;
        }
    }

    /// <summary>Sidebar com os grupos coloridos e os repositórios sob cada um.</summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task Sidebar_com_grupos_coloridos()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-side-" + Path.GetRandomFileName());
        var raiz = Path.Combine(home, "repos");
        Directory.CreateDirectory(raiz);
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            var grupos = new[]
            {
                new Group { Id = "g1", Name = "Manuais - BMSoft", Color = "#DB4C9B" },
                new Group { Id = "g2", Name = "Módulos", Color = "#1F9D55" },
            };

            var ws = new Workspace { Groups = { grupos[0], grupos[1] } };
            var nomes = new (string Nome, string Grupo, string? Papel)[]
            {
                ("Backup", "g1", null),
                ("BmIntegra", "g1", null),
                ("Financeiro", "g1", null),
                ("Notas (DBISAM)", "g2", "origem"),
                ("Notas (MySQL)", "g2", "destino"),
            };

            var i = 0;
            foreach (var (nome, grupo, papel) in nomes)
            {
                var dir = Path.Combine(raiz, "r" + i++);
                Directory.CreateDirectory(dir);
                await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
                await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
                await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
                File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
                await GitService.RunAsync(dir, new[] { "add", "." });
                await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });
                if (nome == "BmIntegra") File.WriteAllText(Path.Combine(dir, "a.txt"), "dois\n");

                ws.Repos.Add(new Repo
                {
                    Id = "r" + i,
                    Name = nome,
                    Path = dir,
                    GroupId = grupo,
                    Role = papel,
                    PairKey = papel is null ? null : "Notas",
                });
            }

            WorkspaceStore.Save(ws);

            var janela = new MainWindow { Width = 300, Height = 420 };
            await ((MainViewModel)janela.DataContext!).InitAsync();
            janela.Show();
            janela.Measure(new Size(300, 420));
            janela.Arrange(new Rect(0, 0, 300, 420));

            var dir2 = OutDir;
            if (dir2 is not null)
            {
                Directory.CreateDirectory(dir2);
                using (var frame = janela.CaptureRenderedFrame())
                    frame?.Save(Path.Combine(dir2, "sidebar-grupos.png"));

                // a pílula usa a cor com transparência: precisa aparecer também no claro
                var app = Application.Current!;
                var antes = app.RequestedThemeVariant;
                app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                janela.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                janela.Measure(new Size(300, 420));
                janela.Arrange(new Rect(0, 0, 300, 420));
                using (var frame = janela.CaptureRenderedFrame())
                    frame?.Save(Path.Combine(dir2, "sidebar-grupos-claro.png"));
                app.RequestedThemeVariant = antes;
            }
            janela.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", null);
            try
            {
                foreach (var f in Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(home, true);
            }
            catch (Exception) { /* pasta temporária */ }
        }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Tela_de_branches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-brshot-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
            await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
            File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "Feat: Melhoria na rotina de estoques" });
            foreach (var b in new[] { "develop", "feat/RefatoracaoRest", "feat/SincEstoques", "fix/PagPorOffSet", "fix/WatermarkProdutoNaoIntegrado" })
                await GitService.RunAsync(dir, new[] { "branch", b });

            var vm = new BranchesViewModel(
                new Repo { Id = "r1", Name = "BM2Maga", Path = dir },
                new MainViewModel(new FakeDialogs()));
            await vm.CarregarAsync();

            var janela = new BranchesWindow { DataContext = vm, Width = 720, Height = 560 };
            janela.Show();
            janela.Measure(new Size(720, 560));
            janela.Arrange(new Rect(0, 0, 720, 560));

            var outDir = OutDir;
            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir);
                using var frame = janela.CaptureRenderedFrame();
                frame?.Save(Path.Combine(outDir, "branches.png"));
            }
            janela.Close();
        }
        finally
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
            catch (Exception) { /* pasta temporária */ }
        }
    }

    [AvaloniaFact]
    public void Dialogo_de_configuracao_do_repositorio()
    {
        var main = new MainViewModel(new FakeDialogs());
        var repo = new Repo
        {
            Id = "r1",
            Name = "BM2Maga (DBISAM)",
            Path = Path.Combine("D:", "Projetos", "DBISAM", "BM2Maga"),
            PairKey = "BM2Maga",
            Role = "origem",
            RemoteTemplate = "https://{{user}}:{{token}}@github.com/bmsoftsistemas/BM2Maga.git",
        };

        var janela = new RepoConfigWindow(main, repo, new FakeDialogs());
        janela.Show();
        janela.Measure(new Size(460, 700));
        janela.Arrange(new Rect(0, 0, 460, 700));

        var dir = OutDir;
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            using var frame = janela.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "config-repo.png"));
        }
        janela.Close();
    }

    [AvaloniaFact]
    public void Dialogo_de_grupo_mostra_a_paleta()
    {
        var janela = new GroupWindow("Novo grupo", "Módulos BMSoft", GroupPalette.Cores[2]);
        janela.Show();
        janela.Measure(new Size(420, 400));
        janela.Arrange(new Rect(0, 0, 420, 400));

        // um botão por cor da paleta, e o escolhido vem marcado
        var swatches = janela.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Tag is string)
            .ToList();

        Assert.Equal(GroupPalette.Cores.Length, swatches.Count);
        Assert.Single(swatches, b => b.BorderThickness.Top > 0);
        Assert.Equal(GroupPalette.Cores[2], swatches.Single(b => b.BorderThickness.Top > 0).Tag);

        var dir = OutDir;
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            using var frame = janela.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "dialogo-grupo.png"));
        }
        janela.Close();
    }

    /// <summary>A sidebar espremida no mínimo ainda precisa mostrar os quatro ícones.</summary>
    [AvaloniaFact]
    public void Janela_com_a_sidebar_no_tamanho_minimo()
    {
        // workspace descartável: o teste não pode depender (nem escrever) no do usuário
        var home = Path.Combine(Path.GetTempPath(), "grepos-ui-" + Path.GetRandomFileName());
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        var window = new MainWindow { Width = 1000, Height = 640 };
        window.Show();
        window.Measure(new Size(1000, 640));
        window.Arrange(new Rect(0, 0, 1000, 640));

        var coluna = window.GetVisualDescendants()
            .OfType<Grid>()
            .First(g => g.ColumnDefinitions.Count == 3 && g.ColumnDefinitions[0].MinWidth > 100)
            .ColumnDefinitions[0];

        coluna.Width = new GridLength(coluna.MinWidth);
        window.Measure(new Size(1000, 640));
        window.Arrange(new Rect(0, 0, 1000, 640));

        var botoes = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("icon"))
            .ToList();

        Assert.Equal(4, botoes.Count);
        foreach (var b in botoes)
            Assert.True(b.Bounds.Width >= 28, $"ícone espremido: {b.Bounds.Width:F0}px");

        var dir = OutDir;
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "sidebar-minima.png"));
        }
        window.Close();
        Environment.SetEnvironmentVariable("GREPOS_HOME", null);
        try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
    }
}
