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
      "public_repos": 3, "followers": 4, "following": 1,
      "avatar_url": "https://avatars.githubusercontent.com/u/123?v=4"
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
        Assert.Equal("https://avatars.githubusercontent.com/u/123?v=4", p.AvatarUrl);
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
    public void Numstat_soma_adicoes_e_remocoes_ignora_binario_e_nao_repete_commit()
    {
        // \u0001 e não \x01: o \x do C# come até quatro dígitos e "\x01aaa" vira outro caractere
        var saida = "\u0001aaa\n10\t2\tapp/a.cs\n5\t0\tapp/b.cs\n-\t-\tassets/logo.png\n" +
                    "\u0001bbb\n3\t1\ttests/c.cs\n";
        var contados = new System.Collections.Generic.HashSet<string>();

        var (mais, menos) = GitService.SomarNumstatPorCommit(saida, contados);
        Assert.Equal(18, mais);
        Assert.Equal(3, menos);

        // o mesmo histórico de novo (o outro repositório do par) não soma nada
        var (outraVez, _) = GitService.SomarNumstatPorCommit(saida, contados);
        Assert.Equal(0, outraVez);
    }

    private static async Task Commit(string dir, string email, string arquivo, string conteudo, string msg)
    {
        File.WriteAllText(Path.Combine(dir, arquivo), conteudo);
        await GitService.RunAsync(dir, new[] { "add", "." });
        await GitService.RunAsync(dir, new[] { "-c", "user.name=Alguém", "-c", "user.email=" + email, "commit", "-qm", msg });
    }

    [Fact]
    public async Task Contagem_de_linhas_e_so_dos_seus_commits_e_o_par_conta_uma_vez()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-linhas-" + Path.GetRandomFileName());
        var origem = Path.Combine(raiz, "dbisam");
        var destino = Path.Combine(raiz, "mysql");
        Directory.CreateDirectory(origem);
        try
        {
            await GitService.RunAsync(origem, new[] { "init", "-q", "-b", "main" });

            await Commit(origem, "eu@bmsoft.com.br", "a.txt", "um\ndois\ntrês\n", "meu: três linhas");
            await Commit(origem, "colega@bmsoft.com.br", "b.txt", string.Concat(Enumerable.Repeat("x\n", 50)), "do colega");
            await Commit(origem, "eu@bmsoft.com.br", "a.txt", "um\ndois\n", "meu: tira uma");

            // o destino nasce da origem, como no par DBISAM × MySQL, e ganha um commit meu
            await GitService.RunAsync(raiz, new[] { "clone", "-q", origem, destino });
            await Commit(destino, "eu@bmsoft.com.br", "c.txt", "mysql\n", "meu: só no destino");

            var autores = new[] { "<eu@bmsoft.com.br>" };
            var contados = new System.Collections.Generic.HashSet<string>();

            var (mais1, menos1) = await GitService.ContarLinhasAsync(origem, autores, contados);
            var (mais2, menos2) = await GitService.ContarLinhasAsync(destino, autores, contados);

            Assert.Equal(3, mais1);   // as 50 do colega ficam de fora
            Assert.Equal(1, menos1);
            Assert.Equal(1, mais2);   // do destino, só o que não veio da origem
            Assert.Equal(0, menos2);

            // e-mail entre <> casa inteiro: "outro.eu@..." não é "eu@..."
            var (nada, _) = await GitService.ContarLinhasAsync(origem, new[] { "<u@bmsoft.com.br>" });
            Assert.Equal(0, nada);
        }
        finally
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(raiz, true);
            }
            catch (Exception) { /* temporária */ }
        }
    }

    [Fact]
    public async Task Identidades_incluem_o_email_do_repositorio_e_o_noreply_das_contas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-id-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "eu@bmsoft.com.br" });

            var ids = await GitService.IdentidadesAsync(new[] { dir }, new[] { "GFBmsoft", "" });

            Assert.Contains("<eu@bmsoft.com.br>", ids);
            Assert.Contains("GFBmsoft@users.noreply.github.com>", ids);
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

        // o separador de milhar vem da cultura da máquina: cravar "46.835" fazia o
        // teste passar aqui (pt-BR) e quebrar no runner do GitHub, que formata "46,835"
        Assert.Equal((46_835 - 9_770).ToString("N0"), vm.LinhasTexto);
        Assert.Contains(46_835.ToString("N0") + "++", vm.LinhasDetalhe);
        Assert.Contains(9_770.ToString("N0") + "--", vm.LinhasDetalhe);
    }
}
