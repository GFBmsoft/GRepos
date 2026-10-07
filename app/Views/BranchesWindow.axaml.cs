using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class BranchesWindow : Window
{
    public BranchesWindow()
    {
        InitializeComponent();
    }

    private readonly MainViewModel? _main;
    private readonly Repo? _repo;

    public BranchesWindow(MainViewModel main, Repo repo) : this()
    {
        _main = main;
        _repo = repo;
        var vm = new BranchesViewModel(repo, main)
        {
            // os diálogos do git-flow abrem sobre esta janela, não sobre a principal
            PedirTexto = (titulo, rotulo) => new PromptWindow(titulo, rotulo, "").ShowDialog<string?>(this),
            Confirmar = (titulo, mensagem) => new ConfirmWindow(titulo, mensagem).ShowDialog<bool>(this),
            EditarFluxo = atual => new GitFlowWindow(atual).ShowDialog<GitFlowConfig?>(this),
            MostrarPullRequests = prs => new PullRequestsWindow(prs).ShowDialog(this),
        };
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();

        // soltar uma branch sobre outra: mesclar, rebase ou pull request
        ArrasteDeBranchNaTela.Habilitar(this,
            dc => dc is BranchItemViewModel b ? new RefDeBranch(b.Name, b.IsRemote) : null,
            () => vm.TemGitHub,
            vm.ExecutarOpcaoAsync);

        ContextRequested += (_, e) => MenuDaBranch(vm, e);
    }

    /// <summary>
    /// Clique direito numa branch: as mesmas operações do arraste, sempre contra a branch
    /// atual, para quem prefere o menu. Montado aqui porque o menu abre fora da árvore da
    /// janela, onde um binding para o DataContext dela não chega.
    /// </summary>
    private void MenuDaBranch(BranchesViewModel vm, ContextRequestedEventArgs e)
    {
        BranchItemViewModel? item = null;
        for (var v = e.Source as Visual; v is not null && item is null; v = v.GetVisualParent())
            item = (v as StyledElement)?.DataContext as BranchItemViewModel;

        if (item is null || item.IsHead || e.Source is not Control onde) return;

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuItem
        {
            Header = $"Mesclar {item.Name} na branch atual",
            Command = vm.MesclarNaAtualCommand, CommandParameter = item,
        });
        menu.Items.Add(new MenuItem
        {
            Header = $"Rebase da branch atual sobre {item.Name}",
            Command = vm.RebaseDaAtualSobreCommand, CommandParameter = item,
        });

        if (_main is not null && _repo is not null)
        {
            var comparar = new MenuItem { Header = $"Comparar {item.Name} com a branch atual…" };
            comparar.Click += async (_, _) => await new CompararWindow(
                new CompararViewModel(_repo, item.Name, vm.BranchAtual, _main.SplitDiff)).ShowDialog(this);
            menu.Items.Add(comparar);
        }

        if (vm.TemGitHub)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem
            {
                Header = $"Abrir pull request de {item.NomeLocal} para a atual…",
                Command = vm.PullRequestParaAtualCommand, CommandParameter = item,
            });
            menu.Items.Add(new MenuItem
            {
                Header = $"Abrir pull request da atual para {item.NomeLocal}…",
                Command = vm.PullRequestDaAtualParaCommand, CommandParameter = item,
            });
        }

        menu.ShowAt(onde, showAtPointer: true);
        e.Handled = true;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void AbrirWorktrees(object? sender, RoutedEventArgs e)
    {
        if (_main is null || _repo is null) return;

        await new WorktreesWindow(_main, _repo).ShowDialog(this);
        if (DataContext is BranchesViewModel vm) await vm.CarregarAsync();
    }

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
