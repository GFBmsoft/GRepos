using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using GRepos.Services;

namespace GRepos.Controls;

/// <summary>
/// A amostra de cor que abre um painel com a paleta ao ser clicada — o mesmo desenho do
/// WinDock. O campo de texto ao lado continua existindo: é por ele que se cola um tom
/// exato, e o painel é o caminho de quem quer só escolher.
///
/// As cores são as dos grupos do GRepos (tons médios, que aparecem nos dois temas), as
/// de destaque do app e as 48 de destaque do Windows 11, na ordem da tela dele.
/// </summary>
public sealed class SeletorDeCor : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    public static readonly StyledProperty<string> CorProperty =
        AvaloniaProperty.Register<SeletorDeCor, string>(nameof(Cor), "", defaultBindingMode: BindingMode.TwoWay);

    /// <summary>A cor em hex ("#2F7BE8"), a mesma string que o campo de texto ao lado edita.</summary>
    public string Cor
    {
        get => GetValue(CorProperty);
        set => SetValue(CorProperty, value);
    }

    /// <summary>Uma cor foi escolhida no painel (não dispara quando a cor muda por fora).</summary>
    public event Action<string>? Escolhida;

    /// <summary>Destaques do próprio app, que já eram oferecidos nas Preferências.</summary>
    public static readonly string[] Destaques =
        { "#4F8CFF", "#3FB950", "#F0883E", "#D2A8FF", "#E3B341", "#56D4BC" };

    private static readonly string[] Windows11 =
    {
        "#FFB900", "#FF8C00", "#F7630C", "#CA5010", "#DA3B01", "#EF6950", "#D13438", "#FF4343",
        "#E74856", "#E81123", "#EA005E", "#C30052", "#E3008C", "#BF0077", "#C239B3", "#9A0089",
        "#0078D7", "#0063B1", "#8E8CD8", "#6B69D6", "#8764B8", "#744DA9", "#B146C2", "#881798",
        "#0099BC", "#2D7D9A", "#00B7C3", "#038387", "#00B294", "#018574", "#00CC6A", "#10893E",
        "#7A7574", "#5D5A58", "#68768A", "#515C6B", "#567C73", "#486860", "#498205", "#107C10",
        "#767676", "#4C4A48", "#69797E", "#4A5459", "#647C64", "#525E54", "#847545", "#7E735F",
    };

    public const int Colunas = 8;

    /// <summary>
    /// A paleta do painel, sem repetição: as cores dos grupos e os destaques do app
    /// primeiro (as mais usadas aqui), depois as do Windows 11.
    /// </summary>
    public static IReadOnlyList<string> Paleta { get; } = GroupPalette.Cores
        .Concat(Destaques)
        .Concat(Windows11)
        .Select(c => c.ToUpperInvariant())
        .Distinct()
        .ToList();

    private readonly Border _amostra;
    private readonly UniformGrid _grade;
    private readonly Dictionary<string, Border> _molduras = new(StringComparer.OrdinalIgnoreCase);
    private readonly Flyout _painel;

    public SeletorDeCor()
    {
        Width = 30;
        Height = 26;
        MinWidth = 0;
        Padding = new Thickness(3);
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(this, "Escolher numa paleta");

        _amostra = new Border { CornerRadius = new CornerRadius(3) };
        Content = _amostra;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        _grade = new UniformGrid { Columns = Colunas };
        foreach (var hex in Paleta) _grade.Children.Add(Amostra(hex));

        var legenda = new TextBlock
        {
            Text = "Clique numa cor. Para um tom exato, digite o código no campo ao lado.",
            FontSize = 11,
            Width = Colunas * 30 - 6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 0, 8),
            Classes = { "faint" },
        };

        _painel = new Flyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Content = new StackPanel { Margin = new Thickness(2), Children = { legenda, _grade } },
        };
        _painel.Opening += (_, _) => MarcarEscolhida();
        Flyout = _painel;

        Pintar();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CorProperty) Pintar();
    }

    private Control Amostra(string hex)
    {
        var cor = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse(hex)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
        };

        // a moldura de fora acende na cor escolhida agora e sob o mouse
        var moldura = new Border
        {
            Padding = new Thickness(2), Margin = new Thickness(1), CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent, // sem fundo a borda transparente não recebe o clique
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = cor,
        };
        ToolTip.SetTip(moldura, hex);
        _molduras[hex] = moldura;

        moldura.PointerEntered += (_, _) =>
        {
            if (!EhAtual(hex)) moldura.BorderBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80));
        };
        moldura.PointerExited += (_, _) =>
        {
            if (!EhAtual(hex)) moldura.BorderBrush = Brushes.Transparent;
        };
        moldura.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Escolher(hex);
        };
        return moldura;
    }

    /// <summary>Escolhe a cor como um clique no painel faria; é o que os testes usam.</summary>
    public void Escolher(string hex)
    {
        Cor = hex;
        _painel.Hide();
        Escolhida?.Invoke(hex);
    }

    private bool EhAtual(string hex) =>
        string.Equals(GroupPalette.Normalizar(Cor), hex, StringComparison.OrdinalIgnoreCase);

    private void MarcarEscolhida()
    {
        foreach (var (hex, moldura) in _molduras)
        {
            moldura.ClearValue(Border.BorderBrushProperty);
            if (EhAtual(hex)) moldura.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Accent"));
            else moldura.BorderBrush = Brushes.Transparent;
        }
    }

    private void Pintar()
    {
        // ainda digitando no campo ao lado: a amostra fica vazia em vez de quebrar
        _amostra.Background = GroupPalette.Normalizar(Cor) is { } hex
            ? new SolidColorBrush(Color.Parse(hex))
            : Brushes.Transparent;
    }
}
