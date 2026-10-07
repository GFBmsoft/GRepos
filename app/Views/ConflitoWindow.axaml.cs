using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class ConflitoWindow : Window
{
    public ConflitoWindow()
    {
        InitializeComponent();
    }

    public ConflitoWindow(ConflitoViewModel vm) : this()
    {
        DataContext = vm;
        vm.Fechar += Close;
        Opened += (_, _) => vm.Carregar();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Cancelar(object? sender, RoutedEventArgs e) => Close();
}
