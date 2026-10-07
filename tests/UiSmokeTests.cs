using System.Linq;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using GRepos.Models;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GRepos.Tests.TestAppBuilder))]

namespace GRepos.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Carrega cada tela de verdade: erro de XAML ou de binding só aparece quando o
/// template é construído, e é exatamente o que quebra em produção.
/// </summary>
public class UiSmokeTests
{
    private static Repo DemoRepo() => new()
    {
        Id = "r1",
        Name = "Financeiro",
        Path = System.IO.Directory.GetCurrentDirectory(),
        PairKey = "Financeiro",
        Role = "origem",
    };

    private sealed class FakeDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string title, string label, string initial = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel main) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel main, Repo repo) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel main) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel main, Repo repo) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel main, Repo repo) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Preferencias_monta_com_o_campo_do_git_bash()
    {
        var main = new MainViewModel(new FakeDialogs());
        var janela = new SettingsWindow(main);
        janela.Show();

        // o campo mostra o que o botão Terminal vai abrir, ou o aviso de que não achou
        var textos = janela.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
        Assert.Contains(textos, t => t.StartsWith("Abre: ") || t.Contains("Git Bash não encontrado"));
        janela.Close();
    }

    /// <summary>Uma seção por vez: escolher na lista troca o painel, e as outras ficam escondidas.</summary>
    [AvaloniaFact]
    public void Preferencias_mostra_so_a_secao_escolhida()
    {
        var janela = new SettingsWindow(new MainViewModel(new FakeDialogs()));
        janela.Show();

        var lista = janela.GetVisualDescendants().OfType<ListBox>().First();
        var secoes = lista.Items.Cast<string>().ToList();
        Assert.Equal(new[] { "Aparência", "Geral", "Obter e puxar", "Terminal", "Diff externo", "Contas", "Aplicativo", "Notas da versão" }, secoes);

        bool Visivel(string rotulo) => janela.GetVisualDescendants().OfType<TextBlock>()
            .Any(t => t.Text == rotulo && t.IsEffectivelyVisible);

        Assert.True(Visivel("Tema"));
        Assert.False(Visivel("Token de acesso pessoal"));

        lista.SelectedIndex = secoes.IndexOf("Contas");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(Visivel("Token de acesso pessoal"));
        Assert.True(Visivel("CONTAS"));
        Assert.False(Visivel("Tema"));
        janela.Close();
    }

    private static void Render(Control view, object dataContext)
    {
        var window = new Window { Width = 1200, Height = 800, Content = view };
        view.DataContext = dataContext;
        window.Show();
        window.Measure(new Size(1200, 800));
        window.Arrange(new Rect(0, 0, 1200, 800));
        window.Close();
    }

    /// <summary>Os dois modos da tela de adicionar, com grupos na lista: o nome segue o link.</summary>
    [AvaloniaFact]
    public void Adicionar_repositorio_monta_nos_dois_modos()
    {
        var main = new MainViewModel(new FakeDialogs());
        main.CreateGroup("Financeiro");
        var janela = new AddRepoWindow(main, new FakeDialogs(), clonar: true);
        janela.Show();

        var link = janela.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.Watermark?.ToString()?.StartsWith("https://") == true);
        link.Text = "https://github.com/bmsoftsistemas-mysql/bmOS.git";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(janela.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "bmOS");
        Assert.Contains(janela.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Clonar");

        janela.GetVisualDescendants().OfType<RadioButton>().First().IsChecked = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(janela.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Adicionar");
        janela.Close();
    }

    [AvaloniaFact]
    public void ChangesView_monta_com_arquivos_e_diff()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new ChangesViewModel(DemoRepo(), main, split: true);

        // as listas precisam ter itens: erro dentro do ItemTemplate só aparece
        // quando o template é realmente construído
        vm.Staged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "src/Unit1.pas", Index = "M", Worktree = ".", Kind = ChangeKind.Tracked },
            Staged = true,
        });
        vm.Unstaged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "novo.txt", Index = ".", Worktree = "?", Kind = ChangeKind.Untracked },
            Staged = false,
        });

        vm.Diff.Load("""
        diff --git a/a.txt b/a.txt
        --- a/a.txt
        +++ b/a.txt
        @@ -1,2 +1,2 @@
         um
        -dois
        +DOIS
        """, "Preparar bloco", _ => Task.CompletedTask);

        Render(new ChangesView(), vm);
    }

    [AvaloniaFact]
    public void ChangesView_monta_no_modo_unificado()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new ChangesViewModel(DemoRepo(), main, split: false);
        vm.Diff.Load("@@ -1 +1 @@\n-a\n+b\n");

        Render(new ChangesView(), vm);
    }

    [AvaloniaFact]
    public void HistoryView_monta_com_grafo_e_detalhe()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new HistoryViewModel(DemoRepo(), main, 50, split: true);
        vm.Commits.Add(new CommitRowViewModel
        {
            Commit = new Commit { Hash = "abc1234567", Subject = "Commit de teste", Author = "Teste", Date = "2026-01-01T10:00:00Z", Refs = { "HEAD -> main", "origin/main" } },
            Row = new GRepos.Services.GraphRow(),
            MaxLanes = 2,
        });
        vm.Files.Add(new CommitFileViewModel { File = new CommitFile { Path = "a.txt", Added = 3, Removed = 1, Status = "M" } });
        vm.HasDetail = true;

        Render(new HistoryView(), vm);
    }

    /// <summary>Menu de contexto e flyouts só constroem os bindings quando abrem.</summary>
    [AvaloniaFact]
    public void HistoryView_abre_o_menu_de_acoes_do_commit()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new HistoryViewModel(DemoRepo(), main, 50, split: true);
        vm.Commits.Add(new CommitRowViewModel
        {
            Commit = new Commit { Hash = "abc1234567", Subject = "Commit de teste", Date = "2026-01-01T10:00:00Z" },
            Row = new GRepos.Services.GraphRow(),
            MaxLanes = 1,
        });
        vm.HasDetail = true;

        var view = new HistoryView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        var lista = view.GetVisualDescendants().OfType<ListBox>().First();
        lista.ContextMenu!.Open(lista);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var itens = lista.ContextMenu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Contains("Cherry-pick na branch atual", itens);
        lista.ContextMenu.Close();

        var resetar = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Resetar para cá ▾");
        resetar.Flyout!.ShowAt(resetar);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        resetar.Flyout.Hide();
        window.Close();
    }

    [AvaloniaFact]
    public void ChangesView_com_varios_marcados_troca_os_rotulos()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new ChangesViewModel(DemoRepo(), main, split: true);
        foreach (var nome in new[] { "a.pas", "b.pas", "c.pas" })
            vm.Unstaged.Add(new FileItemViewModel
            {
                Change = new FileChange { Path = nome, Index = ".", Worktree = "M", Kind = ChangeKind.Tracked },
            });

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();

        var lista = view.FindControl<ListBox>("ListaUnstaged")!;
        lista.SelectedItems!.Add(vm.Unstaged[0]);
        lista.SelectedItems.Add(vm.Unstaged[2]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Preparar selecionados (2)", vm.PrepararRotulo);
        Assert.Equal("Descartar selecionados (2)", vm.DescartarRotulo);
        Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Preparar selecionados (2)");

        var reverter = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Reverter ▾");
        reverter.Flyout!.ShowAt(reverter);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        reverter.Flyout.Hide();
        window.Close();
    }

    /// <summary>Arquivo em conflito com os botões Meu/Deles e a faixa do merge em andamento.</summary>
    [AvaloniaFact]
    public void ChangesView_monta_com_conflito_e_operacao_em_andamento()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new ChangesViewModel(DemoRepo(), main, split: true)
        {
            Operacao = GRepos.Services.GitService.Operacao.Merge,
        };
        vm.Unstaged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "a.pas", Index = "U", Worktree = "U", Kind = ChangeKind.Conflict, Conflito = "UU" },
        });

        var view = new ChangesView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var botoes = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible)
            .Select(b => b.Content as string).ToList();
        Assert.Contains("Meu", botoes);
        Assert.Contains("Deles", botoes);
        Assert.Contains("Concluir merge", botoes);
        Assert.DoesNotContain("+", botoes); // conflito não se prepara pelo "+"

        var lista = view.FindControl<ListBox>("ListaUnstaged")!;
        lista.ContextMenu!.Open(lista);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        lista.ContextMenu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void DiffView_marca_linhas_escolhidas_nos_dois_modos()
    {
        foreach (var split in new[] { true, false })
        {
            var vm = new DiffViewModel { Split = split };
            vm.Load("diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1,2 +1,2 @@\n um\n-dois\n+DOIS\n",
                "Preparar bloco", _ => Task.CompletedTask);

            var view = new DiffView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 600, Content = view };
            window.Show();

            DiffRowBase linha = split
                ? vm.Rows.OfType<DiffSplitRow>().First(r => r.RightIsAdd)
                : vm.Rows.OfType<DiffTextRow>().First(r => r.IsAdd);
            vm.AlternarLinha(linha, direita: true);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Contains(view.GetVisualDescendants().OfType<Button>(),
                b => b.Content as string == "Preparar 1 linha(s)" && b.IsEffectivelyVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("sel"));
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Historico_do_arquivo_monta_as_duas_abas()
    {
        var vm = new FileHistoryViewModel(DemoRepo(), "src/Unit1.pas", blame: false, split: true);
        vm.Commits.Add(new FileCommitViewModel
        {
            Commit = new Commit { Hash = "abc1234567", Subject = "Ajusta juros", Author = "Ana", Date = "2026-01-01T10:00:00Z" },
            Caminho = "Unit1.pas", CaminhoAtual = "src/Unit1.pas",
        });
        vm.Blame.Add(new BlameLineViewModel
        {
            Linha = new BlameLine { Hash = new string('a', 40), Linha = 1, Autor = "Ana", Quando = 1767261600, Assunto = "x", Texto = "unit Unit1;" },
            InicioDeBloco = true, Par = true,
        });
        vm.Blame.Add(new BlameLineViewModel
        {
            Linha = new BlameLine { Hash = new string('0', 40), Linha = 2, Texto = "\tinterface" },
            InicioDeBloco = true,
        });

        var janela = new FileHistoryWindow { DataContext = vm };
        janela.Show();
        vm.AbaSelecionada = 1;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(janela.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Não commitado");
        janela.Close();
    }

    [AvaloniaFact]
    public void Rebase_interativo_monta_com_itens()
    {
        var vm = new RebaseViewModel(DemoRepo(), "abc");
        foreach (var (h, s) in new[] { ("c", "terceiro"), ("b", "segundo"), ("a", "primeiro") })
            vm.Itens.Add(new RebaseItemViewModel(
                new GRepos.Services.ItemRebase { Hash = new string(h[0], 40), Assunto = s, Mensagem = s }, vm));
        vm.Itens[0].Acao = 1; // renomear mostra a caixa de texto
        vm.Itens[1].Acao = 4; // apagado

        var janela = new RebaseWindow { DataContext = vm };
        janela.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains(janela.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "terceiro");
        Assert.True(vm.PodeAplicar);
        janela.Close();
    }

    /// <summary>Item marcável do clique direito de Obter: um clique alterna a opção uma vez só.</summary>
    [AvaloniaFact]
    public void Opcao_de_tags_no_menu_alterna_com_um_clique()
    {
        var main = new MainViewModel(new FakeDialogs());
        var antes = main.BuscarTodasAsTags;
        // mesma montagem do MainWindow.axaml: marca OneWay, comando alterna
        var item = new MenuItem
        {
            ToggleType = MenuItemToggleType.CheckBox,
            DataContext = main,
            Command = main.AlternarTodasAsTagsCommand,
        };
        item.Bind(MenuItem.IsCheckedProperty, new Avalonia.Data.Binding(nameof(MainViewModel.BuscarTodasAsTags))
        {
            Mode = Avalonia.Data.BindingMode.OneWay,
        });
        var janela = new Window { Content = new Menu { Items = { item } } };
        janela.Show();

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(!antes, main.BuscarTodasAsTags);
        Assert.Equal(!antes, item.IsChecked);
        main.BuscarTodasAsTags = antes;
        janela.Close();
    }

    [AvaloniaFact]
    public void PairView_monta_com_os_dois_lados()
    {
        var main = new MainViewModel(new FakeDialogs());
        var a = DemoRepo();
        var b = new Repo { Id = "r2", Name = "Financeiro MySQL", Path = a.Path, PairKey = "Financeiro", Role = "destino" };
        var vm = new PairViewModel(a, b, main, 50);
        vm.Left.Status = new RepoStatus { Branch = "main", Ahead = 1 };
        vm.Right.Status = new RepoStatus { Branch = "main", Unstaged = 2 };
        vm.Left.OnlyHere.Add(new PairCommitViewModel { Subject = "Só na origem", Short = "abc1234" });

        Render(new PairView(), vm);
    }

    [AvaloniaFact]
    public void ChangesView_monta_com_quebra_de_linha_ligada()
    {
        var main = new MainViewModel(new FakeDialogs());
        var vm = new ChangesViewModel(DemoRepo(), main, split: true);
        vm.Diff.Wrap = true;
        vm.Diff.Load("@@ -1 +1 @@\n-linha curta\n+" + new string('x', 400) + "\n");

        Render(new ChangesView(), vm);
    }

    [AvaloniaFact]
    public void PainelView_monta_com_cartoes_em_situacoes_diferentes()
    {
        var main = new MainViewModel(new FakeDialogs());

        // um de cada situação: é o que constrói todos os ramos do template
        var limpo = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "Financeiro" },
            new RepoStatus { Branch = "main" }, "#DB4C9B", main) { CiSituacao = "sucesso" };

        var sujo = new CartaoRepoViewModel(
            new Repo { Id = "r2", Name = "Notas" },
            new RepoStatus { Branch = "fix/x", Ahead = 2, Behind = 1, Unstaged = 3, PendingFiles = 3 },
            "#1F9D55", main) { CiSituacao = "falha" };

        var semCi = new CartaoRepoViewModel(
            new Repo { Id = "r3", Name = "Backup" },
            new RepoStatus { Branch = "main", Conflicted = 2 }, "#5D6675", main) { CiSituacao = "nenhum" };

        // ainda consultando a API: o cartão não pode aparecer como "sem esteira"
        var consultando = new CartaoRepoViewModel(
            new Repo { Id = "r4", Name = "DAV" }, new RepoStatus { Branch = "main" }, "#4F8CFF", main);

        var comErro = new CartaoRepoViewModel(
            new Repo { Id = "r5", Name = "Sumiu" },
            new RepoStatus { Error = "pasta não encontrada" }, "#5D6675", main) { CiSituacao = "nenhum" };

        var vm = new PainelViewModel("Módulos", "5 repositório(s)",
            new[] { limpo, sujo, semCi, consultando, comErro }, main);

        // o cartão de perfil é outro template: precisa ser construído aqui também
        vm.Perfil = new PerfilViewModel("GFBmsoft", new[] { limpo.Repo, sujo.Repo })
        {
            Perfil = new GRepos.Services.Perfil
            {
                Login = "GFBmsoft", Nome = "Gabriel", Bio = "bio", Empresa = "BMSoft",
                Local = "SC", RepositoriosPublicos = 3, Seguidores = 4, Estrelas = 13,
                Linguagens = new[] { "C#" },
            },
            Dias = DiasDeTeste(),
            TotalContribuicoes = 321,
        };
        vm.Perfil.LinhasContadas = true;

        Render(new PainelView(), vm);
    }

    private static System.Collections.Generic.List<GRepos.Services.DiaContribuicao> DiasDeTeste()
    {
        var hoje = new System.DateTime(2026, 10, 1);
        return Enumerable.Range(0, 365)
            .Select(i => new GRepos.Services.DiaContribuicao(hoje.AddDays(-364 + i), i % 7, i % 5))
            .ToList();
    }

    /// <summary>
    /// O quadriculado mostra só as semanas que cabem, sempre até hoje: em janela estreita
    /// encolhe pela esquerda e, sem espaço para um mínimo, some em vez de espremer o texto.
    /// </summary>
    [AvaloniaFact]
    public void Quadriculado_mostra_as_semanas_que_cabem()
    {
        var grade = new GRepos.Controls.GradeContribuicoes { Dias = DiasDeTeste(), ReservaEsquerda = 300 };

        grade.Measure(new Size(double.PositiveInfinity, 200));
        var cheio = grade.DesiredSize.Width;
        Assert.True(cheio > 500, $"ano inteiro deveria passar de 500px, deu {cheio}");

        grade.Measure(new Size(600, 200));
        Assert.True(grade.DesiredSize.Width <= 300, $"sobrou só 300px, mas pediu {grade.DesiredSize.Width}");
        Assert.True(grade.DesiredSize.Width > 0);

        grade.Measure(new Size(350, 200));
        Assert.Equal(0, grade.DesiredSize.Width);

        // a última coluna é a semana de hoje: o dia sob o ponteiro na ponta direita
        grade.Measure(new Size(600, 200));
        grade.Arrange(new Rect(grade.DesiredSize));
        var dia = grade.DiaEm(new Point(grade.DesiredSize.Width - 3, 13 + 3 * 11 + 2));
        Assert.NotNull(dia);
        Assert.Equal(new System.DateTime(2026, 9, 30), dia!.Data); // quarta-feira
        Assert.Equal("6 contribuições em 30/09/2026", GRepos.Controls.GradeContribuicoes.Dica(dia));
    }

    /// <summary>
    /// Cartão aberto: execução, jobs e etapas são templates aninhados dentro do cartão,
    /// com o DataContext trocado para a esteira — só aparecem com as listas preenchidas.
    /// </summary>
    [AvaloniaFact]
    public void PainelView_monta_com_cartao_aberto_e_passo_a_passo()
    {
        var main = new MainViewModel(new FakeDialogs());

        var aberto = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "Financeiro" },
            new RepoStatus { Branch = "develop" }, "#DB4C9B", main) { CiSituacao = "rodando" };

        var esteira = new EsteiraViewModel("", "develop", "", "Financeiro", visiveis: 1, somenteBranch: true);
        var execucao = new CiExecucaoViewModel
        {
            Execucao = new GRepos.Services.CiExecucao
            {
                Id = 1, Numero = 42, Situacao = "rodando", Workflow = "build",
                Titulo = "Pílula de branch na árvore", Branch = "develop", Autor = "GFBmsoft",
            },
        };
        esteira.Execucoes.Add(execucao);
        esteira.Selecionada = execucao;

        var job = new GRepos.Services.CiJob
        {
            Nome = "build", Situacao = "rodando", Duracao = System.TimeSpan.FromSeconds(75),
            Etapas =
            {
                new GRepos.Services.CiEtapa { Numero = 1, Nome = "Checkout", Situacao = "sucesso", Duracao = System.TimeSpan.FromSeconds(3) },
                new GRepos.Services.CiEtapa { Numero = 2, Nome = "Testes", Situacao = "rodando" },
            },
        };
        var jobVm = new CiJobViewModel { Job = job };
        jobVm.SincronizarEtapas(job);
        esteira.Jobs.Add(jobVm);

        aberto.Esteira = esteira;
        aberto.Expandido = true;

        // aberto sem esteira, para o ramo "sem GitHub Actions"
        var semCi = new CartaoRepoViewModel(
            new Repo { Id = "r2", Name = "Backup" }, new RepoStatus { Branch = "main" }, "#5D6675", main)
        { CiSituacao = "nenhum" };
        semCi.Expandido = true;

        var vm = new PainelViewModel("Módulos", "", new[] { aberto, semCi }, main);

        Assert.True(esteira.PodeCancelar);       // rodando: Parar disponível
        Assert.False(esteira.PodeReexecutar);    // e Reexecutar não

        Render(new PainelView(), vm);
    }

    /// <summary>
    /// A grade divide a largura dentro do layout: as fileiras terminam na mesma borda do
    /// cartão de perfil, e o cartão aberto fica sozinho na linha, de ponta a ponta.
    /// </summary>
    [AvaloniaFact]
    public void PainelView_alinha_cartoes_com_o_perfil_e_abre_na_linha_inteira()
    {
        var main = new MainViewModel(new FakeDialogs());
        var cartoes = Enumerable.Range(1, 5).Select(i => new CartaoRepoViewModel(
            new Repo { Id = "r" + i, Name = "Repo " + i }, new RepoStatus { Branch = "main" }, "#4F8CFF", main)
        { CiSituacao = "nenhum" }).ToList();

        var vm = new PainelViewModel("Todos", "", cartoes, main)
        {
            Perfil = new PerfilViewModel("GFBmsoft", new[] { cartoes[0].Repo }),
        };

        var window = new Window { Width = 1000, Height = 700, Content = new PainelView { DataContext = vm } };
        window.Show();
        window.UpdateLayout();

        Panel Moldura(CartaoRepoViewModel c) => window.GetVisualDescendants().OfType<Panel>()
            .First(b => b.Classes.Contains("cartaoMoldura") && ReferenceEquals(b.DataContext, c));

        double Direita(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width, 0), window)!.Value.X;

        var perfil = window.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("card") && b.IsVisible);

        // fileira cheia: o último cartão termina onde o perfil termina
        var primeiraFileira = cartoes.Take(3).Select(Moldura).ToList();
        Assert.Equal(Direita(perfil), Direita(primeiraFileira[^1]), 0);
        Assert.Equal(primeiraFileira[0].Bounds.Width, primeiraFileira[1].Bounds.Width, 1);

        // aberto: sozinho na fileira, encostado à esquerda e com teto de largura — em
        // tela larga o passo a passo da esteira não estica junto com a janela
        cartoes[1].Expandido = true;
        window.UpdateLayout();
        var aberto = Moldura(cartoes[1]);
        double Esquerda(Visual v) => v.TranslatePoint(new Point(0, 0), window)!.Value.X;
        Assert.Equal(Esquerda(perfil), Esquerda(aberto), 0);
        Assert.Equal(System.Math.Min(perfil.Bounds.Width, 860), aberto.Bounds.Width, 0);

        // e os vizinhos não dividem a linha com ele
        var antes = Moldura(cartoes[0]);
        var depois = Moldura(cartoes[2]);
        Assert.True(aberto.TranslatePoint(new Point(0, 0), window)!.Value.Y > antes.TranslatePoint(new Point(0, 0), window)!.Value.Y);
        Assert.True(depois.TranslatePoint(new Point(0, 0), window)!.Value.Y > aberto.TranslatePoint(new Point(0, 0), window)!.Value.Y);

        window.Close();
    }

    [AvaloniaFact]
    public void PainelView_monta_vazio()
    {
        Render(new PainelView(), new PainelViewModel("Grupo novo", "", System.Array.Empty<CartaoRepoViewModel>()));
    }

    [AvaloniaFact]
    public void NovidadesWindow_monta_com_versoes_e_notas()
    {
        var vm = new NovidadesViewModel();
        vm.Versoes.Add(new VersaoViewModel
        {
            Release = new GRepos.Services.Release
            {
                Tag = "1.0.0.9",
                Nome = "GRepos 1.0.0.9",
                Publicada = System.DateTime.UtcNow.AddHours(-2),
                Notas = "## Downloads\n\n- `GRepos-1.0.0.9.exe`: precisa do " +
                        "[.NET 8](https://dotnet.microsoft.com) instalado.\n\n" +
                        "## Mudanças\n\n- Corrige o **modo portátil**\n- Ícone no rodapé\n\n" +
                        "```\ndotnet test\n```\n\n> nota de rodapé",
            },
            EmUso = true,
        });
        vm.Selecionada = vm.Versoes[0];

        var window = new NovidadesWindow { DataContext = vm, Width = 820, Height = 600 };
        window.Show();
        window.Measure(new Size(820, 600));
        window.Arrange(new Rect(0, 0, 820, 600));
        window.Close();
    }

    [AvaloniaFact]
    public void MarkdownView_monta_todos_os_tipos_de_bloco()
    {
        var view = new MarkdownView
        {
            Markdown = "# Título\n\nParágrafo com **negrito**, *itálico*, `código` e " +
                       "[link](https://exemplo.dev).\n\n" +
                       "- item um\n  - subitem\n1. numerado\n\n> citação\n\n---\n\n```\nvar x = 1;\n```",
        };

        Render(view, new object());
    }

    [AvaloniaFact]
    public void EsteiraWindow_monta_com_execucoes_e_passos()
    {
        var vm = new EsteiraViewModel("bmsoft/financeiro", "main", "GFBmsoft", "Financeiro");

        // cartões e passos populados: é o que constrói os ItemTemplates de verdade
        vm.Execucoes.Add(new CiExecucaoViewModel
        {
            Execucao = new GRepos.Services.CiExecucao
            {
                Id = 1, Numero = 42, Situacao = "falha", Workflow = "build",
                Titulo = "Fix missing files", Branch = "main", Autor = "GFBmsoft",
                Url = "https://github.com/x/y/actions/runs/1",
                Criada = System.DateTime.UtcNow.AddMinutes(-12),
            },
        });
        vm.Jobs.Add(new CiJobViewModel
        {
            Job = new GRepos.Services.CiJob { Nome = "build", Situacao = "falha" },
            Etapas =
            {
                new CiEtapaViewModel { Etapa = new GRepos.Services.CiEtapa { Numero = 1, Nome = "Checkout", Situacao = "sucesso", Duracao = System.TimeSpan.FromSeconds(4) } },
                new CiEtapaViewModel { Etapa = new GRepos.Services.CiEtapa { Numero = 2, Nome = "dotnet test", Situacao = "falha", Duracao = System.TimeSpan.FromSeconds(83) } },
                new CiEtapaViewModel { Etapa = new GRepos.Services.CiEtapa { Numero = 3, Nome = "Artefato (runtime)", Situacao = "pulado" } },
            },
        });

        var window = new EsteiraWindow { DataContext = vm, Width = 900, Height = 640 };
        window.Show();
        window.Measure(new Size(900, 640));
        window.Arrange(new Rect(0, 0, 900, 640));
        window.Close();
    }

    /// <summary>
    /// Com o arraste na árvore, o clique passou a valer ao soltar o botão. Precisa
    /// continuar selecionando o repositório e recolhendo o grupo como antes — e o aperto
    /// sozinho não pode mais fazer nada, senão arrastar um grupo já o recolheria.
    /// </summary>
    [AvaloniaFact]
    public void Clique_na_arvore_vale_ao_soltar_e_o_destino_do_arraste_fica_destacado()
    {
        var window = new MainWindow { Width = 900, Height = 600 };
        var vm = (MainViewModel)window.DataContext!;
        var grupo = vm.CreateGroup("DBISAM", "#E07B00");
        vm.AddRepository(@"C:\repos\Financeiro", "Financeiro", grupo);
        vm.AddRepository(@"C:\repos\Solto", "Solto", null);
        vm.RebuildTree();

        window.Show();
        window.Measure(new Size(900, 600));
        window.Arrange(new Rect(0, 0, 900, 600));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var arvore = window.FindControl<ListBox>("TreeList")!;

        Point CentroDe(SidebarNode no)
        {
            var item = arvore.ContainerFromItem(no)!;
            return item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        }

        // repositório: apertar não seleciona, soltar seleciona
        var repo = vm.Tree.OfType<RepoNode>().Single(r => r.Name == "Financeiro");
        window.MouseDown(CentroDe(repo), MouseButton.Left);
        Assert.Null(vm.SelectedNode);
        window.MouseUp(CentroDe(repo), MouseButton.Left);
        Assert.Equal("Financeiro", vm.SelectedNode?.Name);

        // grupo: o clique inteiro recolhe, como antes
        var no = vm.Tree.OfType<GroupNode>().Single(g => g.Name == "DBISAM");
        window.MouseDown(CentroDe(no), MouseButton.Left);
        Assert.False(vm.Groups.Single(g => g.Id == grupo).Collapsed);
        window.MouseUp(CentroDe(no), MouseButton.Left);
        Assert.True(vm.Groups.Single(g => g.Id == grupo).Collapsed);

        // a moldura de destino é só um estado do nó: liga e a linha é redesenhada sem erro
        var destino = vm.Tree.OfType<GroupNode>().Single(g => g.Name == "DBISAM");
        destino.AlvoDeSoltar = true;
        vm.Tree.OfType<PainelNode>().Single().AlvoDeSoltar = true;
        window.Measure(new Size(900, 600));
        window.Arrange(new Rect(0, 0, 900, 600));

        window.Close();
    }

    /// <summary>
    /// Digitar o código da cor muda a amostra na hora. Antes só mudava ao sair do campo,
    /// e parecia que o valor digitado não fazia nada.
    /// </summary>
    [AvaloniaFact]
    public void Digitar_o_codigo_da_cor_muda_a_amostra_sem_sair_do_campo()
    {
        var janela = new GroupWindow("Editar grupo", "Fiscal", "#1F9D55");
        janela.Show();
        janela.Measure(new Size(420, 330));
        janela.Arrange(new Rect(0, 0, 420, 330));

        var seletor = janela.GetVisualDescendants().OfType<GRepos.Controls.SeletorDeCor>().Single();
        var hex = janela.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "#1F9D55");
        var bolinhas = janela.GetVisualDescendants().OfType<Button>().Where(b => b.Tag is string).ToList();

        hex.Text = "#0078d7"; // minúsculas também valem
        Assert.Equal("#0078D7", seletor.Cor);
        Assert.DoesNotContain(bolinhas, b => b.BorderThickness.Top > 0); // não é uma das cores de atalho

        // código pela metade não é cor: fica valendo a última válida
        hex.Text = "#0078";
        Assert.Equal("#0078D7", seletor.Cor);

        // uma das cores de atalho, digitada: a bolinha dela acende
        hex.Text = "#D93F3F";
        Assert.Equal("#D93F3F", seletor.Cor);
        Assert.Equal("#D93F3F", bolinhas.Single(b => b.BorderThickness.Top > 0).Tag);

        janela.Close();
    }

    internal static IssuesViewModel IssuesPopulado()
    {
        var bug = new GRepos.Services.Etiqueta("bug", "#d73a4a");
        var melhoria = new GRepos.Services.Etiqueta("enhancement", "#a2eeef");
        var agora = System.DateTime.UtcNow;

        var vm = new IssuesViewModel("", "GFBmsoft", "bm-api");
        vm.Aplicar(new[]
        {
            new GRepos.Services.Issue
            {
                Numero = 48, Titulo = "[Segurança] Rotas de leitura sem checagem de permissão", Autor = "GFBmsoft",
                Corpo = "As rotas de **leitura** respondem sem conferir a permissão.\n\n- HashPAF\n- Revenda",
                Comentarios = 2, Atualizada = agora.AddHours(-3), Etiquetas = { bug, melhoria },
                Responsaveis = { "WiliampBMsoft" },
            },
            new GRepos.Services.Issue
            {
                Numero = 47, Titulo = "SMTP e Discord sem validação de certificado TLS", Autor = "GFBmsoft",
                Atualizada = agora.AddDays(-1), Etiquetas = { bug },
            },
            new GRepos.Services.Issue { Numero = 41, Titulo = "Documentar o fluxo de transferência", Autor = "colega", Atualizada = agora.AddDays(-4) },
        }, new[] { new GRepos.Services.Issue { Numero = 10, Titulo = "Antiga", Aberta = false } });

        vm.Comentarios.Add(new ComentarioViewModel
        {
            Comentario = new GRepos.Services.Comentario { Autor = "WiliampBMsoft", Corpo = "Confirmado em produção.", Quando = agora.AddHours(-2) },
        });
        return vm;
    }

    [AvaloniaFact]
    public void IssuesWindow_monta_com_lista_etiquetas_e_comentarios()
    {
        var vm = IssuesPopulado();

        var window = new IssuesWindow { DataContext = vm, Width = 1080, Height = 700 };
        window.Show();
        window.Measure(new Size(1080, 700));
        window.Arrange(new Rect(0, 0, 1080, 700));

        Assert.Equal(3, vm.Lista.Count);
        Assert.Equal(2, vm.Etiquetas.Count);

        // um comentário próprio em edição e o formulário de issue: templates que só
        // existem nesses estados
        var meu = new ComentarioViewModel
        {
            Comentario = new GRepos.Services.Comentario { Id = 9, Autor = "GFBmsoft", Corpo = "Vou olhar." },
            Proprio = true,
        };
        vm.Editor.Itens.Add(meu);
        vm.Editor.EditarCommand.Execute(meu);
        window.Measure(new Size(1080, 700));
        window.Arrange(new Rect(0, 0, 1080, 700));
        Assert.True(meu.Editando);

        vm.MontarFormEtiquetas(new[] { "bug" });
        vm.NovaCommand.Execute(null);
        window.Measure(new Size(1080, 700));
        window.Arrange(new Rect(0, 0, 1080, 700));
        Assert.True(vm.Editando);

        window.Close();
    }

    /// <summary>As três abas do PR têm templates próprios: cada uma precisa ser montada.</summary>
    [AvaloniaFact]
    public void PullRequestsWindow_monta_as_abas_de_conversa_commits_e_arquivos()
    {
        var vm = PullRequestsComAbas();

        var window = new PullRequestsWindow { DataContext = vm, Width = 1120, Height = 720 };
        window.Show();
        foreach (var aba in new[] { 0, 1, 2 })
        {
            vm.Aba = aba;
            window.Measure(new Size(1120, 720));
            window.Arrange(new Rect(0, 0, 1120, 720));
        }

        Assert.Equal("Units/Boleto.pas", vm.ArquivoSelecionado!.Path);
        window.Close();
    }

    internal static PullRequestsViewModel PullRequestsComAbas()
    {
        var vm = PullRequestsPopulado();
        var agora = System.DateTime.UtcNow;

        vm.AplicarConversa(new[]
        {
            new GRepos.Services.Comentario { Autor = "colega", Tipo = "pediu mudanças", Corpo = "Falta tratar o **boleto vencido**.", Quando = agora.AddHours(-5) },
            new GRepos.Services.Comentario { Autor = "colega", Tipo = "comentou no código", Onde = "Units/Boleto.pas:42", Corpo = "Aqui estoura com nulo.", Quando = agora.AddHours(-4) },
            new GRepos.Services.Comentario { Autor = "GFBmsoft", Corpo = "Corrigido no último commit.", Quando = agora.AddHours(-3) },
            new GRepos.Services.Comentario { Autor = "colega", Tipo = "aprovou", Quando = agora.AddHours(-1) },
        });
        vm.AplicarCommits(new[]
        {
            new GRepos.Services.CommitDoPr { Sha = "302217b67e22", Assunto = "fix(boleto): ocultar campo do boleto online", Autor = "GFBmsoft", Quando = agora.AddHours(-6) },
            new GRepos.Services.CommitDoPr { Sha = "9a1c44e0b7aa", Assunto = "trata o boleto vencido", Autor = "GFBmsoft", Quando = agora.AddHours(-3) },
        });
        vm.AplicarArquivos(new[]
        {
            new GRepos.Services.ArquivoDoPr
            {
                Caminho = "Units/Boleto.pas", Status = "M", Adicionadas = 2, Removidas = 1,
                Patch = "@@ -1,3 +1,4 @@\n begin\n-  Valor := Base;\n+  Valor := Base * 1.02;\n+  Juros := Valor * Taxa;\n end;",
            },
            new GRepos.Services.ArquivoDoPr { Caminho = "img/logo.png", Status = "A" },
        });
        return vm;
    }

    internal static CompararViewModel CompararPopulado()
    {
        var vm = new CompararViewModel(new Repo { Id = "r1", Name = "Financeiro", Path = "" }, "develop", "imp/boleto", split: true);
        vm.Refs.Add("develop");
        vm.Refs.Add("imp/boleto");
        vm.Resumo = CompararViewModel.Descrever("develop", "imp/boleto", 1, 3, 2);
        vm.Arquivos.Add(new CommitFileViewModel { File = new CommitFile { Path = "Units/Boleto.pas", Added = 12, Removed = 3, Status = "M" } });
        vm.Arquivos.Add(new CommitFileViewModel { File = new CommitFile { Path = "Units/Juros.pas", Added = 40, Status = "A" } });
        vm.Diff.Title = "Units/Boleto.pas  (develop → imp/boleto)";
        vm.Diff.Load(
            "diff --git a/Units/Boleto.pas b/Units/Boleto.pas\n--- a/Units/Boleto.pas\n+++ b/Units/Boleto.pas\n" +
            "@@ -1,3 +1,4 @@\n begin\n-  Valor := Base;\n+  Valor := Base * 1.02;\n+  Juros := Valor * Taxa;\n end;\n");
        return vm;
    }

    [AvaloniaFact]
    public void CompararWindow_monta_com_arquivos_e_diff()
    {
        var window = new CompararWindow { DataContext = CompararPopulado(), Width = 1180, Height = 760 };
        window.Show();
        window.Measure(new Size(1180, 760));
        window.Arrange(new Rect(0, 0, 1180, 760));
        window.Close();
    }

    internal static LoteViewModel LotePopulado()
    {
        var vm = new LoteViewModel("DBISAM", new[]
        {
            new LoteItemViewModel
            {
                Repo = new Repo { Id = "r1", Name = "Financeiro" }, CorDoGrupo = "#DB4C9B",
                Status = new RepoStatus { Branch = "develop", Behind = 2 }, Situacao = "ok", Mensagem = "saiu de imp/boleto",
            },
            new LoteItemViewModel
            {
                Repo = new Repo { Id = "r2", Name = "NFCe" }, CorDoGrupo = "#DB4C9B",
                Status = new RepoStatus { Branch = "master", Ahead = 1, PendingFiles = 3 },
                Situacao = "erro", Mensagem = "há alterações locais que a operação sobrescreveria: commite ou guarde (stash) antes",
            },
            new LoteItemViewModel
            {
                Repo = new Repo { Id = "r3", Name = "SPED" }, CorDoGrupo = "#DB4C9B",
                Status = new RepoStatus { Branch = "master" }, Situacao = "pulado", Mensagem = "não tem essa branch",
            },
            new LoteItemViewModel
            {
                Repo = new Repo { Id = "r4", Name = "Testes" }, CorDoGrupo = "#4F8CFF",
                Status = new RepoStatus { Branch = "main" }, Marcado = false,
            },
        }, new MainViewModel(new FakeDialogs()));

        vm.Operacao = vm.Operacoes.Single(o => o.Operacao == GRepos.Services.OperacaoEmLote.Trocar);
        vm.Branches.Add("develop");
        vm.Branch = "develop";
        vm.Resumo = "1 feito(s), 1 pulado(s), 1 com erro";
        return vm;
    }

    [AvaloniaFact]
    public void LoteWindow_monta_com_repositorios_e_resultados()
    {
        var vm = LotePopulado();

        var window = new LoteWindow { DataContext = vm, Width = 760, Height = 580 };
        window.Show();
        window.Measure(new Size(760, 580));
        window.Arrange(new Rect(0, 0, 760, 580));

        Assert.Equal("Trocar de branch em 3 repositórios", vm.ExecutarRotulo);
        window.Close();
    }

    internal static WorktreesViewModel WorktreesPopulado()
    {
        var vm = new WorktreesViewModel(new Repo { Id = "r1", Name = "Financeiro", Path = @"D:\Repos\Financeiro" },
            new MainViewModel(new FakeDialogs()));

        vm.Lista.Add(new WorktreeItemViewModel
        {
            Worktree = new GRepos.Services.Worktree { Caminho = "D:/Repos/Financeiro", Branch = "develop", Head = "1111111aaaa", Principal = true },
            NoWorkspace = true,
        });
        vm.Lista.Add(new WorktreeItemViewModel
        {
            Worktree = new GRepos.Services.Worktree { Caminho = "D:/Repos/Financeiro-imp-boleto", Branch = "imp/boleto", Head = "2222222bbbb" },
        });
        vm.Lista.Add(new WorktreeItemViewModel
        {
            Worktree = new GRepos.Services.Worktree { Caminho = "D:/Repos/Financeiro-defeito-sped", Branch = "defeito/sped", Head = "3333333cccc", Orfa = true },
        });
        vm.Branches.Add("master");
        vm.NovaBranch = "master";
        return vm;
    }

    [AvaloniaFact]
    public void WorktreesWindow_monta_com_a_lista_e_o_formulario()
    {
        var vm = WorktreesPopulado();

        var window = new WorktreesWindow { DataContext = vm, Width = 760, Height = 500 };
        window.Show();
        window.Measure(new Size(760, 500));
        window.Arrange(new Rect(0, 0, 760, 500));

        Assert.Equal("Financeiro-master", System.IO.Path.GetFileName(vm.NovaPasta));
        window.Close();
    }

    internal static PaletaViewModel PaletaPopulada() => new(new[]
    {
        new GRepos.Services.ItemDaPaleta { Tipo = "ação", Titulo = "Obter", Detalhe = "fetch em Financeiro", Cor = "Accent" },
        new GRepos.Services.ItemDaPaleta { Tipo = "ação", Titulo = "Pull requests…", Detalhe = "ver, criar e mesclar em Financeiro", Cor = "Accent" },
        new GRepos.Services.ItemDaPaleta { Tipo = "repositório", Titulo = "Financeiro", Detalhe = "DBISAM · develop", Cor = "#DB4C9B" },
        new GRepos.Services.ItemDaPaleta { Tipo = "repositório", Titulo = "Financeiro MySQL", Detalhe = "MySQL · imp/boleto", Cor = "#4F8CFF" },
        new GRepos.Services.ItemDaPaleta { Tipo = "branch", Titulo = "feat/boleto-online", Detalhe = "trocar para ela", Cor = "Green" },
        new GRepos.Services.ItemDaPaleta { Tipo = "painel", Titulo = "Painel: DBISAM", Detalhe = "cartões dos repositórios do grupo", Cor = "#DB4C9B" },
    });

    [AvaloniaFact]
    public void PaletaWindow_monta_com_itens_e_o_enter_escolhe()
    {
        var vm = PaletaPopulada();
        var window = new PaletaWindow(vm);
        window.Show();
        window.Measure(new Size(620, 420));
        window.Arrange(new Rect(0, 0, 620, 420));

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.Equal("Pull requests…", vm.Selecionado!.Titulo);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("Pull requests…", vm.Escolhido!.Titulo);
    }

    internal static ConflitoViewModel ConflitoPopulado()
    {
        var vm = new ConflitoViewModel("", "Units/Boleto.pas");
        vm.Montar(
            "procedure TBoleto.Calcular;\nbegin\n  Total := 0;\n" +
            "<<<<<<< HEAD\n  Valor := Base * 1.02; // reajuste\n=======\n  Valor := Base;\n  Juros := Valor * Taxa;\n>>>>>>> feat/juros\n" +
            "  Total := Total + Valor;\nend;\n\nprocedure TBoleto.Imprimir;\nbegin\n" +
            "<<<<<<< HEAD\n=======\n  Log('imprimindo');\n>>>>>>> feat/juros\n" +
            "  Relatorio.Print;\nend;\n");
        vm.Blocos[0].UsarAmbos();
        return vm;
    }

    [AvaloniaFact]
    public void ConflitoWindow_monta_com_blocos_resolvidos_e_pendentes()
    {
        var vm = ConflitoPopulado();

        var window = new ConflitoWindow { DataContext = vm, Width = 1040, Height = 720 };
        window.Show();
        window.Measure(new Size(1040, 720));
        window.Arrange(new Rect(0, 0, 1040, 720));

        Assert.Equal(2, vm.Blocos.Count);
        Assert.Equal(1, vm.Pendentes);
        window.Close();
    }

    internal static PullRequestsViewModel PullRequestsPopulado()
    {
        var vm = new PullRequestsViewModel(new ContextoPr("", "imp/boleto", "GFBmsoft", "Financeiro")
        {
            Destinos = new[] { "develop", "master" },
            TituloSugerido = "fix(boleto): ocultar campo do boleto online",
            NaoEnviados = 1,
        });

        vm.Aplicar(new[]
        {
            new GRepos.Services.PullRequest
            {
                Numero = 42, Titulo = "fix(boleto): ocultar campo do boleto online", Autor = "GFBmsoft",
                Origem = "feat/boleto", Destino = "develop", Sha = "abc",
                Corpo = "Fecha o **#40**.\n\n- oculta o campo\n- ajusta o relatório",
                Atualizado = System.DateTime.UtcNow.AddHours(-2),
            },
            new GRepos.Services.PullRequest
            {
                Numero = 41, Titulo = "Relatório de comissões", Autor = "colega", Rascunho = true,
                Origem = "imp/comissoes", Destino = "develop", Atualizado = System.DateTime.UtcNow.AddDays(-1),
            },
            new GRepos.Services.PullRequest
            {
                Numero = 39, Titulo = "Correção do SPED", Autor = "colega", Estado = "mesclado",
                Origem = "defeito/sped", Destino = "master", Atualizado = System.DateTime.UtcNow.AddDays(-3),
            },
        });

        vm.Lista[0].CiSituacao = "sucesso";
        vm.Lista[1].CiSituacao = "falha";
        var agora = System.DateTime.UtcNow;
        vm.AplicarVerificacoes(new[]
        {
            new GRepos.Services.Verificacao { Nome = "build", Situacao = "sucesso", Iniciada = agora.AddSeconds(-200), Concluida = agora.AddSeconds(-111) },
            new GRepos.Services.Verificacao { Nome = "testes", Situacao = "rodando", Iniciada = agora.AddSeconds(-40), RunId = 7 },
        });
        vm.Verificacoes[1].Passo = "passo 4 de 10: Testes";
        vm.Detalhe = new GRepos.Services.PullRequest
        {
            Numero = 42, Commits = 3, Arquivos = 5, Adicionadas = 120, Removidas = 30, Mesclagem = "clean",
            Corpo = "Fecha o **#40**.\n\n- oculta o campo\n- ajusta o relatório",
        };
        return vm;
    }

    [AvaloniaFact]
    public void PullRequestsWindow_monta_com_lista_detalhe_e_formulario()
    {
        var vm = PullRequestsPopulado();

        var window = new PullRequestsWindow { DataContext = vm, Width = 940, Height = 640 };
        window.Show();
        window.Measure(new Size(940, 640));
        window.Arrange(new Rect(0, 0, 940, 640));

        Assert.True(vm.MostraDetalhe);
        Assert.True(vm.PodeMesclar);

        // o formulário de PR novo usa outros controles: precisa ser montado também
        vm.NovoCommand.Execute(null);
        window.Measure(new Size(940, 640));
        window.Arrange(new Rect(0, 0, 940, 640));
        Assert.True(vm.Criando);

        window.Close();
    }

    [AvaloniaFact]
    public void CtrlF_leva_o_foco_ao_filtro_e_Esc_limpa()
    {
        var window = new MainWindow();
        window.Show();
        window.Measure(new Size(1400, 900));
        window.Arrange(new Rect(0, 0, 1400, 900));

        var caixa = window.FindControl<TextBox>("CaixaFiltro");
        Assert.NotNull(caixa);
        Assert.False(caixa!.IsFocused);

        window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
        Assert.True(caixa.IsFocused);

        caixa.Text = "finan";
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Assert.True(string.IsNullOrEmpty(caixa.Text));

        window.Close();
    }

    /// <summary>
    /// Grupos por pasta com itens: o template do item mora dentro do template do grupo
    /// e pega o comando da janela — binding errado ali só aparece com a lista montada.
    /// </summary>
    [AvaloniaFact]
    public void BranchesWindow_monta_com_grupos_populados()
    {
        var window = new BranchesWindow(new MainViewModel(new FakeDialogs()), DemoRepo());
        var vm = (BranchesViewModel)window.DataContext!;

        BranchItemViewModel Item(string nome, bool remota = false, bool head = false) => new()
        {
            Branch = new Branch { Name = nome, IsRemote = remota, IsHead = head, Upstream = remota ? null : "origin/" + nome, Subject = "assunto" },
            Tipo = GRepos.Services.GitFlow.Classificar(GRepos.Services.GitFlow.SemRemoto(nome, remota)),
        };

        vm.GruposLocais = BranchesViewModel.Agrupar(new[] { Item("develop", head: true), Item("master"), Item("feat/cartaopix"), Item("fix/FNOBS") });
        vm.GruposRemotos = BranchesViewModel.Agrupar(new[] { Item("origin/develop", true), Item("origin/fix/6329_email", true) });
        vm.GruposLocais[1].Recolhido = true;

        window.Show();
        window.Measure(new Size(780, 640));
        window.Arrange(new Rect(0, 0, 780, 640));
        window.Close();
    }

    [AvaloniaFact]
    public void MainWindow_abre_com_a_arvore_e_a_barra()
    {
        var window = new MainWindow();
        window.Show();
        window.Measure(new Size(1400, 900));
        window.Arrange(new Rect(0, 0, 1400, 900));
        window.Close();
    }
}
