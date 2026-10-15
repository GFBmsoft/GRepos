using System;
using System.IO;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// "Trazer para a máquina" — o gh pr checkout — com git de verdade. O remoto é uma
/// pasta local; o PR de fork é simulado com a ref refs/pull/N/head, que é onde o GitHub
/// guarda a ponta de todo PR.
/// </summary>
public class TrazerPrTests
{
    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task<string> Branch(string dir) => (await Git(dir, "rev-parse", "--abbrev-ref", "HEAD")).Trim();

    private static async Task<string> Ler(string dir, string arquivo) =>
        (await Git(dir, "show", "HEAD:" + arquivo)).Trim();

    private static async Task Commit(string dir, string arquivo, string texto)
    {
        File.WriteAllText(Path.Combine(dir, arquivo), texto + "\n");
        await Git(dir, "add", "-A");
        await Git(dir, "commit", "-qm", texto);
    }

    private static async Task<(string Remoto, string Local)> Montar()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-pr-" + Path.GetRandomFileName());
        var remoto = Path.Combine(raiz, "remoto");
        Directory.CreateDirectory(remoto);
        await Git(remoto, "init", "-q", "-b", "main");
        await Git(remoto, "config", "user.email", "t@t");
        await Git(remoto, "config", "user.name", "Teste");
        await Commit(remoto, "a.txt", "inicial");

        // a branch do PR, no próprio repositório
        await Git(remoto, "checkout", "-q", "-b", "feat/log");
        await Commit(remoto, "a.txt", "do PR");

        // o PR de fork: a branch não existe aqui, só a ref que o GitHub mantém
        await Git(remoto, "checkout", "-q", "-b", "fork-tmp", "main");
        await Commit(remoto, "b.txt", "do fork");
        await Git(remoto, "update-ref", "refs/pull/7/head", "HEAD");
        await Git(remoto, "checkout", "-q", "main");
        await Git(remoto, "branch", "-q", "-D", "fork-tmp");

        var local = Path.Combine(raiz, "local");
        await Git(raiz, "clone", "-q", "-c", "core.autocrlf=false", remoto, local);
        return (remoto, local);
    }

    private static void Limpar(string dir)
    {
        try
        {
            var raiz = Path.GetDirectoryName(dir)!;
            foreach (var f in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(raiz, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task PR_do_proprio_repositorio_vira_a_branch_dele_e_depois_so_avanca()
    {
        var (remoto, local) = await Montar();
        try
        {
            var ficou = await GitService.TrazerPullRequestAsync(local, 3, "feat/log", deFork: false);
            Assert.Equal("feat/log", ficou);
            Assert.Equal("feat/log", await Branch(local));
            Assert.Equal("do PR", await Ler(local, "a.txt"));
            // acompanha a do remoto: Puxar e Enviar funcionam nela depois
            Assert.Equal("origin/feat/log", (await Git(local, "rev-parse", "--abbrev-ref", "@{upstream}")).Trim());

            // o autor do PR mandou mais um commit; trazer de novo (estando em outra branch) avança
            await Git(remoto, "checkout", "-q", "feat/log");
            await Commit(remoto, "a.txt", "segunda rodada");
            await Git(local, "checkout", "-q", "main");

            await GitService.TrazerPullRequestAsync(local, 3, "feat/log", deFork: false);
            Assert.Equal("feat/log", await Branch(local));
            Assert.Equal("segunda rodada", await Ler(local, "a.txt"));
        }
        finally { Limpar(local); }
    }

    [Fact]
    public async Task PR_de_fork_vira_pr_N_buscado_pela_ref_do_GitHub()
    {
        var (remoto, local) = await Montar();
        try
        {
            var ficou = await GitService.TrazerPullRequestAsync(local, 7, "feat/do-fork", deFork: true);
            Assert.Equal("pr/7", ficou);
            Assert.Equal("pr/7", await Branch(local));
            Assert.Equal("do fork", await Ler(local, "b.txt"));

            // já estando nela, buscar de novo avança em vez de recusar
            await Git(remoto, "checkout", "-q", "--detach", "refs/pull/7/head");
            await Commit(remoto, "b.txt", "fork de novo");
            await Git(remoto, "update-ref", "refs/pull/7/head", "HEAD");

            await GitService.TrazerPullRequestAsync(local, 7, "feat/do-fork", deFork: true);
            Assert.Equal("fork de novo", await Ler(local, "b.txt"));
        }
        finally { Limpar(local); }
    }

    [Fact]
    public async Task Branch_local_que_divergiu_nao_e_sobrescrita()
    {
        var (_, local) = await Montar();
        try
        {
            await GitService.TrazerPullRequestAsync(local, 3, "feat/log", deFork: false);
            await Git(local, "config", "user.email", "t@t");
            await Git(local, "config", "user.name", "Teste");
            await Git(local, "reset", "-q", "--hard", "HEAD~1");
            await Commit(local, "c.txt", "só aqui");

            await Assert.ThrowsAsync<GitException>(() => GitService.TrazerPullRequestAsync(local, 3, "feat/log", deFork: false));
            Assert.Equal("só aqui", await Ler(local, "c.txt")); // o commit local continua lá
        }
        finally { Limpar(local); }
    }
}
