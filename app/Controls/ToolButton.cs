using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Media;

namespace GRepos.Controls;

/// <summary>
/// Botão da barra de ferramentas: ícone desenhado, legenda embaixo e um contador
/// opcional no canto (commits à frente/atrás, stashes guardados).
/// </summary>
[PseudoClasses(":hasbadge")]
public class ToolButton : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<ToolButton, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ToolButton, string>(nameof(Label), "");

    public static readonly StyledProperty<string?> BadgeProperty =
        AvaloniaProperty.Register<ToolButton, string?>(nameof(Badge));

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Texto do contador; vazio ou nulo esconde o selo.</summary>
    public string? Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ToolButton);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BadgeProperty)
            PseudoClasses.Set(":hasbadge", !string.IsNullOrEmpty(Badge));
    }
}
