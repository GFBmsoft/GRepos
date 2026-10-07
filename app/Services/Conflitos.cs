using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GRepos.Services;

/// <summary>
/// Um pedaço do arquivo em conflito: texto igual dos dois lados, ou um bloco em que cada
/// lado ficou de um jeito. As linhas guardam a própria quebra, para o arquivo voltar ao
/// disco com os mesmos fins de linha.
/// </summary>
public sealed class TrechoDeConflito
{
    public bool Conflito { get; init; }

    /// <summary>Texto comum; vazio num bloco de conflito.</summary>
    public List<string> Linhas { get; init; } = new();

    /// <summary>Primeiro lado do marcador (o HEAD) e segundo (o que está chegando).</summary>
    public List<string> Nosso { get; init; } = new();
    public List<string> Deles { get; init; } = new();

    /// <summary>Linha do arquivo com marcadores onde o trecho começa, a partir de 1.</summary>
    public int LinhaInicial { get; init; }
}

/// <summary>
/// Leitura e remontagem do arquivo com marcadores de conflito (<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt;</c>,
/// <c>=======</c>, <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c>) que o git deixa na pasta de trabalho.
/// Lógica pura: a tela só escolhe o que entra em cada bloco.
/// </summary>
public static class Conflitos
{
    /// <summary>Quebra o texto em linhas mantendo a quebra (\n, \r\n ou \r) no fim de cada uma.</summary>
    public static List<string> Linhas(string texto)
    {
        var linhas = new List<string>();
        var inicio = 0;
        for (var i = 0; i < texto.Length; i++)
        {
            if (texto[i] != '\n' && texto[i] != '\r') continue;
            if (texto[i] == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;

            linhas.Add(texto[inicio..(i + 1)]);
            inicio = i + 1;
        }
        if (inicio < texto.Length) linhas.Add(texto[inicio..]);
        return linhas;
    }

    private static bool Marcador(string linha, char c, bool exato = false)
    {
        if (linha.Length < 7) return false;
        for (var i = 0; i < 7; i++)
            if (linha[i] != c) return false;

        // o marcador é seguido de espaço e rótulo, ou termina a linha; "========" (oito) não é
        if (linha.Length == 7) return true;
        var depois = linha[7];
        return exato ? depois is '\r' or '\n' : depois is ' ' or '\r' or '\n';
    }

    /// <summary>
    /// Separa o arquivo em trechos. Marcador sem fechamento (arquivo que só parece ter
    /// conflito) vira texto comum: melhor não oferecer bloco do que cortar o arquivo.
    /// </summary>
    public static List<TrechoDeConflito> Ler(string texto)
    {
        var linhas = Linhas(texto);
        var trechos = new List<TrechoDeConflito>();
        var comum = new List<string>();
        var inicioComum = 1;

        void FecharComum()
        {
            if (comum.Count == 0) return;
            trechos.Add(new TrechoDeConflito { Linhas = comum, LinhaInicial = inicioComum });
            comum = new List<string>();
        }

        var i = 0;
        while (i < linhas.Count)
        {
            if (!Marcador(linhas[i], '<'))
            {
                if (comum.Count == 0) inicioComum = i + 1;
                comum.Add(linhas[i++]);
                continue;
            }

            // procura o fim do bloco antes de aceitá-lo
            var nosso = new List<string>();
            var deles = new List<string>();
            var parte = 0; // 0 nosso, 1 base (diff3), 2 deles
            var j = i + 1;
            var fechou = false;
            for (; j < linhas.Count; j++)
            {
                var l = linhas[j];
                if (parte < 2 && Marcador(l, '|')) { parte = 1; continue; }
                if (parte < 2 && Marcador(l, '=', exato: true)) { parte = 2; continue; }
                if (parte == 2 && Marcador(l, '>')) { fechou = true; break; }
                if (Marcador(l, '<')) break; // outro começo sem ter fechado: não é um bloco válido

                if (parte == 0) nosso.Add(l);
                else if (parte == 2) deles.Add(l);
            }

            if (!fechou)
            {
                if (comum.Count == 0) inicioComum = i + 1;
                comum.Add(linhas[i++]);
                continue;
            }

            FecharComum();
            trechos.Add(new TrechoDeConflito { Conflito = true, Nosso = nosso, Deles = deles, LinhaInicial = i + 1 });
            i = j + 1;
        }

        FecharComum();
        return trechos;
    }

    public static bool TemMarcadores(string texto) => Ler(texto).Any(t => t.Conflito);

    /// <summary>Quebra de linha predominante do arquivo, para o que for digitado na resolução.</summary>
    public static string QuebraDoArquivo(string texto)
    {
        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < texto.Length; i++)
        {
            if (texto[i] != '\n') continue;
            if (i > 0 && texto[i - 1] == '\r') crlf++;
            else lf++;
        }
        return crlf > lf ? "\r\n" : "\n";
    }

    /// <summary>Linhas para mostrar na tela: sem a quebra de cada uma, unidas por \n.</summary>
    public static string ParaTela(IEnumerable<string> linhas) =>
        string.Join("\n", linhas.Select(l => l.TrimEnd('\r', '\n')));

    /// <summary>
    /// O que foi escolhido ou digitado na tela de volta em linhas do arquivo, com a quebra
    /// dele. Texto vazio são zero linhas: é como se resolve "nenhum dos dois".
    /// </summary>
    public static List<string> DaTela(string texto, string quebra)
    {
        if (texto.Length == 0) return new List<string>();
        return texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l + quebra).ToList();
    }

    /// <summary>
    /// Remonta o arquivo: texto comum como estava, e em cada bloco o que a resolução
    /// daquele bloco mandou. As resoluções vêm na ordem dos blocos.
    /// </summary>
    public static string Montar(IReadOnlyList<TrechoDeConflito> trechos, IReadOnlyList<IReadOnlyList<string>> resolucoes)
    {
        var sb = new StringBuilder();
        var bloco = 0;
        for (var t = 0; t < trechos.Count; t++)
        {
            var trecho = trechos[t];
            if (!trecho.Conflito)
            {
                foreach (var l in trecho.Linhas) sb.Append(l);
                continue;
            }

            if (bloco >= resolucoes.Count)
                throw new InvalidOperationException("Há bloco de conflito sem resolução.");

            var linhas = resolucoes[bloco++];
            for (var k = 0; k < linhas.Count; k++)
            {
                var l = linhas[k];

                // último bloco do arquivo, e o arquivo não terminava em quebra: não inventa uma
                var ultimaDoArquivo = t == trechos.Count - 1 && k == linhas.Count - 1;
                if (ultimaDoArquivo && !TerminaEmQuebra(trecho)) l = l.TrimEnd('\r', '\n');
                sb.Append(l);
            }
        }
        return sb.ToString();
    }

    private static bool TerminaEmQuebra(TrechoDeConflito bloco)
    {
        var ultima = bloco.Deles.LastOrDefault() ?? bloco.Nosso.LastOrDefault();
        return ultima is null || ultima.EndsWith('\n') || ultima.EndsWith('\r');
    }

    // ------------------------------------------------------------- arquivo

    /// <summary>Conteúdo do arquivo e como regravá-lo sem trocar o encoding.</summary>
    public sealed record ArquivoLido(string Texto, bool Ansi, bool Bom);

    private static readonly byte[] BomUtf8 = { 0xEF, 0xBB, 0xBF };

    /// <summary>
    /// Fonte Delphi costuma estar em ANSI: regravar em UTF-8 trocaria todos os acentos
    /// do arquivo de uma vez. O arquivo volta ao disco como foi lido.
    /// </summary>
    public static ArquivoLido LerArquivo(string caminho)
    {
        var bytes = File.ReadAllBytes(caminho);
        var bom = bytes.AsSpan().StartsWith(BomUtf8);
        var corpo = bom ? bytes.AsSpan(3) : bytes.AsSpan();

        return TextoGit.EhUtf8(corpo)
            ? new ArquivoLido(new UTF8Encoding(false).GetString(corpo), false, bom)
            : new ArquivoLido(TextoGit.Ansi.GetString(corpo), true, false);
    }

    public static void GravarArquivo(string caminho, string texto, ArquivoLido original)
    {
        var corpo = original.Ansi ? TextoGit.Ansi.GetBytes(texto) : new UTF8Encoding(false).GetBytes(texto);
        var bytes = original.Bom ? BomUtf8.Concat(corpo).ToArray() : corpo;

        // .tmp e troca: queda no meio da gravação não deixa o fonte pela metade
        var tmp = caminho + ".grepos-tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, caminho, overwrite: true);
    }
}
