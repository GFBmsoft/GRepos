using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace GRepos.Services;

/// <summary>
/// Acha e abre o Git Bash na pasta do repositório. O caminho configurado pode ser a pasta
/// de instalação do Git ou o próprio executável; vazio, procura no PATH e nos lugares
/// em que o instalador costuma deixar.
/// </summary>
public static class GitBash
{
    /// <summary>
    /// Executável a abrir, ou null se não achou. <paramref name="existe"/> e
    /// <paramref name="path"/> só são trocados nos testes.
    /// </summary>
    public static string? Localizar(string? configurado,
                                    Func<string, bool>? existe = null,
                                    string? path = null)
    {
        existe ??= File.Exists;

        var conf = (configurado ?? "").Trim().Trim('"');
        if (conf.Length > 0)
        {
            // caminho informado manda: se não achar nada nele, é erro de configuração,
            // e cair na busca automática esconderia isso do usuário
            if (conf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return existe(conf) ? conf : null;
            return NaPasta(conf, existe);
        }

        foreach (var pasta in Candidatas(path ?? Environment.GetEnvironmentVariable("PATH") ?? ""))
            if (NaPasta(pasta, existe) is { } achou)
                return achou;
        return null;
    }

    /// <summary>
    /// O bash.exe para o terminal embutido. O git-bash.exe não serve: ele abre a janela
    /// do mintty em vez de conversar pelo pseudoconsole.
    /// </summary>
    public static string? LocalizarBash(string? configurado,
                                        Func<string, bool>? existe = null,
                                        string? path = null)
    {
        existe ??= File.Exists;
        var achou = Localizar(configurado, existe, path);
        if (achou is null) return null;
        if (!Path.GetFileName(achou).Equals("git-bash.exe", StringComparison.OrdinalIgnoreCase))
            return achou;

        var bash = Path.Combine(Path.GetDirectoryName(achou) ?? "", "bin", "bash.exe");
        return existe(bash) ? bash : null;
    }

    /// <summary>
    /// Na pasta do Git ou em alguma subpasta dela que costuma estar no PATH
    /// (<c>cmd</c>, <c>bin</c>, <c>mingw64\bin</c>): sobe até três níveis procurando.
    /// </summary>
    private static string? NaPasta(string pasta, Func<string, bool> existe)
    {
        var atual = pasta.TrimEnd('\\', '/');
        for (var i = 0; i < 4 && atual.Length > 0; i++)
        {
            var gitBash = Path.Combine(atual, "git-bash.exe");
            if (existe(gitBash)) return gitBash;

            // instalação sem o git-bash.exe (portátil mínima): o bash da pasta bin serve
            var bash = Path.Combine(atual, "bin", "bash.exe");
            if (existe(bash)) return bash;

            atual = Path.GetDirectoryName(atual) ?? "";
        }
        return null;
    }

    private static IEnumerable<string> Candidatas(string path)
    {
        // só as pastas do PATH que têm cara de Git: subir três níveis a partir de
        // qualquer pasta acharia um git-bash.exe por acaso em lugar nenhum útil
        foreach (var p in path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (p.Contains("git", StringComparison.OrdinalIgnoreCase))
                yield return p;

        foreach (var raiz in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                 })
            if (raiz.Length > 0)
                yield return Path.Combine(raiz, "Git");
    }

    /// <summary>Abre um Git Bash novo já posicionado em <paramref name="pasta"/>.</summary>
    public static void Abrir(string pasta, string? configurado)
    {
        if (!Directory.Exists(pasta))
            throw new DirectoryNotFoundException("Pasta não encontrada: " + pasta);

        var exe = Localizar(configurado) ?? throw new FileNotFoundException(
            string.IsNullOrWhiteSpace(configurado)
                ? "Git Bash não encontrado. Informe a pasta do Git em Preferências."
                : "Git Bash não encontrado em " + configurado.Trim() + ". Confira o caminho em Preferências.");

        var cheia = Path.GetFullPath(pasta);
        var gitBash = Path.GetFileName(exe).Equals("git-bash.exe", StringComparison.OrdinalIgnoreCase);
        Process.Start(new ProcessStartInfo(exe)
        {
            // o git-bash.exe tem o próprio --cd; o bash.exe puro abre no diretório de trabalho
            Arguments = gitBash ? $"--cd=\"{cheia}\"" : "--login -i",
            WorkingDirectory = cheia,
            UseShellExecute = true,
        });
    }
}
