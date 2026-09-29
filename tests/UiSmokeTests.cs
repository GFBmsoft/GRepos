using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using GRepos.Models;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GRepos.Tests.TestAppBuilder))]

namespace GRepos.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Carrega cada tela de verdade: erro de XAML ou de binding só aparece quando o
/// template é construído, e é exatamente o que quebra em produção.
/// </summary>
public class UiSmokeTests
{
    private static Repo DemoRepo() => new()
    {
        Id = "r1",
        Name = "Financeiro",
        Path = System.IO.Directory.GetCurrentDirectory(),
        PairKey = "Financeiro",
        Role = "origem",
    };

    private sealed class FakeDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string title, string label, string initial = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel main) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel main, Repo repo) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel main) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel main, Repo repo) => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel main, Repo repo) => Task.CompletedTask;
    }

    private static void Render(Control view, object dataContext)
    {
        var window = new Window { Width = 1200, Height = 800, Content = view };
        view.DataContext = dataContext;
        window.Show();
        window.Measure(new Size(1200, 800));
        window.Arrange(new Rect(0, 0, 1200, 800));
        window.Close();
    }

    [AvaloniaFact]
    public void ChangesView_monta_com_arquivos_e_diff()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new ChangesViewModel(DemoRepo(), main, split: true);

        // as listas precisam ter itens: erro dentro do ItemTemplate só aparece
        // quando o template é realmente construído
        vm.Staged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "src/Unit1.pas", Index = "M", Worktree = ".", Kind = ChangeKind.Tracked },
            Staged = true,
        });
        vm.Unstaged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "novo.txt", Index = ".", Worktree = "?", Kind = ChangeKind.Untracked },
            Staged = false,
        });

        vm.Diff.Load("""
        diff --git a/a.txt b/a.txt
        --- a/a.txt
        +++ b/a.txt
        @@ -1,2 +1,2 @@
         um
        -dois
        +DOIS
        """, "Preparar bloco", _ => Task.CompletedTask);

        Render(new ChangesView(), vm);
    }

    [AvaloniaFact]
    public void ChangesView_monta_no_modo_unificado()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new ChangesViewModel(DemoRepo(), main, split: false);
        vm.Diff.Load("@@ -1 +1 @@\n-a\n+b\n");

        Render(new ChangesView(), vm);
    }

    [AvaloniaFact]
    public void HistoryView_monta_com_grafo_e_detalhe()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new HistoryViewModel(DemoRepo(), main, 50, split: true);
        vm.Commits.Add(new CommitRowViewModel
        {
            Commit = new Commit { Hash = "abc1234567", Subject = "Commit de teste", Author = "Teste", Date = "2026-01-01T10:00:00Z", Refs = { "HEAD -> main", "origin/main" } },
            Row = new GRepos.Services.GraphRow(),
            MaxLanes = 2,
        });
        vm.Files.Add(new CommitFileViewModel { File = new CommitFile { Path = "a.txt", Added = 3, Removed = 1, Status = "M" } });
        vm.HasDetail = true;

        Render(new HistoryView(), vm);
    }

    [AvaloniaFact]
    public void PairView_monta_com_os_dois_lados()
    {
        var main = new MainViewModel(new FakeDialogs());
        var a = DemoRepo();
        var b = new Repo { Id = "r2", Name = "Financeiro MySQL", Path = a.Path, PairKey = "Financeiro", Role = "destino" };
        var vm = new PairViewModel(a, b, main, 50);
        vm.Left.Status = new RepoStatus { Branch = "main", Ahead = 1 };
        vm.Right.Status = new RepoStatus { Branch = "main", Unstaged = 2 };
        vm.Left.OnlyHere.Add(new PairCommitViewModel { Subject = "Só na origem", Short = "abc1234" });

        Render(new PairView(), vm);
    }

    [AvaloniaFact]
    public void MainWindow_abre_com_a_arvore_e_a_barra()
    {
        var window = new MainWindow();
        window.Show();
        window.Measure(new Size(1400, 900));
        window.Arrange(new Rect(0, 0, 1400, 900));
        window.Close();
    }
}
