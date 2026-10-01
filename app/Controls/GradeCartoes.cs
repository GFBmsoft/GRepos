using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace GRepos.Controls;

/// <summary>
/// Grade dos cartões do painel: tantas colunas quantas couberem com a largura mínima,
/// todas da mesma largura, preenchendo a fileira de ponta a ponta.
///
/// Substitui um WrapPanel com a largura de cada cartão calculada no ViewModel. Lá o
/// tamanho novo chegava um quadro depois do redimensionamento: o painel arrumava os
/// cartões com a largura antiga, eles pulavam de linha e voltavam — a piscada. Aqui a
/// conta é feita dentro do próprio layout, de uma vez.
///
/// Um item marcado com <see cref="LinhaInteiraProperty"/> (o cartão aberto) fica sozinho
/// na fileira, com no máximo <see cref="LarguraAberta"/>.
/// </summary>
public sealed class GradeCartoes : Panel
{
    public static readonly StyledProperty<double> LarguraMinimaProperty =
        AvaloniaProperty.Register<GradeCartoes, double>(nameof(LarguraMinima), 250);

    public static readonly StyledProperty<double> EspacoProperty =
        AvaloniaProperty.Register<GradeCartoes, double>(nameof(Espaco), 10);

    /// <summary>
    /// Teto do item aberto. Sozinho na linha ele ganharia a largura da janela toda, e o
    /// passo a passo da esteira ficava esticado em monitor largo.
    /// </summary>
    public static readonly StyledProperty<double> LarguraAbertaProperty =
        AvaloniaProperty.Register<GradeCartoes, double>(nameof(LarguraAberta), 860);

    public double LarguraAberta
    {
        get => GetValue(LarguraAbertaProperty);
        set => SetValue(LarguraAbertaProperty, value);
    }

    /// <summary>Marca o item que ocupa a linha inteira. Vai no contêiner do item.</summary>
    public static readonly AttachedProperty<bool> LinhaInteiraProperty =
        AvaloniaProperty.RegisterAttached<GradeCartoes, Control, bool>("LinhaInteira");

    static GradeCartoes()
    {
        AffectsMeasure<GradeCartoes>(LarguraMinimaProperty, EspacoProperty, LarguraAbertaProperty);

        // quem muda é o filho (o cartão abriu); quem precisa refazer a conta é a grade
        LinhaInteiraProperty.Changed.AddClassHandler<Control>((filho, _) =>
            (filho.GetVisualParent() as GradeCartoes)?.InvalidateMeasure());
    }

    public double LarguraMinima
    {
        get => GetValue(LarguraMinimaProperty);
        set => SetValue(LarguraMinimaProperty, value);
    }

    public double Espaco
    {
        get => GetValue(EspacoProperty);
        set => SetValue(EspacoProperty, value);
    }

    public static bool GetLinhaInteira(Control c) => c.GetValue(LinhaInteiraProperty);
    public static void SetLinhaInteira(Control c, bool valor) => c.SetValue(LinhaInteiraProperty, valor);

    /// <summary>Colunas e largura de cada uma para a largura disponível.</summary>
    public static (int Colunas, double Largura) Dividir(double disponivel, double minima, double espaco)
    {
        if (double.IsInfinity(disponivel) || disponivel <= 0) return (1, minima);

        var colunas = Math.Max(1, (int)((disponivel + espaco) / (minima + espaco)));
        var largura = (disponivel - (colunas - 1) * espaco) / colunas;
        return (colunas, Math.Max(0, largura));
    }

    protected override Size MeasureOverride(Size disponivel)
    {
        var (colunas, largura) = Dividir(disponivel.Width, LarguraMinima, Espaco);
        var total = double.IsInfinity(disponivel.Width) ? largura : disponivel.Width;

        double altura = 0, alturaDaLinha = 0;
        var coluna = 0;

        foreach (var filho in Children)
        {
            if (!filho.IsVisible) continue;

            if (GetLinhaInteira(filho))
            {
                if (coluna > 0) { altura += alturaDaLinha + Espaco; coluna = 0; alturaDaLinha = 0; }

                filho.Measure(new Size(Math.Min(total, LarguraAberta), double.PositiveInfinity));
                altura += filho.DesiredSize.Height + Espaco;
                continue;
            }

            filho.Measure(new Size(largura, double.PositiveInfinity));
            alturaDaLinha = Math.Max(alturaDaLinha, filho.DesiredSize.Height);

            if (++coluna == colunas)
            {
                altura += alturaDaLinha + Espaco;
                coluna = 0;
                alturaDaLinha = 0;
            }
        }

        if (coluna > 0) altura += alturaDaLinha + Espaco;
        if (altura > 0) altura -= Espaco; // sem espaço sobrando depois da última fileira

        return new Size(double.IsInfinity(disponivel.Width) ? largura * colunas : disponivel.Width, altura);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (colunas, largura) = Dividir(final.Width, LarguraMinima, Espaco);

        double y = 0, alturaDaLinha = 0;
        var coluna = 0;

        foreach (var filho in Children)
        {
            if (!filho.IsVisible) continue;

            if (GetLinhaInteira(filho))
            {
                if (coluna > 0) { y += alturaDaLinha + Espaco; coluna = 0; alturaDaLinha = 0; }

                filho.Arrange(new Rect(0, y, Math.Min(final.Width, LarguraAberta), filho.DesiredSize.Height));
                y += filho.DesiredSize.Height + Espaco;
                continue;
            }

            // a fileira toda fica com a altura do cartão mais alto, como numa grade
            var alturaFileira = AlturaDaFileira(coluna == 0 ? filho : null, colunas);
            if (coluna == 0) alturaDaLinha = alturaFileira;

            filho.Arrange(new Rect(coluna * (largura + Espaco), y, largura, alturaDaLinha));

            if (++coluna == colunas)
            {
                y += alturaDaLinha + Espaco;
                coluna = 0;
                alturaDaLinha = 0;
            }
        }

        return final;
    }

    /// <summary>Altura da fileira que começa em <paramref name="primeiro"/>.</summary>
    private double AlturaDaFileira(Control? primeiro, int colunas)
    {
        if (primeiro is null) return 0;

        var altura = 0.0;
        var contados = 0;
        var achou = false;

        foreach (var filho in Children)
        {
            if (!achou) { achou = ReferenceEquals(filho, primeiro); if (!achou) continue; }
            if (!filho.IsVisible) continue;
            if (GetLinhaInteira(filho)) break;

            altura = Math.Max(altura, filho.DesiredSize.Height);
            if (++contados == colunas) break;
        }
        return altura;
    }
}
