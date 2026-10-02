using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GRepos.Models;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class FileHistoryWindow : Window
{
    public FileHistoryWindow()
    {
        InitializeComponent();
    }

    public FileHistoryWindow(Repo repo, string caminho, bool blame, bool split) : this()
    {
        var vm = new FileHistoryViewModel(repo, caminho, blame, split);
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();

        var lista = this.FindControl<ListBox>("ListaBlame")!;
        lista.DoubleTapped += (_, _) =>
        {
            if (lista.SelectedItem is BlameLineViewModel linha) vm.IrParaCommit(linha);
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
