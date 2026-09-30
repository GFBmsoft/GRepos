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

        // a consulta periódica vive enquanto a janela existe: fechada, não há
        // motivo para continuar gastando cota da API do GitHub
        Opened += async (_, _) => await vm.IniciarAsync();
        Closed += (_, _) => vm.Parar();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Fechar(object? sender, RoutedEventArgs e) => Close();
}
