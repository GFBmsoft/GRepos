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
