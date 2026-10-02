using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// develop que já existia no remoto, Puxar em branch sem vínculo, Trocar em branch
/// remota e as opções de trazer todas as tags e todas as branches.
/// </summary>
public class SincronizacaoTests
{
    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (Exception) { /* pasta temporária */ }
    }

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task Configurar(string dir)
    {
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        await Git(dir, "config", "core.autocrlf", "false");
    }

    private static async Task Commit(string dir, string nome, string msg)
    {
        File.WriteAllText(Path.Combine(dir, nome), msg + "\n");
        await Git(dir, "add", nome);
        await Git(dir, "commit", "-qm", msg);
    }

    private static Task<string> Hash(string dir, string rev) => Git(dir, "rev-parse", rev).ContinueWith(t => t.Result.Trim());

    /// <summary>
    /// Remoto (bare) com main e develop, alimentado por um "colega"; devolve as pastas
    /// do remoto, do colega e de um clone novo — o computador do usuário.
    /// </summary>
    private static async Task<(string Remoto, string Colega, string Meu)> Cenario()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-sync-" + Path.GetRandomFileName());
        var remoto = Path.Combine(raiz, "remoto.git");
        var colega = Path.Combine(raiz, "colega");
        var meu = Path.Combine(raiz, "meu");
        Directory.CreateDirectory(raiz);

        await Git(raiz, "init", "-q", "--bare", "-b", "main", remoto);
        await Git(raiz, "clone", "-q", remoto, colega);
        await Configurar(colega);
        await Git(colega, "checkout", "-qb", "main");
        await Commit(colega, "a.txt", "inicial");
        await Git(colega, "push", "-q", "-u", "origin", "main");
        await Git(colega, "checkout", "-qb", "develop");
        await Commit(colega, "d.txt", "na develop");
        await Git(colega, "push", "-q", "-u", "origin", "develop");
        await Git(colega, "checkout", "-q", "main");

        await Git(raiz, "clone", "-q", remoto, meu);
        await Configurar(meu);
        return (remoto, colega, meu);
    }

    private static string Raiz(string meu) => Path.GetDirectoryName(meu)!;

    [Fact]
    public async Task Gitflow_cria_a_develop_a_partir_da_remota_e_vinculada()
    {
        var (_, _, meu) = await Cenario();
        try
        {
            var soLocal = await GitFlow.InicializarAsync(meu, new GitFlowConfig { Master = "main" }, await GitService.BranchesAsync(meu));

            Assert.False(soLocal);
            Assert.Equal(await Hash(meu, "origin/develop"), await Hash(meu, "develop"));
            Assert.Equal("origin/develop", (await Git(meu, "rev-parse", "--abbrev-ref", "develop@{upstream}")).Trim());
        }
        finally { Limpar(Raiz(meu)); }
    }

    [Fact]
    public async Task Gitflow_vincula_a_develop_local_que_ficou_solta()
    {
        var (_, _, meu) = await Cenario();
        try
        {
            await Git(meu, "branch", "--no-track", "develop", "main"); // o que a versão antiga fazia
            await GitFlow.InicializarAsync(meu, new GitFlowConfig { Master = "main" }, await GitService.BranchesAsync(meu));
            Assert.Equal("origin/develop", (await Git(meu, "rev-parse", "--abbrev-ref", "develop@{upstream}")).Trim());
        }
        finally { Limpar(Raiz(meu)); }
    }

    [Fact]
    public async Task Gitflow_sem_develop_no_remoto_cria_da_principal_e_avisa()
    {
        var (remoto, colega, meu) = await Cenario();
        try
        {
            await Git(colega, "push", "-q", "origin", "--delete", "develop");
            await Git(meu, "fetch", "-q", "--prune");

            var soLocal = await GitFlow.InicializarAsync(meu, new GitFlowConfig { Master = "main" }, await GitService.BranchesAsync(meu));
            Assert.True(soLocal);
            Assert.Equal(await Hash(meu, "main"), await Hash(meu, "develop"));
        }
        finally { Limpar(Raiz(meu)); }
    }

    /// <summary>O caso relatado: develop criada só aqui, a partir da main, e o Puxar falhava.</summary>
    [Fact]
    public async Task Puxar_em_branch_sem_vinculo_vincula_a_remota_de_mesmo_nome()
    {
        var (_, _, meu) = await Cenario();
        try
        {
            await Git(meu, "checkout", "-q", "--no-track", "-b", "develop", "main");
            await GitService.PullAsync(meu, false);

            Assert.Equal(await Hash(meu, "origin/develop"), await Hash(meu, "HEAD"));
            Assert.True(await GitService.TemUpstreamAsync(meu));
        }
        finally { Limpar(Raiz(meu)); }
    }

    [Fact]
    public async Task Puxar_em_branch_so_local_explica_em_portugues()
    {
        var (_, _, meu) = await Cenario();
        try
        {
            await Git(meu, "checkout", "-qb", "feat/nova");
            var e = await Assert.ThrowsAsync<GitException>(() => GitService.PullAsync(meu, false));
            Assert.Contains("só existe no seu computador", e.Message);
        }
        finally { Limpar(Raiz(meu)); }
    }

    [Fact]
    public async Task Trocar_numa_remota_cria_a_local_vinculada_sem_soltar_o_head()
    {
        var (_, _, meu) = await Cenario();
        try
        {
            await GitService.CheckoutRemotaAsync(meu, "origin/develop");
            Assert.Equal("develop", (await Git(meu, "branch", "--show-current")).Trim());
            Assert.True(await GitService.TemUpstreamAsync(meu));

            // de novo, com a local já existente: só troca
            await Git(meu, "checkout", "-q", "main");
            await GitService.CheckoutRemotaAsync(meu, "origin/develop");
            Assert.Equal("develop", (await Git(meu, "branch", "--show-current")).Trim());
        }
        finally { Limpar(Raiz(meu)); }
    }

    [Fact]
    public async Task Sincronizar_cria_as_que_faltam_avanca_as_atrasadas_e_preserva_as_com_commit_proprio()
    {
        var (_, colega, meu) = await Cenario();
        try
        {
            // colega cria "feat" e avança develop e main
            await Git(colega, "checkout", "-qb", "feat");
            await Commit(colega, "f.txt", "feat");
            await Git(colega, "push", "-q", "-u", "origin", "feat");
            await Git(meu, "branch", "--track", "develop", "origin/develop");
            await Git(colega, "checkout", "-q", "develop");
            await Commit(colega, "d2.txt", "develop 2");
            await Git(colega, "push", "-q");
            await Git(colega, "checkout", "-q", "main");
            await Commit(colega, "m2.txt", "main 2");
            await Git(colega, "push", "-q");

            // aqui: main é a atual (não pode ser mexida) e "outra" tem commit só local
            await Git(meu, "checkout", "-qb", "outra", "--track", "origin/main");
            await Commit(meu, "o.txt", "só minha");
            var minha = await Hash(meu, "outra");
            await Git(meu, "checkout", "-q", "main");
            var mainAntes = await Hash(meu, "main");

            await GitService.FetchAsync(meu);
            var (criadas, atualizadas) = await GitService.SincronizarBranchesLocaisAsync(meu);

            Assert.Equal(1, criadas); // feat
            Assert.Equal(1, atualizadas); // develop
            Assert.Equal(await Hash(meu, "origin/feat"), await Hash(meu, "feat"));
            Assert.Equal(await Hash(meu, "origin/develop"), await Hash(meu, "develop"));
            Assert.Equal(mainAntes, await Hash(meu, "main"));
            Assert.Equal(minha, await Hash(meu, "outra"));
        }
        finally { Limpar(Raiz(meu)); }
    }

    [Fact]
    public async Task Obter_com_todas_as_tags_traz_tag_fora_das_branches()
    {
        var (_, colega, meu) = await Cenario();
        try
        {
            // tag num commit que nenhuma branch alcança: o fetch normal não a segue
            await Git(colega, "checkout", "-qb", "temp");
            await Commit(colega, "t.txt", "temporario");
            await Git(colega, "tag", "1.0.0.1");
            await Git(colega, "push", "-q", "origin", "1.0.0.1");
            await Git(colega, "checkout", "-q", "main");

            await GitService.FetchAsync(meu);
            Assert.Equal("", (await Git(meu, "tag", "--list")).Trim());

            await GitService.FetchAsync(meu, todasAsTags: true);
            Assert.Equal("1.0.0.1", (await Git(meu, "tag", "--list")).Trim());

            // tag só local não é podada pelo --prune
            await Git(meu, "tag", "minha");
            await GitService.FetchAsync(meu, todasAsTags: true);
            Assert.Contains("minha", await Git(meu, "tag", "--list"));
        }
        finally { Limpar(Raiz(meu)); }
    }
}
