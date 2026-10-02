using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
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
        this.FindControl<ItemsControl>("Linhas")!.Tapped += LinhaTocada;
    }

    /// <summary>
    /// Clique numa linha + ou − marca/desmarca para preparar só ela. O Tag da faixa diz
    /// o lado ("L" esquerda, "R" direita, "U" unificado); o view model ignora o resto.
    /// </summary>
    private void LinhaTocada(object? sender, TappedEventArgs e)
    {
        if (DataContext is not DiffViewModel vm) return;
        for (var v = e.Source as Visual; v is not null && v != sender; v = v.GetVisualParent())
        {
            if (v is Border { Tag: string lado, DataContext: DiffRowBase row })
            {
                vm.AlternarLinha(row, lado == "R");
                return;
            }
        }
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
