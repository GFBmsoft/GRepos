using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Operações em lote: cada repositório responde por si — feito, pulado com motivo ou
/// erro — e a falha de um não para os outros.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class LoteTests
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

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task Commit(string dir, string nome, string msg)
    {
        File.WriteAllText(Path.Combine(dir, nome), msg + "\n");
        await Git(dir, "add", nome);
        await Git(dir, "commit", "-qm", msg);
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

    /// <summary>Remoto com main e develop, um "colega" que alimenta e um clone do usuário.</summary>
    private static async Task<(string Raiz, string Colega, string Meu)> Cenario(string nome)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-lote-" + Path.GetRandomFileName(), nome);
        var remoto = Path.Combine(raiz, "remoto.git");
        var colega = Path.Combine(raiz, "colega");
        var meu = Path.Combine(raiz, "meu");
        Directory.CreateDirectory(raiz);

        await Git(raiz, "init", "-q", "--bare", "-b", "main", remoto);
        await Git(raiz, "clone", "-q", remoto, colega);
        foreach (var d in new[] { colega })
        {
            await Git(d, "config", "user.email", "t@t");
            await Git(d, "config", "user.name", "Teste");
        }
        await Git(colega, "checkout", "-qb", "main");
        await Commit(colega, "a.txt", "inicial");
        await Git(colega, "push", "-q", "-u", "origin", "main");
        await Git(colega, "checkout", "-qb", "develop");
        await Commit(colega, "d.txt", "na develop");
        await Git(colega, "push", "-q", "-u", "origin", "develop");
        await Git(colega, "checkout", "-q", "main");

        await Git(raiz, "clone", "-q", remoto, meu);
        await Git(meu, "config", "user.email", "t@t");
        await Git(meu, "config", "user.name", "Teste");
        return (raiz, colega, meu);
    }

    [Fact]
    public async Task Trocar_cria_a_local_a_partir_do_remoto_e_pula_quem_nao_tem_a_branch()
    {
        var (raiz, _, meu) = await Cenario("a");
        try
        {
            var troca = await Lote.ExecutarAsync(meu, OperacaoEmLote.Trocar, "develop");
            Assert.Equal("ok", troca.Situacao);
            Assert.Equal("develop", (await GitService.StatusAsync(meu)).Branch);

            Assert.Equal("pulado", (await Lote.ExecutarAsync(meu, OperacaoEmLote.Trocar, "develop")).Situacao);

            var semBranch = await Lote.ExecutarAsync(meu, OperacaoEmLote.Trocar, "release/9.9");
            Assert.Equal("pulado", semBranch.Situacao);
            Assert.Equal("não tem essa branch", semBranch.Mensagem);
        }
        finally
        {
            Limpar(Path.GetDirectoryName(raiz)!);
        }
    }

    [Fact]
    public async Task Puxar_so_avanca_e_nao_abre_merge_onde_ha_commit_local()
    {
        var (raiz, colega, meu) = await Cenario("a");
        try
        {
            await Commit(colega, "novo.txt", "do colega");
            await Git(colega, "push", "-q");

            var limpo = await Lote.ExecutarAsync(meu, OperacaoEmLote.Puxar);
            Assert.Equal(("ok", "atualizado"), (limpo.Situacao, limpo.Mensagem));
            Assert.Equal("já estava em dia", (await Lote.ExecutarAsync(meu, OperacaoEmLote.Puxar)).Mensagem);

            // agora os dois lados andam: o lote não mescla, avisa
            await Commit(colega, "outro.txt", "do colega de novo");
            await Git(colega, "push", "-q");
            await Commit(meu, "meu.txt", "meu");

            var divergente = await Lote.ExecutarAsync(meu, OperacaoEmLote.Puxar);
            Assert.Equal("erro", divergente.Situacao);
            Assert.Contains("puxe este repositório à mão", divergente.Mensagem);
            Assert.Equal(GitService.Operacao.Nenhuma, GitService.OperacaoEmAndamento(meu));
        }
        finally
        {
            Limpar(Path.GetDirectoryName(raiz)!);
        }
    }

    [Fact]
    public async Task Enviar_pula_quem_nao_tem_nada_e_envia_quem_tem()
    {
        var (raiz, _, meu) = await Cenario("a");
        try
        {
            var nada = await Lote.ExecutarAsync(meu, OperacaoEmLote.Enviar);
            Assert.Equal(("pulado", "nada a enviar"), (nada.Situacao, nada.Mensagem));

            await Commit(meu, "meu.txt", "meu");
            var enviado = await Lote.ExecutarAsync(meu, OperacaoEmLote.Enviar);
            Assert.Equal("ok", enviado.Situacao);
            Assert.Equal(0, (await GitService.StatusAsync(meu)).Ahead);
        }
        finally
        {
            Limpar(Path.GetDirectoryName(raiz)!);
        }
    }

    [Fact]
    public async Task Pasta_que_nao_e_repositorio_vira_erro_na_linha_e_nao_excecao()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "grepos-lote-vazio-" + Path.GetRandomFileName());
        Directory.CreateDirectory(pasta);
        try
        {
            var r = await Lote.ExecutarAsync(pasta, OperacaoEmLote.Obter);
            Assert.Equal("erro", r.Situacao);
            Assert.True(r.Mensagem.Length > 0);
        }
        finally
        {
            Limpar(pasta);
        }
    }

    [Fact]
    public void Erro_do_git_vira_uma_linha_que_explica()
    {
        Assert.Contains("commite ou guarde",
            Lote.Resumir("error: Your local changes to the following files would be overwritten by checkout:\n\ta.txt"));
        Assert.Equal("fatal: not a git repository",
            Lote.Resumir("git checkout x\nfatal: not a git repository\nmais coisa"));
    }

    [Fact]
    public async Task Lote_troca_os_marcados_e_resume_o_que_aconteceu()
    {
        var (raizA, _, a) = await Cenario("a");
        var raiz = Path.GetDirectoryName(raizA)!;
        var home = Path.Combine(raiz, "home");
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            // o segundo repositório não tem develop: é o que deve ser pulado
            var b = Path.Combine(raiz, "b");
            Directory.CreateDirectory(b);
            await Git(b, "init", "-q", "-b", "main");
            await Git(b, "config", "user.email", "t@t");
            await Git(b, "config", "user.name", "Teste");
            await Commit(b, "a.txt", "inicial");

            var main = new MainViewModel(new FakeDialogs());
            var vm = main.MontarLote("par Financeiro", new[]
            {
                new Repo { Id = "a", Name = "Origem", Path = a },
                new Repo { Id = "b", Name = "Destino", Path = b },
            });

            await vm.CarregarBranchesAsync();
            Assert.Equal("main", vm.Branches[0]); // a que existe nos dois vem primeiro
            Assert.Contains("develop", vm.Branches);

            vm.Operacao = vm.Operacoes.Single(o => o.Operacao == OperacaoEmLote.Trocar);
            Assert.True(vm.PedeBranch);
            Assert.False(vm.PodeExecutar); // falta a branch

            vm.Branch = "develop";
            Assert.Equal("Trocar de branch em 2 repositórios", vm.ExecutarRotulo);

            var confirmou = false;
            vm.Confirmar = (_, _) => { confirmou = true; return Task.FromResult(true); };
            await vm.ExecutarCommand.ExecuteAsync(null);

            Assert.True(confirmou);
            Assert.True(vm.Executou);
            Assert.Equal("1 feito(s), 1 pulado(s), 0 com erro", vm.Resumo);
            Assert.Equal("ok", vm.Itens.Single(i => i.Nome == "Origem").Situacao);
            Assert.Equal("develop", vm.Itens.Single(i => i.Nome == "Origem").Branch);
            Assert.Equal("pulado", vm.Itens.Single(i => i.Nome == "Destino").Situacao);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    [Fact]
    public async Task Desmarcado_fica_de_fora_e_obter_nao_pede_confirmacao()
    {
        var (raiz, _, meu) = await Cenario("a");
        var home = Path.Combine(raiz, "home");
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            var main = new MainViewModel(new FakeDialogs());
            var vm = main.MontarLote("grupo", new[]
            {
                new Repo { Id = "a", Name = "Um", Path = meu },
                new Repo { Id = "b", Name = "Dois", Path = meu },
            });
            vm.Itens[1].Marcado = false;
            Assert.Equal("Obter em 1 repositório", vm.ExecutarRotulo);

            var perguntou = false;
            vm.Confirmar = (_, _) => { perguntou = true; return Task.FromResult(true); };
            await vm.ExecutarCommand.ExecuteAsync(null);

            Assert.False(perguntou);
            Assert.Equal("ok", vm.Itens[0].Situacao);
            Assert.Equal("", vm.Itens[1].Situacao);

            vm.MarcarTodosCommand.Execute(null);
            Assert.Equal(2, vm.Marcados);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(Path.GetDirectoryName(raiz)!);
        }
    }
}
