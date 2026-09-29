using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GRepos.Views;

public partial class PairView : UserControl
{
    public PairView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
