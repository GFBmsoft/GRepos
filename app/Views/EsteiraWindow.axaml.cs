using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class EsteiraWindow : Window
{
    public EsteiraWindow()
    {
        InitializeComponent();
    }

    public EsteiraWindow(string slug, string branch, string usuario, string repoNome) : this()
    {
        var vm = new EsteiraViewModel(slug, branch, usuario, repoNome);
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
