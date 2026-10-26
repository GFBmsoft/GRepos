using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using GRepos.Controls;

namespace GRepos.Views;

/// <summary>
/// Barra de ferramentas que tira as legendas dos botões quando não cabe na janela, e as
/// devolve quando volta a caber. Com um repositório do GitHub a barra inteira passa de
/// 1400 px, e a janela pode estreitar até 1240: sem isso, os botões da direita saíam
/// cortados.
///
/// A decisão compara a largura que a barra teria <i>com</i> as legendas com a que existe.
/// Com a barra compacta as legendas estão escondidas, então a largura delas é calculada
/// pelo texto; assim a decisão não oscila entre um modo e outro.
/// </summary>
public static class BarraCompacta
{
    public const string Classe = "barraCompacta";

    /// <summary>O ícone dentro do botão: abaixo disso a legenda não alarga o botão.</summary>
    private const double LarguraDoIcone = 21;

    private static readonly Dictionary<(string, double), double> Legendas = new();

    public static void Acompanhar(Border barra) => barra.LayoutUpdated += (_, _) => Ajustar(barra);

    private static void Ajustar(Border barra)
    {
        if (barra.Child is not Grid grade || !barra.IsEffectivelyVisible || barra.Bounds.Width <= 0) return;

        var compacta = barra.Classes.Contains(Classe);
        var disponivel = barra.Bounds.Width - barra.Padding.Left - barra.Padding.Right;
        var deve = LarguraCheia(grade, compacta) > disponivel;
        if (deve != compacta) barra.Classes.Set(Classe, deve);
    }

    /// <summary>
    /// Soma das colunas de tamanho fixo. A coluna elástica (o vão do aviso) fica de fora:
    /// ela só ocupa o que sobra.
    /// </summary>
    private static double LarguraCheia(Grid grade, bool compacta)
    {
        var total = 0.0;
        foreach (var filho in grade.Children.Where(c => c.IsVisible))
        {
            var coluna = Grid.GetColumn(filho);
            if (coluna < grade.ColumnDefinitions.Count && grade.ColumnDefinitions[coluna].Width.IsStar) continue;

            total += filho.DesiredSize.Width;
            if (!compacta) continue;

            var botoes = filho is ToolButton b ? new[] { b } : filho.GetVisualDescendants().OfType<ToolButton>();
            foreach (var botao in botoes.Where(t => t.IsEffectivelyVisible && !t.Classes.Contains("semCompactar")))
                total += System.Math.Max(0, LarguraDaLegenda(botao) - LarguraDoIcone);
        }
        return total;
    }

    private static double LarguraDaLegenda(ToolButton botao)
    {
        if (string.IsNullOrEmpty(botao.Label)) return 0;
        if (botao.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "Legenda") is not { } legenda)
            return 0;

        var chave = (botao.Label, legenda.FontSize);
        if (Legendas.TryGetValue(chave, out var largura)) return largura;

        var tipo = new Typeface(legenda.FontFamily, legenda.FontStyle, legenda.FontWeight);
        using var texto = new TextLayout(botao.Label, tipo, legenda.FontSize, null);
        return Legendas[chave] = texto.WidthIncludingTrailingWhitespace;
    }
}
