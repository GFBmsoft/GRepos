using System;
using System.Collections.Generic;
using System.Linq;

namespace GRepos.Services;

/// <summary>Uma casa do display de LEDs: coluna e linha, contadas em pontos.</summary>
public readonly record struct Ponto(int Col, int Lin);

/// <summary>
/// A fonte de pontos 5×7 dos quadros retrô, só maiúsculas, no traço dos displays de LCD. Ao
/// contrário da do github-cool-readme, que só tem ASCII, esta tem os acentos e a cedilha do
/// português: o acento ocupa as duas linhas acima da letra e a cedilha as duas de baixo, e
/// quem posiciona o texto deixa esse espaço.
/// </summary>
public static class FonteDePontos
{
    /// <summary>Altura da letra, sem acento nem cedilha.</summary>
    public const int Altura = 7;

    private const int Espaco = 1; // entre uma letra e outra

    private static readonly Dictionary<char, string[]> Glifos = new()
    {
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###." },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['J'] = new[] { "....#", "....#", "....#", "....#", "#...#", "#...#", ".###." },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".###.", "#...#", "#....", ".###.", "....#", "#...#", ".###." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." },
        ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
        ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
        ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
        ['3'] = new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." },
        ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
        ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
        [' '] = new[] { "...", "...", "...", "...", "...", "...", "..." },
        ['.'] = new[] { "..", "..", "..", "..", "..", "##", "##" },
        [','] = new[] { "..", "..", "..", "..", ".#", ".#", "#." },
        [':'] = new[] { "..", "##", "##", "..", "##", "##", ".." },
        [';'] = new[] { "..", "##", "##", "..", ".#", ".#", "#." },
        ['·'] = new[] { "..", "..", "##", "##", "..", "..", ".." },
        ['\''] = new[] { "#", "#", ".", ".", ".", ".", "." },
        ['"'] = new[] { "#.#", "#.#", "...", "...", "...", "...", "..." },
        ['!'] = new[] { "#", "#", "#", "#", "#", ".", "#" },
        ['?'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." },
        ['('] = new[] { "..#", ".#.", "#..", "#..", "#..", ".#.", "..#" },
        [')'] = new[] { "#..", ".#.", "..#", "..#", "..#", ".#.", "#.." },
        ['%'] = new[] { "##...", "##..#", "...#.", "..#..", ".#...", "#..##", "...##" },
        ['/'] = new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." },
        ['-'] = new[] { "....", "....", "....", "####", "....", "....", "...." },
        ['_'] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "#####" },
        ['+'] = new[] { ".....", "..#..", "..#..", "#####", "..#..", "..#..", "....." },
        ['='] = new[] { "....", "....", "####", "....", "####", "....", "...." },
        ['#'] = new[] { ".#.#.", ".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".#.#." },
        ['*'] = new[] { "..#..", "#.#.#", ".###.", "..#..", ".###.", "#.#.#", "..#.." },
        ['&'] = new[] { ".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#" },
        ['@'] = new[] { ".###.", "#...#", "#.###", "#.#.#", "#.###", "#....", ".####" },
    };

    // os sinais em cima, nas duas linhas acima da letra
    private static readonly string[] Agudo = { "...#.", "..#.." };
    private static readonly string[] Grave = { ".#...", "..#.." };
    private static readonly string[] Circunflexo = { "..#..", ".#.#." };
    private static readonly string[] Til = { ".##.#", "#..#." };
    private static readonly string[] Trema = { ".....", ".#.#." };

    /// <summary>A letra acentuada: a base, o sinal de cima e, na cedilha, o de baixo.</summary>
    private static readonly Dictionary<char, (char Base, string[]? Cima, string[]? Baixo)> Acentuadas = new()
    {
        ['Á'] = ('A', Agudo, null), ['À'] = ('A', Grave, null), ['Â'] = ('A', Circunflexo, null),
        ['Ã'] = ('A', Til, null), ['Ä'] = ('A', Trema, null),
        ['É'] = ('E', Agudo, null), ['È'] = ('E', Grave, null), ['Ê'] = ('E', Circunflexo, null),
        ['Í'] = ('I', Agudo, null), ['Ì'] = ('I', Grave, null), ['Î'] = ('I', Circunflexo, null),
        ['Ó'] = ('O', Agudo, null), ['Ò'] = ('O', Grave, null), ['Ô'] = ('O', Circunflexo, null),
        ['Õ'] = ('O', Til, null), ['Ö'] = ('O', Trema, null),
        ['Ú'] = ('U', Agudo, null), ['Ù'] = ('U', Grave, null), ['Û'] = ('U', Circunflexo, null),
        ['Ü'] = ('U', Trema, null),
        ['Ñ'] = ('N', Til, null),
        ['Ç'] = ('C', null, new[] { "..#..", ".##.." }),
    };

    private static (string[] Glifo, string[]? Cima, string[]? Baixo) Letra(char c)
    {
        var m = char.ToUpperInvariant(c);
        if (Glifos.TryGetValue(m, out var g)) return (g, null, null);
        if (Acentuadas.TryGetValue(m, out var a)) return (Glifos[a.Base], a.Cima, a.Baixo);
        return (Glifos['?'], null, null);
    }

    /// <summary>Largura do texto em pontos, na escala 1.</summary>
    public static int Largura(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return 0;
        return texto.Sum(c => Letra(c).Glifo[0].Length) + (texto.Length - 1) * Espaco;
    }

    /// <summary>
    /// As casas acesas do texto com o canto de cima da primeira letra em (col, lin). Na escala 2,
    /// cada ponto da fonte vira um quadrado de 2×2 casas.
    /// </summary>
    public static List<Ponto> Celulas(string texto, int col, int lin, int escala = 1) =>
        CelulasPorLetra(texto, col, lin, escala).SelectMany(l => l).ToList();

    /// <summary>As casas de cada letra separadas: o efeito de máquina de escrever acende uma por vez.</summary>
    public static List<List<Ponto>> CelulasPorLetra(string texto, int col, int lin, int escala = 1)
    {
        var letras = new List<List<Ponto>>();
        var x = col;
        foreach (var c in texto ?? "")
        {
            var (glifo, cima, baixo) = Letra(c);
            var casas = new List<Ponto>();
            Pintar(casas, glifo, x, lin, escala);
            if (cima is not null) Pintar(casas, cima, x, lin - 2 * escala, escala);
            if (baixo is not null) Pintar(casas, baixo, x, lin + Altura * escala, escala);
            letras.Add(casas);
            x += (glifo[0].Length + Espaco) * escala;
        }
        return letras;
    }

    private static void Pintar(List<Ponto> casas, string[] desenho, int col, int lin, int escala)
    {
        for (var y = 0; y < desenho.Length; y++)
            for (var x = 0; x < desenho[y].Length; x++)
            {
                if (desenho[y][x] != '#') continue;
                for (var i = 0; i < escala; i++)
                    for (var j = 0; j < escala; j++)
                        casas.Add(new Ponto(col + x * escala + i, lin + y * escala + j));
            }
    }

    /// <summary>O texto cortado até caber em <paramref name="colunas"/> pontos, na escala 1.</summary>
    public static string Caber(string texto, int colunas)
    {
        var s = texto ?? "";
        while (s.Length > 1 && Largura(s) > colunas) s = s[..^1];
        return s;
    }
}
