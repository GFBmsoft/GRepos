using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>
/// Um lado da comparação. <see cref="Rev"/> é o que vem antes dos dois-pontos no git
/// (<c>HEAD</c>, um hash, vazio para o índice, <c>:2</c> para um lado do conflito);
/// null quer dizer o arquivo do disco, que a ferramenta abre no lugar e pode editar.
/// </summary>
public sealed record VersaoDeArquivo(string? Rev, string Caminho, string Rotulo)
{
    public static VersaoDeArquivo NoDisco(string caminho) => new(null, caminho, "disco");
    public static VersaoDeArquivo NoIndice(string caminho) => new("", caminho, "índice");
    public static VersaoDeArquivo Em(string rev, string caminho, string rotulo) => new(rev, caminho, rotulo);
}

/// <summary>
/// Abre dois lados de um arquivo numa ferramenta de comparação instalada (Beyond Compare,
/// WinMerge, Meld…). O caminho configurado pode ser a pasta de instalação ou o próprio
/// executável; vazio, procura nos lugares em que os instaladores costumam deixar.
/// </summary>
public static class DiffExterno
{
    /// <summary>Usado quando a ferramenta não é uma das conhecidas.</summary>
    public const string ArgumentosGenericos = "\"$LOCAL\" \"$REMOTE\"";

    /// <summary>
    /// Ferramentas reconhecidas: executável, pastas de instalação e a linha de comando de
    /// cada uma. <c>$LOCAL</c> e <c>$REMOTE</c> são os mesmos nomes do <c>git difftool</c>;
    /// <c>$LTITLE</c> e <c>$RTITLE</c>, os rótulos dos lados.
    /// </summary>
    private static readonly (string Exe, string[] Pastas, string Argumentos)[] Conhecidas =
    {
        ("BCompare.exe", new[] { "Beyond Compare 5", "Beyond Compare 4", "Beyond Compare 3" },
            "\"$LOCAL\" \"$REMOTE\" /lefttitle=\"$LTITLE\" /righttitle=\"$RTITLE\""),
        ("WinMergeU.exe", new[] { "WinMerge" },
            "/e /u /dl \"$LTITLE\" /dr \"$RTITLE\" \"$LOCAL\" \"$REMOTE\""),
        ("Meld.exe", new[] { "Meld" }, ArgumentosGenericos),
        ("kdiff3.exe", new[] { "KDiff3" }, ArgumentosGenericos),
        ("p4merge.exe", new[] { "Perforce" }, ArgumentosGenericos),
        ("TortoiseGitMerge.exe", new[] { @"TortoiseGit\bin" }, "/base:\"$LOCAL\" /mine:\"$REMOTE\""),
        ("Code.exe", new[] { "Microsoft VS Code" }, "--diff \"$LOCAL\" \"$REMOTE\""),
    };

    /// <summary>
    /// Executável a abrir, ou null se não achou. <paramref name="existe"/> e
    /// <paramref name="raizes"/> só são trocados nos testes.
    /// </summary>
    public static string? Localizar(string? configurado,
                                    Func<string, bool>? existe = null,
                                    IEnumerable<string>? raizes = null)
    {
        existe ??= File.Exists;

        var conf = (configurado ?? "").Trim().Trim('"');
        if (conf.Length > 0)
        {
            // caminho informado manda: se não achar nada nele, é erro de configuração,
            // e cair na busca automática esconderia isso do usuário
            if (existe(conf)) return conf; // o próprio executável (ou um .cmd que o chama)
            if (conf.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;

            var pasta = conf.TrimEnd('\\', '/');
            return Conhecidas.Select(c => Path.Combine(pasta, c.Exe)).FirstOrDefault(existe);
        }

        foreach (var raiz in raizes ?? RaizesPadrao())
            foreach (var (exe, pastas, _) in Conhecidas)
                foreach (var pasta in pastas)
                    if (Path.Combine(raiz, pasta, exe) is var candidato && existe(candidato))
                        return candidato;
        return null;
    }

    private static IEnumerable<string> RaizesPadrao() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        }.Where(r => r.Length > 0);

    /// <summary>Linha de comando padrão da ferramenta, pelo nome do executável.</summary>
    public static string ArgumentosPadrao(string exe)
    {
        var nome = Path.GetFileName(exe);
        foreach (var c in Conhecidas)
            if (c.Exe.Equals(nome, StringComparison.OrdinalIgnoreCase))
                return c.Argumentos;
        return ArgumentosGenericos;
    }

    /// <summary>Troca os marcadores do modelo; vazio usa o padrão da ferramenta.</summary>
    public static string Argumentos(string exe, string? modelo, string local, string remote,
                                    string tituloLocal, string tituloRemote)
    {
        var m = string.IsNullOrWhiteSpace(modelo) ? ArgumentosPadrao(exe) : modelo.Trim();
        // aspa dentro de título quebraria a linha de comando inteira
        return m.Replace("$LOCAL", local)
                .Replace("$REMOTE", remote)
                .Replace("$LTITLE", tituloLocal.Replace('"', '\''))
                .Replace("$RTITLE", tituloRemote.Replace('"', '\''));
    }

    /// <summary>
    /// Nome do arquivo temporário de uma versão: mantém a extensão (é por ela que a
    /// ferramenta escolhe o realce) e leva o rótulo, que é o que aparece na aba.
    /// </summary>
    public static string NomeTemporario(string caminho, string rotulo)
    {
        var arquivo = Path.GetFileName(caminho.TrimEnd('/', '\\'));
        if (arquivo.Length == 0) arquivo = "arquivo";
        var limpo = new string(rotulo.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        return $"{Path.GetFileNameWithoutExtension(arquivo)}.{(limpo.Length > 0 ? limpo : "versao")}{Path.GetExtension(arquivo)}";
    }

    internal static string PastaTemporaria => Path.Combine(Path.GetTempPath(), "GRepos", "diff");

    /// <summary>
    /// Compara as duas versões na ferramenta. A que não existe (arquivo novo, apagado, um
    /// lado do conflito) entra como arquivo vazio — é como a ferramenta mostra "tudo novo".
    /// </summary>
    public static async Task AbrirAsync(string repo, VersaoDeArquivo esquerda, VersaoDeArquivo direita,
                                        string? configurado, string? modelo)
    {
        var exe = Localizar(configurado) ?? throw new FileNotFoundException(
            string.IsNullOrWhiteSpace(configurado)
                ? "Nenhuma ferramenta de diff configurada. Informe a pasta de instalação em Preferências → Diff externo."
                : "Ferramenta de diff não encontrada em " + configurado.Trim() + ". Confira o caminho em Preferências.");

        LimparAntigos();
        var pasta = Path.Combine(PastaTemporaria, Path.GetRandomFileName());

        var local = await PrepararAsync(repo, esquerda, pasta);
        var remote = await PrepararAsync(repo, direita, pasta);

        Process.Start(new ProcessStartInfo(exe)
        {
            Arguments = Argumentos(exe, modelo, local, remote,
                $"{esquerda.Caminho} ({esquerda.Rotulo})", $"{direita.Caminho} ({direita.Rotulo})"),
            WorkingDirectory = repo,
            UseShellExecute = false,
        })?.Dispose();
    }

    /// <summary>Caminho a entregar à ferramenta: o arquivo do disco, ou uma cópia da versão pedida.</summary>
    private static async Task<string> PrepararAsync(string repo, VersaoDeArquivo versao, string pasta)
    {
        if (versao.Rev is null)
        {
            var real = Path.GetFullPath(Path.Combine(repo, versao.Caminho.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(real)) return real;
        }

        var bytes = versao.Rev is null
            ? null
            : await GitService.ConteudoBrutoAsync(repo, $"{versao.Rev}:{versao.Caminho}");

        Directory.CreateDirectory(pasta);
        var destino = Path.Combine(pasta, NomeTemporario(versao.Caminho, bytes is null ? "vazio" : versao.Rotulo));
        await File.WriteAllBytesAsync(destino, bytes ?? Array.Empty<byte>());
        // é uma cópia: editar ali não muda nada no repositório, e a ferramenta avisa
        File.SetAttributes(destino, FileAttributes.ReadOnly);
        return destino;
    }

    /// <summary>
    /// Não dá para apagar ao fechar a ferramenta: várias devolvem o controle na hora e
    /// seguem em outra instância. As cópias de mais de um dia saem na comparação seguinte.
    /// </summary>
    private static void LimparAntigos()
    {
        try
        {
            if (!Directory.Exists(PastaTemporaria)) return;
            foreach (var dir in Directory.EnumerateDirectories(PastaTemporaria))
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) < TimeSpan.FromDays(1)) continue;
                foreach (var f in Directory.EnumerateFiles(dir))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
        }
        catch (Exception)
        {
            // arquivo ainda aberto na ferramenta: fica para a próxima
        }
    }
}
