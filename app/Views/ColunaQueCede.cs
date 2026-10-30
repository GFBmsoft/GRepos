using System.Linq;
using Avalonia.Controls;
using GRepos.Services;

namespace GRepos.Views;

/// <summary>
/// Grade de três colunas (fixa, divisor, elástica com mínimo): em espaço apertado a fixa
/// cede, e volta à largura escolhida quando a grade alarga. Sem isso o mínimo da elástica
/// passa da borda, e o que mora nela sai cortado.
/// </summary>
public static class ColunaQueCede
{
    public static void Acompanhar(Grid grade)
    {
        var fixa = grade.ColumnDefinitions[0];
        var divisor = grade.ColumnDefinitions[1];
        var elastica = grade.ColumnDefinitions[2];
        var escolhida = fixa.Width.Value;

        foreach (var arrastavel in grade.Children.OfType<GridSplitter>())
            arrastavel.DragCompleted += (_, _) => escolhida = fixa.ActualWidth;

        grade.SizeChanged += (_, e) =>
        {
            var cabe = e.NewSize.Width - divisor.Width.Value - elastica.MinWidth;
            fixa.Width = new GridLength(JanelaGuardada.ColunaQueCede(escolhida, fixa.MinWidth, cabe));
        };
    }
}
