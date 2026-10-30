using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace GRepos.Controls;

/// <summary>
/// Botão da barra de ferramentas: pílula com o glifo, a legenda ao lado e um contador
/// opcional no canto (commits à frente/atrás, stashes guardados). A cor vem da classe
/// (azul, verde...), como nos outros botões.
/// </summary>
[PseudoClasses(":hasbadge")]
public class ToolButton : Button
{
    public static readonly StyledProperty<string> GlifoProperty =
        AvaloniaProperty.Register<ToolButton, string>(nameof(Glifo), "");

    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ToolButton, string>(nameof(Label), "");

    public static readonly StyledProperty<string?> BadgeProperty =
        AvaloniaProperty.Register<ToolButton, string?>(nameof(Badge));

    public string Glifo
    {
        get => GetValue(GlifoProperty);
        set => SetValue(GlifoProperty, value);
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
