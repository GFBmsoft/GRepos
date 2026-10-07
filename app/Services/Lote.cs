using System;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;

namespace GRepos.Services;

public enum OperacaoEmLote { Obter, Puxar, Enviar, Trocar }

/// <summary>O que aconteceu num repositório: "ok", "pulado" (não se aplicava) ou "erro".</summary>
public sealed record ResultadoDoLote(string Situacao, string Mensagem)
{
    public static ResultadoDoLote Ok(string mensagem) => new("ok", mensagem);
    public static ResultadoDoLote Pulado(string motivo) => new("pulado", motivo);
    public static ResultadoDoLote Erro(string mensagem) => new("erro", mensagem);
}

/// <summary>
/// A mesma operação em vários repositórios — um grupo inteiro, ou os dois lados de um
/// par Origem × Destino. Cada repositório responde por si: a falha de um não para os
/// outros, e onde a operação não se aplica ele é pulado com o motivo.
/// </summary>
public static class Lote
{
    public static string Nome(OperacaoEmLote operacao) => operacao switch
    {
        OperacaoEmLote.Obter => "Obter",
        OperacaoEmLote.Puxar => "Puxar",
        OperacaoEmLote.Enviar => "Enviar",
        _ => "Trocar de branch",
    };

    /// <summary>O que a operação faz, dito antes de rodar.</summary>
    public static string Descricao(OperacaoEmLote operacao) => operacao switch
    {
        OperacaoEmLote.Obter => "Traz as novidades do remoto de cada repositório, sem mexer nos arquivos.",
        OperacaoEmLote.Puxar => "Avança a branch atual de cada repositório. Só avanço rápido: onde houver commit local " +
                                "ainda não enviado, o repositório fica como está e avisa.",
        OperacaoEmLote.Enviar => "Envia a branch atual dos repositórios que têm commits a enviar. Os outros são pulados.",
        _ => "Troca cada repositório para a branch informada. Quem não tem essa branch, nem local nem no remoto, é pulado.",
    };

    /// <summary>
    /// Roda a operação num repositório e devolve o resultado em vez de lançar: no lote,
    /// erro de um repositório é uma linha da lista, não o fim da operação.
    /// </summary>
    public static async Task<ResultadoDoLote> ExecutarAsync(string repo, OperacaoEmLote operacao, string branch = "")
    {
        try
        {
            switch (operacao)
            {
                case OperacaoEmLote.Obter:
                    await GitService.FetchAsync(repo);
                    return ResultadoDoLote.Ok("obtido");

                case OperacaoEmLote.Puxar:
                {
                    var status = await GitService.StatusAsync(repo);
                    if (status.Branch.Length == 0) return ResultadoDoLote.Pulado("HEAD solto, sem branch para puxar");
                    if (string.IsNullOrEmpty(status.Upstream)) return ResultadoDoLote.Pulado("a branch não tem par no remoto");

                    var saida = await GitService.PullSoAvancoAsync(repo);
                    return ResultadoDoLote.Ok(saida.Contains("Already up to date", StringComparison.OrdinalIgnoreCase)
                        ? "já estava em dia"
                        : "atualizado");
                }

                case OperacaoEmLote.Enviar:
                {
                    var status = await GitService.StatusAsync(repo);
                    if (status.Branch.Length == 0) return ResultadoDoLote.Pulado("HEAD solto, sem branch para enviar");

                    var semPar = string.IsNullOrEmpty(status.Upstream);
                    if (!semPar && status.Ahead == 0) return ResultadoDoLote.Pulado("nada a enviar");

                    await GitService.PushAsync(repo, semPar);
                    return ResultadoDoLote.Ok(semPar ? "branch criada no remoto" : $"{status.Ahead} commit(s) enviado(s)");
                }

                default:
                {
                    branch = branch.Trim();
                    if (branch.Length == 0) return ResultadoDoLote.Erro("informe a branch");

                    var atual = (await GitService.StatusAsync(repo)).Branch;
                    if (atual == branch) return ResultadoDoLote.Pulado("já está nela");

                    if (await GitService.RefExisteAsync(repo, "refs/heads/" + branch))
                    {
                        await GitService.CheckoutAsync(repo, branch);
                        return ResultadoDoLote.Ok($"saiu de {atual}");
                    }
                    if (await GitService.RefExisteAsync(repo, "refs/remotes/origin/" + branch))
                    {
                        await GitService.CheckoutRemotaAsync(repo, "origin/" + branch);
                        return ResultadoDoLote.Ok($"saiu de {atual}; branch local criada a partir do remoto");
                    }
                    return ResultadoDoLote.Pulado("não tem essa branch");
                }
            }
        }
        catch (Exception e)
        {
            return ResultadoDoLote.Erro(Resumir(e.Message));
        }
    }

    /// <summary>A linha que explica, não a saída inteira do git: a lista tem uma linha por repositório.</summary>
    public static string Resumir(string erro)
    {
        if (erro.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase) ||
            erro.Contains("Diverging branches", StringComparison.OrdinalIgnoreCase))
            return "tem commit local que o remoto não tem: puxe este repositório à mão";
        if (erro.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase))
            return "há alterações locais que a operação sobrescreveria: commite ou guarde (stash) antes";

        var linha = erro.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("fatal:") || l.StartsWith("error:"))
                    ?? erro.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0)
                    ?? "falhou";
        return linha.Length > 180 ? linha[..180] + "…" : linha;
    }
}
