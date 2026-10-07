using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Uma pasta de trabalho do repositório: a principal ou uma criada por "git worktree".</summary>
public sealed class Worktree
{
    public string Caminho { get; init; } = "";

    /// <summary>Branch aberta ali; vazio com o HEAD solto.</summary>
    public string Branch { get; init; } = "";
    public string Head { get; init; } = "";

    /// <summary>A primeira da lista é a pasta original do repositório: não se remove.</summary>
    public bool Principal { get; init; }
    public bool Travada { get; init; }

    /// <summary>A pasta sumiu do disco e só sobrou o registro no git.</summary>
    public bool Orfa { get; init; }
}

/// <summary>
/// Worktrees: a mesma história em mais de uma pasta, cada uma com a sua branch — para
/// olhar ou corrigir outra branch sem guardar o que está em andamento.
/// </summary>
public static class Worktrees
{
    /// <summary>Saída de <c>git worktree list --porcelain</c>: blocos separados por linha vazia.</summary>
    public static List<Worktree> Ler(string raw)
    {
        var lista = new List<Worktree>();
        string caminho = "", branch = "", head = "";
        bool travada = false, orfa = false;

        void Fechar()
        {
            if (caminho.Length == 0) return;
            lista.Add(new Worktree
            {
                Caminho = caminho, Branch = branch, Head = head,
                Principal = lista.Count == 0, Travada = travada, Orfa = orfa,
            });
            caminho = branch = head = "";
            travada = orfa = false;
        }

        foreach (var bruta in raw.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');
            if (linha.Length == 0) { Fechar(); continue; }

            if (linha.StartsWith("worktree ")) { Fechar(); caminho = linha["worktree ".Length..]; }
            else if (linha.StartsWith("HEAD ")) head = linha["HEAD ".Length..];
            else if (linha.StartsWith("branch ")) branch = linha["branch ".Length..].Replace("refs/heads/", "");
            else if (linha.StartsWith("locked")) travada = true;
            else if (linha.StartsWith("prunable")) orfa = true;
        }
        Fechar();
        return lista;
    }

    public static async Task<List<Worktree>> ListarAsync(string repo) =>
        Ler(await GitService.RunAsync(repo, new[] { "worktree", "list", "--porcelain" }));

    /// <summary>
    /// Pasta sugerida para a branch: ao lado do repositório, com o nome dele e o da
    /// branch ("Financeiro-imp-boleto"). Dentro do repositório ela apareceria como
    /// arquivo novo nele mesmo.
    /// </summary>
    public static string PastaSugerida(string repo, string branch)
    {
        var limpo = new string(branch.Trim().Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '-').ToArray())
            .Trim('-');
        if (limpo.Length == 0) return "";

        var raiz = repo.TrimEnd('\\', '/');
        var pai = Path.GetDirectoryName(raiz) ?? raiz;
        return Path.Combine(pai, $"{Path.GetFileName(raiz)}-{limpo}");
    }

    /// <summary>
    /// Cria a pasta com a branch. Branch local abre como está; se só existe no remoto,
    /// nasce a local vinculada a ela; nome novo vira branch nova a partir do HEAD.
    /// </summary>
    public static async Task CriarAsync(string repo, string pasta, string branch)
    {
        branch = branch.Trim();
        pasta = pasta.Trim();
        if (branch.Length == 0) throw new GitException("Informe a branch da nova pasta de trabalho.");
        if (pasta.Length == 0) throw new GitException("Informe a pasta.");
        if (Directory.Exists(pasta) && Directory.EnumerateFileSystemEntries(pasta).Any())
            throw new GitException("A pasta já existe e não está vazia: " + pasta);

        if (await GitService.RefExisteAsync(repo, "refs/heads/" + branch))
            await GitService.RunAsync(repo, new[] { "worktree", "add", pasta, branch });
        else if (await GitService.RefExisteAsync(repo, "refs/remotes/origin/" + branch))
            await GitService.RunAsync(repo, new[] { "worktree", "add", "--track", "-b", branch, pasta, "origin/" + branch });
        else
            await GitService.RunAsync(repo, new[] { "worktree", "add", "-b", branch, pasta });
    }

    /// <summary>
    /// Remove a pasta de trabalho. Sem <paramref name="forcar"/> o git recusa quando há
    /// alteração não commitada lá — é a proteção que se quer por padrão.
    /// </summary>
    public static Task<string> RemoverAsync(string repo, string pasta, bool forcar)
    {
        var args = new List<string> { "worktree", "remove" };
        if (forcar) args.Add("--force");
        args.Add(pasta);
        return GitService.RunAsync(repo, args);
    }

    /// <summary>Limpa os registros de pastas que não existem mais.</summary>
    public static Task<string> LimparOrfasAsync(string repo) =>
        GitService.RunAsync(repo, new[] { "worktree", "prune" });
}
