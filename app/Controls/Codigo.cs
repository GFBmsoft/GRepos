using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using GRepos.Services;
using GRepos.ViewModels;

namespace GRepos.Controls;

/// <summary>
/// Liga uma <see cref="LinhaRealcada"/> a um TextBlock: o texto vira pedaços coloridos
/// pelo realce de sintaxe. Linha sem trechos continua sendo texto simples, que é mais
/// barato — e é o caso de quase todo arquivo que não é código.
/// </summary>
public sealed class Codigo : AvaloniaObject
{
    public static readonly AttachedProperty<LinhaRealcada?> LinhaProperty =
        AvaloniaProperty.RegisterAttached<Codigo, TextBlock, LinhaRealcada?>("Linha");

    public static LinhaRealcada? GetLinha(TextBlock alvo) => alvo.GetValue(LinhaProperty);
    public static void SetLinha(TextBlock alvo, LinhaRealcada? valor) => alvo.SetValue(LinhaProperty, valor);

    static Codigo()
    {
        LinhaProperty.Changed.AddClassHandler<TextBlock>((alvo, e) => Aplicar(alvo, e.NewValue as LinhaRealcada));
    }

    /// <summary>Recurso de cor de cada tipo de trecho, definido nos dois temas.</summary>
    public static string Recurso(TipoTrecho tipo) => tipo switch
    {
        TipoTrecho.Palavra => "SynPalavra",
        TipoTrecho.Literal => "SynLiteral",
        TipoTrecho.Comentario => "SynComentario",
        TipoTrecho.Numero => "SynNumero",
        _ => "SynFuncao",
    };

    private static void Aplicar(TextBlock alvo, LinhaRealcada? linha)
    {
        alvo.Inlines?.Clear();
        if (linha is null || linha.Trechos.Count == 0)
        {
            alvo.Text = linha?.Texto ?? "";
            return;
        }

        var texto = linha.Texto;
        var inlines = new InlineCollection();
        var pos = 0;
        foreach (var t in linha.Trechos)
        {
            // trecho fora do texto não deveria existir; se existir, o resto sai sem cor
            if (t.Inicio < pos || t.Inicio + t.Tamanho > texto.Length) break;
            if (t.Inicio > pos) inlines.Add(new Run(texto[pos..t.Inicio]));

            var run = new Run(texto.Substring(t.Inicio, t.Tamanho));
            if (Cor(Recurso(t.Tipo)) is { } cor) run.Foreground = cor;
            inlines.Add(run);
            pos = t.Inicio + t.Tamanho;
        }
        if (pos < texto.Length) inlines.Add(new Run(texto[pos..]));

        alvo.Inlines = inlines;
    }

    // Do aplicativo e não do controle: a linha é montada antes de entrar na árvore, e ali
    // o TextBlock ainda não enxerga os recursos do tema.
    private static IBrush? Cor(string chave) =>
        Application.Current is { } app && app.TryGetResource(chave, app.ActualThemeVariant, out var r)
            ? r as IBrush
            : null;
}
