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

    /// <summary>
    /// Fonte Delphi em ANSI (Windows-1252): o acento aparece inteiro, não como "�", e o
    /// bloco volta ao git nos bytes originais — senão o "git apply" não casa o contexto.
    /// </summary>
    [Fact]
    public async Task Arquivo_ansi_mostra_acento_e_aceita_preparar_bloco()
    {
        var dir = await RepoComArquivoNovo("", "u.pas");
        var arquivo = Path.Combine(dir, "u.pas");
        try
        {
            File.WriteAllBytes(arquivo, TextoGit.Ansi.GetBytes("{O módulo grava}\nserviço\nfim\n"));

            var raw = await GitService.DiffFileAsync(dir, "u.pas", staged: false, untracked: true);
            Assert.Contains("+{O módulo grava}", raw);
            var diff = DiffParser.Parse(raw);
            await GitService.ApplyPatchAsync(dir, DiffParser.BuildHunkPatch(diff, diff.Hunks[0]), cached: true, reverse: false);
            await Git(dir, "commit", "-qm", "ansi");

            File.WriteAllBytes(arquivo, TextoGit.Ansi.GetBytes("{O módulo grava}\nserviço alterado\nfim\n"));
            raw = await GitService.DiffFileAsync(dir, "u.pas", staged: false);
            Assert.Contains("-serviço\n", raw.Replace("\r", ""));
            Assert.Contains("+serviço alterado", raw);

            diff = DiffParser.Parse(raw);
            await GitService.ApplyPatchAsync(dir, DiffParser.BuildHunkPatch(diff, diff.Hunks[0]), cached: true, reverse: false);
            var preparado = await GitService.DiffFileAsync(dir, "u.pas", staged: true);
            Assert.Contains("+serviço alterado", preparado);

            var historico = await GitService.CommitFileDiffAsync(dir, "HEAD", "u.pas");
            Assert.Contains("+{O módulo grava}", historico);
        }
        finally { Limpar(dir); }
    }

    /// <summary>
    /// Pasta com acento (o "Units/Impressão" do bmOS): o git escapava o caminho como
    /// "Impress\303\243o", a lista mostrava isso e o "git add" não achava o arquivo.
    /// </summary>
    [Fact]
    public async Task Pasta_com_acento_aparece_e_e_preparada()
    {
        var dir = await RepoComArquivoNovo("");
        var pasta = Path.Combine(dir, "Impressão");
        var arquivo = Path.Combine(pasta, "Ação.pas");
        try
        {
            Directory.CreateDirectory(pasta);
            File.WriteAllBytes(arquivo, TextoGit.Ansi.GetBytes("impressão\nfim\n"));
            await Git(dir, "add", ".");
            await Git(dir, "commit", "-qm", "pasta");

            File.WriteAllBytes(arquivo, TextoGit.Ansi.GetBytes("impressão\nmeio\nfim\n"));
            File.WriteAllText(Path.Combine(pasta, "Nova ção.txt"), "x\n");

            var (_, files) = await GitService.StatusAndChangesAsync(dir);
            Assert.Contains(files, f => f.Path == "Impressão/Ação.pas");
            Assert.Contains(files, f => f.Path == "Impressão/Nova ção.txt");

            var raw = await GitService.DiffFileAsync(dir, "Impressão/Ação.pas", staged: false);
            Assert.Contains("+meio", raw);
            var diff = DiffParser.Parse(raw);
            await GitService.ApplyPatchAsync(dir, DiffParser.BuildHunkPatch(diff, diff.Hunks[0]), cached: true, reverse: false);
            await GitService.StageAsync(dir, new[] { "Impressão/Nova ção.txt" });

            (_, files) = await GitService.StatusAndChangesAsync(dir);
            Assert.Contains(files, f => f.Path == "Impressão/Ação.pas" && f.Index == "M");
            Assert.Contains(files, f => f.Path == "Impressão/Nova ção.txt" && f.Index == "A");
            Assert.Contains("impressão", await GitService.DiffFileAsync(dir, "Impressão/Ação.pas", staged: true));
        }
        finally { Limpar(dir); }
    }

    [Theory]
    [InlineData("\"Units/Impress\\303\\243o/a.pas\"", "Units/Impressão/a.pas")]
    [InlineData("\"a\\tb\\\\c\\\"d\"", "a\tb\\c\"d")]
    [InlineData("Units/Impressão/a.pas", "Units/Impressão/a.pas")]
    public void Caminho_entre_aspas_do_git_e_decodificado(string doGit, string esperado) =>
        Assert.Equal(esperado, TextoGit.Caminho(doGit));

    [Fact]
    public void Linhas_utf8_e_ansi_no_mesmo_diff()
    {
        var bytes = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(
            System.Text.Encoding.UTF8.GetBytes("-ação\n"), TextoGit.Ansi.GetBytes("+ação – fim\n")));
        Assert.Equal("-ação\n+ação – fim\n", TextoGit.Decodificar(bytes));
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
