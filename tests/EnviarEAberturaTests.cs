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
/// O Enviar que leva ao histórico e a abertura do app com muitos repositórios: a esteira de
/// cada cartão sai assim que o status do repositório dele chega, sem esperar os outros.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class EnviarEAberturaTests
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

    /// <summary>Clone de um remoto local com um commit a enviar; devolve a raiz e o clone.</summary>
    private static async Task<(string Raiz, string Meu)> ComCommitAEnviar()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-enviar-" + Path.GetRandomFileName());
        var remoto = Path.Combine(raiz, "remoto.git");
        var meu = Path.Combine(raiz, "meu");
        Directory.CreateDirectory(raiz);

        await Git(raiz, "init", "-q", "--bare", "-b", "main", remoto);
        await Git(raiz, "clone", "-q", remoto, meu);
        await Git(meu, "config", "user.email", "t@t");
        await Git(meu, "config", "user.name", "Teste");
        await Git(meu, "checkout", "-qb", "main");
        File.WriteAllText(Path.Combine(meu, "a.txt"), "inicial\n");
        await Git(meu, "add", "a.txt");
        await Git(meu, "commit", "-qm", "inicial");
        await Git(meu, "push", "-q", "-u", "origin", "main");

        File.WriteAllText(Path.Combine(meu, "b.txt"), "novo\n");
        await Git(meu, "add", "b.txt");
        await Git(meu, "commit", "-qm", "a enviar");
        return (raiz, meu);
    }

    private static async Task<MainViewModel> AbrirNaAbaAlteracoes(string meu)
    {
        var main = new MainViewModel(new FakeDialogs());
        main.AddRepository(meu, "Financeiro", null);
        main.RebuildTree();
        var id = main.Repos.Single().Id;
        main.SelecionarRepositorio(id);
        await main.RefreshRepoAsync(id);

        Assert.Equal(0, main.SelectedTab);
        Assert.Equal(1, main.CurrentStatus?.Ahead);
        return main;
    }

    /// <summary>
    /// O pedido: depois de commitar e enviar, o próximo passo é olhar o histórico, e era
    /// preciso clicar na aba. Enviado, ela troca sozinha.
    /// </summary>
    [Fact]
    public async Task Enviar_da_aba_alteracoes_leva_ao_historico()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", Path.Combine(raiz, "home"));
        try
        {
            var main = await AbrirNaAbaAlteracoes(meu);

            await main.PushCommand.ExecuteAsync(null);

            Assert.Equal(0, main.CurrentStatus?.Ahead);
            Assert.Equal(1, main.SelectedTab);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    /// <summary>Envio que falhou: o commit continua por enviar, e a aba fica onde estava.</summary>
    [Fact]
    public async Task Enviar_que_falhou_fica_nas_alteracoes()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", Path.Combine(raiz, "home"));
        try
        {
            await Git(meu, "remote", "set-url", "origin", Path.Combine(raiz, "nao-existe.git"));
            var main = await AbrirNaAbaAlteracoes(meu);

            await main.PushCommand.ExecuteAsync(null);

            Assert.Equal("erro", main.StatusTipo);
            Assert.Equal(0, main.SelectedTab);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    /// <summary>
    /// Na abertura, a esteira de cada cartão esperava a varredura dos sessenta repositórios
    /// terminar. Agora cada um espera só o status do seu: liberado um, ele consulta e termina
    /// enquanto o outro ainda espera. Sem remoto do GitHub, termina como "sem esteira".
    /// </summary>
    [Fact]
    public async Task Cada_cartao_consulta_a_esteira_quando_o_seu_status_chega()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-abertura-" + Path.GetRandomFileName());
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            var main = new MainViewModel(new FakeDialogs());
            foreach (var nome in new[] { "Um", "Dois" })
            {
                var repo = Path.Combine(home, nome);
                Directory.CreateDirectory(repo);
                await Git(repo, "init", "-q", "-b", "main");
                main.AddRepository(repo, nome, null);
            }

            var cartoes = main.Repos
                .Select(r => new CartaoRepoViewModel(r, null, "#4F8CFF", main))
                .ToList();
            var painel = new PainelViewModel("Todos", "", cartoes, main);

            var liberar = cartoes.ToDictionary(c => c, _ => new TaskCompletionSource<bool>());
            var carga = painel.CarregarEsteirasAsync(c => liberar[c].Task);

            liberar[cartoes[0]].SetResult(true);
            await Espera.Ate(() => cartoes[0].CiSituacao == "nenhum");
            Assert.True(cartoes[1].ConsultandoCi);
            Assert.False(carga.IsCompleted);

            // falso: o painel saiu de cena enquanto esperava, e o cartão fica de fora
            liberar[cartoes[1]].SetResult(false);
            await carga;
            Assert.True(cartoes[1].ConsultandoCi);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(home);
        }
    }

    /// <summary>
    /// O aviso de cada repositório sai da varredura de verdade: o painel aberto durante ela
    /// termina com todos os cartões consultados e com o status de cada um.
    /// </summary>
    [Fact]
    public async Task Painel_aberto_durante_a_varredura_consulta_todos_os_cartoes()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-abertura-" + Path.GetRandomFileName());
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            var main = new MainViewModel(new FakeDialogs());
            // mais repositórios que vagas na fila da varredura
            for (var i = 0; i < 12; i++)
            {
                var repo = Path.Combine(home, "repo" + i);
                Directory.CreateDirectory(repo);
                await Git(repo, "init", "-q", "-b", "main");
                main.AddRepository(repo, "Repo" + i, null);
            }

            // como na abertura: a varredura começa e o painel aparece logo em seguida
            var varredura = main.RefreshAllAsync();
            main.MostrarPainel(null);
            await varredura;

            var painel = main.Painel!;
            await Espera.Ate(() => painel.Cartoes.All(c => c.CiSituacao == "nenhum"));
            Assert.All(painel.Cartoes, c => Assert.Equal("main", c.Status?.Branch));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(home);
        }
    }
}
