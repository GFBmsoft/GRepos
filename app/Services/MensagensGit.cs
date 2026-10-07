using System;
using System.Linq;
using System.Text.RegularExpressions;
using GRepos.Models;

namespace GRepos.Services;

/// <summary>Um aviso pronto para a tela: o título em destaque e a linha que explica.</summary>
public sealed record Aviso(string Titulo, string Detalhe = "");

/// <summary>
/// O que o git diz, em português e do jeito que interessa a quem clicou no botão. O
/// resultado de obter, puxar e enviar sai dos números do repositório antes e depois —
/// não do texto do git, que muda com a versão e vem em inglês. O texto dele só é lido
/// para os erros, onde não há outra fonte.
/// </summary>
public static class MensagensGit
{
    private static string Commits(int n) => n == 1 ? "1 commit" : $"{n} commits";

    /// <summary>"Obter" não muda os arquivos: o que importa é o que ficou para puxar.</summary>
    public static Aviso Obtido(RepoStatus? depois)
    {
        if (depois is null) return new Aviso("Obter concluído");
        if (string.IsNullOrEmpty(depois.Upstream))
            return new Aviso("Obter concluído", $"A branch {depois.Branch} ainda não tem par no remoto.");

        return depois.Behind > 0
            ? new Aviso("Obter concluído", $"Há {Commits(depois.Behind)} no remoto para puxar em {depois.Branch}.")
            : new Aviso("Obter concluído", $"Nada novo no remoto para {depois.Branch}.");
    }

    /// <summary>Quantos commits chegaram: os que faltavam antes menos os que ainda faltam.</summary>
    public static Aviso Puxado(RepoStatus? antes, RepoStatus? depois)
    {
        if (depois is null) return new Aviso("Puxar concluído");

        var mudou = antes is not null && antes.Head.Length > 0 && antes.Head != depois.Head;
        if (!mudou) return new Aviso("Puxar concluído", $"{depois.Branch} já estava em dia com o remoto.");

        var chegaram = Math.Max(0, (antes?.Behind ?? 0) - depois.Behind);
        return new Aviso("Puxar concluído", chegaram > 0
            ? $"{Commits(chegaram)} {(chegaram == 1 ? "novo" : "novos")} em {depois.Branch}."
            : $"{depois.Branch} atualizada com o remoto.");
    }

    public static Aviso Enviado(RepoStatus? antes, RepoStatus? depois)
    {
        if (depois is null) return new Aviso("Enviar concluído");

        // a branch não existia no remoto: o envio a criou lá
        if (antes is not null && string.IsNullOrEmpty(antes.Upstream) && !string.IsNullOrEmpty(depois.Upstream))
            return new Aviso("Enviar concluído", $"Branch {depois.Branch} criada no remoto.");

        var enviados = Math.Max(0, (antes?.Ahead ?? 0) - depois.Ahead);
        return enviados > 0
            ? new Aviso("Enviar concluído", $"{Commits(enviados)} {(enviados == 1 ? "enviado" : "enviados")} de {depois.Branch}.")
            : new Aviso("Enviar concluído", "Nada a enviar: o remoto já estava em dia.");
    }

    /// <summary>
    /// A última linha que o git escreveu, traduzida quando é uma das frases de sempre.
    /// Serve às operações que não têm um resumo próprio (desfazer, trocar de branch).
    /// </summary>
    public static string Traduzir(string saida)
    {
        var linha = saida.Trim().Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        if (linha.Length == 0) return "";

        if (linha.StartsWith("Already up to date", StringComparison.OrdinalIgnoreCase)) return "Já estava em dia.";
        if (linha.StartsWith("Everything up-to-date", StringComparison.OrdinalIgnoreCase)) return "Nada a enviar: o remoto já estava em dia.";
        if (linha.StartsWith("Fast-forward", StringComparison.OrdinalIgnoreCase)) return "Branch avançada.";

        var m = Regex.Match(linha, @"^Switched to (?:a new )?branch '(.+)'");
        if (m.Success) return $"Agora em {m.Groups[1].Value}.";

        m = Regex.Match(linha, @"^Your branch is up to date with '(.+)'");
        if (m.Success) return $"Em dia com {m.Groups[1].Value}.";

        m = Regex.Match(linha, @"^Your branch is behind '(.+)' by (\d+) commit");
        if (m.Success) return $"{Commits(int.Parse(m.Groups[2].Value))} atrás de {m.Groups[1].Value}.";

        m = Regex.Match(linha, @"^Your branch is ahead of '(.+)' by (\d+) commit");
        if (m.Success) return $"{Commits(int.Parse(m.Groups[2].Value))} à frente de {m.Groups[1].Value}.";

        m = Regex.Match(linha, @"^branch '(.+)' set up to track '(.+)'", RegexOptions.IgnoreCase);
        if (m.Success) return $"Branch {m.Groups[1].Value} vinculada a {m.Groups[2].Value}.";

        m = Regex.Match(linha, @"^HEAD is now at (\w+) (.*)$");
        if (m.Success) return $"Agora em {m.Groups[1].Value}: {m.Groups[2].Value}";

        m = Regex.Match(linha, @"^(\d+) files? changed(?:, (\d+) insertions?\(\+\))?(?:, (\d+) deletions?\(-\))?");
        if (m.Success)
        {
            var arquivos = int.Parse(m.Groups[1].Value);
            var mais = m.Groups[2].Success ? m.Groups[2].Value : "0";
            var menos = m.Groups[3].Success ? m.Groups[3].Value : "0";
            return $"{arquivos} {(arquivos == 1 ? "arquivo alterado" : "arquivos alterados")}, +{mais} −{menos}.";
        }

        if (linha.StartsWith("Merge made by", StringComparison.OrdinalIgnoreCase)) return "Merge feito.";
        if (linha.StartsWith("Successfully rebased", StringComparison.OrdinalIgnoreCase)) return "Rebase concluído.";

        return linha; // frase que não conhecemos: melhor a original do que nenhuma
    }

    /// <summary>A mensagem veio crua do git (em inglês), e não de um texto que o app escreveu.</summary>
    public static bool PareceDoGit(string mensagem) =>
        new[] { "fatal:", "error:", "hint:", "rejected", "CONFLICT" }
            .Any(marca => mensagem.Contains(marca, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// O erro do git explicado. O que ele escreveu fica no detalhe quando a causa não é
    /// uma das conhecidas — esconder a mensagem original deixaria o usuário sem pista.
    /// </summary>
    /// <param name="operacao">"obter", "puxar", "enviar"… no infinitivo, para o título.</param>
    public static Aviso Erro(string operacao, string mensagem)
    {
        var titulo = $"Não foi possível {operacao}";
        bool Tem(string trecho) => mensagem.Contains(trecho, StringComparison.OrdinalIgnoreCase);

        // mensagens que o próprio app já escreve em português passam como estão
        if (!PareceDoGit(mensagem)) return new Aviso(titulo, mensagem.Trim());

        if (Tem("non-fast-forward") || Tem("fetch first") || Tem("Updates were rejected"))
            return new Aviso(titulo, "O remoto tem commits que você ainda não tem. Use Puxar e envie de novo.");
        // "unable to access" não entra: o git escreve isso também em 403, 404 e credencial recusada
        if (Tem("Could not resolve host") || Tem("Failed to connect") || Tem("Connection timed out") || Tem("Could not connect"))
            return new Aviso(titulo, "Sem conexão com o servidor. Confira a internet ou a VPN e tente de novo.");
        if (Tem("Authentication failed") || Tem("could not read Username") || Tem("Invalid username or"))
            return new Aviso(titulo, "O GitHub não aceitou a credencial. Confira o usuário e o token em Preferências → Contas.");
        if (Tem("Permission denied") || Tem("Write access to repository not granted") || Tem("returned error: 403"))
            return new Aviso(titulo, "Sua conta não tem permissão de escrita neste repositório.");
        if (Tem("Repository not found") || Tem("returned error: 404"))
            return new Aviso(titulo, "Repositório não encontrado no remoto — ou a conta em uso não o enxerga.");
        if (Tem("would be overwritten"))
            return new Aviso(titulo, "Há alterações locais que seriam sobrescritas. Faça o commit ou guarde-as (Esconder) antes.");
        if (Tem("CONFLICT") || Tem("Automatic merge failed"))
            return new Aviso(titulo, "Parou em conflito. Resolva os arquivos na aba Alterações e use Continuar; para desistir, Abortar.");
        if (Tem("Not possible to fast-forward") || Tem("divergent branches") || Tem("Need to specify how to reconcile"))
            return new Aviso(titulo, "A branch local e a remota seguiram caminhos diferentes: há commits dos dois lados.");
        if (Tem("no upstream") || Tem("no tracking information"))
            return new Aviso(titulo, "Esta branch ainda não tem par no remoto. Use Enviar para criá-la lá.");
        if (Tem("unrelated histories"))
            return new Aviso(titulo, "As duas branches não têm história em comum.");
        if (Tem("index.lock"))
            return new Aviso(titulo, "Outra operação do git está em andamento neste repositório. Espere terminar e tente de novo.");

        // causa desconhecida: a primeira linha útil do git, sem os prefixos dele
        var linha = mensagem.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("fatal:") || l.StartsWith("error:")) ?? mensagem.Trim();
        linha = Regex.Replace(linha, @"^(fatal|error):\s*", "");
        return new Aviso(titulo, linha.Length > 0 ? char.ToUpperInvariant(linha[0]) + linha[1..] : "");
    }
}
