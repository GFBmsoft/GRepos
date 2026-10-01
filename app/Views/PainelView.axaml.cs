using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GRepos.Views;

/// <summary>
/// A largura dos cartões é decidida pela <see cref="Controls.GradeCartoes"/>, dentro do
/// layout. Antes a View repassava a largura ao ViewModel a cada redimensionamento, e o
/// valor chegava um quadro atrasado — era o que fazia os cartões piscarem.
/// </summary>
public partial class PainelView : UserControl
{
    public PainelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
