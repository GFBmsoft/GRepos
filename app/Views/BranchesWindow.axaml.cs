using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GRepos.Controls;
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
            ConfirmarExclusao = (titulo, mensagem) =>
                new ConfirmWindow(titulo, mensagem, "Excluir", perigo: true).ShowDialog<bool>(this),
            EditarFluxo = atual => new GitFlowWindow(atual).ShowDialog<GitFlowConfig?>(this),
            MostrarPullRequests = prs => new PullRequestsWindow(prs).ShowDialog(this),
            FecharJanela = Close,
        };
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();

        // soltar uma branch sobre outra: mesclar, rebase ou pull request
        ArrasteDeBranchNaTela.Habilitar(this,
            dc => dc is BranchItemViewModel b ? new RefDeBranch(b.Name, b.IsRemote) : null,
            () => vm.TemGitHub,
            vm.ExecutarOpcaoAsync);

        ContextRequested += (_, e) =>
        {
            if (BranchSob(e.Source) is not { IsHead: false } item || e.Source is not Control onde) return;
            MenuDaBranch(vm, item).ShowAt(onde, showAtPointer: true);
            e.Handled = true;
        };
        AddHandler(Gestures.TappedEvent, (_, e) => TrocarAoClicar(vm, e));
    }

    /// <summary>A branch da linha sob o ponteiro: o DataContext muda quando chega no item.</summary>
    private static BranchItemViewModel? BranchSob(object? fonte)
    {
        BranchItemViewModel? item = null;
        for (var v = fonte as Visual; v is not null && item is null; v = v.GetVisualParent())
            item = (v as StyledElement)?.DataContext as BranchItemViewModel;
        return item;
    }

    /// <summary>
    /// Clicar na linha troca de branch. Tapped só vem de clique: quem arrasta para mesclar não
    /// troca sem querer, e o botão direito é outro evento. Botão dentro da linha tem comando próprio.
    /// </summary>
    private static void TrocarAoClicar(BranchesViewModel vm, TappedEventArgs e)
    {
        if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null) return;
        if (!vm.Busy && BranchSob(e.Source) is { CanCheckout: true } item) vm.TrocarCommand.Execute(item);
    }

    /// <summary>
    /// Clique direito numa branch: trocar, excluir e as mesmas operações do arraste, sempre
    /// contra a branch atual, para quem prefere o menu. Montado aqui porque o menu abre fora
    /// da árvore da janela, onde um binding para o DataContext dela não chega.
    /// </summary>
    public MenuFlyout MenuDaBranch(BranchesViewModel vm, BranchItemViewModel item)
    {
        MenuItem Item(string texto, string glifo, string cor, ICommand comando)
        {
            var i = ItemDeMenu.Novo(texto, glifo, cor);
            i.Command = comando;
            i.CommandParameter = item;
            return i;
        }

        var menu = new MenuFlyout();
        menu.Items.Add(Item($"Trocar para {item.Name}", Glifos.Trocar, "verde", vm.TrocarCommand));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item($"Mesclar {item.Name} na branch atual", Glifos.Mesclar, "roxo", vm.MesclarNaAtualCommand));
        menu.Items.Add(Item($"Rebase da branch atual sobre {item.Name}", Glifos.Rebase, "roxo", vm.RebaseDaAtualSobreCommand));

        if (_main is not null && _repo is not null)
        {
            var comparar = ItemDeMenu.Novo($"Comparar {item.Name} com a branch atual…", Glifos.Comparar, "azul");
            comparar.Click += async (_, _) => await new CompararWindow(
                new CompararViewModel(_repo, item.Name, vm.BranchAtual, _main.SplitDiff)).ShowDialog(this);
            menu.Items.Add(comparar);
        }

        if (vm.TemGitHub)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item($"Abrir pull request de {item.NomeLocal} para a atual…", Glifos.PullRequest, "roxo",
                vm.PullRequestParaAtualCommand));
            menu.Items.Add(Item($"Abrir pull request da atual para {item.NomeLocal}…", Glifos.PullRequest, "roxo",
                vm.PullRequestDaAtualParaCommand));
        }

        if (item.PodeExcluir)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item($"Excluir {item.Name}…", Glifos.Excluir, "vermelho", vm.ExcluirCommand));
        }

        return menu;
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
