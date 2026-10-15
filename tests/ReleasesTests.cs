using System.Linq;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Releases pela API: o que o "gh release create/upload" faz.</summary>
public class ReleasesTests
{
    [Theory]
    [InlineData("1.0.0.46", "1.0.0.47")]
    [InlineData("v2.9", "v2.10")]
    [InlineData("v1.0.0-rc09", "v1.0.0-rc10")]
    [InlineData("release-007", "release-008")]
    [InlineData("estavel", "")]
    [InlineData("", "")]
    public void Sugere_a_proxima_tag_somando_um_no_ultimo_numero(string ultima, string esperada) =>
        Assert.Equal(esperada, ProximaVersao.Sugerir(ultima));

    [Fact]
    public void Release_traz_id_rascunho_e_pre_release()
    {
        var r = GitHubService.LerRelease("""
        {"id":77,"tag_name":"1.0.0.47","name":"","draft":true,"prerelease":false,"body":"",
         "created_at":"2026-10-15T12:00:00Z","published_at":null,
         "assets":[{"id":5,"name":"a.exe","size":10,"browser_download_url":"https://x"}]}
        """)!;

        Assert.Equal(77, r.Id);
        Assert.True(r.Rascunho);
        Assert.False(r.PreRelease);
        Assert.Equal(5, r.Arquivos[0].Id);
        Assert.NotNull(r.Publicada); // rascunho não tem published_at: vale a criação
    }

    [Fact]
    public void Tag_repetida_e_falta_de_permissao_viram_texto_claro()
    {
        var repetida = GitHubService.MotivoDaRecusa(422,
            """{"message":"Validation Failed","errors":[{"resource":"Release","code":"already_exists","field":"tag_name"}]}""");
        Assert.Contains("já existe uma release com essa tag", repetida);

        Assert.Contains("conteúdo", GitHubService.MotivoDaRecusa(403, "{}", "conteúdo do repositório"));
        Assert.Contains("pull requests", GitHubService.MotivoDaRecusa(403, "{}"));
    }

    [Fact]
    public void Lista_mantem_a_escolhida_e_mostra_os_selos()
    {
        var vm = new ReleasesViewModel("a/b", "");
        vm.Aplicar(new[]
        {
            new Release { Id = 2, Tag = "1.0.0.2", Rascunho = true },
            new Release { Id = 1, Tag = "1.0.0.1", Nome = "Primeira", PreRelease = true },
        });

        Assert.Equal(2, vm.Selecionada!.Release.Id);
        Assert.Equal("rascunho", vm.Lista[0].Selo);
        Assert.Equal("pré-release", vm.Lista[1].Selo);
        Assert.Equal("1.0.0.2", vm.Lista[0].Titulo); // sem nome, o título é a tag
        Assert.Equal("Primeira", vm.Lista[1].Titulo);

        vm.Selecionada = vm.Lista[1];
        vm.Aplicar(vm.Lista.Select(i => i.Release).ToList());
        Assert.Equal(1, vm.Selecionada!.Release.Id);
    }

    [Fact]
    public void Tag_que_ainda_nao_existe_pede_o_ponto_de_partida()
    {
        var vm = new ReleasesViewModel("a/b", "");
        vm.TagsLocais.Add("1.0.0.46");
        vm.NovaReleaseCommand.Execute(null);

        vm.FormTag = "1.0.0.46";
        Assert.False(vm.TagNova);
        vm.FormTag = "1.0.0.47";
        Assert.True(vm.TagNova);
        Assert.Equal("Publicar release", vm.SalvarRotulo);
        vm.FormRascunho = true;
        Assert.Equal("Salvar rascunho", vm.SalvarRotulo);
    }
}
