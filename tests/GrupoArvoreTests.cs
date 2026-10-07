using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Controls;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Grupos dentro de grupos (pasta, subpasta, repositórios): a leitura da lista plana
/// como árvore, e a árvore da sidebar montada a partir dela.
/// </summary>
public class GrupoArvoreTests
{
    private static Group G(string id, string? pai = null, bool recolhido = false) =>
        new() { Id = id, Name = id.ToUpperInvariant(), ParentId = pai, Collapsed = recolhido };

    // dbisam > (fiscal > (nfe), financeiro) ; mysql
    private static readonly Group[] Grupos =
    {
        G("dbisam"), G("mysql"), G("fiscal", "dbisam"), G("financeiro", "dbisam"), G("nfe", "fiscal"),
    };

    [Fact]
    public void Ordem_de_arvore_poe_cada_pai_antes_dos_filhos_com_nivel_e_caminho()
    {
        var ordem = GrupoArvore.EmOrdem(Grupos);

        Assert.Equal(new[] { "dbisam", "fiscal", "nfe", "financeiro", "mysql" }, ordem.Select(o => o.Grupo.Id));
        Assert.Equal(new[] { 0, 1, 2, 1, 0 }, ordem.Select(o => o.Nivel));
        Assert.Equal("DBISAM / FISCAL / NFE", ordem[2].Caminho);
        Assert.Equal("      NFE", ordem[2].Recuado);
    }

    [Fact]
    public void Descendentes_e_ancestrais()
    {
        Assert.Equal(new[] { "dbisam", "financeiro", "fiscal", "nfe" },
            GrupoArvore.ComDescendentes(Grupos, "dbisam").OrderBy(x => x));
        Assert.Equal(new[] { "nfe", "fiscal", "dbisam" }, GrupoArvore.Ancestrais(Grupos, "nfe").Select(g => g.Id));
        Assert.Equal("Sem grupo", GrupoArvore.Caminho(Grupos, null));
    }

    [Fact]
    public void Grupo_nao_pode_ir_para_dentro_de_si_nem_de_um_descendente()
    {
        Assert.False(GrupoArvore.PodeFicarDentro(Grupos, "dbisam", "dbisam"));
        Assert.False(GrupoArvore.PodeFicarDentro(Grupos, "dbisam", "nfe"));
        Assert.True(GrupoArvore.PodeFicarDentro(Grupos, "nfe", "mysql"));
        Assert.True(GrupoArvore.PodeFicarDentro(Grupos, "fiscal", null));
    }

    [Fact]
    public void Pai_que_nao_existe_e_ciclo_nao_somem_com_o_grupo()
    {
        // workspace editado à mão: o pai foi apagado, ou A está em B que está em A
        var orfao = new[] { G("a", "sumiu") };
        Assert.Equal(0, GrupoArvore.EmOrdem(orfao).Single().Nivel);

        var ciclo = new[] { G("a", "b"), G("b", "a"), G("c") };
        Assert.Equal(new[] { "a", "b", "c" }, GrupoArvore.EmOrdem(ciclo).Select(o => o.Grupo.Id).OrderBy(x => x));
    }

    [Fact]
    public void Paleta_do_seletor_nao_repete_cor_e_comeca_pelas_dos_grupos()
    {
        Assert.Equal(SeletorDeCor.Paleta.Count, SeletorDeCor.Paleta.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(GroupPalette.Cores.Select(c => c.ToUpperInvariant()), SeletorDeCor.Paleta.Take(GroupPalette.Cores.Length));
        Assert.All(SeletorDeCor.Paleta, c => Assert.NotNull(GroupPalette.Normalizar(c)));
    }
}

[Collection(WorkspaceGlobal.Nome)]
public class ArvoreComSubgruposTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public (string Nome, string Cor, string? PaiId)? Resposta;
        public string? PaiOferecido;
        public string[] PaisPossiveis = Array.Empty<string>();

        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(true);
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

        public Task<(string Nome, string Cor, string? PaiId)?> ShowGrupoAsync(
            string titulo, string nome, string cor, string? paiId, System.Collections.Generic.IReadOnlyList<GrupoNaArvore> pais)
        {
            PaiOferecido = paiId;
            PaisPossiveis = pais.Select(p => p.Grupo.Name).ToArray();
            return Task.FromResult(Resposta);
        }
    }

    /// <summary>Workspace numa pasta temporária, restaurando o GREPOS_HOME que havia antes.</summary>
    private static async Task Com(Func<MainViewModel, FakeDialogs, Task> teste)
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-arvore-" + Path.GetRandomFileName());
        Directory.CreateDirectory(home);
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            var dialogos = new FakeDialogs();
            await teste(new MainViewModel(dialogos), dialogos);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }

    /// <summary>DBISAM > Fiscal > NFe, cada nível com um repositório; e um solto.</summary>
    private static (string Dbisam, string Fiscal, string Nfe) Montar(MainViewModel main)
    {
        var dbisam = main.CreateGroup("DBISAM", "#E07B00");
        var fiscal = main.CreateGroup("Fiscal", "#1F9D55", dbisam);
        var nfe = main.CreateGroup("NFe", "#2F7BE8", fiscal);

        main.AddRepository(@"C:\repos\Financeiro", "Financeiro", dbisam);
        main.AddRepository(@"C:\repos\SPED", "SPED", fiscal);
        main.AddRepository(@"C:\repos\NFCe", "NFCe", nfe);
        main.AddRepository(@"C:\repos\Solto", "Solto", null);
        main.RebuildTree();
        return (dbisam, fiscal, nfe);
    }

    private static string[] Linhas(MainViewModel main) => main.Tree
        .Where(n => n is GroupNode or RepoNode)
        .Select(n => new string(' ', n.Nivel * 2) + (n is GroupNode g ? $"[{g.Name} {g.Count}]" : ((RepoNode)n).Name))
        .ToArray();

    [Fact]
    public Task Arvore_desenha_subgrupo_dentro_do_grupo_com_recuo_e_contagem_do_ramo() => Com((main, _) =>
    {
        Montar(main);

        Assert.Equal(new[]
        {
            "[DBISAM 3]",          // conta o que está em qualquer nível abaixo
            "  [Fiscal 2]",
            "    [NFe 1]",
            "    NFCe",            // o repositório fica no nível do grupo dele
            "  SPED",
            "Financeiro",
            "[Sem grupo 1]",
            "Solto",
        }, Linhas(main));

        Assert.Equal(new Avalonia.Thickness(28, 0, 0, 0), main.Tree.OfType<GroupNode>().Single(g => g.Name == "NFe").Recuo);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Recolher_o_pai_esconde_tudo_que_esta_abaixo_e_selecionar_reabre_o_caminho() => Com((main, _) =>
    {
        var (dbisam, fiscal, _) = Montar(main);

        main.ToggleGroupCommand.Execute(main.Tree.OfType<GroupNode>().Single(g => g.Id == fiscal));
        main.ToggleGroupCommand.Execute(main.Tree.OfType<GroupNode>().Single(g => g.Id == dbisam));
        Assert.Equal(new[] { "[DBISAM 3]", "[Sem grupo 1]", "Solto" }, Linhas(main));

        // ir a um repositório escondido dois níveis abaixo abre os dois grupos
        var nfce = main.Repos.Single(r => r.Name == "NFCe");
        main.SelecionarRepositorio(nfce.Id);

        Assert.Contains("    NFCe", Linhas(main));
        Assert.All(main.Groups, g => Assert.False(g.Collapsed));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Painel_e_lote_do_grupo_incluem_os_subgrupos() => Com((main, _) =>
    {
        var (dbisam, fiscal, _) = Montar(main);

        Assert.Equal(new[] { "Financeiro", "NFCe", "SPED" }, main.ReposDoGrupo(dbisam).Select(r => r.Name).OrderBy(n => n));
        Assert.Equal(new[] { "NFCe", "SPED" }, main.ReposDoGrupo(fiscal).Select(r => r.Name).OrderBy(n => n));
        Assert.Equal(new[] { "Solto" }, main.ReposDoGrupo("").Select(r => r.Name));

        main.MostrarPainel(dbisam);
        Assert.Equal(3, main.Painel!.Cartoes.Count);

        // uma seção por subgrupo, na ordem da árvore, com o caminho no título
        Assert.Equal(new[] { "DBISAM", "DBISAM / Fiscal", "DBISAM / Fiscal / NFe" }, main.Painel.Secoes.Select(s => s.Titulo));
        Assert.All(main.Painel.Secoes, s => Assert.True(s.MostraTitulo));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Remover_um_grupo_sobe_os_subgrupos_e_os_repositorios_um_nivel() => Com((main, _) =>
    {
        var (dbisam, fiscal, nfe) = Montar(main);

        main.RemoveGroup(fiscal);

        Assert.Equal(dbisam, main.Groups.Single(g => g.Id == nfe).ParentId);
        Assert.Equal(dbisam, main.Repos.Single(r => r.Name == "SPED").GroupId);
        Assert.Equal(new[] { "[DBISAM 3]", "  [NFe 1]", "  NFCe", "Financeiro", "SPED", "[Sem grupo 1]", "Solto" }, Linhas(main));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Novo_subgrupo_oferece_o_pai_e_herda_a_cor_dele() => Com(async (main, dialogos) =>
    {
        var (dbisam, fiscal, _) = Montar(main);
        main.ToggleGroupCommand.Execute(main.Tree.OfType<GroupNode>().Single(g => g.Id == fiscal));

        dialogos.Resposta = ("Notas", "#1F9D55", fiscal);
        await main.NovoGrupoAsync(fiscal);

        Assert.Equal(fiscal, dialogos.PaiOferecido);
        var novo = main.Groups.Single(g => g.Name == "Notas");
        Assert.Equal(fiscal, novo.ParentId);
        Assert.Contains("    [Notas 0]", Linhas(main)); // o pai recolhido foi aberto para ele aparecer
        _ = dbisam;
    });

    [Fact]
    public Task Editar_nao_oferece_o_proprio_grupo_nem_os_descendentes_como_pai() => Com(async (main, dialogos) =>
    {
        var (dbisam, fiscal, nfe) = Montar(main);
        var mysql = main.CreateGroup("MySQL", "#8B5CF6");

        dialogos.Resposta = ("Fiscal", "#1F9D55", mysql);
        await main.EditGroupAsync(fiscal);

        Assert.Equal(new[] { "DBISAM", "MySQL" }, dialogos.PaisPossiveis); // sem Fiscal e sem NFe
        Assert.Equal(mysql, main.Groups.Single(g => g.Id == fiscal).ParentId);
        Assert.Equal(fiscal, main.Groups.Single(g => g.Id == nfe).ParentId); // o que estava dentro vai junto

        // mesmo que alguém force, um ciclo não é gravado
        main.UpdateGroup(dbisam, "DBISAM", "#E07B00", dbisam, mudarPai: true);
        Assert.Null(main.Groups.Single(g => g.Id == dbisam).ParentId);
    });

    [Fact]
    public Task Subgrupos_sobrevivem_a_gravar_e_reabrir_o_workspace() => Com(async (main, _) =>
    {
        var (dbisam, fiscal, _) = Montar(main);

        var outro = new MainViewModel(new FakeDialogs());
        await outro.InitAsync();

        Assert.Equal(dbisam, outro.Groups.Single(g => g.Id == fiscal).ParentId);
    });
}
