using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;

namespace GRepos.Views;

/// <summary>Base dos diálogos: moldura, título e rodapé de botões no mesmo padrão.</summary>
public abstract class DialogWindow : Window
{
    protected DialogWindow(string title, double width = 460)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
    }

    protected static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Margin = new Thickness(0, 0, 0, 4),
        TextWrapping = TextWrapping.Wrap,
        Classes = { "faint" },
    };

    protected static StackPanel Field(string label, Control input) => new()
    {
        Margin = new Thickness(0, 0, 0, 10),
        Children = { Label(label), input },
    };

    /// <summary>Moldura com a borda do tema (segue claro/escuro).</summary>
    protected static Border Framed(Control child)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = child,
        };
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Border"));
        return border;
    }

    /// <summary>
    /// Ação secundária dentro do corpo do diálogo: pequeno e só com a borda. Os botões
    /// do rodapé continuam grandes — são a ação principal da janela.
    /// </summary>
    protected static Button BtnDiscreto(string texto, bool perigo = false)
    {
        var b = new Button
        {
            Content = texto,
            MinWidth = 0,
            Padding = new Thickness(9, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1), // "tiny" tira a borda; aqui ela volta
        };
        b.Classes.Add("tiny");
        if (perigo) b.Classes.Add("danger");
        b.Bind(Button.BorderBrushProperty, new DynamicResourceExtension("Border"));
        return b;
    }

    protected static Button Btn(string text, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) b.Classes.Add("primary");
        return b;
    }

    /// <summary>Respiro entre o conteúdo e a barra de rolagem, para não ficarem colados.</summary>
    protected const double FolgaDaBarra = 8;

    /// <summary>Título de seção, para separar assuntos dentro do mesmo diálogo.</summary>
    protected static TextBlock Secao(string texto, bool primeira = false) => new()
    {
        Text = texto.ToUpperInvariant(),
        Classes = { "sectionTitle" },
        Margin = new Thickness(0, primeira ? 0 : 14, 0, 8),
    };

    /// <param name="rodapeCentralizado">Botões no meio, em vez de encostados à direita.</param>
    /// <param name="corpoRolante">
    /// Corpo em área de rolagem própria, com título e rodapé fixos. Combina com janela
    /// redimensionável: esticar a janela mostra mais campos em vez de rolar mais.
    /// </param>
    protected void Compose(string heading, IEnumerable<Control> body, IEnumerable<Control> footer,
        bool rodapeCentralizado = false, bool corpoRolante = false)
    {
        var titulo = new TextBlock
        {
            Text = heading,
            FontSize = 14.5,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        };

        var corpo = new StackPanel { Spacing = 0 };
        foreach (var c in body) corpo.Children.Add(c);

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = rodapeCentralizado ? HorizontalAlignment.Center : HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
        };
        foreach (var c in footer) foot.Children.Add(c);

        if (corpoRolante)
        {
            // AllowAutoHide desligado é o que resolve o corte: com ele ligado a barra
            // é um overlay que aparece por cima do conteúdo ao passar o mouse. Desligada,
            // ela ocupa lugar no layout e o conteúdo é medido já sem esse espaço.
            var rolagem = new ScrollViewer
            {
                AllowAutoHide = false,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, FolgaDaBarra, 0),
                Content = corpo,
            };

            var grade = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            Grid.SetRow(titulo, 0);
            Grid.SetRow(rolagem, 1);
            Grid.SetRow(foot, 2);
            grade.Children.Add(titulo);
            grade.Children.Add(rolagem);
            grade.Children.Add(foot);

            Content = new Border { Padding = new Thickness(16), Child = grade };
            return;
        }

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(titulo);
        panel.Children.Add(corpo);
        panel.Children.Add(foot);

        Content = new Border { Padding = new Thickness(16), Child = panel };
    }
}

// ------------------------------------------------------------------ confirmar

public sealed class ConfirmWindow : DialogWindow
{
    public ConfirmWindow(string title, string message) : base(title, 420)
    {
        var no = Btn("Cancelar");
        var yes = Btn("Confirmar", true);
        no.Click += (_, _) => Close(false);
        yes.Click += (_, _) => Close(true);

        Compose(title,
            new Control[]
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) },
            },
            new[] { no, yes },
            rodapeCentralizado: true);
    }
}

// --------------------------------------------------------------------- texto

public sealed class PromptWindow : DialogWindow
{
    public PromptWindow(string title, string label, string initial) : base(title, 420)
    {
        var input = new TextBox { Text = initial };
        var cancel = Btn("Cancelar");
        var ok = Btn("Confirmar", true);

        cancel.Click += (_, _) => Close(null);
        ok.Click += (_, _) => Close(input.Text);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) Close(input.Text);
        };

        Compose(title, new Control[] { Field(label, input) }, new[] { cancel, ok });
        Opened += (_, _) => input.Focus();
    }
}

// ------------------------------------------------------------------- grupo

/// <summary>Nome e cor do grupo. Devolve null quando o usuário cancela.</summary>
public sealed class GroupWindow : DialogWindow
{
    public GroupWindow(string titulo, string nome, string cor) : base(titulo, 420)
    {
        var escolhida = GroupPalette.Normalizar(cor) ?? GroupPalette.Padrao;

        var nomeBox = new TextBox { Text = nome, Watermark = "Ex.: Módulos BMSoft" };
        var hexBox = new TextBox { Text = escolhida, Width = 96 };

        var amostra = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.Parse(escolhida)),
            Margin = new Thickness(8, 0, 0, 0),
        };

        var swatches = new WrapPanel();
        var botoes = new List<Button>();

        void Selecionar(string valor, bool atualizaHex)
        {
            escolhida = valor;
            amostra.Background = new SolidColorBrush(Color.Parse(valor));
            if (atualizaHex) hexBox.Text = valor;
            foreach (var b in botoes)
                b.BorderThickness = new Thickness(
                    b.Tag as string == valor ? 3 : 0);
        }

        foreach (var c in GroupPalette.Cores)
        {
            var b = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.Parse(c)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(c == escolhida ? 3 : 0),
                Padding = new Thickness(0),
                Tag = c,
            };
            b.Click += (_, _) => Selecionar(c, true);
            botoes.Add(b);
            swatches.Children.Add(b);
        }

        hexBox.LostFocus += (_, _) =>
        {
            var v = GroupPalette.Normalizar(hexBox.Text);
            if (v is null) hexBox.Text = escolhida; // texto inválido volta ao que valia
            else Selecionar(v, false);
        };

        var linhaCor = new StackPanel { Orientation = Orientation.Horizontal };
        linhaCor.Children.Add(hexBox);
        linhaCor.Children.Add(amostra);

        var cancelar = Btn("Cancelar");
        var salvar = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        salvar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nomeBox.Text)) return;
            Close(new GroupResult(nomeBox.Text!.Trim(), escolhida));
        };

        Compose(titulo,
            new Control[]
            {
                Field("Nome do grupo", nomeBox),
                Field("Cor", swatches),
                Field("…ou informe o código da cor", linhaCor),
            },
            new[] { cancelar, salvar });

        Opened += (_, _) => nomeBox.Focus();
    }
}

public sealed record GroupResult(string Nome, string Cor);

// ------------------------------------------------------------ adicionar repo

/// <summary>
/// Adiciona uma pasta que já é repositório ou clona um do GitHub. O clone já sai com o
/// link no padrão do GRepos (só o usuário na URL, token no Credential Manager).
/// </summary>
public sealed class AddRepoWindow : DialogWindow
{
    public AddRepoWindow(MainViewModel main, IDialogService dialogs, bool clonar = false) : base("Adicionar repositório")
    {
        var existente = new RadioButton { Content = "Pasta no computador", GroupName = "modo", IsChecked = !clonar };
        var clone = new RadioButton { Content = "Clonar do GitHub", GroupName = "modo", IsChecked = clonar, Margin = new Thickness(16, 0, 0, 0) };
        var modos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        modos.Children.Add(existente);
        modos.Children.Add(clone);

        var path = new TextBox { Watermark = @"D:\Projetos\..." };
        var name = new TextBox();
        var group = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var newGroup = new TextBox { Watermark = "Ex.: Financeiro" };

        var groups = main.Groups.ToList();
        group.ItemsSource = new[] { "Sem grupo" }.Concat(groups.Select(g => g.Name)).ToList();
        group.SelectedIndex = 0;

        var browse = Btn("Procurar");
        browse.Click += async (_, _) =>
        {
            var chosen = await dialogs.PickFolderAsync("Selecione o repositório");
            if (chosen is null) return;
            path.Text = chosen;
            try
            {
                name.Text = await GitService.InspectPathAsync(chosen);
            }
            catch (Exception ex)
            {
                main.Notify(ex.Message, true);
                name.Text = "";
            }
        };

        // ---------------------------------------------------------------- clonar

        var link = new TextBox { Watermark = "https://github.com/organizacao/repositorio.git" };

        // sugere onde o último clone foi feito; sem histórico, ao lado do último repositório
        var pastaPai = new TextBox
        {
            Text = main.Settings.PastaDeClone is { Length: > 0 } p ? p
                : main.Repos.LastOrDefault() is { } ultimo ? System.IO.Path.GetDirectoryName(ultimo.Path) ?? "" : "",
            Watermark = @"D:\Projetos",
        };
        var nomePasta = new TextBox { Watermark = "nome da pasta (sai do link)" };

        // o nome segue o link até o usuário mexer nele
        var nomeEditado = false;
        var mudandoNome = false;
        link.TextChanged += (_, _) =>
        {
            if (nomeEditado) return;
            mudandoNome = true;
            nomePasta.Text = RemotoConfig.NomeDoLink(link.Text);
            mudandoNome = false;
        };
        nomePasta.TextChanged += (_, _) => { if (!mudandoNome) nomeEditado = true; };

        var procurarPai = Btn("Procurar");
        procurarPai.Click += async (_, _) =>
        {
            var chosen = await dialogs.PickFolderAsync("Pasta onde o repositório será criado");
            if (chosen is not null) pastaPai.Text = chosen;
        };

        var principal = main.Settings.GithubUser ?? "";
        var contas = main.Contas.ToList();
        var conta = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        conta.ItemsSource = new[] { principal.Length > 0 ? $"Automática (principal: {principal})" : "Automática" }
            .Concat(contas).ToList();
        conta.SelectedIndex = 0;
        string ContaEscolhida() => conta.SelectedIndex > 0 ? contas[conta.SelectedIndex - 1] : "";

        var dicaClone = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "faint" },
            Text = "O link fica gravado só com o usuário da conta; o token é o dela, guardado no Windows. " +
                   "Se o link colado tiver um token, ele não é gravado.",
        };

        var situacao = new TextBlock
        {
            FontSize = 11.5,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };

        // ----------------------------------------------------------------- layout

        Grid LinhaComBotao(Control campo, Button botao)
        {
            var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            botao.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(botao, 1);
            linha.Children.Add(campo);
            linha.Children.Add(botao);
            return linha;
        }

        var camposExistente = new StackPanel
        {
            Children =
            {
                Field("Pasta do repositório", LinhaComBotao(path, browse)),
                Field("Nome exibido", name),
            },
        };
        var camposClone = new StackPanel
        {
            Children =
            {
                Field("Link do repositório", link),
                Field("Criar dentro da pasta", LinhaComBotao(pastaPai, procurarPai)),
                Field("Nome da pasta (também é o nome exibido)", nomePasta),
                Field("Conta do GitHub", conta),
                dicaClone,
            },
            Margin = new Thickness(0, 0, 0, 10),
        };

        var cancel = Btn("Cancelar");
        var add = Btn("Adicionar", true);

        void TrocarModo()
        {
            var c = clone.IsChecked == true;
            camposExistente.IsVisible = !c;
            camposClone.IsVisible = c;
            add.Content = c ? "Clonar" : "Adicionar";
            situacao.IsVisible = false;
        }
        existente.IsCheckedChanged += (_, _) => TrocarModo();
        clone.IsCheckedChanged += (_, _) => TrocarModo();
        TrocarModo();

        string? GrupoEscolhido()
        {
            if (!string.IsNullOrWhiteSpace(newGroup.Text)) return main.CreateGroup(newGroup.Text!.Trim());
            return group.SelectedIndex > 0 ? groups[group.SelectedIndex - 1].Id : null;
        }

        void Mostrar(string texto, bool erro)
        {
            situacao.Text = texto;
            situacao.IsVisible = true;
            if (erro) situacao.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("Red"));
            else situacao.ClearValue(TextBlock.ForegroundProperty);
        }

        cancel.Click += (_, _) => Close();
        add.Click += async (_, _) =>
        {
            if (clone.IsChecked != true)
            {
                if (string.IsNullOrWhiteSpace(path.Text) || string.IsNullOrWhiteSpace(name.Text)) return;
                main.AddRepository(path.Text!.Trim(), name.Text!.Trim(), GrupoEscolhido());
                Close();
                return;
            }

            var pai = (pastaPai.Text ?? "").Trim();
            var nome = (nomePasta.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(link.Text) || pai.Length == 0 || nome.Length == 0)
            {
                Mostrar("Informe o link, a pasta e o nome.", true);
                return;
            }

            var destino = System.IO.Path.Combine(pai, nome);
            var usuario = ContaEscolhida() is { Length: > 0 } c ? c : principal;

            add.IsEnabled = cancel.IsEnabled = false;
            Mostrar("Clonando… repositório grande pode levar alguns minutos.", false);
            try
            {
                await RemotoConfig.ClonarAsync(link.Text!, destino, usuario);
            }
            catch (Exception ex)
            {
                Mostrar(ex.Message, true);
                add.IsEnabled = cancel.IsEnabled = true;
                return;
            }

            main.Settings.PastaDeClone = pai;
            main.AddRepository(destino, nome, GrupoEscolhido(), ContaEscolhida());
            Close();
        };

        Compose("Adicionar repositório",
            new Control[]
            {
                modos,
                camposExistente,
                camposClone,
                Field("Grupo", group),
                Field("…ou criar um grupo novo", newGroup),
                situacao,
            },
            new[] { cancel, add });
    }
}

// --------------------------------------------------------- configurar repo

public sealed class RepoConfigWindow : DialogWindow
{
    public RepoConfigWindow(MainViewModel main, Repo repo, IDialogService dialogs) : base("Configurar Repositório")
    {
        var name = new TextBox { Text = repo.Name, Watermark = "como aparece na lista" };
        var pathBox = new TextBox { Text = repo.Path, IsReadOnly = true };

        var dicaNome = new TextBlock
        {
            Text = "Pasta no disco: " + System.IO.Path.GetFileName(repo.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Classes = { "faint" },
        };
        var group = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var pairKey = new TextBox { Text = repo.PairKey ?? "", Watermark = "Ex.: Financeiro" };
        var role = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "origem", "destino" },
            SelectedIndex = repo.Role == "destino" ? 1 : 0,
        };

        var groups = main.Groups.ToList();
        group.ItemsSource = new[] { "Sem grupo" }.Concat(groups.Select(g => g.Name)).ToList();
        group.SelectedIndex = repo.GroupId is null ? 0 : groups.FindIndex(g => g.Id == repo.GroupId) + 1;

        var known = main.Repos.Where(r => !string.IsNullOrEmpty(r.PairKey))
                              .Select(r => r.PairKey!)
                              .Distinct()
                              .ToList();
        var explicacaoPapel = new TextBlock
        {
            Text = "O papel só vale quando há um par: marque “origem” no repositório de onde as alterações " +
                   "saem (ex.: o DBISAM) e “destino” no que as recebe (ex.: o MySQL). Isso define a ordem em " +
                   "que os dois aparecem na lista e qual fica à esquerda na aba Par. Sem par, deixe em branco.",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "faint" },
        };

        var hint = new TextBlock
        {
            Text = known.Count > 0 ? "Pares já usados: " + string.Join(", ", known) : "",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = known.Count > 0,
            Classes = { "faint" },
        };


        // ------------------------------------------------------ link do remoto

        // O link leva só o usuário; o token fica no Credential Manager, na conta dele.
        // Sem modelo salvo, o campo abre com o remoto atual nesse padrão, e salvar já
        // leva ao git — antes eram dois botões que ninguém sabia quando usar.
        var urlBox = new TextBox
        {
            Text = repo.RemoteTemplate ?? "",
            Watermark = "https://{{user}}@github.com/owner/repo.git",
        };

        // conta do repositório: "automática" segue a URL e, sem usuário nela, a principal
        var principalConta = main.Settings.GithubUser ?? "";
        var contasCadastradas = main.Contas.ToList();
        var conta = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        conta.ItemsSource = new[] { principalConta.Length > 0 ? $"Automática (principal: {principalConta})" : "Automática" }
            .Concat(contasCadastradas).ToList();
        conta.SelectedIndex = repo.Conta is { Length: > 0 } atualConta
            ? contasCadastradas.FindIndex(c => string.Equals(c, atualConta, StringComparison.OrdinalIgnoreCase)) + 1
            : 0;
        if (conta.SelectedIndex < 0) conta.SelectedIndex = 0;

        string ContaEscolhida() => conta.SelectedIndex > 0 ? contasCadastradas[conta.SelectedIndex - 1] : "";

        // {{user}} vira a conta escolhida; na automática, a principal
        var usuarioConta = ContaEscolhida() is { Length: > 0 } escolhida ? escolhida : principalConta;
        var remotoAtual = "";

        var dicaUrl = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "faint" },
        };

        var autenticacao = new TextBlock
        {
            FontSize = 11.5,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "Verificando a autenticação…",
        };

        // o que o salvar vai mudar no git; some quando o link já está certo
        var previa = new TextBlock
        {
            FontSize = 11.5,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        };
        previa.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("Orange"));

        void AtualizarPrevia()
        {
            dicaUrl.Text = "{{user}} vira " +
                (usuarioConta.Length > 0 ? $"“{usuarioConta}”" : "o usuário de Preferências → Autenticação") +
                ". O token não vai no link.";
            var texto = RemotoConfig.Previa(remotoAtual, urlBox.Text, usuarioConta);
            previa.Text = texto ?? "";
            previa.IsVisible = texto is not null;
        }

        async System.Threading.Tasks.Task MostrarAutenticacaoAsync()
        {
            if (usuarioConta.Length == 0)
            {
                autenticacao.Text = "✗ Nenhuma conta do GitHub em Preferências → Autenticação.";
                return;
            }

            var temToken = !string.IsNullOrEmpty(await GitHubService.TokenDoUsuarioAsync(usuarioConta));
            autenticacao.Text = temToken
                ? $"✓ O git usa o token da conta “{usuarioConta}”, guardado no Windows."
                : $"✗ Nenhum token salvo para “{usuarioConta}”. Salve em Preferências → Autenticação.";
        }

        conta.SelectionChanged += async (_, _) =>
        {
            usuarioConta = ContaEscolhida() is { Length: > 0 } c ? c : principalConta;
            AtualizarPrevia();
            await MostrarAutenticacaoAsync();
        };
        urlBox.TextChanged += (_, _) => AtualizarPrevia();

        var remove = Btn("Remover da lista");
        remove.Classes.Add("danger");
        var cancel = Btn("Cancelar");
        var save = Btn("Salvar", true);

        remove.Click += async (_, _) =>
        {
            var ok = await dialogs.ConfirmAsync("Remover repositório",
                $"Remover \"{repo.Name}\" do GRepos?\n\nO repositório em disco não é apagado.");
            if (!ok) return;
            main.RemoveRepository(repo);
            Close();
        };
        cancel.Click += (_, _) => Close();
        save.Click += async (_, _) =>
        {
            try
            {
                var mudou = await RemotoConfig.AplicarAsync(repo.Path, urlBox.Text, usuarioConta);
                if (mudou is not null) main.Notify(mudou);
            }
            catch (Exception ex)
            {
                previa.Text = ex.Message; // fica aberto para o usuário corrigir
                previa.IsVisible = true;
                return;
            }

            var gid = group.SelectedIndex > 0 ? groups[group.SelectedIndex - 1].Id : null;
            main.UpdateRepository(repo,
                string.IsNullOrWhiteSpace(name.Text) ? repo.Name : name.Text!.Trim(),
                gid,
                pairKey.Text,
                role.SelectedItem as string,
                urlBox.Text,
                ContaEscolhida());
            Close();
        };

        Opened += async (_, _) =>
        {
            remotoAtual = await GitService.RemoteUrlAsync(repo.Path);
            if (string.IsNullOrWhiteSpace(urlBox.Text) && usuarioConta.Length > 0)
                urlBox.Text = UrlTemplate.Sugerir(remotoAtual);
            AtualizarPrevia();
            await MostrarAutenticacaoAsync();
        };

        Compose("Configurar Repositório",
            new Control[]
            {
                Secao("Geral", primeira: true),
                Field("Nome de exibição", name),
                dicaNome,
                Field("Caminho", pathBox),
                Field("Grupo", group),
                Secao("Remoto"),
                Field("Conta do GitHub", conta),
                Label("Link do remoto"),
                urlBox,
                dicaUrl,
                autenticacao,
                previa,
                Secao("Par Origem × Destino"),
                Field("Chave do par (mesmo módulo em outro banco) — use a mesma nos dois repositórios", pairKey),
                hint,
                Field("Papel neste par", role),
                explicacaoPapel,
            },
            new Control[] { remove, cancel, save },
            rodapeCentralizado: true,
            corpoRolante: true);
    }
}

// ---------------------------------------------------------------- ajustes

public sealed class SettingsWindow : DialogWindow
{
    private static readonly string[] Accents =
        { "#4F8CFF", "#3FB950", "#F0883E", "#D2A8FF", "#E3B341", "#56D4BC" };

    public SettingsWindow(MainViewModel main) : base("Preferências", 520)
    {
        // esta é a única tela longa: em vez de caber num tamanho fixo, ela abre num
        // tamanho confortável e o usuário estica se quiser ver mais campos de uma vez
        SizeToContent = SizeToContent.Manual;
        CanResize = true;
        Height = 700;
        MinWidth = 460;
        MinHeight = 420;
        MaxHeight = double.PositiveInfinity;

        var s = main.Settings;

        var theme = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Escuro", "Claro" },
            SelectedIndex = s.Theme == "light" ? 1 : 0,
        };
        var density = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Compacta", "Confortável" },
            SelectedIndex = s.Density == "confortavel" ? 1 : 0,
        };
        var estiloArvore = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Minimalista", "Pílulas" },
            SelectedIndex = s.ArvoreMinimalista ? 0 : 1,
        };
        var abaInicial = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Alterações", "Histórico" },
            SelectedIndex = s.DefaultTab == "historico" ? 1 : 0,
        };
        var refresh = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = s.AutoRefreshSeconds, Increment = 10 };
        var esteiras = new NumericUpDown { Minimum = 1, Maximum = 50, Value = s.EsteirasVisiveis, Increment = 1 };
        var avisarAtualizacao = new CheckBox
        {
            Content = "Avisar quando sair uma versão nova",
            IsChecked = s.AvisarAtualizacao,
        };

        // Sem isto, quem abriu o app minutos antes de sair uma release ficava 24h sem
        // saber: a consulta é uma por dia e não havia como pedir outra.
        var procurar = BtnDiscreto("Procurar agora");
        var resultadoBusca = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12), // senão "Grupos" cola no texto
            Classes = { "faint" },
            Text = MainViewModel.VersaoEmUso.Length > 0
                ? "Consultado uma vez por dia."
                : "Build local não tem versão para comparar — o aviso fica desligado.",
        };

        procurar.Click += async (_, _) =>
        {
            try
            {
                procurar.IsEnabled = false;
                resultadoBusca.Text = "Consultando…";
                await main.VerificarAtualizacaoAsync(forcar: true);
                resultadoBusca.Text = main.TemAtualizacao
                    ? $"Versão {main.AtualizacaoTag} disponível — o aviso está na barra de status."
                    : "Você já está na versão mais recente.";
            }
            catch (Exception ex)
            {
                resultadoBusca.Text = ex.Message;
            }
            finally
            {
                procurar.IsEnabled = true;
            }
        };

        var linhaAtualizacao = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 6, 0, 0),
            Children = { procurar },
        };

        // ------------------------------------------------ onde ficam as configurações

        var portatil = new CheckBox
        {
            Content = "Guardar as configurações junto do executável (modo portátil)",
            IsChecked = WorkspaceStore.Portatil,
        };
        var ondeFica = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
            Classes = { "faint" },
        };

        // Erro em linha separada: antes ele substituía o caminho, e aí a informação
        // sumia da tela e não voltava mais — nem ao desmarcar.
        var erroPortatil = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12),
            Foreground = new SolidColorBrush(Color.Parse("#E5534B")),
            IsVisible = false,
        };

        // O caminho é sempre relido de quem manda nele, nunca deduzido do que a ação
        // devolveu: assim a tela mostra onde o arquivo está de fato, dê certo ou errado.
        void MostrarOndeFica()
        {
            ondeFica.Text = WorkspaceStore.FilePath;
            portatil.IsChecked = WorkspaceStore.Portatil;
        }

        MostrarOndeFica();

        portatil.IsCheckedChanged += (_, _) =>
        {
            var querPortatil = portatil.IsChecked == true;
            if (querPortatil == WorkspaceStore.Portatil)
            {
                MostrarOndeFica();
                return;
            }

            try
            {
                WorkspaceStore.MoverPara(querPortatil);
                erroPortatil.IsVisible = false;
            }
            catch (Exception ex)
            {
                erroPortatil.Text = ex.Message;
                erroPortatil.IsVisible = true;
            }

            MostrarOndeFica();
        };
        var logLimit = new NumericUpDown { Minimum = 50, Maximum = 5000, Value = s.LogLimit, Increment = 50 };

        // ------------------------------------------------------------ terminal

        var gitBash = new TextBox
        {
            Text = s.GitBashPath,
            Watermark = @"vazio procura sozinho — ex.: C:\Program Files\Git",
        };
        var procurarGit = BtnDiscreto("Procurar…");
        procurarGit.Margin = new Thickness(6, 0, 0, 0);
        var gitBashUsado = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Classes = { "faint" },
        };

        // mostra o executável que o botão Terminal vai abrir, para o erro aparecer aqui
        // e não só na hora de clicar
        void MostrarGitBash()
        {
            var achou = GitBash.Localizar(gitBash.Text);
            gitBashUsado.Text = achou is not null
                ? "Abre: " + achou
                : string.IsNullOrWhiteSpace(gitBash.Text)
                    ? "Git Bash não encontrado automaticamente — informe a pasta de instalação do Git."
                    : "Nenhum git-bash.exe nesse caminho.";
            gitBashUsado.Foreground = achou is null ? new SolidColorBrush(Color.Parse("#E5534B")) : null;
        }

        MostrarGitBash();
        gitBash.TextChanged += (_, _) => MostrarGitBash();
        procurarGit.Click += async (_, _) =>
        {
            var pasta = await main.Dialogos.PickFolderAsync("Pasta de instalação do Git");
            if (pasta is not null) gitBash.Text = pasta;
        };

        var linhaGitBash = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(procurarGit, 1);
        linhaGitBash.Children.Add(gitBash);
        linhaGitBash.Children.Add(procurarGit);

        var accent = s.Accent;
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var color in Accents)
        {
            var dot = new Button
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.Parse(color)),
                BorderThickness = new Thickness(color == accent ? 2 : 0),
                BorderBrush = Brushes.White,
                Padding = new Thickness(0),
            };
            dot.Click += (_, _) =>
            {
                accent = color;
                foreach (var child in swatches.Children.OfType<Button>())
                    child.BorderThickness = new Thickness(
                        child.Background is SolidColorBrush b && b.Color == Color.Parse(accent) ? 2 : 0);
            };
            swatches.Children.Add(dot);
        }

        var groupsPanel = new StackPanel { Spacing = 5 };
        foreach (var g in main.Groups.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };

            var ponto = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse(GroupPalette.Normalizar(g.Color) ?? GroupPalette.Padrao)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0),
            };
            var nome = new TextBlock { Text = g.Name, VerticalAlignment = VerticalAlignment.Center };
            var editar = BtnDiscreto("Editar");
            var del = BtnDiscreto("Excluir", perigo: true);
            editar.Margin = new Thickness(6, 0, 0, 0);
            del.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(nome, 1);
            Grid.SetColumn(editar, 2);
            Grid.SetColumn(del, 3);

            editar.Click += async (_, _) =>
            {
                await main.EditGroupAsync(g.Id);
                var atual = main.Groups.FirstOrDefault(x => x.Id == g.Id);
                if (atual is null) return;
                nome.Text = atual.Name;
                ponto.Background = new SolidColorBrush(
                    Color.Parse(GroupPalette.Normalizar(atual.Color) ?? GroupPalette.Padrao));
            };
            del.Click += (_, _) =>
            {
                main.RemoveGroup(g.Id);
                groupsPanel.Children.Remove(row);
            };

            row.Children.Add(ponto);
            row.Children.Add(nome);
            row.Children.Add(editar);
            row.Children.Add(del);
            groupsPanel.Children.Add(row);
        }

        // ---------------------------------------------------- autenticação

        // várias contas: cada uma com o próprio token no gerenciador de credenciais, que
        // guarda por usuário. A principal vale para quem não escolhe outra.
        var contas = main.Contas.ToList();
        var principal = s.GithubUser;

        var usuario = new TextBox
        {
            Text = contas.Count == 0 ? s.GithubUser : "",
            Watermark = contas.Count == 0 ? "seu usuário no GitHub" : "conta a adicionar, ou uma da lista para trocar o token",
        };
        var token = new TextBox
        {
            PasswordChar = '●',
            Watermark = "cole aqui o personal access token",
        };
        var contasPanel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 10) };
        var situacao = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Classes = { "faint" },
            Text = "O token é guardado no Gerenciador de Credenciais do Windows, junto com o do git. " +
                   "Só o nome de usuário fica no arquivo de configuração.",
        };

        var salvarToken = BtnDiscreto("Salvar conta e token");
        var testar = BtnDiscreto("Testar");

        // o resultado precisa saltar aos olhos: antes ele virava mais uma linha
        // cinza no meio do texto de ajuda e passava despercebido
        async void ComAviso(Func<Task<string>> acao)
        {
            try
            {
                situacao.Classes.Set("faint", true);
                situacao.FontWeight = FontWeight.Normal;
                situacao.Foreground = null;
                situacao.Text = "Aguarde…";

                var recado = await acao();

                situacao.Classes.Set("faint", false);
                situacao.FontWeight = FontWeight.SemiBold;
                situacao.Foreground = new SolidColorBrush(Color.Parse("#3FB950"));
                situacao.Text = "✓ " + recado;
            }
            catch (Exception ex)
            {
                situacao.Classes.Set("faint", false);
                situacao.FontWeight = FontWeight.SemiBold;
                situacao.Foreground = new SolidColorBrush(Color.Parse("#E5534B"));
                situacao.Text = "✕ " + ex.Message;
            }
        }

        // uma linha por conta: situação do token, nome, e as ações dela
        async void MontarContas()
        {
            contasPanel.Children.Clear();
            if (contas.Count == 0)
            {
                contasPanel.Children.Add(new TextBlock
                {
                    Text = "Nenhuma conta ainda. Informe usuário e token abaixo.",
                    FontSize = 11.5,
                    Classes = { "faint" },
                });
                return;
            }

            foreach (var conta in contas.ToList())
            {
                var ehPrincipal = string.Equals(conta, principal, StringComparison.OrdinalIgnoreCase);
                var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*,Auto,Auto"), Height = 26 };

                var marca = new TextBlock { Text = "…", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                var nome = new TextBlock
                {
                    Text = ehPrincipal ? conta + "  · principal" : conta,
                    FontWeight = ehPrincipal ? FontWeight.SemiBold : FontWeight.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                var tornar = BtnDiscreto("Tornar principal");
                tornar.Margin = new Thickness(0, 0, 6, 0);
                tornar.IsVisible = !ehPrincipal;
                tornar.Click += (_, _) => { principal = conta; MontarContas(); };

                var tirar = BtnDiscreto("Remover", perigo: true);
                tirar.Click += async (_, _) =>
                {
                    var ok = await main.Dialogos.ConfirmAsync("Remover conta",
                        $"Remover a conta “{conta}” do GRepos e o token dela do Gerenciador de Credenciais?\n\n" +
                        "Repositórios que usavam esta conta voltam para a automática.");
                    if (!ok) return;

                    ComAviso(async () =>
                    {
                        await GitHubService.RemoverCredencialAsync(conta);
                        contas.RemoveAll(c => string.Equals(c, conta, StringComparison.OrdinalIgnoreCase));
                        if (ehPrincipal) principal = contas.FirstOrDefault() ?? "";
                        MontarContas();
                        return $"Conta {conta} removida.";
                    });
                };

                Grid.SetColumn(nome, 1);
                Grid.SetColumn(tornar, 2);
                Grid.SetColumn(tirar, 3);
                linha.Children.Add(marca);
                linha.Children.Add(nome);
                linha.Children.Add(tornar);
                linha.Children.Add(tirar);
                contasPanel.Children.Add(linha);

                // o token de cada conta é conferido no gerenciador, sem abrir janela de login
                try
                {
                    var tem = await GitHubService.TemCredencialAsync(conta);
                    marca.Text = tem ? "✓" : "✕";
                    marca.Foreground = new SolidColorBrush(Color.Parse(tem ? "#3FB950" : "#E5534B"));
                    ToolTip.SetTip(linha, tem ? "Token guardado no Gerenciador de Credenciais"
                                              : "Sem token guardado — salve abaixo");
                }
                catch (Exception)
                {
                    marca.Text = "?";
                }
            }
        }

        salvarToken.Click += (_, _) => ComAviso(async () =>
        {
            var conta = (usuario.Text ?? "").Trim();
            await GitHubService.SalvarCredencialAsync(conta, token.Text ?? "");
            token.Text = "";

            if (!contas.Contains(conta, StringComparer.OrdinalIgnoreCase)) contas.Add(conta);
            if (principal.Length == 0) principal = conta;
            usuario.Text = "";
            MontarContas();
            return "Token salvo para " + conta;
        });

        testar.Click += (_, _) => ComAviso(async () =>
        {
            var conta = await GitHubService.TestarAsync(usuario.Text ?? "", token.Text);

            // testou com o campo vazio e o GitHub disse quem é: preenche o usuário,
            // que é justamente o que o git precisa para achar a credencial depois
            if (string.IsNullOrWhiteSpace(usuario.Text))
            {
                var login = conta.Split(' ')[0];
                if (login.Length > 0) usuario.Text = login;
            }
            return "Conectado como " + conta;
        });

        var acoesToken = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        acoesToken.Children.Add(salvarToken);
        acoesToken.Children.Add(testar);

        MontarContas();

        // ------------------------------------------ gerenciador de credenciais

        var helperTexto = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Classes = { "faint" },
            Text = "Verificando…",
        };
        var configurarHelper = BtnDiscreto("Usar o Gerenciador de Credenciais do Windows");

        async Task AtualizarHelperAsync()
        {
            try
            {
                var atual = await GitHubService.HelperAsync();
                var conta = principal; // a principal é a que vale sem escolha
                var temCred = conta.Length > 0 && await GitHubService.TemCredencialAsync(conta);

                if (atual.Length == 0)
                {
                    helperTexto.Text = "Nenhum gerenciador configurado no git — a autenticação é " +
                                       "pedida a cada envio. Configure para guardar uma vez só.";
                    configurarHelper.IsEnabled = true;
                }
                else
                {
                    helperTexto.Text = $"Gerenciador em uso: {atual}. " + (temCred
                        ? $"Credencial encontrada para {conta}: enviar não pede login."
                        : conta.Length == 0
                            ? "Cadastre uma conta acima para o git achar a credencial certa."
                            : $"Nenhuma credencial guardada para {conta} ainda — salve o token acima.");
                    configurarHelper.IsEnabled = !atual.Contains(GitHubService.HelperPadrao, StringComparison.Ordinal);
                }
            }
            catch (Exception ex)
            {
                helperTexto.Text = "Não foi possível consultar o git: " + ex.Message;
            }
        }

        configurarHelper.Click += async (_, _) =>
        {
            try
            {
                helperTexto.Text = "Configurando…";
                await GitHubService.ConfigurarHelperAsync();
                await AtualizarHelperAsync();
            }
            catch (Exception ex)
            {
                helperTexto.Text = ex.Message;
            }
        };

        Opened += async (_, _) => await AtualizarHelperAsync();

        var close = Btn("Fechar");
        var save = Btn("Salvar", true);
        close.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            // conta digitada e não salva também entra: o token dela pode já estar no
            // gerenciador, guardado pelo próprio git
            var digitada = (usuario.Text ?? "").Trim();
            if (digitada.Length > 0 && !contas.Contains(digitada, StringComparer.OrdinalIgnoreCase))
                contas.Add(digitada);
            if (principal.Length == 0) principal = contas.FirstOrDefault() ?? "";
            main.SetContas(contas, principal);
            main.SetAvisarAtualizacao(avisarAtualizacao.IsChecked == true);
            main.SetArvoreMinimalista(estiloArvore.SelectedIndex == 0);
            main.SetEsteirasVisiveis((int)(esteiras.Value ?? 6));
            main.SetGitBashPath(gitBash.Text ?? "");
            main.ApplySettings(
                theme.SelectedIndex == 1 ? "light" : "dark",
                accent,
                density.SelectedIndex == 1 ? "confortavel" : "compacta",
                (int)(refresh.Value ?? 60),
                (int)(logLimit.Value ?? 300),
                abaInicial.SelectedIndex == 1 ? "historico" : "alteracoes");
            Close();
        };

        var body = new List<Control>
        {
            Secao("Customização", primeira: true),
            Field("Tema", theme),
            Field("Cor de destaque", swatches),
            Field("Densidade das listas", density),
            Field("Estilo da árvore", estiloArvore),
            Field("Abrir o repositório em", abaInicial),
            Field("Atualizar status automaticamente (segundos, 0 desliga)", refresh),
            Field("Commits carregados no histórico", logLimit),
            Field("Execuções mostradas na esteira", esteiras),
            avisarAtualizacao,
            linhaAtualizacao,
            resultadoBusca,
            portatil,
            ondeFica,
            erroPortatil,
        };
        if (groupsPanel.Children.Count > 0) body.Add(Field("Grupos", groupsPanel));

        body.Add(Secao("Terminal"));
        body.Add(Field("Git Bash (pasta do Git ou caminho do git-bash.exe)", linhaGitBash));
        body.Add(gitBashUsado);

        body.Add(Secao("Autenticação"));
        body.Add(Label("Contas do GitHub. A principal vale para os repositórios que não escolhem outra em Configurar repositório."));
        body.Add(contasPanel);
        body.Add(Field("Usuário", usuario));
        body.Add(Field("Token de acesso pessoal", token));
        body.Add(acoesToken);
        body.Add(situacao);
        body.Add(Secao("Gerenciador de credenciais"));
        body.Add(helperTexto);
        body.Add(new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { configurarHelper } });

        Compose("Preferências", body, new[] { close, save },
            rodapeCentralizado: true, corpoRolante: true);
    }
}

// ------------------------------------------------------------------ stash

public sealed class StashWindow : DialogWindow
{
    private readonly MainViewModel _main;
    private readonly Repo _repo;
    private readonly ListBox _list = new() { MaxHeight = 260 };
    private readonly TextBlock _empty = new()
    {
        Text = "Nenhum stash guardado.",
        Margin = new Thickness(0, 10, 0, 0),
        Classes = { "faint" },
    };

    public StashWindow(MainViewModel main, Repo repo) : base($"Stash — {repo.Name}", 500)
    {
        _main = main;
        _repo = repo;

        var msg = new TextBox { Watermark = "Descrição (opcional)" };
        var push = Btn("Guardar", true);
        push.Margin = new Thickness(6, 0, 0, 0);
        push.Click += async (_, _) =>
        {
            await RunAsync(() => GitService.StashPushAsync(_repo.Path, msg.Text ?? "", false));
            msg.Text = "";
        };

        var pushRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(push, 1);
        pushRow.Children.Add(msg);
        pushRow.Children.Add(push);

        _list.ItemTemplate = new FuncDataTemplate<StashEntry>((s, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Height = 26 };
            var text = new TextBlock
            {
                Text = $"{s.Label} — {s.Subject}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var apply = new Button { Content = "Aplicar", Classes = { "tiny" } };
            var drop = new Button { Content = "✕", Classes = { "tiny", "danger" } };
            Grid.SetColumn(apply, 1);
            Grid.SetColumn(drop, 2);

            apply.Click += async (_, _) => await RunAsync(() => GitService.StashApplyAsync(_repo.Path, s.Index, true));
            drop.Click += async (_, _) => await RunAsync(() => GitService.StashDropAsync(_repo.Path, s.Index));

            row.Children.Add(text);
            row.Children.Add(apply);
            row.Children.Add(drop);
            return row;
        });

        var close = Btn("Fechar");
        close.Click += (_, _) => Close();

        Compose($"Stash — {repo.Name}",
            new Control[] { Field("Guardar alterações atuais", pushRow), _list, _empty },
            new[] { close });

        Opened += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var list = await GitService.StashesAsync(_repo.Path);
            _list.ItemsSource = list;
            _empty.IsVisible = list.Count == 0;
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await ReloadAsync();
            await _main.RefreshRepoAsync(_repo.Id);
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }
}

// ------------------------------------------------------------------ git-flow

/// <summary>
/// Branches e prefixos do git-flow, no desenho do "Initialise repository for Git-flow"
/// do SourceTree. Devolve null quando o usuário cancela.
/// </summary>
public sealed class GitFlowWindow : DialogWindow
{
    public GitFlowWindow(GitFlowConfig atual) : base(atual.Inicializado ? "Configurar git-flow" : "Inicializar git-flow", 440)
    {
        var master = new TextBox { Text = atual.Master };
        var develop = new TextBox { Text = atual.Develop };
        var feature = new TextBox { Text = atual.Feature };
        var hotfix = new TextBox { Text = atual.Hotfix };
        var release = new TextBox { Text = atual.Release };
        var tag = new TextBox { Text = atual.VersionTag, Watermark = "vazio: a tag é só a versão" };

        var padrao = BtnDiscreto("Usar padrão");
        padrao.Click += (_, _) =>
        {
            var p = new GitFlowConfig();
            master.Text = p.Master;
            develop.Text = p.Develop;
            feature.Text = p.Feature;
            hotfix.Text = p.Hotfix;
            release.Text = p.Release;
            tag.Text = p.VersionTag;
        };

        var cancelar = Btn("Cancelar");
        var ok = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        ok.Click += (_, _) => Close(new GitFlowConfig
        {
            Master = (master.Text ?? "").Trim(),
            Develop = (develop.Text ?? "").Trim(),
            Feature = Prefixo(feature.Text),
            Hotfix = Prefixo(hotfix.Text),
            Release = Prefixo(release.Text),
            VersionTag = (tag.Text ?? "").Trim(),
            Inicializado = true,
        });

        Compose(Title ?? "",
            new Control[]
            {
                Secao("Branches", primeira: true),
                Field("Produção", master),
                Field("Desenvolvimento (criada a partir da produção, se não existir)", develop),
                Secao("Prefixos"),
                Field("Feature — sai da develop e volta para ela", feature),
                Field("Fix (hotfix) — sai da produção e volta para as duas", hotfix),
                Field("Release — sai da develop e entra na produção com tag", release),
                Field("Prefixo da tag de versão", tag),
                padrao,
            },
            new[] { cancelar, ok });
    }

    /// <summary>"feat" e "feat/" querem dizer a mesma coisa; a barra é garantida aqui.</summary>
    private static string Prefixo(string? texto)
    {
        var t = (texto ?? "").Trim();
        return t.Length == 0 || t.EndsWith('/') ? t : t + "/";
    }
}
