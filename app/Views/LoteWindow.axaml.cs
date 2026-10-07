using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class LoteWindow : Window
{
    public LoteWindow()
    {
        InitializeComponent();
    }

    public LoteWindow(LoteViewModel vm) : this()
    {
        vm.Confirmar = (titulo, mensagem) => new ConfirmWindow(titulo, mensagem).ShowDialog<bool>(this);
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarBranchesAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
