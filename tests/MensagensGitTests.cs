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
/// Os avisos de obter, puxar e enviar: em português, dizendo o que aconteceu a partir do
/// repositório antes e depois, e com a cor certa para cada resultado.
/// </summary>
public class MensagensGitTests
{
    private static RepoStatus S(string branch = "develop", string? upstream = "origin/develop",
        int ahead = 0, int behind = 0, string head = "aaa") =>
        new() { Branch = branch, Upstream = upstream, Ahead = ahead, Behind = behind, Head = head };

    [Fact]
    public void Obter_diz_o_que_ficou_para_puxar()
    {
        Assert.Equal(new Aviso("Obter concluído", "Há 3 commits no remoto para puxar em develop."), MensagensGit.Obtido(S(behind: 3)));
        Assert.Equal(new Aviso("Obter concluído", "Há 1 commit no remoto para puxar em develop."), MensagensGit.Obtido(S(behind: 1)));
        Assert.Equal(new Aviso("Obter concluído", "Nada novo no remoto para develop."), MensagensGit.Obtido(S()));
        Assert.Contains("ainda não tem par no remoto", MensagensGit.Obtido(S("imp/x", upstream: null)).Detalhe);
    }

    [Fact]
    public void Puxar_conta_os_commits_que_chegaram()
    {
        var chegaram = MensagensGit.Puxado(S(behind: 4, head: "aaa"), S(head: "bbb"));
        Assert.Equal(new Aviso("Puxar concluído", "4 commits novos em develop."), chegaram);

        Assert.Equal("1 commit novo em develop.", MensagensGit.Puxado(S(behind: 1, head: "aaa"), S(head: "bbb")).Detalhe);

        // o HEAD não saiu do lugar: não havia nada para trazer
        Assert.Equal("develop já estava em dia com o remoto.", MensagensGit.Puxado(S(), S()).Detalhe);

        // mudou, mas o status de antes não sabia quantos faltavam (não tinha obtido ainda)
        Assert.Equal("develop atualizada com o remoto.", MensagensGit.Puxado(S(head: "aaa"), S(head: "bbb")).Detalhe);
    }

    [Fact]
    public void Enviar_conta_os_commits_enviados_e_avisa_a_branch_criada()
    {
        Assert.Equal(new Aviso("Enviar concluído", "2 commits enviados de develop."), MensagensGit.Enviado(S(ahead: 2), S()));
        Assert.Equal("1 commit enviado de develop.", MensagensGit.Enviado(S(ahead: 1), S()).Detalhe);
        Assert.Equal("Nada a enviar: o remoto já estava em dia.", MensagensGit.Enviado(S(), S()).Detalhe);
        Assert.Equal("Branch imp/x criada no remoto.",
            MensagensGit.Enviado(S("imp/x", upstream: null, ahead: 1), S("imp/x", "origin/imp/x")).Detalhe);
    }

    [Theory]
    [InlineData("Already up to date.", "Já estava em dia.")]
    [InlineData("Everything up-to-date", "Nada a enviar: o remoto já estava em dia.")]
    [InlineData("Switched to branch 'develop'", "Agora em develop.")]
    [InlineData("Switched to a new branch 'imp/x'", "Agora em imp/x.")]
    [InlineData("Updating a1b2..c3d4\nFast-forward\n a.pas | 2 +-\n 3 files changed, 10 insertions(+), 4 deletions(-)", "3 arquivos alterados, +10 −4.")]
    [InlineData(" 1 file changed, 1 insertion(+)", "1 arquivo alterado, +1 −0.")]
    [InlineData("HEAD is now at a1b2c3d Ajuste do boleto", "Agora em a1b2c3d: Ajuste do boleto")]
    [InlineData("Your branch is behind 'origin/develop' by 2 commits, and can be fast-forwarded.", "2 commits atrás de origin/develop.")]
    [InlineData("branch 'imp/x' set up to track 'origin/imp/x'.", "Branch imp/x vinculada a origin/imp/x.")]
    [InlineData("frase que o app não conhece", "frase que o app não conhece")]
    [InlineData("", "")]
    public void Frases_de_sempre_do_git_saem_em_portugues(string saida, string esperado)
    {
        Assert.Equal(esperado, MensagensGit.Traduzir(saida));
    }

    [Theory]
    [InlineData("enviar", " ! [rejected]        develop -> develop (non-fast-forward)\nerror: failed to push some refs", "Use Puxar e envie de novo")]
    [InlineData("puxar", "fatal: unable to access 'https://github.com/x/y.git/': Could not resolve host: github.com", "Sem conexão com o servidor")]
    [InlineData("enviar", "remote: Write access to repository not granted.\nfatal: unable to access 'https://github.com/x/y/': The requested URL returned error: 403", "não tem permissão de escrita")]
    [InlineData("puxar", "error: Your local changes to the following files would be overwritten by merge:\n\ta.pas", "Faça o commit ou guarde-as")]
    [InlineData("puxar", "CONFLICT (content): Merge conflict in a.pas\nAutomatic merge failed; fix conflicts and then commit the result.", "Parou em conflito")]
    [InlineData("puxar", "fatal: Need to specify how to reconcile divergent branches.", "commits dos dois lados")]
    [InlineData("obter", "fatal: Authentication failed for 'https://github.com/x/y.git/'", "não aceitou a credencial")]
    public void Erro_conhecido_do_git_vira_explicacao(string operacao, string mensagem, string esperado)
    {
        var aviso = MensagensGit.Erro(operacao, mensagem);

        Assert.Equal("Não foi possível " + operacao, aviso.Titulo);
        Assert.Contains(esperado, aviso.Detalhe);
    }

    [Fact]
    public void Erro_desconhecido_mostra_a_linha_do_git_e_texto_do_app_passa_como_esta()
    {
        var desconhecido = MensagensGit.Erro("puxar", "hint: algo\nfatal: bad object refs/heads/x");
        Assert.Equal("Bad object refs/heads/x", desconhecido.Detalhe);

        // mensagem que o próprio app escreveu, já em português
        var proprio = MensagensGit.Erro("puxar", "A branch \"x\" só existe no seu computador, então não há o que puxar.");
        Assert.StartsWith("A branch \"x\" só existe", proprio.Detalhe);
        Assert.False(MensagensGit.PareceDoGit(proprio.Detalhe));
    }
}

[Collection(WorkspaceGlobal.Nome)]
public class AvisosTests
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

    [Fact]
    public void Cada_tipo_de_aviso_tem_a_sua_cor_e_o_seu_icone()
    {
        var main = new MainViewModel(new FakeDialogs());

        main.Avisar("sucesso", "Puxar concluído", "2 commits novos em develop.");
        Assert.Equal(("Green", "✓"), (main.StatusAccent, main.StatusIcone));
        Assert.True(main.TemStatusTitulo);
        Assert.True(main.HasStatusMessage);
        Assert.False(main.StatusIsError);

        main.Avisar("andamento", "Enviar em andamento…", "Financeiro");
        Assert.Equal(("Yellow", "●"), (main.StatusAccent, main.StatusIcone));

        main.Avisar("erro", "Não foi possível enviar", "Sem conexão.");
        Assert.Equal(("Red", "✕"), (main.StatusAccent, main.StatusIcone));
        Assert.True(main.StatusIsError);
    }

    [Fact]
    public void Sem_repositorio_selecionado_o_aviso_fica_no_cartao_de_baixo()
    {
        var main = new MainViewModel(new FakeDialogs()) { VaoDaBarra = 600 };

        main.Avisar("sucesso", "Clone concluído", "Financeiro");
        Assert.False(main.AvisoNaBarra);
        Assert.True(main.AvisoNoRodape);
        Assert.Equal("Clone concluído\nFinanceiro", main.StatusCompleto);

        main.DismissStatusCommand.Execute(null);
        Assert.False(main.AvisoNoRodape);
    }

    [Fact]
    public void Aviso_simples_continua_valendo_e_erro_cru_do_git_ganha_explicacao()
    {
        var main = new MainViewModel(new FakeDialogs());

        main.Notify("Branch criada.");
        Assert.Equal(("Accent", "i"), (main.StatusAccent, main.StatusIcone));
        Assert.False(main.TemStatusTitulo);
        Assert.Equal("Branch criada.", main.StatusMessage);

        // erro que o app escreveu passa como está, sem título inventado
        main.Notify("Não foi possível salvar o workspace: disco cheio", true);
        Assert.Equal("Red", main.StatusAccent);
        Assert.False(main.TemStatusTitulo);

        // erro cru do git: explicação em português
        main.Notify("fatal: unable to access 'https://github.com/x/y/': Could not resolve host: github.com", true);
        Assert.Equal("Não foi possível concluir a operação", main.StatusTitulo);
        Assert.Contains("Sem conexão com o servidor", main.StatusMessage);
    }
}
