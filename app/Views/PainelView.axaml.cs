using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GRepos.Views;

public partial class PainelView : UserControl
{
    public PainelView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
