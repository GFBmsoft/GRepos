using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class DiffView : UserControl
{
    public DiffView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            PushCharWidth();
            PushViewport();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PushCharWidth();
        PushViewport();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PushViewport();
    }

    /// <summary>Largura útil = painel menos a barra de rolagem vertical.</summary>
    private void PushViewport()
    {
        if (DataContext is DiffViewModel vm && Bounds.Width > 0)
            vm.ViewportWidth = Bounds.Width - 14;
    }

    /// <summary>
    /// Mede um caractere da fonte monoespaçada e entrega ao ViewModel: a largura das
    /// colunas do diff sai daí, e chutar esse número desalinha ou corta o texto.
    /// </summary>
    private void PushCharWidth()
    {
        if (DataContext is not DiffViewModel vm) return;

        var family = this.TryFindResource("MonoFont", out var res) && res is FontFamily f
            ? f
            : FontFamily.Default;
        var text = new FormattedText(
            new string('0', 100),
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(family),
            12,
            Brushes.Black);

        vm.CharWidth = text.Width / 100;
    }
}
