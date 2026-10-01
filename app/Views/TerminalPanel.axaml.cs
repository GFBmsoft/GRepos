using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using AvaloniaTerminal;

namespace GRepos.Views;

public partial class TerminalPanel : UserControl
{
    public TerminalPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Leva o teclado ao terminal visível: abrir o painel é para digitar.</summary>
    public void Focar() =>
        this.GetVisualDescendants().OfType<TerminalControl>().FirstOrDefault(t => t.IsVisible)?.Focus();
}
