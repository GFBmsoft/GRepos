using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

    public BranchesWindow(MainViewModel main, Repo repo) : this()
    {
        var vm = new BranchesViewModel(repo, main)
        {
            // os diálogos do git-flow abrem sobre esta janela, não sobre a principal
            PedirTexto = (titulo, rotulo) => new PromptWindow(titulo, rotulo, "").ShowDialog<string?>(this),
            Confirmar = (titulo, mensagem) => new ConfirmWindow(titulo, mensagem).ShowDialog<bool>(this),
            EditarFluxo = atual => new GitFlowWindow(atual).ShowDialog<GitFlowConfig?>(this),
        };
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
