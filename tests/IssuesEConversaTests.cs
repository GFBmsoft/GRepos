using System;
using System.IO;
using System.Linq;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Conversa, commits e arquivos de um pull request, e a lista de issues com etiquetas:
/// a leitura das respostas do GitHub e os filtros da tela.
/// </summary>
public class IssuesEConversaTests
{
    // -------------------------------------------------------- conversa do PR

    [Fact]
    public void Revisoes_dizem_o_que_o_revisor_fez_e_a_vazia_fica_de_fora()
    {
        var revisoes = GitHubService.LerRevisoes("""
            [
              {"user":{"login":"ana"},"state":"APPROVED","body":"","submitted_at":"2026-10-06T18:00:00Z"},
              {"user":{"login":"beto"},"state":"CHANGES_REQUESTED","body":"Falta o teste.","submitted_at":"2026-10-06T17:00:00Z"},
              {"user":{"login":"beto"},"state":"COMMENTED","body":"","submitted_at":"2026-10-06T16:00:00Z"},
              {"user":{"login":"beto"},"state":"COMMENTED","body":"Ficou bom.","submitted_at":"2026-10-06T19:00:00Z"},
              {"user":{"login":"ana"},"state":"PENDING","body":"rascunho"}
            ]
            """);

        Assert.Equal(new[] { "aprovou", "pediu mudanças", "revisou" }, revisoes.Select(r => r.Tipo));

        // aprovar sem escrever nada é comum: a fala aparece, com o texto de "sem texto"
        var aprovou = new ComentarioViewModel { Comentario = revisoes[0] };
        Assert.Equal("*Sem texto.*", aprovou.Corpo);
        Assert.Equal("Green", aprovou.TipoCor);
        Assert.Equal("Red", new ComentarioViewModel { Comentario = revisoes[1] }.TipoCor);
    }

    [Fact]
    public void Comentario_no_codigo_traz_o_arquivo_e_a_linha()
    {
        var c = GitHubService.LerComentariosDeCodigo("""
            [{"user":{"login":"ana"},"body":"Aqui estoura com nulo.","path":"Units/Boleto.pas","line":null,"original_line":42,"created_at":"2026-10-06T18:00:00Z"}]
            """).Single();

        Assert.Equal("comentou no código", c.Tipo);
        Assert.Equal("Units/Boleto.pas:42", c.Onde);
    }

    [Fact]
    public void Comentarios_tolera_usuario_apagado_e_resposta_de_erro()
    {
        var c = GitHubService.LerComentarios("""[{"user":null,"body":"oi","created_at":"2026-10-06T18:00:00Z"}]""").Single();
        Assert.Equal("", c.Autor);
        Assert.Equal("(sem autor)", new ComentarioViewModel { Comentario = c }.Autor);

        Assert.Empty(GitHubService.LerComentarios("""{"message":"Not Found"}"""));
    }

    [Fact]
    public void Commits_do_pr_usam_a_primeira_linha_e_o_login_quando_ha()
    {
        var commits = GitHubService.LerCommitsDoPr("""
            [
              {"sha":"302217b67e22f77e","author":{"login":"WiliampBMsoft"},
               "commit":{"author":{"name":"Wiliam","date":"2026-10-06T17:27:41Z"},"message":"TempoReal: WebSocket\n\nPOST /tempo-real/ticket"}},
              {"sha":"abc","author":null,
               "commit":{"author":{"name":"Fulano de Tal","date":"2026-10-06T18:00:00Z"},"message":"ajuste"}}
            ]
            """);

        Assert.Equal("TempoReal: WebSocket", commits[0].Assunto);
        Assert.Equal("WiliampBMsoft", commits[0].Autor);
        Assert.Equal("Fulano de Tal", commits[1].Autor); // conta desconhecida: vale o nome do commit
        Assert.Equal("302217b", new CommitDoPrViewModel { Commit = commits[0] }.Short);
    }

    [Fact]
    public void Arquivo_do_pr_vira_diff_que_o_painel_le()
    {
        var arquivos = GitHubService.LerArquivosDoPr("""
            [
              {"filename":"APIInterna.dpr","status":"modified","additions":2,"deletions":1,
               "patch":"@@ -1,3 +1,4 @@\n begin\n-  Antigo;\n+  Novo;\n+  Outro;\n end."},
              {"filename":"img/logo.png","status":"added","additions":0,"deletions":0}
            ]
            """);

        Assert.Equal(new[] { "M", "A" }, arquivos.Select(a => a.Status));

        var diff = DiffParser.Parse(GitHubService.DiffDoArquivo(arquivos[0]));
        Assert.Single(diff.Hunks);
        Assert.Contains(diff.Hunks[0].Lines, l => l.Text.Contains("Novo;"));

        // binário: a API não manda o trecho
        Assert.Equal("", GitHubService.DiffDoArquivo(arquivos[1]));
    }

    [Fact]
    public void Abas_do_pr_mostram_a_contagem_e_o_diff_do_arquivo_escolhido()
    {
        var vm = new PullRequestsViewModel(new ContextoPr("", "imp/x", "", "Financeiro"));
        vm.Aplicar(new[] { new PullRequest { Numero = 56, Origem = "imp/x", Destino = "develop" } });
        vm.Detalhe = new PullRequest { Numero = 56, Commits = 7, Arquivos = 20, Comentarios = 6, Mesclagem = "clean" };

        Assert.Equal("Conversa (6)", vm.AbaConversaRotulo);
        Assert.Equal("Commits (7)", vm.AbaCommitsRotulo);
        Assert.Equal("Arquivos (20)", vm.AbaArquivosRotulo);

        vm.AplicarArquivos(new[]
        {
            new ArquivoDoPr { Caminho = "a.pas", Status = "M", Adicionadas = 1, Patch = "@@ -1 +1,2 @@\n um\n+dois" },
            new ArquivoDoPr { Caminho = "logo.png", Status = "A" },
        });

        Assert.Equal("a.pas", vm.ArquivoSelecionado!.Path);
        Assert.False(vm.DiffDoArquivo.IsEmpty);

        vm.ArquivoSelecionado = vm.Arquivos[1];
        Assert.True(vm.DiffDoArquivo.IsEmpty);
        Assert.Contains("binário", vm.DiffDoArquivo.EmptyMessage);

        // trocar de PR leva a conversa, os commits e os arquivos do anterior
        vm.AplicarConversa(new[] { new Comentario { Autor = "ana", Corpo = "ok" } });
        vm.Aplicar(new[] { new PullRequest { Numero = 57, Origem = "feat/y", Destino = "develop" } });
        Assert.Empty(vm.Conversa);
        Assert.Empty(vm.Arquivos);
        Assert.Null(vm.ArquivoSelecionado);
    }

    [Fact]
    public void Conversa_atualiza_no_lugar()
    {
        var vm = new PullRequestsViewModel(new ContextoPr("", "imp/x", "", "Financeiro"));
        var quando = new DateTime(2026, 10, 6, 18, 0, 0, DateTimeKind.Utc);

        vm.AplicarConversa(new[] { new Comentario { Autor = "ana", Corpo = "primeira versão", Quando = quando } });
        var linha = vm.Conversa.Single();

        // o comentário foi editado e chegou outro
        vm.AplicarConversa(new[]
        {
            new Comentario { Autor = "ana", Corpo = "editado", Quando = quando },
            new Comentario { Autor = "beto", Corpo = "novo", Quando = quando.AddMinutes(5) },
        });

        Assert.Same(linha, vm.Conversa[0]);
        Assert.Equal("editado", linha.Corpo);
        Assert.Equal(2, vm.Conversa.Count);
    }

    // --------------------------------------------------------------- issues

    private const string IssuesJson = """
        [
          {"number":48,"title":"[Segurança] Rotas de leitura sem checagem","state":"open","comments":0,
           "user":{"login":"GFBmsoft"},"body":"Detalhe.","html_url":"https://github.com/bm/api/issues/48",
           "labels":[{"name":"bug","color":"d73a4a"},{"name":"enhancement","color":"a2eeef"}],
           "assignees":[{"login":"WiliampBMsoft"}],"updated_at":"2026-10-06T18:00:00Z"},
          {"number":53,"title":"Transferencias: envio longo","state":"open","comments":2,
           "user":{"login":"WiliampBMsoft"},"body":null,"labels":[],"assignees":[],
           "pull_request":{"url":"https://api.github.com/repos/bm/api/pulls/53"}},
          {"number":47,"title":"SMTP sem validação de certificado","state":"open","comments":3,
           "user":{"login":"GFBmsoft"},"body":"","labels":[{"name":"bug","color":"d73a4a"}],"assignees":[]}
        ]
        """;

    [Fact]
    public void Issues_trazem_etiquetas_com_cor_e_deixam_os_pull_requests_de_fora()
    {
        var issues = GitHubService.LerIssues(IssuesJson);

        Assert.Equal(new[] { 48, 47 }, issues.Select(i => i.Numero)); // o #53 é PR
        Assert.Equal(new[] { new Etiqueta("bug", "#d73a4a"), new Etiqueta("enhancement", "#a2eeef") }, issues[0].Etiquetas);
        Assert.Equal(new[] { "WiliampBMsoft" }, issues[0].Responsaveis);
        Assert.True(issues[0].Aberta);

        Assert.Empty(GitHubService.LerIssues("""{"message":"Not Found"}"""));
    }

    private static IssuesViewModel Vm()
    {
        var vm = new IssuesViewModel("", "", "bm-api");
        vm.Aplicar(GitHubService.LerIssues(IssuesJson), new[]
        {
            new Issue { Numero = 10, Titulo = "Antiga", Aberta = false, Etiquetas = { new Etiqueta("documentation", "#0075ca") } },
        });
        return vm;
    }

    [Fact]
    public void Etiquetas_em_uso_viram_filtro_da_mais_comum_para_a_menos()
    {
        var vm = Vm();

        Assert.Equal(new[] { "bug 2", "enhancement 1" }, vm.Etiquetas.Select(e => e.Texto));
        Assert.Equal("Abertas 2", vm.AbertasRotulo);
        Assert.Equal("Fechadas 1", vm.FechadasRotulo);
        Assert.Equal(48, vm.Selecionada!.Issue.Numero);

        vm.AlternarEtiquetaCommand.Execute(vm.Etiquetas.Single(e => e.Nome == "enhancement"));
        Assert.Equal(new[] { 48 }, vm.Lista.Select(i => i.Issue.Numero));
        Assert.True(vm.Etiquetas.Single(e => e.Nome == "enhancement").Ativa);
        Assert.Equal(2, vm.Etiquetas.Count); // as outras continuam ali, para combinar ou trocar

        // clicar de novo tira o filtro
        vm.AlternarEtiquetaCommand.Execute(vm.Etiquetas.Single(e => e.Nome == "enhancement"));
        Assert.Equal(2, vm.Lista.Count);
    }

    [Fact]
    public void Busca_olha_titulo_autor_e_numero_e_o_vazio_explica()
    {
        var vm = Vm();

        vm.Busca = "smtp";
        Assert.Equal(new[] { 47 }, vm.Lista.Select(i => i.Issue.Numero));

        vm.Busca = "#48";
        Assert.Equal(new[] { 48 }, vm.Lista.Select(i => i.Issue.Numero));

        vm.Busca = "nada disso";
        Assert.True(vm.Vazio);
        Assert.Equal("Nenhuma issue com esse filtro.", vm.TextoVazio);
        Assert.Null(vm.Selecionada);
    }

    [Fact]
    public void Fechadas_tem_as_proprias_etiquetas_e_o_filtro_nao_vaza_de_uma_aba_para_a_outra()
    {
        var vm = Vm();
        vm.AlternarEtiquetaCommand.Execute(vm.Etiquetas.Single(e => e.Nome == "bug"));

        vm.VerFechadasCommand.Execute(null);

        Assert.True(vm.MostrarFechadas);
        Assert.Equal(new[] { 10 }, vm.Lista.Select(i => i.Issue.Numero));
        Assert.Equal(new[] { "documentation 1" }, vm.Etiquetas.Select(e => e.Texto));
        Assert.Equal("Purple", vm.Lista[0].EstadoCor);
    }

    [Fact]
    public void Linha_da_issue_resume_autor_e_comentarios()
    {
        var item = Vm().Lista.Single(i => i.Issue.Numero == 47);

        Assert.StartsWith("GFBmsoft", item.Rodape);
        Assert.EndsWith("3 comentários", item.Rodape);
        Assert.Equal("sem responsável", item.Responsaveis);
        Assert.Equal("*Sem descrição.*", item.Corpo);
    }

    // ------------------------------------------------------------ escrever
    // A chamada em si depende de rede e de token com permissão; o que dá para garantir
    // aqui é quando cada ação aparece e o que ela manda.

    private static Comentario Fala(long id, string autor, string origem = "conversa", string tipo = "comentou") =>
        new() { Id = id, Autor = autor, Origem = origem, Tipo = tipo, Corpo = "texto", Quando = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void So_o_proprio_comentario_pode_ser_editado_e_revisao_nao()
    {
        var conversa = new ConversaViewModel("", "GFBmsoft") { Numero = 48 };
        conversa.Aplicar(new[]
        {
            Fala(1, "gfbmsoft"),                               // o login não diferencia maiúsculas
            Fala(2, "colega"),
            Fala(3, "GFBmsoft", "codigo", "comentou no código"),
            Fala(4, "GFBmsoft", "revisao", "aprovou"),
        });

        Assert.Equal(new[] { true, false, true, false }, conversa.Itens.Select(i => i.PodeEditar));
    }

    [Fact]
    public void Editar_abre_a_caixa_com_o_texto_e_cancelar_nao_muda_nada()
    {
        var conversa = new ConversaViewModel("", "GFBmsoft") { Numero = 48 };
        conversa.Aplicar(new[] { Fala(1, "GFBmsoft"), Fala(2, "colega") });
        var meu = conversa.Itens[0];

        conversa.EditarCommand.Execute(meu);
        Assert.True(meu.Editando);
        Assert.Equal("texto", meu.Rascunho);
        Assert.False(meu.PodeEditar); // já está editando: os botões somem

        meu.Rascunho = "mexido";
        conversa.CancelarEdicaoCommand.Execute(meu);
        Assert.False(meu.Editando);
        Assert.Equal("texto", meu.Corpo);

        // o dos outros não abre
        conversa.EditarCommand.Execute(conversa.Itens[1]);
        Assert.False(conversa.Itens[1].Editando);
    }

    [Fact]
    public void Recarga_da_conversa_nao_derruba_a_edicao_em_andamento()
    {
        var conversa = new ConversaViewModel("", "GFBmsoft") { Numero = 48 };
        conversa.Aplicar(new[] { Fala(1, "GFBmsoft") });
        var meu = conversa.Itens[0];
        conversa.EditarCommand.Execute(meu);
        meu.Rascunho = "ainda digitando";

        // o ciclo automático traz a mesma conversa com mais uma fala
        conversa.Aplicar(new[] { Fala(1, "GFBmsoft"), Fala(2, "colega") });

        Assert.Same(meu, conversa.Itens[0]);
        Assert.True(meu.Editando);
        Assert.Equal("ainda digitando", meu.Rascunho);
    }

    [Fact]
    public void Comentar_precisa_de_texto_e_de_algo_escolhido()
    {
        var conversa = new ConversaViewModel("", "GFBmsoft");
        conversa.Novo = "olá";
        Assert.False(conversa.PodeComentar); // sem issue nem PR escolhido

        conversa.Numero = 48;
        Assert.True(conversa.PodeComentar);

        conversa.Novo = "   ";
        Assert.False(conversa.PodeComentar);
    }

    [Fact]
    public void Cada_tipo_de_comentario_e_editado_no_endereco_certo()
    {
        Assert.EndsWith("/repos/bm/api/issues/comments/7", GitHubService.EnderecoDoComentario("bm/api", Fala(7, "x")));
        Assert.EndsWith("/repos/bm/api/pulls/comments/7", GitHubService.EnderecoDoComentario("bm/api", Fala(7, "x", "codigo")));

        // o id vem da resposta: sem ele não há o que editar
        var lido = GitHubService.LerComentarios("""[{"id":991,"user":{"login":"ana"},"body":"oi"}]""").Single();
        Assert.Equal(991, lido.Id);
        Assert.Equal("conversa", lido.Origem);
    }

    [Fact]
    public void Fechar_com_texto_na_caixa_vira_comentar_e_fechar()
    {
        var vm = Vm();
        Assert.Equal("Fechar issue", vm.EstadoRotulo);

        vm.Editor.Novo = "Resolvido na 1.0.0.35.";
        Assert.Equal("Comentar e fechar", vm.EstadoRotulo);

        vm.VerFechadasCommand.Execute(null);
        Assert.Equal("Reabrir issue", vm.EstadoRotulo);
    }

    [Fact]
    public void Filtrar_a_lista_nao_apaga_o_comentario_que_estava_sendo_digitado()
    {
        var vm = Vm();
        Assert.Equal(48, vm.Selecionada!.Issue.Numero);
        vm.Editor.Novo = "meio comentário";

        vm.Busca = "rotas"; // a lista é refeita, mas a issue escolhida é a mesma
        Assert.Equal(48, vm.Selecionada!.Issue.Numero);
        Assert.Equal("meio comentário", vm.Editor.Novo);

        vm.Busca = "smtp"; // outra issue: aí sim a caixa esvazia
        Assert.Equal(47, vm.Selecionada!.Issue.Numero);
        Assert.Equal("", vm.Editor.Novo);
        Assert.Equal(47, vm.Editor.Numero);
    }

    [Fact]
    public async System.Threading.Tasks.Task Formulario_de_issue_nova_e_de_edicao()
    {
        var vm = Vm();

        await vm.NovaCommand.ExecuteAsync(null);
        Assert.True(vm.Editando);
        Assert.False(vm.MostraDetalhe);
        Assert.Equal("NOVA ISSUE", vm.FormCabecalho);
        Assert.Equal("Criar issue", vm.SalvarRotulo);
        Assert.False(vm.PodeSalvar); // sem título

        // sem a lista do repositório, valem as etiquetas que as issues já usam
        Assert.Equal(new[] { "bug", "documentation", "enhancement" }, vm.FormEtiquetas.Select(e => e.Nome));
        Assert.All(vm.FormEtiquetas, e => Assert.False(e.Ativa));

        vm.FormTitulo = "Nova rota";
        Assert.True(vm.PodeSalvar);
        vm.CancelarFormCommand.Execute(null);
        Assert.True(vm.MostraDetalhe);

        // editar traz o que a issue já tem, com as etiquetas dela marcadas
        await vm.EditarIssueCommand.ExecuteAsync(null);
        Assert.Equal("EDITAR ISSUE #48", vm.FormCabecalho);
        Assert.Equal("[Segurança] Rotas de leitura sem checagem", vm.FormTitulo);
        Assert.Equal("Detalhe.", vm.FormCorpo);
        Assert.Equal(new[] { "bug", "enhancement" }, vm.FormEtiquetas.Where(e => e.Ativa).Select(e => e.Nome));

        vm.AlternarEtiquetaDoFormCommand.Execute(vm.FormEtiquetas.Single(e => e.Nome == "bug"));
        Assert.Equal(new[] { "enhancement" }, vm.FormEtiquetas.Where(e => e.Ativa).Select(e => e.Nome));

        // com a lista do repositório, entram também as que nenhuma issue usa ainda
        vm.MontarFormEtiquetas(new[] { "priority" }, new[] { new Etiqueta("bug", "#d73a4a"), new Etiqueta("priority", "#b60205") });
        Assert.Equal(new[] { "priority" }, vm.FormEtiquetas.Where(e => e.Ativa).Select(e => e.Nome));
    }

    [Fact]
    public void Revisar_nao_vale_no_proprio_pr_e_pedir_mudancas_exige_texto()
    {
        var vm = new PullRequestsViewModel(new ContextoPr("", "imp/x", "GFBmsoft", "Financeiro"));
        vm.Aplicar(new[]
        {
            new PullRequest { Numero = 5, Autor = "colega", Origem = "feat/y", Destino = "develop" },
            new PullRequest { Numero = 6, Autor = "GFBmsoft", Origem = "imp/x", Destino = "develop" },
        });

        vm.Selecionada = vm.Lista.Single(p => p.Pr.Numero == 5);
        Assert.True(vm.PodeRevisar);
        Assert.False(vm.PodePedirMudancas);
        vm.Editor.Novo = "Falta o teste.";
        Assert.True(vm.PodePedirMudancas);

        vm.Selecionada = vm.Lista.Single(p => p.Pr.Numero == 6);
        Assert.False(vm.PodeRevisar);
        Assert.Contains("próprio pull request", vm.RevisarDica);
        Assert.Equal("", vm.Editor.Novo); // a caixa é do PR anterior
        Assert.Equal(6, vm.Editor.Numero);
    }

    [Fact]
    public void Editar_pr_traz_titulo_e_descricao_e_troca_o_painel()
    {
        var vm = new PullRequestsViewModel(new ContextoPr("", "imp/x", "GFBmsoft", "Financeiro"));
        vm.Aplicar(new[] { new PullRequest { Numero = 6, Titulo = "Boleto", Corpo = "da lista", Origem = "imp/x", Destino = "develop" } });
        vm.Detalhe = new PullRequest { Numero = 6, Corpo = "Descrição completa.", Mesclagem = "clean" };

        vm.EditarPrCommand.Execute(null);
        Assert.True(vm.EditandoPr);
        Assert.False(vm.MostraDetalhe);
        Assert.Equal("Boleto", vm.EdicaoTitulo);
        Assert.Equal("Descrição completa.", vm.EdicaoCorpo);

        vm.EdicaoTitulo = "  ";
        Assert.False(vm.PodeSalvarPr);

        vm.CancelarEdicaoPrCommand.Execute(null);
        Assert.True(vm.MostraDetalhe);
    }

    [Fact]
    public void Recusa_de_revisar_o_proprio_pr_sai_em_portugues()
    {
        var motivo = GitHubService.MotivoDaRecusa(422,
            """{"message":"Unprocessable Entity","errors":["Can not approve your own pull request"]}""");
        Assert.Contains("próprio pull request", motivo);
    }

    /// <summary>
    /// Conferência contra respostas de verdade, quando GREPOS_AMOSTRAS aponta uma pasta
    /// com elas (issues.json, c.json, r.json, cm.json, f.json). Sem a pasta, não faz nada:
    /// as amostras são de repositório privado e não entram no git.
    /// </summary>
    [Fact]
    public void Respostas_reais_do_github_sao_lidas_sem_erro()
    {
        var pasta = Environment.GetEnvironmentVariable("GREPOS_AMOSTRAS");
        if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) return;

        string Ler(string nome) => File.ReadAllText(Path.Combine(pasta, nome));

        var issues = GitHubService.LerIssues(Ler("issues.json"));
        Assert.NotEmpty(issues);
        Assert.All(issues, i => Assert.True(i.Numero > 0 && i.Titulo.Length > 0));
        Assert.Contains(issues, i => i.Etiquetas.Count > 0);

        Assert.NotEmpty(GitHubService.LerComentarios(Ler("c.json")));
        GitHubService.LerRevisoes(Ler("r.json"));

        var commits = GitHubService.LerCommitsDoPr(Ler("cm.json"));
        Assert.All(commits, c => Assert.True(c.Sha.Length > 0 && c.Assunto.Length > 0 && c.Autor.Length > 0));

        var arquivos = GitHubService.LerArquivosDoPr(Ler("f.json"));
        Assert.NotEmpty(arquivos);
        foreach (var a in arquivos.Where(a => a.Patch.Length > 0))
            Assert.NotEmpty(DiffParser.Parse(GitHubService.DiffDoArquivo(a)).Hunks);
    }
}
