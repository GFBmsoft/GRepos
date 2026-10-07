using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using GRepos.Services;

namespace GRepos.Views;

/// <summary>
/// Põe na área de transferência o mesmo conteúdo em dois formatos: HTML, que o Word, o
/// Outlook e o Teams colam com título, lista e link; e texto limpo, para o Bloco de
/// Notas e qualquer lugar que só aceite texto.
/// </summary>
public static class CopiaFormatada
{
    /// <summary>O nome com que o Windows registra HTML na área de transferência.</summary>
    private static readonly DataFormat<byte[]> FormatoHtml = DataFormat.CreateBytesPlatformFormat("HTML Format");

    /// <returns>Falso quando o sistema não aceitou o HTML e foi só o texto limpo.</returns>
    public static async Task<bool> CopiarAsync(IClipboard clipboard, string html, string textoLimpo)
    {
        try
        {
            var item = new DataTransferItem();
            item.Set(DataFormat.Text, textoLimpo);
            item.Set(FormatoHtml, MarkdownExport.ParaAreaDeTransferencia(html));

            var dados = new DataTransfer();
            dados.Add(item);
            await clipboard.SetDataAsync(dados);
            return true;
        }
        catch (Exception)
        {
            // sistema sem o formato de HTML na área de transferência: vai o texto limpo
            await clipboard.SetTextAsync(textoLimpo);
            return false;
        }
    }
}
