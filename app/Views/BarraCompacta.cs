using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using GRepos.Controls;

namespace GRepos.Views;

/// <summary>
/// Barra de ferramentas que tira as legendas dos botões quando não cabe na janela, e as
/// devolve quando volta a caber. Com um repositório do GitHub a barra inteira passa de
/// 1400 px, e a janela estreita bem menos que isso: sem compactar, os botões da direita
/// saíam cortados.
///
/// A decisão compara a largura da barra <i>com</i> as legendas com a que existe. Essa
/// largura só é real com a barra cheia, então é guardada ali e vale enquanto a barra estiver
/// compacta com os mesmos textos. Texto novo (outra branch, outro repositório) não tem
/// largura guardada: a barra fica cheia por um passo de layout, antes de desenhar, só para
/// medir. A largura nunca é estimada: bastava errar um pixel para os dois modos discordarem
/// e a barra alternar até o Avalonia derrubar o app com "Infinite layout loop detected".
/// </summary>
public static class BarraCompacta
{
    public const string Classe = "barraCompacta";

    public static void Acompanhar(Border barra)
    {
        // a última largura medida com as legendas, e de quais textos ela é
        var medidos = 0;
        var cheia = 0.0;

        barra.LayoutUpdated += (_, _) =>
        {
            if (barra.Child is not Grid grade || !barra.IsEffectivelyVisible || barra.Bounds.Width <= 0) return;

            var compacta = barra.Classes.Contains(Classe);
            var textos = Textos(grade);
            if (!compacta) (medidos, cheia) = (textos, ColunasFixas(grade).Sum(c => c.DesiredSize.Width));

            var disponivel = barra.Bounds.Width - barra.Padding.Left - barra.Padding.Right;
            var deve = textos == medidos && cheia > disponivel;
            if (deve != compacta) barra.Classes.Set(Classe, deve);
        };
    }

    /// <summary>
    /// O que tem tamanho próprio. A coluna elástica (o vão do aviso) fica de fora: ela só
    /// ocupa o que sobra.
    /// </summary>
    private static IEnumerable<Control> ColunasFixas(Grid grade) =>
        grade.Children.Where(c => c.IsVisible &&
                                  (Grid.GetColumn(c) >= grade.ColumnDefinitions.Count ||
                                   !grade.ColumnDefinitions[Grid.GetColumn(c)].Width.IsStar));

    /// <summary>Os textos à vista, que são o que muda a largura da barra de um repositório para outro.</summary>
    private static int Textos(Grid grade)
    {
        var textos = new HashCode();
        foreach (var coluna in ColunasFixas(grade))
        foreach (var peca in coluna is Panel painel ? painel.Children.ToArray() : new[] { coluna })
            if (peca.IsVisible)
                textos.Add(peca switch { ToolButton b => b.Label, TextBlock t => t.Text, _ => null });
        return textos.ToHashCode();
    }
}
