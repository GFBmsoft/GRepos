using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GRepos.Services;

/// <summary>
/// O HTML que aparece em README de verdade — &lt;p&gt;, &lt;ul&gt;&lt;li&gt;, &lt;br&gt;,
/// &lt;b&gt;, &lt;a&gt;, &lt;img&gt; — convertido para o markdown equivalente antes do
/// parser. Não é um parser de HTML: tag desconhecida some e o texto dela fica.
/// Bloco de código cercado por ``` passa intacto.
/// </summary>
public static class HtmlEmbutido
{
    private static readonly Regex Comentario = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)([^>]*?)(/?)>", RegexOptions.Compiled);
    private static readonly Regex Atributo = new(@"([a-zA-Z-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.Compiled);

    public static string ParaMarkdown(string texto)
    {
        if (string.IsNullOrEmpty(texto) || texto.IndexOf('<') < 0 && texto.IndexOf('&') < 0) return texto;

        var saida = new StringBuilder();
        var fora = new StringBuilder();
        var dentroDeCodigo = false;

        foreach (var linha in texto.Replace("\r\n", "\n").Split('\n'))
        {
            if (linha.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!dentroDeCodigo) { saida.Append(Converter(fora.ToString())); fora.Clear(); }
                dentroDeCodigo = !dentroDeCodigo;
                saida.Append(linha).Append('\n');
                continue;
            }

            if (dentroDeCodigo) saida.Append(linha).Append('\n');
            else fora.Append(linha).Append('\n');
        }

        saida.Append(Converter(fora.ToString()));
        return saida.ToString().TrimEnd('\n');
    }

    private static string Converter(string trecho)
    {
        if (trecho.Length == 0) return trecho;

        trecho = Comentario.Replace(trecho, "");

        // `List<string>` em código inline não é HTML: guarda e devolve no fim
        var codigos = new System.Collections.Generic.List<string>();
        trecho = CodigoInline.Replace(trecho, m =>
        {
            codigos.Add(m.Value);
            return "\u0001" + (codigos.Count - 1) + "\u0001";
        });

        // listas aninhadas: a profundidade vira recuo, a numerada guarda o contador
        var listas = new System.Collections.Generic.Stack<int>(); // -1 = marcadores, >=0 = numerada
        var link = new System.Collections.Generic.Stack<string>();

        var html = Tag.Replace(trecho, m =>
        {
            var fecha = m.Groups[1].Value == "/";
            var nome = m.Groups[2].Value.ToLowerInvariant();
            var atributos = m.Groups[3].Value;

            switch (nome)
            {
                case "p":
                case "div":
                case "table":
                case "details":
                    return "\n\n";

                case "br":
                case "hr" when fecha:
                    return "\n";

                case "hr":
                    return "\n\n---\n\n";

                case "ul":
                case "ol":
                    if (fecha) { if (listas.Count > 0) listas.Pop(); }
                    else listas.Push(nome == "ol" ? 0 : -1);
                    return "\n";

                case "li":
                    if (fecha) return "\n";
                    var recuo = new string(' ', Math.Max(0, listas.Count - 1) * 2);
                    if (listas.Count > 0 && listas.Peek() >= 0)
                    {
                        var n = listas.Pop() + 1;
                        listas.Push(n);
                        return "\n" + recuo + n + ". ";
                    }
                    return "\n" + recuo + "- ";

                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    return fecha ? "\n\n" : "\n\n" + new string('#', nome[1] - '0') + " ";

                case "tr":
                    return fecha ? "\n" : "";

                case "td":
                case "th":
                    return fecha ? " " : "";

                case "summary":
                    return fecha ? "**\n\n" : "\n\n**";

                case "b":
                case "strong":
                    return "**";

                case "i":
                case "em":
                    return "*";

                case "code":
                    return "`";

                case "a":
                    if (fecha) return link.Count > 0 ? "](" + link.Pop() + ")" : "";
                    var href = Valor(atributos, "href");
                    if (href.Length == 0) return "";
                    link.Push(href);
                    return "[";

                case "img":
                    var alt = Valor(atributos, "alt");
                    var src = Valor(atributos, "src");
                    return src.Length == 0 ? alt : $"![{alt}]({src})";

                default:
                    // span, center, sub, td... o texto fica, a tag sai. O que não é
                    // tag HTML conhecida ("a <b> c" num texto, <T> genérico) fica como está
                    return Descartaveis.Contains(nome) ? "" : m.Value;
            }
        });

        // linhas só com o recuo que havia antes das tags viram linha em branco de verdade
        var limpo = Regex.Replace(html, @"[ \t]+\n", "\n");
        limpo = Regex.Replace(limpo, @"\n{3,}", "\n\n");
        limpo = WebUtility.HtmlDecode(limpo);

        return Regex.Replace(limpo, "\u0001(\\d+)\u0001", m => codigos[int.Parse(m.Groups[1].Value)]);
    }

    private static readonly Regex CodigoInline = new(@"`[^`\n]+`", RegexOptions.Compiled);

    private static readonly System.Collections.Generic.HashSet<string> Descartaveis = new()
    {
        "span", "center", "font", "sub", "sup", "small", "big", "u", "s", "del", "ins", "mark",
        "kbd", "section", "article", "header", "footer", "nav", "picture", "source",
        "thead", "tbody", "tfoot", "caption", "dl", "dt", "dd", "blockquote", "pre",
    };

    private static string Valor(string atributos, string nome)
    {
        foreach (Match a in Atributo.Matches(atributos))
            if (string.Equals(a.Groups[1].Value, nome, StringComparison.OrdinalIgnoreCase))
                return a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
        return "";
    }
}
