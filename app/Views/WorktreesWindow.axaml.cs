using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GRepos.Models;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class WorktreesWindow : Window
{
    public WorktreesWindow()
    {
        InitializeComponent();
    }

    public WorktreesWindow(MainViewModel main, Repo repo) : this()
    {
        var vm = new WorktreesViewModel(repo, main)
        {
            Confirmar = (titulo, mensagem) => new ConfirmWindow(titulo, mensagem).ShowDialog<bool>(this),
            EscolherPasta = async titulo =>
            {
                var pastas = await StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions { Title = titulo, AllowMultiple = false });
                return pastas.FirstOrDefault()?.TryGetLocalPath();
            },
        };
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
