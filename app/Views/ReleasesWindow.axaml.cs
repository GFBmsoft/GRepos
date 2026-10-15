using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class ReleasesWindow : Window
{
    public ReleasesWindow()
    {
        InitializeComponent();
    }

    public ReleasesWindow(ReleasesViewModel vm) : this()
    {
        // confirmação e seletor de arquivos abrem sobre esta janela, não sobre a principal
        vm.Confirmar = (titulo, mensagem) => new ConfirmWindow(titulo, mensagem).ShowDialog<bool>(this);
        vm.EscolherArquivos = async () =>
        {
            var arquivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Arquivos para a release",
                AllowMultiple = true,
            });
            return arquivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
        };
        DataContext = vm;
        Opened += async (_, _) => await vm.CarregarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
