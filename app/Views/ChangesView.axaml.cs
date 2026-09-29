using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GRepos.Views;

public partial class ChangesView : UserControl
{
    public ChangesView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
