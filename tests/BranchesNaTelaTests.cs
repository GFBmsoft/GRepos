using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// A janela de Branches com o mouse de verdade: clicar na linha troca de branch, excluir só
/// depois de confirmar, e os menus no padrão das pílulas (o glifo na cor da ação).
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class BranchesNaTelaTests
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

    private static async Task<string> Repositorio()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-brt-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await Git(dir, "init", "-q", "-b", "main");
        await Git(dir, "config", "user.email", "t@t");
        await Git(dir, "config", "user.name", "Teste");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
        await Git(dir, "add", ".");
        await Git(dir, "commit", "-qm", "primeiro");
        await Git(dir, "branch", "feat/estoque");
        await Git(dir, "branch", "fix/boleto");
        return dir;
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

    private static string Cabeca(string dir) => File.ReadAllText(Path.Combine(dir, ".git", "HEAD")).Trim();

    private static async Task<(BranchesWindow Janela, BranchesViewModel Vm)> Abrir(string dir)
    {
        var janela = new BranchesWindow(new MainViewModel(new FakeDialogs()), new Repo { Id = "r1", Name = "Demo", Path = dir })
        {
            Width = 780,
            Height = 640,
        };
        janela.Show();
        var vm = (BranchesViewModel)janela.DataContext!;
        await Espera.Ate(() => vm.TotalLocais == 3 && !vm.Busy);

        // pasta nova chega recolhida: abertas, as linhas existem de verdade na tela
        foreach (var grupo in vm.GruposLocais) grupo.Recolhido = false;
        Montar(janela);
        return (janela, vm);
    }

    private static void Montar(Window janela)
    {
        janela.Measure(new Size(780, 640));
        janela.Arrange(new Rect(0, 0, 780, 640));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock Linha(Window janela, string nome) =>
        janela.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == nome);

    private static Point Centro(Window janela, string nome)
    {
        var t = Linha(janela, nome);
        return t.TranslatePoint(new Point(t.Bounds.Width / 2, t.Bounds.Height / 2), janela)!.Value;
    }

    [AvaloniaFact]
    public async Task Clicar_na_linha_troca_de_branch_e_fecha_a_janela()
    {
        var dir = await Repositorio();
        try
        {
            var (janela, _) = await Abrir(dir);
            var ponto = Centro(janela, "estoque");

            janela.MouseDown(ponto, MouseButton.Left);
            janela.MouseUp(ponto, MouseButton.Left);

            await Espera.Ate(() => Cabeca(dir).EndsWith("feat/estoque"));
            await Espera.Ate(() => !janela.IsVisible);
        }
        finally { Limpar(dir); }
    }

    [AvaloniaFact]
    public async Task Arrastar_clicar_com_o_direito_ou_clicar_na_atual_nao_troca()
    {
        var dir = await Repositorio();
        try
        {
            var (janela, vm) = await Abrir(dir);

            // apertou numa linha e soltou em outra: é o gesto de mesclar, não um clique
            janela.MouseDown(Centro(janela, "estoque"), MouseButton.Left);
            janela.MouseUp(Centro(janela, "boleto"), MouseButton.Left);

            janela.MouseDown(Centro(janela, "boleto"), MouseButton.Right);
            janela.MouseUp(Centro(janela, "boleto"), MouseButton.Right);

            janela.MouseDown(Centro(janela, "main"), MouseButton.Left);
            janela.MouseUp(Centro(janela, "main"), MouseButton.Left);

            await Task.Delay(300);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.EndsWith("/main", Cabeca(dir));
            Assert.True(janela.IsVisible);
            Assert.False(vm.TemErro);
            janela.Close();
        }
        finally { Limpar(dir); }
    }

    /// <summary>As ações só aparecem na linha sob o mouse, e a atual não tem nenhuma.</summary>
    [AvaloniaFact]
    public async Task Acoes_aparecem_na_linha_sob_o_mouse()
    {
        var dir = await Repositorio();
        try
        {
            var (janela, _) = await Abrir(dir);

            StackPanel Acoes(string nome) => Linha(janela, nome).GetVisualAncestors().OfType<Grid>().First()
                .Children.OfType<StackPanel>().First(s => s.Classes.Contains("acoesDaLinha"));
            bool Visivel(string nome, bool excluir) => Acoes(nome).Children.OfType<Button>()
                .ElementAt(excluir ? 1 : 0).IsEffectivelyVisible;

            Assert.False(Acoes("estoque").IsVisible);

            janela.MouseMove(Centro(janela, "estoque"));
            Montar(janela);
            Assert.True(Visivel("estoque", excluir: false));
            Assert.True(Visivel("estoque", excluir: true));
            Assert.False(Acoes("boleto").IsVisible);

            janela.MouseMove(Centro(janela, "main"));
            Montar(janela);
            Assert.False(Visivel("main", excluir: false));
            Assert.False(Visivel("main", excluir: true));
            janela.Close();
        }
        finally { Limpar(dir); }
    }

    [AvaloniaFact]
    public async Task Menu_da_branch_troca_exclui_e_a_remota_nao_tem_excluir()
    {
        var dir = await Repositorio();
        try
        {
            var (janela, vm) = await Abrir(dir);

            IEnumerable<string> Textos(BranchItemViewModel item) => janela.MenuDaBranch(vm, item).Items
                .OfType<MenuItem>().Select(i => (string)i.Header!);

            var local = vm.Locais.First(i => i.Name == "feat/estoque");
            Assert.Contains("Trocar para feat/estoque", Textos(local));
            Assert.Contains("Excluir feat/estoque…", Textos(local));

            var remota = new BranchItemViewModel { Branch = new Branch { Name = "origin/feat/estoque", IsRemote = true } };
            Assert.Contains("Trocar para origin/feat/estoque", Textos(remota));
            Assert.DoesNotContain(Textos(remota), t => t.StartsWith("Excluir"));
            janela.Close();
        }
        finally { Limpar(dir); }
    }

    /// <summary>Para ver com os próprios olhos: a janela com a linha sob o mouse e o menu aberto.</summary>
    [AvaloniaFact]
    public async Task Capturas_da_janela_e_do_menu()
    {
        if (Environment.GetEnvironmentVariable("GREPOS_SHOTS") is not { } pasta) return;

        var dir = await Repositorio();
        try
        {
            var (janela, vm) = await Abrir(dir);
            Directory.CreateDirectory(pasta);

            janela.MouseMove(Centro(janela, "estoque"));
            Montar(janela);
            using (var foto = janela.CaptureRenderedFrame()) foto?.Save(Path.Combine(pasta, "branches.png"));

            var menu = janela.MenuDaBranch(vm, vm.Locais.First(i => i.Name == "feat/estoque"));
            menu.ShowAt(Linha(janela, "estoque"));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var popup = TopLevel.GetTopLevel(menu.Items.OfType<MenuItem>().First())!;
            using (var foto = popup.CaptureRenderedFrame()) foto?.Save(Path.Combine(pasta, "menu-branch.png"));

            menu.Hide();
            janela.Close();
        }
        finally { Limpar(dir); }
    }

    private static readonly string[] Cores = { "azul", "verde", "roxo", "laranja", "amarelo", "vermelho", "cinza" };

    private static IEnumerable<MenuItem> Itens(IEnumerable<object?> itens)
    {
        foreach (var item in itens.OfType<MenuItem>())
        {
            yield return item;
            foreach (var filho in Itens(item.Items)) yield return filho;
        }
    }

    /// <summary>
    /// Trava o padrão: item de menu novo sem ícone volta a destoar dos outros. Quem tem marca
    /// de ligado (ToggleType) usa a coluna do ícone para ela.
    /// </summary>
    [AvaloniaFact]
    public void Todo_item_de_menu_tem_o_glifo_na_cor_da_acao()
    {
        var janela = new BranchesWindow();
        var telas = new Control[] { new ChangesView(), new HistoryView(), new DiffView(), janela };
        var problemas = new List<string>();

        foreach (var tela in telas)
        {
            var menus = tela.GetLogicalDescendants().OfType<Control>().Select(c => c.ContextMenu?.Items.Cast<object?>())
                .Concat(tela.GetLogicalDescendants().OfType<Button>().Select(b => (b.Flyout as MenuFlyout)?.Items.Cast<object?>()))
                .Where(m => m is not null);

            foreach (var item in menus.SelectMany(m => Itens(m!)).Where(i => i.ToggleType == MenuItemToggleType.None))
                if (item.Icon is not TextBlock t || !t.Classes.Contains("glifo") || !Cores.Any(t.Classes.Contains))
                    problemas.Add($"{tela.GetType().Name}: {item.Header}");
        }

        Assert.True(problemas.Count == 0, string.Join("\n", problemas));
    }

    /// <summary>
    /// Sem o recurso GlyphFont nada dá erro: o Windows acha a maioria dos glifos em outra fonte
    /// e só alguns viram quadradinho, o que passa batido num teste que só monta a tela.
    /// </summary>
    [AvaloniaFact]
    public async Task Glifos_usam_a_fonte_de_icones()
    {
        var dir = await Repositorio();
        try
        {
            var (janela, _) = await Abrir(dir);

            var glifos = janela.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("glifo")).ToList();
            Assert.NotEmpty(glifos);
            Assert.All(glifos, t => Assert.Contains("Segoe Fluent Icons", t.FontFamily.ToString()));
            janela.Close();
        }
        finally { Limpar(dir); }
    }

    /// <summary>
    /// O menu é descendente lógico do botão que o abre: um estilo de conteúdo de botão escrito
    /// sem ">" pega o menu também, e os itens saem lado a lado, vazando da janela. Foi o EV03
    /// do GRemote, que tem este mesmo teste.
    /// </summary>
    [AvaloniaFact]
    public async Task Menus_dos_botoes_ficam_em_coluna()
    {
        var dir = await Repositorio();
        try
        {
            var (janela, _) = await Abrir(dir);
            var botao = janela.GetVisualDescendants().OfType<Button>().Single(b => b.Flyout is MenuFlyout);
            var menu = (MenuFlyout)botao.Flyout!;

            menu.ShowAt(botao);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var itens = menu.Items.OfType<MenuItem>().ToList();
            var segundo = itens[1].TranslatePoint(default, itens[0]);
            Assert.NotNull(segundo);
            Assert.True(segundo!.Value.Y > 0 && Math.Abs(segundo.Value.X) < 1,
                $"itens do menu lado a lado: o segundo está em {segundo}");

            menu.Hide();
            janela.Close();
        }
        finally { Limpar(dir); }
    }
}
