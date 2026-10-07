using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Soltar uma branch sobre outra: o que cabe oferecer em cada par e o que o git faz
/// com a opção escolhida.
/// </summary>
public class ArrasteDeBranchTests
{
    [Theory]
    [InlineData("HEAD -> main", "main", false)]
    [InlineData("feat/boleto", "feat/boleto", false)]
    [InlineData("origin/feat/boleto", "origin/feat/boleto", true)]
    public void Ref_do_log_vira_branch(string refDoLog, string nome, bool remota)
    {
        var b = ArrasteDeBranch.DaRef(refDoLog)!;

        Assert.Equal(nome, b.Nome);
        Assert.Equal(remota, b.Remota);
    }

    [Theory]
    [InlineData("tag: 1.0.0.33")]
    [InlineData("origin/HEAD")]
    [InlineData("HEAD")]
    public void Tag_e_head_solto_nao_sao_branch(string refDoLog)
    {
        Assert.Null(ArrasteDeBranch.DaRef(refDoLog));
    }

    [Fact]
    public void Commit_com_local_e_remota_vale_a_local()
    {
        var b = ArrasteDeBranch.DoCommit(new[] { "tag: v1", "origin/develop", "develop" })!;
        Assert.Equal("develop", b.Nome);

        Assert.Equal("origin/x", ArrasteDeBranch.DoCommit(new[] { "origin/x" })!.Nome);
        Assert.Null(ArrasteDeBranch.DoCommit(new[] { "tag: v1" }));
    }

    [Fact]
    public void Local_sobre_local_oferece_as_tres()
    {
        var opcoes = ArrasteDeBranch.Opcoes(new RefDeBranch("imp/boleto", false), new RefDeBranch("develop", false), temGitHub: true);

        Assert.Equal(new[] { AcaoDeArraste.Mesclar, AcaoDeArraste.Rebase, AcaoDeArraste.PullRequest }, opcoes.Select(o => o.Acao));
        Assert.All(opcoes, o => Assert.True(o.Disponivel));
        Assert.Equal("git checkout develop\ngit merge imp/boleto", opcoes[0].Comandos);
        Assert.Equal("git rebase develop imp/boleto", opcoes[1].Comandos);
        Assert.Contains("de imp/boleto para develop", opcoes[2].Rotulo);
    }

    [Fact]
    public void Remota_nao_recebe_merge_nem_sofre_rebase()
    {
        var paraRemota = ArrasteDeBranch.Opcoes(new RefDeBranch("imp/x", false), new RefDeBranch("origin/develop", true), true);
        Assert.False(paraRemota.Single(o => o.Acao == AcaoDeArraste.Mesclar).Disponivel);
        Assert.True(paraRemota.Single(o => o.Acao == AcaoDeArraste.PullRequest).Disponivel);
        Assert.Contains("de imp/x para develop", paraRemota.Single(o => o.Acao == AcaoDeArraste.PullRequest).Rotulo);

        var daRemota = ArrasteDeBranch.Opcoes(new RefDeBranch("origin/imp/x", true), new RefDeBranch("develop", false), true);
        Assert.True(daRemota.Single(o => o.Acao == AcaoDeArraste.Mesclar).Disponivel);
        Assert.False(daRemota.Single(o => o.Acao == AcaoDeArraste.Rebase).Disponivel);
    }

    [Fact]
    public void Sem_github_nao_oferece_pull_request_e_a_mesma_branch_nao_oferece_nada()
    {
        var opcoes = ArrasteDeBranch.Opcoes(new RefDeBranch("a", false), new RefDeBranch("b", false), temGitHub: false);
        Assert.DoesNotContain(opcoes, o => o.Acao == AcaoDeArraste.PullRequest);

        Assert.Empty(ArrasteDeBranch.Opcoes(new RefDeBranch("a", false), new RefDeBranch("a", false), true));

        // "x" sobre "origin/x" é enviar, não pull request
        var mesma = ArrasteDeBranch.Opcoes(new RefDeBranch("x", false), new RefDeBranch("origin/x", true), true);
        Assert.False(mesma.Single(o => o.Acao == AcaoDeArraste.PullRequest).Disponivel);
    }

    // ----------------------------------------------------------- no git

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task Commit(string dir, string nome, string conteudo, string msg)
    {
        File.WriteAllText(Path.Combine(dir, nome), conteudo);
        await Git(dir, "add", nome);
        await Git(dir, "commit", "-qm", msg);
    }

    /// <summary>main com um commit; develop e imp/x saindo dela, cada uma com o seu.</summary>
    private static async Task<string> Cenario(bool conflito = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-arraste-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        await Git(dir, "config", "core.autocrlf", "false");
        await Commit(dir, "a.txt", "base\n", "inicial");

        await Git(dir, "checkout", "-qb", "develop");
        await Commit(dir, conflito ? "a.txt" : "d.txt", "develop\n", "na develop");

        await Git(dir, "checkout", "-q", "-b", "imp/x", "main");
        await Commit(dir, conflito ? "a.txt" : "x.txt", "imp\n", "na imp");
        return dir;
    }

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

    [Fact]
    public async Task Mesclar_troca_para_o_destino_e_traz_a_origem()
    {
        var dir = await Cenario();
        try
        {
            // a branch atual é a imp/x: o merge precisa acontecer na develop
            await GitService.MesclarEmAsync(dir, "imp/x", "develop");

            Assert.Equal("develop", (await Git(dir, "branch", "--show-current")).Trim());
            Assert.True(File.Exists(Path.Combine(dir, "x.txt")));
            Assert.True(File.Exists(Path.Combine(dir, "d.txt")));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public async Task Rebase_reaplica_a_arrastada_sobre_o_destino()
    {
        var dir = await Cenario();
        try
        {
            await Git(dir, "checkout", "-q", "main");
            await GitService.RebaseSobreAsync(dir, "imp/x", "develop");

            Assert.Equal("imp/x", (await Git(dir, "branch", "--show-current")).Trim());
            var assuntos = (await Git(dir, "log", "--format=%s")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(new[] { "na imp", "na develop", "inicial" }, assuntos.Select(a => a.Trim()));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public async Task Merge_em_conflito_fica_em_andamento_para_resolver()
    {
        var dir = await Cenario(conflito: true);
        try
        {
            await Assert.ThrowsAsync<GitException>(() => GitService.MesclarEmAsync(dir, "imp/x", "develop"));

            Assert.Equal(GitService.Operacao.Merge, GitService.OperacaoEmAndamento(dir));
            Assert.Equal(1, (await GitService.StatusAsync(dir)).Conflicted);
        }
        finally
        {
            Limpar(dir);
        }
    }
}
