using System;
using System.IO;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Comparação de versões, escolha do arquivo da release e a troca do executável.
/// A troca é feita com arquivos de verdade numa pasta temporária: é renomeação, e
/// o que interessa é que o antigo sobreviva para poder voltar.
/// </summary>
public class AtualizadorTests
{
    private const string ReleaseJson = """
    {
      "tag_name": "1.0.0.2",
      "html_url": "https://github.com/GFBmsoft/GRepos/releases/tag/1.0.0.2",
      "assets": [
        {
          "name": "GRepos-1.0.0.2.exe",
          "size": 26214400,
          "browser_download_url": "https://github.com/GFBmsoft/GRepos/releases/download/1.0.0.2/GRepos-1.0.0.2.exe"
        },
        {
          "name": "GRepos-1.0.0.2-standalone.exe",
          "size": 93323264,
          "browser_download_url": "https://github.com/GFBmsoft/GRepos/releases/download/1.0.0.2/GRepos-1.0.0.2-standalone.exe"
        }
      ]
    }
    """;

    [Theory]
    [InlineData("1.0.0.1", "1.0.0.2", true)]
    [InlineData("1.0.0.1", "1.0.1.0", true)]
    [InlineData("1.0.0.2", "1.0.0.2", false)]
    [InlineData("1.0.0.3", "1.0.0.2", false)]
    // build feito depois da tag: está à frente da release, não atrás
    [InlineData("1.0.0.2-dev.5", "1.0.0.2", false)]
    [InlineData("1.0.0.1-dev.5", "1.0.0.2", true)]
    // tag escrita com "v" na frente ainda assim é comparada
    [InlineData("1.0.0.1", "v1.0.0.2", true)]
    // sem versão carimbada (build local) nunca há o que oferecer
    [InlineData("", "1.0.0.2", false)]
    [InlineData("1.0.0", "1.0.0.2", false)]
    public void Novidade_so_quando_a_release_e_maior(string atual, string tag, bool esperado)
    {
        Assert.Equal(esperado, Atualizador.TemNovidade(atual, tag));
    }

    [Fact]
    public void A_release_entrega_o_standalone_entre_os_anexos()
    {
        var release = GitHubService.LerRelease(ReleaseJson);

        Assert.NotNull(release);
        Assert.Equal("1.0.0.2", release!.Tag);
        Assert.Equal(2, release.Arquivos.Count);

        // o que troca sozinho é o standalone: o outro depende do .NET instalado
        var alvo = release.Standalone;
        Assert.NotNull(alvo);
        Assert.Equal("GRepos-1.0.0.2-standalone.exe", alvo!.Nome);
        Assert.Equal(93323264, alvo.Tamanho);
        Assert.StartsWith("https://", alvo.Url);
    }

    [Fact]
    public void Resposta_sem_release_nao_quebra()
    {
        Assert.Null(GitHubService.LerRelease("""{"message":"Not Found"}"""));
        Assert.Null(GitHubService.LerRelease("[]"));
    }

    [Fact]
    public void Troca_guarda_o_antigo_e_poe_o_novo_no_lugar()
    {
        var dir = Pasta();
        try
        {
            var atual = Path.Combine(dir, "GRepos.exe");
            var novo = Path.Combine(dir, "GRepos-1.0.0.2-standalone.exe.baixando");
            File.WriteAllText(atual, "versao antiga");
            File.WriteAllText(novo, "versao nova");

            Atualizador.Trocar(atual, novo);

            Assert.Equal("versao nova", File.ReadAllText(atual));
            Assert.False(File.Exists(novo));

            // o antigo fica como caminho de volta, e some na abertura seguinte
            var antigo = atual + Atualizador.SufixoAntigo;
            Assert.Equal("versao antiga", File.ReadAllText(antigo));

            Atualizador.LimparAntigo(atual);
            Assert.False(File.Exists(antigo));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public void Uma_segunda_troca_substitui_o_antigo_da_anterior()
    {
        var dir = Pasta();
        try
        {
            var atual = Path.Combine(dir, "GRepos.exe");
            File.WriteAllText(atual, "v1");
            File.WriteAllText(atual + Atualizador.SufixoAntigo, "v0"); // sobra de antes

            var novo = Path.Combine(dir, "novo.exe");
            File.WriteAllText(novo, "v2");
            Atualizador.Trocar(atual, novo);

            Assert.Equal("v2", File.ReadAllText(atual));
            Assert.Equal("v1", File.ReadAllText(atual + Atualizador.SufixoAntigo));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public void So_o_executavel_de_arquivo_unico_troca_sozinho()
    {
        var dir = Pasta();
        try
        {
            var exe = Path.Combine(dir, "GRepos.exe");
            File.WriteAllText(exe, "exe");
            Assert.True(Atualizador.PodeTrocarSozinho(exe));

            // build de pasta: trocar só o .exe deixaria as DLLs ao lado desencontradas
            File.WriteAllText(Path.Combine(dir, "GRepos.dll"), "dll");
            Assert.False(Atualizador.PodeTrocarSozinho(exe));

            Assert.False(Atualizador.PodeTrocarSozinho(null));
            Assert.False(Atualizador.PodeTrocarSozinho(""));
        }
        finally
        {
            Limpar(dir);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Endereco_que_nao_e_https_e_recusado()
    {
        var dir = Pasta();
        try
        {
            var arquivo = new ReleaseAsset
            {
                Nome = "GRepos.exe",
                Tamanho = 10,
                Url = "http://exemplo/GRepos.exe",
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Atualizador.BaixarAsync(arquivo, dir));
        }
        finally
        {
            Limpar(dir);
        }
    }

    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-upd-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Limpar(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { /* pasta temporária */ }
    }
}
