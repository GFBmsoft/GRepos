using System.Linq;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Responsáveis, revisores e marco: o "gh pr edit --add-reviewer/--milestone".</summary>
public class PessoasTests
{
    [Fact]
    public void PR_e_issue_trazem_responsaveis_revisores_e_marco()
    {
        var pr = GitHubService.LerPullRequests("""
        [{"number":4,"state":"open","title":"x","user":{"login":"bia"},
          "head":{"ref":"feat/x","sha":"abc","repo":{"full_name":"fork/repo"}},"base":{"ref":"main"},
          "assignees":[{"login":"ana"}],"requested_reviewers":[{"login":"caio"},{"login":"dani"}],
          "milestone":{"number":2,"title":"1.1"}}]
        """).Single();

        Assert.Equal(new[] { "ana" }, pr.Responsaveis);
        Assert.Equal(new[] { "caio", "dani" }, pr.Revisores);
        Assert.Equal((2, "1.1"), (pr.MarcoNumero, pr.Marco));
        Assert.Equal("fork/repo", pr.OrigemRepo);

        var issue = GitHubService.LerIssues("""[{"number":9,"title":"y","state":"open","milestone":null}]""").Single();
        Assert.Equal(0, issue.MarcoNumero);
    }

    [Fact]
    public void Resumo_diz_so_o_que_existe()
    {
        Assert.Equal("sem responsável", PessoasViewModel.MontarResumo(new string[0], new string[0], new Marco(0, ""), ehPr: true));
        Assert.Equal("com ana, bia · revisão pedida a caio · marco 1.1",
            PessoasViewModel.MontarResumo(new[] { "ana", "bia" }, new[] { "caio" }, new Marco(2, "1.1"), ehPr: true));
        // issue não tem revisão
        Assert.Equal("com ana", PessoasViewModel.MontarResumo(new[] { "ana" }, new[] { "caio" }, new Marco(0, ""), ehPr: false));
    }

    [Fact]
    public void Editor_marca_quem_ja_esta_e_mantem_quem_saiu_do_repositorio()
    {
        var vm = new PessoasViewModel("", "", ehPr: true);
        vm.Mostrar(4, new[] { "ana" }, new[] { "ex-colega" }, new Marco(5, "fechado"));
        vm.MontarEditor(new[] { "bia", "ana" }, new[] { new Marco(2, "1.1") });

        Assert.Equal(new[] { "ana", "bia", "ex-colega" }, vm.Pessoas.Select(p => p.Login));
        Assert.True(vm.Pessoas[0].Responsavel);
        Assert.True(vm.Pessoas[2].Revisor);

        // "(nenhum)", os abertos e o atual, mesmo já fechado
        Assert.Equal(new[] { 0, 2, 5 }, vm.Marcos.Select(m => m.Numero));
        Assert.Equal(5, vm.MarcoEscolhido!.Numero);
    }

    [Fact]
    public void Trocar_de_item_fecha_o_editor()
    {
        var vm = new PessoasViewModel("", "", ehPr: false);
        vm.Mostrar(1, new string[0], new string[0], new Marco(0, ""));
        vm.MontarEditor(new[] { "ana" }, new Marco[0]);
        Assert.True(vm.Editando);

        vm.Mostrar(1, new[] { "ana" }, new string[0], new Marco(0, "")); // recarga do mesmo
        Assert.True(vm.Editando);
        vm.Mostrar(2, new string[0], new string[0], new Marco(0, ""));
        Assert.False(vm.Editando);
    }
}
