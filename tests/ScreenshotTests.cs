using System;
using System.Linq;
using System.IO;
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
/// Renderiza as telas de verdade e grava PNG quando GREPOS_SHOTS aponta uma pasta.
/// Serve para conferir layout sem abrir janela na máquina de quem está trabalhando.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class ScreenshotTests
{
    private static string? OutDir => Environment.GetEnvironmentVariable("GREPOS_SHOTS");

    private static void Shot(Control view, object dataContext, string nome, int w = 1280, int h = 800)
    {
        var window = new Window { Width = w, Height = h, Content = view };
        view.DataContext = dataContext;
        window.Show();
        window.Measure(new Size(w, h));
        window.Arrange(new Rect(0, 0, w, h));

        var dir = OutDir;
        if (dir is null) return; // sem pasta configurada o teste só valida a montagem

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(dir, nome + ".png"));
    }

    private static HistoryViewModel HistoricoDeExemplo()
    {
        var vm = new HistoryViewModel(new Repo { Id = "r1", Name = "Financeiro", Path = "." },
            new MainViewModel(new FakeDialogs()), 100, split: true);

        var commits = new[]
        {
            Commit("4372d2c",
                "feat: O pacote que nunca atualiza pode ser calado, e atualizar não abre mais janela — assunto bem longo para forçar o corte",
                new[] { "8fb0c29" }, "HEAD -> main", "tag: 1.0.0.27", "origin/main", "origin/HEAD"),
            Commit("8fb0c29", "Merge da feature de juros no main", new[] { "f4d81d8", "023b4e7" }),
            Commit("023b4e7", "Adiciona calculo de juros compostos", new[] { "c2bbdc4" }, "feature/juros"),
            Commit("f4d81d8", "Adiciona parametros de configuracao", new[] { "c2bbdc4" }),
            Commit("c2bbdc4", "Estrutura inicial do modulo financeiro", Array.Empty<string>()),
        };

        var rows = GraphBuilder.Build(commits);
        var max = GraphBuilder.MaxLanes(rows);
        foreach (var r in rows)
            vm.Commits.Add(new CommitRowViewModel { Commit = r.Commit, Row = r, MaxLanes = max });

        vm.DetailSubject = "Merge da feature de juros no main";
        vm.DetailAuthor = "Gabriel Ferreira <gabriel@bmsoft.com.br>";
        vm.DetailDate = "29/09/2026 10:49:06";
        vm.DetailHash = "8fb0c29";
        vm.DetailParents = "f4d81d8, 023b4e7";
        vm.DetailBody = "Traz o cálculo de juros compostos para o main.";
        vm.HasDetail = true;

        vm.Files.Add(new CommitFileViewModel { File = new CommitFile { Path = "Financeiro.pas", Added = 2, Removed = 0, Status = "M" } });
        vm.Files.Add(new CommitFileViewModel { File = new CommitFile { Path = "Config.ini", Added = 1, Removed = 0, Status = "A" } });

        vm.Diff.CharWidth = 7.2;
        vm.Diff.ViewportWidth = 560;
        vm.Diff.Title = "Financeiro.pas";
        vm.Diff.Load("""
        diff --git a/Financeiro.pas b/Financeiro.pas
        --- a/Financeiro.pas
        +++ b/Financeiro.pas
        @@ -1,7 +1,9 @@
         unit Financeiro;

         interface
        +
        +function CalcularJuros(Valor: Currency): Currency;

         implementation

         end.
        """);
        return vm;
    }

    private static Commit Commit(string hash, string subject, string[] parents, params string[] refs) => new()
    {
        Hash = hash + new string('0', 33),
        Subject = subject,
        Parents = new System.Collections.Generic.List<string>(Array.ConvertAll(parents, p => p + new string('0', 33))),
        Author = "Gabriel Ferreira",
        Email = "gabriel@bmsoft.com.br",
        Date = "2026-09-29T10:49:06-03:00",
        Refs = new System.Collections.Generic.List<string>(refs),
    };

    private sealed class FakeDialogs : IDialogService
    {
        public System.Threading.Tasks.Task<bool> ConfirmAsync(string t, string m) => System.Threading.Tasks.Task.FromResult(false);
        public System.Threading.Tasks.Task<string?> PickFolderAsync(string t) => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<string?> PromptAsync(string t, string l, string i = "") => System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => System.Threading.Tasks.Task.FromResult<(string, string)?>(null);
        public System.Threading.Tasks.Task ShowAddRepoAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowRepoConfigAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowSettingsAsync(MainViewModel m) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowBranchesAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowNovidadesAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task ShowStashAsync(MainViewModel m, Repo r) => System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>Janela inteira, para conferir moldura, seções e rodapé.</summary>
    private static void ShotJanela(Window window, string nome, int w, int h)
    {
        window.Width = w;
        window.Height = h;
        window.Show();
        window.Measure(new Size(w, h));
        window.Arrange(new Rect(0, 0, w, h));

        var dir = OutDir;
        if (dir is null) return;

        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(dir, nome + ".png"));
    }

    /// <summary>Pasta, subpasta e repositórios na árvore; os caminhos não existem, só o desenho interessa.</summary>
    [AvaloniaFact]
    public async Task Sidebar_com_subgrupos()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-sub-" + Path.GetRandomFileName());
        Directory.CreateDirectory(home);
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            var ws = new Workspace
            {
                Groups =
                {
                    new Group { Id = "db", Name = "DBISAM", Color = "#E07B00" },
                    new Group { Id = "fis", Name = "Fiscal", Color = "#1F9D55", ParentId = "db" },
                    new Group { Id = "nfe", Name = "Notas", Color = "#2F7BE8", ParentId = "fis" },
                    new Group { Id = "my", Name = "MySQL", Color = "#8B5CF6" },
                },
            };
            foreach (var (nome, grupo) in new[]
                     {
                         ("Financeiro", "db"), ("Master", "db"), ("SPED", "fis"), ("NFCe", "nfe"), ("NFSe - PWS", "nfe"),
                         ("Financeiro MySQL", "my"),
                     })
                ws.Repos.Add(new Repo { Id = nome, Name = nome, Path = Path.Combine(home, nome), GroupId = grupo });
            WorkspaceStore.Save(ws);

            var janela = new MainWindow { Width = 300, Height = 420 };
            var principal = (MainViewModel)janela.DataContext!;
            await principal.InitAsync();
            ShotJanela(janela, "sidebar-subgrupos", 300, 420);

            Assert.Equal(new[] { 0, 1, 2, 0 }, principal.Tree.OfType<GroupNode>().Select(g => g.Nivel));
            janela.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }

    /// <summary>Diálogo de grupo com a escolha do pai, e o painel de cores aberto.</summary>
    [AvaloniaFact]
    public void Dialogo_de_grupo_com_pai_e_painel_de_cores()
    {
        var pais = GrupoArvore.EmOrdem(new[]
        {
            new Group { Id = "db", Name = "DBISAM" },
            new Group { Id = "fis", Name = "Fiscal", ParentId = "db" },
            new Group { Id = "my", Name = "MySQL" },
        });

        var janela = new GroupWindow("Novo subgrupo de Fiscal", "Notas", "#1F9D55", "fis", pais);
        ShotJanela(janela, "dialogo-grupo-pai", 420, 330);

        var seletor = janela.GetVisualDescendants().OfType<GRepos.Controls.SeletorDeCor>().Single();
        var combo = janela.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal(2, combo.SelectedIndex); // "(nível principal)", DBISAM, Fiscal

        // escolher no painel acompanha o campo e as bolinhas
        seletor.Escolher("#0078D7");
        var hex = janela.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "#0078D7");
        Assert.NotNull(hex);

        // o painel em si, fora do popup, para conferir o desenho da grade
        var painel = new Window
        {
            Content = new Border { Padding = new Thickness(12), Child = (Control)((Flyout)seletor.Flyout!).Content! },
        };
        seletor.Flyout = null;
        ShotJanela(painel, "painel-de-cores", 280, 330);
        painel.Close();
        janela.Close();
    }

    [AvaloniaFact]
    public void Issues_com_etiquetas()
    {
        var vm = UiSmokeTests.IssuesPopulado();
        var meu = new ComentarioViewModel
        {
            Comentario = new Comentario { Id = 9, Autor = "GFBmsoft", Corpo = "Vou olhar isso hoje.", Quando = DateTime.UtcNow.AddMinutes(-20) },
            Proprio = true,
        };
        vm.Editor.Itens.Add(meu);
        vm.Editor.Novo = "Resolvido na 1.0.0.35.";
        var janela = new IssuesWindow { DataContext = vm };
        ShotJanela(janela, "issues", 1080, 760);
        janela.Close();

        var form = UiSmokeTests.IssuesPopulado();
        form.EditarIssueCommand.Execute(null);
        var janelaForm = new IssuesWindow { DataContext = form };
        ShotJanela(janelaForm, "issues-editar", 1080, 700);
        janelaForm.Close();
    }

    [AvaloniaFact]
    public void Pull_request_nas_abas_de_conversa_e_arquivos()
    {
        var conversa = new PullRequestsWindow { DataContext = UiSmokeTests.PullRequestsComAbas() };
        ShotJanela(conversa, "pr-conversa", 1120, 720);
        conversa.Close();

        var vm = UiSmokeTests.PullRequestsComAbas();
        vm.Aba = 2;
        var arquivos = new PullRequestsWindow { DataContext = vm };
        ShotJanela(arquivos, "pr-arquivos", 1120, 720);
        arquivos.Close();
    }

    [AvaloniaFact]
    public void Comparar_branches()
    {
        var janela = new CompararWindow { DataContext = UiSmokeTests.CompararPopulado() };
        ShotJanela(janela, "comparar", 1180, 760);
        janela.Close();
    }

    [AvaloniaFact]
    public void Operacoes_em_lote()
    {
        var janela = new LoteWindow { DataContext = UiSmokeTests.LotePopulado() };
        ShotJanela(janela, "lote", 760, 580);
        janela.Close();
    }

    [AvaloniaFact]
    public void Pastas_de_trabalho()
    {
        var janela = new WorktreesWindow { DataContext = UiSmokeTests.WorktreesPopulado() };
        ShotJanela(janela, "worktrees", 760, 500);
        janela.Close();
    }

    [AvaloniaFact]
    public void Paleta_de_comandos()
    {
        var vm = UiSmokeTests.PaletaPopulada();
        vm.Consulta = "fin";
        var janela = new PaletaWindow { DataContext = vm };
        ShotJanela(janela, "paleta", 620, 420);
        janela.Close();
    }

    [AvaloniaFact]
    public void Conflito_bloco_a_bloco()
    {
        var janela = new ConflitoWindow { DataContext = UiSmokeTests.ConflitoPopulado() };
        ShotJanela(janela, "conflito", 1040, 720);
        janela.Close();
    }

    [AvaloniaFact]
    public void Pull_requests_com_detalhe_e_com_formulario()
    {
        var detalhe = new PullRequestsWindow { DataContext = UiSmokeTests.PullRequestsPopulado() };
        ShotJanela(detalhe, "pull-requests", 940, 640);
        detalhe.Close();

        var vm = UiSmokeTests.PullRequestsPopulado();
        vm.NovoCommand.Execute(null);
        var novo = new PullRequestsWindow { DataContext = vm };
        ShotJanela(novo, "pull-requests-novo", 940, 640);
        novo.Close();
    }

    /// <summary>Preferências na seção das notas: a versão escolhida e o que mudou nela.</summary>
    [AvaloniaFact]
    public void Preferencias_na_secao_de_notas_da_versao()
    {
        var janela = new SettingsWindow(new MainViewModel(new FakeDialogs()));
        janela.Show();
        var lista = janela.GetVisualDescendants().OfType<ListBox>().First();
        lista.SelectedIndex = lista.Items.Cast<string>().ToList().IndexOf("Notas da versão");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ShotJanela(janela, "preferencias-notas", 720, 560);
    }

    /// <summary>
    /// Preferências com grupos: são as linhas de grupo, com botões encostados à direita,
    /// que denunciam a barra de rolagem passando por cima do conteúdo.
    /// </summary>
    [AvaloniaFact]
    public void Preferencias_com_secoes_e_grupos()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-prefs-" + Path.GetRandomFileName());
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            var main = new MainViewModel(new FakeDialogs());
            main.CreateGroup("Manuais - BMSoft", "#DB4C9B");
            main.CreateGroup("Módulos", "#1F9D55");

            // duas contas: a lista de contas é o que a seção de autenticação desenha
            var contaAntes = GitService.CredentialUser;
            main.SetContas(new[] { "GFBmsoft", "bmsoftsistemas" }, "GFBmsoft");
            try {             ShotJanela(new SettingsWindow(main), "preferencias", 720, 560); }
            finally { GitService.CredentialUser = contaAntes; }
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", null);
            try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }

    /// <summary>Configurar Repositório: título, campos e o rodapé com os três botões.</summary>
    [AvaloniaFact]
    public void Configurar_repositorio_com_rodape_centralizado()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-cfg-" + Path.GetRandomFileName());
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            var dialogs = new FakeDialogs();
            var main = new MainViewModel(dialogs);
            var repo = new Repo { Id = "r1", Name = "Financeiro", Path = home, PairKey = "Financeiro", Role = "origem" };
            var contaAntes = GitService.CredentialUser;
            main.SetContas(new[] { "GFBmsoft", "bmsoftsistemas" }, "GFBmsoft");
            repo.Conta = "bmsoftsistemas";
            GitService.CredentialUser = contaAntes;

            ShotJanela(new RepoConfigWindow(main, repo, dialogs), "configurar-repositorio", 460, 700);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", null);
            try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }

    /// <summary>Painel do grupo, com um cartão de cada situação.</summary>
    [AvaloniaFact]
    public void Painel_do_grupo_com_cartoes()
    {
        var main = new MainViewModel(new FakeDialogs());

        var cartoes = new[]
        {
            new CartaoRepoViewModel(new Repo { Id = "r1", Name = "Financeiro (DBISAM)" },
                new RepoStatus { Branch = "main" }, "#DB4C9B", main) { CiSituacao = "sucesso" },
            new CartaoRepoViewModel(new Repo { Id = "r2", Name = "Notas" },
                new RepoStatus { Branch = "fix/CorrecaoTbEdit", Ahead = 2, Behind = 1, Unstaged = 3, PendingFiles = 3 },
                "#1F9D55", main) { CiSituacao = "falha", PrConsultado = true, PrsAbertos = 2 },
            new CartaoRepoViewModel(new Repo { Id = "r3", Name = "Backup" },
                new RepoStatus { Branch = "main", Conflicted = 2 }, "#F0883E", main) { CiSituacao = "nenhum" },
            new CartaoRepoViewModel(new Repo { Id = "r4", Name = "BmIntegra" },
                new RepoStatus { Branch = "main", Ahead = 5 }, "#4F8CFF", main) { CiSituacao = "rodando" },
            new CartaoRepoViewModel(new Repo { Id = "r5", Name = "Compras" },
                new RepoStatus { Branch = "main" }, "#56D4BC", main),
            new CartaoRepoViewModel(new Repo { Id = "r6", Name = "Servidor antigo" },
                new RepoStatus { Error = "pasta não encontrada" }, "#5D6675", main) { CiSituacao = "nenhum" },
        };

        var vm = new PainelViewModel("Todos os repositórios", "", cartoes, main);
        vm.Subtitulo = vm.Resumo;

        // no painel geral cada grupo é uma seção, como o MainViewModel monta
        vm.Secoes = new System.Collections.ObjectModel.ObservableCollection<SecaoPainelViewModel>
        {
            new()
            {
                Titulo = "Manuais - BMSoft", Cor = "#DB4C9B", MostraTitulo = true,
                Cartoes = new System.Collections.ObjectModel.ObservableCollection<CartaoRepoViewModel>(
                    cartoes.Take(3)),
            },
            new()
            {
                Titulo = "Módulos", Cor = "#1F9D55", MostraTitulo = true,
                Cartoes = new System.Collections.ObjectModel.ObservableCollection<CartaoRepoViewModel>(
                    cartoes.Skip(3)),
            },
        };
        vm.Perfil = new PerfilViewModel("GFBmsoft", cartoes.Select(c => c.Repo).ToList())
        {
            Perfil = new GRepos.Services.Perfil
            {
                Login = "GFBmsoft",
                Nome = "Gabriel Ferreira",
                Bio = "Delphi, Golang, C — Bmsoft Sistemas",
                Empresa = "Bmsoft Sistemas",
                Local = "Rio do Sul, SC",
                RepositoriosPublicos = 3,
                Seguidores = 4,
                Estrelas = 13,
                Linguagens = new[] { "C#", "Pascal", "Lua" },
            },
            LinhasAdicionadas = 46_835,
            LinhasRemovidas = 9_770,
            LinhasContadas = true,
            OutrasContas = new[] { "bmsoftsistemas" },
            Dias = Enumerable.Range(0, 365).Select(i =>
            {
                var q = Math.Max(0, (i * 37 % 11) - 4 + (i > 250 ? 3 : 0));
                return new GRepos.Services.DiaContribuicao(
                    new DateTime(2026, 10, 1).AddDays(-364 + i), q, Math.Min(4, q));
            }).ToList(),
            TotalContribuicoes = 412,
        };

        // terceira seção recolhida, como o grupo fechado na árvore
        vm.Secoes.Add(new SecaoPainelViewModel
        {
            Titulo = "Arquivados", Cor = "#8957E5", MostraTitulo = true,
            Cartoes = new System.Collections.ObjectModel.ObservableCollection<CartaoRepoViewModel>(cartoes.Skip(1).Take(2)),
            Recolhido = true,
        });

        // a foto de verdade vem da API; aqui vale o ícone do app no lugar
        var raiz = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", ".."));
        vm.Perfil.Foto = new Avalonia.Media.Imaging.Bitmap(Path.Combine(raiz, "app", "Assets", "app.png"));

        Shot(new PainelView(), vm, "painel", 1000, 560);
    }

    /// <summary>Cartão do painel aberto, com a última execução e o passo a passo.</summary>
    [AvaloniaFact]
    public void Painel_com_cartao_aberto()
    {
        var main = new MainViewModel(new FakeDialogs());

        var aberto = new CartaoRepoViewModel(
            new Repo { Id = "r1", Name = "Financeiro" },
            new RepoStatus { Branch = "develop", Ahead = 1 }, "#DB4C9B", main) { CiSituacao = "rodando", CiWorkflow = "build" };

        var esteira = new EsteiraViewModel("", "develop", "", "Financeiro", visiveis: 1, somenteBranch: true);
        var execucao = new CiExecucaoViewModel
        {
            Execucao = new CiExecucao
            {
                Id = 1, Numero = 42, Situacao = "rodando", Workflow = "build",
                Titulo = "fix(boleto): ocultar campo do boleto online", Branch = "develop", Autor = "GFBmsoft",
                Criada = DateTime.UtcNow.AddMinutes(-3),
            },
        };
        esteira.Execucoes.Add(execucao);
        esteira.Selecionada = execucao;

        var job = new CiJob
        {
            Nome = "build", Situacao = "rodando", Duracao = TimeSpan.FromSeconds(95),
            Etapas =
            {
                new CiEtapa { Numero = 1, Nome = "Set up job", Situacao = "sucesso", Duracao = TimeSpan.FromSeconds(2) },
                new CiEtapa { Numero = 2, Nome = "Run actions/checkout@v7", Situacao = "sucesso", Duracao = TimeSpan.FromSeconds(6) },
                new CiEtapa { Numero = 3, Nome = "Testes", Situacao = "rodando" },
                new CiEtapa { Numero = 4, Nome = "Publicar", Situacao = "nenhum" },
            },
        };
        var jobVm = new CiJobViewModel { Job = job };
        jobVm.SincronizarEtapas(job);
        esteira.Jobs.Add(jobVm);
        esteira.Workflows.Add(new GitHubService.Workflow { Id = 1, Nome = "build", Ativo = true });
        esteira.WorkflowEscolhido = esteira.Workflows[0];

        aberto.Esteira = esteira;
        aberto.Expandido = true;

        var outros = new[]
        {
            new CartaoRepoViewModel(new Repo { Id = "r2", Name = "NFsPWS" }, new RepoStatus { Branch = "feat/qrcode" }, "#DB4C9B", main) { CiSituacao = "sucesso" },
            new CartaoRepoViewModel(new Repo { Id = "r3", Name = "Backup" }, new RepoStatus { Branch = "master" }, "#DB4C9B", main) { CiSituacao = "nenhum" },
            new CartaoRepoViewModel(new Repo { Id = "r4", Name = "SPED" }, new RepoStatus { Branch = "develop", Unstaged = 2, PendingFiles = 2 }, "#DB4C9B", main) { CiSituacao = "falha" },
        };

        var vm = new PainelViewModel("Módulos", "4 repositório(s)", new[] { aberto }.Concat(outros), main);

        Shot(new PainelView(), vm, "painel-aberto", 1000, 600);
    }

    /// <summary>README de verdade — o deste projeto — renderizado na aba Leia-me.</summary>
    [AvaloniaFact]
    public void Leiame_renderizado()
    {
        var raiz = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", ".."));
        var texto = Leiame.Ler(raiz);
        if (texto.Length == 0) return; // rodando fora da árvore do projeto

        var view = new ScrollViewer
        {
            Padding = new Avalonia.Thickness(22, 16),
            Content = new MarkdownView { Markdown = texto, MaxWidth = 820 },
        };

        Shot(view, new object(), "leiame", 900, 700);
    }

    [AvaloniaFact]
    public void Alteracoes_com_dois_arquivos_marcados()
    {
        var vm = new ChangesViewModel(new Repo { Id = "r1", Name = "Financeiro", Path = "." },
            new MainViewModel(new FakeDialogs()), split: true);
        vm.Staged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "src/Unit1.pas", Index = "M", Worktree = ".", Kind = ChangeKind.Tracked },
            Staged = true,
        });
        foreach (var nome in new[] { "src/Juros.pas", "src/Juros.dfm", "src/Parametros.pas" })
            vm.Unstaged.Add(new FileItemViewModel
            {
                Change = new FileChange { Path = nome, Index = ".", Worktree = "M", Kind = ChangeKind.Tracked },
            });

        var view = new ChangesView();
        view.Loaded += (_, _) =>
        {
            var lista = view.FindControl<ListBox>("ListaUnstaged")!;
            lista.SelectedItems!.Add(vm.Unstaged[0]);
            lista.SelectedItems.Add(vm.Unstaged[1]);
        };
        Shot(view, vm, "alteracoes-marcados");
    }

    /// <summary>
    /// EV13: com a coluna de arquivos no mínimo, os botões continuam dentro dela — os de
    /// "selecionados (N)", que são os mais largos, descem de linha.
    /// </summary>
    [AvaloniaFact]
    public void Alteracoes_com_a_coluna_de_arquivos_no_minimo()
    {
        var vm = new ChangesViewModel(new Repo { Id = "r1", Name = "Financeiro", Path = "" },
            new MainViewModel(new FakeDialogs()), split: true);
        vm.Staged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "src/Unit1.pas", Index = "M", Worktree = ".", Kind = ChangeKind.Tracked },
            Staged = true,
        });
        foreach (var nome in new[] { "src/Juros.pas", "src/Juros.dfm" })
            vm.Unstaged.Add(new FileItemViewModel
            {
                Change = new FileChange { Path = nome, Index = ".", Worktree = "M", Kind = ChangeKind.Tracked },
            });

        var view = new ChangesView();
        var grade = (Grid)view.Content!;
        grade.ColumnDefinitions[0].Width = new GridLength(60); // menos que o mínimo: vale o mínimo
        Shot(view, vm, "alteracoes-coluna-minima", 900, 700);

        Assert.Equal(260, grade.ColumnDefinitions[0].ActualWidth);

        // nenhum botão da coluna passa da borda dela
        var painel = grade.Children[0];
        foreach (var botao in painel.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
        {
            var direita = botao.TranslatePoint(new Point(botao.Bounds.Width, 0), painel)!.Value.X;
            Assert.True(direita <= painel.Bounds.Width + 0.5, $"\"{botao.Content}\" sai da coluna ({direita:0} > {painel.Bounds.Width:0})");
        }
    }

    [AvaloniaFact]
    public void Alteracoes_em_conflito_com_linhas_escolhidas()
    {
        var vm = new ChangesViewModel(new Repo { Id = "r1", Name = "Financeiro", Path = "." },
            new MainViewModel(new FakeDialogs()), split: true) { Operacao = GitService.Operacao.Merge };
        vm.Unstaged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "src/Juros.pas", Index = "U", Worktree = "U", Kind = ChangeKind.Conflict, Conflito = "UU" },
        });
        vm.Unstaged.Add(new FileItemViewModel
        {
            Change = new FileChange { Path = "src/Parametros.pas", Index = ".", Worktree = "M", Kind = ChangeKind.Tracked },
        });
        vm.Diff.Title = "src/Parametros.pas  (local)";
        vm.Diff.Load("diff --git a/a b/a\n--- a/a\n+++ b/a\n@@ -1,4 +1,5 @@\n unit Parametros;\n-const Taxa = 1;\n+const Taxa = 2;\n+const Multa = 3;\n interface\n",
            "Preparar bloco", _ => Task.CompletedTask);
        vm.Diff.AlternarLinha(vm.Diff.Rows.OfType<DiffSplitRow>().First(r => r.RightIsAdd), direita: true);
        Shot(new ChangesView(), vm, "alteracoes-conflito");
    }

    [AvaloniaFact]
    public void Historico_do_arquivo_na_aba_de_autoria()
    {
        var vm = new FileHistoryViewModel(new Repo { Path = "." }, "src/Juros.pas", blame: true, split: true);
        var linhas = new[] { "unit Juros;", "", "interface", "", "function Calcular(Valor: Currency): Currency;" };
        for (var i = 0; i < linhas.Length; i++)
            vm.Blame.Add(new BlameLineViewModel
            {
                Linha = new BlameLine
                {
                    Hash = i < 3 ? new string('a', 40) : i == 3 ? new string('b', 40) : new string('0', 40),
                    Linha = i + 1, Autor = i < 3 ? "Gabriel Ferreira" : "Ana Souza",
                    Quando = 1767261600, Assunto = "Estrutura inicial", Texto = linhas[i],
                },
                InicioDeBloco = i is 0 or 3 or 4,
                Par = i < 3 || i == 4,
            });
        ShotJanela(new FileHistoryWindow { DataContext = vm }, "historico-arquivo-blame", 1000, 400);
    }

    [AvaloniaFact]
    public void Rebase_interativo_com_acoes()
    {
        var vm = new RebaseViewModel(new Repo { Path = "." }, "abc") { Aviso = "Alguns destes commits já estão no remoto: depois de reorganizar, o envio vai exigir push forçado." };
        foreach (var (h, s) in new[] { ("d", "Corrige typo"), ("c", "Ajusta juros"), ("b", "WIP"), ("a", "Cálculo de juros compostos") })
            vm.Itens.Add(new RebaseItemViewModel(new ItemRebase { Hash = new string(h[0], 40), Assunto = s, Mensagem = s }, vm));
        vm.Itens[0].Acao = 3;
        vm.Itens[1].Acao = 1;
        vm.Itens[2].Acao = 4;
        ShotJanela(new RebaseWindow { DataContext = vm }, "rebase", 860, 480);
    }

    /// <summary>O diff no desenho do GitHub: unificado, com realce, contador e faixa de bloco.</summary>
    [AvaloniaFact]
    public void Diff_unificado_com_realce()
    {
        var vm = new DiffViewModel { Split = false, Externo = () => Task.CompletedTask, Recarregar = () => Task.CompletedTask };
        vm.CharWidth = 7.2;
        vm.ViewportWidth = 1000;
        vm.Title = "app/App.axaml.cs  (local)";
        vm.Load(string.Join("\n", new[]
        {
            "diff --git a/app/App.axaml.cs b/app/App.axaml.cs",
            "--- a/app/App.axaml.cs",
            "+++ b/app/App.axaml.cs",
            "@@ -1,6 +1,9 @@",
            " using Avalonia;",
            "+using Avalonia.Controls;",
            " using Avalonia.Controls.ApplicationLifetimes;",
            "+using Avalonia.Interactivity;",
            " using Avalonia.Markup.Xaml;",
            "+using GRepos.Services;",
            " using GRepos.Views;",
            " ",
            " namespace GRepos;",
            "@@ -11,6 +14,9 @@ public partial class App : Application",
            " ",
            "     public override void OnFrameworkInitializationCompleted()",
            "     {",
            "+        // toda janela, diálogos inclusive: no Windows 10 a barra de título não segue o tema sozinha",
            "+        Window.WindowOpenedEvent.AddClassHandler<Window>((janela, _) => BarraDeTitulo.Acompanhar(janela));",
            "+",
            "         if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)",
            "-            desktop.MainWindow = new MainWindow(\"antiga\", 42);",
            "+            desktop.MainWindow = new MainWindow();",
            " ",
        }) + "\n", "Preparar bloco", _ => Task.CompletedTask);

        Shot(new DiffView(), vm, "diff-unificado");
    }

    [AvaloniaFact]
    public void Historico_com_commit_selecionado()
    {
        Shot(new HistoryView(), HistoricoDeExemplo(), "historico");
    }

    [AvaloniaFact]
    public void Historico_no_tema_claro()
    {
        var app = Application.Current!;
        var antes = app.RequestedThemeVariant;
        app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        try
        {
            Shot(new HistoryView(), HistoricoDeExemplo(), "historico-claro");
        }
        finally
        {
            app.RequestedThemeVariant = antes;
        }
    }

    /// <summary>Sidebar com os grupos coloridos e os repositórios sob cada um.</summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task Sidebar_com_grupos_coloridos()
    {
        var home = Path.Combine(Path.GetTempPath(), "grepos-side-" + Path.GetRandomFileName());
        var raiz = Path.Combine(home, "repos");
        Directory.CreateDirectory(raiz);
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        try
        {
            var grupos = new[]
            {
                new Group { Id = "g1", Name = "Manuais - BMSoft", Color = "#DB4C9B" },
                new Group { Id = "g2", Name = "Módulos", Color = "#1F9D55" },
            };

            var ws = new Workspace { Groups = { grupos[0], grupos[1] } };
            var nomes = new (string Nome, string Grupo, string? Papel)[]
            {
                ("Backup", "g1", null),
                ("BmIntegra", "g1", null),
                ("Financeiro", "g1", null),
                ("Notas (DBISAM)", "g2", "origem"),
                ("Notas (MySQL)", "g2", "destino"),
            };

            var i = 0;
            foreach (var (nome, grupo, papel) in nomes)
            {
                var dir = Path.Combine(raiz, "r" + i++);
                Directory.CreateDirectory(dir);
                await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
                await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
                await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
                File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
                await GitService.RunAsync(dir, new[] { "add", "." });
                await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });
                if (nome == "BmIntegra") File.WriteAllText(Path.Combine(dir, "a.txt"), "dois\n");

                // pílulas de branch em cores diferentes: principal, feature e develop
                if (nome == "Financeiro") await GitService.RunAsync(dir, new[] { "checkout", "-q", "-b", "feat/cartaopix" });
                if (nome == "Backup") await GitService.RunAsync(dir, new[] { "checkout", "-q", "-b", "develop" });

                ws.Repos.Add(new Repo
                {
                    Id = "r" + i,
                    Name = nome,
                    Path = dir,
                    GroupId = grupo,
                    Role = papel,
                    PairKey = papel is null ? null : "Notas",
                });
            }

            WorkspaceStore.Save(ws);

            var janela = new MainWindow { Width = 300, Height = 420 };
            var principal = (MainViewModel)janela.DataContext!;
            await principal.InitAsync();
            janela.Show();

            // a varredura de status roda no timer: sem ela a foto sai sem pílula de
            // branch nem contadores
            foreach (var no in principal.Tree.OfType<RepoNode>().ToList())
                await principal.RefreshRepoAsync(no.Id);
            janela.Measure(new Size(300, 420));
            janela.Arrange(new Rect(0, 0, 300, 420));

            var dir2 = OutDir;
            if (dir2 is not null)
            {
                Directory.CreateDirectory(dir2);
                using (var frame = janela.CaptureRenderedFrame())
                    frame?.Save(Path.Combine(dir2, "sidebar-grupos.png"));

                // a pílula usa a cor com transparência: precisa aparecer também no claro
                var app = Application.Current!;
                var antes = app.RequestedThemeVariant;
                app.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                janela.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
                janela.Measure(new Size(300, 420));
                janela.Arrange(new Rect(0, 0, 300, 420));
                using (var frame = janela.CaptureRenderedFrame())
                    frame?.Save(Path.Combine(dir2, "sidebar-grupos-claro.png"));
                app.RequestedThemeVariant = antes;
            }
            janela.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", null);
            try
            {
                foreach (var f in Directory.EnumerateFiles(home, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(home, true);
            }
            catch (Exception) { /* pasta temporária */ }
        }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Tela_de_branches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-brshot-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
            await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
            File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "Feat: Melhoria na rotina de estoques" });
            foreach (var b in new[] { "develop", "feat/RefatoracaoRest", "feat/SincEstoques", "fix/PagPorOffSet", "fix/WatermarkProdutoNaoIntegrado" })
                await GitService.RunAsync(dir, new[] { "branch", b });

            var vm = new BranchesViewModel(
                new Repo { Id = "r1", Name = "BM2Maga", Path = dir },
                new MainViewModel(new FakeDialogs()));
            await vm.CarregarAsync();

            var janela = new BranchesWindow { DataContext = vm, Width = 720, Height = 560 };
            janela.Show();
            janela.Measure(new Size(720, 560));
            janela.Arrange(new Rect(0, 0, 720, 560));

            var outDir = OutDir;
            if (outDir is not null)
            {
                Directory.CreateDirectory(outDir);
                using var frame = janela.CaptureRenderedFrame();
                frame?.Save(Path.Combine(outDir, "branches.png"));
            }
            janela.Close();
        }
        finally
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
            catch (Exception) { /* pasta temporária */ }
        }
    }

    [AvaloniaFact]
    public void Adicionar_repositorio_clonando()
    {
        var main = new MainViewModel(new FakeDialogs());
        var janela = new AddRepoWindow(main, new FakeDialogs(), clonar: true);
        janela.Show();
        janela.GetVisualDescendants().OfType<TextBox>()
            .First(t => t.Watermark?.ToString()?.StartsWith("https://") == true)
            .Text = "https://github.com/bmsoftsistemas-mysql/bmOS.git";
        janela.Measure(new Size(460, 640));
        janela.Arrange(new Rect(0, 0, 460, 640));

        var dir = OutDir;
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            using var frame = janela.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "adicionar-clone.png"));
        }
        janela.Close();
    }

    [AvaloniaFact]
    public void Dialogo_de_configuracao_do_repositorio()
    {
        var main = new MainViewModel(new FakeDialogs());
        var repo = new Repo
        {
            Id = "r1",
            Name = "BM2Maga (DBISAM)",
            Path = Path.Combine("D:", "Projetos", "DBISAM", "BM2Maga"),
            PairKey = "BM2Maga",
            Role = "origem",
            RemoteTemplate = "https://{{user}}:{{token}}@github.com/bmsoftsistemas/BM2Maga.git",
        };

        var janela = new RepoConfigWindow(main, repo, new FakeDialogs());
        janela.Show();
        janela.Measure(new Size(460, 700));
        janela.Arrange(new Rect(0, 0, 460, 700));

        var dir = OutDir;
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            using var frame = janela.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "config-repo.png"));
        }
        janela.Close();
    }

    [AvaloniaFact]
    public void Dialogo_de_grupo_mostra_a_paleta()
    {
        var janela = new GroupWindow("Novo grupo", "Módulos BMSoft", GroupPalette.Cores[2]);
        janela.Show();
        janela.Measure(new Size(420, 400));
        janela.Arrange(new Rect(0, 0, 420, 400));

        // um botão por cor da paleta, e o escolhido vem marcado
        var swatches = janela.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Tag is string)
            .ToList();

        Assert.Equal(GroupPalette.Cores.Length, swatches.Count);
        Assert.Single(swatches, b => b.BorderThickness.Top > 0);
        Assert.Equal(GroupPalette.Cores[2], swatches.Single(b => b.BorderThickness.Top > 0).Tag);

        var dir = OutDir;
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            using var frame = janela.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "dialogo-grupo.png"));
        }
        janela.Close();
    }

    /// <summary>A sidebar espremida no mínimo ainda precisa mostrar os quatro ícones.</summary>
    [AvaloniaFact]
    public void Janela_com_a_sidebar_no_tamanho_minimo()
    {
        // workspace descartável: o teste não pode depender (nem escrever) no do usuário
        var home = Path.Combine(Path.GetTempPath(), "grepos-ui-" + Path.GetRandomFileName());
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);

        var window = new MainWindow { Width = 1000, Height = 640 };
        window.Show();
        window.Measure(new Size(1000, 640));
        window.Arrange(new Rect(0, 0, 1000, 640));

        var coluna = window.GetVisualDescendants()
            .OfType<Grid>()
            .First(g => g.ColumnDefinitions.Count == 3 && g.ColumnDefinitions[0].MinWidth > 100)
            .ColumnDefinitions[0];

        coluna.Width = new GridLength(coluna.MinWidth);
        window.Measure(new Size(1000, 640));
        window.Arrange(new Rect(0, 0, 1000, 640));

        // só os ícones da sidebar: são eles que a largura mínima precisa comportar.
        // Contar os da janela inteira pegava junto o do rodapé, que é menor de propósito.
        var sidebar = window.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("panel"));

        var botoes = sidebar.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("icon"))
            .ToList();

        Assert.Equal(4, botoes.Count);
        foreach (var b in botoes)
            Assert.True(b.Bounds.Width >= 28, $"ícone espremido: {b.Bounds.Width:F0}px");

        var dir = OutDir;
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(dir, "sidebar-minima.png"));
        }
        window.Close();
        Environment.SetEnvironmentVariable("GREPOS_HOME", null);
        try { Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
    }
}
