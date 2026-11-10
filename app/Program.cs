using Avalonia;

namespace GRepos;

internal static class Program
{
    [System.STAThread]
    public static void Main(string[] args)
    {
        Services.Tempos.Marcar("Main");

        // O token do GitHub sai do credential manager, meio segundo de processador. Pedido
        // aqui, ele chega enquanto a janela é montada. Pedido pelo painel, disputava a máquina
        // com a varredura e levava 3 a 4 segundos, com toda consulta ao GitHub esperando por ele.
        _ = System.Threading.Tasks.Task.Run(Services.GitHubService.AdiantarTokenAsync);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
