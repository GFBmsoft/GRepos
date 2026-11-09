using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// A mensagem de commit que o GConv deixa para um repositório (a conversão DBISAM → MySQL): entra no campo com ele vazio e
/// alteração pendente, só para o HEAD e a branch gravados, nunca em cima do que foi digitado, e apagada não volta.
/// Num repositório real; a pasta do GConv é a dos testes (IsolamentoDoWorkspace).
/// </summary>
public class MensagemDoGConvTests
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

    private const string Mensagem =
        "- Migração DBISAM para MySQL: devolução com a nota referenciada\n\nPedidos: 6553\nCommits da Origem: 21c85e6\n\n" +
        "Mensagens dos commits da Origem:\n- 21c85e6 (Wiliam, 27/10/2026) Notas - Pedido #6553 --mantis * Devolução";

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    private static async Task<string> NovoRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-gconv-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "feat/SkillMigraDbisam");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        File.WriteAllText(Path.Combine(dir, "UFrm.pas"), "inicial\n");
        await Git(dir, "add", ".");
        await Git(dir, "commit", "-qm", "inicial");
        return dir;
    }

    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (Exception) { /* pasta temporária */ }
    }

    /// <summary>O arquivo como o GConv grava (MensagemParaOGRepos): o caminho do repositório, a branch, o HEAD e o texto.</summary>
    private static async Task<string> Gravar(string repo, string mensagem, string? head = null, string? branch = null, string? caminho = null)
    {
        Directory.CreateDirectory(MensagemDoGConv.Pasta);
        // um arquivo por repositório, como o GConv: a gravação nova entra no lugar da anterior
        var arq = Path.Combine(MensagemDoGConv.Pasta, $"{Path.GetFileName(repo)}.json");
        var o = new
        {
            Versao = 1, Repositorio = caminho ?? repo,
            Branch = branch ?? (await Git(repo, "branch", "--show-current")).Trim(),
            Head = head ?? (await Git(repo, "rev-parse", "HEAD")).Trim(),
            Modulo = "Notas", Quando = DateTime.Now, Mensagem = mensagem,
            Descricao = "conversão do Notas em 09/11 10:24 + 1 correção",
        };
        File.WriteAllText(arq, JsonSerializer.Serialize(o));
        return arq;
    }

    private static ChangesViewModel Changes(string dir) =>
        new(new Repo { Id = "r-" + Path.GetFileName(dir), Name = "Notas", Path = dir }, new MainViewModel(new FakeDialogs()), split: false);

    [Fact]
    public async Task Achar_casa_o_caminho_o_head_e_a_branch()
    {
        var dir = await NovoRepo();
        try
        {
            var head = (await Git(dir, "rev-parse", "HEAD")).Trim();
            // o caminho escrito de outro jeito (maiúsculas, barra no fim) é o mesmo repositório
            await Gravar(dir, Mensagem, caminho: dir.ToUpperInvariant() + "\\");
            File.WriteAllText(Path.Combine(MensagemDoGConv.Pasta, "quebrado.json"), "{ isto não é json");

            var s = MensagemDoGConv.Achar(dir, head, "feat/SkillMigraDbisam");
            Assert.NotNull(s);
            Assert.Equal(Mensagem, s!.Mensagem);
            Assert.Equal("conversão do Notas em 09/11 10:24 + 1 correção", s.Descricao);

            Assert.Null(MensagemDoGConv.Achar(dir, "0000000000000000000000000000000000000000", "feat/SkillMigraDbisam"));
            Assert.Null(MensagemDoGConv.Achar(dir, head, "develop"));
            Assert.Null(MensagemDoGConv.Achar(dir + "-outro", head, "feat/SkillMigraDbisam"));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Entra_no_campo_vazio_com_alteracao_e_apagada_nao_volta()
    {
        var dir = await NovoRepo();
        try
        {
            await Gravar(dir, Mensagem);
            var vm = Changes(dir);

            // sem alteração pendente não há o que commitar: o campo fica vazio
            await vm.ReloadAsync();
            Assert.Equal("", vm.CommitMessage);
            Assert.False(vm.TemSugestao);

            File.WriteAllText(Path.Combine(dir, "UFrm.pas"), "convertido\n");
            await vm.ReloadAsync();
            Assert.Equal(Mensagem, vm.CommitMessage);
            Assert.True(vm.TemSugestao);
            Assert.Equal("Mensagem do GConv: conversão do Notas em 09/11 10:24 + 1 correção", vm.SugestaoTexto);

            // apagar dispensa: a recarga seguinte (salvar um arquivo) não põe de volta
            vm.DispensarSugestaoCommand.Execute(null);
            Assert.Equal("", vm.CommitMessage);
            Assert.False(vm.TemSugestao);
            await vm.ReloadAsync(silent: true);
            Assert.Equal("", vm.CommitMessage);

            // outra aba do mesmo repositório (trocou de repositório e voltou) também não
            var outra = Changes(dir);
            await outra.ReloadAsync();
            Assert.Equal("", outra.CommitMessage);

            // uma gravação nova do GConv (outra correção) volta
            await Gravar(dir, Mensagem + "\n\n- Correção: Type mismatch no QDav");
            await outra.ReloadAsync();
            Assert.EndsWith("Type mismatch no QDav", outra.CommitMessage);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Nunca_pisa_no_que_foi_digitado_nem_no_emendar()
    {
        var dir = await NovoRepo();
        try
        {
            await Gravar(dir, Mensagem);
            File.WriteAllText(Path.Combine(dir, "UFrm.pas"), "convertido\n");
            var vm = Changes(dir);
            vm.CommitMessage = "minha mensagem";
            await vm.ReloadAsync();
            Assert.Equal("minha mensagem", vm.CommitMessage);
            Assert.False(vm.TemSugestao);

            var emendar = Changes(dir);
            emendar.Amend = true;
            for (var i = 0; i < 50 && emendar.CommitMessage == ""; i++) await Task.Delay(20);
            await emendar.ReloadAsync();
            Assert.Equal("inicial", emendar.CommitMessage);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Commit_por_fora_tira_a_mensagem_velha_do_campo()
    {
        var dir = await NovoRepo();
        try
        {
            await Gravar(dir, Mensagem);
            File.WriteAllText(Path.Combine(dir, "UFrm.pas"), "convertido\n");
            var vm = Changes(dir);
            await vm.ReloadAsync();
            Assert.Equal(Mensagem, vm.CommitMessage);

            // commitou no terminal com outra mensagem e seguiu mexendo: a do GConv descrevia o que já foi
            await Git(dir, "commit", "-qam", "commit do terminal");
            File.WriteAllText(Path.Combine(dir, "UFrm.pas"), "outra coisa\n");
            await vm.ReloadAsync();
            Assert.Equal("", vm.CommitMessage);
            Assert.False(vm.TemSugestao);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Commit_pelo_GRepos_usa_a_mensagem_e_ela_nao_volta()
    {
        var dir = await NovoRepo();
        try
        {
            await Gravar(dir, Mensagem);
            File.WriteAllText(Path.Combine(dir, "UFrm.pas"), "convertido\n");
            var vm = Changes(dir);
            await vm.ReloadAsync();
            await GitService.StageAsync(dir, new[] { "UFrm.pas" });
            await vm.ReloadAsync();
            await vm.CommitCommand.ExecuteAsync(null);

            Assert.Equal(Mensagem, (await Git(dir, "log", "-1", "--format=%B")).Trim().Replace("\r\n", "\n"));
            Assert.Equal("", vm.CommitMessage);
            File.WriteAllText(Path.Combine(dir, "UFrm.pas"), "depois\n");
            await vm.ReloadAsync();
            Assert.Equal("", vm.CommitMessage);
        }
        finally { Limpar(dir); }
    }

    /// <summary>A linha de onde a mensagem veio, com o X, em cima do campo; a mensagem longa rola dentro do campo.</summary>
    [AvaloniaFact]
    public void Linha_do_GConv_em_cima_do_campo()
    {
        var vm = new ChangesViewModel(new Repo { Id = "d", Name = "Notas", Path = Path.GetTempPath() }, new MainViewModel(new FakeDialogs()), split: true);
        vm.Unstaged.Add(new FileItemViewModel { Change = new FileChange { Path = "UFrm.pas", Index = ".", Worktree = "M", Kind = ChangeKind.Tracked } });
        var longa = Mensagem + string.Concat(Enumerable.Range(1, 60).Select(i => $"\n- c0ffe{i:00} (Johann, 27/10/2026) Pedido 65{i:00}: ajuste"));
        vm.MostrarSugestao(new MensagemDoGConv.Sugestao(longa, "conversão do Notas em 09/11 10:24 + 1 correção", "x.json", "abc", DateTime.Now));

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var linha = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.StartsWith("Mensagem do GConv") == true);
        Assert.True(linha.IsEffectivelyVisible);
        var campo = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Mensagem");
        Assert.True(campo.Bounds.Height <= 240, $"o campo cresceu para {campo.Bounds.Height}");
        Assert.True(linha.TranslatePoint(default, view)!.Value.Y < campo.TranslatePoint(default, view)!.Value.Y);

        if (Environment.GetEnvironmentVariable("GREPOS_SHOTS") is { } pasta)
        {
            Directory.CreateDirectory(pasta);
            using var foto = window.CaptureRenderedFrame();
            foto?.Save(Path.Combine(pasta, "commit-mensagem-do-gconv.png"));
        }

        var fechar = view.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b) as string == "Tirar a mensagem do GConv do campo");
        fechar.Command!.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("", vm.CommitMessage);
        Assert.False(linha.IsEffectivelyVisible);
        window.Close();
    }
}
