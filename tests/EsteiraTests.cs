using System;
using System.Linq;
using GRepos.Services;
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
