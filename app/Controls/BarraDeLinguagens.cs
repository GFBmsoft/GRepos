using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GRepos.Services;

namespace GRepos.Controls;

/// <summary>
/// A barra de linguagens, como a do GitHub e a do github-cool-readme: uma fatia por linguagem,
/// na cor dela, proporcional às linhas. Preenche a largura que tiver, com as pontas redondas.
/// </summary>
public sealed class BarraDeLinguagens : Control
{
    private const double Folga = 2; // entre uma fatia e outra

    public static readonly StyledProperty<IReadOnlyList<FatiaDeLinguagem>?> FatiasProperty =
        AvaloniaProperty.Register<BarraDeLinguagens, IReadOnlyList<FatiaDeLinguagem>?>(nameof(Fatias));

    public IReadOnlyList<FatiaDeLinguagem>? Fatias
    {
        get => GetValue(FatiasProperty);
        set => SetValue(FatiasProperty, value);
    }

    static BarraDeLinguagens() => AffectsRender<BarraDeLinguagens>(FatiasProperty);

    public BarraDeLinguagens() => Height = 8;

    public override void Render(DrawingContext context)
    {
        if (Fatias is not { Count: > 0 } fatias || Bounds.Width <= 0) return;

        var util = Bounds.Width - Folga * (fatias.Count - 1);
        var raio = Bounds.Height / 2;

        using (context.PushClip(new RoundedRect(new Rect(Bounds.Size), raio)))
        {
            var x = 0.0;
            foreach (var f in fatias)
            {
                // fatia minúscula ainda aparece: um fio de 2 px diz que a linguagem existe
                var largura = Math.Max(2, util * f.Fracao);
                context.FillRectangle(new SolidColorBrush(Color.Parse(f.Cor)),
                    new Rect(x, 0, largura, Bounds.Height));
                x += largura + Folga;
            }
        }
    }
}
