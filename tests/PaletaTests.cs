using System.Linq;
using System.Threading.Tasks;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Paleta de comandos (Ctrl+P): a busca, a ordem dos resultados e a navegação.</summary>
public class PaletaTests
{
    private static ItemDaPaleta Item(string titulo, string tipo = "repositório", string detalhe = "") =>
        new() { Titulo = titulo, Tipo = tipo, Detalhe = detalhe };

    private static readonly ItemDaPaleta[] Itens =
    {
        Item("Obter", "ação", "fetch em Financeiro"),
        Item("Financeiro", detalhe: "DBISAM · develop"),
        Item("Financeiro MySQL", detalhe: "MySQL · develop"),
        Item("NFCe", detalhe: "DBISAM · master"),
        Item("Emissão", detalhe: "DBISAM"),
        Item("feat/boleto-online", "branch"),
    };

    private static string[] Buscar(string consulta) =>
        Paleta.Filtrar(Itens, consulta).Select(i => i.Titulo).ToArray();

    [Fact]
    public void Sem_consulta_vem_tudo_na_ordem_original()
    {
        Assert.Equal(Itens.Select(i => i.Titulo), Buscar(""));
    }

    [Fact]
    public void Letras_em_ordem_bastam_e_nao_precisam_estar_juntas()
    {
        Assert.Equal(new[] { "Financeiro MySQL" }, Buscar("fmy"));
        Assert.Contains("feat/boleto-online", Buscar("bol"));
        Assert.Empty(Buscar("xyz"));
    }

    [Fact]
    public void Acento_e_maiuscula_nao_contam()
    {
        Assert.Equal("Emissão", Buscar("emissao").First());
        Assert.Equal("Emissão", Buscar("EMISSÃO").First());
    }

    [Fact]
    public void Titulo_vale_mais_que_detalhe_e_o_mais_curto_vem_antes()
    {
        var achados = Buscar("financeiro");

        // os dois repositórios antes da ação que só cita o Financeiro no detalhe
        Assert.Equal(new[] { "Financeiro", "Financeiro MySQL", "Obter" }, achados);
    }

    [Fact]
    public void Tipo_tambem_e_buscavel()
    {
        Assert.Equal(new[] { "feat/boleto-online" }, Buscar("branch"));
    }

    [Fact]
    public void Comeco_de_palavra_ganha_do_meio()
    {
        var itens = new[] { Item("Comissoes"), Item("imp/missao") };

        Assert.Equal("imp/missao", Paleta.Filtrar(itens, "mis").First().Titulo);
    }

    [Fact]
    public void Setas_dao_a_volta_e_o_enter_escolhe_o_selecionado()
    {
        var vm = new PaletaViewModel(Itens);
        Assert.Equal("Obter", vm.Selecionado!.Titulo);

        vm.Mover(-1);
        Assert.Equal("feat/boleto-online", vm.Selecionado!.Titulo);
        vm.Mover(1);
        Assert.Equal("Obter", vm.Selecionado!.Titulo);

        vm.Consulta = "nfc";
        Assert.Equal("NFCe", vm.Selecionado!.Titulo);
        Assert.True(vm.Escolher());
        Assert.Equal("NFCe", vm.Escolhido!.Titulo);
    }

    [Fact]
    public void Nada_encontrado_nao_escolhe()
    {
        var vm = new PaletaViewModel(Itens) { Consulta = "zzz" };

        Assert.True(vm.Vazio);
        vm.Mover(1);
        Assert.False(vm.Escolher());
    }

    [Fact]
    public void Branches_que_chegam_depois_entram_sem_tirar_a_selecao()
    {
        var vm = new PaletaViewModel(Itens);
        vm.Mover(2);
        var antes = vm.Selecionado;

        vm.Acrescentar(new[] { Item("develop", "branch") });

        Assert.Same(antes, vm.Selecionado);
        Assert.Contains(vm.Resultados, i => i.Titulo == "develop");
    }

    [Fact]
    public async Task Escolher_roda_a_acao_do_item()
    {
        var rodou = false;
        var vm = new PaletaViewModel(new[]
        {
            new ItemDaPaleta { Titulo = "Obter", Executar = () => { rodou = true; return Task.CompletedTask; } },
        });

        Assert.True(vm.Escolher());
        await vm.Escolhido!.Executar();

        Assert.True(rodou);
    }
}
