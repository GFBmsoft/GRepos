using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

    /// <summary>
    /// Teto para operações de rede. Sem ele, um git esperando o credential manager
    /// deixa a barra inteira desabilitada para sempre.
    /// </summary>
    internal static TimeSpan TempoLimiteRede { get; set; } = TimeSpan.FromSeconds(75);

    public static Task<string> RunAsync(string repo, IEnumerable<string> args, string? stdin = null) =>
        RunWithEnvAsync(repo, args, stdin);

    /// <param name="env">Variáveis extras para o processo do git (ex.: desligar prompts).</param>
    public static Task<string> RunWithEnvAsync(
        string repo, IEnumerable<string> args, string? stdin = null, params (string Nome, string Valor)[] env) =>
        ExecutarAsync(repo, args, stdin, default, env);

    /// <summary>Operação de rede: leva o usuário ao credential manager e tem prazo.</summary>
    private static async Task<string> RedeAsync(string repo, params string[] args)
    {
        try
        {
            return await ExecutarAsync(repo, ComCredencial(repo, args), null, TempoLimiteRede,
                Array.Empty<(string, string)>());
        }
        catch (OperationCanceledException)
        {
            throw new GitException(
                "A operação passou de " + (int)TempoLimiteRede.TotalSeconds + " segundos e foi cancelada. " +
                "Costuma ser uma janela de login do Git Credential Manager esperando resposta — " +
                "procure-a na barra de tarefas, ou configure o usuário em Preferências → Autenticação.");
        }
    }

    private static async Task<string> ExecutarAsync(
        string repo, IEnumerable<string> args, string? stdin, TimeSpan tempoLimite,
        (string Nome, string Valor)[] env, bool conteudo = false, Encoding? encodingStdin = null)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            // conteúdo de arquivo: Latin1 guarda os bytes intactos para o TextoGit decidir
            StandardOutputEncoding = conteudo ? Encoding.Latin1 : Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = stdin is null ? null : encodingStdin ?? new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("-C");
        psi.ArgumentList.Add(repo);
        // sem isto "Impressão/" sai como "Impress\303\243o/": a tela mostra o escape e o
        // "git add" não acha o arquivo
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var a in args) psi.ArgumentList.Add(a);

        // Herdar estas variáveis de quem abriu o app (terminal, script, agente) impede o
        // credential manager de pedir login e quebra push/pull com "terminal prompts
        // disabled". Quem precisa delas passa em `env`, nunca por herança.
        psi.Environment.Remove("GIT_TERMINAL_PROMPT");
        psi.Environment.Remove("GCM_INTERACTIVE");
        psi.Environment.Remove("GIT_ASKPASS");
        psi.Environment.Remove("SSH_ASKPASS");

        foreach (var (nome, valor) in env) psi.Environment[nome] = valor;

        // Criar o processo custa alguns milissegundos e, numa varredura de cem repositórios,
        // isso somado travava a tela por um segundo. Fora da thread de quem chamou, a soma some.
        using var proc = await Task.Run(() => Process.Start(psi))
            ?? throw new GitException("não foi possível executar o git");

        if (stdin is not null)
        {
            await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();
        }

        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();

        string stdout;
        string stderr;

        if (tempoLimite > TimeSpan.Zero)
        {
            try
            {
                await proc.WaitForExitAsync().WaitAsync(tempoLimite);
            }
            catch (TimeoutException)
            {
                // derruba o git e o que ele abriu (o credential manager, por exemplo)
                try { proc.Kill(entireProcessTree: true); } catch (Exception) { /* já morreu */ }
                throw new OperationCanceledException();
            }

            // Processos-netos herdam os pipes: se um deles sobreviver, a leitura
            // trava mesmo com o git já encerrado. Daí o teto separado aqui.
            try
            {
                stdout = await outTask.WaitAsync(TimeSpan.FromSeconds(5));
                stderr = await errTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                try { proc.Kill(entireProcessTree: true); } catch (Exception) { /* já morreu */ }
                throw new OperationCanceledException();
            }
        }
        else
        {
            await proc.WaitForExitAsync();
            stdout = await outTask;
            stderr = await errTask;
        }

        if (proc.ExitCode != 0)
        {
            var msg = stderr.Trim();
            throw new GitException(msg.Length > 0 ? msg : $"git falhou (código {proc.ExitCode})");
        }
        return conteudo ? TextoGit.Decodificar(Encoding.Latin1.GetBytes(stdout)) : stdout;
    }

    private static Task<string> Run(string repo, params string[] args) => RunAsync(repo, args);

    /// <summary>Saída com conteúdo de arquivo (diff, show), que pode estar em ANSI.</summary>
    private static Task<string> RunConteudoAsync(string repo, IEnumerable<string> args) =>
        ExecutarAsync(repo, args, null, default, Array.Empty<(string, string)>(), conteudo: true);

    /// <summary>
    /// Bytes de um arquivo numa versão (<c>HEAD:caminho</c>, <c>:caminho</c> para o índice),
    /// como o checkout os gravaria — com a conversão de fim de linha, para a comparação
    /// com o arquivo do disco não acusar todas as linhas. Null quando a versão não existe.
    /// </summary>
    public static async Task<byte[]?> ConteudoBrutoAsync(string repo, string objeto)
    {
        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-C", repo, "cat-file", "--filters", objeto }) psi.ArgumentList.Add(a);

        using var proc = await Task.Run(() => Process.Start(psi))
            ?? throw new GitException("não foi possível executar o git");
        using var saida = new MemoryStream();
        var erro = proc.StandardError.ReadToEndAsync();
        await proc.StandardOutput.BaseStream.CopyToAsync(saida);
        await proc.WaitForExitAsync();
        await erro;
        return proc.ExitCode == 0 ? saida.ToArray() : null;
    }

    /// <summary>
    /// Chaves de config do repositório que casam com o padrão. Sem nenhuma, o git sai
    /// com código 1 — aqui isso é só um dicionário vazio.
    /// </summary>
    public static async Task<Dictionary<string, string>> ConfigListAsync(string repo, string padrao)
    {
        var saida = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string bruto;
        try { bruto = await Run(repo, "config", "--local", "--get-regexp", padrao); }
        catch (GitException) { return saida; }

        foreach (var linha in bruto.Split('\n'))
        {
            var l = linha.TrimEnd('\r');
            if (l.Length == 0) continue;
            var espaco = l.IndexOf(' ');
            if (espaco < 0) saida[l] = "";
            else saida[l[..espaco]] = l[(espaco + 1)..];
        }
        return saida;
    }

    public static Task<string> ConfigSetAsync(string repo, string chave, string valor) =>
        Run(repo, "config", "--local", chave, valor);

    /// <summary>
    /// Usuário do GitHub configurado nas preferências. Informá-lo ao credential manager
    /// é o que faz o token salvo ser encontrado: sem isso, o git procura a credencial
    /// de "https://github.com" sem conta e acaba abrindo a janela de login.
    /// </summary>
    public static string CredentialUser { get; set; } = "";

    /// <summary>Conta escolhida para cada repositório (pela pasta), quando não é a principal.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> ContasPorRepo =
        new(StringComparer.OrdinalIgnoreCase);

    private static string Chave(string repo)
    {
        try { return Path.GetFullPath(repo).TrimEnd('\\', '/'); }
        catch (Exception) { return repo.TrimEnd('\\', '/'); }
    }

    /// <summary>Define a conta de um repositório; vazio volta para a principal.</summary>
    public static void DefinirConta(string repo, string? conta)
    {
        if (string.IsNullOrWhiteSpace(conta)) ContasPorRepo.TryRemove(Chave(repo), out _);
        else ContasPorRepo[Chave(repo)] = conta.Trim();
    }

    /// <summary>Conta que o git usa neste repositório: a escolhida para ele ou a principal.</summary>
    public static string ContaDe(string repo) =>
        ContasPorRepo.TryGetValue(Chave(repo), out var conta) ? conta : CredentialUser;

    private static string[] ComCredencial(string repo, string[] args)
    {
        var conta = ContaDe(repo);
        if (string.IsNullOrWhiteSpace(conta)) return args;

        var completo = new string[args.Length + 2];
        completo[0] = "-c";
        completo[1] = $"credential.https://github.com.username={conta.Trim()}";
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

        var arquivos = ParsePorcelainV2(raw);
        // arquivos distintos: o mesmo caminho pode estar preparado e alterado de novo
        s.PendingFiles = arquivos.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        return (s, arquivos);
    }

    /// <summary>
    /// Conta os stashes lendo o reflog em disco. Chamar "git stash list" custava outro
    /// processo — metade do tempo do status — só para preencher um contador.
    /// </summary>
    private static int CountStashes(string repo)
    {
        try
        {
            if (GitDir(repo) is not { } gitDir) return 0;
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

    /// <summary>Pasta interna do git, sem chamar processo; null quando não há.</summary>
    public static string? GitDir(string repo)
    {
        var gitDir = Path.Combine(repo, ".git");
        if (Directory.Exists(gitDir)) return gitDir;
        if (!File.Exists(gitDir)) return null;

        // worktree ou submódulo: ".git" é um arquivo apontando para o diretório real
        var line = File.ReadAllText(gitDir).Trim();
        const string prefix = "gitdir:";
        if (!line.StartsWith(prefix)) return null;
        gitDir = line[prefix.Length..].Trim();
        return Path.IsPathRooted(gitDir) ? gitDir : Path.GetFullPath(Path.Combine(repo, gitDir));
    }

    // ------------------------------------------------- operação em andamento

    public enum Operacao { Nenhuma, Merge, Rebase, CherryPick, Revert }

    /// <summary>
    /// Merge, rebase, cherry-pick ou revert parado (em geral por conflito). Sai dos
    /// marcadores em disco, como o próprio git faz — sem custo de processo na recarga.
    /// </summary>
    public static Operacao OperacaoEmAndamento(string repo)
    {
        try
        {
            if (GitDir(repo) is not { } d) return Operacao.Nenhuma;
            if (Directory.Exists(Path.Combine(d, "rebase-merge")) || Directory.Exists(Path.Combine(d, "rebase-apply")))
                return Operacao.Rebase;
            if (File.Exists(Path.Combine(d, "CHERRY_PICK_HEAD"))) return Operacao.CherryPick;
            if (File.Exists(Path.Combine(d, "REVERT_HEAD"))) return Operacao.Revert;
            if (File.Exists(Path.Combine(d, "MERGE_HEAD"))) return Operacao.Merge;
        }
        catch (Exception) { /* leitura de marcador não justifica erro na tela */ }
        return Operacao.Nenhuma;
    }

    /// <summary>GIT_EDITOR=true: aceita a mensagem sugerida sem abrir editor nenhum.</summary>
    private static readonly (string, string)[] SemEditor = { ("GIT_EDITOR", "true") };

    public static Task<string> ContinuarOperacaoAsync(string repo, Operacao op) => op switch
    {
        // merge parado termina com um commit comum, com a mensagem que o git preparou
        Operacao.Merge => RunWithEnvAsync(repo, new[] { "commit", "--no-edit" }, null, SemEditor),
        Operacao.Rebase => RunWithEnvAsync(repo, new[] { "rebase", "--continue" }, null, SemEditor),
        Operacao.CherryPick => RunWithEnvAsync(repo, new[] { "cherry-pick", "--continue" }, null, SemEditor),
        Operacao.Revert => RunWithEnvAsync(repo, new[] { "revert", "--continue" }, null, SemEditor),
        _ => Task.FromResult(""),
    };

    public static Task<string> AbortarOperacaoAsync(string repo, Operacao op) => op switch
    {
        Operacao.Merge => Run(repo, "merge", "--abort"),
        Operacao.Rebase => Run(repo, "rebase", "--abort"),
        Operacao.CherryPick => Run(repo, "cherry-pick", "--abort"),
        Operacao.Revert => Run(repo, "revert", "--abort"),
        _ => Task.FromResult(""),
    };

    /// <summary>
    /// Resolve o conflito ficando inteiro com um dos lados. No rebase o git inverte os
    /// nomes — "ours" é a base onde se reaplica e "theirs" é o seu commit —, então
    /// "manter o meu" vira --theirs ali. Se o lado escolhido apagou o arquivo, resolver
    /// é apagá-lo também.
    /// </summary>
    public static async Task ResolverConflitoAsync(string repo, FileChange arquivo, bool manterMeu)
    {
        var ours = manterMeu != (OperacaoEmAndamento(repo) == Operacao.Rebase);
        var xy = arquivo.Conflito.Length == 2 ? arquivo.Conflito : "UU";
        var apagou = (ours ? xy[0] : xy[1]) == 'D';

        if (apagou)
        {
            await Run(repo, "rm", "--quiet", "--", arquivo.Path);
            return;
        }
        try
        {
            await Run(repo, "checkout", ours ? "--ours" : "--theirs", "--", arquivo.Path);
        }
        catch (GitException e) when (e.Message.Contains("does not have"))
        {
            // AU/UA: o arquivo nasceu só de um lado; ficar com o outro é não tê-lo
            await Run(repo, "rm", "--quiet", "--", arquivo.Path);
            return;
        }
        await Run(repo, "add", "--", arquivo.Path);
    }

    /// <summary>Marca como resolvido o que o usuário já acertou no editor.</summary>
    public static Task<string> MarcarResolvidoAsync(string repo, string path) => Run(repo, "add", "--", path);

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
                list.Add(new FileChange { Path = TextoGit.Caminho(line[2..].Trim('\r')), Index = ".", Worktree = "?", Kind = ChangeKind.Untracked });
            }
            else if (line.StartsWith("u "))
            {
                // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path> — 9 campos antes do caminho
                var parts = line[2..].Split(' ', 10);
                if (parts.Length == 10)
                    list.Add(new FileChange
                    {
                        Path = TextoGit.Caminho(parts[9].Trim('\r')), Index = "U", Worktree = "U",
                        Kind = ChangeKind.Conflict, Conflito = parts[0],
                    });
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
                    Path = TextoGit.Caminho(path),
                    OrigPath = orig is null ? null : TextoGit.Caminho(orig),
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

        return await RunConteudoAsync(repo, args);
    }

    /// <summary>
    /// Arquivo em conflito contra o HEAD. O "git diff" normal devolve o formato combinado
    /// (@@@), que o parser não lê; contra o HEAD os marcadores aparecem como linhas novas.
    /// </summary>
    public static Task<string> DiffConflitoAsync(string repo, string file) =>
        RunConteudoAsync(repo, new[] { "diff", "--no-color", "--no-ext-diff", "HEAD", "--", file });

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

        var texto = TextoGit.Decodificar(bytes);
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

    /// <summary>
    /// Volta o repositório inteiro a <paramref name="alvo"/> (HEAD ou o upstream): descarta
    /// preparados, alterações locais e arquivos novos. Ignorados ficam — sem <c>-x</c>, o
    /// <c>clean</c> não apaga build nem configuração local. Destrutivo: confirmação na UI.
    /// </summary>
    public static async Task ReverterTudoAsync(string repo, string alvo)
    {
        await Run(repo, "reset", "--hard", alvo);
        await Run(repo, "clean", "-fd");
    }

    public enum ModoReset { Soft, Mixed, Hard }

    /// <summary>Move a branch atual para <paramref name="hash"/>.</summary>
    public static Task<string> ResetAsync(string repo, string hash, ModoReset modo) =>
        Run(repo, "reset", "--" + modo.ToString().ToLowerInvariant(), hash);

    /// <summary>Aplica o commit na branch atual. Em merge, toma o primeiro pai como base.</summary>
    public static Task<string> CherryPickAsync(string repo, string hash, bool merge) =>
        merge ? Run(repo, "cherry-pick", "-m", "1", hash) : Run(repo, "cherry-pick", hash);

    /// <summary>Cria um commit que desfaz <paramref name="hash"/>, sem abrir editor.</summary>
    public static Task<string> RevertCommitAsync(string repo, string hash, bool merge) =>
        merge ? Run(repo, "revert", "--no-edit", "-m", "1", hash) : Run(repo, "revert", "--no-edit", hash);

    public static Task<string> CreateBranchAtAsync(string repo, string name, string hash) =>
        Run(repo, "branch", name, hash);

    public static Task<string> CreateTagAsync(string repo, string name, string hash) =>
        Run(repo, "tag", name, hash);

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
        // os bytes já vêm prontos; Latin1 só os carrega até o stdin sem alterar nenhum
        var bytes = TextoGit.CodificarPatch(patch, TextoGit.EncodingDoArquivo(ArquivoDoPatch(repo, patch)));
        return ExecutarAsync(repo, args, Encoding.Latin1.GetString(bytes), default,
            Array.Empty<(string, string)>(), encodingStdin: Encoding.Latin1);
    }

    /// <summary>Caminho em disco do arquivo de um patch, pela linha "+++ b/" (ou "--- a/").</summary>
    private static string ArquivoDoPatch(string repo, string patch)
    {
        string? rel = null;
        foreach (var linha in patch.Split('\n'))
        {
            var l = linha.TrimEnd('\r');
            if (l.StartsWith("+++ b/")) { rel = l[6..]; break; }
            if (l.StartsWith("--- a/")) rel ??= l[6..];
            if (l.StartsWith("@@")) break;
        }
        return rel is null ? "" : Path.Combine(repo, rel.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>Mensagem completa do último commit (assunto e corpo), sem a quebra final.</summary>
    public static async Task<string> UltimaMensagemAsync(string repo) =>
        (await RunConteudoAsync(repo, new[] { "log", "-1", "--format=%B" })).TrimEnd('\r', '\n');

    public static Task<string> CommitAsync(string repo, string message, bool amend)
    {
        if (string.IsNullOrWhiteSpace(message)) throw new GitException("mensagem de commit vazia");
        var args = new List<string> { "commit", "-m", message };
        if (amend) args.Add("--amend");
        return RunAsync(repo, args);
    }

    // ---------------------------------------------------------------- remoto

    // rede sempre por RedeAsync: é o que aplica o prazo e informa o usuário ao
    // credential manager

    /// <param name="todasAsTags">
    /// Traz todas as tags do remoto, não só as que apontam para commits trazidos. Com
    /// --prune junto não há risco: o git não poda tag que veio por --tags.
    /// </param>
    public static Task<string> FetchAsync(string repo, bool todasAsTags = false) =>
        todasAsTags
            ? RedeAsync(repo, "fetch", "--all", "--prune", "--tags")
            : RedeAsync(repo, "fetch", "--all", "--prune");

    /// <summary>
    /// Pull que resolve a branch sem vínculo: se o remoto tem uma de mesmo nome, vincula e
    /// puxa (é o caso da develop criada aqui quando já existia lá); se não tem, explica em
    /// vez de devolver o "no tracking information" do git.
    /// </summary>
    public static async Task<string> PullAsync(string repo, bool rebase)
    {
        var branch = (await Run(repo, "branch", "--show-current")).Trim();
        if (branch.Length > 0 && !await TemUpstreamAsync(repo))
        {
            await RedeAsync(repo, "fetch", "origin");
            if (!await RefExisteAsync(repo, "refs/remotes/origin/" + branch))
                throw new GitException(
                    $"A branch \"{branch}\" só existe no seu computador, então não há o que puxar. " +
                    "Use Enviar para criá-la no remoto; depois disso Puxar e Enviar funcionam normalmente.");
            await Run(repo, "branch", "--set-upstream-to=origin/" + branch);
        }
        return rebase ? await RedeAsync(repo, "pull", "--rebase") : await RedeAsync(repo, "pull");
    }

    /// <summary>
    /// Pull que só avança (fast-forward). É o das operações em lote: onde a branch local
    /// tem commit próprio o git recusa, em vez de abrir um merge — e possivelmente um
    /// conflito — em vários repositórios de uma vez.
    /// </summary>
    public static Task<string> PullSoAvancoAsync(string repo) => RedeAsync(repo, "pull", "--ff-only");

    public static async Task<bool> TemUpstreamAsync(string repo)
    {
        try
        {
            await Run(repo, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
            return true;
        }
        catch (GitException)
        {
            return false;
        }
    }

    public static async Task<bool> RefExisteAsync(string repo, string refCompleta)
    {
        try
        {
            await Run(repo, "rev-parse", "--verify", "--quiet", refCompleta);
            return true;
        }
        catch (GitException)
        {
            return false;
        }
    }

    /// <summary>Envia uma branch que não é a atual, já criando o vínculo com o remoto.</summary>
    public static Task<string> PushBranchAsync(string repo, string branch) =>
        RedeAsync(repo, "push", "--set-upstream", "origin", branch);

    /// <summary>
    /// "Trocar" numa remota (origin/x): vai para a branch local x, criando-a vinculada se
    /// não existir. Fazer checkout de "origin/x" direto deixaria o HEAD solto (detached),
    /// e um commit ali se perderia.
    /// </summary>
    public static async Task<string> CheckoutRemotaAsync(string repo, string remota)
    {
        var barra = remota.IndexOf('/');
        var local = barra > 0 ? remota[(barra + 1)..] : remota;
        if (!await RefExisteAsync(repo, "refs/heads/" + local))
            return await Run(repo, "checkout", "--track", remota);

        var saida = await Run(repo, "checkout", local);
        if (!await TemUpstreamAsync(repo)) await Run(repo, "branch", "--set-upstream-to=" + remota);
        return saida;
    }

    /// <summary>
    /// Deixa uma branch local para cada remota: cria as que faltam (vinculadas) e avança as
    /// que só estão atrás. Nunca mexe na branch atual nem em branch com commit próprio —
    /// só avanço rápido, conferido pelo update-ref com o valor antigo.
    /// </summary>
    public static async Task<(int Criadas, int Atualizadas)> SincronizarBranchesLocaisAsync(string repo)
    {
        var raw = await Run(repo, "for-each-ref", "--format=%(refname)%1f%(objectname)%1f%(upstream)%1f%(HEAD)",
            "refs/heads", "refs/remotes");
        var refs = raw.Split('\n').Select(l => l.TrimEnd('\r').Split(US)).Where(f => f.Length >= 4).ToList();

        var locais = refs.Where(f => f[0].StartsWith("refs/heads/"))
            .ToDictionary(f => f[0]["refs/heads/".Length..], f => f, StringComparer.Ordinal);
        var remotas = refs.Where(f => f[0].StartsWith("refs/remotes/") && !f[0].EndsWith("/HEAD"))
            .ToDictionary(f => f[0], f => f[1], StringComparer.Ordinal);

        var criadas = 0;
        var atualizadas = 0;
        // origin primeiro: com dois remotos tendo a mesma branch, ela vale
        foreach (var (remota, hash) in remotas.OrderBy(r => r.Key.StartsWith("refs/remotes/origin/") ? 0 : 1))
        {
            var curta = remota["refs/remotes/".Length..];
            var nome = curta[(curta.IndexOf('/') + 1)..];

            if (!locais.TryGetValue(nome, out var local))
            {
                try
                {
                    await Run(repo, "branch", "--track", nome, curta);
                    locais[nome] = new[] { "refs/heads/" + nome, hash, remota, " " };
                    criadas++;
                }
                catch (GitException) { /* nome inválido como branch local: segue */ }
                continue;
            }

            var ehAtual = local[3].Trim() == "*";
            if (ehAtual || local[2] != remota || local[1] == hash) continue;
            try
            {
                await Run(repo, "merge-base", "--is-ancestor", local[1], hash);
                await Run(repo, "update-ref", "refs/heads/" + nome, hash, local[1]);
                atualizadas++;
            }
            catch (GitException) { /* tem commit só local: não é avanço rápido, fica como está */ }
        }
        return (criadas, atualizadas);
    }

    /// <summary>
    /// Envia a branch atual. Também cria o vínculo quando a branch está ligada a outra de
    /// nome diferente — o caso de "feat/x" criada de origin/develop, que o git vincula à
    /// develop: o push simples recusava ("upstream ... does not match the name"), e com
    /// outra configuração mandaria a feature para dentro da develop.
    /// </summary>
    public static async Task<string> PushAsync(string repo, bool setUpstream)
    {
        var branch = (await Run(repo, "branch", "--show-current")).Trim();
        if (!setUpstream && branch.Length > 0) setUpstream = await UpstreamDeOutroNomeAsync(repo, branch);
        if (!setUpstream || branch.Length == 0) return await RedeAsync(repo, "push");
        return await RedeAsync(repo, "push", "--set-upstream", "origin", branch);
    }

    /// <summary>A branch está vinculada a uma remota de nome diferente (ex.: feat/x → develop)?</summary>
    public static async Task<bool> UpstreamDeOutroNomeAsync(string repo, string branch)
    {
        try
        {
            var merge = (await Run(repo, "config", "--get", $"branch.{branch}.merge")).Trim();
            return merge.Length > 0 && merge != "refs/heads/" + branch;
        }
        catch (GitException)
        {
            return false; // sem vínculo nenhum: o chamador já trata
        }
    }

    /// <summary>
    /// O que um pull request da branch precisa saber do repositório local: se ela já está
    /// no remoto com o próprio nome, quantos commits ainda não subiram, o assunto do último
    /// commit e as branches do remoto que podem ser o destino.
    /// </summary>
    public static async Task<(bool Enviada, int NaoEnviados, string Assunto, List<string> Destinos)> SituacaoParaPrAsync(
        string repo, string branch)
    {
        var destinos = (await Run(repo, "for-each-ref", "--format=%(refname)", "refs/remotes/origin"))
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("refs/remotes/origin/") && !l.EndsWith("/HEAD"))
            .Select(l => l["refs/remotes/origin/".Length..])
            .OrderBy(DestinoPreferido)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (branch.Length == 0) return (false, 0, "", destinos);

        // conta o nome, não o vínculo: "feat/x" criada de origin/develop tem upstream e
        // mesmo assim não existe no remoto
        var enviada = destinos.Contains(branch);
        var naoEnviados = 0;
        if (enviada)
            int.TryParse((await Run(repo, "rev-list", "--count", $"origin/{branch}..{branch}")).Trim(), out naoEnviados);

        var assunto = (await RunConteudoAsync(repo, new[] { "log", "-1", "--format=%s", branch })).Trim();
        return (enviada, naoEnviados, assunto, destinos);
    }

    /// <summary>
    /// Mescla <paramref name="origem"/> em <paramref name="destino"/>, trocando para o
    /// destino antes se ele não for a branch atual. Em conflito o git sai com erro e
    /// deixa o merge em andamento — é a faixa de Continuar/Abortar da aba Alterações.
    /// </summary>
    public static async Task<string> MesclarEmAsync(string repo, string origem, string destino)
    {
        var atual = (await Run(repo, "branch", "--show-current")).Trim();
        if (atual != destino) await Run(repo, "checkout", destino);
        return await Run(repo, "merge", "--no-edit", origem);
    }

    /// <summary>Reaplica os commits de <paramref name="branch"/> em cima de <paramref name="sobre"/>.</summary>
    public static Task<string> RebaseSobreAsync(string repo, string branch, string sobre) =>
        Run(repo, "rebase", sobre, branch);

    /// <summary>
    /// O que muda de <paramref name="a"/> para <paramref name="b"/> (branches, tags ou
    /// commits): quantos commits cada lado tem que o outro não tem, e os arquivos que
    /// diferem. Sem detecção de renomeação: cada arquivo aparece pelo próprio caminho.
    /// </summary>
    public static async Task<(int SoEmA, int SoEmB, List<CommitFile> Arquivos)> CompararAsync(string repo, string a, string b)
    {
        var contagem = (await Run(repo, "rev-list", "--left-right", "--count", $"{a}...{b}"))
            .Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var soEmA = contagem.Length > 0 && int.TryParse(contagem[0], out var x) ? x : 0;
        var soEmB = contagem.Length > 1 && int.TryParse(contagem[1], out var y) ? y : 0;

        var numstat = await Run(repo, "diff", "--no-renames", "--numstat", a, b);
        var names = await Run(repo, "diff", "--no-renames", "--name-status", a, b);

        var statusOf = new Dictionary<string, string>();
        foreach (var line in names.Split('\n').Where(l => l.Trim().Length > 0))
        {
            var cols = line.Trim('\r').Split('\t');
            if (cols.Length >= 2) statusOf[TextoGit.Caminho(cols[^1])] = cols[0][..1];
        }

        var arquivos = numstat.Split('\n')
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Trim('\r').Split('\t'))
            .Where(c => c.Length >= 3)
            .Select(c =>
            {
                var caminho = TextoGit.Caminho(c[^1]);
                return new CommitFile
                {
                    Path = caminho,
                    Added = int.TryParse(c[0], out var ad) ? ad : 0,
                    Removed = int.TryParse(c[1], out var rm) ? rm : 0,
                    Status = statusOf.TryGetValue(caminho, out var st) ? st : "M",
                };
            })
            .ToList();

        return (soEmA, soEmB, arquivos);
    }

    /// <summary>O diff de um arquivo entre as duas pontas da comparação.</summary>
    public static Task<string> CompararArquivoAsync(string repo, string a, string b, string file, int context = 3) =>
        RunConteudoAsync(repo, new[] { "diff", "--no-color", "--no-ext-diff", "--no-renames", $"-U{context}", a, b, "--", file });

    /// <summary>Branches (locais e remotas) e tags, para escolher as pontas de uma comparação.</summary>
    public static async Task<List<string>> RefsAsync(string repo) =>
        (await Run(repo, "for-each-ref", "--format=%(refname:short)", "refs/heads", "refs/remotes", "refs/tags"))
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.EndsWith("/HEAD") && l != "origin")
            .ToList();

    /// <summary>As branches de integração vêm na frente da lista de destinos.</summary>
    private static int DestinoPreferido(string nome) => nome switch
    {
        "develop" => 0,
        "main" or "master" => 1,
        _ => 2,
    };

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

    /// <summary>Teto do clone: repositório grande leva minutos, bem mais que o push.</summary>
    internal static TimeSpan TempoLimiteClone { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Clona <paramref name="url"/> em <paramref name="destino"/>. A conta vai ao
    /// credential manager como nas outras operações de rede.
    /// </summary>
    public static async Task CloneAsync(string pai, string url, string destino, string conta)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(conta))
        {
            args.Add("-c");
            args.Add($"credential.https://github.com.username={conta.Trim()}");
        }
        args.AddRange(new[] { "clone", "--", url, destino });

        try
        {
            await ExecutarAsync(pai, args, null, TempoLimiteClone, Array.Empty<(string, string)>());
        }
        catch (OperationCanceledException)
        {
            throw new GitException(
                "O clone passou de " + (int)TempoLimiteClone.TotalMinutes + " minutos e foi cancelado. " +
                "Se havia uma janela de login do Git Credential Manager esperando, procure-a na barra de tarefas.");
        }
    }

    public static Task<string> SetRemoteUrlAsync(string repo, string url) =>
        Run(repo, "remote", "set-url", "origin", url);

    public static Task<string> CheckoutAsync(string repo, string name) => Run(repo, "checkout", name);

    /// <summary>
    /// Linhas adicionadas e removidas pelos commits <b>do próprio usuário</b>. Merges ficam
    /// de fora: as mudanças deles já foram contadas nos commits de origem. Arquivo
    /// binário vem como "-" e não entra na conta.
    ///
    /// Antes contava os commits de todo mundo: no Financeiro eram 1,4 milhão de linhas,
    /// das quais 5 mil do usuário — e o saldo dos outros chegava a dar negativo.
    ///
    /// <paramref name="jaContados"/> é compartilhado entre os repositórios: o par Origem ×
    /// Destino nasce do mesmo histórico, e sem isso cada commit comum contava duas vezes.
    /// O filtro de autor vai para o próprio git, que nem calcula o diff de quem não é o
    /// usuário — por isso também ficou muito mais rápido.
    /// </summary>
    /// <param name="autores">Trechos que identificam o autor, como "&lt;email&gt;".</param>
    public static async Task<(long Adicionadas, long Removidas)> ContarLinhasAsync(
        string repo, IReadOnlyCollection<string> autores, ISet<string>? jaContados = null)
    {
        if (autores.Count == 0) return (0, 0);

        var args = new List<string>
        {
            "log", "--all", "--no-merges", "--numstat", "--format=%x01%H",
            "--fixed-strings", "--regexp-ignore-case",
        };
        foreach (var a in autores) args.Add("--author=" + a);

        return SomarNumstatPorCommit(await RunAsync(repo, args), jaContados ?? new HashSet<string>());
    }

    /// <summary>
    /// Soma o numstat commit a commit, pulando os que já foram contados. Cada commit
    /// começa com uma linha "\x01hash".
    /// </summary>
    internal static (long Adicionadas, long Removidas) SomarNumstatPorCommit(string saida, ISet<string> jaContados)
    {
        long mais = 0, menos = 0;
        var contando = false;

        foreach (var bruta in saida.Split('\n'))
        {
            var linha = bruta.TrimEnd('\r');
            if (linha.StartsWith('\x01'))
            {
                contando = jaContados.Add(linha[1..].Trim());
                continue;
            }
            if (!contando) continue;

            var partes = linha.Split('\t');
            if (partes.Length < 3) continue;

            if (long.TryParse(partes[0], out var a)) mais += a;
            if (long.TryParse(partes[1], out var r)) menos += r;
        }

        return (mais, menos);
    }

    /// <summary>
    /// Quem é o usuário nos commits: os e-mails do git (o global e o de cada
    /// repositório) e o e-mail "noreply" que o GitHub usa nas contas cadastradas.
    /// Entre &lt;&gt; o e-mail casa inteiro, e "outro.gabriel@..." não vira "gabriel@...".
    /// </summary>
    public static async Task<List<string>> IdentidadesAsync(IEnumerable<string> repos, IEnumerable<string> contas)
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task Ler(string pasta, params string[] args)
        {
            try
            {
                var e = (await RunAsync(pasta, args)).Trim();
                if (e.Length > 0) emails.Add("<" + e + ">");
            }
            catch (GitException) { /* sem e-mail configurado */ }
        }

        await Ler(Path.GetTempPath(), "config", "--global", "user.email");
        foreach (var r in repos) await Ler(r, "config", "user.email");

        // "12345+login@users.noreply.github.com" e "login@users.noreply.github.com"
        foreach (var c in contas.Where(c => !string.IsNullOrWhiteSpace(c)))
            emails.Add(c.Trim() + "@users.noreply.github.com>");

        return emails.ToList();
    }

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

    /// <summary>
    /// Busca por mensagem ou autor (nome/e-mail), sem diferenciar maiúsculas; um termo
    /// que pareça hash também acha o commit pelo prefixo. São duas consultas: --grep e
    /// --author juntos valem como "e" no git, e numa caixa de busca só se espera "ou".
    /// </summary>
    public static async Task<List<Commit>> SearchLogAsync(string repo, string termo, int limit, bool allBranches)
    {
        termo = termo.Trim();

        async Task<List<Commit>> Consulta(string filtro)
        {
            var args = new List<string>
            {
                "log", $"-{limit}", "--date-order", LogFormat, "-i", "--fixed-strings", filtro + termo,
            };
            if (allBranches) args.Add("--all");
            return (await RunAsync(repo, args)).Split(RS)
                .Where(r => r.Trim().Length > 0)
                .Select(ParseCommit)
                .Where(c => c is not null)
                .Select(c => c!)
                .ToList();
        }

        var porMensagem = await Consulta("--grep=");
        var porAutor = await Consulta("--author=");
        var lista = porMensagem.Concat(porAutor)
            .GroupBy(c => c.Hash).Select(g => g.First())
            .OrderByDescending(c => DateTimeOffset.TryParse(c.Date, out var d) ? d : DateTimeOffset.MinValue)
            .Take(limit)
            .ToList();

        if (termo.Length >= 4 && termo.All(Uri.IsHexDigit))
        {
            try
            {
                var porHash = (await RunAsync(repo, new[] { "log", "-1", LogFormat, termo + "^{commit}", "--" }))
                    .Split(RS).Where(r => r.Trim().Length > 0).Select(ParseCommit).FirstOrDefault();
                if (porHash is not null && lista.All(c => c.Hash != porHash.Hash)) lista.Insert(0, porHash);
            }
            catch (GitException) { /* não era hash de nenhum commit */ }
        }
        return lista;
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

    // ------------------------------------------------- histórico do arquivo

    /// <summary>
    /// Commits que mexeram no arquivo, seguindo renomeações. Cada um vem com o caminho
    /// que o arquivo tinha naquele commit — é por ele que se pede o diff.
    /// </summary>
    public static async Task<List<(Commit Commit, string Caminho)>> FileLogAsync(string repo, string file, int limit)
    {
        var raw = await RunAsync(repo, new[]
        {
            "-c", "core.quotePath=false", "log", "--follow", $"-{limit}", "--name-only",
            "--pretty=format:%x1e%H%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%D%x1f%s", "--", file,
        });
        return ParseFileLog(raw, file);
    }

    public static List<(Commit Commit, string Caminho)> ParseFileLog(string raw, string file)
    {
        var lista = new List<(Commit, string)>();
        foreach (var registro in raw.Split(RS))
        {
            var linhas = registro.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (linhas.Count == 0) continue;
            if (ParseCommit(linhas[0]) is not { } c) continue;
            lista.Add((c, linhas.Count > 1 ? TextoGit.Caminho(linhas[^1]) : file));
        }
        return lista;
    }

    /// <summary>Autoria linha a linha da versão em disco (inclui o que não foi commitado).</summary>
    public static async Task<List<BlameLine>> BlameAsync(string repo, string file) =>
        ParseBlame(await RunConteudoAsync(repo, new[] { "blame", "--line-porcelain", "--", file }));

    /// <summary>
    /// "--line-porcelain": cabeçalho "hash linhaOrig linhaFinal [n]", pares chave-valor
    /// (author, author-time, summary…) e o conteúdo numa linha começada por TAB.
    /// </summary>
    public static List<BlameLine> ParseBlame(string raw)
    {
        var lista = new List<BlameLine>();
        BlameLine? atual = null;
        foreach (var bruta in raw.Split('\n'))
        {
            var l = bruta.TrimEnd('\r');
            if (atual is null)
            {
                var p = l.Split(' ');
                if (p.Length >= 3 && p[0].Length == 40 && int.TryParse(p[2], out var n))
                    atual = new BlameLine { Hash = p[0], Linha = n };
                continue;
            }
            if (l.StartsWith('\t'))
            {
                atual.Texto = l[1..];
                lista.Add(atual);
                atual = null;
            }
            else if (l.StartsWith("author ")) atual.Autor = l[7..];
            else if (l.StartsWith("author-time ") && long.TryParse(l[12..], out var t)) atual.Quando = t;
            else if (l.StartsWith("summary ")) atual.Assunto = l[8..];
        }
        return lista;
    }

    public static Task<string> CommitFileDiffAsync(string repo, string hash, string file, int context = 3) =>
        RunConteudoAsync(repo, new[] { "show", "--no-color", "--format=", "-m", "--first-parent", $"-U{context}", hash, "--", file });

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
