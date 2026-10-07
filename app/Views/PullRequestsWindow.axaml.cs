using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class PullRequestsWindow : Window
{
    public PullRequestsWindow()
    {
        InitializeComponent();
    }

    public PullRequestsWindow(PullRequestsViewModel vm) : this()
    {
        DataContext = vm;
        // a consulta periódica vive enquanto a janela existe, como na esteira
        Opened += async (_, _) => await vm.IniciarAsync();
        Closed += (_, _) => vm.Parar();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
