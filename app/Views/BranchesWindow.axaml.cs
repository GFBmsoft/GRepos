using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.Models;
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
        var vm = new BranchesViewModel(repo, main);
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
