using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Pull requests dentro do app: leitura das respostas do GitHub, a tradução das recusas,
/// as regras do formulário de PR novo e a situação local da branch.
/// </summary>
public class PullRequestsTests
{
    private const string PrJson = """
        {
          "number": 42, "title": "Boleto online", "state": "open", "draft": false,
          "html_url": "https://github.com/bm/fin/pull/42", "body": "Fecha o **#40**.",
          "user": { "login": "GFBmsoft" }, "updated_at": "2026-10-07T12:00:00Z", "merged_at": null,
          "head": { "ref": "imp/boleto", "sha": "abc123" }, "base": { "ref": "develop", "sha": "def456" },
          "commits": 3, "changed_files": 5, "additions": 120, "deletions": 30, "mergeable_state": "clean"
        }
        """;

    [Fact]
    public void Pr_traz_origem_destino_e_tamanho()
    {
        var pr = GitHubService.LerPullRequest(PrJson)!;

        Assert.Equal(42, pr.Numero);
        Assert.Equal("imp/boleto", pr.Origem);
        Assert.Equal("develop", pr.Destino);
        Assert.Equal("abc123", pr.Sha);
        Assert.Equal("aberto", pr.Estado);
        Assert.Equal("Fecha o **#40**.", pr.Corpo);
        Assert.Equal((3, 5, 120, 30), (pr.Commits, pr.Arquivos, pr.Adicionadas, pr.Removidas));
        Assert.Equal("clean", pr.Mesclagem);
    }

    [Fact]
    public void Listagem_sem_head_nem_corpo_nao_quebra()
    {
        var prs = GitHubService.LerPullRequests("""[{"number":1,"title":"x","state":"closed","merged_at":"2026-10-01T00:00:00Z","body":null,"head":null}]""");

        Assert.Single(prs);
        Assert.Equal("mesclado", prs[0].Estado);
        Assert.Equal("", prs[0].Origem);
        Assert.Equal("", prs[0].Corpo);
    }

    [Fact]
    public void Verificacoes_viram_uma_situacao_so()
    {
        var todas = GitHubService.LerVerificacoes("""
            {"check_runs":[
              {"name":"build","status":"completed","conclusion":"success","html_url":"u"},
              {"name":"lint","status":"completed","conclusion":"skipped"},
              {"name":"deploy","status":"in_progress","conclusion":null}
            ]}
            """);

        Assert.Equal(new[] { "sucesso", "sucesso", "rodando" }, todas.Select(v => v.Situacao));
        Assert.Equal("rodando", GitHubService.ResumoDasVerificacoes(todas));
        Assert.Equal("sucesso", GitHubService.ResumoDasVerificacoes(todas.Take(2).ToList()));

        // uma quebrada reprova mesmo com outra ainda rodando
        var comFalha = todas.Append(new Verificacao { Nome = "testes", Situacao = "falha" }).ToList();
        Assert.Equal("falha", GitHubService.ResumoDasVerificacoes(comFalha));

        Assert.Equal("nenhum", GitHubService.ResumoDasVerificacoes(Array.Empty<Verificacao>()));
        Assert.Empty(GitHubService.LerVerificacoes("""{"message":"Not Found"}"""));
    }

    [Theory]
    [InlineData(422, """{"message":"Validation Failed","errors":[{"message":"A pull request already exists for bm:imp/x."}]}""", "já existe um pull request")]
    [InlineData(422, """{"message":"Validation Failed","errors":[{"message":"No commits between develop and imp/x"}]}""", "não há commits de diferença")]
    [InlineData(422, """{"message":"Validation Failed","errors":[{"resource":"PullRequest","field":"head","code":"invalid"}]}""", "não existe no GitHub")]
    [InlineData(405, """{"message":"Pull Request is not mergeable"}""", "não pode ser mesclado")]
    [InlineData(405, """{"message":"Squash merges are not allowed on this repository."}""", "não permite essa forma")]
    [InlineData(403, """{"message":"Resource not accessible by personal access token"}""", "permissão de escrita em pull requests")]
    [InlineData(500, "<html>", "respondeu 500")]
    public void Recusa_do_github_sai_em_portugues(int codigo, string json, string esperado)
    {
        Assert.Contains(esperado, GitHubService.MotivoDaRecusa(codigo, json));
    }

    // ------------------------------------------------------------ ViewModel

    private static PullRequest Pr(int numero, string origem, string estado = "aberto", bool rascunho = false) =>
        new() { Numero = numero, Titulo = "PR " + numero, Origem = origem, Destino = "develop", Estado = estado, Rascunho = rascunho };

    private static PullRequestsViewModel Vm(string branch = "imp/boleto", bool enviada = true, int naoEnviados = 0) =>
        new(new ContextoPr("", branch, "", "Financeiro")
        {
            Destinos = new[] { "develop", "master", branch },
            TituloSugerido = "Boleto online",
            BranchEnviada = enviada,
            NaoEnviados = naoEnviados,
        });

    [Fact]
    public void Abre_no_pr_da_branch_atual()
    {
        var vm = Vm();
        vm.Aplicar(new[] { Pr(9, "feat/outra"), Pr(7, "imp/boleto") });

        Assert.Equal(7, vm.Selecionada!.Pr.Numero);
        Assert.Equal("imp/boleto → develop", vm.Selecionada.Caminho);
    }

    [Fact]
    public void Formulario_sugere_titulo_e_tira_a_propria_branch_dos_destinos()
    {
        var vm = Vm();

        Assert.Equal("Boleto online", vm.NovoTitulo);
        Assert.Equal(new[] { "develop", "master" }, vm.Destinos);
        Assert.Equal("develop", vm.NovoDestino);
        Assert.True(vm.PodeCriar);
        Assert.Equal("", vm.AvisoNovo);
    }

    [Fact]
    public void Branch_que_nao_subiu_nao_cria_pr()
    {
        var vm = Vm(enviada: false);

        Assert.False(vm.PodeCriar);
        Assert.Contains("ainda não está no GitHub", vm.AvisoNovo);
    }

    [Fact]
    public void Commit_nao_enviado_avisa_mas_deixa_criar()
    {
        var vm = Vm(naoEnviados: 2);

        Assert.True(vm.PodeCriar);
        Assert.Contains("2 commits locais", vm.AvisoNovo);
    }

    [Fact]
    public void Branch_com_pr_aberto_nao_cria_outro()
    {
        var vm = Vm();
        vm.Aplicar(new[] { Pr(7, "imp/boleto") });

        Assert.False(vm.PodeCriar);
        Assert.Contains("#7", vm.AvisoNovo);

        // mesclado não conta: a branch pode ganhar um PR novo
        vm.Aplicar(new[] { Pr(7, "imp/boleto", "mesclado") });
        Assert.True(vm.PodeCriar);
    }

    [Fact]
    public void Sem_titulo_nao_cria()
    {
        var vm = Vm();
        vm.NovoTitulo = "   ";

        Assert.False(vm.PodeCriar);
        Assert.Equal("Informe o título.", vm.AvisoNovo);
    }

    [Fact]
    public void Formulario_e_detalhe_nao_aparecem_juntos()
    {
        var vm = Vm();
        vm.Aplicar(new[] { Pr(9, "feat/outra") });
        Assert.True(vm.MostraDetalhe);

        vm.NovoCommand.Execute(null);
        Assert.True(vm.Criando);
        Assert.Null(vm.Selecionada);
        Assert.False(vm.MostraDetalhe);
        Assert.False(vm.SemSelecao);

        // clicar num PR da lista sai do formulário
        vm.Selecionada = vm.Lista[0];
        Assert.False(vm.Criando);
        Assert.True(vm.MostraDetalhe);
    }

    [Fact]
    public void Trocar_de_pr_nao_deixa_o_detalhe_do_anterior()
    {
        var vm = Vm();
        vm.Aplicar(new[] { Pr(9, "feat/outra"), Pr(8, "feat/mais") });
        vm.Detalhe = new PullRequest { Numero = 9, Mesclagem = "clean" };
        vm.Verificacoes.Add(new VerificacaoViewModel());
        Assert.True(vm.PodeMesclar);

        vm.Selecionada = vm.Lista[1];

        Assert.Null(vm.Detalhe);
        Assert.Empty(vm.Verificacoes);
        Assert.False(vm.PodeMesclar); // até o GitHub dizer a situação do novo
    }

    [Theory]
    [InlineData("clean", true)]
    [InlineData("behind", true)]
    [InlineData("unstable", true)]
    [InlineData("dirty", false)]
    [InlineData("blocked", false)]
    public void Mesclar_depende_da_situacao_no_github(string situacao, bool mesclavel)
    {
        var item = Pr(1, "imp/x");
        var detalhe = new PullRequest { Numero = 1, Mesclagem = situacao };

        Assert.Equal(mesclavel, PullRequestsViewModel.SituacaoDeMesclagem(item, detalhe).Mesclavel);
    }

    [Fact]
    public void Rascunho_fechado_e_mesclado_nao_mesclam()
    {
        var limpo = new PullRequest { Mesclagem = "clean" };

        Assert.False(PullRequestsViewModel.SituacaoDeMesclagem(Pr(1, "a", rascunho: true), limpo).Mesclavel);
        Assert.False(PullRequestsViewModel.SituacaoDeMesclagem(Pr(1, "a", "fechado"), limpo).Mesclavel);
        Assert.False(PullRequestsViewModel.SituacaoDeMesclagem(Pr(1, "a", "mesclado"), limpo).Mesclavel);
        Assert.Equal("rascunho", new PrViewModel { Pr = Pr(1, "a", rascunho: true) }.Estado);
    }

    // ------------------------------------------------ acompanhar o Actions

    private static readonly DateTime Agora = new(2026, 10, 7, 15, 0, 0, DateTimeKind.Utc);

    private static Verificacao V(string nome, string situacao) => new() { Nome = nome, Situacao = situacao };

    private static (string Texto, string Cor, bool Acompanhar) Ci(DateTime? mexido, params Verificacao[] v) =>
        PullRequestsViewModel.SituacaoDasVerificacoes(
            new PullRequest { Numero = 1, Atualizado = mexido }, null, v, consultado: true, Agora);

    [Fact]
    public void Pr_recem_criado_sem_verificacao_esta_aguardando_o_actions()
    {
        var recente = Ci(Agora.AddSeconds(-20));
        Assert.Equal("Aguardando o GitHub Actions começar…", recente.Texto);
        Assert.True(recente.Acompanhar);

        // passado o prazo, é porque o repositório não roda verificação nenhuma
        var antigo = Ci(Agora.AddMinutes(-30));
        Assert.Equal("Nenhuma verificação rodou neste pull request.", antigo.Texto);
        Assert.False(antigo.Acompanhar);
    }

    [Fact]
    public void Rodando_diz_quantas_terminaram_e_qual_esta_em_andamento()
    {
        var r = Ci(Agora, V("build", "sucesso"), V("testes", "rodando"), V("deploy", "rodando"));

        Assert.Equal("Actions rodando: 1 de 3 concluída(s) — agora: testes, deploy", r.Texto);
        Assert.Equal("Yellow", r.Cor);
        Assert.True(r.Acompanhar);

        // uma já quebrou: não precisa esperar o resto para saber
        var comFalha = Ci(Agora, V("build", "falha"), V("testes", "rodando"));
        Assert.Contains("1 já quebrou", comFalha.Texto);
        Assert.Equal("Red", comFalha.Cor);
    }

    [Fact]
    public void Terminado_diz_se_passou_ou_quebrou_e_para_de_acompanhar()
    {
        Assert.Equal(("As 2 verificações passaram.", "Green", false), Ci(Agora, V("a", "sucesso"), V("b", "sucesso")));
        Assert.Equal(("A verificação passou.", "Green", false), Ci(Agora, V("a", "sucesso")));
        Assert.Equal(("1 de 2 verificações quebraram.", "Red", false), Ci(Agora, V("a", "sucesso"), V("b", "falha")));
    }

    [Fact]
    public void Pr_fechado_ou_ainda_nao_consultado_nao_afirma_nada_sobre_o_actions()
    {
        var fechado = PullRequestsViewModel.SituacaoDasVerificacoes(
            new PullRequest { Estado = "mesclado" }, null, Array.Empty<Verificacao>(), true, Agora);
        Assert.Equal("", fechado.Texto);

        var semConsulta = PullRequestsViewModel.SituacaoDasVerificacoes(
            new PullRequest { Atualizado = Agora }, null, Array.Empty<Verificacao>(), consultado: false, Agora);
        Assert.Equal("Consultando as verificações…", semConsulta.Texto);
    }

    [Fact]
    public void Verificacao_traz_tempos_e_a_execucao_do_actions()
    {
        var v = GitHubService.LerVerificacoes("""
            {"check_runs":[{"name":"build","status":"completed","conclusion":"success",
              "started_at":"2026-10-07T14:00:00Z","completed_at":"2026-10-07T14:01:29Z",
              "details_url":"https://github.com/bm/fin/actions/runs/123456/job/789"}]}
            """).Single();

        Assert.Equal(123456, v.RunId);
        Assert.Equal("passou · 1m 29s", new VerificacaoViewModel { Verificacao = v }.Texto);

        Assert.Equal(0, GitHubService.RunDoLink("https://outro-servico.example/checks/9"));
        Assert.Equal("na fila", new VerificacaoViewModel { Verificacao = V("x", "rodando") }.Texto);
    }

    [Fact]
    public void Passo_em_andamento_do_job_aparece_na_linha()
    {
        var job = new CiJob
        {
            Nome = "build",
            Etapas =
            {
                new CiEtapa { Numero = 1, Nome = "Checkout", Situacao = "sucesso" },
                new CiEtapa { Numero = 2, Nome = "Testes", Situacao = "rodando" },
                new CiEtapa { Numero = 3, Nome = "Publicar", Situacao = "nenhum" },
            },
        };

        Assert.Equal("passo 2 de 3: Testes", VerificacaoViewModel.PassoDe(job));
        Assert.Equal("", VerificacaoViewModel.PassoDe(new CiJob { Nome = "sem passos" }));

        // o passo só aparece enquanto a verificação roda
        var linha = new VerificacaoViewModel { Verificacao = V("build", "rodando"), Passo = "passo 2 de 3: Testes" };
        Assert.True(linha.TemPasso);
        linha.Verificacao = V("build", "sucesso");
        Assert.False(linha.TemPasso);
    }

    [Fact]
    public void Verificacoes_atualizam_no_lugar_e_explicam_o_instavel()
    {
        var vm = Vm();
        vm.Aplicar(new[] { new PullRequest { Numero = 7, Origem = "imp/boleto", Destino = "develop", Atualizado = DateTime.UtcNow } });
        Assert.Equal("Consultando as verificações…", vm.CiTexto);

        vm.AplicarVerificacoes(new[] { V("build", "rodando") });
        var linha = vm.Verificacoes.Single();
        vm.Detalhe = new PullRequest { Numero = 7, Mesclagem = "unstable" };
        Assert.Equal("Dá para mesclar, mas as verificações ainda estão rodando", vm.MesclagemTexto);
        Assert.StartsWith("Actions rodando: 0 de 1", vm.CiTexto);

        // o ciclo seguinte traz o resultado: a mesma linha, com o conteúdo novo
        vm.AplicarVerificacoes(new[] { V("build", "sucesso") });
        Assert.Same(linha, vm.Verificacoes.Single());
        Assert.Equal("A verificação passou.", vm.CiTexto);

        // trocar de PR não deixa a situação do anterior
        vm.Aplicar(new[] { new PullRequest { Numero = 8, Origem = "feat/x", Destino = "develop", Atualizado = DateTime.UtcNow } });
        Assert.Equal("Consultando as verificações…", vm.CiTexto);
    }

    // ------------------------------------------------- situação local da branch

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task Commit(string dir, string nome, string msg)
    {
        File.WriteAllText(Path.Combine(dir, nome), msg + "\n");
        await Git(dir, "add", nome);
        await Git(dir, "commit", "-qm", msg);
    }

    [Fact]
    public async Task Situacao_local_distingue_branch_enviada_da_que_so_tem_vinculo()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-pr-" + Path.GetRandomFileName());
        var remoto = Path.Combine(raiz, "remoto.git");
        var meu = Path.Combine(raiz, "meu");
        Directory.CreateDirectory(raiz);
        try
        {
            await Git(raiz, "init", "-q", "--bare", "-b", "main", remoto);
            await Git(raiz, "clone", "-q", remoto, meu);
            await Git(meu, "config", "user.email", "t@t");
            await Git(meu, "config", "user.name", "Teste");
            await Git(meu, "checkout", "-qb", "main");
            await Commit(meu, "a.txt", "inicial");
            await Git(meu, "push", "-q", "-u", "origin", "main");
            await Git(meu, "checkout", "-qb", "develop");
            await Git(meu, "push", "-q", "-u", "origin", "develop");

            // criada de origin/develop: tem vínculo, mas não existe no remoto com esse nome
            await Git(meu, "checkout", "-q", "-b", "imp/boleto", "--track", "origin/develop");
            await Commit(meu, "b.txt", "Boleto online");

            var antes = await GitService.SituacaoParaPrAsync(meu, "imp/boleto");
            Assert.False(antes.Enviada);
            Assert.Equal("Boleto online", antes.Assunto);
            Assert.Equal(new[] { "develop", "main" }, antes.Destinos);

            await Git(meu, "push", "-q", "origin", "imp/boleto");
            await Commit(meu, "c.txt", "Ajuste");

            var depois = await GitService.SituacaoParaPrAsync(meu, "imp/boleto");
            Assert.True(depois.Enviada);
            Assert.Equal(1, depois.NaoEnviados);
            Assert.Equal("Ajuste", depois.Assunto);
            Assert.Contains("imp/boleto", depois.Destinos);
        }
        finally
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(raiz, true);
            }
            catch (Exception) { /* pasta temporária */ }
        }
    }
}
