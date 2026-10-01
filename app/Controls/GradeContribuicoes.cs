using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using GRepos.Services;

namespace GRepos.Controls;

/// <summary>
/// O quadriculado de contribuições do GitHub (EV05): uma coluna por semana, uma linha
/// por dia, a cor dizendo quanto se fez. Desenhado à mão em vez de 371 bordas — é um
/// cartão que aparece a cada clique na árvore.
///
/// Mostra só as semanas que cabem, sempre terminando em hoje: em janela estreita o ano
/// encolhe pela esquerda, e o texto ao lado do quadriculado nunca fica espremido
/// (<see cref="ReservaEsquerda"/> é o espaço garantido para ele).
/// </summary>
public sealed class GradeContribuicoes : Control
{
    private const double Celula = 9;
    private const double Espaco = 2;
    private const double Passo = Celula + Espaco;
    private const double Rotulos = 26;   // coluna dos dias da semana
    private const double Meses = 13;     // faixa dos meses, em cima
    private const int MinimoDeSemanas = 8;

    public static readonly StyledProperty<IReadOnlyList<DiaContribuicao>?> DiasProperty =
        AvaloniaProperty.Register<GradeContribuicoes, IReadOnlyList<DiaContribuicao>?>(nameof(Dias));

    public static readonly StyledProperty<double> ReservaEsquerdaProperty =
        AvaloniaProperty.Register<GradeContribuicoes, double>(nameof(ReservaEsquerda), 340);

    public IReadOnlyList<DiaContribuicao>? Dias
    {
        get => GetValue(DiasProperty);
        set => SetValue(DiasProperty, value);
    }

    /// <summary>Largura que sobra para quem divide a linha com o quadriculado.</summary>
    public double ReservaEsquerda
    {
        get => GetValue(ReservaEsquerdaProperty);
        set => SetValue(ReservaEsquerdaProperty, value);
    }

    static GradeContribuicoes()
    {
        AffectsMeasure<GradeContribuicoes>(DiasProperty, ReservaEsquerdaProperty);
        AffectsRender<GradeContribuicoes>(DiasProperty);
    }

    public GradeContribuicoes()
    {
        // a paleta vem do tema: trocar de claro para escuro tem que repintar
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    /// <summary>As semanas, de domingo a sábado, como o GitHub agrupa.</summary>
    private List<DiaContribuicao?[]> _semanas = new();
    private int _visiveis;

    private static List<DiaContribuicao?[]> Agrupar(IReadOnlyList<DiaContribuicao> dias)
    {
        var semanas = new List<DiaContribuicao?[]>();
        DiaContribuicao?[]? atual = null;
        DateTime? domingo = null;

        foreach (var d in dias.OrderBy(d => d.Data))
        {
            var inicio = d.Data.AddDays(-(int)d.Data.DayOfWeek);
            if (atual is null || inicio != domingo)
            {
                atual = new DiaContribuicao?[7];
                semanas.Add(atual);
                domingo = inicio;
            }
            atual[(int)d.Data.DayOfWeek] = d;
        }

        return semanas;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _semanas = Dias is { Count: > 0 } dias ? Agrupar(dias) : new();

        var cabem = double.IsInfinity(availableSize.Width)
            ? _semanas.Count
            : (int)Math.Floor((availableSize.Width - ReservaEsquerda - Rotulos + Espaco) / Passo);

        _visiveis = Math.Min(_semanas.Count, Math.Max(0, cabem));
        if (_visiveis < MinimoDeSemanas) _visiveis = 0; // um toco de quadriculado não diz nada

        return _visiveis == 0
            ? new Size(0, 0)
            : new Size(Rotulos + _visiveis * Passo - Espaco, Meses + 7 * Passo - Espaco);
    }

    /// <summary>Nível 0 é a casa vazia; 1 a 4, o verde do tema cada vez mais forte.</summary>
    public static IBrush Cor(int nivel)
    {
        if (nivel <= 0)
            return ResourceBrushConverter.Resolve("BgActive") ?? new SolidColorBrush(Color.Parse("#232A35"));

        var verde = (ResourceBrushConverter.Resolve("Green") as ISolidColorBrush)?.Color
                    ?? Color.Parse("#3FB950");
        var opacidade = nivel switch { 1 => 0.35, 2 => 0.55, 3 => 0.78, _ => 1.0 };
        return new SolidColorBrush(verde, opacidade);
    }

    private static readonly string[] NomesDosMeses =
        { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez" };

    public override void Render(DrawingContext context)
    {
        if (_visiveis == 0) return;

        var texto = ResourceBrushConverter.Resolve("TextFaint") ?? Brushes.Gray;
        var fonte = new Typeface(FontFamily.Default);
        var pincel = Enumerable.Range(0, 5).Select(Cor).ToArray();

        // segunda, quarta e sexta, como no GitHub: rótulo em toda linha fica poluído
        foreach (var (linha, nome) in new[] { (1, "seg"), (3, "qua"), (5, "sex") })
            Escrever(context, nome, fonte, texto, 0, Meses + linha * Passo - 2);

        var primeira = _semanas.Count - _visiveis;
        var mesAnterior = -1;
        var ultimoRotulo = double.NegativeInfinity;

        for (var s = 0; s < _visiveis; s++)
        {
            var semana = _semanas[primeira + s];
            var x = Rotulos + s * Passo;

            // o mês aparece na semana em que ele começa, sem encavalar no rótulo anterior
            var algum = semana.FirstOrDefault(d => d is not null);
            if (algum is not null && algum.Data.Month - 1 != mesAnterior)
            {
                if (mesAnterior != -1 || s == 0)
                {
                    if (x - ultimoRotulo >= 3 * Passo)
                    {
                        Escrever(context, NomesDosMeses[algum.Data.Month - 1], fonte, texto, x, -1);
                        ultimoRotulo = x;
                    }
                }
                mesAnterior = algum.Data.Month - 1;
            }

            for (var d = 0; d < 7; d++)
            {
                var dia = semana[d];
                if (dia is null) continue; // semana corrente: os dias que ainda não chegaram

                context.DrawRectangle(pincel[Math.Clamp(dia.Nivel, 0, 4)], null,
                    new Rect(x, Meses + d * Passo, Celula, Celula), 2, 2);
            }
        }
    }

    private static void Escrever(DrawingContext ctx, string s, Typeface fonte, IBrush cor, double x, double y)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, fonte, 9.5, cor);
        ctx.DrawText(ft, new Point(x, y));
    }

    /// <summary>O dia sob o ponteiro, para a dica: "3 contribuições em 12/09/2026".</summary>
    public DiaContribuicao? DiaEm(Point p)
    {
        if (_visiveis == 0 || p.X < Rotulos || p.Y < Meses) return null;

        var s = (int)((p.X - Rotulos) / Passo);
        var d = (int)((p.Y - Meses) / Passo);
        if (s >= _visiveis || d > 6) return null;

        return _semanas[_semanas.Count - _visiveis + s][d];
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var dia = DiaEm(e.GetPosition(this));
        var dica = dia is null ? null : Dica(dia);
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

    public static string Dica(DiaContribuicao dia) => dia.Quantidade switch
    {
        0 => $"Nenhuma contribuição em {dia.Data:dd/MM/yyyy}",
        1 => $"1 contribuição em {dia.Data:dd/MM/yyyy}",
        _ => $"{dia.Quantidade} contribuições em {dia.Data:dd/MM/yyyy}",
    };
}
