using System.Linq;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class UrlTemplateTests
{
    private const string Modelo = "https://{{user}}:{{token}}@github.com/bmsoftsistemas/BM2Maga.git";

    [Fact]
    public void Expande_usuario_e_token()
    {
        var url = UrlTemplate.Expandir(Modelo, "GFBmsoft", "ghp_segredo123");
        Assert.Equal("https://GFBmsoft:ghp_segredo123@github.com/bmsoftsistemas/BM2Maga.git", url);
    }

    [Fact]
    public void Aceita_espacos_dentro_das_chaves()
    {
        var url = UrlTemplate.Expandir("https://{{ user }}@github.com/o/r.git", "gf", null);
        Assert.Equal("https://gf@github.com/o/r.git", url);
    }

    [Fact]
    public void Variavel_sem_valor_fica_visivel_em_vez_de_gerar_url_quebrada()
    {
        // sem token salvo, o usuário precisa ver o que faltou
        var url = UrlTemplate.Expandir(Modelo, "GFBmsoft", null);
        Assert.Contains("{{token}}", url);
        Assert.Contains("GFBmsoft", url);
    }

    [Fact]
    public void Variavel_desconhecida_e_preservada()
    {
        var url = UrlTemplate.Expandir("https://{{outra}}@x.com/r.git", "gf", "t");
        Assert.Contains("{{outra}}", url);
    }

    [Fact]
    public void Escapa_caracteres_especiais_do_token()
    {
        var url = UrlTemplate.Expandir(Modelo, "gf", "tok/en+com@simbolos");
        Assert.DoesNotContain("tok/en+com@simbolos", url);
        Assert.Contains("tok%2Fen%2Bcom%40simbolos", url);
    }

    [Fact]
    public void Lista_as_variaveis_usadas()
    {
        var vars = UrlTemplate.Variaveis(Modelo).ToList();
        Assert.Equal(new[] { "user", "token" }, vars);
        Assert.True(UrlTemplate.UsaToken(Modelo));
        Assert.False(UrlTemplate.UsaToken("https://github.com/o/r.git"));
    }

    [Fact]
    public void Mascara_o_token_ao_exibir()
    {
        var url = UrlTemplate.Expandir("https://{{user}}:{{token}}@x.com/r.git", "gf", "segredo");
        var visivel = UrlTemplate.Mascarar(url, "segredo");
        Assert.DoesNotContain("segredo", visivel);
        Assert.Contains("●", visivel);
    }

    [Theory]
    [InlineData("https://GFBmsoft@github.com/bmsoftsistemas/BM2Maga.git",
                "https://{{user}}:{{token}}@github.com/bmsoftsistemas/BM2Maga.git")]
    [InlineData("https://github.com/GFBmsoft/WinDock.git",
                "https://{{user}}:{{token}}@github.com/GFBmsoft/WinDock.git")]
    public void Sugere_o_modelo_a_partir_do_remoto_atual(string remoto, string esperado)
    {
        Assert.Equal(esperado, UrlTemplate.Sugerir(remoto));
    }

    [Fact]
    public void Modelo_vazio_nao_quebra()
    {
        Assert.Equal("", UrlTemplate.Expandir("", "gf", "t"));
        Assert.Equal("", UrlTemplate.Sugerir(""));
        Assert.Equal("", UrlTemplate.Mascarar("", "t"));
    }
}
