using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GRepos.Services;

public enum AcaoRebase { Manter, Renomear, Juntar, JuntarSemMensagem, Apagar }

public sealed class ItemRebase
{
    public string Hash { get; init; } = "";
    public string Assunto { get; init; } = "";

    /// <summary>Mensagem completa original; a nova só vale com <see cref="AcaoRebase.Renomear"/>.</summary>
    public string Mensagem { get; init; } = "";
    public string NovaMensagem { get; set; } = "";
    public AcaoRebase Acao { get; set; }
}

/// <summary>
/// Rebase interativo sem editor: a lista vira o "todo" do git, entregue por
/// GIT_SEQUENCE_EDITOR (um cp por cima do arquivo que o git abriria). Renomear não usa o
/// "reword", que abriria editor: é um "pick" seguido de "exec git commit --amend -F".
/// Juntar usa a mensagem combinada que o git monta (GIT_EDITOR=true aceita como está).
/// </summary>
public static class RebaseInterativo
{
    private const char US = '\x1f';
    private const char RS = '\x1e';

    /// <summary>
    /// Commits de <paramref name="desde"/> (incluso) até o HEAD, do mais antigo ao mais
    /// novo — a ordem do todo. Recusa merge no caminho: o rebase os achataria.
    /// </summary>
    public static async Task<List<ItemRebase>> ListarAsync(string repo, string desde)
    {
        try
        {
            await GitService.RunAsync(repo, new[] { "merge-base", "--is-ancestor", desde, "HEAD" });
        }
        catch (GitException)
        {
            throw new GitException("Esse commit não faz parte da branch atual: só dá para reorganizar o caminho até o HEAD.");
        }

        var raiz = await EhRaizAsync(repo, desde);
        var raw = await GitService.RunAsync(repo, new[]
        {
            "log", "--reverse", "--format=%H%x1f%P%x1f%s%x1f%B%x1e", raiz ? "HEAD" : desde + "^..HEAD",
        });

        var itens = new List<ItemRebase>();
        foreach (var reg in raw.Split(RS))
        {
            var f = reg.TrimStart('\r', '\n').Split(US);
            if (f.Length < 4) continue;
            if (f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1)
                throw new GitException("Há um merge entre esse commit e o HEAD; reorganizar desfaria o merge. Escolha um commit depois dele.");
            var msg = f[3].TrimEnd('\r', '\n');
            itens.Add(new ItemRebase { Hash = f[0], Assunto = f[2], Mensagem = msg, NovaMensagem = msg });
        }
        return itens;
    }

    private static async Task<bool> EhRaizAsync(string repo, string hash)
    {
        var pais = await GitService.RunAsync(repo, new[] { "rev-list", "--parents", "-n", "1", hash });
        return pais.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 1;
    }

    /// <summary>O que impede aplicar a lista; null quando está tudo certo.</summary>
    public static string? Validar(IReadOnlyList<ItemRebase> doMaisAntigo)
    {
        var mantidos = doMaisAntigo.Where(i => i.Acao != AcaoRebase.Apagar).ToList();
        if (mantidos.Count == 0) return "Todos os commits seriam apagados. Para isso, use Resetar.";
        if (mantidos[0].Acao is AcaoRebase.Juntar or AcaoRebase.JuntarSemMensagem)
            return $"\"{mantidos[0].Assunto}\" não tem commit anterior na lista para juntar.";
        foreach (var i in doMaisAntigo.Where(i => i.Acao == AcaoRebase.Renomear))
            if (string.IsNullOrWhiteSpace(i.NovaMensagem)) return $"A nova mensagem de \"{i.Assunto}\" está vazia.";
        return null;
    }

    /// <param name="arquivoDaMensagem">Onde gravar a mensagem nova do item; devolve o caminho.</param>
    public static string MontarTodo(IReadOnlyList<ItemRebase> doMaisAntigo, Func<ItemRebase, string> arquivoDaMensagem)
    {
        var sb = new StringBuilder();
        foreach (var i in doMaisAntigo)
        {
            switch (i.Acao)
            {
                case AcaoRebase.Apagar: sb.Append("drop ").Append(i.Hash).Append('\n'); break;
                case AcaoRebase.Juntar: sb.Append("squash ").Append(i.Hash).Append('\n'); break;
                case AcaoRebase.JuntarSemMensagem: sb.Append("fixup ").Append(i.Hash).Append('\n'); break;
                case AcaoRebase.Renomear:
                    sb.Append("pick ").Append(i.Hash).Append('\n');
                    sb.Append("exec git commit --amend --only --no-verify --allow-empty -F '")
                      .Append(arquivoDaMensagem(i).Replace('\\', '/')).Append("'\n");
                    break;
                default: sb.Append("pick ").Append(i.Hash).Append('\n'); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Algum desses commits já está num remoto? Então o envio vai exigir push forçado.</summary>
    public static async Task<bool> JaEnviadoAsync(string repo, string maisAntigo) =>
        (await GitService.RunAsync(repo, new[] { "branch", "-r", "--contains", maisAntigo })).Trim().Length > 0;

    public static async Task<string> AplicarAsync(string repo, IReadOnlyList<ItemRebase> doMaisAntigo)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "grepos-rebase-" + Path.GetRandomFileName());
        Directory.CreateDirectory(pasta);
        try
        {
            var n = 0;
            var todo = MontarTodo(doMaisAntigo, i =>
            {
                var arq = Path.Combine(pasta, $"msg{++n}.txt");
                File.WriteAllText(arq, i.NovaMensagem.Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
                return arq;
            });
            var arquivoTodo = Path.Combine(pasta, "todo.txt");
            File.WriteAllText(arquivoTodo, todo, new UTF8Encoding(false));

            var primeiro = doMaisAntigo[0].Hash;
            var args = new List<string> { "rebase", "-i", "--autostash" };
            args.Add(await EhRaizAsync(repo, primeiro) ? "--root" : primeiro + "^");

            return await GitService.RunWithEnvAsync(repo, args, null,
                ("GIT_SEQUENCE_EDITOR", $"cp '{arquivoTodo.Replace('\\', '/')}'"),
                ("GIT_EDITOR", "true"));
        }
        finally
        {
            // se o rebase parou em conflito, os "exec" seguintes ainda leem as mensagens:
            // a pasta só pode sumir quando o git terminou de vez
            if (GitService.OperacaoEmAndamento(repo) == GitService.Operacao.Nenhuma)
                try { Directory.Delete(pasta, true); } catch (Exception) { /* temporário */ }
        }
    }
}
