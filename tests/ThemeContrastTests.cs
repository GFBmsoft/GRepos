using System;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Contraste medido pela fórmula da WCAG. É o que garante que o tema claro continue
/// legível: as cores de estado nasceram para fundo escuro e somem no branco.
/// </summary>
public class ThemeContrastTests
{
    private static readonly ThemeVariant[] Temas = { ThemeVariant.Light, ThemeVariant.Dark };

    private static Color Cor(string chave, ThemeVariant tema)
    {
        var app = Application.Current;
        Assert.NotNull(app);
        Assert.True(app!.TryGetResource(chave, tema, out var res), $"recurso ausente: {chave}");
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(res);
        return brush.Color;
    }

    private static double Luminancia(Color c)
    {
        static double Canal(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);
    }

    private static double Contraste(Color a, Color b)
    {
        var la = Luminancia(a);
        var lb = Luminancia(b);
        var (claro, escuro) = la > lb ? (la, lb) : (lb, la);
        return (claro + 0.05) / (escuro + 0.05);
    }

    private static void Exige(string frente, string fundo, ThemeVariant tema, double minimo)
    {
        var razao = Contraste(Cor(frente, tema), Cor(fundo, tema));
        Assert.True(razao >= minimo,
            $"{tema}: {frente} sobre {fundo} = {razao:F2}:1, mínimo {minimo:F1}:1");
    }

    [AvaloniaFact]
    public void Texto_principal_e_secundario_sao_legiveis()
    {
        foreach (var tema in Temas)
        {
            Exige("Text", "Bg", tema, 7.0);         // AAA para o texto normal
            Exige("Text", "BgPanel", tema, 7.0);
            Exige("TextDim", "BgPanel", tema, 4.5); // AA
            Exige("TextFaint", "BgPanel", tema, 3.5);
        }
    }

    [AvaloniaFact]
    public void Cores_de_estado_aparecem_sobre_o_painel()
    {
        foreach (var tema in Temas)
        foreach (var cor in new[] { "Accent", "Green", "Red", "Orange", "Yellow", "Purple" })
            Exige(cor, "BgPanel", tema, 4.0);
    }

    [AvaloniaFact]
    public void Raias_do_grafo_aparecem_sobre_o_fundo()
    {
        foreach (var tema in Temas)
            for (var i = 0; i < 8; i++)
                Exige($"Lane{i}", "BgPanel", tema, 3.0);
    }

    [AvaloniaFact]
    public void Texto_sobre_o_destaque_e_legivel()
    {
        foreach (var tema in Temas)
            Exige("OnAccent", "Accent", tema, 4.0);
    }

    [AvaloniaFact]
    public void Fundos_de_diff_nao_apagam_o_codigo()
    {
        foreach (var tema in Temas)
        {
            Exige("Text", "AddBg", tema, 6.0);
            Exige("Text", "DelBg", tema, 6.0);
            Exige("TextFaint", "GutterBg", tema, 3.0);
        }
    }

    [AvaloniaFact]
    public void Selecao_e_borda_se_distinguem_do_fundo()
    {
        foreach (var tema in Temas)
        {
            Assert.True(Contraste(Cor("BgActive", tema), Cor("BgPanel", tema)) >= 1.10,
                $"{tema}: linha selecionada não se distingue do painel");
            Assert.True(Contraste(Cor("Border", tema), Cor("BgPanel", tema)) >= 1.25,
                $"{tema}: borda não se distingue do painel");
        }
    }
}
