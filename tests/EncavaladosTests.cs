using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Procura controle encavalado numa janela já montada: dois controles visíveis ocupando o
/// mesmo lugar, ou um que passa da borda e sai cortado. Nada disso dá erro: um Grid com
/// colunas Auto que somam mais que a janela deixa a coluna * sem espaço, e o que mora nela
/// é desenhado por cima dos vizinhos (EV18, rodapé da Esteira).
/// </summary>
internal static class Encavalados
{
    private sealed record Peca(Control Controle, Rect Inteiro, Rect Visivel, bool Rolavel);

    public static List<string> Procurar(Visual raiz)
    {
        var area = new Rect(raiz.Bounds.Size);
        var pecas = new List<Peca>();

        foreach (var c in raiz.GetVisualDescendants().OfType<Control>())
        {
            if (!EhPeca(c) || !c.IsEffectivelyVisible || c.Bounds.Width < 1 || c.Bounds.Height < 1) continue;
            if (c.GetVisualAncestors().Any(a => a is ScrollBar || (a is Control p && EhPeca(p)))) continue;
            if (Opacidade(c, raiz) < 0.01) continue;

            var inteiro = Tinta(c, raiz);
            if (inteiro is null) continue;

            var visivel = inteiro.Value.Intersect(area);
            var rolavel = false;
            foreach (var a in c.GetVisualAncestors())
            {
                if (a == raiz) break;
                if (a is ScrollContentPresenter) rolavel = true;
                if (a.ClipToBounds && RetanguloDe(a, raiz) is { } corte) visivel = visivel.Intersect(corte);
            }
            pecas.Add(new Peca(c, inteiro.Value, visivel, rolavel));
        }

        var problemas = new List<string>();

        // fora de rolagem, o controle inteiro tem que aparecer: cortado é o "Fechar" da EV18
        foreach (var p in pecas.Where(p => !p.Rolavel))
        {
            var r = p.Inteiro;
            var v = p.Visivel;
            if (v.Width < r.Width - 1.5 || v.Height < r.Height - 1.5)
                problemas.Add($"cortado: {Descrever(p.Controle)} {Mostrar(r)}, aparece {Mostrar(v)}");
        }

        for (var i = 0; i < pecas.Count; i++)
        for (var j = i + 1; j < pecas.Count; j++)
        {
            var comum = pecas[i].Visivel.Intersect(pecas[j].Visivel);
            if (comum.Width > 1.5 && comum.Height > 1.5)
                problemas.Add($"encavalado: {Descrever(pecas[i].Controle)} {Mostrar(pecas[i].Inteiro)} × " +
                              $"{Descrever(pecas[j].Controle)} {Mostrar(pecas[j].Inteiro)}");
        }

        return problemas;
    }

    private static bool EhPeca(Control c) => c is Button or TextBox or ComboBox or TextBlock;

    private static double Opacidade(Visual v, Visual raiz)
    {
        var o = v.Opacity;
        foreach (var a in v.GetVisualAncestors())
        {
            if (a == raiz) break;
            o *= a.Opacity;
        }
        return o;
    }

    private static Rect? RetanguloDe(Visual v, Visual raiz) =>
        v.TransformToVisual(raiz) is { } m ? new Rect(v.Bounds.Size).TransformToAABB(m) : null;

    /// <summary>
    /// Onde o controle desenha. Para texto é a área das letras, não a do TextBlock: um título
    /// esticado na coluna inteira com um botão encostado à direita, na mesma célula, só
    /// encavala se as letras chegarem até o botão.
    /// </summary>
    private static Rect? Tinta(Control c, Visual raiz)
    {
        if (c.TransformToVisual(raiz) is not { } m) return null;
        if (c is not TextBlock t || string.IsNullOrEmpty(t.Text) && t.Inlines is not { Count: > 0 })
            return c is TextBlock ? null : new Rect(c.Bounds.Size).TransformToAABB(m);

        var layout = t.TextLayout;
        var largura = layout.Width;
        var util = c.Bounds.Width - t.Padding.Left - t.Padding.Right;
        var x = t.Padding.Left + t.TextAlignment switch
        {
            TextAlignment.Right or TextAlignment.End => Math.Max(0, util - largura),
            TextAlignment.Center => Math.Max(0, (util - largura) / 2),
            _ => 0,
        };
        var ink = new Rect(x, t.Padding.Top, largura, Math.Min(layout.Height, c.Bounds.Height));
        return ink.TransformToAABB(m);
    }

    private static string Descrever(Control c)
    {
        var texto = c switch
        {
            TextBlock t => t.Text,
            TextBox t => string.IsNullOrEmpty(t.Text) ? t.Watermark?.ToString() : t.Text,
            GRepos.Controls.ToolButton t => t.Label,
            ContentControl { Content: string s } => s,
            ContentControl cc => cc.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text,
            _ => null,
        } ?? "";
        if (texto.Length > 40) texto = texto[..40] + "…";
        return $"{c.GetType().Name}\"{texto}\"";
    }

    private static string Mostrar(Rect r) => $"[{r.X:0},{r.Y:0} {r.Width:0}×{r.Height:0}]";
}

/// <summary>
/// Cada janela no tamanho com que abre e no mínimo que ela aceita, com as listas populadas:
/// nada encavalado, nada cortado na borda.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class EncavaladosTests
{
    private static void Montar(Window janela, double largura, double altura)
    {
        for (var i = 0; i < 2; i++)
        {
            janela.Measure(new Size(largura, double.IsNaN(altura) ? double.PositiveInfinity : altura));
            var h = double.IsNaN(altura) ? Math.Min(janela.MaxHeight, janela.DesiredSize.Height) : altura;
            janela.Arrange(new Rect(0, 0, largura, h));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static List<string> Conferir(Window janela, string nome, bool minimo = false, Action<Window>? depois = null)
    {
        try
        {
            var largura = minimo && janela.MinWidth > 0 ? janela.MinWidth : janela.Width;
            var altura = minimo && janela.MinHeight > 0 ? janela.MinHeight : janela.Height;
            janela.Width = largura;
            if (!double.IsNaN(altura)) janela.Height = altura;
            janela.Show();
            Montar(janela, largura, altura);
            if (depois is not null)
            {
                depois(janela);
                Montar(janela, largura, altura);
            }

            return Encavalados.Procurar(janela)
                .Select(p => $"{nome} {largura:0}×{janela.Bounds.Height:0}: {p}").ToList();
        }
        finally
        {
            janela.Close();
            // a Esteira de teste liga a consulta periódica, para o ritmo aparecer no rodapé
            (janela.DataContext as EsteiraViewModel)?.Parar();
        }
    }

    private static List<string> ConferirSecoes(Func<Window> criar, string nome, bool minimo)
    {
        var problemas = new List<string>();
        var secoes = 0;
        problemas.AddRange(Conferir(criar(), nome, minimo, j =>
            secoes = j.GetVisualDescendants().OfType<ListBox>().First().ItemCount));
        for (var i = 1; i < secoes; i++)
        {
            var indice = i;
            string secao = "";
            problemas.AddRange(Conferir(criar(), nome, minimo, j =>
            {
                var lista = j.GetVisualDescendants().OfType<ListBox>().First();
                lista.SelectedIndex = indice;
                secao = lista.SelectedItem as string ?? "";
            }).Select(p => p.Replace(nome + " ", $"{nome} → {secao} ")));
        }
        return problemas;
    }

    private static IEnumerable<(string Nome, Func<Window> Criar)> Janelas()
    {
        yield return ("Esteira", () => new EsteiraWindow { DataContext = UiSmokeTests.EsteiraPopulada() });
        yield return ("Segredos", () =>
        {
            var vm = new SegredosViewModel("GFBmsoft/GRepos", "GFBmsoft", "GRepos");
            vm.Aplicar(
                new[] { new GRepos.Services.ItemDeActions { Nome = "DEPLOY_TOKEN", Atualizado = DateTime.UtcNow } },
                new[] { new GRepos.Services.ItemDeActions { Nome = "AMBIENTE", Valor = "producao" } });
            return new SegredosWindow { DataContext = vm };
        });
        yield return ("Segredos (editando)", () =>
        {
            var vm = new SegredosViewModel("GFBmsoft/GRepos", "GFBmsoft", "GRepos");
            vm.NovaVariavelCommand.Execute(null);
            vm.FormNome = "nome com espaço";
            return new SegredosWindow { DataContext = vm };
        });
        yield return ("Issues", () => new IssuesWindow { DataContext = UiSmokeTests.IssuesPopulado() });
        yield return ("Issues (nova)", () =>
        {
            var vm = UiSmokeTests.IssuesPopulado();
            vm.MontarFormEtiquetas(new[] { "bug" });
            vm.NovaCommand.Execute(null);
            return new IssuesWindow { DataContext = vm };
        });
        yield return ("Issues (editando)", () =>
        {
            var vm = UiSmokeTests.IssuesPopulado();
            vm.EditarIssueCommand.Execute(null);
            return new IssuesWindow { DataContext = vm };
        });
        yield return ("Pull requests", () => new PullRequestsWindow { DataContext = UiSmokeTests.PullRequestsPopulado() });
        foreach (var aba in new[] { 0, 1, 2 })
            yield return ($"Pull requests (aba {aba})", () =>
            {
                var vm = UiSmokeTests.PullRequestsComAbas();
                vm.Aba = aba;
                return new PullRequestsWindow { DataContext = vm };
            });
        yield return ("Pull requests (novo)", () =>
        {
            var vm = UiSmokeTests.PullRequestsPopulado();
            vm.NovoCommand.Execute(null);
            return new PullRequestsWindow { DataContext = vm };
        });
        yield return ("Releases", () => new ReleasesWindow { DataContext = Releases() });
        yield return ("Releases (editando)", () =>
        {
            var vm = Releases();
            vm.EditarReleaseCommand.Execute(null);
            return new ReleasesWindow { DataContext = vm };
        });
        yield return ("Comparar", () => new CompararWindow { DataContext = UiSmokeTests.CompararPopulado() });
        yield return ("Lote", () => new LoteWindow { DataContext = UiSmokeTests.LotePopulado() });
        yield return ("Pastas de trabalho", () => new WorktreesWindow { DataContext = UiSmokeTests.WorktreesPopulado() });
        yield return ("Conflito", () => new ConflitoWindow { DataContext = UiSmokeTests.ConflitoPopulado() });
        yield return ("Paleta", () => new PaletaWindow { DataContext = UiSmokeTests.PaletaPopulada() });
        yield return ("Novidades", () => new NovidadesWindow { DataContext = Novidades() });
        yield return ("Publicar no GitHub", () =>
        {
            var vm = new PublicarRepoViewModel(@"C:\repos\Notas Fiscais", "GFBmsoft");
            vm.Donos.Add("BMSoft");
            vm.Nome = "nome inválido";
            return new PublicarRepoWindow { DataContext = vm };
        });
        yield return ("Rebase", () =>
        {
            var vm = new RebaseViewModel(new Repo { Path = "." }, "abc")
            {
                Aviso = "Alguns destes commits já estão no remoto: depois de reorganizar, o envio vai exigir push forçado.",
            };
            foreach (var (h, s) in new[] { ("d", "Corrige typo"), ("c", "Ajusta juros"), ("b", "WIP"), ("a", "Cálculo de juros compostos") })
                vm.Itens.Add(new RebaseItemViewModel(new GRepos.Services.ItemRebase { Hash = new string(h[0], 40), Assunto = s, Mensagem = s }, vm));
            vm.Itens[0].Acao = 3;
            vm.Itens[1].Acao = 1;
            vm.Itens[2].Acao = 4;
            return new RebaseWindow { DataContext = vm };
        });
        foreach (var blame in new[] { false, true })
            yield return (blame ? "Histórico do arquivo (autoria)" : "Histórico do arquivo", () =>
            {
                var vm = new FileHistoryViewModel(new Repo { Path = "." }, "src/Juros.pas", blame, split: true);
                vm.Commits.Add(new FileCommitViewModel
                {
                    Commit = new Commit { Hash = "abc1234567", Subject = "Ajusta juros", Author = "Ana", Date = "2026-01-01T10:00:00Z" },
                    Caminho = "src/Juros.pas", CaminhoAtual = "src/Juros.pas",
                });
                vm.Blame.Add(new BlameLineViewModel
                {
                    Linha = new BlameLine { Hash = new string('a', 40), Linha = 1, Autor = "Gabriel Ferreira", Quando = 1767261600, Assunto = "Estrutura inicial", Texto = "unit Juros;" },
                    InicioDeBloco = true, Par = true,
                });
                return new FileHistoryWindow { DataContext = vm };
            });
        yield return ("Branches", () =>
        {
            var janela = new BranchesWindow(new MainViewModel(new FakeDialogs()), new Repo { Id = "r1", Name = "Financeiro", Path = "" });
            var vm = (BranchesViewModel)janela.DataContext!;
            BranchItemViewModel Item(string nome, bool remota = false, bool head = false) => new()
            {
                Branch = new Branch { Name = nome, IsRemote = remota, IsHead = head, Upstream = remota ? null : "origin/" + nome, Subject = "assunto" },
                Tipo = GRepos.Services.GitFlow.Classificar(GRepos.Services.GitFlow.SemRemoto(nome, remota)),
            };
            vm.GruposLocais = BranchesViewModel.Agrupar(new[] { Item("develop", head: true), Item("master"), Item("feat/cartaopix"), Item("fix/FNOBS") });
            vm.GruposRemotos = BranchesViewModel.Agrupar(new[] { Item("origin/develop", true), Item("origin/fix/6329_email", true) });
            return janela;
        });

        // diálogos: a altura acompanha o conteúdo, só a largura é fixa
        yield return ("Confirmar", () => new ConfirmWindow("Descartar alterações",
            "Descartar as alterações de 3 arquivo(s)? Isso não pode ser desfeito: o que não foi commitado nem guardado se perde."));
        yield return ("Pergunta", () => new PromptWindow("Nova branch", "Nome da branch", "feat/boleto-online"));
        yield return ("Grupo", () => new GroupWindow("Novo subgrupo de Fiscal", "Notas", "#1F9D55", "fis",
            GrupoArvore.EmOrdem(new[] { new Group { Id = "db", Name = "DBISAM" }, new Group { Id = "fis", Name = "Fiscal", ParentId = "db" } })));
        foreach (var clonar in new[] { false, true })
            yield return (clonar ? "Adicionar (clonar)" : "Adicionar", () =>
            {
                var main = new MainViewModel(new FakeDialogs());
                main.CreateGroup("Financeiro");
                return new AddRepoWindow(main, new FakeDialogs(), clonar);
            });
        yield return ("Configurar repositório", () =>
        {
            var main = new MainViewModel(new FakeDialogs());
            var repo = new Repo { Id = "r1", Name = "Financeiro", Path = "", PairKey = "Financeiro", Role = "origem" };
            return new RepoConfigWindow(main, repo, new FakeDialogs());
        });
        foreach (var inicializado in new[] { false, true })
            yield return ("git-flow", () => new GitFlowWindow(new GRepos.Services.GitFlowConfig { Inicializado = inicializado }));
    }

    private static ReleasesViewModel Releases()
    {
        var vm = new ReleasesViewModel("GFBmsoft/GRepos", "GFBmsoft", "GRepos");
        vm.Aplicar(new[]
        {
            new GRepos.Services.Release
            {
                Id = 2, Tag = "1.0.0.47", Nome = "GRepos 1.0.0.47", Rascunho = true, Notas = "## Novidades\n- log da esteira",
                Arquivos = { new GRepos.Services.ReleaseAsset { Id = 20, Nome = "GRepos-1.0.0.47.exe", Tamanho = 29_000_000 } },
            },
            new GRepos.Services.Release { Id = 1, Tag = "1.0.0.46", PreRelease = true, Publicada = DateTime.UtcNow },
        });
        return vm;
    }

    private static NovidadesViewModel Novidades()
    {
        var vm = new NovidadesViewModel();
        vm.Versoes.Add(new VersaoViewModel
        {
            Release = new GRepos.Services.Release
            {
                Tag = "1.0.0.9", Nome = "GRepos 1.0.0.9", Publicada = DateTime.UtcNow.AddHours(-2),
                Notas = "## Mudanças\n\n- Corrige o **modo portátil**\n- Ícone no rodapé",
            },
            EmUso = true,
        });
        vm.Selecionada = vm.Versoes[0];
        return vm;
    }

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

    /// <summary>
    /// EV18: na Esteira, o recado do ritmo passava por baixo de "Segredos e variáveis…" e
    /// o "Fechar" saía cortado, já no tamanho com que a janela abre.
    /// </summary>
    [AvaloniaFact]
    public void Janelas_e_dialogos_nao_encavalam_no_tamanho_padrao_nem_no_minimo()
    {
        var problemas = new List<string>();
        foreach (var (nome, criar) in Janelas())
        {
            problemas.AddRange(Conferir(criar(), nome));
            problemas.AddRange(Conferir(criar(), nome + " (mínima)", minimo: true));
        }
        foreach (var minimo in new[] { false, true })
            problemas.AddRange(ConferirSecoes(() => new SettingsWindow(new MainViewModel(new FakeDialogs())),
                minimo ? "Preferências (mínima)" : "Preferências", minimo));
        Assert.True(problemas.Count == 0, string.Join("\n", problemas));
    }

    private static Task<string> Git(string dir, params string[] args) => GitService.RunAsync(dir, args);

    /// <summary>
    /// A janela principal com um par de repositórios de verdade: cada aba, o painel e os
    /// diálogos que leem o repositório (stash e ignorados). A barra é a mais larga que
    /// existe (nome e branch compridos, todos os botões do GitHub): com folga ela tem as
    /// legendas, e da padrão para baixo fica só com os ícones, sem cortar nenhum.
    /// </summary>
    [AvaloniaFact]
    public async Task Tela_principal_nao_encavala_no_tamanho_padrao_nem_no_minimo()
    {
        var home = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "grepos-encav-" + System.IO.Path.GetRandomFileName());
        var origem = System.IO.Path.Combine(home, "Financeiro");
        var destino = System.IO.Path.Combine(home, "Financeiro MySQL");
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            foreach (var dir in new[] { origem, destino })
            {
                System.IO.Directory.CreateDirectory(dir);
                await Git(dir, "init", "-q", "-b", "main");
                await Git(dir, "config", "user.email", "t@t");
                await Git(dir, "config", "user.name", "Teste");
                foreach (var (arquivo, msg) in new[] { ("Juros.pas", "feat: cálculo de juros compostos com um assunto bem comprido para forçar o corte"), ("Boleto.pas", "fix(boleto): ocultar campo do boleto online") })
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, arquivo), "unit X;\n" + msg + "\n");
                    await Git(dir, "add", "-A");
                    await Git(dir, "commit", "-qm", msg);
                }
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(origem, "Juros.pas"), "unit X;\nguardado\n");
            await Git(origem, "stash", "push", "-m", "ajuste de juros pela metade");
            System.IO.File.WriteAllText(System.IO.Path.Combine(origem, "Juros.pas"), "unit X;\npreparado\n");
            await Git(origem, "add", "Juros.pas");
            System.IO.File.WriteAllText(System.IO.Path.Combine(origem, "Boleto.pas"), "unit X;\nlocal\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(origem, "Parametros de configuracao do modulo.pas"), "novo\n");

            // nome de repositório e de branch compridos: são os textos da barra que variam
            await Git(origem, "checkout", "-q", "-b", "feat/boleto-online-com-pix-e-juros-compostos-no-vencimento");

            var ws = new Workspace { Groups = { new Group { Id = "mod", Name = "Módulos", Color = "#2F7BE8" } } };
            ws.Repos.Add(new Repo { Id = "fin", Name = "Financeiro - Contas a Receber e a Pagar com Boletos", Path = origem, GroupId = "mod", PairKey = "Financeiro", Role = "origem" });
            ws.Repos.Add(new Repo { Id = "finmy", Name = "Financeiro MySQL", Path = destino, GroupId = "mod", PairKey = "Financeiro", Role = "destino" });
            WorkspaceStore.Save(ws);

            var problemas = new List<string>();
            MainViewModel? principal = null;
            foreach (var tamanho in new[] { "larga", "padrão", "mínima" })
            {
                var janela = new MainWindow();
                var vm = principal = (MainViewModel)janela.DataContext!;
                await vm.InitAsync();

                var minimo = tamanho == "mínima";
                var (largura, altura) = tamanho switch
                {
                    "larga" => (1920.0, 1000.0),
                    "padrão" => (1440.0, 900.0), // o Width/Height do MainWindow.axaml
                    _ => (janela.MinWidth, janela.MinHeight),
                };
                janela.Width = largura;
                janela.Height = altura;
                janela.Show();
                if (minimo)
                {
                    // sidebar escolhida larga, e a janela estreitada depois: a sidebar tem que
                    // ceder, senão a barra de ferramentas sai cortada; e voltar ao alargar,
                    // senão o próximo arraste guarda a largura errada como preferência
                    var sidebar = janela.FindControl<Grid>("Layout")!.ColumnDefinitions[0];
                    vm.SetSidebarWidth(500);
                    Montar(janela, 1440, altura);
                    Montar(janela, largura, altura);
                    Assert.True(sidebar.ActualWidth < 500, $"sidebar com {sidebar.ActualWidth:0}");
                    Montar(janela, 1440, altura);
                    Assert.Equal(500, sidebar.ActualWidth);
                    Montar(janela, largura, altura);
                }
                void Olhar(string onde)
                {
                    Montar(janela, largura, altura);
                    problemas.AddRange(Encavalados.Procurar(janela).Select(p => $"Principal {largura:0}×{altura:0} → {onde}: {p}"));
                }
                Olhar("sem seleção");

                vm.SelecionarRepositorio("fin");
                await vm.RefreshRepoAsync("fin");
                await Espera.Ate(() => vm.Changes is { } c && c.Unstaged.Count > 0);

                // a barra inteira, como num repositório do GitHub: PRs, Issues, Releases,
                // Esteira e Remoto. O estado entra direto, sem ir à API; antes, espera a
                // consulta do remoto (sem origin) terminar, que ela zera tudo isso ao começar
                await Espera.Ate(() => vm.SemRemoto);
                vm.RemoteWebUrl = "https://github.com/bmsoft/financeiro";
                vm.TemGitHub = true;
                vm.CiSituacao = "cancelado";
                vm.PrsAbertos = 12;
                Olhar("Alterações");

                // com folga a barra tem as legendas; na mínima, só os ícones
                var barra = janela.FindControl<Border>("Barra")!;
                if (tamanho == "larga") Assert.DoesNotContain(BarraCompacta.Classe, barra.Classes);
                if (minimo) Assert.Contains(BarraCompacta.Classe, barra.Classes);

                // a tela real sai um pouco mais larga que o headless: sem folga aqui, a barra
                // "cabe" no teste e corta o último botão no monitor de verdade
                if (minimo) Assert.True(vm.VaoDaBarra >= 24, $"folga de {vm.VaoDaBarra:0} px na barra compacta");
                if (Environment.GetEnvironmentVariable("GREPOS_SHOTS") is { } pasta)
                {
                    System.IO.Directory.CreateDirectory(pasta);
                    using var quadro = janela.CaptureRenderedFrame();
                    quadro?.Save(System.IO.Path.Combine(pasta, $"principal-{tamanho}.png"));
                }

                vm.SelectedTab = 1;
                await Espera.Ate(() => vm.History is { HasDetail: true });
                Olhar("Histórico");

                vm.SelectedTab = 2;
                await vm.Pair!.LoadAsync();
                Olhar("Par");

                vm.MostrarPainel("mod");
                Olhar("Painel do grupo");

                vm.MostrarPainel(null);
                Olhar("Painel geral");

                janela.Close();
            }

            var repo = ws.Repos[0];
            await Ignorados.IgnorarAsync(origem, new[] { "Boleto.pas" }, Array.Empty<string>());
            foreach (var minimo in new[] { false, true })
            {
                var stash = new StashWindow(principal!, repo);
                stash.Show();
                await Espera.Ate(() => stash.GetVisualDescendants().OfType<ListBox>().Any(l => l.ItemCount > 0));
                problemas.AddRange(Conferir(stash, minimo ? "Stash (mínima)" : "Stash", minimo));

                var ignorados = new IgnoradosWindow(principal!, repo);
                ignorados.Show();
                await ignorados.Carga;
                problemas.AddRange(Conferir(ignorados, minimo ? "Ignorados (mínima)" : "Ignorados", minimo));
            }

            Assert.True(problemas.Count == 0, string.Join("\n", problemas));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { System.IO.Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }

    /// <summary>
    /// Compacta, a barra perde a legenda da Esteira, e a cor não separa cancelada de pulada:
    /// o estado por extenso tem que estar na dica, com ou sem detalhe da execução.
    /// </summary>
    [Theory]
    [InlineData("falha", "Esteira quebrou")]
    [InlineData("cancelado", "Esteira cancelada")]
    [InlineData("pulado", "Esteira pulada")]
    public void A_dica_da_Esteira_diz_o_estado_mesmo_sem_detalhe(string situacao, string estado)
    {
        var vm = new MainViewModel(new FakeDialogs()) { CiSituacao = situacao };
        Assert.Equal("", vm.CiDetalhe);
        Assert.StartsWith(estado, vm.CiTooltip);
    }

    /// <summary>
    /// A barra escolhe o modo depois do layout, e a escolha muda o layout. Se o modo
    /// compacto acha que a barra cheia cabe e o cheio acha que não, ela alterna até o
    /// Avalonia desistir com "Infinite layout loop detected", o que derruba o app. Era uma
    /// faixa de 6 px que mudava com a branch e com a Esteira, e a 1440 (a largura padrão)
    /// pegava feat/cartaopix.
    /// </summary>
    [AvaloniaFact]
    public async Task Barra_nao_alterna_entre_os_modos_em_nenhuma_largura()
    {
        var home = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "grepos-barra-" + System.IO.Path.GetRandomFileName());
        var dir = System.IO.Path.Combine(home, "Financeiro");
        var antes = Environment.GetEnvironmentVariable("GREPOS_HOME");
        Environment.SetEnvironmentVariable("GREPOS_HOME", home);
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            await Git(dir, "init", "-q", "-b", "main");
            var ws = new Workspace { Groups = { new Group { Id = "mod", Name = "Módulos", Color = "#2F7BE8" } } };
            ws.Repos.Add(new Repo { Id = "fin", Name = "Financeiro", Path = dir, GroupId = "mod" });
            WorkspaceStore.Save(ws);

            var janela = new MainWindow();
            var vm = (MainViewModel)janela.DataContext!;
            await vm.InitAsync();
            janela.Width = 1440;
            janela.Height = 900;
            janela.Show();
            vm.SelecionarRepositorio("fin");
            var barra = janela.FindControl<Border>("Barra")!;

            bool Passo(double largura)
            {
                janela.Width = largura;
                janela.Measure(new Size(largura, 900));
                janela.Arrange(new Rect(0, 0, largura, 900));
                janela.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return barra.Classes.Contains(BarraCompacta.Classe);
            }

            var subindo = Enumerable.Range(1300, 200).ToList();
            foreach (var branch in new[] { "main", "feat/cartaopix" })
            {
                await Git(dir, "checkout", "-q", "-B", branch);
                await vm.RefreshRepoAsync("fin");
                await Espera.Ate(() => vm.BranchCaption == Rotulos.Branch(branch) && vm.SemRemoto);
                vm.RemoteWebUrl = "https://github.com/bmsoft/financeiro";
                vm.TemGitHub = true;

                foreach (var ci in new[] { "sucesso", "falha" })
                {
                    vm.CiSituacao = ci;
                    var modos = new HashSet<bool>();
                    foreach (var largura in subindo.Concat(Enumerable.Reverse(subindo)))
                    {
                        var modo = Passo(largura);
                        Assert.True(modo == Passo(largura), $"{branch}, Esteira {ci}: mudou de modo sozinha a {largura}");
                        modos.Add(modo);
                    }
                    Assert.True(modos.Count == 2, $"{branch}, Esteira {ci}: a varredura não cruzou o limite");
                }
            }

            // a legenda que muda com a barra compacta: a largura cheia guardada envelhece
            foreach (var ci in new[] { "sucesso", "falha", "rodando", "cancelado", "pulado" })
            {
                vm.CiSituacao = ci;
                var modo = Passo(1300);
                Assert.True(modo == Passo(1300), $"Esteira {ci}: mudou de modo sozinha");
            }

            vm.CiSituacao = "";
            janela.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { System.IO.Directory.Delete(home, true); } catch (Exception) { /* pasta temporária */ }
        }
    }
}
