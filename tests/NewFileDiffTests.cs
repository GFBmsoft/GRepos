using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Arquivo novo não tem diff pelo git no Windows ("--no-index /dev/null" não existe lá):
/// o patch é montado pelo app e precisa ser aceito pelo próprio git.
/// </summary>
public class NewFileDiffTests
{
    private static async Task Git(string repo, params string[] args) =>
        await GitService.RunAsync(repo, args);

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

    private static async Task<string> RepoComArquivoNovo(string conteudo, string nome = "novo.txt")
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-new-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        File.WriteAllText(Path.Combine(dir, "base.txt"), "base\n");
        await Git(dir, "add", ".");
        await Git(dir, "commit", "-qm", "inicial");
        File.WriteAllText(Path.Combine(dir, nome), conteudo);
        return dir;
    }

    [Fact]
    public async Task Mostra_o_conteudo_do_arquivo_novo()
    {
        var dir = await RepoComArquivoNovo("alfa\nbeta\ngama\n");
        try
        {
            var raw = await GitService.DiffFileAsync(dir, "novo.txt", staged: false, untracked: true);
            var diff = DiffParser.Parse(raw);

            Assert.Single(diff.Hunks);
            Assert.Equal(3, diff.Hunks[0].Lines.Count(l => l.Kind == DiffLineKind.Add));
            Assert.Equal("@@ -0,0 +1,3 @@", diff.Hunks[0].Header);
            Assert.Contains("alfa", raw);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task O_patch_gerado_e_aceito_pelo_git_apply()
    {
        var dir = await RepoComArquivoNovo("alfa\nbeta\n");
        try
        {
            var raw = await GitService.DiffFileAsync(dir, "novo.txt", staged: false, untracked: true);
            var diff = DiffParser.Parse(raw);
            var patch = DiffParser.BuildHunkPatch(diff, diff.Hunks[0]);

            // é isto que o botão "Preparar bloco" faz
            await GitService.ApplyPatchAsync(dir, patch, cached: true, reverse: false);

            var (status, files) = await GitService.StatusAndChangesAsync(dir);
            Assert.Equal(1, status.Staged);
            Assert.Contains(files, f => f.Path == "novo.txt" && f.Index == "A");
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Arquivo_binario_nao_e_despejado_na_tela()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-bin-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await Git(dir, "init", "-q", "-b", "main");
            File.WriteAllBytes(Path.Combine(dir, "img.bin"), new byte[] { 1, 2, 0, 3, 4 });

            var raw = await GitService.DiffFileAsync(dir, "img.bin", staged: false, untracked: true);

            Assert.True(DiffParser.Parse(raw).Binary);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Marca_ausencia_de_quebra_de_linha_no_fim()
    {
        var dir = await RepoComArquivoNovo("sem quebra final");
        try
        {
            var raw = await GitService.DiffFileAsync(dir, "novo.txt", staged: false, untracked: true);
            Assert.Contains("No newline at end of file", raw);
        }
        finally { Limpar(dir); }
    }
}
