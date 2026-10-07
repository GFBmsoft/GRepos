using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Uma linha da paleta de comandos: um repositório, uma branch ou uma ação.</summary>
public sealed class ItemDaPaleta
{
    /// <summary>"repositório", "branch" ou "ação" — aparece à direita, e também é buscável.</summary>
    public string Tipo { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Detalhe { get; init; } = "";

    /// <summary>Cor do marcador da linha: a do grupo, ou um recurso do tema.</summary>
    public string Cor { get; init; } = "TextDim";

    public Func<Task> Executar { get; init; } = () => Task.CompletedTask;
}

/// <summary>
/// Busca da paleta (Ctrl+P): as letras digitadas precisam aparecer na ordem, não
/// juntas — "fmy" acha "Financeiro MySQL". Acento e maiúscula não contam.
/// </summary>
public static class Paleta
{
    /// <summary>Minúsculas e sem acento: "Emissão" e "emissao" são a mesma busca.</summary>
    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>
    /// Pontos de <paramref name="consulta"/> em <paramref name="texto"/>; null se as letras
    /// não aparecem todas, em ordem. Vale mais o trecho contínuo, o começo do texto e o
    /// começo de palavra (depois de espaço, "/", "-", "_" ou ".").
    /// </summary>
    public static int? Pontuar(string texto, string consulta)
    {
        var t = Normalizar(texto);
        var q = Normalizar(consulta).Replace(" ", "");
        if (q.Length == 0) return 0;

        // o trecho inteiro, junto, ganha de qualquer combinação espalhada
        var direto = t.IndexOf(q, StringComparison.Ordinal);
        if (direto >= 0)
            return 1000 - Math.Min(direto, 200) + (direto == 0 || Separador(t[direto - 1]) ? 200 : 0) - Math.Min(t.Length, 100);

        var pontos = 0;
        var ti = 0;
        var anterior = -2;
        foreach (var letra in q)
        {
            var achou = t.IndexOf(letra, ti);
            if (achou < 0) return null;

            pontos += 10;
            if (achou == anterior + 1) pontos += 15;
            if (achou == 0 || Separador(t[achou - 1])) pontos += 20;

            anterior = achou;
            ti = achou + 1;
        }
        return pontos - Math.Min(t.Length, 100);
    }

    private static bool Separador(char c) => c is ' ' or '/' or '-' or '_' or '.' or '\\';

    /// <summary>
    /// Os itens que casam, do melhor para o pior. Sem consulta, todos na ordem em que
    /// vieram. A consulta olha o título; o detalhe e o tipo valem menos.
    /// </summary>
    public static List<ItemDaPaleta> Filtrar(IEnumerable<ItemDaPaleta> itens, string consulta, int limite = 50)
    {
        if (string.IsNullOrWhiteSpace(consulta)) return itens.Take(limite).ToList();

        return itens
            .Select((item, ordem) =>
            {
                var titulo = Pontuar(item.Titulo, consulta);
                var resto = Pontuar(item.Tipo + " " + item.Detalhe, consulta);
                var pontos = titulo is not null ? titulo.Value + 500 : resto;
                return (item, pontos, ordem);
            })
            .Where(x => x.pontos is not null)
            .OrderByDescending(x => x.pontos)
            .ThenBy(x => x.ordem)
            .Take(limite)
            .Select(x => x.item)
            .ToList();
    }
}
