using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GRepos.Services;

namespace GRepos.Controls;

/// <summary>
/// Um quadro retrô do cartão de perfil: o <see cref="DisplayDeLeds"/> desenhado em LEDs redondos
/// (com ou sem a grade dos apagados) ou em pixels quadrados (OLED), sempre na proporção de
/// 150 × 73 pontos, ocupando a largura que tiver.
///
/// O relógio anda a cada 40 ms, mas só redesenha quando o desenho muda: tudo no display anda de
/// ponto em ponto, e entre uma entrada e outra o quadro fica parado sem custo nenhum.
/// </summary>
public sealed class PainelDeLeds : Control
{
    public static readonly StyledProperty<DisplayDeLeds?> DisplayProperty =
        AvaloniaProperty.Register<PainelDeLeds, DisplayDeLeds?>(nameof(Display));

    /// <summary>Id de <see cref="CoresDeDestaque"/>.</summary>
    public static readonly StyledProperty<string> CorProperty =
        AvaloniaProperty.Register<PainelDeLeds, string>(nameof(Cor), "github");

    /// <summary>Id de <see cref="EstilosDosQuadros"/>.</summary>
    public static readonly StyledProperty<string> EstiloProperty =
        AvaloniaProperty.Register<PainelDeLeds, string>(nameof(Estilo), "pontos");

    /// <summary>Desligado, o quadro fica parado no primeiro slide (as capturas dos testes).</summary>
    public static readonly StyledProperty<bool> AnimadoProperty =
        AvaloniaProperty.Register<PainelDeLeds, bool>(nameof(Animado), true);

    public DisplayDeLeds? Display
    {
        get => GetValue(DisplayProperty);
        set => SetValue(DisplayProperty, value);
    }

    public string Cor
    {
        get => GetValue(CorProperty);
        set => SetValue(CorProperty, value);
    }

    public string Estilo
    {
        get => GetValue(EstiloProperty);
        set => SetValue(EstiloProperty, value);
    }

    public bool Animado
    {
        get => GetValue(AnimadoProperty);
        set => SetValue(AnimadoProperty, value);
    }

    static PainelDeLeds()
    {
        AffectsRender<PainelDeLeds>(DisplayProperty, CorProperty, EstiloProperty, AnimadoProperty);
    }

    private readonly Stopwatch _relogio = new();
    private DispatcherTimer? _timer;
    private long _ultimoEstado = long.MinValue;

    public PainelDeLeds()
    {
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>O tempo do quadro, em segundos; nulo com ele parado.</summary>
    public double? Tempo => Animado && _relogio.IsRunning ? _relogio.Elapsed.TotalSeconds : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var largura = double.IsInfinity(availableSize.Width) ? 450 : availableSize.Width;
        return new Size(largura, largura * DisplayDeLeds.Linhas / DisplayDeLeds.Colunas);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Ligar();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Desligar();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // quadro novo começa do começo: o anel enche de novo, o primeiro slide entra
        if (change.Property == DisplayProperty || change.Property == AnimadoProperty)
        {
            Desligar();
            if (VisualRoot is not null) Ligar();
        }
    }

    private void Ligar()
    {
        if (!Animado || Display is null) return;

        _relogio.Restart();
        _ultimoEstado = long.MinValue;
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Render, Tique);
        _timer.Start();
    }

    private void Desligar()
    {
        _timer?.Stop();
        _relogio.Reset();
    }

    private void Tique(object? sender, EventArgs e)
    {
        if (Display is not { } display || !IsEffectivelyVisible) return;

        var estado = display.Estado(_relogio.Elapsed.TotalSeconds);
        if (estado == _ultimoEstado) return;

        _ultimoEstado = estado;
        InvalidateVisual();
    }

    // ------------------------------------------------------------------------------ desenho

    private StreamGeometry? _grade;
    private (double Passo, bool Pontos) _chaveDaGrade;

    public override void Render(DrawingContext context)
    {
        if (Display is not { } display || Bounds.Width <= 0) return;

        var cores = CoresDeDestaque.De(Cor).Display;
        var estilo = EstilosDosQuadros.De(Estilo);
        var passo = Bounds.Width / DisplayDeLeds.Colunas;

        if (cores.Painel is { } painel)
            context.DrawRectangle(new SolidColorBrush(Resolver(painel)), null, new Rect(Bounds.Size), 6, 6);

        // os LEDs apagados, bem fracos atrás de tudo
        if (estilo.Pontos && estilo.Grade)
        {
            if (_grade is null || _chaveDaGrade != (passo, true))
            {
                _grade = Geometria(TodasAsCasas(), passo, pontos: true);
                _chaveDaGrade = (passo, true);
            }
            context.DrawGeometry(new SolidColorBrush(Resolver(cores.Apagados), cores.ForcaDosApagados), null, _grade);
        }

        foreach (var grupo in display.Camadas(Tempo).GroupBy(c => (c.Tom, c.Opacidade)))
        {
            var (tom, opacidade) = grupo.Key;
            var cor = Resolver(tom switch
            {
                Tom.Destaque => cores.Destaque,
                Tom.Brilho => cores.Brilho,
                _ => cores.Aceso,
            });
            var forca = opacidade * (tom == Tom.Apagado ? cores.Apagado : 1);
            context.DrawGeometry(new SolidColorBrush(cor, forca), null,
                Geometria(grupo.SelectMany(c => c.Pontos), passo, estilo.Pontos));
        }
    }

    private static IEnumerable<Ponto> TodasAsCasas()
    {
        for (var r = 0; r < DisplayDeLeds.Linhas; r++)
            for (var c = 0; c < DisplayDeLeds.Colunas; c++)
                yield return new Ponto(c, r);
    }

    /// <summary>
    /// As casas numa geometria só: LED redondo (80% da casa) ou pixel quadrado. Os quadrados
    /// se sobrepõem um fio para não deixar emenda, e o preenchimento NonZero junta tudo numa
    /// forma: a sobreposição não escurece a camada fraca.
    /// </summary>
    public static StreamGeometry Geometria(IEnumerable<Ponto> casas, double passo, bool pontos)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.SetFillRule(FillRule.NonZero);

        var raio = passo * 0.4;
        var vistos = new HashSet<Ponto>();
        foreach (var c in casas)
        {
            if (c.Col < 0 || c.Lin < 0 || c.Col >= DisplayDeLeds.Colunas || c.Lin >= DisplayDeLeds.Linhas) continue;
            if (!vistos.Add(c)) continue;

            if (pontos)
            {
                var cx = (c.Col + 0.5) * passo;
                var cy = (c.Lin + 0.5) * passo;
                ctx.BeginFigure(new Point(cx - raio, cy), true);
                ctx.ArcTo(new Point(cx + raio, cy), new Size(raio, raio), 0, false, SweepDirection.Clockwise);
                ctx.ArcTo(new Point(cx - raio, cy), new Size(raio, raio), 0, false, SweepDirection.Clockwise);
                ctx.EndFigure(true);
            }
            else
            {
                var x = c.Col * passo;
                var y = c.Lin * passo;
                var lado = passo + 0.35;
                ctx.BeginFigure(new Point(x, y), true);
                ctx.LineTo(new Point(x + lado, y));
                ctx.LineTo(new Point(x + lado, y + lado));
                ctx.LineTo(new Point(x, y + lado));
                ctx.EndFigure(true);
            }
        }
        return geo;
    }

    /// <summary>Hex direto, ou um recurso do tema ("Text", "Lane0"), que muda com o tema.</summary>
    private static Color Resolver(string cor)
    {
        if (cor.StartsWith('#')) return Color.Parse(cor);
        return (ResourceBrushConverter.Resolve(cor) as ISolidColorBrush)?.Color ?? Colors.Gray;
    }
}
