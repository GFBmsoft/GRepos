using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class PublicarRepoWindow : Window
{
    public PublicarRepoWindow()
    {
        InitializeComponent();
    }

    public PublicarRepoWindow(PublicarRepoViewModel vm) : this()
    {
        vm.Fechar = Close;
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Cancelar(object? sender, RoutedEventArgs e) => Close();
}
