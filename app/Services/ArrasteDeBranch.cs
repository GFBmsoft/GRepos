using System;
using System.Collections.Generic;

namespace GRepos.Services;

/// <summary>Uma branch como alvo ou origem de um arraste: local ou do remoto.</summary>
public sealed record RefDeBranch(string Nome, bool Remota)
{
    /// <summary>Nome sem o remoto: "origin/feat/x" é "feat/x".</summary>
    public string NomeLocal => Remota && Nome.IndexOf('/') is var i and > 0 ? Nome[(i + 1)..] : Nome;
}

public enum AcaoDeArraste { Mesclar, Rebase, PullRequest }

/// <summary>Uma opção do menu que abre ao soltar; indisponível vem com o motivo.</summary>
public sealed record OpcaoDeArraste(AcaoDeArraste Acao, string Rotulo, string Comandos, bool Disponivel, string Motivo = "");

/// <summary>
/// Soltar uma branch sobre outra, como no GitKraken: mesclar a arrastada na de baixo,
/// reaplicá-la sobre ela (rebase) ou abrir o pull request de uma para a outra. Aqui fica
/// só a decisão do que cabe em cada par; quem roda é o <see cref="GitService"/>.
/// </summary>
public static class ArrasteDeBranch
{
    /// <summary>
    /// Branch a partir de uma ref do log ("HEAD -> main", "feat/x", "origin/feat/x").
    /// Tag e "origin/HEAD" não são branch e devolvem null.
    /// </summary>
    public static RefDeBranch? DaRef(string refDoLog)
    {
        var r = refDoLog.Trim();
        if (r.Length == 0 || r.StartsWith("tag:") || r == "HEAD") return null;
        if (r.StartsWith("HEAD -> ")) r = r["HEAD -> ".Length..];
        if (r.EndsWith("/HEAD")) return null;

        return new RefDeBranch(r, r.StartsWith("origin/"));
    }

    /// <summary>A branch de um commit: a local na frente da remota, que só se usa na falta dela.</summary>
    public static RefDeBranch? DoCommit(IEnumerable<string> refs)
    {
        RefDeBranch? remota = null;
        foreach (var r in refs)
        {
            if (DaRef(r) is not { } b) continue;
            if (!b.Remota) return b;
            remota ??= b;
        }
        return remota;
    }

    public static IReadOnlyList<OpcaoDeArraste> Opcoes(RefDeBranch origem, RefDeBranch destino, bool temGitHub)
    {
        var opcoes = new List<OpcaoDeArraste>();
        if (origem.Nome == destino.Nome) return opcoes;

        // a mesma branch dos dois lados do remoto ("x" e "origin/x") é puxar ou enviar
        var mesmaBranch = origem.NomeLocal == destino.NomeLocal;

        opcoes.Add(destino.Remota
            ? new OpcaoDeArraste(AcaoDeArraste.Mesclar, $"Mesclar {origem.Nome} em {destino.Nome}", "",
                false, "o destino é uma branch do remoto: mescle na local ou abra um pull request")
            : new OpcaoDeArraste(AcaoDeArraste.Mesclar, $"Mesclar {origem.Nome} em {destino.Nome}",
                $"git checkout {destino.Nome}\ngit merge {origem.Nome}", true));

        opcoes.Add(origem.Remota
            ? new OpcaoDeArraste(AcaoDeArraste.Rebase, $"Rebase de {origem.Nome} sobre {destino.Nome}", "",
                false, "não se reescreve uma branch do remoto: arraste a local")
            : new OpcaoDeArraste(AcaoDeArraste.Rebase, $"Rebase de {origem.Nome} sobre {destino.Nome}",
                $"git rebase {destino.Nome} {origem.Nome}", true));

        if (temGitHub)
            opcoes.Add(mesmaBranch
                ? new OpcaoDeArraste(AcaoDeArraste.PullRequest,
                    $"Abrir pull request de {origem.NomeLocal} para {destino.NomeLocal}", "",
                    false, "origem e destino são a mesma branch")
                : new OpcaoDeArraste(AcaoDeArraste.PullRequest,
                    $"Abrir pull request de {origem.NomeLocal} para {destino.NomeLocal}…", "", true));

        return opcoes;
    }

    /// <summary>O que confirmar antes de rodar: os comandos e o que eles mudam.</summary>
    public static string Confirmacao(OpcaoDeArraste opcao, RefDeBranch origem, RefDeBranch destino) => opcao.Acao switch
    {
        AcaoDeArraste.Mesclar =>
            $"Trazer os commits de {origem.Nome} para dentro de {destino.Nome}.\n\n" +
            $"{opcao.Comandos}\n\n" +
            $"A branch atual passa a ser {destino.Nome}. Se der conflito, o merge para e você resolve na aba Alterações.",
        AcaoDeArraste.Rebase =>
            $"Reaplicar os commits de {origem.Nome} em cima de {destino.Nome}.\n\n" +
            $"{opcao.Comandos}\n\n" +
            $"Isso reescreve o histórico de {origem.Nome}: se ela já foi enviada, o próximo envio vai " +
            "precisar ser forçado. A branch atual passa a ser ela.",
        _ => "",
    };
}
