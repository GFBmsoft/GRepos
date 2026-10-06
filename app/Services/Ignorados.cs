using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>
/// Item fora da lista de alterações só nesta máquina. <see cref="Rastreado"/>: arquivo do
/// repositório marcado com skip-worktree (o texto é o caminho); senão, uma linha do
/// <c>.git/info/exclude</c> (o texto é o padrão, como está no arquivo).
/// </summary>
public sealed record Ignorado(string Texto, bool Rastreado);

/// <summary>
/// "Ignorar alterações" sem mexer no repositório dos outros. Arquivo rastreado recebe
/// skip-worktree: o git para de olhar para ele, e a alteração local fica onde está.
/// Arquivo novo vai para o <c>.git/info/exclude</c>, que é um .gitignore que não é
/// versionado. Nenhum dos dois aparece em commit nem chega ao remoto.
/// </summary>
public static class Ignorados
{
    // ------------------------------------------------------------ lógica pura

    /// <summary>
    /// Padrão que casa só com este caminho: a barra inicial prende à raiz (sem ela,
    /// "config.ini" ignoraria o de qualquer pasta) e os curingas são escapados.
    /// </summary>
    public static string Padrao(string caminho)
    {
        var sb = new StringBuilder("/");
        foreach (var c in caminho.Replace('\\', '/').TrimStart('/'))
        {
            if (c is '*' or '?' or '[' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        // espaço no fim é descartado pelo git, a menos que venha escapado
        if (sb[^1] == ' ') sb.Insert(sb.Length - 1, '\\');
        return sb.ToString();
    }

    /// <summary>Linhas que valem como padrão: sem as vazias e sem os comentários.</summary>
    public static List<string> Padroes(string conteudo) =>
        conteudo.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0 && !l.StartsWith('#'))
            .ToList();

    /// <summary>Acrescenta os padrões que ainda não estão lá, mantendo a quebra de linha do arquivo.</summary>
    public static string Acrescentar(string conteudo, IEnumerable<string> padroes)
    {
        var quebra = conteudo.Contains("\r\n") ? "\r\n" : "\n";
        var existentes = new HashSet<string>(Padroes(conteudo));
        var sb = new StringBuilder(conteudo);
        if (conteudo.Length > 0 && !conteudo.EndsWith('\n')) sb.Append(quebra);

        foreach (var p in padroes)
            if (existentes.Add(p)) sb.Append(p).Append(quebra);
        return sb.ToString();
    }

    /// <summary>Tira as linhas iguais aos padrões; comentários e o resto ficam como estavam.</summary>
    public static string Remover(string conteudo, IEnumerable<string> padroes)
    {
        var fora = new HashSet<string>(padroes);
        var quebra = conteudo.Contains("\r\n") ? "\r\n" : "\n";
        var linhas = conteudo.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (linhas.Count > 0 && linhas[^1].Length == 0) linhas.RemoveAt(linhas.Count - 1);

        var ficam = linhas.Where(l => !fora.Contains(l)).ToList();
        return ficam.Count == 0 ? "" : string.Join(quebra, ficam) + quebra;
    }

    /// <summary>
    /// Caminhos com skip-worktree na saída de <c>git ls-files -v -z</c>: a letra é "S"
    /// (ou "s", quando o arquivo também está como assume-unchanged).
    /// </summary>
    public static List<string> ParseLsFiles(string raw)
    {
        var lista = new List<string>();
        foreach (var item in raw.Split('\0'))
            if (item.Length > 2 && item[0] is 'S' or 's' && item[1] == ' ')
                lista.Add(item[2..]);
        return lista;
    }

    // -------------------------------------------------------------- operações

    /// <param name="rastreados">Arquivos do repositório com alteração local.</param>
    /// <param name="novos">Arquivos (ou pastas) que o git ainda não acompanha.</param>
    public static async Task IgnorarAsync(string repo, IEnumerable<string> rastreados, IEnumerable<string> novos)
    {
        await MarcarAsync(repo, "--skip-worktree", rastreados.ToList());

        var padroes = novos.Select(Padrao).ToList();
        if (padroes.Count == 0) return;
        var arquivo = ArquivoExclude(repo)
            ?? throw new GitException("não foi possível localizar a pasta .git deste repositório");
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        File.WriteAllText(arquivo, Acrescentar(LerSeExistir(arquivo), padroes));
    }

    /// <summary>Tudo o que está ignorado só nesta máquina; lista vazia se a pasta não é um repositório.</summary>
    public static async Task<List<Ignorado>> ListarAsync(string repo)
    {
        var lista = new List<Ignorado>();
        try
        {
            var raw = await GitService.RunAsync(repo, new[] { "ls-files", "-v", "-z" });
            lista.AddRange(ParseLsFiles(raw).Select(p => new Ignorado(p, true)));

            if (ArquivoExclude(repo) is { } arquivo)
                lista.AddRange(Padroes(LerSeExistir(arquivo)).Select(p => new Ignorado(p, false)));
        }
        catch (Exception)
        {
            // só alimenta um contador e uma lista de consulta: sem repositório, nada a mostrar
        }
        return lista;
    }

    /// <summary>Volta a acompanhar: os arquivos reaparecem nas alterações, se ainda diferirem.</summary>
    public static async Task VoltarAsync(string repo, IEnumerable<Ignorado> itens)
    {
        var lista = itens.ToList();
        await MarcarAsync(repo, "--no-skip-worktree", lista.Where(i => i.Rastreado).Select(i => i.Texto).ToList());

        var padroes = lista.Where(i => !i.Rastreado).Select(i => i.Texto).ToList();
        if (padroes.Count == 0 || ArquivoExclude(repo) is not { } arquivo || !File.Exists(arquivo)) return;
        File.WriteAllText(arquivo, Remover(File.ReadAllText(arquivo), padroes));
    }

    /// <summary>
    /// Este vai para o repositório: o <c>.gitignore</c> da raiz é versionado e vale para
    /// todo mundo. Só faz efeito em arquivo que o git ainda não acompanha.
    /// </summary>
    public static void AdicionarAoGitignore(string repo, IEnumerable<string> caminhos)
    {
        var arquivo = Path.Combine(repo, ".gitignore");
        File.WriteAllText(arquivo, Acrescentar(LerSeExistir(arquivo), caminhos.Select(Padrao)));
    }

    // os caminhos vão pela entrada padrão: numa lista grande a linha de comando estoura
    private static Task MarcarAsync(string repo, string opcao, List<string> caminhos) =>
        caminhos.Count == 0
            ? Task.CompletedTask
            : GitService.RunAsync(repo, new[] { "update-index", opcao, "-z", "--stdin" },
                string.Join('\0', caminhos) + '\0');

    private static string LerSeExistir(string arquivo) => File.Exists(arquivo) ? File.ReadAllText(arquivo) : "";

    /// <summary>Num worktree o exclude mora na pasta .git comum, não na do worktree.</summary>
    private static string? ArquivoExclude(string repo)
    {
        if (GitService.GitDir(repo) is not { } dir) return null;

        var comum = Path.Combine(dir, "commondir");
        if (File.Exists(comum))
        {
            var alvo = File.ReadAllText(comum).Trim();
            dir = Path.IsPathRooted(alvo) ? alvo : Path.GetFullPath(Path.Combine(dir, alvo));
        }
        return Path.Combine(dir, "info", "exclude");
    }
}
