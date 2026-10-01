using System;
using System.Collections.Generic;
using System.Text;

namespace GRepos.Services;

public enum BlocoMdTipo { Paragrafo, Titulo, Item, Codigo, Citacao, Regua, Tabela }

/// <summary>Uma linha da tabela; as células já vêm com a formatação inline resolvida.</summary>
public sealed class LinhaTabelaMd
{
    public bool Cabecalho { get; init; }
    public IReadOnlyList<IReadOnlyList<TrechoMd>> Celulas { get; init; } =
        Array.Empty<IReadOnlyList<TrechoMd>>();
}

/// <summary>Um pedaço de texto com formatação inline.</summary>
public sealed class TrechoMd
{
    public string Texto { get; init; } = "";
    public bool Negrito { get; init; }
    public bool Italico { get; init; }
    public bool Codigo { get; init; }

    /// <summary>Destino do link; vazio quando o trecho não é link.</summary>
    public string Link { get; init; } = "";
}

public sealed class BlocoMd
{
    public BlocoMdTipo Tipo { get; init; } = BlocoMdTipo.Paragrafo;

    /// <summary>Nível do título (1 a 6) ou profundidade do item de lista.</summary>
    public int Nivel { get; init; }

    /// <summary>Marcador do item: "•" na lista simples, "1." na numerada.</summary>
    public string Marcador { get; init; } = "";

    /// <summary>Texto cru — é o que vale em bloco de código, onde nada é interpretado.</summary>
    public string Texto { get; init; } = "";

    public IReadOnlyList<TrechoMd> Trechos { get; init; } = Array.Empty<TrechoMd>();

    /// <summary>Preenchido só quando <see cref="Tipo"/> é <see cref="BlocoMdTipo.Tabela"/>.</summary>
    public IReadOnlyList<LinhaTabelaMd> Linhas { get; init; } = Array.Empty<LinhaTabelaMd>();
}

/// <summary>
/// Markdown de um subconjunto — títulos, ênfase, código, listas, citações, réguas e
/// links — que é o que README e notas de release usam de fato. Lógica pura, sem UI:
/// a View só sabe desenhar blocos e trechos.
///
/// Não é um parser completo de CommonMark e não tenta ser: HTML fora do comum e
/// aninhamentos exóticos passam como texto, que é melhor que quebrar a tela. O HTML
/// comum de README (p, ul/li, br, b, a, img) vira markdown antes, em <see cref="HtmlEmbutido"/>,
/// e ":hammer:" vira emoji em <see cref="Emojis"/>.
/// </summary>
public static class MarkdownParser
{
    public static List<BlocoMd> Blocos(string markdown)
    {
        var blocos = new List<BlocoMd>();
        if (string.IsNullOrWhiteSpace(markdown)) return blocos;

        markdown = HtmlEmbutido.ParaMarkdown(markdown.Replace("\r\n", "\n"));
        var linhas = markdown.Replace('\r', '\n').Split('\n');
        var paragrafo = new StringBuilder();

        void FecharParagrafo()
        {
            if (paragrafo.Length == 0) return;
            blocos.Add(new BlocoMd
            {
                Tipo = BlocoMdTipo.Paragrafo,
                Trechos = Trechos(paragrafo.ToString().Trim()),
            });
            paragrafo.Clear();
        }

        for (var i = 0; i < linhas.Length; i++)
        {
            var linha = linhas[i];
            var limpa = linha.TrimEnd();
            var semEspaco = limpa.TrimStart();

            // bloco de código: tudo entre as cercas vai cru
            if (semEspaco.StartsWith("```", StringComparison.Ordinal))
            {
                FecharParagrafo();
                var codigo = new StringBuilder();
                i++;
                while (i < linhas.Length && !linhas[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    codigo.AppendLine(linhas[i]);
                    i++;
                }
                blocos.Add(new BlocoMd { Tipo = BlocoMdTipo.Codigo, Texto = codigo.ToString().TrimEnd() });
                continue;
            }

            if (semEspaco.Length == 0)
            {
                FecharParagrafo();
                continue;
            }

            // régua: --- *** ___
            if (semEspaco.Length >= 3 && (EhTudo(semEspaco, '-') || EhTudo(semEspaco, '*') || EhTudo(semEspaco, '_')))
            {
                FecharParagrafo();
                blocos.Add(new BlocoMd { Tipo = BlocoMdTipo.Regua });
                continue;
            }

            if (semEspaco.StartsWith("#", StringComparison.Ordinal))
            {
                var nivel = 0;
                while (nivel < semEspaco.Length && semEspaco[nivel] == '#') nivel++;

                if (nivel <= 6 && nivel < semEspaco.Length && semEspaco[nivel] == ' ')
                {
                    FecharParagrafo();
                    blocos.Add(new BlocoMd
                    {
                        Tipo = BlocoMdTipo.Titulo,
                        Nivel = nivel,
                        Trechos = Trechos(semEspaco[(nivel + 1)..].Trim()),
                    });
                    continue;
                }
            }

            // tabela: uma linha com barras seguida da linha de tracinhos
            if (semEspaco.Contains('|') && i + 1 < linhas.Length && EhSeparadorDeTabela(linhas[i + 1]))
            {
                FecharParagrafo();

                var linhasTabela = new List<LinhaTabelaMd>
                {
                    new() { Cabecalho = true, Celulas = Celulas(semEspaco) },
                };

                i += 2; // pula o separador
                while (i < linhas.Length && linhas[i].Contains('|') && linhas[i].Trim().Length > 0)
                {
                    linhasTabela.Add(new LinhaTabelaMd { Celulas = Celulas(linhas[i]) });
                    i++;
                }
                i--; // o for incrementa de novo

                blocos.Add(new BlocoMd { Tipo = BlocoMdTipo.Tabela, Linhas = linhasTabela });
                continue;
            }

            if (semEspaco.StartsWith("> ", StringComparison.Ordinal) || semEspaco == ">")
            {
                FecharParagrafo();
                blocos.Add(new BlocoMd
                {
                    Tipo = BlocoMdTipo.Citacao,
                    Trechos = Trechos(semEspaco.Length > 1 ? semEspaco[2..].Trim() : ""),
                });
                continue;
            }

            var recuo = limpa.Length - semEspaco.Length;
            var marcador = MarcadorDeLista(semEspaco, out var conteudo);
            if (marcador.Length > 0)
            {
                FecharParagrafo();
                blocos.Add(new BlocoMd
                {
                    Tipo = BlocoMdTipo.Item,
                    Nivel = recuo / 2,
                    Marcador = marcador,
                    Trechos = Trechos(conteudo),
                });
                continue;
            }

            if (paragrafo.Length > 0) paragrafo.Append(' ');
            paragrafo.Append(semEspaco);
        }

        FecharParagrafo();
        return blocos;
    }

    /// <summary>A linha "|---|:--:|---|" que confirma que a anterior era o cabeçalho.</summary>
    private static bool EhSeparadorDeTabela(string linha)
    {
        var limpa = linha.Trim();
        if (!limpa.Contains('-') || !limpa.Contains('|')) return false;

        foreach (var c in limpa)
            if (c is not ('|' or '-' or ':' or ' ')) return false;

        return true;
    }

    /// <summary>Células de uma linha de tabela, sem as barras das pontas.</summary>
    private static List<IReadOnlyList<TrechoMd>> Celulas(string linha)
    {
        var bruta = linha.Trim().Trim('|');
        var saida = new List<IReadOnlyList<TrechoMd>>();

        foreach (var celula in bruta.Split('|'))
            saida.Add(Trechos(celula.Trim()));

        return saida;
    }

    private static bool EhTudo(string texto, char c)
    {
        foreach (var ch in texto)
            if (ch != c && ch != ' ') return false;
        return true;
    }

    /// <summary>"- ", "* ", "+ " ou "1. "; devolve o marcador a desenhar.</summary>
    private static string MarcadorDeLista(string linha, out string conteudo)
    {
        conteudo = "";

        if (linha.Length > 2 && (linha[0] is '-' or '*' or '+') && linha[1] == ' ')
        {
            conteudo = linha[2..].Trim();
            return "•";
        }

        var ponto = linha.IndexOf(". ", StringComparison.Ordinal);
        if (ponto is > 0 and <= 3)
        {
            var numero = linha[..ponto];
            foreach (var c in numero)
                if (!char.IsDigit(c)) return "";

            conteudo = linha[(ponto + 2)..].Trim();
            return numero + ".";
        }

        return "";
    }

    /// <summary>
    /// Quebra a linha em trechos formatados. A ordem importa: código cru primeiro, para
    /// que `**isto**` dentro de crases não vire negrito.
    /// </summary>
    /// <summary>
    /// A ênfase não pode abrir nem fechar colada a espaço — é o que impede "2 * 3 e um
    /// **total" de virar itálico até o próximo asterisco, e é a mesma regra do CommonMark.
    /// </summary>
    private static bool Delimita(string linha, int inicio, int fim) =>
        fim > inicio && !char.IsWhiteSpace(linha[inicio]) && !char.IsWhiteSpace(linha[fim - 1]);

    /// <summary>Colchete que fecha o de <paramref name="abre"/>, contando aninhamento.</summary>
    private static int FechaColchete(string linha, int abre)
    {
        var nivel = 0;
        for (var i = abre; i < linha.Length; i++)
        {
            if (linha[i] == '[') nivel++;
            else if (linha[i] == ']' && --nivel == 0) return i;
        }
        return -1;
    }

    /// <summary>Texto puro de um rótulo de link, descartando a marcação interna.</summary>
    private static string SoTexto(string rotulo)
    {
        var sb = new StringBuilder();
        foreach (var t in Trechos(rotulo)) sb.Append(t.Texto);
        return sb.ToString();
    }

    public static List<TrechoMd> Trechos(string linha)
    {
        var saida = new List<TrechoMd>();
        if (string.IsNullOrEmpty(linha)) return saida;

        var texto = new StringBuilder();
        var i = 0;

        void Descarregar()
        {
            if (texto.Length == 0) return;
            saida.Add(new TrechoMd { Texto = Emojis.Trocar(texto.ToString()) });
            texto.Clear();
        }

        while (i < linha.Length)
        {
            // `código`
            if (linha[i] == '`')
            {
                var fim = linha.IndexOf('`', i + 1);
                if (fim > i)
                {
                    Descarregar();
                    saida.Add(new TrechoMd { Texto = linha[(i + 1)..fim], Codigo = true });
                    i = fim + 1;
                    continue;
                }
            }

            // ![alt](url): imagem não é desenhada, mas o "!" solto no meio do texto é
            // pior que nada — fica só o alt, discreto, como legenda
            if (linha[i] == '!' && i + 1 < linha.Length && linha[i + 1] == '[')
            {
                var fechaAlt = linha.IndexOf(']', i + 2);
                if (fechaAlt > i && fechaAlt + 1 < linha.Length && linha[fechaAlt + 1] == '(')
                {
                    var fimUrl = linha.IndexOf(')', fechaAlt + 2);
                    if (fimUrl > fechaAlt)
                    {
                        Descarregar();
                        var alt = linha[(i + 2)..fechaAlt];
                        if (alt.Length > 0) saida.Add(new TrechoMd { Texto = alt, Italico = true });
                        i = fimUrl + 1;
                        continue;
                    }
                }
            }

            // [texto](url), inclusive [![alt](img)](url) — o selo de build é assim, e
            // sem contar os colchetes aninhados o rótulo saía cru na tela
            if (linha[i] == '[')
            {
                var fechaTexto = FechaColchete(linha, i);
                if (fechaTexto > i && fechaTexto + 1 < linha.Length && linha[fechaTexto + 1] == '(')
                {
                    var fechaUrl = linha.IndexOf(')', fechaTexto + 2);
                    if (fechaUrl > fechaTexto)
                    {
                        Descarregar();

                        var rotulo = linha[(i + 1)..fechaTexto];
                        saida.Add(new TrechoMd
                        {
                            Texto = Emojis.Trocar(SoTexto(rotulo)),
                            Link = linha[(fechaTexto + 2)..fechaUrl].Trim(),
                        });
                        i = fechaUrl + 1;
                        continue;
                    }
                }
            }

            // **negrito** e __negrito__
            if (i + 1 < linha.Length && (linha[i] == '*' || linha[i] == '_') && linha[i + 1] == linha[i])
            {
                var marca = new string(linha[i], 2);
                var fim = linha.IndexOf(marca, i + 2, StringComparison.Ordinal);
                if (fim > i + 1 && Delimita(linha, i + 2, fim))
                {
                    Descarregar();
                    saida.Add(new TrechoMd { Texto = Emojis.Trocar(linha[(i + 2)..fim]), Negrito = true });
                    i = fim + 2;
                    continue;
                }
            }

            // *itálico* e _itálico_
            if (linha[i] is '*' or '_')
            {
                var fim = linha.IndexOf(linha[i], i + 1);
                if (fim > i + 1 && Delimita(linha, i + 1, fim))
                {
                    Descarregar();
                    saida.Add(new TrechoMd { Texto = Emojis.Trocar(linha[(i + 1)..fim]), Italico = true });
                    i = fim + 1;
                    continue;
                }
            }

            texto.Append(linha[i]);
            i++;
        }

        Descarregar();
        return saida;
    }
}
