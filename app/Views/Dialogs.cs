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
using GRepos.Controls;
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
            MinWidth = 0,
            Padding = new Thickness(9, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        b.Classes.Add("tiny");
        if (perigo) b.Classes.Add("danger");
        Rotular(b, texto);
        return b;
    }

    protected static Button Btn(string text, bool primary = false)
    {
        var b = new Button { MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) b.Classes.Add("primary");
        Rotular(b, text);
        return b;
    }

    /// <summary>O glifo e a cor de cada ação dos diálogos: a mesma ação sai igual em todos.</summary>
    private static readonly Dictionary<string, (string Glifo, string Cor)> Acoes = new()
    {
        ["Cancelar"] = (Glifos.Fechar, "cinza"),
        ["Fechar"] = (Glifos.Fechar, "cinza"),
        ["Salvar"] = (Glifos.Salvar, "azul"),
        ["Salvar conta e token"] = (Glifos.Salvar, "azul"),
        ["Confirmar"] = (Glifos.Finalizar, "azul"),
        ["Procurar"] = (Glifos.Pasta, "amarelo"),
        ["Procurar…"] = (Glifos.Pasta, "amarelo"),
        ["Procurar agora"] = (Glifos.Atualizar, "azul"),
        ["Adicionar"] = (Glifos.Adicionar, "verde"),
        ["Clonar"] = (Glifos.Puxar, "verde"),
        ["Aplicar"] = (Glifos.Puxar, "verde"),
        ["Testar"] = (Glifos.Finalizar, "verde"),
        ["Tornar principal"] = (Glifos.Finalizar, "verde"),
        ["Voltar todos"] = (Glifos.Desfazer, "verde"),
        ["Voltar a acompanhar"] = (Glifos.Desfazer, "verde"),
        ["Editar"] = (Glifos.Editar, "azul"),
        ["Excluir"] = (Glifos.Excluir, "vermelho"),
        ["Remover"] = (Glifos.Excluir, "vermelho"),
        ["Remover da lista"] = (Glifos.Excluir, "vermelho"),
        ["Subgrupo"] = (Glifos.NovaPasta, "amarelo"),
        ["Guardar"] = (Glifos.Esconder, "amarelo"),
        ["Usar padrão"] = (Glifos.Desfazer, "cinza"),
        ["Usar o Gerenciador de Credenciais do Windows"] = (Glifos.Ajustes, "cinza"),
    };

    /// <summary>Põe o texto no botão, com o glifo e a cor da ação; texto fora da tabela vai sozinho.</summary>
    protected static void Rotular(Button b, string texto)
    {
        if (!Acoes.TryGetValue(texto, out var acao))
        {
            b.Content = texto;
            return;
        }

        b.Classes.Add(acao.Cor);
        b.Content = new StackPanel
        {
            Classes = { "comIcone" },
            Children =
            {
                new TextBlock { Classes = { "glifo" }, Text = acao.Glifo },
                new TextBlock { Text = texto },
            },
        };
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

    /// <summary>
    /// Diálogo com muitos campos, no padrão das configurações do GNotas (a EV19 dele): as seções
    /// numa lista à esquerda, cada uma com o seu glifo, e a escolhida num cartão à direita, com
    /// o título dela no alto. Cada página é montada na primeira visita e guardada: o que foi
    /// digitado numa seção não se perde ao visitar outra, e o Salvar lê os controles, que já
    /// existem antes de a seção deles ser aberta.
    /// </summary>
    protected ListBox ComposeEmSecoes(IReadOnlyList<(string Nome, string Glifo, Func<Control> Pagina)> secoes,
        IEnumerable<Control> footer)
    {
        var nomes = secoes.Select(s => s.Nome).ToList();
        var lista = new ListBox
        {
            Classes = { "secoes" },
            ItemsSource = nomes,
            Margin = new Thickness(0, 6, 10, 0),
            ItemTemplate = new FuncDataTemplate<string>((nome, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Height = 30,
                Margin = new Thickness(10, 0),
                Children =
                {
                    new TextBlock
                    {
                        Text = secoes[Math.Max(0, nomes.IndexOf(nome))].Glifo,
                        Classes = { "glifo" },
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock { Text = nome, FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                },
            }),
        };

        var conteudo = new ScrollViewer
        {
            AllowAutoHide = false,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var montadas = new Dictionary<int, Control>();

        lista.SelectionChanged += (_, _) =>
        {
            var escolhida = Math.Max(0, lista.SelectedIndex);
            if (!montadas.TryGetValue(escolhida, out var pagina))
                montadas[escolhida] = pagina = secoes[escolhida].Pagina();
            conteudo.Content = pagina;
            conteudo.Offset = default; // seção nova começa do topo, não onde a anterior parou
        };

        var cartao = new Border { Classes = { "cartaoDaSecao" }, Child = conteudo };

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
        };
        foreach (var c in footer) foot.Children.Add(c);

        var grade = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("190,*"),
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(10, 10, 12, 12),
        };
        Grid.SetColumn(cartao, 1);
        Grid.SetRow(foot, 1);
        Grid.SetColumnSpan(foot, 2);
        grade.Children.Add(lista);
        grade.Children.Add(cartao);
        grade.Children.Add(foot);
        Content = grade;

        lista.SelectedIndex = 0;
        return lista;
    }

    // ------------------------------------------------ as peças das seções, como as do GNotas

    /// <summary>Uma página de seção: o título grande no alto e os campos com respiro entre eles.</summary>
    protected static StackPanel Pagina(string titulo, params Control[] filhos)
    {
        var p = new StackPanel { Spacing = 20, Margin = new Thickness(26, 22, 26, 26) };
        p.Children.Add(new TextBlock
        {
            Text = titulo,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        foreach (var f in filhos) p.Children.Add(f);
        return p;
    }

    /// <summary>Um campo: o nome em negrito, o controle e, embaixo, a explicação (apagada).</summary>
    protected static StackPanel Campo(string nome, Control controle, string? ajuda = null)
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new TextBlock { Text = nome, FontSize = 13, FontWeight = FontWeight.SemiBold });
        p.Children.Add(controle);
        if (ajuda is not null) p.Children.Add(Ajuda(ajuda));
        return p;
    }

    /// <summary>A explicação de um campo: apagada, e sem passar de uma largura que se leia bem.</summary>
    protected static TextBlock Ajuda(string texto) => new()
    {
        Text = texto,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 560,
        HorizontalAlignment = HorizontalAlignment.Left,
        Classes = { "faint" },
    };

    /// <summary>
    /// Opções lado a lado numa pílula (Escuro | Claro), como o tema nas configurações do GNotas.
    /// Escolher uma acende ela e chama <paramref name="escolher"/> com a posição.
    /// </summary>
    protected static Border Opcoes(IReadOnlyList<string> textos, int atual, Action<int> escolher)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        void Marcar(int escolhida)
        {
            for (var i = 0; i < sp.Children.Count; i++) sp.Children[i].Classes.Set("ativo", i == escolhida);
        }
        for (var i = 0; i < textos.Count; i++)
        {
            var indice = i;
            var b = new Button { Classes = { "modo" }, Content = textos[i] };
            b.Click += (_, _) =>
            {
                Marcar(indice);
                escolher(indice);
            };
            sp.Children.Add(b);
        }
        Marcar(atual);
        return new Border { Classes = { "modos" }, Child = sp };
    }
}

// ------------------------------------------------------------------ confirmar

public sealed class ConfirmWindow : DialogWindow
{
    /// <param name="acao">O texto do botão que confirma. Com <paramref name="perigo"/>, ele sai em vermelho.</param>
    /// <param name="aviso">O que convém saber antes de confirmar, numa faixa amarela acima do texto.</param>
    public ConfirmWindow(string title, string message, string acao = "Confirmar", bool perigo = false, string aviso = "")
        : base(title, 420)
    {
        var no = Btn("Cancelar");
        var yes = Btn(acao, !perigo);
        if (perigo) yes.Classes.Add("vermelho");
        no.Click += (_, _) => Close(false);
        yes.Click += (_, _) => Close(true);

        var corpo = new List<Control>();
        if (aviso.Length > 0)
            corpo.Add(new Border
            {
                Name = "AvisoDaConfirmacao",
                Padding = new Thickness(10, 8),
                Margin = new Thickness(0, 0, 0, 10),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#66B08500")),
                Background = new SolidColorBrush(Color.Parse("#24B08500")),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    Children =
                    {
                        new TextBlock
                        {
                            Classes = { "glifo", "amarelo" }, Text = Glifos.Alerta,
                            Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                        },
                        new TextBlock
                        {
                            [Grid.ColumnProperty] = 1,
                            Classes = { "amarelo" }, Text = aviso, FontWeight = FontWeight.SemiBold,
                            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None,
                        },
                    },
                },
            });
        corpo.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });

        Compose(title, corpo, new[] { no, yes }, rodapeCentralizado: true);
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
    /// <param name="pais">
    /// Grupos que podem conter este, em ordem de árvore. Nulo esconde a escolha — o
    /// diálogo antigo, só com nome e cor.
    /// </param>
    public GroupWindow(string titulo, string nome, string cor,
        string? paiId = null, IReadOnlyList<GrupoNaArvore>? pais = null) : base(titulo, 420)
    {
        var escolhida = GroupPalette.Normalizar(cor) ?? GroupPalette.Padrao;

        var nomeBox = new TextBox { Text = nome, Watermark = "Ex.: Módulos BMSoft" };
        var hexBox = new TextBox { Text = escolhida, Width = 96 };

        // a amostra abre o painel com a paleta inteira; as bolinhas acima são o atalho
        // para as cores de grupo, que aparecem bem nos dois temas
        var seletor = new SeletorDeCor { Cor = escolhida, Margin = new Thickness(8, 0, 0, 0) };

        var swatches = new WrapPanel();
        var botoes = new List<Button>();

        void Selecionar(string valor, bool atualizaHex)
        {
            escolhida = valor;
            if (seletor.Cor != valor) seletor.Cor = valor;
            if (atualizaHex) hexBox.Text = valor;
            foreach (var b in botoes)
                b.BorderThickness = new Thickness(
                    string.Equals(b.Tag as string, valor, StringComparison.OrdinalIgnoreCase) ? 3 : 0);
        }

        foreach (var c in GroupPalette.Cores)
        {
            var b = new Button
            {
                Classes = { "amostra" },
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

        seletor.Escolhida += valor => Selecionar(GroupPalette.Normalizar(valor) ?? escolhida, true);

        // a cor acompanha o que se digita: assim que o código vira uma cor válida, a
        // amostra e as bolinhas já mudam, sem precisar sair do campo
        hexBox.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (GroupPalette.Normalizar(hexBox.Text) is { } v && v != escolhida) Selecionar(v, false);
        };
        hexBox.LostFocus += (_, _) => hexBox.Text = escolhida; // inválido volta ao que valia; válido fica por extenso

        var linhaCor = new StackPanel { Orientation = Orientation.Horizontal };
        linhaCor.Children.Add(hexBox);
        linhaCor.Children.Add(seletor);

        // "dentro de": é o que faz pasta e subpasta. O primeiro item é o nível principal
        var opcoes = pais ?? Array.Empty<GrupoNaArvore>();
        var dentroDe = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        dentroDe.ItemsSource = new[] { "(nível principal)" }.Concat(opcoes.Select(p => p.Recuado)).ToList();
        dentroDe.SelectedIndex = Math.Max(0, opcoes.ToList().FindIndex(p => p.Grupo.Id == paiId) + 1);

        var cancelar = Btn("Cancelar");
        var salvar = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        salvar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nomeBox.Text)) return;

            // sem a lista de pais o grupo fica onde estava
            var pai = pais is null ? paiId
                : dentroDe.SelectedIndex > 0 ? opcoes[dentroDe.SelectedIndex - 1].Grupo.Id : null;
            Close(new GroupResult(nomeBox.Text!.Trim(), escolhida, pai));
        };

        var corpo = new List<Control> { Field("Nome do grupo", nomeBox) };
        if (pais is not null) corpo.Add(Field("Dentro de", dentroDe));
        corpo.Add(Field("Cor", swatches));
        corpo.Add(Field("…ou escolha na paleta, ou informe o código", linhaCor));

        Compose(titulo, corpo, new[] { cancelar, salvar });

        Opened += (_, _) => nomeBox.Focus();
    }
}

public sealed record GroupResult(string Nome, string Cor, string? PaiId = null);

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

        // em ordem de árvore, com o subgrupo recuado sob o pai
        var arvore = main.GruposEmArvore();
        var groups = arvore.Select(a => a.Grupo).ToList();
        group.ItemsSource = new[] { "Sem grupo" }.Concat(arvore.Select(a => a.Recuado)).ToList();
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
            Rotular(add, c ? "Clonar" : "Adicionar");
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

        // em ordem de árvore, com o subgrupo recuado sob o pai
        var arvore = main.GruposEmArvore();
        var groups = arvore.Select(a => a.Grupo).ToList();
        group.ItemsSource = new[] { "Sem grupo" }.Concat(arvore.Select(a => a.Recuado)).ToList();
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
                (usuarioConta.Length > 0 ? $"“{usuarioConta}”" : "o usuário de Configurações → Contas") +
                ". O token não vai no link.";
            var texto = RemotoConfig.Previa(remotoAtual, urlBox.Text, usuarioConta);
            previa.Text = texto ?? "";
            previa.IsVisible = texto is not null;
        }

        async System.Threading.Tasks.Task MostrarAutenticacaoAsync()
        {
            if (usuarioConta.Length == 0)
            {
                autenticacao.Text = "✗ Nenhuma conta do GitHub em Configurações → Contas.";
                return;
            }

            var temToken = !string.IsNullOrEmpty(await GitHubService.TokenDoUsuarioAsync(usuarioConta));
            autenticacao.Text = temToken
                ? $"✓ O git usa o token da conta “{usuarioConta}”, guardado no Windows."
                : $"✗ Nenhum token salvo para “{usuarioConta}”. Salve em Configurações → Contas.";
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

    /// <summary>As seções, na ordem da lista à esquerda (os testes conferem por aqui).</summary>
    public static readonly string[] Secoes =
        { "Aparência", "Geral", "Obter e puxar", "Terminal", "Diff externo", "Contas", "Sobre", "Notas da versão" };

    private readonly ListBox _lista;

    /// <summary>A seção aberta (os testes trocam por aqui).</summary>
    public string? SecaoAberta
    {
        get => _lista.SelectedItem as string;
        set => _lista.SelectedItem = value;
    }

    /// <summary>
    /// As configurações, no padrão das do GNotas e do GTerm: as seções à esquerda e a escolhida
    /// num cartão. A versão, a procura de atualização e onde ficam as configurações estão no Sobre.
    /// </summary>
    public SettingsWindow(MainViewModel main) : base("Configurações", 800)
    {
        // são muitos campos: ficam separados por seção, com a lista à esquerda. A janela abre num
        // tamanho confortável e o usuário estica se quiser ver mais de uma vez
        SizeToContent = SizeToContent.Manual;
        CanResize = true;
        Height = 600;
        MinWidth = 660;
        MinHeight = 440;
        MaxHeight = double.PositiveInfinity;

        var s = main.Settings;

        // as escolhas de botões lado a lado guardam a posição; o Salvar lê daqui
        var temaEscolhido = s.Theme == "light" ? 1 : 0;
        var densidadeEscolhida = s.Density == "confortavel" ? 1 : 0;
        var arvoreEscolhida = s.ArvoreMinimalista ? 0 : 1;
        var abaEscolhida = s.DefaultTab == "historico" ? 1 : 0;
        var theme = Opcoes(new[] { "Escuro", "Claro" }, temaEscolhido, i => temaEscolhido = i);
        var density = Opcoes(new[] { "Compacta", "Confortável" }, densidadeEscolhida, i => densidadeEscolhida = i);
        var estiloArvore = Opcoes(new[] { "Minimalista", "Pílulas" }, arvoreEscolhida, i => arvoreEscolhida = i);
        var abaInicial = Opcoes(new[] { "Alterações", "Histórico" }, abaEscolhida, i => abaEscolhida = i);

        var refresh = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = s.AutoRefreshSeconds, Increment = 10, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        var esteiras = new NumericUpDown { Minimum = 1, Maximum = 50, Value = s.EsteirasVisiveis, Increment = 1, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        var todasAsTags = new CheckBox
        {
            Content = "Trazer todas as tags do remoto",
            IsChecked = s.BuscarTodasAsTags,
        };
        var todasAsBranches = new CheckBox
        {
            Content = "Criar e atualizar as branches locais de todas as remotas",
            IsChecked = s.SincronizarTodasAsBranches,
        };
        var avisarAtualizacao = new CheckBox
        {
            Content = "Avisar quando sair uma versão nova",
            IsChecked = s.AvisarAtualizacao,
        };

        // procurar e, achando versão nova, atualizar: o mesmo botão, como no GTerm. Quem atualiza é
        // o comando do convite da barra de status, que procura de novo, pergunta e baixa; a janela
        // fecha antes, porque atualizar reinicia o aplicativo
        var botaoDaVersao = new Button { Classes = { "pilula", "azul" }, HorizontalAlignment = HorizontalAlignment.Left };
        var situacaoDaVersao = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Classes = { "faint" },
        };

        void MostrarVersao()
        {
            var nova = main.TemAtualizacao;
            botaoDaVersao.Classes.Set("azul", !nova);
            botaoDaVersao.Classes.Set("verde", nova);
            botaoDaVersao.IsEnabled = main.VersaoDoApp.Length > 0 && !main.VerificandoAtualizacao && !main.Atualizando;
            botaoDaVersao.Content = new StackPanel
            {
                Classes = { "comIcone" },
                Children =
                {
                    new TextBlock { Classes = { "glifo" }, Text = nova ? Glifos.Instalar : Glifos.Atualizar },
                    new TextBlock { Text = main.RotuloDeAtualizar },
                },
            };
            situacaoDaVersao.Text = main.VersaoDoApp.Length == 0
                ? "Build local, sem versão: não há com o que comparar."
                : main.SituacaoDaVersao;
        }

        void VersaoMudou(object? _, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainViewModel.SituacaoDaVersao) or nameof(MainViewModel.RotuloDeAtualizar)
                or nameof(MainViewModel.VerificandoAtualizacao) or nameof(MainViewModel.Atualizando))
                MostrarVersao();
        }

        MostrarVersao();
        main.PropertyChanged += VersaoMudou;
        Closed += (_, _) => main.PropertyChanged -= VersaoMudou;

        botaoDaVersao.Click += (_, _) =>
        {
            if (!main.TemAtualizacao)
            {
                main.ProcurarAtualizacaoCommand.Execute(null);
                return;
            }
            Close();
            main.AtualizacaoCommand.Execute(null);
        };

        var linhaDaVersao = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(situacaoDaVersao, 1);
        linhaDaVersao.Children.Add(botaoDaVersao);
        linhaDaVersao.Children.Add(situacaoDaVersao);

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
            Classes = { "faint" },
        };

        // Erro em linha separada: antes ele substituía o caminho, e aí a informação
        // sumia da tela e não voltava mais — nem ao desmarcar.
        var erroPortatil = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
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
        var logLimit = new NumericUpDown { Minimum = 50, Maximum = 5000, Value = s.LogLimit, Increment = 50, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };

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

        // --------------------------------------------------------- diff externo

        var diffExterno = new TextBox
        {
            Text = s.DiffExternoPath,
            Watermark = @"vazio procura sozinho — ex.: C:\Program Files\Beyond Compare 5",
        };
        var diffArgs = new TextBox { Text = s.DiffExternoArgs };
        var procurarDiff = BtnDiscreto("Procurar…");
        procurarDiff.Margin = new Thickness(6, 0, 0, 0);
        var diffUsado = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10),
            Classes = { "faint" },
        };

        void MostrarDiffExterno()
        {
            var achou = DiffExterno.Localizar(diffExterno.Text);
            diffUsado.Text = achou is not null
                ? "Abre: " + achou
                : string.IsNullOrWhiteSpace(diffExterno.Text)
                    ? "Nenhuma ferramenta encontrada automaticamente — informe a pasta de instalação ou o .exe."
                    : "Nenhuma ferramenta conhecida nessa pasta. Se for outra, informe o caminho completo do .exe.";
            diffUsado.Foreground = achou is null && !string.IsNullOrWhiteSpace(diffExterno.Text)
                ? new SolidColorBrush(Color.Parse("#E5534B"))
                : null;
            // o padrão aparece como dica: dá para ver o que será usado antes de mexer
            diffArgs.Watermark = "vazio usa: " + (achou is null ? DiffExterno.ArgumentosGenericos : DiffExterno.ArgumentosPadrao(achou));
        }

        MostrarDiffExterno();
        diffExterno.TextChanged += (_, _) => MostrarDiffExterno();
        procurarDiff.Click += async (_, _) =>
        {
            var pasta = await main.Dialogos.PickFolderAsync("Pasta de instalação da ferramenta de diff");
            if (pasta is not null) diffExterno.Text = pasta;
        };

        var linhaDiff = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(procurarDiff, 1);
        linhaDiff.Children.Add(diffExterno);
        linhaDiff.Children.Add(procurarDiff);

        // o campo com o código e, ao lado, a amostra que abre a paleta — como no WinDock.
        // As bolinhas continuam como atalho para os destaques de sempre
        var accent = GroupPalette.Normalizar(s.Accent) ?? Accents[0];
        var hexDestaque = new TextBox { Text = accent, Width = 96 };
        var seletorDestaque = new SeletorDeCor { Cor = accent, Margin = new Thickness(8, 0, 12, 0) };
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

        void Destacar(string valor, bool atualizaHex)
        {
            accent = valor;
            if (seletorDestaque.Cor != valor) seletorDestaque.Cor = valor;
            if (atualizaHex) hexDestaque.Text = valor;
            foreach (var child in swatches.Children.OfType<Button>())
                child.BorderThickness = new Thickness(
                    string.Equals(child.Tag as string, valor, StringComparison.OrdinalIgnoreCase) ? 2 : 0);
        }

        foreach (var color in Accents)
        {
            var dot = new Button
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.Parse(color)),
                BorderThickness = new Thickness(string.Equals(color, accent, StringComparison.OrdinalIgnoreCase) ? 2 : 0),
                BorderBrush = Brushes.White,
                Padding = new Thickness(0),
                Tag = color,
            };
            dot.Click += (_, _) => Destacar(color, true);
            swatches.Children.Add(dot);
        }

        seletorDestaque.Escolhida += valor => Destacar(GroupPalette.Normalizar(valor) ?? accent, true);
        // a cor acompanha o que se digita, sem precisar sair do campo
        hexDestaque.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (GroupPalette.Normalizar(hexDestaque.Text) is { } v && v != accent) Destacar(v, false);
        };
        hexDestaque.LostFocus += (_, _) => hexDestaque.Text = accent; // inválido volta ao que valia; válido fica por extenso

        var linhaDestaque = new StackPanel { Orientation = Orientation.Horizontal };
        linhaDestaque.Children.Add(hexDestaque);
        linhaDestaque.Children.Add(seletorDestaque);
        linhaDestaque.Children.Add(swatches);

        // os grupos em árvore, cada subgrupo recuado sob o pai. A amostra de cada linha é o
        // próprio seletor: clicar nela abre a paleta e a cor troca na hora
        var groupsPanel = new StackPanel { Spacing = 5 };

        void MontarGrupos()
        {
            groupsPanel.Children.Clear();
            foreach (var item in main.GruposEmArvore())
            {
                var g = item.Grupo;
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
                    Margin = new Thickness(item.Nivel * 18, 0, 0, 0),
                };

                var cor = new SeletorDeCor
                {
                    Cor = GroupPalette.Normalizar(g.Color) ?? GroupPalette.Padrao,
                    Width = 24, Height = 20, Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                cor.Escolhida += valor =>
                {
                    var atual = main.Groups.FirstOrDefault(x => x.Id == g.Id);
                    if (atual is not null) main.UpdateGroup(g.Id, atual.Name, GroupPalette.Normalizar(valor) ?? atual.Color);
                };

                var nome = new TextBlock { Text = g.Name, VerticalAlignment = VerticalAlignment.Center };
                var sub = BtnDiscreto("Subgrupo");
                var editar = BtnDiscreto("Editar");
                var del = BtnDiscreto("Excluir", perigo: true);
                ToolTip.SetTip(sub, "Criar um grupo dentro deste");
                ToolTip.SetTip(del, "Exclui só o grupo: os subgrupos e os repositórios dele sobem um nível");
                sub.Margin = new Thickness(6, 0, 0, 0);
                editar.Margin = new Thickness(6, 0, 0, 0);
                del.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(nome, 1);
                Grid.SetColumn(sub, 2);
                Grid.SetColumn(editar, 3);
                Grid.SetColumn(del, 4);

                // nome, cor e lugar na árvore podem ter mudado: a lista é refeita
                sub.Click += async (_, _) =>
                {
                    await main.NovoGrupoAsync(g.Id);
                    MontarGrupos();
                };
                editar.Click += async (_, _) =>
                {
                    await main.EditGroupAsync(g.Id);
                    MontarGrupos();
                };
                del.Click += (_, _) =>
                {
                    main.RemoveGroup(g.Id);
                    MontarGrupos();
                };

                row.Children.Add(cor);
                row.Children.Add(nome);
                row.Children.Add(sub);
                row.Children.Add(editar);
                row.Children.Add(del);
                groupsPanel.Children.Add(row);
            }
        }

        MontarGrupos();

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
        var contasPanel = new StackPanel { Spacing = 2 };
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
            main.BuscarTodasAsTags = todasAsTags.IsChecked == true;
            main.SincronizarTodasAsBranches = todasAsBranches.IsChecked == true;
            main.SetArvoreMinimalista(arvoreEscolhida == 0);
            main.SetEsteirasVisiveis((int)(esteiras.Value ?? 6));
            main.SetGitBashPath(gitBash.Text ?? "");
            main.SetDiffExterno(diffExterno.Text ?? "", diffArgs.Text ?? "");
            main.ApplySettings(
                temaEscolhido == 1 ? "light" : "dark",
                accent,
                densidadeEscolhida == 1 ? "confortavel" : "compacta",
                (int)(refresh.Value ?? 60),
                (int)(logLimit.Value ?? 300),
                abaEscolhida == 1 ? "historico" : "alteracoes");
            Close();
        };

        gitBashUsado.Margin = new Thickness(0);
        diffUsado.Margin = new Thickness(0);

        var temGrupos = groupsPanel.Children.Count > 0;
        Control PaginaDaAparencia() => Pagina("Aparência", new Control?[]
        {
            Campo("Tema", theme),
            Campo("Cor de destaque", linhaDestaque,
                "Os botões acesos, a seleção e os destaques. Digite o código, escolha na paleta ou numa bolinha."),
            Campo("Densidade das listas", density),
            Campo("Estilo da árvore", estiloArvore,
                "Minimalista: a seta, o ponto e o nome na cor do grupo. Pílulas: o grupo numa pílula colorida."),
            temGrupos
                ? Campo("Grupos", groupsPanel,
                    "A amostra de cada linha troca a cor do grupo. Excluir tira só o grupo: os subgrupos e os repositórios dele sobem um nível.")
                : null,
        }.OfType<Control>().ToArray());

        Control PaginaGeral() => Pagina("Geral",
            Campo("Abrir o repositório em", abaInicial, "A aba que aparece ao escolher um repositório na árvore."),
            Campo("Atualizar o status sozinho", refresh, "A cada quantos segundos; 0 desliga."),
            Campo("Commits carregados no histórico", logLimit),
            Campo("Execuções mostradas na esteira", esteiras));

        Control PaginaObterEPuxar() => Pagina("Obter e puxar",
            Campo("Ao obter e puxar", new StackPanel { Spacing = 6, Children = { todasAsTags, todasAsBranches } },
                "A branch atual e as que têm commit só seu nunca são mexidas: as locais só avançam quando estão apenas " +
                "atrás da remota. As mesmas opções ficam no clique direito de Obter e Puxar."));

        Control PaginaDoTerminal() => Pagina("Terminal",
            Campo("Git Bash", new StackPanel { Spacing = 6, Children = { linhaGitBash, gitBashUsado } },
                "A pasta de instalação do Git ou o caminho do git-bash.exe. Vazio, o GRepos procura sozinho."));

        Control PaginaDoDiff() => Pagina("Diff externo",
            Campo("Ferramenta", new StackPanel { Spacing = 6, Children = { linhaDiff, diffUsado } },
                "Aberta pelo botão Diff externo do painel de diferenças. Beyond Compare, WinMerge, Meld, KDiff3, P4Merge, " +
                "TortoiseGitMerge e VS Code são reconhecidos pela pasta de instalação; outra, informe o caminho do .exe."),
            Campo("Argumentos", diffArgs, "$LOCAL é o lado esquerdo e $REMOTE o direito."));

        Control PaginaDasContas() => Pagina("Contas",
            Campo("Contas do GitHub", contasPanel,
                "A principal vale para os repositórios que não escolhem outra em Configurar repositório."),
            Campo("Usuário", usuario),
            Campo("Token de acesso pessoal", new StackPanel { Spacing = 8, Children = { token, acoesToken, situacao } }),
            Campo("Gerenciador de credenciais", new StackPanel { Spacing = 8, Children = { helperTexto, configurarHelper } }));

        Control PaginaSobre() => Pagina("Sobre",
            new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new SelectableTextBlock { Text = main.Versao, FontSize = 14, FontWeight = FontWeight.SemiBold },
                    linhaDaVersao,
                },
            },
            Campo("Aviso de versão nova", avisarAtualizacao,
                "Com ele, o GRepos procura uma vez por dia e, havendo versão nova, o convite aparece na barra de status. " +
                "O botão acima e o ícone da barra procuram na hora, ligado ou não."),
            Campo("Onde ficam as configurações", new StackPanel { Spacing = 6, Children = { portatil, ondeFica, erroPortatil } }));

        Control PaginaDasNotas()
        {
            // todas as versões em sequência, da mais nova para a mais antiga, como no Sobre do GNotas;
            // a em uso ganha a marca. Montada só na primeira visita: são todas as versões já publicadas
            var notas = NotasDaVersao.Carregar();
            if (notas.Count == 0)
                return Pagina("Notas da versão", Ajuda("Este executável não traz as notas da versão."));

            var emUso = MainViewModel.VersaoEmUso.Length > 0 ? NotasDaVersao.IndiceDa(notas, MainViewModel.VersaoEmUso) : -1;
            var texto = new System.Text.StringBuilder();
            for (var i = 0; i < notas.Count; i++)
                texto.Append("### ").Append(notas[i].Rotulo).Append(i == emUso ? " · em uso" : "").Append("\n\n")
                     .Append(notas[i].Notas).Append("\n\n");

            return Pagina("Notas da versão",
                Ajuda(emUso >= 0
                    ? "Você está na " + MainViewModel.VersaoEmUso + ". O que mudou em cada versão, da mais nova para a mais antiga."
                    : "Build local, sem versão publicada. O que mudou em cada versão, da mais nova para a mais antiga."),
                new MarkdownView { Markdown = texto.ToString(), Margin = new Thickness(0, -14, 0, 0) });
        }

        _lista = ComposeEmSecoes(new (string, string, Func<Control>)[]
        {
            (Secoes[0], Glifos.Cor, PaginaDaAparencia),
            (Secoes[1], Glifos.Ajustes, PaginaGeral),
            (Secoes[2], Glifos.Obter, PaginaObterEPuxar),
            (Secoes[3], Glifos.Terminal, PaginaDoTerminal),
            (Secoes[4], Glifos.Comparar, PaginaDoDiff),
            (Secoes[5], Glifos.Autoria, PaginaDasContas),
            (Secoes[6], Glifos.Sobre, PaginaSobre),
            (Secoes[7], Glifos.Documento, PaginaDasNotas),
        }, new[] { close, save });
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
            var apply = new Button { Classes = { "tiny" } };
            Rotular(apply, "Aplicar");
            var drop = new Button
            {
                Classes = { "tiny", "redonda", "vermelho" },
                Margin = new Thickness(3, 0, 0, 0),
                Content = new TextBlock { Classes = { "glifo" }, Text = Glifos.Excluir },
            };
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

// ----------------------------------------------------------------- ignorados

/// <summary>O que está fora da lista de alterações só nesta máquina, e o caminho de volta.</summary>
public sealed class IgnoradosWindow : DialogWindow
{
    private readonly MainViewModel _main;
    private readonly Repo _repo;
    private readonly ListBox _list = new() { MaxHeight = 320 };
    private readonly Button _todos;
    private readonly TextBlock _empty = new()
    {
        Text = "Nenhum arquivo ignorado.",
        Margin = new Thickness(0, 10, 0, 0),
        Classes = { "faint" },
    };
    private List<Ignorado> _itens = new();

    public IgnoradosWindow(MainViewModel main, Repo repo) : base($"Ignorados — {repo.Name}", 520)
    {
        _main = main;
        _repo = repo;

        _list.ItemTemplate = new FuncDataTemplate<Ignorado>((i, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Height = 26 };
            var text = new TextBlock
            {
                Text = i.Texto,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTip.SetTip(text, i.Texto);
            var tipo = new TextBlock
            {
                Text = i.Rastreado ? "alterado" : "novo",
                FontSize = 10.5,
                Margin = new Thickness(8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "faint" },
            };
            var voltar = new Button { Classes = { "tiny" } };
            Rotular(voltar, "Voltar a acompanhar");
            Grid.SetColumn(tipo, 1);
            Grid.SetColumn(voltar, 2);
            voltar.Click += async (_, _) => await VoltarAsync(new[] { i });

            row.Children.Add(text);
            row.Children.Add(tipo);
            row.Children.Add(voltar);
            return row;
        });

        _todos = Btn("Voltar todos");
        _todos.Click += async (_, _) => await VoltarAsync(_itens);
        var close = Btn("Fechar");
        close.Click += (_, _) => Close();

        Compose($"Ignorados — {repo.Name}",
            new Control[]
            {
                Label("Arquivos que não aparecem nas alterações só neste computador: nada disto vai para o " +
                      "repositório. Um arquivo alterado e ignorado pode impedir uma troca de branch ou um pull " +
                      "que mexa nele — nesse caso, volte a acompanhá-lo aqui."),
                _list, _empty,
            },
            new[] { _todos, close });

        Opened += (_, _) => Carga = CarregarAsync();
    }

    /// <summary>Carga disparada pelo Opened, para o teste poder esperar por ela.</summary>
    public Task Carga { get; private set; } = Task.CompletedTask;

    private async Task CarregarAsync()
    {
        _itens =await Ignorados.ListarAsync(_repo.Path);
        _list.ItemsSource = _itens;
        _empty.IsVisible = _itens.Count == 0;
        _todos.IsEnabled = _itens.Count > 0;
    }

    private async Task VoltarAsync(IEnumerable<Ignorado> itens)
    {
        try
        {
            await Ignorados.VoltarAsync(_repo.Path, itens.ToList());
            await CarregarAsync();
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
