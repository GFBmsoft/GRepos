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
            },
        });

        var window = new EsteiraWindow { DataContext = vm, Width = 900, Height = 640 };
        window.Show();
        window.Measure(new Size(900, 640));
        window.Arrange(new Rect(0, 0, 900, 640));
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
