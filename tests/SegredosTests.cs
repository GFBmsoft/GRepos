using System;
using System.Text;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Segredos e variáveis do Actions: o "gh secret set" e o "gh variable set".</summary>
public class SegredosTests
{
    [Theory]
    [InlineData("DEPLOY_TOKEN", "")]
    [InlineData("deploy_token", "")] // o GitHub guarda em maiúsculas; minúscula não é erro
    [InlineData("", "Informe o nome.")]
    [InlineData("GITHUB_TOKEN", "O nome não pode começar com GITHUB_.")]
    [InlineData("1TOKEN", "O nome não pode começar com número.")]
    [InlineData("MEU TOKEN", "Use só letras, números e sublinhado (_), sem espaço nem acento.")]
    [InlineData("SENHA_AÇÃO", "Use só letras, números e sublinhado (_), sem espaço nem acento.")]
    public void Nome_segue_as_regras_do_GitHub(string nome, string problema) =>
        Assert.Equal(problema, GitHubService.ProblemaNoNome(nome));

    [Fact]
    public void Valor_sai_cifrado_e_so_a_chave_privada_abre()
    {
        // a chave do repositório é um par X25519: o app só vê a metade pública
        var par = Sodium.PublicKeyBox.GenerateKeyPair();
        var cifrado = GitHubService.Cifrar("ghp_segredo", par.PublicKey);

        Assert.DoesNotContain("ghp_segredo", cifrado);
        var aberto = Sodium.SealedPublicKeyBox.Open(Convert.FromBase64String(cifrado), par.PrivateKey, par.PublicKey);
        Assert.Equal("ghp_segredo", Encoding.UTF8.GetString(aberto));
    }

    [Fact]
    public void Listas_saem_da_resposta_em_ordem_de_nome()
    {
        var segredos = GitHubService.LerItensDeActions("""
        {"total_count":2,"secrets":[{"name":"ZETA","updated_at":"2026-10-15T12:00:00Z"},{"name":"ALFA","created_at":"2026-10-01T12:00:00Z"}]}
        """, "secrets");
        Assert.Equal(new[] { "ALFA", "ZETA" }, segredos.ConvertAll(s => s.Nome));
        Assert.NotNull(segredos[0].Atualizado);

        var variaveis = GitHubService.LerItensDeActions("""{"variables":[{"name":"AMBIENTE","value":"producao"}]}""", "variables");
        Assert.Equal("producao", variaveis[0].Valor);
        Assert.Empty(GitHubService.LerItensDeActions("""{"message":"Not Found"}""", "secrets"));
    }

    [Fact]
    public void Segredo_nunca_mostra_valor_e_editar_trava_o_nome()
    {
        var vm = new SegredosViewModel("a/b", "");
        vm.Aplicar(new[] { new ItemDeActions { Nome = "TOKEN" } }, new[] { new ItemDeActions { Nome = "AMBIENTE", Valor = "prod" } });

        Assert.Equal("••••••", vm.Segredos[0].Valor);
        Assert.Equal("prod", vm.Variaveis[0].Valor);

        vm.SegredoSelecionado = vm.Segredos[0];
        vm.TrocarSegredoCommand.Execute(null);
        Assert.Equal("TROCAR O VALOR DE TOKEN", vm.FormCabecalho);
        Assert.False(vm.NomeEditavel);
        Assert.Equal("", vm.FormValor); // o valor antigo não existe para mostrar

        vm.VariavelSelecionada = vm.Variaveis[0];
        vm.EditarVariavelCommand.Execute(null);
        Assert.Equal("prod", vm.FormValor);

        vm.NovoSegredoCommand.Execute(null);
        vm.FormNome = "github_x";
        Assert.False(vm.PodeSalvar);
        Assert.True(vm.TemProblemaNoNome);
    }
}
