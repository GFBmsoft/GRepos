using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GRepos.Services;

namespace GRepos.Controls;

/// <summary>Desenha as raias do grafo de uma linha do histórico.</summary>
public sealed class GraphCell : Control
{
    // abaixo disso as raias estão apertadas: traço e nó afinam para não virar borrão
    private const double StepApertado = 8;

    public static readonly StyledProperty<GraphRow?> RowProperty =
        AvaloniaProperty.Register<GraphCell, GraphRow?>(nameof(Row));

    public static readonly StyledProperty<IBrush?> NodeBorderBrushProperty =
        AvaloniaProperty.Register<GraphCell, IBrush?>(nameof(NodeBorderBrush));

    public static readonly StyledProperty<double> LaneStepProperty =
        AvaloniaProperty.Register<GraphCell, double>(nameof(LaneStep), GraphBuilder.LaneStepNormal);

    public GraphRow? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>Cor do anel em volta do nó — acompanha o fundo da lista.</summary>
    public IBrush? NodeBorderBrush
    {
        get => GetValue(NodeBorderBrushProperty);
        set => SetValue(NodeBorderBrushProperty, value);
    }

    static GraphCell()
    {
        AffectsRender<GraphCell>(RowProperty, NodeBorderBrushProperty, LaneStepProperty);
    }

    public GraphCell()
    {
        // sem isto as raias além da largura da coluna são desenhadas por cima da mensagem
        ClipToBounds = true;
    }

    /// <summary>Distância entre raias; a lista aperta quando há raias demais.</summary>
    public double LaneStep
    {
        get => GetValue(LaneStepProperty);
        set => SetValue(LaneStepProperty, value);
    }

    private double X(int lane) => GraphBuilder.LaneMargin + lane * LaneStep;

    /// <summary>
    /// Cor da raia vem do tema ("Lane0".."Lane7"): no tema claro as cores do escuro
    /// somem no branco. Sem recurso, cai no padrão do GraphBuilder.
    /// </summary>
    private static IBrush LaneBrush(int lane)
    {
        var key = "Lane" + (lane % 8);
        return ResourceBrushConverter.Resolve(key)
               ?? new SolidColorBrush(Color.Parse(GraphBuilder.LaneColor(lane)));
    }

    public override void Render(DrawingContext context)
    {
        var row = Row;
        if (row is null) return;

        var h = Bounds.Height;
        var half = h / 2;

        var apertado = LaneStep < StepApertado;
        var espessura = apertado ? 1.0 : 1.6;

        foreach (var (from, to) in row.EdgesUp)
            DrawCurve(context, X(from), 0, X(to), half, from, espessura);

        foreach (var (from, to) in row.Edges)
            DrawCurve(context, X(from), half, X(to), h, from, espessura);

        var r = (row.Commit.Parents.Count > 1 ? 4.5 : 3.5) - (apertado ? 1 : 0);

        // nó de raia cortada pela borda encosta nela: todo commit tem que ter o seu ponto
        var limite = Bounds.Width - r - 2;
        var center = new Point(limite > 0 ? System.Math.Min(X(row.Lane), limite) : X(row.Lane), half);

        if (NodeBorderBrush is not null)
            context.DrawEllipse(NodeBorderBrush, null, center, r + 1.5, r + 1.5);

        context.DrawEllipse(LaneBrush(row.Lane), null, center, r, r);
    }

    private static void DrawCurve(DrawingContext ctx, double x1, double y1, double x2, double y2, int lane, double espessura)
    {
        var pen = new Pen(LaneBrush(lane), espessura);

        if (System.Math.Abs(x1 - x2) < 0.5)
        {
            ctx.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
            return;
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var mid = (y1 + y2) / 2;
            g.BeginFigure(new Point(x1, y1), false);
            g.CubicBezierTo(new Point(x1, mid), new Point(x2, mid), new Point(x2, y2));
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, geo);
    }
}
