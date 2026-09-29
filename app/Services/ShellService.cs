using System;
using System.Diagnostics;
using System.IO;

namespace GRepos.Services;

/// <summary>Abre endereços e pastas no sistema — navegador padrão e Explorer.</summary>
public static class ShellService
{
    public static void AbrirUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // só http(s): abrir qualquer esquema com ShellExecute é pedir problema
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Endereço inválido: " + url);

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void AbrirPasta(string caminho)
    {
        if (!Directory.Exists(caminho))
            throw new DirectoryNotFoundException("Pasta não encontrada: " + caminho);

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetFullPath(caminho)}\"")
        {
            UseShellExecute = true,
        });
    }
}
