using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class PainelView : UserControl
{
    /// <summary>Margens do painel mais o espaço da barra de rolagem.</summary>
    private const double Folgas = 46;

    public PainelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => PassarLargura();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        PassarLargura();
    }

    /// <summary>
    /// A largura útil vai para o ViewModel, que divide entre as colunas. Mesmo caminho
    /// do diff: quem sabe o tamanho é a View, quem decide o layout é o ViewModel.
    /// </summary>
    private void PassarLargura()
    {
        if (DataContext is PainelViewModel vm && Bounds.Width > 0)
            vm.LarguraDisponivel = Bounds.Width - Folgas;
    }
}
