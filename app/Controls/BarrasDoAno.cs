using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GRepos.Services;

namespace GRepos.Controls;

/// <summary>
/// O ano em barrinhas, uma por dia, embaixo do quadro de sequências (o cartão "Streaks" do
/// github-cool-readme). Os dias da sequência atual e da maior saem na cor de destaque; os
/// outros, apagados. Ocupa a largura que tiver: em quadro estreito as barras afinam.
/// </summary>
public sealed class BarrasDoAno : Control
{
    public static readonly StyledProperty<IReadOnlyList<DiaContribuicao>?> DiasProperty =
        AvaloniaProperty.Register<BarrasDoAno, IReadOnlyList<DiaContribuicao>?>(nameof(Dias));

    public IReadOnlyList<DiaContribuicao>? Dias
    {
        get => GetValue(DiasProperty);
        set => SetValue(DiasProperty, value);
    }

    /// <summary>A cor dos dias em sequência (a cor de destaque dos quadros); sem ela, o verde.</summary>
    public static readonly StyledProperty<IBrush?> DestaqueProperty =
        AvaloniaProperty.Register<BarrasDoAno, IBrush?>(nameof(Destaque));

    public IBrush? Destaque
    {
        get => GetValue(DestaqueProperty);
        set => SetValue(DestaqueProperty, value);
    }

    static BarrasDoAno()
    {
        AffectsRender<BarrasDoAno>(DiasProperty, DestaqueProperty);
        AffectsMeasure<BarrasDoAno>(DiasProperty);
    }

    public BarrasDoAno()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Height = 22;
    }

    private List<DiaContribuicao> _ordenados = new();
    private Sequencias? _sequencias;
    private double _teto = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        _ordenados = Dias?.OrderBy(d => d.Data).ToList() ?? new();
        _sequencias = EstatisticasDoPerfil.Sequencias(_ordenados);
        _teto = Teto(_ordenados);
        return base.MeasureOverride(availableSize);
    }

    /// <summary>
    /// A altura cheia vale para o dia no percentil 95, não para o maior: um dia fora da curva
    /// achataria todo o resto do ano numa linha.
    /// </summary>
    public static double Teto(IReadOnlyList<DiaContribuicao> dias)
    {
        var positivos = dias.Where(d => d.Quantidade > 0).Select(d => d.Quantidade).OrderBy(q => q).ToList();
        if (positivos.Count == 0) return 1;
        return Math.Max(1, positivos[(int)Math.Floor((positivos.Count - 1) * 0.95)]);
    }

    public override void Render(DrawingContext context)
    {
        if (_ordenados.Count == 0 || Bounds.Width <= 0) return;

        var destaque = Destaque ?? ResourceBrushConverter.Resolve("Green") ?? Brushes.Green;
        var apagada = (ResourceBrushConverter.Resolve("TextFaint") as ISolidColorBrush)?.Color ?? Colors.Gray;
        var comum = new SolidColorBrush(apagada, 0.45);

        var passo = Bounds.Width / _ordenados.Count;
        var largura = Math.Max(0.6, passo * (passo >= 2 ? 0.7 : 1));
        var altura = Bounds.Height;

        for (var i = 0; i < _ordenados.Count; i++)
        {
            var dia = _ordenados[i];
            if (dia.Quantidade <= 0) continue;

            var h = Math.Max(1.5, altura * Math.Min(1, dia.Quantidade / _teto));
            var pincel = _sequencias?.EmSequencia(dia.Data) == true ? destaque : comum;
            context.FillRectangle(pincel, new Rect(i * passo, altura - h, largura, h));
        }
    }

    /// <summary>O dia sob o ponteiro, para a dica.</summary>
    public DiaContribuicao? DiaEm(Point p)
    {
        if (_ordenados.Count == 0 || Bounds.Width <= 0 || p.X < 0) return null;
        var i = (int)(p.X / (Bounds.Width / _ordenados.Count));
        return i < _ordenados.Count ? _ordenados[i] : null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var dia = DiaEm(e.GetPosition(this));
        var dica = dia is null ? null : GradeContribuicoes.Dica(dia);
        if (!Equals(ToolTip.GetTip(this), dica))
        {
            ToolTip.SetTip(this, dica);
            ToolTip.SetIsOpen(this, dica is not null);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }
}
