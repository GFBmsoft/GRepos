using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using GRepos.Services;

namespace GRepos.Views;

/// <summary>
/// Desenha markdown já analisado. A montagem é em código porque a formatação inline
/// (negrito no meio da frase, link no meio do parágrafo) vira <c>Inlines</c> de um
/// TextBlock — coisa que DataTemplate em XAML não monta sem contorcionismo.
/// </summary>
public class MarkdownView : UserControl
{
    public static readonly StyledProperty<string> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string>(nameof(Markdown), "");

    private readonly StackPanel _pilha = new() { Spacing = 0 };

    /// <summary>Os textos do documento, na ordem em que aparecem: é por eles que a seleção anda.</summary>
    private readonly List<SelectableTextBlock> _textos = new();

    /// <summary>Onde começa cada link dentro do texto do bloco, para o clique acertar o link e não a linha.</summary>
    private readonly Dictionary<SelectableTextBlock, List<(int Inicio, int Fim, string Url)>> _links = new();

    /// <summary>Bloco em que o arraste da seleção começou; -1 sem arraste em curso.</summary>
    private int _ancora = -1;

    /// <summary>O caractere do bloco de partida em que o botão foi apertado.</summary>
    private int _indiceDaAncora;
    private Point _apertou;
    private bool _arrastou;

    public MarkdownView()
    {
        Content = _pilha;
        Focusable = true;

        // cada parágrafo é um texto separado, e a seleção de cada um parava na borda dele.
        // Aqui ela atravessa: o arraste que sai de um bloco continua nos seguintes, e o
        // Ctrl+C leva tudo que está marcado
        AddHandler(PointerPressedEvent, AoApertar, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, AoMover, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, AoSoltar, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, AoTeclar, RoutingStrategies.Tunnel);
        ContextRequested += (_, e) => AbrirMenu(e);
    }

    // ---------------------------------------------------------------- seleção

    private static int Tamanho(SelectableTextBlock t) => (t.Inlines?.Text ?? t.Text ?? "").Length;

    /// <summary>O caractere do bloco que está sob o ponto (em coordenadas deste controle).</summary>
    private int IndiceEm(SelectableTextBlock bloco, Point pontoAqui)
    {
        var p = this.TranslatePoint(pontoAqui, bloco) ?? default;
        var acerto = bloco.TextLayout.HitTestPoint(new Point(p.X - bloco.Padding.Left, p.Y - bloco.Padding.Top));
        return Math.Clamp(acerto.TextPosition + (acerto.IsTrailing ? 1 : 0), 0, Tamanho(bloco));
    }

    /// <summary>O bloco na altura do ponto; acima do primeiro vale o primeiro, abaixo do último, o último.</summary>
    private int BlocoEm(Point pontoAqui)
    {
        var melhor = -1;
        var distancia = double.MaxValue;
        for (var i = 0; i < _textos.Count; i++)
        {
            var topo = _textos[i].TranslatePoint(default, this)?.Y ?? 0;
            var base_ = topo + _textos[i].Bounds.Height;
            if (pontoAqui.Y >= topo && pontoAqui.Y <= base_) return i;

            var d = Math.Min(Math.Abs(pontoAqui.Y - topo), Math.Abs(pontoAqui.Y - base_));
            if (d < distancia) (melhor, distancia) = (i, d);
        }
        return melhor;
    }

    private static void Limpar(SelectableTextBlock t)
    {
        if (t.SelectionStart != t.SelectionEnd) t.SelectionEnd = t.SelectionStart;
    }

    private void AoApertar(object? sender, PointerPressedEventArgs e)
    {
        _ancora = -1;
        _arrastou = false;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var origem = (e.Source as Visual)?.FindAncestorOfType<SelectableTextBlock>(includeSelf: true);
        _ancora = origem is null ? -1 : _textos.IndexOf(origem);
        _apertou = e.GetPosition(this);
        _indiceDaAncora = origem is null ? 0 : IndiceEm(origem, _apertou);

        // um clique novo começa uma seleção nova: a dos outros blocos sai
        for (var i = 0; i < _textos.Count; i++)
            if (i != _ancora) Limpar(_textos[i]);
    }

    private void AoMover(object? sender, PointerEventArgs e)
    {
        if (_ancora < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var agora = e.GetPosition(this);
        if (Math.Abs(agora.X - _apertou.X) > 3 || Math.Abs(agora.Y - _apertou.Y) > 3) _arrastou = true;
        if (!_arrastou) return;

        var atual = BlocoEm(agora);
        if (atual < 0) return;

        // do ponto em que apertou até o ponteiro: o bloco de partida vai dali até a borda
        // dele, os do meio entram inteiros, e o da ponta vai da borda até o ponteiro
        for (var i = 0; i < _textos.Count; i++)
        {
            var t = _textos[i];

            if (i == _ancora)
            {
                var ate = atual == _ancora ? IndiceEm(t, agora) : atual > _ancora ? Tamanho(t) : 0;
                (t.SelectionStart, t.SelectionEnd) = (_indiceDaAncora, ate);
            }
            else if (i > Math.Min(_ancora, atual) && i < Math.Max(_ancora, atual))
            {
                t.SelectAll();
            }
            else if (i == atual)
            {
                var indice = IndiceEm(t, agora);
                (t.SelectionStart, t.SelectionEnd) = atual > _ancora ? (0, indice) : (indice, Tamanho(t));
            }
            else
            {
                Limpar(t);
            }
        }
    }

    private void AoSoltar(object? sender, PointerReleasedEventArgs e)
    {
        var clicado = _ancora >= 0 && _ancora < _textos.Count ? _textos[_ancora] : null;
        _ancora = -1;

        // clique sem arrastar em cima de um link: abre. Antes qualquer clique na linha
        // abria o link, e tentar selecionar o texto dela já levava ao navegador
        if (clicado is null || _arrastou || e.InitialPressMouseButton != MouseButton.Left) return;
        if (!_links.TryGetValue(clicado, out var links)) return;

        var indice = IndiceEm(clicado, e.GetPosition(this));
        foreach (var (inicio, fim, url) in links)
        {
            if (indice < inicio || indice > fim) continue;
            try { ShellService.AbrirUrl(url); }
            catch (Exception) { /* link quebrado no documento não é erro do app */ }
            return;
        }
    }

    /// <summary>O que está marcado, bloco a bloco, na ordem do documento.</summary>
    public string TextoSelecionado() => string.Join(Environment.NewLine,
        _textos.Select(t => t.SelectedText ?? "").Where(s => s.Length > 0));

    public bool TudoSelecionado => _textos.Count > 0 &&
        _textos.All(t => Tamanho(t) == 0 || Math.Abs(t.SelectionEnd - t.SelectionStart) == Tamanho(t));

    public void SelecionarTudo()
    {
        foreach (var t in _textos) t.SelectAll();
    }

    /// <summary>
    /// Copia o que está marcado. Com o documento inteiro marcado vai formatado (HTML mais
    /// texto limpo), que é o que se espera de "selecionar tudo e copiar" numa tela
    /// renderizada; com um trecho, vai o texto do trecho.
    /// </summary>
    public async Task CopiarSelecaoAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        if (TudoSelecionado)
        {
            await CopiaFormatada.CopiarAsync(clipboard, MarkdownExport.ParaHtml(Markdown), MarkdownExport.ParaTexto(Markdown));
            return;
        }

        var texto = TextoSelecionado();
        if (texto.Length > 0) await clipboard.SetTextAsync(texto);
    }

    private async void AoTeclar(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;

        if (e.Key == Key.A)
        {
            SelecionarTudo();
            e.Handled = true;
        }
        else if (e.Key == Key.C && TextoSelecionado().Length > 0)
        {
            e.Handled = true; // senão o bloco com o foco copiaria só o pedaço dele
            await CopiarSelecaoAsync();
        }
    }

    private void AbrirMenu(ContextRequestedEventArgs e)
    {
        if (_textos.Count == 0 || e.Source is not Control onde) return;

        var copiar = new MenuItem { Header = "Copiar", IsEnabled = TextoSelecionado().Length > 0 };
        copiar.Click += async (_, _) => await CopiarSelecaoAsync();

        var tudo = new MenuItem { Header = "Selecionar tudo" };
        tudo.Click += (_, _) => SelecionarTudo();

        var copiarTudo = new MenuItem { Header = "Copiar tudo (formatado)" };
        copiarTudo.Click += async (_, _) =>
        {
            SelecionarTudo();
            await CopiarSelecaoAsync();
        };

        var menu = new MenuFlyout();
        menu.Items.Add(copiar);
        menu.Items.Add(tudo);
        menu.Items.Add(copiarTudo);
        menu.ShowAt(onde, showAtPointer: true);
        e.Handled = true;
    }

    public string Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) Montar();
    }

    private void Montar()
    {
        _pilha.Children.Clear();
        _textos.Clear();
        _links.Clear();
        _ancora = -1;

        foreach (var bloco in MarkdownParser.Blocos(Markdown))
            _pilha.Children.Add(Desenhar(bloco));
    }

    private Control Desenhar(BlocoMd bloco) => bloco.Tipo switch
    {
        BlocoMdTipo.Regua => Regua(),
        BlocoMdTipo.Codigo => Codigo(bloco.Texto),
        BlocoMdTipo.Citacao => Citacao(bloco),
        BlocoMdTipo.Titulo => Titulo(bloco),
        BlocoMdTipo.Item => Item(bloco),
        BlocoMdTipo.Tabela => Tabela(bloco),
        _ => Paragrafo(bloco),
    };

    /// <summary>Tabela simples: cabeçalho em negrito com régua embaixo, colunas iguais.</summary>
    private Control Tabela(BlocoMd bloco)
    {
        var colunas = 0;
        foreach (var linha in bloco.Linhas)
            if (linha.Celulas.Count > colunas) colunas = linha.Celulas.Count;

        if (colunas == 0) return new Border();

        var grade = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        for (var c = 0; c < colunas; c++)
            grade.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var l = 0; l < bloco.Linhas.Count; l++)
            grade.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var l = 0; l < bloco.Linhas.Count; l++)
        {
            var linha = bloco.Linhas[l];
            for (var c = 0; c < linha.Celulas.Count; c++)
            {
                var celula = Texto(new BlocoMd { Trechos = linha.Celulas[c] }, 12);
                celula.Margin = new Thickness(0, 4, 18, 4);
                if (linha.Cabecalho) celula.FontWeight = FontWeight.SemiBold;

                Grid.SetRow(celula, l);
                Grid.SetColumn(celula, c);
                grade.Children.Add(celula);
            }

            if (!linha.Cabecalho) continue;

            var regua = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom };
            regua.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Border"));
            Grid.SetRow(regua, l);
            Grid.SetColumn(regua, 0);
            Grid.SetColumnSpan(regua, colunas);
            grade.Children.Add(regua);
        }

        // tabela larga rola na horizontal em vez de espremer o resto da página
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grade,
        };
    }

    private static Control Regua()
    {
        var linha = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 10) };
        linha.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Border"));
        return linha;
    }

    private Control Codigo(string texto)
    {
        var caixa = new Border
        {
            Padding = new Thickness(10, 7),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 4, 0, 8),
        };
        caixa.Bind(Border.BackgroundProperty, new DynamicResourceExtension("BgRaised"));

        var texto1 = new SelectableTextBlock
        {
            Text = texto,
            FontSize = 11.5,
            TextWrapping = TextWrapping.NoWrap,
            Classes = { "mono" },
            ContextFlyout = null, // o menu é o do documento, que copia a seleção inteira
        };
        _textos.Add(texto1);

        caixa.Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = texto1,
        };
        return caixa;
    }

    private Control Citacao(BlocoMd bloco)
    {
        var barra = new Border { Width = 3, CornerRadius = new CornerRadius(2) };
        barra.Bind(Border.BackgroundProperty, new DynamicResourceExtension("Border"));

        var grade = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3,*"),
            Margin = new Thickness(0, 3, 0, 6),
        };

        var corpo = Texto(bloco, 12.5);
        corpo.Margin = new Thickness(9, 0, 0, 0);
        corpo.Classes.Add("dim");

        Grid.SetColumn(corpo, 1);
        grade.Children.Add(barra);
        grade.Children.Add(corpo);
        return grade;
    }

    private Control Titulo(BlocoMd bloco)
    {
        var tamanhos = new[] { 19.0, 16.0, 14.0, 13.0, 12.5, 12.0 };
        var i = Math.Clamp(bloco.Nivel - 1, 0, tamanhos.Length - 1);

        var texto = Texto(bloco, tamanhos[i]);
        texto.FontWeight = FontWeight.SemiBold;
        texto.Margin = new Thickness(0, i == 0 ? 2 : 12, 0, 6);
        return texto;
    }

    private Control Item(BlocoMd bloco)
    {
        var grade = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(bloco.Nivel * 16, 1, 0, 3),
        };

        var marcador = new TextBlock
        {
            Text = bloco.Marcador,
            FontSize = 12.5,
            MinWidth = 16,
            VerticalAlignment = VerticalAlignment.Top,
            Classes = { "faint" },
        };

        var corpo = Texto(bloco, 12.5);
        corpo.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(corpo, 1);

        grade.Children.Add(marcador);
        grade.Children.Add(corpo);
        return grade;
    }

    private Control Paragrafo(BlocoMd bloco)
    {
        var texto = Texto(bloco, 12.5);
        texto.Margin = new Thickness(0, 0, 0, 8);
        return texto;
    }

    /// <summary>Monta os trechos como Inlines: é o que permite negrito no meio da frase.</summary>
    private SelectableTextBlock Texto(BlocoMd bloco, double tamanho)
    {
        var alvo = new SelectableTextBlock
        {
            FontSize = tamanho,
            TextWrapping = TextWrapping.Wrap,
            Inlines = new InlineCollection(),

            // com fundo, o bloco inteiro recebe o clique — não só onde há letra. Sem isso o
            // arraste que começava no espaço entre duas palavras não selecionava nada
            Background = Brushes.Transparent,
        };

        var links = new List<(int Inicio, int Fim, string Url)>();
        var posicao = 0;

        foreach (var trecho in bloco.Trechos)
        {
            var corrida = new Run(trecho.Texto);

            if (trecho.Negrito) corrida.FontWeight = FontWeight.SemiBold;
            if (trecho.Italico) corrida.FontStyle = FontStyle.Italic;

            if (trecho.Codigo)
            {
                corrida.FontFamily = this.TryFindResource("MonoFont", out var f) && f is FontFamily fam
                    ? fam
                    : FontFamily.Default;
                corrida.FontSize = tamanho - 1;
            }

            if (trecho.Link.Length > 0)
            {
                corrida.Foreground = this.TryFindResource("Accent", out var a) && a is IBrush pincel
                    ? pincel
                    : Brushes.SteelBlue;
                corrida.TextDecorations = TextDecorations.Underline;

                // Run não recebe evento por si: guarda-se onde o link fica no texto, e o
                // clique confere se caiu ali
                links.Add((posicao, posicao + trecho.Texto.Length, trecho.Link));
            }

            posicao += trecho.Texto.Length;
            alvo.Inlines!.Add(corrida);
        }

        alvo.ContextFlyout = null; // o menu é o do documento, que copia a seleção inteira
        _textos.Add(alvo);
        if (links.Count > 0) _links[alvo] = links;

        return alvo;
    }
}
