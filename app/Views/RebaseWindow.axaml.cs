using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.Models;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class RebaseWindow : Window
{
    public RebaseWindow()
    {
        InitializeComponent();
    }

    public RebaseWindow(MainViewModel main, Repo repo, string hash) : this()
    {
        var vm = new RebaseViewModel(repo, hash)
        {
            Fechar = _ => Close(),
            Confirmar = (titulo, mensagem) => new ConfirmWindow(titulo, mensagem).ShowDialog<bool>(this),
        };
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Cancelar(object? sender, RoutedEventArgs e) => Close();
}
