using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GRepos.Services;

public enum TipoBranch { Principal, Develop, Feature, Fix, Release, Outra }

/// <summary>
/// Configuração do git-flow, nas mesmas chaves que o SourceTree e o git-flow AVH gravam
/// (<c>gitflow.branch.*</c> e <c>gitflow.prefix.*</c>) — quem já inicializou por lá
/// não precisa refazer aqui.
/// </summary>
public sealed record GitFlowConfig
{
    public string Master { get; init; } = "master";
    public string Develop { get; init; } = "develop";
    public string Feature { get; init; } = "feat/";
    public string Release { get; init; } = "release/";
    public string Hotfix { get; init; } = "fix/";
    public string VersionTag { get; init; } = "";

    /// <summary>Verdadeiro quando o repositório já tem as chaves gravadas.</summary>
    public bool Inicializado { get; init; }
}

/// <summary>
/// Git-flow sem o plugin: as ações são checkout, merge e tag do git comum, que é tudo o
/// que o git-flow faz por baixo. O plugin não vem no Git for Windows e não precisa.
/// </summary>
public static class GitFlow
{
    // nomes de pasta que valem mesmo quando a configuração diz outra coisa: o
    // Financeiro tem "imp/" configurado, mas as branches de verdade são feat/ e fix/
    private static readonly string[] PastasFeature = { "feat", "feature", "features", "imp" };
    private static readonly string[] PastasFix = { "fix", "hotfix", "bugfix", "defeito", "bug" };
    private static readonly string[] PastasRelease = { "release", "releases" };

    /// <summary>Nome sem o remoto na frente: "origin/feat/x" vira "feat/x".</summary>
    public static string SemRemoto(string nome, bool remota)
    {
        if (!remota) return nome;
        var barra = nome.IndexOf('/');
        return barra > 0 ? nome[(barra + 1)..] : nome;
    }

    /// <summary>Pasta da branch ("feat" em "feat/estoque"); vazio quando não tem.</summary>
    public static string Pasta(string nome)
    {
        var barra = nome.LastIndexOf('/');
        return barra > 0 ? nome[..barra] : "";
    }

    public static TipoBranch Classificar(string nome, GitFlowConfig? cfg = null)
    {
        cfg ??= new GitFlowConfig();
        if (string.IsNullOrEmpty(nome)) return TipoBranch.Outra;

        if (Igual(nome, cfg.Master) || Igual(nome, "main") || Igual(nome, "master")) return TipoBranch.Principal;
        if (Igual(nome, cfg.Develop) || Igual(nome, "develop") || Igual(nome, "dev")) return TipoBranch.Develop;

        if (Comeca(nome, cfg.Feature)) return TipoBranch.Feature;
        if (Comeca(nome, cfg.Hotfix)) return TipoBranch.Fix;
        if (Comeca(nome, cfg.Release)) return TipoBranch.Release;

        var pasta = nome.Split('/')[0];
        if (PastasFeature.Contains(pasta, StringComparer.OrdinalIgnoreCase)) return TipoBranch.Feature;
        if (PastasFix.Contains(pasta, StringComparer.OrdinalIgnoreCase)) return TipoBranch.Fix;
        if (PastasRelease.Contains(pasta, StringComparer.OrdinalIgnoreCase)) return TipoBranch.Release;

        return TipoBranch.Outra;
    }

    /// <summary>Recurso de cor do tema para o tipo — o mesmo na árvore e na tela de branches.</summary>
    public static string Cor(TipoBranch tipo) => tipo switch
    {
        TipoBranch.Principal => "Green",
        TipoBranch.Develop => "Accent",
        TipoBranch.Feature => "Purple",
        TipoBranch.Fix => "Orange",
        TipoBranch.Release => "Yellow",
        _ => "TextDim",
    };

    /// <summary>Ordem dos grupos na tela: o fluxo principal primeiro, pastas avulsas no fim.</summary>
    public static int Ordem(TipoBranch tipo) => tipo switch
    {
        TipoBranch.Principal => 0,
        TipoBranch.Develop => 1,
        TipoBranch.Feature => 2,
        TipoBranch.Fix => 3,
        TipoBranch.Release => 4,
        _ => 5,
    };

    private static bool Igual(string a, string b) =>
        b.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool Comeca(string nome, string prefixo) =>
        prefixo.Length > 0 && nome.Length > prefixo.Length &&
        nome.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------ configuração

    public static async Task<GitFlowConfig> LerAsync(string repo)
    {
        var bruto = await GitService.ConfigListAsync(repo, "^gitflow\\.");

        string? Valor(string chave) => bruto.TryGetValue(chave, out var v) ? v : null;

        var padrao = new GitFlowConfig();
        return new GitFlowConfig
        {
            Master = Valor("gitflow.branch.master") ?? padrao.Master,
            Develop = Valor("gitflow.branch.develop") ?? padrao.Develop,
            Feature = Valor("gitflow.prefix.feature") ?? padrao.Feature,
            Release = Valor("gitflow.prefix.release") ?? padrao.Release,
            Hotfix = Valor("gitflow.prefix.hotfix") ?? padrao.Hotfix,
            VersionTag = Valor("gitflow.prefix.versiontag") ?? padrao.VersionTag,
            Inicializado = Valor("gitflow.branch.develop") is not null,
        };
    }

    /// <summary>Grava as chaves e cria a develop a partir da principal, se ainda não existir.</summary>
    public static async Task InicializarAsync(string repo, GitFlowConfig cfg, IReadOnlyCollection<string> locais)
    {
        await GitService.ConfigSetAsync(repo, "gitflow.branch.master", cfg.Master);
        await GitService.ConfigSetAsync(repo, "gitflow.branch.develop", cfg.Develop);
        await GitService.ConfigSetAsync(repo, "gitflow.prefix.feature", cfg.Feature);
        await GitService.ConfigSetAsync(repo, "gitflow.prefix.release", cfg.Release);
        await GitService.ConfigSetAsync(repo, "gitflow.prefix.hotfix", cfg.Hotfix);
        await GitService.ConfigSetAsync(repo, "gitflow.prefix.versiontag", cfg.VersionTag);

        if (!locais.Contains(cfg.Develop, StringComparer.OrdinalIgnoreCase))
            await GitService.RunAsync(repo, new[] { "branch", cfg.Develop, cfg.Master });
    }

    // ------------------------------------------------------------------ ações

    /// <summary>Branch de onde cada tipo parte: feature e release saem da develop; fix, da principal.</summary>
    public static string Base(TipoBranch tipo, GitFlowConfig cfg) =>
        tipo == TipoBranch.Fix ? cfg.Master : cfg.Develop;

    public static string Prefixo(TipoBranch tipo, GitFlowConfig cfg) => tipo switch
    {
        TipoBranch.Feature => cfg.Feature,
        TipoBranch.Fix => cfg.Hotfix,
        TipoBranch.Release => cfg.Release,
        _ => "",
    };

    /// <summary>Nome final: o prefixo do tipo, sem duplicar se o usuário já digitou.</summary>
    public static string NomeCompleto(TipoBranch tipo, string nome, GitFlowConfig cfg)
    {
        var limpo = nome.Trim().Replace(' ', '-');
        var prefixo = Prefixo(tipo, cfg);
        return limpo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) ? limpo : prefixo + limpo;
    }

    public static Task IniciarAsync(string repo, TipoBranch tipo, string nome, GitFlowConfig cfg) =>
        GitService.RunAsync(repo, new[] { "checkout", "-b", NomeCompleto(tipo, nome, cfg), Base(tipo, cfg) });

    /// <summary>
    /// Passos de "finalizar", na ordem, para mostrar na confirmação e para executar.
    /// Feature volta para a develop; fix e release entram na principal, ganham tag e
    /// voltam para a develop. A branch só é apagada no fim, com <c>-d</c>: se algum merge
    /// não entrou, o git recusa em vez de perder trabalho.
    /// </summary>
    public static List<string[]> PassosFinalizar(string branch, GitFlowConfig cfg)
    {
        var tipo = Classificar(branch, cfg);
        var passos = new List<string[]>();

        if (tipo == TipoBranch.Feature)
        {
            passos.Add(new[] { "checkout", cfg.Develop });
            passos.Add(new[] { "merge", "--no-ff", branch, "-m", $"Merge branch '{branch}' into {cfg.Develop}" });
        }
        else if (tipo is TipoBranch.Fix or TipoBranch.Release)
        {
            var versao = branch[(branch.IndexOf('/') + 1)..];

            passos.Add(new[] { "checkout", cfg.Master });
            passos.Add(new[] { "merge", "--no-ff", branch, "-m", $"Merge branch '{branch}' into {cfg.Master}" });
            if (tipo == TipoBranch.Release)
                passos.Add(new[] { "tag", "-a", cfg.VersionTag + versao, "-m", cfg.VersionTag + versao });
            passos.Add(new[] { "checkout", cfg.Develop });
            passos.Add(new[] { "merge", "--no-ff", branch, "-m", $"Merge branch '{branch}' into {cfg.Develop}" });
        }
        else
        {
            return passos;
        }

        passos.Add(new[] { "branch", "-d", branch });
        return passos;
    }

    public static string Descrever(IEnumerable<string[]> passos) =>
        string.Join("\n", passos.Select(p => "git " + string.Join(' ', p.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))));

    public static async Task FinalizarAsync(string repo, string branch, GitFlowConfig cfg)
    {
        foreach (var passo in PassosFinalizar(branch, cfg))
            await GitService.RunAsync(repo, passo);
    }
}
