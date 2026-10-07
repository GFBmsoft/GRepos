using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class CompararWindow : Window
{
    public CompararWindow()
    {
        InitializeComponent();
    }

    public CompararWindow(CompararViewModel vm) : this()
    {
        DataContext = vm;
        Opened += async (_, _) => await vm.IniciarAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
