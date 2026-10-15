using System.IO;
using System.Threading.Tasks;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Publicar um repositório local no GitHub e fazer fork: o "gh repo create/fork".</summary>
public class PublicarRepoTests
{
    [Theory]
    [InlineData("Financeiro", "Financeiro")]
    [InlineData("Notas Fiscais (MySQL)", "Notas-Fiscais-MySQL")]
    [InlineData("módulo_dav", "m-dulo_dav")]
    [InlineData("  ", "")]
    public void Nome_da_pasta_vira_nome_aceito_pelo_GitHub(string pasta, string esperado) =>
        Assert.Equal(esperado, GitHubService.NomeDeRepositorio(pasta));

    [Fact]
    public void Remoto_leva_a_conta_na_frente_para_o_credential_manager_achar_o_token()
    {
        Assert.Equal("https://GFBmsoft@github.com/GFBmsoft/GRepos.git", GitHubService.UrlDoRemoto("GFBmsoft", "GFBmsoft/GRepos"));
        Assert.Equal("https://github.com/a/b.git", GitHubService.UrlDoRemoto("", "a/b"));
    }

    [Fact]
    public void Nome_invalido_sugere_o_aceito_e_nao_deixa_publicar()
    {
        var vm = new PublicarRepoViewModel(@"C:\repos\Notas Fiscais", "GFBmsoft");
        Assert.Equal("Notas-Fiscais", vm.Nome);
        Assert.True(vm.PodePublicar);
        Assert.Equal("GFBmsoft", vm.Dono);

        vm.Nome = "com espaço";
        Assert.False(vm.PodePublicar);
        Assert.Contains("com-espa-o", vm.AvisoDoNome);
    }

    [Fact]
    public async Task Repositorio_sem_commit_nao_tem_o_que_enviar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-pub-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q" });
            Assert.False(await GitService.TemCommitsAsync(dir));

            await GitService.AdicionarRemotoAsync(dir, "origin", "https://u@github.com/u/r.git");
            Assert.Equal("https://u@github.com/u/r.git", await GitService.RemoteUrlAsync(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (System.UnauthorizedAccessException) { }
        }
    }
}
