using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

public class PerfilTests
{
    private const string PerfilJson = """
    {
      "login": "GFBmsoft", "name": "Gabriel Ferreira", "bio": "Delphi, Go, C",
      "company": "Bmsoft Sistemas", "location": "Rio do Sul, SC",
      "public_repos": 3, "followers": 4, "following": 1
    }
    """;

    private const string ReposJson = """
    [
      { "name": "GRepos", "fork": false, "stargazers_count": 7, "language": "C#" },
      { "name": "WinDock", "fork": false, "stargazers_count": 5, "language": "C#" },
      { "name": "dotfiles", "fork": false, "stargazers_count": 1, "language": "Lua" },
      { "name": "fork-alheio", "fork": true, "stargazers_count": 900, "language": "Rust" },
      { "name": "sem-linguagem", "fork": false, "stargazers_count": 0, "language": null }
    ]
    """;

    [Fact]
    public void Perfil_sai_da_resposta_da_conta()
    {
        var p = GitHubService.LerPerfil(PerfilJson);

        Assert.Equal("GFBmsoft", p.Login);
        Assert.Equal("Gabriel Ferreira", p.Nome);
        Assert.Equal("Rio do Sul, SC", p.Local);
        Assert.Equal(3, p.RepositoriosPublicos);
        Assert.Equal(4, p.Seguidores);
    }

    [Fact]
    public void Estrelas_somam_so_os_repositorios_proprios()
    {
        var (estrelas, linguagens) = GitHubService.LerEstatisticasDeRepos(ReposJson);

        // as 900 estrelas do fork não são suas
        Assert.Equal(13, estrelas);

        // ordenadas por frequência; repositório sem linguagem não entra
        Assert.Equal(new[] { "C#", "Lua" }, linguagens);
    }

    [Fact]
    public void Resposta_vazia_de_repositorios_nao_quebra()
    {
        var (estrelas, linguagens) = GitHubService.LerEstatisticasDeRepos("[]");
        Assert.Equal(0, estrelas);
        Assert.Empty(linguagens);

        var (e2, l2) = GitHubService.LerEstatisticasDeRepos("""{"message":"Not Found"}""");
        Assert.Equal(0, e2);
        Assert.Empty(l2);
    }

    [Fact]
    public void Numstat_soma_adicoes_e_remocoes_e_ignora_binario()
    {
        var saida = "10\t2\tapp/a.cs\n5\t0\tapp/b.cs\n-\t-\tassets/logo.png\n\n3\t1\ttests/c.cs\n";

        var (mais, menos) = GitService.SomarNumstat(saida);

        Assert.Equal(18, mais);
        Assert.Equal(3, menos);
    }

    [Fact]
    public async Task Contagem_de_linhas_bate_com_um_historico_de_verdade()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-linhas-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
            await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });

            File.WriteAllText(Path.Combine(dir, "a.txt"), "um\ndois\ntrês\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });

            File.WriteAllText(Path.Combine(dir, "a.txt"), "um\ndois\n");
            await GitService.RunAsync(dir, new[] { "commit", "-aqm", "tira uma linha" });

            var (mais, menos) = await GitService.ContarLinhasAsync(dir);

            Assert.Equal(3, mais);  // três linhas no commit inicial
            Assert.Equal(1, menos); // uma removida depois
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { /* temporária */ }
        }
    }

    [Fact]
    public void Enquanto_nao_contou_o_cartao_nao_inventa_numero()
    {
        var vm = new PerfilViewModel("GFBmsoft", new[] { new Repo { Id = "r1", Name = "x" } });

        Assert.Equal("—", vm.LinhasTexto);
        Assert.Equal("—", vm.Estrelas);
        Assert.Equal("1", vm.ReposLocais);

        vm.LinhasAdicionadas = 46_835;
        vm.LinhasRemovidas = 9_770;
        vm.LinhasContadas = true;

        Assert.Equal((46_835 - 9_770).ToString("N0"), vm.LinhasTexto);
        Assert.Contains("46.835++", vm.LinhasDetalhe);
        Assert.Contains("9.770--", vm.LinhasDetalhe);
    }
}
