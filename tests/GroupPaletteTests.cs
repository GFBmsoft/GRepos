using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class GroupPaletteTests
{
    [Fact]
    public void Sugere_a_primeira_cor_ainda_nao_usada()
    {
        var usadas = GroupPalette.Cores.Take(3);
        Assert.Equal(GroupPalette.Cores[3], GroupPalette.ProximaLivre(usadas));
    }

    [Fact]
    public void Com_a_paleta_toda_em_uso_volta_a_repetir()
    {
        var proxima = GroupPalette.ProximaLivre(GroupPalette.Cores);
        Assert.Contains(proxima, GroupPalette.Cores);
    }

    [Fact]
    public void Ignora_diferenca_de_caixa_ao_procurar_cor_livre()
    {
        var usadas = GroupPalette.Cores.Take(2).Select(c => c.ToLowerInvariant());
        Assert.Equal(GroupPalette.Cores[2], GroupPalette.ProximaLivre(usadas));
    }

    [Theory]
    [InlineData("#2F7BE8", "#2F7BE8")]
    [InlineData("2f7be8", "#2F7BE8")]   // aceita sem cerquilha e em minúsculas
    [InlineData("#abc", "#AABBCC")]     // forma curta vira completa
    [InlineData(" #2f7be8 ", "#2F7BE8")]
    [InlineData("", null)]
    [InlineData("vermelho", null)]
    [InlineData("#12345", null)]
    public void Normaliza_o_que_o_usuario_digita(string entrada, string? esperado)
    {
        Assert.Equal(esperado, GroupPalette.Normalizar(entrada));
    }

    [Fact]
    public void Toda_cor_da_paleta_e_valida()
    {
        foreach (var c in GroupPalette.Cores)
        {
            Assert.Equal(c, GroupPalette.Normalizar(c));
            Color.Parse(c); // não pode lançar
        }
    }

    /// <summary>O ponto do grupo aparece nos dois temas: precisa se destacar em ambos.</summary>
    [AvaloniaFact]
    public void Cores_da_paleta_aparecem_nos_dois_temas()
    {
        foreach (var tema in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var fundo = Fundo(tema);
            foreach (var c in GroupPalette.Cores)
            {
                var razao = Contraste(Color.Parse(c), fundo);
                Assert.True(razao >= 2.0, $"{tema}: cor de grupo {c} = {razao:F2}:1 sobre o painel");
            }
        }
    }

    private static Color Fundo(ThemeVariant tema)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource("BgPanel", tema, out var res));
        return ((ISolidColorBrush)res!).Color;
    }

    private static double Contraste(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Canal(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Canal(c.R) + 0.7152 * Canal(c.G) + 0.0722 * Canal(c.B);
        }

        var la = Lum(a);
        var lb = Lum(b);
        var (claro, escuro) = la > lb ? (la, lb) : (lb, la);
        return (claro + 0.05) / (escuro + 0.05);
    }
}
