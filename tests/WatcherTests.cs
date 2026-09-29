using System.IO;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class RepoWatcherFilterTests
{
    [Theory]
    [InlineData(@"D:\proj\src\Unit1.pas", false)]            // arquivo do projeto
    [InlineData(@"D:\proj\bin\Debug\app.exe", false)]        // build também mexe no status
    [InlineData(@"D:\proj\.git\objects\ab\cdef", true)]      // ruído interno
    [InlineData(@"D:\proj\.git\index.lock", true)]           // lock transitório
    [InlineData(@"D:\proj\.git\COMMIT_EDITMSG", true)]
    [InlineData(@"D:\proj\.git\HEAD", false)]                // troca de branch por fora
    [InlineData(@"D:\proj\.git\index", false)]               // stage feito por fora
    [InlineData(@"D:\proj\.git\refs\heads\main", false)]     // commit por fora
    [InlineData(@"D:\proj\.git\logs\refs\stash", false)]     // stash por fora
    public void Filtra_ruido_do_git_mas_nao_o_que_muda_a_tela(string caminho, bool ignorado)
    {
        Assert.Equal(ignorado, RepoWatcher.ShouldIgnore(caminho));
    }
}

/// <summary>Usa o git de verdade: a contagem de stash passou a ler o reflog em disco.</summary>
public class StashCountTests
{
    private static async Task Git(string repo, params string[] args) =>
        await GitService.RunAsync(repo, args);

    /// <summary>Objetos do git ficam somente leitura no Windows e travam o Delete.</summary>
    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (System.Exception) { /* pasta temporária: o SO limpa depois */ }
    }

    [Fact]
    public async Task Conta_stashes_sem_chamar_git_stash_list()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-stash-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await Git(dir, "init", "-q", "-b", "main");
            await Git(dir, "config", "user.email", "t@t");
            await Git(dir, "config", "user.name", "Teste");

            var arquivo = Path.Combine(dir, "a.txt");
            File.WriteAllText(arquivo, "um\n");
            await Git(dir, "add", ".");
            await Git(dir, "commit", "-qm", "inicial");

            Assert.Equal(0, (await GitService.StatusAsync(dir)).Stashes);

            File.WriteAllText(arquivo, "dois\n");
            await Git(dir, "stash", "push", "-m", "primeiro");
            Assert.Equal(1, (await GitService.StatusAsync(dir)).Stashes);

            File.WriteAllText(arquivo, "tres\n");
            await Git(dir, "stash", "push", "-m", "segundo");
            Assert.Equal(2, (await GitService.StatusAsync(dir)).Stashes);

            await Git(dir, "stash", "drop");
            Assert.Equal(1, (await GitService.StatusAsync(dir)).Stashes);
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public async Task Status_e_lista_saem_da_mesma_chamada()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-sc-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await Git(dir, "init", "-q", "-b", "main");
            await Git(dir, "config", "user.email", "t@t");
            await Git(dir, "config", "user.name", "Teste");
            File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
            await Git(dir, "add", ".");
            await Git(dir, "commit", "-qm", "inicial");

            File.WriteAllText(Path.Combine(dir, "a.txt"), "dois\n");   // modificado
            File.WriteAllText(Path.Combine(dir, "b.txt"), "novo\n");   // não rastreado

            var (status, files) = await GitService.StatusAndChangesAsync(dir);

            Assert.Equal("main", status.Branch);
            Assert.Equal(1, status.Unstaged);
            Assert.Equal(1, status.Untracked);
            Assert.Equal(2, files.Count);
            Assert.Contains(files, f => f.Path == "a.txt");
            Assert.Contains(files, f => f.Path == "b.txt");
        }
        finally
        {
            Limpar(dir);
        }
    }
}
