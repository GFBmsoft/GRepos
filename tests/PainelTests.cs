using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>Painel de repositórios: seleção a partir do cartão e o texto de cada situação.</summary>
[Collection(WorkspaceGlobal.Nome)]
public class PainelTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(false);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string t, string l, string i = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel m, Repo r) => Task.CompletedTask;
    }

    /// <summary>
    /// Clicar num grupo abre o painel e **recolhe** o grupo, então os nós daquele grupo
    /// saem da árvore. O "Abrir" do cartão procurava um nó que não existia mais e não
    /// fazia nada — precisa reabrir o grupo antes de selecionar.
    /// </summary>
    [Fact]
    public async Task Abrir_funciona_com_o_grupo_recolhido()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-painel-" + Path.GetRandomFileName());
        var repo = Path.Combine(home, "repo");
        Directory.CreateDirectory(repo);

        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            await GitService.RunAsync(repo, new[] { "init", "-q", "-b", "main" });

            var main = new MainViewModel(new FakeDialogs());
            var grupoId = main.CreateGroup("Módulos");
            main.AddRepository(repo, "Financeiro", grupoId);
            main.RebuildTree();

            var no = main.Tree.OfType<RepoNode>().Single();
            main.SelecionarRepositorio(no.Id);
            Assert.Equal("Financeiro", main.SelectedNode?.Name);

            // recolhe o grupo, como o clique nele faz
            main.ToggleGroupCommand.Execute(main.Tree.OfType<GroupNode>().First(g => g.Id == grupoId));
            Assert.Empty(main.Tree.OfType<RepoNode>());

            main.SelectedNode = null;
            main.SelecionarRepositorio(no.Id);

            Assert.NotNull(main.SelectedNode);
            Assert.Equal("Financeiro", main.SelectedNode!.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(home, true); } catch (Exception) { /* temporária */ }
        }
    }

    [Fact]
    public void Cartao_sem_actions_nao_vira_erro_e_nao_oferece_esteira()
    {
        var cartao = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "Backup" }, new RepoStatus { Branch = "main" }, "#5D6675");

        // enquanto a API não respondeu, o cartão não pode afirmar que não tem esteira
        Assert.True(cartao.ConsultandoCi);
        Assert.False(cartao.SemCi);
        Assert.False(cartao.TemCi);

        cartao.CiSituacao = "nenhum";
        Assert.False(cartao.ConsultandoCi);
        Assert.True(cartao.SemCi);
        Assert.False(cartao.TemCi);
        Assert.Equal("sem esteira", cartao.CiTexto);
        Assert.Equal("", cartao.CiDetalheTexto);
    }

    [Fact]
    public void Cartao_com_esteira_mostra_o_que_rodou()
    {
        var cartao = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "GRepos", Path = @"D:\repo" },
            new RepoStatus { Branch = "main" }, "#4F8CFF")
        {
            CiWorkflow = "build",
            CiDetalhe = "Painel de repositórios",
            CiSituacao = "falha",
        };

        Assert.True(cartao.TemCi);
        Assert.Equal("esteira quebrou", cartao.CiTexto);
        Assert.Equal("build · Painel de repositórios", cartao.CiDetalheTexto);
        Assert.Contains("build · Painel de repositórios", cartao.Tooltip);
    }

    private const string PrsJson = """
    [
      { "number": 12, "title": "Painel de repositórios", "state": "open", "draft": false,
        "html_url": "https://github.com/x/y/pull/12", "updated_at": "2026-09-30T18:00:00Z",
        "merged_at": null, "user": { "login": "GFBmsoft" } },
      { "number": 11, "title": "Esteira detalhada", "state": "closed",
        "html_url": "https://github.com/x/y/pull/11", "updated_at": "2026-09-29T10:00:00Z",
        "merged_at": "2026-09-29T10:05:00Z", "user": { "login": "GFBmsoft" } },
      { "number": 10, "title": "Tentativa abandonada", "state": "closed",
        "html_url": "https://github.com/x/y/pull/10", "updated_at": "2026-09-28T10:00:00Z",
        "merged_at": null, "user": { "login": "outro" } }
    ]
    """;

    [Fact]
    public void Mesclado_e_fechado_sao_estados_diferentes()
    {
        var prs = GitHubService.LerPullRequests(PrsJson);

        Assert.Equal(3, prs.Count);
        Assert.Equal("aberto", prs[0].Estado);
        Assert.True(prs[0].Aberto);

        // merged_at preenchido é o que separa "mesclado" de simplesmente "fechado"
        Assert.Equal("mesclado", prs[1].Estado);
        Assert.Equal("fechado", prs[2].Estado);
        Assert.Single(prs, p => p.Aberto);
    }

    [Fact]
    public void Resposta_sem_prs_nao_quebra()
    {
        Assert.Empty(GitHubService.LerPullRequests("[]"));
        Assert.Empty(GitHubService.LerPullRequests("""{"message":"Not Found"}"""));
    }

    [Fact]
    public void Cartao_conta_so_os_prs_abertos()
    {
        var cartao = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "GRepos" }, new RepoStatus { Branch = "main" }, "#fff");

        Assert.False(cartao.TemPrs);
        Assert.Equal("", cartao.PrTexto);

        cartao.PrConsultado = true;
        cartao.PrsAbertos = 1;
        Assert.True(cartao.TemPrs);
        Assert.Equal("1 PR aberto", cartao.PrTexto);

        cartao.PrsAbertos = 3;
        Assert.Equal("3 PRs abertos", cartao.PrTexto);

        cartao.PrsAbertos = 0;
        Assert.False(cartao.TemPrs);
    }

    [Fact]
    public void Resumo_conta_o_que_precisa_de_atencao()
    {
        var cartoes = new[]
        {
            new CartaoRepoViewModel(new Repo { Id = "1" }, new RepoStatus { Branch = "main" }, "#fff"),
            new CartaoRepoViewModel(new Repo { Id = "2" },
                new RepoStatus { Branch = "main", Ahead = 1, Unstaged = 2, PendingFiles = 2 }, "#fff"),
            new CartaoRepoViewModel(new Repo { Id = "3" },
                new RepoStatus { Branch = "main", Behind = 3 }, "#fff") { CiSituacao = "falha" },
        };

        var painel = new PainelViewModel("Módulos", "", cartoes);

        Assert.Contains("3 repositório(s)", painel.Resumo);
        Assert.Contains("1 com alterações", painel.Resumo);
        Assert.Contains("1 a enviar", painel.Resumo);
        Assert.Contains("1 a receber", painel.Resumo);
        Assert.Contains("1 com esteira quebrada", painel.Resumo);
    }

    [Fact]
    public void Cartoes_dividem_a_largura_do_painel_por_igual()
    {
        // sem medida ainda, vale o mínimo em vez de zero
        Assert.True(GRepos.Controls.GradeCartoes.Dividir(double.PositiveInfinity, 250, 10).Largura > 0);

        // 3 colunas: a soma dos cartões mais os vãos fecha a largura, sem sobra à
        // direita — era o desalinho com o cartão de perfil, que ocupa a linha inteira
        var (tres, largura3) = GRepos.Controls.GradeCartoes.Dividir(980, 250, 10);
        Assert.Equal(3, tres);
        Assert.Equal(980, largura3 * 3 + 10 * 2, 3);

        // painel estreito não espreme: cai para menos colunas
        Assert.Equal(420, GRepos.Controls.GradeCartoes.Dividir(420, 250, 10).Largura, 3);

        var (duas, largura2) = GRepos.Controls.GradeCartoes.Dividir(700, 250, 10);
        Assert.Equal(2, duas);
        Assert.Equal(700, largura2 * 2 + 10, 3);
    }

    [Fact]
    public void Painel_sem_repositorios_diz_que_esta_vazio()
    {
        var painel = new PainelViewModel("Grupo novo", "", Array.Empty<CartaoRepoViewModel>());

        Assert.True(painel.Vazio);
        Assert.Equal("", painel.Resumo);
    }

    /// <summary>
    /// Seção do painel geral e grupo da árvore são o mesmo grupo: recolher de um lado
    /// recolhe do outro, e o estado fica gravado no workspace.
    /// </summary>
    [Fact]
    public async Task Secao_do_painel_recolhe_junto_com_a_arvore()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-secao-" + Path.GetRandomFileName());
        var repo = Path.Combine(home, "repo");
        Directory.CreateDirectory(repo);

        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            await GitService.RunAsync(repo, new[] { "init", "-q", "-b", "main" });

            var main = new MainViewModel(new FakeDialogs());
            var grupoId = main.CreateGroup("Módulos");
            main.AddRepository(repo, "Financeiro", grupoId);
            main.RebuildTree();

            main.MostrarPainel(null);
            var secao = main.Painel!.Secoes.Single(s => s.GrupoId == grupoId);
            Assert.False(secao.Recolhido);
            Assert.True(secao.MostraCartoes);

            // árvore → painel
            main.ToggleGroupCommand.Execute(main.Tree.OfType<GroupNode>().First(g => g.Id == grupoId));
            Assert.True(secao.Recolhido);
            Assert.False(secao.MostraCartoes);

            // painel → árvore
            secao.AlternarCommand.Execute(null);
            Assert.False(secao.Recolhido);
            Assert.False(main.Tree.OfType<GroupNode>().First(g => g.Id == grupoId).Collapsed);
            Assert.Single(main.Tree.OfType<RepoNode>());

            // recolhido no painel, o painel remontado nasce recolhido
            secao.AlternarCommand.Execute(null);
            Assert.Empty(main.Tree.OfType<RepoNode>());
            main.MostrarPainel(null);
            Assert.True(main.Painel!.Secoes.Single(s => s.GrupoId == grupoId).Recolhido);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(home, true); } catch (Exception) { /* temporária */ }
        }
    }

    [Fact]
    public void Secao_recolhida_avisa_o_que_tem_dentro_e_fecha_os_cartoes()
    {
        var sujo = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "Notas" },
            new RepoStatus { Branch = "main", Ahead = 1, PendingFiles = 2, Unstaged = 2 }, "#1F9D55")
        { Expandido = true };

        var secao = new SecaoPainelViewModel
        {
            MostraTitulo = true,
            Cartoes = new System.Collections.ObjectModel.ObservableCollection<CartaoRepoViewModel> { sujo },
        };

        Assert.False(secao.MostraPendencias); // aberta, os próprios cartões já dizem

        secao.AlternarCommand.Execute(null);

        Assert.True(secao.Recolhido);
        Assert.True(secao.MostraPendencias);
        Assert.Equal("1 com alterações · 1 a enviar", secao.Pendencias);
        Assert.False(sujo.Expandido);
    }

    [Fact]
    public void Secao_sem_titulo_nao_recolhe()
    {
        // painel de um grupo: a seção única não tem onde clicar e nunca some
        var secao = new SecaoPainelViewModel { MostraTitulo = false };
        secao.AlternarCommand.Execute(null);
        Assert.False(secao.Recolhido);
    }
}
