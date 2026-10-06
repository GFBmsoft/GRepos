using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.Services;
using GRepos.Views;

namespace GRepos;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // toda janela, diálogos inclusive: no Windows 10 a barra de título não segue o tema sozinha
        Window.WindowOpenedEvent.AddClassHandler<Window>((janela, _) => BarraDeTitulo.Acompanhar(janela));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
