using System.Linq;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Disparo e reexecução. São as primeiras chamadas que **escrevem** no GitHub, então
/// o que importa testar é quando os botões podem ser usados — a execução em si depende
/// de rede e de um token com permissão.
/// </summary>
public class WorkflowDispatchTests
{
    private const string WorkflowsJson = """
    {
      "total_count": 3,
      "workflows": [
        { "id": 371242285, "name": "build", "path": ".github/workflows/build.yml", "state": "active" },
        { "id": 371242286, "name": "testes", "path": ".github/workflows/testing.yml", "state": "active" },
        { "id": 371242287, "name": "antigo", "path": ".github/workflows/old.yml", "state": "disabled_manually" }
      ]
    }
    """;

    [Fact]
    public void Workflows_saem_com_id_nome_e_arquivo()
    {
        var lista = GitHubService.LerWorkflows(WorkflowsJson);

        Assert.Equal(3, lista.Count);
        Assert.Equal(371242285, lista[0].Id);
        Assert.Equal("build", lista[0].Nome);
        Assert.Equal("build.yml", lista[0].Arquivo);

        // desativado não pode ser oferecido para disparo
        Assert.True(lista[0].Ativo);
        Assert.False(lista[2].Ativo);
    }

    [Fact]
    public void Resposta_sem_workflows_nao_quebra()
    {
        Assert.Empty(GitHubService.LerWorkflows("""{"total_count":0,"workflows":[]}"""));
        Assert.Empty(GitHubService.LerWorkflows("""{"message":"Not Found"}"""));
    }

    [Fact]
    public void Sem_workflow_carregado_nao_da_para_disparar()
    {
        var vm = new EsteiraViewModel("bm/repo", "main", "GFBmsoft", "Repo");

        Assert.False(vm.PodeDisparar);

        // a branch atual já vem preenchida: é o alvo mais provável
        Assert.Equal("main", vm.Referencia);
    }

    [Fact]
    public void Reexecutar_depende_da_execucao_escolhida()
    {
        var vm = new EsteiraViewModel("bm/repo", "main", "GFBmsoft", "Repo");
        Assert.False(vm.PodeReexecutar);

        // execução rodando não se reexecuta
        vm.Selecionada = new CiExecucaoViewModel
        {
            Execucao = new CiExecucao { Id = 1, Situacao = "rodando" },
        };
        Assert.False(vm.PodeReexecutar);
        Assert.False(vm.PodeReexecutarFalhas);

        // terminada com sucesso: dá para reexecutar tudo, mas não "só as falhas"
        vm.Selecionada = new CiExecucaoViewModel
        {
            Execucao = new CiExecucao { Id = 2, Situacao = "sucesso" },
        };
        Assert.True(vm.PodeReexecutar);
        Assert.False(vm.PodeReexecutarFalhas);

        vm.Selecionada = new CiExecucaoViewModel
        {
            Execucao = new CiExecucao { Id = 3, Situacao = "falha" },
        };
        Assert.True(vm.PodeReexecutar);
        Assert.True(vm.PodeReexecutarFalhas);
    }

    [Fact]
    public void Enquanto_executa_os_botoes_ficam_fora()
    {
        var vm = new EsteiraViewModel("bm/repo", "main", "GFBmsoft", "Repo")
        {
            Selecionada = new CiExecucaoViewModel
            {
                Execucao = new CiExecucao { Id = 3, Situacao = "falha" },
            },
        };

        Assert.True(vm.PodeReexecutar);

        vm.Executando = true;
        Assert.False(vm.PodeReexecutar);
        Assert.False(vm.PodeReexecutarFalhas);
        Assert.False(vm.PodeDisparar);
    }
}
