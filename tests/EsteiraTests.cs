using System.Collections.Generic;
using System;
using System.Linq;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Leitura das respostas do GitHub Actions e os rótulos que a janela da esteira
/// mostra. A API é fixa e documentada: dá para testar sem rede, com a resposta real.
/// </summary>
public class EsteiraTests
{
    private const string RunsJson = """
    {
      "total_count": 2,
      "workflow_runs": [
        {
          "id": 987654321,
          "name": "build",
          "display_title": "Fix missing files",
          "head_branch": "main",
          "run_number": 42,
          "status": "completed",
          "conclusion": "failure",
          "html_url": "https://github.com/bm/repo/actions/runs/987654321",
          "created_at": "2026-09-30T12:00:00Z",
          "run_started_at": "2026-09-30T12:00:10Z",
          "updated_at": "2026-09-30T12:03:00Z",
          "actor": { "login": "GFBmsoft" }
        },
        {
          "id": 987654320,
          "name": "build",
          "display_title": "Add Services e EnvTests",
          "head_branch": "main",
          "run_number": 41,
          "status": "in_progress",
          "conclusion": null,
          "html_url": "https://github.com/bm/repo/actions/runs/987654320",
          "created_at": "2026-09-30T11:00:00Z",
          "actor": { "login": "GFBmsoft" }
        }
      ]
    }
    """;

    private const string JobsJson = """
    {
      "total_count": 1,
      "jobs": [
        {
          "name": "build (windows-latest)",
          "status": "completed",
          "conclusion": "failure",
          "html_url": "https://github.com/bm/repo/actions/runs/1/job/2",
          "started_at": "2026-09-30T12:00:10Z",
          "completed_at": "2026-09-30T12:02:30Z",
          "steps": [
            {
              "name": "Set up job",
              "status": "completed",
              "conclusion": "success",
              "number": 1,
              "started_at": "2026-09-30T12:00:10Z",
              "completed_at": "2026-09-30T12:00:14Z"
            },
            {
              "name": "dotnet test",
              "status": "completed",
              "conclusion": "failure",
              "number": 2,
              "started_at": "2026-09-30T12:00:14Z",
              "completed_at": "2026-09-30T12:01:37Z"
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Execucoes_saem_da_resposta_com_situacao_traduzida()
    {
        var lista = GitHubService.LerExecucoes(RunsJson);

        Assert.Equal(2, lista.Count);
        Assert.Equal(987654321, lista[0].Id);
        Assert.Equal(42, lista[0].Numero);
        Assert.Equal("falha", lista[0].Situacao);
        Assert.Equal("Fix missing files", lista[0].Titulo);
        Assert.Equal("GFBmsoft", lista[0].Autor);
        Assert.Equal("main", lista[0].Branch);

        // sem conclusion e ainda em progresso: continua "rodando", não "indisponivel"
        Assert.Equal("rodando", lista[1].Situacao);
    }

    /// <summary>
    /// Build de tag traz o nome da tag no head_branch. Filtrar por "main" o esconde —
    /// foi o que fez a execução da 1.0.0.10 não aparecer no app.
    /// </summary>
    [Fact]
    public void Build_de_tag_rodando_aparece_mesmo_estando_fora_da_branch()
    {
        var lista = new[]
        {
            new CiExecucao { Id = 20, Branch = "1.0.0.10", Situacao = "rodando", Titulo = "Painel" },
            new CiExecucao { Id = 19, Branch = "main", Situacao = "sucesso", Titulo = "Painel" },
        };

        var escolhida = GitHubService.EscolherExecucao(lista, "main");

        Assert.NotNull(escolhida);
        Assert.Equal(20, escolhida!.Id);
        Assert.Equal("1.0.0.10", escolhida.Branch);
    }

    [Fact]
    public void Terminado_o_build_de_tag_a_barra_volta_a_falar_da_branch()
    {
        var lista = new[]
        {
            new CiExecucao { Id = 20, Branch = "1.0.0.10", Situacao = "sucesso" },
            new CiExecucao { Id = 19, Branch = "main", Situacao = "falha" },
        };

        // nada rodando: vale a da branch, mesmo sendo mais antiga
        var escolhida = GitHubService.EscolherExecucao(lista, "main");
        Assert.Equal(19, escolhida!.Id);
        Assert.Equal("falha", escolhida.Situacao);
    }

    [Fact]
    public void Sem_execucao_da_branch_vale_a_mais_recente()
    {
        var lista = new[]
        {
            new CiExecucao { Id = 20, Branch = "1.0.0.10", Situacao = "sucesso" },
            new CiExecucao { Id = 19, Branch = "outra", Situacao = "falha" },
        };

        Assert.Equal(20, GitHubService.EscolherExecucao(lista, "feat/nova")!.Id);
        Assert.Null(GitHubService.EscolherExecucao(System.Array.Empty<CiExecucao>(), "main"));
    }

    [Fact]
    public void Resposta_sem_execucoes_nao_quebra()
    {
        Assert.Empty(GitHubService.LerExecucoes("""{"total_count":0,"workflow_runs":[]}"""));
        Assert.Empty(GitHubService.LerExecucoes("""{"message":"Not Found"}"""));
    }

    [Fact]
    public void Jobs_trazem_os_passos_em_ordem_com_duracao()
    {
        var jobs = GitHubService.LerJobs(JobsJson);

        Assert.Single(jobs);
        Assert.Equal("falha", jobs[0].Situacao);
        Assert.Equal(TimeSpan.FromSeconds(140), jobs[0].Duracao);

        var passos = jobs[0].Etapas;
        Assert.Equal(2, passos.Count);
        Assert.Equal(new[] { 1, 2 }, passos.Select(p => p.Numero));
        Assert.Equal("sucesso", passos[0].Situacao);
        Assert.Equal(TimeSpan.FromSeconds(4), passos[0].Duracao);

        // o passo que quebrou é o que interessa achar na janela
        Assert.Equal("dotnet test", passos[1].Nome);
        Assert.Equal("falha", passos[1].Situacao);
    }

    [Fact]
    public void Passo_ainda_rodando_fica_sem_duracao()
    {
        var jobs = GitHubService.LerJobs("""
        {"jobs":[{"name":"build","status":"in_progress","conclusion":null,
          "steps":[{"name":"dotnet build","status":"in_progress","conclusion":null,"number":1,
                    "started_at":"2026-09-30T12:00:00Z","completed_at":null}]}]}
        """);

        Assert.Equal("rodando", jobs[0].Situacao);
        Assert.Null(jobs[0].Etapas[0].Duracao);
    }

    [Theory]
    [InlineData("main", "main")]
    [InlineData("fix/CorrecaoTbEdit", "CorrecaoTbEdit")]
    [InlineData("feature/relatorio-de-comissoes", "relatorio-de-…")]
    [InlineData("", "—")]
    public void Nome_de_branch_cabe_no_botao(string entrada, string esperado)
    {
        var curto = Rotulos.Branch(entrada);

        Assert.Equal(esperado, curto);
        Assert.True(curto.Length <= Rotulos.LimiteBranch);
    }

    /// <summary>
    /// O ponto da atualização automática: a linha precisa ser **a mesma** depois do
    /// ciclo, com o conteúdo novo. Trocar o item recriaria a linha na tela e levaria
    /// junto a rolagem e a seleção do usuário, bem enquanto ele acompanha o build.
    /// </summary>
    [Fact]
    public void Atualizar_muda_o_conteudo_sem_recriar_a_linha()
    {
        var lista = new System.Collections.ObjectModel.ObservableCollection<CiEtapaViewModel>();

        void Aplicar(params CiEtapa[] etapas) => ListaSync.AplicarModelos(
            lista, etapas,
            item => item.Etapa.Numero.ToString(),
            modelo => modelo.Numero.ToString(),
            (item, modelo) => item.Etapa = modelo,
            modelo => new CiEtapaViewModel { Etapa = modelo });

        Aplicar(
            new CiEtapa { Numero = 1, Nome = "Checkout", Situacao = "sucesso" },
            new CiEtapa { Numero = 2, Nome = "dotnet test", Situacao = "rodando" });

        var primeira = lista[0];
        var segunda = lista[1];
        Assert.Equal("●", segunda.Simbolo);

        // o passo terminou e um terceiro começou
        Aplicar(
            new CiEtapa { Numero = 1, Nome = "Checkout", Situacao = "sucesso" },
            new CiEtapa { Numero = 2, Nome = "dotnet test", Situacao = "falha", Duracao = TimeSpan.FromSeconds(83) },
            new CiEtapa { Numero = 3, Nome = "Publicar", Situacao = "rodando" });

        Assert.Same(primeira, lista[0]);
        Assert.Same(segunda, lista[1]); // mesma instância...
        Assert.Equal("✕", segunda.Simbolo); // ...com o conteúdo novo
        Assert.Equal("1m 23s", segunda.DuracaoTexto);
        Assert.Equal("SemiBold", segunda.Peso);
        Assert.Equal(3, lista.Count);
    }

    [Fact]
    public void Execucao_que_sumiu_da_resposta_sai_da_lista()
    {
        var lista = new System.Collections.ObjectModel.ObservableCollection<CiExecucaoViewModel>();

        void Aplicar(params CiExecucao[] execucoes) => ListaSync.AplicarModelos(
            lista, execucoes,
            item => item.Execucao.Id.ToString(),
            modelo => modelo.Id.ToString(),
            (item, modelo) => item.Execucao = modelo,
            modelo => new CiExecucaoViewModel { Execucao = modelo });

        Aplicar(
            new CiExecucao { Id = 2, Situacao = "rodando" },
            new CiExecucao { Id = 1, Situacao = "sucesso" });
        var antiga = lista[1];

        // execução nova entra no topo e a mais velha sai da página
        Aplicar(
            new CiExecucao { Id = 3, Situacao = "rodando" },
            new CiExecucao { Id = 2, Situacao = "sucesso" });

        Assert.Equal(2, lista.Count);
        Assert.Equal(3, lista[0].Execucao.Id);
        Assert.Equal("passou", lista[1].Rodape.Split(' ')[0]);
        Assert.DoesNotContain(antiga, lista);
    }

    /// <summary>
    /// A esteira mostra as últimas N e recolhe o resto. O recorte precisa conviver com a
    /// atualização automática sem recriar as linhas, senão a rolagem e o cartão aberto
    /// se perdem a cada ciclo.
    /// </summary>
    [Fact]
    public void Mostra_as_ultimas_e_recolhe_o_resto()
    {
        var vm = new EsteiraViewModel("bm/repo", "main", "", "Repo", null, visiveis: 3);
        Alimentar(vm, Execucoes(10));

        Assert.Equal(3, vm.Execucoes.Count);
        Assert.Equal(7, vm.Recolhidas);
        Assert.True(vm.TemRecolhidas);
        Assert.Equal("mostrar mais 7", vm.TextoRecolhidas);

        // a mais recente é a que abre
        Assert.Equal(10, vm.Selecionada!.Execucao.Id);
        var primeira = vm.Execucoes[0];

        vm.Expandido = true;
        Assert.Equal(10, vm.Execucoes.Count);
        Assert.False(vm.TemRecolhidas);
        Assert.Equal("mostrar menos", vm.TextoRecolhidas);

        // expandir não recria o que já estava na tela
        Assert.Same(primeira, vm.Execucoes[0]);
        Assert.Same(primeira, vm.Selecionada);
    }

    [Fact]
    public void Filtro_por_situacao_conta_e_restringe()
    {
        var vm = new EsteiraViewModel("bm/repo", "main", "", "Repo", null, visiveis: 20);
        Alimentar(vm, new[]
        {
            new CiExecucao { Id = 5, Situacao = "rodando" },
            new CiExecucao { Id = 4, Situacao = "falha" },
            new CiExecucao { Id = 3, Situacao = "sucesso" },
            new CiExecucao { Id = 2, Situacao = "sucesso" },
            new CiExecucao { Id = 1, Situacao = "cancelado" },
        });

        Assert.Equal(5, vm.TotalTodas);
        Assert.Equal(2, vm.TotalSucesso);
        Assert.Equal(1, vm.TotalFalha);
        Assert.Equal(1, vm.TotalRodando);
        Assert.Equal("✓ 2", vm.RotuloSucesso);

        vm.Filtro = "falha";
        Assert.Single(vm.Execucoes);
        Assert.Equal(4, vm.Execucoes[0].Execucao.Id);

        // o cartão aberto tinha saído pelo filtro: a seleção acompanha
        Assert.Equal(4, vm.Selecionada!.Execucao.Id);

        vm.Filtro = "";
        Assert.Equal(5, vm.Execucoes.Count);
    }

    [Fact]
    public void Filtrar_de_novo_na_mesma_situacao_volta_para_todas()
    {
        var vm = new EsteiraViewModel("bm/repo", "main", "", "Repo", null, visiveis: 20);
        Alimentar(vm, Execucoes(4));

        vm.FiltrarCommand.Execute("sucesso");
        Assert.Equal("sucesso", vm.Filtro);

        vm.FiltrarCommand.Execute("sucesso");
        Assert.Equal("", vm.Filtro);
    }

    /// <summary>
    /// EV12: filtrar por uma situação sem execuções esvaziava a lista e a seleção, mas os
    /// passos da execução anterior ficavam na tela, por baixo dos avisos.
    /// </summary>
    [Fact]
    public void Filtro_sem_resultado_leva_os_passos_junto()
    {
        var vm = new EsteiraViewModel("", "main", "", "Repo", null, visiveis: 20);
        Alimentar(vm, Execucoes(8));
        vm.Jobs.Add(new CiJobViewModel { Job = new CiJob { Nome = "build" } });

        vm.Filtro = "falha";

        Assert.Empty(vm.Execucoes);
        Assert.Null(vm.Selecionada);
        Assert.Empty(vm.Jobs);

        // um aviso só, e que não negue os contadores ao lado
        Assert.True(vm.SemExecucoes);
        Assert.False(vm.SemSelecao);
        Assert.False(vm.SemJobs);
        Assert.StartsWith("Nenhuma execução nesta situação", vm.TextoSemExecucoes);

        vm.Filtro = "";
        Assert.Equal(8, vm.Execucoes.Count);
        Assert.NotNull(vm.Selecionada);
    }

    /// <summary>
    /// EV14: os passos de artefato num build de tag têm "if:" e não rodam. Apareciam com
    /// um ponto e "0s", como se faltasse informação; são passos pulados de propósito.
    /// </summary>
    [Fact]
    public void Passo_pulado_aparece_como_pulado_e_nao_como_sem_informacao()
    {
        var job = GitHubService.LerJobs("""
            {"jobs":[{"name":"build","status":"completed","conclusion":"success","steps":[
              {"name":"Nomear os executáveis","status":"completed","conclusion":"success","number":8,
               "started_at":"2026-10-07T14:00:00Z","completed_at":"2026-10-07T14:00:01Z"},
              {"name":"Artefato (runtime)","status":"completed","conclusion":"skipped","number":9,
               "started_at":"2026-10-07T14:00:01Z","completed_at":"2026-10-07T14:00:01Z"}
            ]}]}
            """).Single();

        Assert.Equal(new[] { "sucesso", "pulado" }, job.Etapas.Select(e => e.Situacao));

        var passou = new CiEtapaViewModel { Etapa = job.Etapas[0] };
        var pulado = new CiEtapaViewModel { Etapa = job.Etapas[1] };

        Assert.Equal("⊘", pulado.Simbolo);
        Assert.Equal("pulado", pulado.DuracaoTexto); // e não "0s"
        Assert.Equal("TextFaint", pulado.CorDoNome);

        Assert.Equal("✓", passou.Simbolo);
        Assert.Equal("1s", passou.DuracaoTexto);
        Assert.Equal("Text", passou.CorDoNome);
    }

    [Fact]
    public void Trocar_de_execucao_nao_deixa_os_passos_da_anterior()
    {
        var vm = new EsteiraViewModel("", "main", "", "Repo", null, visiveis: 20);
        Alimentar(vm, Execucoes(3));
        vm.Jobs.Add(new CiJobViewModel { Job = new CiJob { Nome = "build" } });

        vm.Selecionada = vm.Execucoes[1];

        Assert.Empty(vm.Jobs);
    }

    private static CiExecucao[] Execucoes(int quantas) =>
        Enumerable.Range(1, quantas)
            .Reverse()
            .Select(i => new CiExecucao { Id = i, Numero = i, Situacao = "sucesso" })
            .ToArray();

    /// <summary>Preenche a lista interna sem rede, pelo mesmo caminho do carregamento.</summary>
    private static void Alimentar(EsteiraViewModel vm, IReadOnlyList<CiExecucao> execucoes)
    {
        var campo = typeof(EsteiraViewModel).GetField("_todas",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        campo.SetValue(vm, execucoes);

        typeof(EsteiraViewModel).GetMethod("AplicarFiltro",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, null);
    }

    [Fact]
    public void O_ritmo_e_mais_rapido_quando_algo_esta_rodando()
    {
        // acompanhar um build pedindo de 30 em 30 segundos seria inútil; e pedir de 8 em
        // 8 com tudo parado só gastaria a cota por hora da API
        Assert.True(EsteiraViewModel.IntervaloRodando < EsteiraViewModel.IntervaloParado);
        Assert.True(EsteiraViewModel.IntervaloRodando >= TimeSpan.FromSeconds(5));
    }

    [Theory]
    // o workflow carimba quatro números; só isso é versão publicada
    [InlineData("1.0.0.1+9dfd09b", "1.0.0.1")]
    [InlineData("0.0.0.0-dev.7+9dfd09b", "0.0.0.0-dev.7")]
    [InlineData("1.0.0.1", "1.0.0.1")]
    // build local fica com o padrão do SDK e não pode parecer versão lançada
    [InlineData("1.0.0+9dfd09b", "")]
    [InlineData("1.0.0", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void So_a_versao_carimbada_pelo_workflow_aparece(string? informacional, string esperado)
    {
        Assert.Equal(esperado, Rotulos.VersaoPublicada(informacional));
    }

    [Fact]
    public void Quando_e_duracao_viram_texto_curto()
    {
        var agora = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal("agora", Rotulos.Quando(agora.AddSeconds(-20), agora));
        Assert.Equal("há 12 min", Rotulos.Quando(agora.AddMinutes(-12), agora));
        Assert.Equal("há 3 h", Rotulos.Quando(agora.AddHours(-3), agora));
        Assert.Equal("há 2 d", Rotulos.Quando(agora.AddDays(-2), agora));
        Assert.Equal("", Rotulos.Quando(null, agora));

        Assert.Equal("8s", Rotulos.Duracao(TimeSpan.FromSeconds(8)));
        Assert.Equal("1m 23s", Rotulos.Duracao(TimeSpan.FromSeconds(83)));
        Assert.Equal("1h 05m", Rotulos.Duracao(TimeSpan.FromMinutes(65)));
        Assert.Equal("", Rotulos.Duracao(null));
    }
}
