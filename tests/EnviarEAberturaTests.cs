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
/// E os selos do Enviar e do Puxar, que contam os arquivos que vão ao remoto e vêm dele.
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

    /// <summary>Marca de commit, depois os arquivos dele; o mesmo arquivo em dois commits conta uma vez.</summary>
    [Fact]
    public void Envio_lido_do_log()
    {
        Assert.Equal(EnvioPendente.Nada, EnvioPendente.DoLog(""));
        Assert.Equal(new EnvioPendente(1, 2), EnvioPendente.DoLog("\u0001\n\na.txt\nb.txt\n"));

        // um commit vazio, e o c.txt mexido em dois commits
        Assert.Equal(new EnvioPendente(3, 2),
            EnvioPendente.DoLog("\u0001\n\u0001\n\nc.txt\n\u0001\n\nImpressão/b ç.txt\nc.txt\n"));
        Assert.Equal(new EnvioPendente(1, 1), EnvioPendente.DoLog("\u0001\r\n\r\na.txt\r\n"));
    }

    [Theory]
    [InlineData(1, 3, "origin/main", "Enviar 1 commit com 3 arquivos (push)")]
    [InlineData(2, 1, "origin/main", "Enviar 2 commits com 1 arquivo (push)")]
    [InlineData(1, 0, "origin/main", "Enviar 1 commit, sem arquivo alterado (push)")]
    [InlineData(2, 5, null, "Enviar a branch feat/pix, que ainda não existe no remoto: 2 commits com 5 arquivos (push)")]
    [InlineData(0, 0, null, "Enviar a branch feat/pix, que ainda não existe no remoto (push)")]
    public void Dica_do_enviar_diz_commits_e_arquivos(int commits, int arquivos, string? upstream, string esperado)
    {
        var status = new RepoStatus { Branch = "feat/pix", Upstream = upstream, Ahead = commits };
        Assert.Equal(esperado, new EnvioPendente(commits, arquivos).Dica(status));
    }

    /// <summary>
    /// O pedido: o Enviar não mostrava o total de arquivos a enviar. O número era o de
    /// commits, e um commit com três arquivos mostrava "1".
    /// </summary>
    [Fact]
    public async Task Envio_conta_os_arquivos_e_nao_os_commits()
    {
        var (raiz, meu) = await ComCommitAEnviar(); // um commit a enviar, com o b.txt
        try
        {
            // o segundo mexe de novo no b.txt e traz dois arquivos, um deles com acento
            File.WriteAllText(Path.Combine(meu, "b.txt"), "de novo\n");
            File.WriteAllText(Path.Combine(meu, "c.txt"), "c\n");
            Directory.CreateDirectory(Path.Combine(meu, "Impressão"));
            File.WriteAllText(Path.Combine(meu, "Impressão", "d ç.txt"), "d\n");
            await Git(meu, "add", "-A");
            await Git(meu, "commit", "-qm", "mais dois");

            var status = await GitService.StatusAsync(meu);

            Assert.Equal(new EnvioPendente(2, 3), await GitService.EnvioPendenteAsync(meu, status));
        }
        finally { Limpar(raiz); }
    }

    /// <summary>Outro clone envia um commit com dois arquivos, e este busca: fica um commit atrás.</summary>
    private static async Task ColegaEnviaDoisArquivos(string raiz, string meu)
    {
        var outro = Path.Combine(raiz, "outro");
        await Git(raiz, "clone", "-q", Path.Combine(raiz, "remoto.git"), outro);
        await Git(outro, "config", "user.email", "o@o");
        await Git(outro, "config", "user.name", "Outro");
        File.WriteAllText(Path.Combine(outro, "x.txt"), "x\n");
        File.WriteAllText(Path.Combine(outro, "y.txt"), "y\n");
        await Git(outro, "add", "-A");
        await Git(outro, "commit", "-qm", "do colega");
        await Git(outro, "push", "-q");
        await Git(meu, "fetch", "-q");
    }

    /// <summary>
    /// Atrás e à frente ao mesmo tempo: cada sentido conta só os arquivos do seu lado. O que
    /// só o remoto tem não é arquivo a enviar, e o commit daqui não é arquivo a puxar.
    /// </summary>
    [Fact]
    public async Task Atras_e_a_frente_cada_lado_conta_os_seus_arquivos()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        try
        {
            var emDia = await GitService.StatusAsync(meu);
            Assert.Equal(RecebimentoPendente.Nada, await GitService.RecebimentoPendenteAsync(meu, emDia));

            await ColegaEnviaDoisArquivos(raiz, meu);
            var status = await GitService.StatusAsync(meu);

            Assert.Equal(1, status.Behind);
            Assert.Equal(new EnvioPendente(1, 1), await GitService.EnvioPendenteAsync(meu, status));
            Assert.Equal(new RecebimentoPendente(1, 2), await GitService.RecebimentoPendenteAsync(meu, status));
        }
        finally { Limpar(raiz); }
    }

    [Theory]
    [InlineData(1, 2, "Trazer 1 commit com 2 arquivos do remoto (pull). Clique direito: opções")]
    [InlineData(3, 1, "Trazer 3 commits com 1 arquivo do remoto (pull). Clique direito: opções")]
    [InlineData(2, 0, "Trazer 2 commits do remoto, sem arquivo alterado (pull). Clique direito: opções")]
    [InlineData(0, 0, "Trazer os commits do remoto (pull). Clique direito: opções")]
    public void Dica_do_puxar_diz_commits_e_arquivos(int commits, int arquivos, string esperado) =>
        Assert.Equal(esperado, new RecebimentoPendente(commits, arquivos).Dica);

    /// <summary>
    /// O pedido seguinte ao do Enviar: o Puxar também em arquivos, para os dois selos, lado a
    /// lado, contarem a mesma coisa. Puxado, o selo some e a dica volta à de sempre.
    /// </summary>
    [Fact]
    public async Task Selo_do_puxar_mostra_os_arquivos()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", Path.Combine(raiz, "home"));
        try
        {
            // só atrás: o commit daqui já foi, e o do colega chega com dois arquivos
            await Git(meu, "push", "-q");
            await ColegaEnviaDoisArquivos(raiz, meu);

            var main = new MainViewModel(new FakeDialogs());
            main.AddRepository(meu, "Financeiro", null);
            main.RebuildTree();
            var id = main.Repos.Single().Id;
            main.SelecionarRepositorio(id);
            await main.RefreshRepoAsync(id);

            await Espera.Ate(() => main.PuxarBadge == "2");
            Assert.Equal(1, main.CurrentStatus?.Behind);
            Assert.Equal("", main.EnviarBadge);
            Assert.Equal("Trazer 1 commit com 2 arquivos do remoto (pull). Clique direito: opções", main.PuxarTooltip);

            await main.PullCommand.ExecuteAsync(null);

            Assert.Equal(0, main.CurrentStatus?.Behind);
            Assert.Equal("", main.PuxarBadge);
            Assert.Equal(RecebimentoPendente.DicaSemContagem, main.PuxarTooltip);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    /// <summary>
    /// Branch que ainda não existe no remoto: o status não traz a contagem, e o selo ficava
    /// vazio mesmo com commits. Conta o que nenhuma branch do origin tem.
    /// </summary>
    [Fact]
    public async Task Branch_sem_par_conta_o_que_o_remoto_ainda_nao_tem()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        try
        {
            await Git(meu, "push", "-q");
            await Git(meu, "checkout", "-qb", "feat/pix");

            // recém-criada: vai só a branch
            var nova = await GitService.StatusAsync(meu);
            Assert.True(string.IsNullOrEmpty(nova.Upstream));
            Assert.Equal(EnvioPendente.Nada, await GitService.EnvioPendenteAsync(meu, nova));

            File.WriteAllText(Path.Combine(meu, "c.txt"), "c\n");
            await Git(meu, "add", "-A");
            await Git(meu, "commit", "-qm", "um");
            File.WriteAllText(Path.Combine(meu, "c.txt"), "c de novo\n");
            File.WriteAllText(Path.Combine(meu, "d.txt"), "d\n");
            await Git(meu, "add", "-A");
            await Git(meu, "commit", "-qm", "dois");

            var status = await GitService.StatusAsync(meu);

            Assert.Equal(new EnvioPendente(2, 2), await GitService.EnvioPendenteAsync(meu, status));
        }
        finally { Limpar(raiz); }
    }

    /// <summary>Feat/pix enviada com um commit de dois arquivos, apagada no remoto e podada pelo Obter.</summary>
    private static async Task ParApagadoNoRemoto(string raiz, string meu)
    {
        await Git(meu, "push", "-q");
        await Git(meu, "checkout", "-qb", "feat/pix");
        File.WriteAllText(Path.Combine(meu, "c.txt"), "c\n");
        File.WriteAllText(Path.Combine(meu, "d.txt"), "d\n");
        await Git(meu, "add", "-A");
        await Git(meu, "commit", "-qm", "pix");
        await Git(meu, "push", "-q", "-u", "origin", "feat/pix");
        await Git(Path.Combine(raiz, "remoto.git"), "branch", "-qD", "feat/pix");
        await Git(meu, "fetch", "-q", "--all", "--prune"); // o que o Obter faz
    }

    /// <summary>
    /// O pedido: branch com o par apagado no remoto deixava o Enviar desligado, dizendo "em
    /// dia". O git traz o par sem a contagem, e isso era lido como 0 à frente e 0 atrás.
    /// </summary>
    [Fact]
    public async Task Par_apagado_no_remoto_e_marcado_no_status()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        try
        {
            await ParApagadoNoRemoto(raiz, meu);

            var status = await GitService.StatusAsync(meu);

            Assert.Equal("origin/feat/pix", status.Upstream);
            Assert.True(status.ParAusente);
            Assert.True(status.TemOQueEnviar);
            Assert.Equal(new EnvioPendente(1, 2), await GitService.EnvioPendenteAsync(meu, status));
            Assert.Equal(
                "Enviar 1 commit com 2 arquivos e criar origin/feat/pix, que não existe no remoto (push)",
                new EnvioPendente(1, 2).Dica(status));
            Assert.Equal(
                "Enviar a branch feat/pix e criar origin/feat/pix, que não existe no remoto (push)",
                EnvioPendente.Nada.Dica(status));

            // com o par no lugar, a marca não aparece
            await Git(meu, "checkout", "-q", "main");
            var comPar = await GitService.StatusAsync(meu);
            Assert.False(comPar.ParAusente);
            Assert.False(comPar.TemOQueEnviar);
        }
        finally { Limpar(raiz); }
    }

    /// <summary>
    /// Clone de um remoto vazio: o par já vem configurado e não existe lá. Sem commit não há
    /// o que enviar; depois do primeiro, o Enviar ficava desligado do mesmo jeito.
    /// </summary>
    [Fact]
    public async Task Clone_de_remoto_vazio_envia_depois_do_primeiro_commit()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "grepos-vazio-" + Path.GetRandomFileName());
        var remoto = Path.Combine(raiz, "remoto.git");
        var meu = Path.Combine(raiz, "meu");
        Directory.CreateDirectory(raiz);
        try
        {
            await Git(raiz, "init", "-q", "--bare", "-b", "main", remoto);
            await Git(raiz, "clone", "-q", remoto, meu);
            await Git(meu, "config", "user.email", "t@t");
            await Git(meu, "config", "user.name", "Teste");

            var semCommit = await GitService.StatusAsync(meu);
            Assert.Equal("origin/main", semCommit.Upstream);
            Assert.False(semCommit.ParAusente);
            Assert.False(semCommit.TemOQueEnviar);

            File.WriteAllText(Path.Combine(meu, "a.txt"), "inicial\n");
            await Git(meu, "add", "a.txt");
            await Git(meu, "commit", "-qm", "inicial");

            var status = await GitService.StatusAsync(meu);
            Assert.True(status.ParAusente);
            Assert.True(status.TemOQueEnviar);
            Assert.Equal(new EnvioPendente(1, 1), await GitService.EnvioPendenteAsync(meu, status));
        }
        finally { Limpar(raiz); }
    }

    /// <summary>
    /// Na tela, com o par apagado: o Enviar liga, a dica diz que a branch será criada, e o
    /// envio a recria no remoto. Voltar ao remoto some: não há para onde voltar.
    /// </summary>
    [Fact]
    public async Task Enviar_recria_a_branch_apagada_no_remoto()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", Path.Combine(raiz, "home"));
        try
        {
            await ParApagadoNoRemoto(raiz, meu);

            var main = new MainViewModel(new FakeDialogs());
            main.AddRepository(meu, "Financeiro", null);
            main.RebuildTree();
            var id = main.Repos.Single().Id;
            main.SelecionarRepositorio(id);
            await main.RefreshRepoAsync(id);

            await Espera.Ate(() => main.EnviarBadge == "2");
            Assert.True(main.PodeEnviar);
            Assert.Equal(
                "Enviar 1 commit com 2 arquivos e criar origin/feat/pix, que não existe no remoto (push)",
                main.EnviarTooltip);
            await main.Changes!.ReloadAsync();
            Assert.False(main.Changes.TemRemoto);

            await main.PushCommand.ExecuteAsync(null);

            Assert.True(main.CurrentStatus?.TemPar);
            Assert.False(main.PodeEnviar);
            Assert.Equal("", main.EnviarBadge);
            Assert.Contains("feat/pix", await Git(Path.Combine(raiz, "remoto.git"), "branch", "--list", "feat/pix"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    /// <summary>
    /// Na tela: o selo com os arquivos, a dica com os commits. A recarga com o HEAD no mesmo
    /// lugar (é o que acontece a cada arquivo salvo) não chama o git de novo para contar.
    /// </summary>
    [Fact]
    public async Task Selo_do_enviar_mostra_os_arquivos()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", Path.Combine(raiz, "home"));
        try
        {
            // o commit a enviar passa a ter três arquivos
            File.WriteAllText(Path.Combine(meu, "c.txt"), "c\n");
            File.WriteAllText(Path.Combine(meu, "d.txt"), "d\n");
            await Git(meu, "add", "-A");
            await Git(meu, "commit", "-q", "--amend", "--no-edit");

            var main = await AbrirNaAbaAlteracoes(meu);
            await Espera.Ate(() => main.EnviarBadge == "3");
            Assert.Equal("Enviar 1 commit com 3 arquivos (push)", main.EnviarTooltip);

            var contagem = main.ContagemDosSelos;
            File.WriteAllText(Path.Combine(meu, "solto.txt"), "sem commit\n");
            await main.RefreshRepoAsync(main.Repos.Single().Id);
            Assert.Same(contagem, main.ContagemDosSelos);
            Assert.Equal("3", main.EnviarBadge);

            await main.PushCommand.ExecuteAsync(null);

            Assert.Equal("", main.EnviarBadge);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    /// <summary>Branch nova com commits: o selo aparece, e a dica diz que ela será criada no remoto.</summary>
    [Fact]
    public async Task Selo_do_enviar_na_branch_sem_par()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", Path.Combine(raiz, "home"));
        try
        {
            await Git(meu, "push", "-q");
            await Git(meu, "checkout", "-qb", "feat/pix");
            File.WriteAllText(Path.Combine(meu, "c.txt"), "c\n");
            File.WriteAllText(Path.Combine(meu, "d.txt"), "d\n");
            await Git(meu, "add", "-A");
            await Git(meu, "commit", "-qm", "pix");

            var main = new MainViewModel(new FakeDialogs());
            main.AddRepository(meu, "Financeiro", null);
            main.RebuildTree();
            var id = main.Repos.Single().Id;
            main.SelecionarRepositorio(id);
            await main.RefreshRepoAsync(id);

            // a contagem espera o remoto ser lido
            await Espera.Ate(() => main.EnviarBadge == "2");
            Assert.Equal(
                "Enviar a branch feat/pix, que ainda não existe no remoto: 1 commit com 2 arquivos (push)",
                main.EnviarTooltip);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    [Theory]
    [InlineData("ref: refs/heads/main\n", "main")]
    [InlineData("ref: refs/heads/feat/cartao-pix\r\n", "feat/cartao-pix")]
    [InlineData("4f2c9a1e0b7d3c5a8e6f1b2d4c7a9e0f3b5d8c1a\n", null)]  // HEAD solto
    [InlineData("ref: refs/heads/.invalid\n", null)]                  // reftable
    [InlineData("ref: refs/remotes/origin/main\n", null)]
    [InlineData("", null)]
    public void Branch_do_conteudo_do_head(string conteudo, string? esperado) =>
        Assert.Equal(esperado, GitService.BranchDoConteudoDoHead(conteudo));

    /// <summary>
    /// Repositórios de verdade: branch com barra, HEAD solto, worktree (o .git é um arquivo
    /// apontando para a pasta dela) e pasta que não é repositório.
    /// </summary>
    [Fact]
    public async Task Branch_do_head_sem_chamar_o_git()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        try
        {
            Assert.Equal("main", GitService.BranchDoHead(meu));

            await Git(meu, "checkout", "-qb", "feat/pix");
            Assert.Equal("feat/pix", GitService.BranchDoHead(meu));

            var outra = Path.Combine(raiz, "outra");
            await Git(meu, "worktree", "add", "-q", "-b", "imp/boleto", outra);
            Assert.Equal("imp/boleto", GitService.BranchDoHead(outra));

            await Git(meu, "checkout", "-q", "--detach");
            Assert.Null(GitService.BranchDoHead(meu));

            Assert.Null(GitService.BranchDoHead(raiz));                          // não é repositório
            Assert.Null(GitService.BranchDoHead(Path.Combine(raiz, "nao-existe")));
        }
        finally { Limpar(raiz); }
    }

    /// <summary>
    /// O pedido: a branch na árvore aparecia só quando a varredura trazia o status, segundos
    /// depois do nome. Agora vem do HEAD ao montar a árvore e o painel. Com o status
    /// conhecido, a árvore nem lê o arquivo: vale o status.
    /// </summary>
    [Fact]
    public async Task Arvore_e_painel_mostram_a_branch_antes_do_status()
    {
        var (raiz, meu) = await ComCommitAEnviar();
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", Path.Combine(raiz, "home"));
        try
        {
            await Git(meu, "checkout", "-qb", "feat/pix");

            // o nó que o AddRepository monta nasce antes de qualquer status: o refresh que
            // ele dispara só começa depois da árvore
            var main = new MainViewModel(new FakeDialogs());
            main.AddRepository(meu, "Financeiro", null);

            var no = main.Tree.OfType<RepoNode>().Single();
            Assert.Equal("feat/pix", no.BranchLida);
            Assert.True(no.TemBranch);
            Assert.Equal("feat/pix", no.BranchNome);

            main.MostrarPainel(null);
            Assert.Equal("feat/pix", main.Painel!.Cartoes.Single().Branch);

            await main.RefreshAllAsync();
            main.RebuildTree();
            var depois = main.Tree.OfType<RepoNode>().Single();
            Assert.NotNull(depois.Status);
            Assert.Null(depois.BranchLida);
            Assert.Equal("feat/pix", depois.BranchNome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            Limpar(raiz);
        }
    }

    /// <summary>No cartão, como na árvore: a lida até o status chegar; com erro, o traço.</summary>
    [Fact]
    public void Cartao_mostra_a_branch_lida_ate_o_status_chegar()
    {
        var repo = new Repo { Id = "r1", Name = "Financeiro" };
        var cartao = new CartaoRepoViewModel(repo, null, "#4F8CFF") { BranchLida = "feat/pix" };
        Assert.Equal("feat/pix", cartao.Branch);

        cartao.Status = new RepoStatus { Branch = "develop" };
        Assert.Equal("develop", cartao.Branch);

        cartao.Status = new RepoStatus { Error = "pasta não encontrada" };
        Assert.Equal("—", cartao.Branch);

        Assert.Equal("—", new CartaoRepoViewModel(repo, null, "#4F8CFF").Branch);
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
