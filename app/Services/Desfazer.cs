using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>O que o botão Desfazer vai fazer: a frase para confirmar e o comando do git.</summary>
public sealed class PlanoDesfazer
{
    public string Descricao { get; init; } = "";
    public string[] Args { get; init; } = Array.Empty<string>();

    /// <summary>Commit desfeito já está no remoto: o próximo envio exigirá push forçado.</summary>
    public bool JaEnviado { get; set; }
}

/// <summary>
/// "Desfazer a última ação", como o do GitKraken, lido do reflog: checkout volta para a
/// branch anterior; o resto (commit, emenda, reset, merge, pull, cherry-pick, revert,
/// rebase) devolve a branch à posição anterior. Commit e emenda voltam com --soft, para as
/// alterações reaparecerem preparadas; os demais com --keep, que recusa em vez de pisar em
/// alteração local.
/// </summary>
public static class Desfazer
{
    private static readonly Regex Checkout = new(@"^checkout: moving from (.+) to (.+)$", RegexOptions.Compiled);

    /// <param name="ultimoHead">Assunto da última entrada do reflog do HEAD.</param>
    /// <param name="ultimoRamo">Assunto da última entrada do reflog da branch (vazio se não há).</param>
    /// <param name="anteriorRamo">Hash da entrada anterior da branch; vazio quando não existe.</param>
    public static PlanoDesfazer? Interpretar(string ultimoHead, string ultimoRamo, string anteriorRamo)
    {
        var c = Checkout.Match(ultimoHead.Trim());
        if (c.Success)
        {
            var de = c.Groups[1].Value;
            return new PlanoDesfazer
            {
                Descricao = $"Voltar para {Rotulo(de)}, onde você estava antes da troca para {Rotulo(c.Groups[2].Value)}.",
                Args = new[] { "checkout", de },
            };
        }

        var s = ultimoRamo.Trim();
        if (s.Length == 0 || anteriorRamo.Length == 0) return null;

        var dois = s.IndexOf(": ", StringComparison.Ordinal);
        var tipo = dois < 0 ? s : s[..dois];
        var resto = dois < 0 ? "" : s[(dois + 2)..];

        // criação da branch e primeiro commit não têm "antes" para onde voltar
        if (tipo.StartsWith("branch") || tipo == "commit (initial)" || tipo == "clone") return null;

        string descricao;
        var soft = false;
        if (tipo == "commit")
        {
            descricao = $"Desfazer o commit \"{resto}\". As alterações dele voltam preparadas para commit.";
            soft = true;
        }
        else if (tipo == "commit (amend)")
        {
            descricao = $"Desfazer a emenda do último commit (\"{resto}\"). O commit volta a ser o de antes; o que foi emendado fica preparado.";
            soft = true;
        }
        else if (tipo.StartsWith("reset")) descricao = "Desfazer o último reset, voltando a branch para onde estava.";
        else if (tipo.StartsWith("merge") || tipo == "commit (merge)") descricao = "Desfazer o último merge.";
        else if (tipo.StartsWith("pull")) descricao = "Desfazer o último pull (os commits trazidos saem da branch).";
        else if (tipo == "cherry-pick") descricao = $"Desfazer o cherry-pick de \"{resto}\".";
        else if (tipo == "revert") descricao = $"Desfazer o commit de reversão \"{resto}\".";
        else if (tipo.StartsWith("rebase")) descricao = "Desfazer o último rebase, voltando a branch ao que era antes dele.";
        else descricao = $"Desfazer \"{s}\", voltando a branch para a posição anterior.";

        return new PlanoDesfazer
        {
            Descricao = descricao,
            Args = new[] { "reset", soft ? "--soft" : "--keep", anteriorRamo },
        };
    }

    private static string Rotulo(string r) => Regex.IsMatch(r, "^[0-9a-f]{40}$") ? $"o commit {r[..7]}" : r;

    private const char US = '\x1f';

    /// <summary>Branch recém-clonada ou com reflog desligado não tem histórico de posições.</summary>
    private static async Task<bool> TemReflogAsync(string repo, string ramo)
    {
        try
        {
            await GitService.RunAsync(repo, new[] { "reflog", "exists", "refs/heads/" + ramo });
            return true;
        }
        catch (GitException)
        {
            return false;
        }
    }

    /// <summary>Lê os reflogs e monta o plano; null quando não há o que desfazer.</summary>
    public static async Task<PlanoDesfazer?> PlanejarAsync(string repo)
    {
        var head = (await GitService.RunAsync(repo, new[] { "reflog", "-1", "--format=%gs" })).Trim();

        var ramo = (await GitService.RunAsync(repo, new[] { "branch", "--show-current" })).Trim();
        string ultimoRamo = "", anterior = "";
        if (ramo.Length > 0 && await TemReflogAsync(repo, ramo))
        {
            var linhas = (await GitService.RunAsync(repo,
                    new[] { "reflog", "show", "-2", $"--format=%H{US}%gs", "refs/heads/" + ramo }))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r').Split(US))
                .ToList();
            if (linhas.Count > 0 && linhas[0].Length > 1) ultimoRamo = linhas[0][1];
            if (linhas.Count > 1) anterior = linhas[1][0];
        }

        var plano = Interpretar(head, ultimoRamo, anterior);
        if (plano is null || plano.Args[0] != "reset") return plano;

        // o commit que vai sair da branch já está em algum remoto?
        var remotos = await GitService.RunAsync(repo, new[] { "branch", "-r", "--contains", "HEAD" });
        plano.JaEnviado = remotos.Trim().Length > 0;
        return plano;
    }
}
