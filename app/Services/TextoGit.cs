using System;
using System.IO;
using System.Text;

namespace GRepos.Services;

/// <summary>
/// O git devolve o conteúdo dos arquivos com os bytes originais. Fonte Delphi costuma
/// estar em ANSI (Windows-1252), e lido como UTF-8 cada acento vira "�". Aqui cada linha
/// que não é UTF-8 válido é lida como Windows-1252 — o que o VS Code também faz.
/// </summary>
public static class TextoGit
{
    public static readonly Encoding Ansi = CriarAnsi();

    private static readonly UTF8Encoding Utf8Estrito = new(false, true);
    private static readonly UTF8Encoding Utf8 = new(false);

    private static Encoding CriarAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public static bool EhUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            Utf8Estrito.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>UTF-8 onde for válido; Windows-1252 linha a linha onde não for.</summary>
    public static string Decodificar(ReadOnlySpan<byte> bytes)
    {
        if (EhUtf8(bytes)) return Utf8.GetString(bytes);

        var sb = new StringBuilder(bytes.Length);
        while (bytes.Length > 0)
        {
            var fim = bytes.IndexOf((byte)'\n');
            var linha = fim < 0 ? bytes : bytes[..(fim + 1)];
            sb.Append(EhUtf8(linha) ? Utf8.GetString(linha) : Ansi.GetString(linha));
            bytes = bytes[linha.Length..];
        }
        return sb.ToString();
    }

    /// <summary>
    /// Caminho como o git o escreve: entre aspas e com escapes estilo C ("\303\243" são os
    /// bytes UTF-8 de "ã") quando tem caractere especial. Sem aspas, volta como veio.
    /// </summary>
    public static string Caminho(string s)
    {
        if (s.Length < 2 || s[0] != '"' || s[^1] != '"') return s;

        var bytes = new System.Collections.Generic.List<byte>(s.Length);
        for (var i = 1; i < s.Length - 1; i++)
        {
            var c = s[i];
            if (c != '\\' || i + 1 >= s.Length - 1)
            {
                bytes.AddRange(Utf8.GetBytes(c.ToString()));
                continue;
            }

            var e = s[++i];
            if (e is >= '0' and <= '7' && i + 2 < s.Length - 1)
            {
                bytes.Add(Convert.ToByte(s.Substring(i, 3), 8));
                i += 2;
                continue;
            }
            bytes.Add(e switch
            {
                'n' => (byte)'\n', 't' => (byte)'\t', 'r' => (byte)'\r', 'a' => 7, 'b' => 8,
                'f' => 12, 'v' => 11, _ => (byte)e,
            });
        }
        return Utf8.GetString(bytes.ToArray());
    }

    /// <summary>
    /// Bytes do patch para o "git apply": cabeçalho (caminhos) sempre em UTF-8, que é como
    /// o git guarda nomes; linhas de conteúdo no encoding do arquivo.
    /// </summary>
    public static byte[] CodificarPatch(string patch, Encoding conteudo)
    {
        var saida = new System.Collections.Generic.List<byte>(patch.Length);
        var noBloco = false;
        foreach (var linha in patch.Split('\n'))
        {
            if (linha.StartsWith("diff --git ")) noBloco = false;
            else if (linha.StartsWith("@@")) noBloco = true;
            saida.AddRange((noBloco ? conteudo : Utf8).GetBytes(linha));
            saida.Add((byte)'\n');
        }
        saida.RemoveAt(saida.Count - 1); // o Split cria uma linha a mais que as quebras
        return saida.ToArray();
    }

    /// <summary>
    /// Encoding em que um patch deve voltar ao git: o mesmo do arquivo em disco, senão o
    /// "git apply" não acha as linhas de contexto. Arquivo apagado ou ilegível fica em UTF-8.
    /// </summary>
    public static Encoding EncodingDoArquivo(string caminho)
    {
        try
        {
            return File.Exists(caminho) && !EhUtf8(File.ReadAllBytes(caminho)) ? Ansi : Utf8;
        }
        catch (IOException)
        {
            return Utf8;
        }
        catch (UnauthorizedAccessException)
        {
            return Utf8;
        }
    }
}
