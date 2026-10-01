using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class WebUrlTests
{
    [Theory]
    // formas reais encontradas nos repositórios da BMSoft
    [InlineData("https://GFBmsoft@github.com/bmsoftsistemas/BM2Maga.git", "https://github.com/bmsoftsistemas/BM2Maga")]
    [InlineData("https://github.com/GFBmsoft/WinDock.git", "https://github.com/GFBmsoft/WinDock")]
    [InlineData("git@github.com:bmsoftsistemas/BM2Maga.git", "https://github.com/bmsoftsistemas/BM2Maga")]
    [InlineData("ssh://git@github.com/owner/repo.git", "https://github.com/owner/repo")]
    [InlineData("https://dev.azure.com/org/proj/_git/repo", "https://dev.azure.com/org/proj/_git/repo")]
    [InlineData("", "")]
    public void Converte_o_remoto_em_endereco_de_navegador(string remoto, string esperado)
    {
        Assert.Equal(esperado, GitService.WebUrl(remoto));
    }

    [Fact]
    public void Tira_a_credencial_embutida_mas_preserva_o_caminho()
    {
        var url = GitService.WebUrl("https://usuario:senha@github.com/owner/repo.git");
        Assert.Equal("https://github.com/owner/repo", url);
        Assert.DoesNotContain("senha", url);
    }
}

public class GitHubSlugTests
{
    [Theory]
    [InlineData("https://GFBmsoft@github.com/bmsoftsistemas/BM2Maga.git", "bmsoftsistemas/BM2Maga")]
    [InlineData("git@github.com:GFBmsoft/WinDock.git", "GFBmsoft/WinDock")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    public void Extrai_owner_e_repositorio(string remoto, string esperado)
    {
        Assert.Equal(esperado, GitHubService.Slug(remoto));
    }

    [Theory]
    [InlineData("https://GFBmsoft@github.com/bmsoftsistemas/BM2Maga.git", "GFBmsoft")]
    [InlineData("https://usuario:senha@github.com/owner/repo.git", "usuario")]
    [InlineData("", "")]
    public void Extrai_o_usuario_da_url_para_achar_a_credencial(string remoto, string esperado)
    {
        // o credential manager guarda por conta: sem o usuário não acha o token
        Assert.Equal(esperado, GitHubService.Usuario(remoto));
    }

    [Theory]
    [InlineData("https://github.com/GFBmsoft/WinDock.git")]
    [InlineData("git@github.com:owner/repo.git")]
    public void Sem_usuario_na_url_vale_a_conta_configurada(string remoto)
    {
        // sem conta a chamada vai anônima, e a cota anônima acaba em minutos no painel
        Assert.Equal(GitService.CredentialUser, GitHubService.Usuario(remoto));
    }

    [Fact]
    public void Token_no_lugar_do_usuario_cai_na_conta_configurada()
    {
        // remoto do jeito que o SourceTree deixa: o token não é uma conta, e usá-lo como
        // usuário fazia a esteira ser consultada sem autenticação
        var usuario = GitHubService.Usuario("https://ghp_abc123XYZ@github.com/bmsoftsistemas/Financeiro.git");

        Assert.DoesNotContain("ghp_", usuario);
        Assert.Equal(GitService.CredentialUser, usuario);
    }

    [Theory]
    [InlineData("https://dev.azure.com/org/proj/_git/repo")]   // não é GitHub
    [InlineData("https://gitlab.com/owner/repo.git")]
    [InlineData("https://github.com/soh-o-owner")]             // falta o repositório
    [InlineData("")]
    public void Fora_do_github_nao_tem_esteira(string remoto)
    {
        Assert.Null(GitHubService.Slug(remoto));
    }
}
