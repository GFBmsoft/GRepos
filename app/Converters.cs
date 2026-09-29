using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GRepos;

public static class Converters
{
    /// <summary>"#RRGGBB" -> Brush, para cores que não dependem do tema.</summary>
    public static readonly IValueConverter StringToBrush =
        new FuncValueConverter<string?, IBrush?>(s =>
            string.IsNullOrWhiteSpace(s) ? null : new SolidColorBrush(Color.Parse(s)));

    /// <summary>
    /// Nome de recurso ("Green", "Orange", "Lane2") -> Brush do tema atual. Os ViewModels
    /// devolvem o nome, não o hexadecimal: no tema claro as mesmas cores são outras.
    /// </summary>
    public static readonly IValueConverter ResourceBrush = new ResourceBrushConverter();

    /// <summary>Cor do grupo bem diluída — preenchimento da pílula.</summary>
    public static readonly IValueConverter SoftBrush = Alpha(0x24);

    /// <summary>Cor do grupo meio diluída — contorno da pílula.</summary>
    public static readonly IValueConverter EdgeBrush = Alpha(0x66);

    private static IValueConverter Alpha(byte a) =>
        new FuncValueConverter<string?, IBrush?>(s =>
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var c = Color.Parse(s);
            return new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        });

    /// <summary>Texto vazio some da interface.</summary>
    public static readonly IValueConverter NotEmpty =
        new FuncValueConverter<string?, bool>(s => !string.IsNullOrWhiteSpace(s));
}

public sealed class ResourceBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0) return null;
        if (key[0] == '#') return new SolidColorBrush(Color.Parse(key));
        return Resolve(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;

    public static IBrush? Resolve(string key)
    {
        var app = Application.Current;
        if (app is null) return null;
        // busca na aplicação inteira (inclui os recursos declarados nos estilos)
        return app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is IBrush brush
            ? brush
            : null;
    }
}

/// <summary>Converte um índice de aba em bool (para o TabControl trabalhar com bindings simples).</summary>
public sealed class IndexEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && parameter is string p && int.TryParse(p, out var n) && i == n;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string p && int.TryParse(p, out var n) ? n : Avalonia.Data.BindingOperations.DoNothing;
}
