using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GRepos.Models;

namespace GRepos.Services;

public sealed class GitException : Exception
{
    public GitException(string message) : base(message) { }
}

/// <summary>
/// Camada Git. Usa o git CLI do sistema: herda credential manager, SSH, hooks e
/// configuração do usuário sem reimplementar nada.
/// </summary>
public static class GitService
{
    private const char US = ''; // separador de campo
    private const char RS = ''; // separador de registro

    private const string LogFormat =
        "--pretty=format:%H" + "%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%D%x1f%s%x1e";

    public static Task<string> RunAsync(string repo, IEnumerable<string> args, string? stdin = null) =>
        RunWithEnvAsync(repo, args, stdin);

    /// <param name="env">Variáveis extras para o processo do git (ex.: desligar prompts).</param>
    public static async Task<string> RunWithEnvAsync(
        string repo, IEnumerable<string> args, string? stdin = null, params (string Nome, string Valor)[] env)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repo);
        foreach (var a in args) psi.ArgumentList.Add(a);

        // Herdar estas variáveis de quem abriu o app (terminal, script, agente) impede o
        // credential manager de pedir login e quebra push/pull com "terminal prompts
        // disabled". Quem precisa delas passa em `env`, nunca por herança.
        psi.Environment.Remove("GIT_TERMINAL_PROMPT");
        psi.Environment.Remove("GCM_INTERACTIVE");
        psi.Environment.Remove("GIT_ASKPASS");
        psi.Environment.Remove("SSH_ASKPASS");

        foreach (var (nome, valor) in env) psi.Environment[nome] = valor;

        using var proc = Process.Start(psi) ?? throw new GitException("não foi possível executar o git");

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();
        }

        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        var stdout = await outTask;
        var stderr = await errTask;

        if (proc.ExitCode != 0)
        {
            var msg = stderr.Trim();
            throw new GitException(msg.Length > 0 ? msg : $"git falhou (código {proc.ExitCode})");
        }
        return stdout;
    }

    private static Task<string> Run(string repo, params string[] args) => RunAsync(repo, args);

    /// <summary>
    /// Usuário do GitHub configurado nas preferências. Informá-lo ao credential manager
    /// é o que faz o token salvo ser encontrado: sem isso, o git procura a credencial
    /// de "https://github.com" sem conta e acaba abrindo a janela de login.
    /// </summary>
    public static string CredentialUser { get; set; } = "";

    private static string[] ComCredencial(params string[] args)
    {
        if (string.IsNullOrWhiteSpace(CredentialUser)) return args;

        var completo = new string[args.Length + 2];
        completo[0] = "-c";
        completo[1] = $"credential.https://github.com.username={CredentialUser.Trim()}";
        args.CopyTo(completo, 2);
        return completo;
    }

    // --------------------------------------------------------------- status

    public static async Task<RepoStatus> StatusAsync(string repo) => (await StatusAndChangesAsync(repo)).Status;

    /// <summary>
    /// Status e lista de arquivos de uma única chamada ao git: as duas informações saem
    /// do mesmo "git status", e rodá-lo duas vezes dobrava o custo de abrir um repositório.
    /// </summary>
    public static async Task<(RepoStatus Status, List<FileChange> Files)> StatusAndChangesAsync(string repo)
    {
        var s = new RepoStatus();
        if (!Directory.Exists(repo))
        {
            s.Error = "pasta não encontrada";
            return (s, new List<FileChange>());
        }
        if (!Directory.Exists(Path.Combine(repo, ".git")) && !File.Exists(Path.Combine(repo, ".git")))
        {
            s.Error = "pasta não é um repositório git";
            return (s, new List<FileChange>());
        }

        string raw;
        try
        {
            raw = await Run(repo, "--no-optional-locks", "status", "--porcelain=v2", "--branch",
                "--untracked-files=all");
        }
        catch (Exception e)
        {
            s.Error = e.Message;
            return (s, new List<FileChange>());
        }

        foreach (var line in raw.Split('\n'))
        {
            if (line.StartsWith("# branch.head ")) s.Branch = line[14..].Trim();
            else if (line.StartsWith("# branch.upstream ")) s.Upstream = line[18..].Trim();
            else if (line.StartsWith("# branch.oid ")) s.Head = line[13..].Trim();
            else if (line.StartsWith("# branch.ab "))
            {
                foreach (var tok in line[12..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (tok.Length < 2 || !int.TryParse(tok[1..], out var n)) continue;
                    if (tok[0] == '+') s.Ahead = n;
                    else if (tok[0] == '-') s.Behind = n;
                }
            }
            else if (line.StartsWith("? ")) s.Untracked++;
            else if (line.StartsWith("u ")) s.Conflicted++;
            else if ((line.StartsWith("1 ") || line.StartsWith("2 ")) && line.Length > 4)
            {
                if (line[2] != '.') s.Staged++;
                if (line[3] != '.') s.Unstaged++;
            }
        }

        s.Stashes = CountStashes(repo);
        return (s, ParsePorcelainV2(raw));
    }

    /// <summary>
    /// Conta os stashes lendo o reflog em disco. Chamar "git stash list" custava outro
    /// processo — metade do tempo do status — só para preencher um contador.
    /// </summary>
    private static int CountStashes(string repo)
    {
        try
        {
            var gitDir = Path.Combine(repo, ".git");
            if (File.Exists(gitDir))
            {
                // worktree ou submódulo: ".git" é um arquivo apontando para o diretório real
                var line = File.ReadAllText(gitDir).Trim();
                const string prefix = "gitdir:";
                if (!line.StartsWith(prefix)) return 0;
                gitDir = line[prefix.Length..].Trim();
                if (!Path.IsPathRooted(gitDir)) gitDir = Path.GetFullPath(Path.Combine(repo, gitDir));
            }

            var reflog = Path.Combine(gitDir, "logs", "refs", "stash");
            if (!File.Exists(reflog)) return 0;

            var count = 0;
            foreach (var l in File.ReadLines(reflog))
                if (l.Trim().Length > 0) count++;
            return count;
        }
        catch (Exception)
        {
            return 0; // contador de badge não justifica propagar erro de leitura
        }
    }

    // ----------------------------------------------------- arquivos alterados

    public static async Task<List<FileChange>> ChangesAsync(string repo) =>
        (await StatusAndChangesAsync(repo)).Files;

    /// <summary>
    /// Formato de "git status --porcelain=v2":
    ///   1 &lt;XY&gt; &lt;sub&gt; &lt;mH&gt; &lt;mI&gt; &lt;mW&gt; &lt;hH&gt; &lt;hI&gt; &lt;path&gt;
    ///   2 &lt;XY&gt; &lt;sub&gt; &lt;mH&gt; &lt;mI&gt; &lt;mW&gt; &lt;hH&gt; &lt;hI&gt; &lt;score&gt; &lt;path&gt;TAB&lt;origPath&gt;
    /// São 7 campos antes do caminho (8 no renomeado, por causa do score).
    /// </summary>
    public static List<FileChange> ParsePorcelainV2(string raw)
    {
        var list = new List<FileChange>();

        foreach (var line in raw.Split('\n'))
        {
            if (line.StartsWith("? "))
            {
                list.Add(new FileChange { Path = line[2..].Trim('\r'), Index = ".", Worktree = "?", Kind = ChangeKind.Untracked });
            }
            else if (line.StartsWith("u "))
            {
                // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path> — 9 campos antes do caminho
                var parts = line[2..].Split(' ', 10);
                if (parts.Length == 10)
                    list.Add(new FileChange { Path = parts[9].Trim('\r'), Index = "U", Worktree = "U", Kind = ChangeKind.Conflict });
            }
            else if ((line.StartsWith("1 ") || line.StartsWith("2 ")) && line.Length > 4)
            {
                var renamed = line[0] == '2';
                var rest = line[2..];
                var xy = rest[..2];
                var skip = renamed ? 8 : 7;
                var parts = rest.Split(' ', skip + 1);
                if (parts.Length <= skip) continue;

                var pathPart = parts[skip].Trim('\r');
                string path = pathPart;
                string? orig = null;
                if (renamed)
                {
                    var t = pathPart.Split('\t', 2);
                    path = t[0];
                    orig = t.Length > 1 ? t[1] : null;
                }
                list.Add(new FileChange
                {
                    Path = path,
                    OrigPath = orig,
                    Index = xy[..1],
                    Worktree = xy[1..2],
                    Kind = ChangeKind.Tracked,
                });
            }
        }

        return list.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<string> DiffFileAsync(
        string repo, string file, bool staged, bool untracked = false, int context = 3)
    {
        if (untracked && !staged) return NewFileDiff(repo, file);

        var args = new List<string> { "diff", "--no-color", "--no-ext-diff", $"-U{context}" };
        if (staged) args.Add("--cached");
        args.Add("--");
        args.Add(file);

        return await RunAsync(repo, args);
    }

    /// <summary>Tamanho a partir do qual o arquivo novo não é exibido inteiro.</summary>
    private const long MaxNewFileBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Diff de arquivo ainda não rastreado. O git só o produz com "--no-index /dev/null",
    /// que não existe no Windows — então montamos o patch, no mesmo formato que o
    /// "git apply" aceita para preparar o arquivo por bloco.
    /// </summary>
    private static string NewFileDiff(string repo, string file)
    {
        var full = Path.Combine(repo, file.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return "";

        var info = new FileInfo(full);
        if (info.Length > MaxNewFileBytes)
            return $"diff --git a/{file} b/{file}\nBinary files differ (arquivo novo com {info.Length / 1024} KB)\n";

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (IOException)
        {
            return ""; // arquivo em uso pelo editor: nada a mostrar agora
        }

        if (Array.IndexOf(bytes, (byte)0) >= 0)
            return $"diff --git a/{file} b/{file}\nBinary files /dev/null and b/{file} differ\n";

        var texto = new UTF8Encoding(false).GetString(bytes);
        var semQuebraFinal = texto.Length > 0 && !texto.EndsWith("\n");
        var linhas = texto.Split('\n');
        var total = linhas.Length;
        if (total > 0 && linhas[^1].Length == 0) total--; // quebra final não é linha

        var sb = new StringBuilder();
        sb.Append($"diff --git a/{file} b/{file}\n");
        sb.Append("new file mode 100644\n");
        sb.Append("--- /dev/null\n");
        sb.Append($"+++ b/{file}\n");
        sb.Append($"@@ -0,0 +1,{total} @@\n");

        for (var i = 0; i < total; i++)
            sb.Append('+').Append(linhas[i].TrimEnd('\r')).Append('\n');

        if (semQuebraFinal) sb.Append("\\ No newline at end of file\n");
        return sb.ToString();
    }

    public static Task StageAsync(string repo, IEnumerable<string> files)
    {
        var list = files.ToList();
        if (list.Count == 0) return Task.CompletedTask;
        var args = new List<string> { "add", "--" };
        args.AddRange(list);
        return RunAsync(repo, args);
    }

    public static Task UnstageAsync(string repo, IEnumerable<string> files)
    {
        var list = files.ToList();
        if (list.Count == 0) return Task.CompletedTask;
        var args = new List<string> { "restore", "--staged", "--" };
        args.AddRange(list);
        return RunAsync(repo, args);
    }

    /// <summary>Descarta alterações do working tree. Destrutivo: a confirmação fica na UI.</summary>
    public static async Task DiscardAsync(string repo, IEnumerable<string> tracked, IEnumerable<string> untracked)
    {
        var t = tracked.ToList();
        if (t.Count > 0)
        {
            var args = new List<string> { "restore", "--worktree", "--" };
            args.AddRange(t);
            await RunAsync(repo, args);
        }
        foreach (var f in untracked)
        {
            var full = Path.Combine(repo, f.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(full)) Directory.Delete(full, true);
            else if (File.Exists(full)) File.Delete(full);
        }
    }

    /// <summary>Aplica um patch (bloco isolado) no index.</summary>
    public static Task ApplyPatchAsync(string repo, string patch, bool cached, bool reverse)
    {
        // sem --unidiff-zero: o contexto de 3 linhas é o que garante que o bloco
        // seja aplicado no lugar certo
        var args = new List<string> { "apply", "--whitespace=nowarn" };
        if (cached) args.Add("--cached");
        if (reverse) args.Add("--reverse");
        args.Add("-");
        if (!patch.EndsWith("\n")) patch += "\n";
        return RunAsync(repo, args, patch);
    }

    public static Task<string> CommitAsync(string repo, string message, bool amend)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new GitException("mensagem de commit vazia");
        var args = new List<string> { "commit", "-m", message };
        if (amend) args.Add("--amend");
        return RunAsync(repo, args);
    }

    // ---------------------------------------------------------------- remoto

    public static Task<string> FetchAsync(string repo) =>
        RunAsync(repo, ComCredencial("fetch", "--all", "--prune"));

    public static Task<string> PullAsync(string repo, bool rebase) =>
        RunAsync(repo, rebase ? ComCredencial("pull", "--rebase") : ComCredencial("pull"));

    public static async Task<string> PushAsync(string repo, bool setUpstream)
    {
        if (!setUpstream) return await RunAsync(repo, ComCredencial("push"));
        var branch = (await Run(repo, "rev-parse", "--abbrev-ref", "HEAD")).Trim();
        return await RunAsync(repo, ComCredencial("push", "--set-upstream", "origin", branch));
    }

    // -------------------------------------------------------------- branches

    public static async Task<List<Branch>> BranchesAsync(string repo)
    {
        var raw = await Run(repo, "branch", "--all",
            "--format=%(refname:short)%1f%(HEAD)%1f%(upstream:short)%1f%(contents:subject)");

        return raw.Split('\n')
            .Where(l => l.Trim().Length > 0 && !l.Contains("->"))
            .Select(l => l.Split(US))
            .Where(f => f.Length >= 4)
            .Select(f => new Branch
            {
                Name = f[0],
                IsHead = f[1].Trim() == "*",
                IsRemote = f[0].StartsWith("remotes/") || f[0].StartsWith("origin/"),
                Upstream = f[2].Length == 0 ? null : f[2],
                Subject = f[3].Trim('\r'),
            })
            .ToList();
    }

    public static Task<string> SetRemoteUrlAsync(string repo, string url) =>
        Run(repo, "remote", "set-url", "origin", url);

    public static Task<string> CheckoutAsync(string repo, string name) => Run(repo, "checkout", name);

    /// <summary>
    /// Marca o repositório como confiável (safe.directory). O git bloqueia pastas de
    /// outro dono do Windows — comum em cópias de backup e discos que vieram de outra máquina.
    /// </summary>
    public static Task<string> TrustRepositoryAsync(string repo)
    {
        var caminho = Path.GetFullPath(repo).Replace('\\', '/').TrimEnd('/');
        // roda fora do repositório: é justamente o acesso a ele que está bloqueado
        return RunAsync(Path.GetTempPath(), new[] { "config", "--global", "--add", "safe.directory", caminho });
    }

    public static Task<string> CreateBranchAsync(string repo, string name, bool checkout) =>
        checkout ? Run(repo, "checkout", "-b", name) : Run(repo, "branch", name);

    // ----------------------------------------------------------------- stash

    public static async Task<List<StashEntry>> StashesAsync(string repo)
    {
        var raw = await Run(repo, "stash", "list", "--format=%gd%1f%s");
        return raw.Split('\n')
            .Where(l => l.Trim().Length > 0)
            .Select((l, i) =>
            {
                var f = l.Split(US);
                return new StashEntry
                {
                    Index = i,
                    Label = f.Length > 0 ? f[0] : "",
                    Subject = f.Length > 1 ? f[1].Trim('\r') : "",
                };
            })
            .ToList();
    }

    public static Task<string> StashPushAsync(string repo, string message, bool keepIndex)
    {
        var args = new List<string> { "stash", "push", "--include-untracked" };
        if (keepIndex) args.Add("--keep-index");
        if (!string.IsNullOrWhiteSpace(message))
        {
            args.Add("-m");
            args.Add(message);
        }
        return RunAsync(repo, args);
    }

    public static Task<string> StashApplyAsync(string repo, int index, bool drop) =>
        Run(repo, "stash", drop ? "pop" : "apply", $"stash@{{{index}}}");

    public static Task<string> StashDropAsync(string repo, int index) =>
        Run(repo, "stash", "drop", $"stash@{{{index}}}");

    // ------------------------------------------------------------- histórico

    public static async Task<List<Commit>> LogAsync(string repo, int limit, bool allBranches)
    {
        var args = new List<string> { "log", $"-{limit}", "--date-order", LogFormat };
        if (allBranches) args.Add("--all");
        var raw = await RunAsync(repo, args);

        return raw.Split(RS)
            .Where(r => r.Trim().Length > 0)
            .Select(ParseCommit)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
    }

    private static Commit? ParseCommit(string record)
    {
        var f = record.TrimStart('\n', '\r').Split(US);
        if (f.Length < 7) return null;
        return new Commit
        {
            Hash = f[0],
            Parents = f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
            Author = f[2],
            Email = f[3],
            Date = f[4],
            Refs = f[5].Split(", ", StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).ToList(),
            Subject = f[6],
        };
    }

    public static async Task<CommitDetail> CommitDetailAsync(string repo, string hash)
    {
        var meta = await Run(repo, "show", "--no-patch",
            "--pretty=format:%H%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%D%x1f%s%x1f%b", hash);

        var f = meta.Split(US);
        if (f.Length < 8) throw new GitException("commit não encontrado");

        var commit = ParseCommit(string.Join(US, f.Take(7))) ?? throw new GitException("commit ilegível");

        var numstat = await Run(repo, "show", "--numstat", "--format=", "-m", "--first-parent", hash);
        var names = await Run(repo, "show", "--name-status", "--format=", "-m", "--first-parent", hash);

        var statusOf = new Dictionary<string, string>();
        foreach (var line in names.Split('\n').Where(l => l.Trim().Length > 0))
        {
            var cols = line.Trim('\r').Split('\t');
            if (cols.Length >= 2) statusOf[cols[^1]] = cols[0][..1];
        }

        var files = numstat.Split('\n')
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Trim('\r').Split('\t'))
            .Where(c => c.Length >= 3)
            .Select(c => new CommitFile
            {
                Path = c[^1],
                Added = int.TryParse(c[0], out var a) ? a : 0,
                Removed = int.TryParse(c[1], out var r) ? r : 0,
                Status = statusOf.TryGetValue(c[^1], out var st) ? st : "M",
            })
            .ToList();

        return new CommitDetail { Commit = commit, Body = f[7].Trim(), Files = files };
    }

    public static Task<string> CommitFileDiffAsync(string repo, string hash, string file) =>
        Run(repo, "show", "--no-color", "--format=", "-m", "--first-parent", hash, "--", file);

    /// <summary>URL do remoto "origin"; vazio quando o repositório não tem remoto.</summary>
    public static async Task<string> RemoteUrlAsync(string repo)
    {
        try
        {
            return (await Run(repo, "remote", "get-url", "origin")).Trim();
        }
        catch (GitException)
        {
            return "";
        }
    }

    /// <summary>
    /// Endereço para abrir no navegador. Converte SSH em HTTPS e tira o usuário
    /// embutido na URL (https://user@github.com/...), que o navegador não precisa.
    /// </summary>
    public static string WebUrl(string remoteUrl)
    {
        var url = remoteUrl.Trim();
        if (url.Length == 0) return "";

        if (url.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            // git@github.com:owner/repo.git
            var sep = url.IndexOf(':');
            if (sep < 0) return "";
            url = "https://" + url[4..sep] + "/" + url[(sep + 1)..];
        }
        else if (url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url[6..];
        }

        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) url = url[..^4];

        // remove credencial embutida
        var esquema = url.IndexOf("://", StringComparison.Ordinal);
        if (esquema > 0)
        {
            var resto = url[(esquema + 3)..];
            var arroba = resto.IndexOf('@');
            var barra = resto.IndexOf('/');
            if (arroba > 0 && (barra < 0 || arroba < barra))
                url = url[..(esquema + 3)] + resto[(arroba + 1)..];
        }

        return url.TrimEnd('/');
    }

    /// <summary>Valida a pasta e devolve o nome sugerido (basename da raiz do repositório).</summary>
    public static async Task<string> InspectPathAsync(string path)
    {
        var top = (await Run(path, "rev-parse", "--show-toplevel")).Trim();
        var name = Path.GetFileName(top.TrimEnd('/', '\\'));
        return string.IsNullOrEmpty(name) ? top : name;
    }
}
