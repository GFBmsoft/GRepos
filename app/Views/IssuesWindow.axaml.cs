using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class IssuesWindow : Window
{
    public IssuesWindow()
    {
        InitializeComponent();
    }

    public IssuesWindow(IssuesViewModel vm) : this()
    {
        // a confirmação de excluir comentário abre sobre esta janela
        vm.Editor.Confirmar = (titulo, mensagem) => new ConfirmWindow(titulo, mensagem).ShowDialog<bool>(this);
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
