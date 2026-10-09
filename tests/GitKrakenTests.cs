using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Conflito (meu/deles), preparar por linha, desfazer, histórico do arquivo e blame,
/// busca, rebase interativo e emendar com a mensagem anterior — num repositório real.
/// </summary>
public class GitKrakenTests
{
    private sealed class FakeDialogs : IDialogService
    {
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
    }

    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (Exception) { /* pasta temporária */ }
    }

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task<string> NovoRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-gk-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        await Git(dir, "config", "core.autocrlf", "false");
        Escrever(dir, "a.txt", "um\ndois\ntres\n");
        await Git(dir, "add", ".");
        await Git(dir, "commit", "-qm", "inicial");
        return dir;
    }

    private static void Escrever(string dir, string nome, string texto) =>
        File.WriteAllText(Path.Combine(dir, nome), texto);

    private static string Ler(string dir, string nome) => File.ReadAllText(Path.Combine(dir, nome));

    private static async Task Commit(string dir, string nome, string texto, string msg)
    {
        Escrever(dir, nome, texto);
        await Git(dir, "add", nome);
        await Git(dir, "commit", "-qm", msg);
    }

    private static (ChangesViewModel Vm, MainViewModel Main) Changes(string dir)
    {
        var main = new MainViewModel(new FakeDialogs());
        return (new ChangesViewModel(new Repo { Id = "r1", Name = "Demo", Path = dir }, main, split: false), main);
    }

    /// <summary>main e "outra" mexem na mesma linha; o merge para em conflito.</summary>
    private static async Task<string> RepoEmConflito()
    {
        var dir = await NovoRepo();
        await Git(dir, "checkout", "-qb", "outra");
        await Commit(dir, "a.txt", "um\nDELES\ntres\n", "na outra");
        await Git(dir, "checkout", "-q", "main");
        await Commit(dir, "a.txt", "um\nMEU\ntres\n", "na main");
        try { await Git(dir, "merge", "outra"); } catch (GitException) { /* conflito esperado */ }
        return dir;
    }

    // ------------------------------------------------------------- 1. conflitos

    [Theory]
    [InlineData(true, "um\nMEU\ntres\n")]
    [InlineData(false, "um\nDELES\ntres\n")]
    public async Task Conflito_resolvido_com_um_dos_lados(bool meu, string esperado)
    {
        var dir = await RepoEmConflito();
        try
        {
            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            Assert.Equal(GitService.Operacao.Merge, vm.Operacao);
            var item = Assert.Single(vm.Unstaged, f => f.IsConflito);
            Assert.Equal("UU", item.Change.Conflito);

            if (meu) await vm.ManterMeuCommand.ExecuteAsync(item);
            else await vm.ManterDelesCommand.ExecuteAsync(item);

            Assert.Equal(esperado, Ler(dir, "a.txt"));
            Assert.DoesNotContain(vm.Unstaged, f => f.IsConflito);

            await vm.ContinuarOperacaoCommand.ExecuteAsync(null);
            Assert.Equal(GitService.Operacao.Nenhuma, vm.Operacao);
            Assert.Equal(2, (await Git(dir, "log", "-1", "--format=%P")).Trim().Split(' ').Length);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task No_rebase_meu_continua_sendo_o_meu_commit()
    {
        var dir = await NovoRepo();
        try
        {
            await Git(dir, "checkout", "-qb", "feat");
            await Commit(dir, "a.txt", "um\nMEU\ntres\n", "minha feature");
            await Git(dir, "checkout", "-q", "main");
            await Commit(dir, "a.txt", "um\nDA MAIN\ntres\n", "na main");
            await Git(dir, "checkout", "-q", "feat");
            try { await Git(dir, "rebase", "main"); } catch (GitException) { /* conflito esperado */ }

            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            Assert.Equal(GitService.Operacao.Rebase, vm.Operacao);

            await vm.ManterMeuCommand.ExecuteAsync(vm.Unstaged.Single(f => f.IsConflito));
            Assert.Equal("um\nMEU\ntres\n", Ler(dir, "a.txt"));

            await vm.ContinuarOperacaoCommand.ExecuteAsync(null);
            Assert.Equal(GitService.Operacao.Nenhuma, vm.Operacao);
            Assert.Equal("minha feature", (await Git(dir, "log", "-1", "--format=%s")).Trim());
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Abortar_volta_ao_estado_de_antes_do_merge()
    {
        var dir = await RepoEmConflito();
        try
        {
            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            await vm.AbortarOperacaoCommand.ExecuteAsync(null);
            Assert.Equal(GitService.Operacao.Nenhuma, vm.Operacao);
            Assert.Equal("um\nMEU\ntres\n", Ler(dir, "a.txt"));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Preparar_tudo_nao_marca_conflito_como_resolvido()
    {
        var dir = await RepoEmConflito();
        try
        {
            Escrever(dir, "b.txt", "novo\n");
            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            await vm.StageAllCommand.ExecuteAsync(null);
            Assert.Contains(vm.Unstaged, f => f.IsConflito);
            Assert.Contains(vm.Staged, f => f.Path == "b.txt");
        }
        finally { Limpar(dir); }
    }

    // ---------------------------------------------------- 2. preparar por linha

    [Fact]
    public async Task Preparar_so_uma_das_linhas_alteradas()
    {
        var dir = await NovoRepo();
        try
        {
            Escrever(dir, "a.txt", "um\nDOIS\ntres\nquatro\n");
            var (vm, _) = Changes(dir);
            await vm.ReloadAsync();
            await Task.Delay(200); // o diff carrega em segundo plano ao selecionar

            var linhas = vm.Diff.Rows.OfType<DiffTextRow>().ToList();
            vm.Diff.AlternarLinha(linhas.Single(l => l.IsAdd && l.Line.Text == "quatro"), false);
            var cabecalho = vm.Diff.Rows.OfType<DiffHeaderRow>().Single();
            Assert.Equal("Preparar 1 linha(s)", cabecalho.LinhasRotulo);

            await cabecalho.ApplyLinhasCommand.ExecuteAsync(null);
            Assert.Equal("um\ndois\ntres\nquatro\n", await Git(dir, "show", ":a.txt"));
            Assert.Equal("um\nDOIS\ntres\nquatro\n", Ler(dir, "a.txt"));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Remover_do_indice_so_a_linha_escolhida()
    {
        var dir = await NovoRepo();
        try
        {
            Escrever(dir, "a.txt", "um\nDOIS\ntres\nquatro\n");
            await Git(dir, "add", "a.txt");

            var diff = DiffParser.Parse(await GitService.DiffFileAsync(dir, "a.txt", staged: true));
            var hunk = diff.Hunks.Single();
            var escolhidas = new HashSet<DiffLine>(hunk.Lines.Where(l => l.Kind == DiffLineKind.Add && l.Text == "quatro"));
            await GitService.ApplyPatchAsync(dir, DiffParser.BuildLinesPatch(diff, hunk, escolhidas, reverso: true), true, true);

            Assert.Equal("um\nDOIS\ntres\n", await Git(dir, "show", ":a.txt"));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public void Linha_removida_nao_escolhida_vira_contexto()
    {
        var diff = DiffParser.Parse("diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1,3 +1,3 @@\n um\n-dois\n+DOIS\n tres\n");
        var hunk = diff.Hunks[0];
        var patch = DiffParser.BuildLinesPatch(diff, hunk, new HashSet<DiffLine> { hunk.Lines[2] }, reverso: false);
        Assert.Contains("@@ -1,3 +1,4 @@\n um\n dois\n+DOIS\n tres\n", patch);
    }

    // ------------------------------------------------------------ 3. desfazer

    [Theory]
    [InlineData("checkout: moving from main to feat", "", "", "checkout main")]
    [InlineData("commit: x", "commit: x", "abc", "reset --soft abc")]
    [InlineData("commit (amend): x", "commit (amend): x", "abc", "reset --soft abc")]
    [InlineData("reset: moving to HEAD~1", "reset: moving to HEAD~1", "abc", "reset --keep abc")]
    [InlineData("merge x: Fast-forward", "merge x: Fast-forward", "abc", "reset --keep abc")]
    [InlineData("rebase (finish): returning", "rebase (finish): refs/heads/f onto 1", "abc", "reset --keep abc")]
    public void Desfazer_entende_o_reflog(string head, string ramo, string anterior, string esperado)
    {
        var plano = Desfazer.Interpretar(head, ramo, anterior);
        Assert.NotNull(plano);
        Assert.Equal(esperado, string.Join(' ', plano!.Args));
    }

    [Fact]
    public void Primeiro_commit_nao_tem_o_que_desfazer() =>
        Assert.Null(Desfazer.Interpretar("commit (initial): um", "commit (initial): um", ""));

    [Fact]
    public async Task Desfazer_commit_devolve_as_alteracoes_preparadas_e_desfazer_de_novo_refaz()
    {
        var dir = await NovoRepo();
        try
        {
            var inicial = (await Git(dir, "rev-parse", "HEAD")).Trim();
            await Commit(dir, "a.txt", "mudado\n", "segundo");
            var segundo = (await Git(dir, "rev-parse", "HEAD")).Trim();

            var plano = await Desfazer.PlanejarAsync(dir);
            Assert.Contains("segundo", plano!.Descricao);
            Assert.False(plano.JaEnviado);
            await Git(dir, plano.Args);
            Assert.Equal(inicial, (await Git(dir, "rev-parse", "HEAD")).Trim());
            Assert.StartsWith("M ", await Git(dir, "status", "--porcelain"));

            var refazer = await Desfazer.PlanejarAsync(dir);
            await Git(dir, refazer!.Args);
            Assert.Equal(segundo, (await Git(dir, "rev-parse", "HEAD")).Trim());
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Desfazer_troca_de_branch_volta_para_a_anterior()
    {
        var dir = await NovoRepo();
        try
        {
            await Git(dir, "checkout", "-qb", "feat");
            var plano = await Desfazer.PlanejarAsync(dir);
            await Git(dir, plano!.Args);
            Assert.Equal("main", (await Git(dir, "branch", "--show-current")).Trim());
        }
        finally { Limpar(dir); }
    }

    // ------------------------------------------ 4. histórico do arquivo e blame

    [Fact]
    public async Task Historico_do_arquivo_segue_a_renomeacao()
    {
        var dir = await NovoRepo();
        try
        {
            await Git(dir, "mv", "a.txt", "b.txt");
            await Git(dir, "commit", "-qm", "renomeia");
            await Commit(dir, "b.txt", "um\ndois\ntres\nquatro\n", "acrescenta");

            var vm = new FileHistoryViewModel(new Repo { Path = dir }, "b.txt", blame: true, split: true);
            await vm.CarregarAsync();

            Assert.Equal(new[] { "acrescenta", "renomeia", "inicial" }, vm.Commits.Select(c => c.Subject));
            Assert.Equal("a.txt", vm.Commits[2].Caminho);
            Assert.True(vm.Commits[2].Renomeado);

            Assert.Equal(4, vm.Blame.Count);
            Assert.Equal("Teste", vm.Blame[0].Autor);
            Assert.True(vm.Blame[3].InicioDeBloco); // "quatro" veio de outro commit
            Assert.False(vm.Blame[1].InicioDeBloco);

            vm.IrParaCommit(vm.Blame[3]);
            Assert.Equal(0, vm.AbaSelecionada);
            Assert.Equal("acrescenta", vm.Selecionado!.Subject);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Blame_marca_linha_nao_commitada()
    {
        var dir = await NovoRepo();
        try
        {
            Escrever(dir, "a.txt", "um\nLOCAL\ntres\n");
            var linhas = await GitService.BlameAsync(dir, "a.txt");
            Assert.True(linhas[1].NaoCommitada);
            Assert.False(linhas[0].NaoCommitada);
            Assert.Equal("inicial", linhas[0].Assunto);
        }
        finally { Limpar(dir); }
    }

    // ---------------------------------------------------------------- 5. busca

    [Fact]
    public async Task Busca_por_mensagem_autor_e_hash()
    {
        var dir = await NovoRepo();
        try
        {
            await Commit(dir, "a.txt", "x\n", "Corrige cálculo de juros");
            await Git(dir, "-c", "user.name=Maria Souza", "commit", "-q", "--allow-empty", "-m", "outro");
            var inicial = (await Git(dir, "rev-list", "--max-parents=0", "HEAD")).Trim();

            Assert.Equal("Corrige cálculo de juros",
                Assert.Single(await GitService.SearchLogAsync(dir, "JUROS", 100, true)).Subject);
            Assert.Equal("outro", Assert.Single(await GitService.SearchLogAsync(dir, "maria", 100, true)).Subject);
            Assert.Equal(inicial, (await GitService.SearchLogAsync(dir, inicial[..7], 100, true))[0].Hash);
            Assert.Empty(await GitService.SearchLogAsync(dir, "nada disso", 100, true));

            // hash colado com "#" na frente, como sai de um chat ou de uma issue
            Assert.Equal(inicial, (await GitService.SearchLogAsync(dir, "#" + inicial[..7], 100, true))[0].Hash);
            Assert.Equal(inicial, (await GitService.SearchLogAsync(dir, " #" + inicial + " ", 100, true))[0].Hash);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Busca_por_prefixo_ambiguo_lista_todos_os_commits()
    {
        var dir = await NovoRepo();
        try
        {
            // dois mil commits de uma vez pelo fast-import: com 4 dígitos (65.536 combinações)
            // sobram dezenas de pares que começam igual
            var fluxo = new System.Text.StringBuilder();
            for (var i = 0; i < 2000; i++)
                fluxo.Append("commit refs/heads/muitos\n")
                     .Append($"committer Teste <t@t> {1700000000 + i * 60} +0000\n")
                     .Append($"data <<FIM\nm{i}\nFIM\n\n");
            await GitService.RunAsync(dir, new[] { "fast-import", "--quiet" }, fluxo.ToString());

            var grupo = (await Git(dir, "rev-list", "muitos")).Split('\n')
                .Select(h => h.Trim()).Where(h => h.Length > 0)
                .GroupBy(h => h[..4]).First(g => g.Count() > 1);
            var esperados = grupo.OrderBy(h => h).ToList();

            // o caminho antigo, prefixo^{commit}, falha aqui
            await Assert.ThrowsAsync<GitException>(() => Git(dir, "rev-parse", grupo.Key + "^{commit}"));

            var achados = await GitService.SearchLogAsync(dir, grupo.Key, 100, true);
            Assert.Equal(esperados, achados.Select(c => c.Hash).OrderBy(h => h).ToList());
            Assert.Equal(achados.OrderByDescending(c => DateTimeOffset.Parse(c.Date)).Select(c => c.Hash),
                achados.Select(c => c.Hash));

            Assert.Equal(esperados,
                (await GitService.SearchLogAsync(dir, "#" + grupo.Key, 100, true)).Select(c => c.Hash).OrderBy(h => h).ToList());
        }
        finally { Limpar(dir); }
    }

    // ----------------------------------------------------- 6. rebase interativo

    private static async Task<(string Dir, List<ItemRebase> Itens)> RepoComTresCommits()
    {
        var dir = await NovoRepo();
        await Commit(dir, "b.txt", "b\n", "b");
        await Commit(dir, "c.txt", "c\n", "c");
        await Commit(dir, "d.txt", "d\n", "d");
        var b = (await Git(dir, "rev-parse", "HEAD~2")).Trim();
        return (dir, await RebaseInterativo.ListarAsync(dir, b));
    }

    private static async Task<string[]> Assuntos(string dir) =>
        (await Git(dir, "log", "--format=%s")).Trim().Split('\n').Select(s => s.Trim()).ToArray();

    [Fact]
    public async Task Rebase_junta_renomeia_e_apaga()
    {
        var (dir, itens) = await RepoComTresCommits();
        try
        {
            Assert.Equal(new[] { "b", "c", "d" }, itens.Select(i => i.Assunto));
            itens[0].Acao = AcaoRebase.Renomear;
            itens[0].NovaMensagem = "b renomeado\n\ncom corpo e \"aspas\"";
            itens[1].Acao = AcaoRebase.JuntarSemMensagem;
            itens[2].Acao = AcaoRebase.Apagar;
            Assert.Null(RebaseInterativo.Validar(itens));

            await RebaseInterativo.AplicarAsync(dir, itens);

            Assert.Equal(new[] { "b renomeado", "inicial" }, await Assuntos(dir));
            Assert.Contains("com corpo e \"aspas\"", await Git(dir, "log", "-1", "--format=%B"));
            Assert.True(File.Exists(Path.Combine(dir, "c.txt")));
            Assert.False(File.Exists(Path.Combine(dir, "d.txt")));
            Assert.Equal(GitService.Operacao.Nenhuma, GitService.OperacaoEmAndamento(dir));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Rebase_reordena_pela_janela()
    {
        var (dir, _) = await RepoComTresCommits();
        try
        {
            var b = (await Git(dir, "rev-parse", "HEAD~2")).Trim();
            var vm = new RebaseViewModel(new Repo { Path = dir }, b);
            await vm.CarregarAsync();
            Assert.Equal(new[] { "d", "c", "b" }, vm.Itens.Select(i => i.Item.Assunto));
            Assert.False(vm.PodeAplicar);

            vm.DescerCommand.Execute(vm.Itens[0]); // d passa a ser anterior a c
            Assert.True(vm.PodeAplicar);
            var fechou = false;
            vm.Fechar = _ => fechou = true;
            await vm.AplicarCommand.ExecuteAsync(null);

            Assert.True(fechou);
            Assert.Equal(new[] { "c", "d", "b", "inicial" }, await Assuntos(dir));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Juntar_no_mais_antigo_nao_tem_onde_juntar()
    {
        var (dir, itens) = await RepoComTresCommits();
        try
        {
            itens[0].Acao = AcaoRebase.Juntar;
            Assert.Contains("não tem commit anterior", RebaseInterativo.Validar(itens));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Rebase_recusa_merge_no_caminho()
    {
        var dir = await NovoRepo();
        try
        {
            var inicial = (await Git(dir, "rev-parse", "HEAD")).Trim();
            await Git(dir, "checkout", "-qb", "f");
            await Commit(dir, "f.txt", "f\n", "f");
            await Git(dir, "checkout", "-q", "main");
            await Commit(dir, "m.txt", "m\n", "m");
            await Git(dir, "merge", "-q", "--no-edit", "f");

            var e = await Assert.ThrowsAsync<GitException>(() => RebaseInterativo.ListarAsync(dir, inicial));
            Assert.Contains("merge", e.Message);
        }
        finally { Limpar(dir); }
    }

    // ------------------------------------------------- 7. emendar e Ctrl+Enter

    [Fact]
    public async Task Emendar_traz_a_mensagem_do_ultimo_e_desmarcar_tira()
    {
        var dir = await NovoRepo();
        try
        {
            var (vm, _) = Changes(dir);
            vm.Amend = true;
            for (var i = 0; i < 50 && vm.CommitMessage == ""; i++) await Task.Delay(20);
            Assert.Equal("inicial", vm.CommitMessage);

            vm.Amend = false;
            Assert.Equal("", vm.CommitMessage);

            vm.CommitMessage = "minha";
            vm.Amend = true;
            await Task.Delay(200);
            Assert.Equal("minha", vm.CommitMessage); // não pisa no que já foi digitado
        }
        finally { Limpar(dir); }
    }
}
