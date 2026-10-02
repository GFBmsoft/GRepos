using System.IO;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Link do remoto sem os botões "Usar o remoto atual" e "Aplicar no git": o modelo é
/// sugerido ao abrir e o git só muda ao salvar, e só se o link for diferente.
/// Nenhum caso aqui tem token na URL — senão a migração gravaria no Credential Manager real.
/// </summary>
public class RemotoConfigTests
{
    private static async Task<string> RepoAsync(string? remoto)
    {
        var dir = Directory.CreateTempSubdirectory("grepos-remoto-").FullName;
        await GitService.RunAsync(dir, new[] { "init", "-q" });
        if (remoto is not null) await GitService.RunAsync(dir, new[] { "remote", "add", "origin", remoto });
        return dir;
    }

    [Fact]
    public async Task Salvar_poe_o_usuario_no_link_e_depois_nao_mexe_mais()
    {
        var dir = await RepoAsync("https://github.com/org/repo.git");
        try
        {
            var modelo = await RemotoConfig.SugerirAsync(dir);
            Assert.Equal("https://{{user}}@github.com/org/repo.git", modelo);

            var msg = await RemotoConfig.AplicarAsync(dir, modelo, "fulano-sem-token");
            Assert.NotNull(msg);
            Assert.Equal("https://fulano-sem-token@github.com/org/repo.git", await GitService.RemoteUrlAsync(dir));

            Assert.Null(await RemotoConfig.AplicarAsync(dir, modelo, "fulano-sem-token"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Sem_remoto_o_link_e_criado()
    {
        var dir = await RepoAsync(null);
        try
        {
            await RemotoConfig.AplicarAsync(dir, "https://{{user}}@github.com/org/novo.git", "fulano-sem-token");
            Assert.Equal("https://fulano-sem-token@github.com/org/novo.git", await GitService.RemoteUrlAsync(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Sem_conta_definida_nao_aplica_e_explica()
    {
        var dir = await RepoAsync("https://github.com/org/repo.git");
        try
        {
            var erro = await Assert.ThrowsAsync<GitException>(() =>
                RemotoConfig.AplicarAsync(dir, "https://{{user}}@github.com/org/repo.git", ""));
            Assert.Contains("Preferências", erro.Message);
            Assert.Equal("https://github.com/org/repo.git", await GitService.RemoteUrlAsync(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("https://github.com/bmsoftsistemas-mysql/bmOS.git", "bmOS")]
    [InlineData("https://ghp_x@github.com/o/Financeiro/", "Financeiro")]
    [InlineData("git@github.com:o/NFe.git", "NFe")]
    [InlineData(@"D:\repos\local", "local")]
    public void Nome_da_pasta_sai_do_link(string link, string nome) =>
        Assert.Equal(nome, RemotoConfig.NomeDoLink(link));

    /// <summary>Clone de um repositório local: não precisa de rede nem de conta.</summary>
    [Fact]
    public async Task Clona_e_recusa_pasta_ocupada()
    {
        var origem = await RepoAsync(null);
        var pai = Directory.CreateTempSubdirectory("grepos-clone-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(origem, "a.txt"), "a\n");
            await GitService.RunAsync(origem, new[] { "add", "." });
            await GitService.RunAsync(origem, new[] { "-c", "user.name=T", "-c", "user.email=t@t", "commit", "-qm", "a" });

            var destino = Path.Combine(pai, "Impressão");
            await RemotoConfig.ClonarAsync(origem, destino, "fulano-sem-token");
            Assert.True(File.Exists(Path.Combine(destino, "a.txt")));

            var erro = await Assert.ThrowsAsync<GitException>(() => RemotoConfig.ClonarAsync(origem, destino, ""));
            Assert.Contains("não está vazia", erro.Message);
        }
        finally
        {
            foreach (var d in new[] { origem, pai })
            {
                foreach (var f in Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(d, true);
            }
        }
    }

    [Theory]
    [InlineData("https://GFBmsoft@github.com/o/r.git", null)]
    [InlineData("https://ghp_abc123@github.com/o/r.git", "token gravado")]
    [InlineData("https://github.com/o/r.git", "passa a ser https://GFBmsoft@github.com/o/r.git")]
    public void Previa_diz_o_que_o_salvar_vai_mudar(string atual, string? trecho)
    {
        var previa = RemotoConfig.Previa(atual, "https://{{user}}@github.com/o/r.git", "GFBmsoft");
        if (trecho is null) Assert.Null(previa);
        else Assert.Contains(trecho, previa);
    }
}
