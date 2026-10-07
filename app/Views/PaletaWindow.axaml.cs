using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class PaletaWindow : Window
{
    public PaletaWindow()
    {
        InitializeComponent();
    }

    public PaletaWindow(PaletaViewModel vm) : this()
    {
        DataContext = vm;

        var caixa = this.FindControl<TextBox>("Caixa");
        var lista = this.FindControl<ListBox>("Lista");
        Opened += (_, _) => caixa?.Focus();

        // clicar fora fecha, como qualquer paleta: ela é um atalho, não uma tela. O
        // próprio fechamento desativa a janela, e não pode fechar de novo
        var fechando = false;
        Closing += (_, _) => fechando = true;
        Deactivated += (_, _) => { if (!fechando) Close(); };

        // Tunnel: as setas e o Enter são da lista mesmo com o foco na caixa de texto
        AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Down: vm.Mover(1); break;
                case Key.Up: vm.Mover(-1); break;
                case Key.Enter:
                    if (vm.Escolher()) Close();
                    break;
                case Key.Escape: Close(); break;
                default: return;
            }

            if (vm.Selecionado is not null) lista?.ScrollIntoView(vm.Selecionado);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        if (lista is not null)
            lista.Tapped += (_, e) =>
            {
                // clique na barra de rolagem não é escolha
                if ((e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is null) return;
                if (vm.Escolher()) Close();
            };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
