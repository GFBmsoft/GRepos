using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GRepos.Services;

/// <summary>
/// O Markdown fora da tela: como HTML, para colar formatado num e-mail, no Word ou no
/// Teams, e como texto limpo, para onde a formatação não chega. Sai dos mesmos blocos
/// que a tela desenha — o que se cola é o que se viu.
/// </summary>
public static class MarkdownExport
{
    /// <summary>
    /// Só o que o HTML exige. O WebUtility troca também cada acento por um código
    /// ("vers&#227;o"), o que é válido mas deixa o HTML ilegível para quem for conferir.
    /// </summary>
    public static string Escapar(string texto) => texto
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");


    private static string Html(string texto) => Escapar(texto);
    private static string TrechosHtml(IEnumerable<TrechoMd> trechos)
    {
        var sb = new StringBuilder();
        foreach (var t in trechos)
        {
            var conteudo = Html(t.Texto);
            if (t.Codigo) conteudo = $"<code>{conteudo}</code>";
            if (t.Negrito) conteudo = $"<b>{conteudo}</b>";
            if (t.Italico) conteudo = $"<i>{conteudo}</i>";
            if (t.Link.Length > 0) conteudo = $"<a href=\"{Html(t.Link)}\">{conteudo}</a>";
            sb.Append(conteudo);
        }
        return sb.ToString();
    }

    /// <summary>
    /// O Markdown como um fragmento de HTML. Itens seguidos viram uma lista só, e a
    /// profundidade de cada um vira lista dentro de lista.
    /// </summary>
    public static string ParaHtml(string markdown)
    {
        var sb = new StringBuilder();
        var listas = new Stack<string>(); // "ul" ou "ol" de cada nível aberto

        void FecharListas(int ate = 0)
        {
            while (listas.Count > ate) sb.Append($"</{listas.Pop()}>");
        }

        foreach (var b in MarkdownParser.Blocos(markdown))
        {
            if (b.Tipo != BlocoMdTipo.Item) FecharListas();

            switch (b.Tipo)
            {
                case BlocoMdTipo.Titulo:
                    var n = Math.Clamp(b.Nivel, 1, 6);
                    sb.Append($"<h{n}>{TrechosHtml(b.Trechos)}</h{n}>");
                    break;

                case BlocoMdTipo.Item:
                    var tipo = b.Marcador.Length > 0 && char.IsDigit(b.Marcador[0]) ? "ol" : "ul";
                    var nivel = Math.Max(0, b.Nivel) + 1;
                    FecharListas(nivel);

                    // mesmo nível, mas trocou de marcador (de pontos para números): outra lista
                    if (listas.Count == nivel && listas.Peek() != tipo) FecharListas(nivel - 1);
                    while (listas.Count < nivel)
                    {
                        sb.Append($"<{tipo}>");
                        listas.Push(tipo);
                    }
                    sb.Append($"<li>{TrechosHtml(b.Trechos)}</li>");
                    break;

                case BlocoMdTipo.Codigo:
                    sb.Append($"<pre><code>{Html(b.Texto)}</code></pre>");
                    break;

                case BlocoMdTipo.Citacao:
                    sb.Append($"<blockquote>{TrechosHtml(b.Trechos)}</blockquote>");
                    break;

                case BlocoMdTipo.Regua:
                    sb.Append("<hr>");
                    break;

                case BlocoMdTipo.Tabela:
                    sb.Append("<table border=\"1\" cellspacing=\"0\" cellpadding=\"4\">");
                    foreach (var linha in b.Linhas)
                    {
                        var celula = linha.Cabecalho ? "th" : "td";
                        sb.Append("<tr>");
                        foreach (var c in linha.Celulas) sb.Append($"<{celula}>{TrechosHtml(c)}</{celula}>");
                        sb.Append("</tr>");
                    }
                    sb.Append("</table>");
                    break;

                default:
                    if (b.Trechos.Count > 0) sb.Append($"<p>{TrechosHtml(b.Trechos)}</p>");
                    break;
            }
        }

        FecharListas();
        return sb.ToString();
    }

    private static string TrechosTexto(IEnumerable<TrechoMd> trechos)
    {
        var sb = new StringBuilder();
        foreach (var t in trechos)
        {
            sb.Append(t.Texto);

            // o endereço vai junto quando não é o próprio texto: sem formatação ele sumiria
            if (t.Link.Length > 0 && !string.Equals(t.Link, t.Texto, StringComparison.OrdinalIgnoreCase))
                sb.Append($" ({t.Link})");
        }
        return sb.ToString();
    }

    /// <summary>
    /// O texto como se lê na tela, sem os sinais do Markdown: sem "#" nos títulos, sem
    /// asteriscos, com "•" nos itens e o endereço dos links entre parênteses.
    /// </summary>
    public static string ParaTexto(string markdown)
    {
        var linhas = new List<string>();
        BlocoMdTipo? anterior = null;

        foreach (var b in MarkdownParser.Blocos(markdown))
        {
            // itens de uma mesma lista ficam juntos; entre blocos diferentes vai uma linha vazia
            if (anterior is not null && !(anterior == BlocoMdTipo.Item && b.Tipo == BlocoMdTipo.Item))
                linhas.Add("");

            switch (b.Tipo)
            {
                case BlocoMdTipo.Item:
                    var marcador = b.Marcador.Length > 0 ? b.Marcador : "•";
                    linhas.Add(new string(' ', Math.Max(0, b.Nivel) * 2) + marcador + " " + TrechosTexto(b.Trechos));
                    break;
                case BlocoMdTipo.Codigo:
                    linhas.Add(b.Texto.TrimEnd('\r', '\n'));
                    break;
                case BlocoMdTipo.Regua:
                    linhas.Add("────────");
                    break;
                case BlocoMdTipo.Tabela:
                    foreach (var linha in b.Linhas)
                        linhas.Add(string.Join("\t", linha.Celulas.Select(TrechosTexto)));
                    break;
                default:
                    linhas.Add(TrechosTexto(b.Trechos));
                    break;
            }
            anterior = b.Tipo;
        }

        return string.Join(Environment.NewLine, linhas).Trim();
    }

    /// <summary>
    /// O formato "HTML Format" da área de transferência do Windows: o HTML precedido de um
    /// cabeçalho que diz, em bytes, onde o documento e o trecho começam e terminam. É o
    /// que o Word, o Outlook e os navegadores leem ao colar com formatação.
    /// </summary>
    public static byte[] ParaAreaDeTransferencia(string fragmento)
    {
        const string antes = "<html><body><!--StartFragment-->";
        const string depois = "<!--EndFragment--></body></html>";

        // os números têm largura fixa para o tamanho do cabeçalho não depender deles
        const string modelo =
            "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";

        var utf8 = new UTF8Encoding(false);
        var cabecalho = utf8.GetByteCount(string.Format(modelo, 0, 0, 0, 0));
        var inicioDoFragmento = cabecalho + utf8.GetByteCount(antes);
        var fimDoFragmento = inicioDoFragmento + utf8.GetByteCount(fragmento);
        var fim = fimDoFragmento + utf8.GetByteCount(depois);

        return utf8.GetBytes(
            string.Format(modelo, cabecalho, fim, inicioDoFragmento, fimDoFragmento) + antes + fragmento + depois);
    }
}
