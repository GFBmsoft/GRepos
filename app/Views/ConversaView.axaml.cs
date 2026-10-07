using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GRepos.Views;

public partial class ConversaView : UserControl
{
    public ConversaView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
