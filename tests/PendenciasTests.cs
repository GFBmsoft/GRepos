using System.Linq;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>"Esperando por você", o gh status: pela busca do GitHub, sem notificações.</summary>
public class PendenciasTests
{
    [Fact]
    public void Resultado_da_busca_vira_pendencia_com_repositorio_e_tipo()
    {
        var lista = GitHubService.LerPendencias("""
        {"total_count":2,"items":[
          {"number":12,"title":"Log da esteira","html_url":"https://github.com/GFBmsoft/GRepos/pull/12",
           "repository_url":"https://api.github.com/repos/GFBmsoft/GRepos","user":{"login":"bia"},
           "pull_request":{"url":"x"},"draft":true,"updated_at":"2026-10-15T12:00:00Z"},
          {"number":3,"title":"Bug no boleto","html_url":"https://github.com/BMSoft/Financeiro/issues/3",
           "repository_url":"https://api.github.com/repos/BMSoft/Financeiro","user":{"login":"ana"}}]}
        """);

        Assert.Equal(2, lista.Count);
        Assert.Equal("GFBmsoft/GRepos", lista[0].Repo);
        Assert.True(lista[0].EhPr);
        Assert.True(lista[0].Rascunho);
        Assert.False(lista[1].EhPr);

        var item = new PendenciaViewModel { Pendencia = lista[1] };
        Assert.Equal("Financeiro #3", item.Onde);
        Assert.Empty(GitHubService.LerPendencias("""{"message":"Validation Failed"}"""));
    }

    [Fact]
    public void Colunas_mostram_os_primeiros_e_contam_o_resto()
    {
        var vm = new PendenciasViewModel("GFBmsoft");
        Pendencia P(int n) => new() { Numero = n, Repo = "a/b" };

        vm.Aplicar(Enumerable.Range(1, 9).Select(P), new[] { P(50) }, new Pendencia[0]);

        Assert.Equal(ColunaDePendencias.Visiveis, vm.Colunas[0].Itens.Count);
        Assert.Equal("mais 3 no GitHub", vm.Colunas[0].RestoTexto);
        Assert.Equal("REVISÃO PEDIDA A VOCÊ · 9", vm.Colunas[0].Cabecalho);
        Assert.True(vm.Colunas[2].SemItens);
        Assert.Equal("9 revisões pedidas · 1 atribuída a você", vm.Resumo);

        vm.Aplicar(new Pendencia[0], new Pendencia[0], new[] { P(1) });
        Assert.Equal("nada pedindo sua atenção", vm.Resumo);
    }
}
