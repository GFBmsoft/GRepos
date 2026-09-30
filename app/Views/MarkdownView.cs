using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
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

    public MarkdownView() => Content = _pilha;

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

    private static Control Codigo(string texto)
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
        };

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
        };

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

                // link é clicável no bloco inteiro: Run não recebe evento por si
                alvo.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                var destino = trecho.Link;
                alvo.PointerPressed += (_, _) =>
                {
                    try { ShellService.AbrirUrl(destino); }
                    catch (Exception) { /* link quebrado no documento não é erro do app */ }
                };
            }

            alvo.Inlines!.Add(corrida);
        }

        return alvo;
    }
}
