using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Services;

namespace GRepos.ViewModels;

public abstract class DiffRowBase : ObservableObject
{
    /// <summary>Bloco a que a linha pertence — é por bloco que se monta o patch.</summary>
    public Hunk? Hunk { get; init; }
}

/// <summary>Cabeçalho @@ do bloco, com as ações de preparar/remover o bloco ou só as linhas escolhidas.</summary>
public sealed partial class DiffHeaderRow : DiffRowBase
{
    private readonly Func<Task>? _apply;
    private readonly Func<Task>? _applyLinhas;

    public DiffHeaderRow(Func<Task>? apply, Func<Task>? applyLinhas = null)
    {
        _apply = apply;
        _applyLinhas = applyLinhas;
    }

    public string Header { get; init; } = "";
    public bool CanApply { get; init; }
    public string ApplyLabel { get; init; } = "";

    /// <summary>"Preparar" ou "Remover": o verbo do botão das linhas.</summary>
    public string Verbo { get; init; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemLinhas), nameof(LinhasRotulo), nameof(MostrarDica))]
    private int _escolhidas;

    public bool TemLinhas => Escolhidas > 0;
    public bool MostrarDica => CanApply && Escolhidas == 0;
    public string LinhasRotulo => $"{Verbo} {Escolhidas} linha(s)";

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_apply is not null) await _apply();
    }

    [RelayCommand]
    private async Task ApplyLinhasAsync()
    {
        if (_applyLinhas is not null) await _applyLinhas();
    }
}

/// <summary>Linha no modo unificado.</summary>
public sealed partial class DiffTextRow : DiffRowBase
{
    public DiffLine Line { get; init; } = new();

    public string OldNo => Line.OldNoText;
    public string NewNo => Line.NewNoText;
    public string Text => Line.Marker + DiffText.Expand(Line.Text);
    public bool IsAdd => Line.Kind == DiffLineKind.Add;
    public bool IsDel => Line.Kind == DiffLineKind.Del;

    [ObservableProperty] private bool _escolhida;
}

/// <summary>Linha no modo lado a lado.</summary>
public sealed partial class DiffSplitRow : DiffRowBase
{
    public SideRow Row { get; init; } = new();

    public string LeftNo => Row.Left?.OldNoText ?? "";
    public string RightNo => Row.Right?.NewNoText ?? "";
    public string LeftText => DiffText.Expand(Row.Left?.Text ?? "");
    public string RightText => DiffText.Expand(Row.Right?.Text ?? "");

    public bool LeftIsDel => Row.Left?.Kind == DiffLineKind.Del;
    public bool RightIsAdd => Row.Right?.Kind == DiffLineKind.Add;
    public bool LeftEmpty => Row.Left is null;
    public bool RightEmpty => Row.Right is null;

    [ObservableProperty] private bool _leftEscolhida;
    [ObservableProperty] private bool _rightEscolhida;
}

public static class DiffText
{
    public const int TabSize = 4;

    /// <summary>
    /// Expande tabulações em espaços. Sem isso as colunas do modo lado a lado
    /// desalinham e a largura calculada não bate com o que é desenhado.
    /// </summary>
    public static string Expand(string text)
    {
        if (!text.Contains('\t')) return text;

        var sb = new System.Text.StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c == '\t') sb.Append(' ', TabSize - sb.Length % TabSize);
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>Exibição de um diff: blocos achatados em linhas, modo lado a lado e ação por bloco.</summary>
public sealed partial class DiffViewModel : ObservableObject
{
    private ParsedDiff? _parsed;
    private string _applyLabel = "";
    private Func<string, Task>? _apply;

    [ObservableProperty] private ObservableCollection<DiffRowBase> _rows = new();
    [ObservableProperty] private string _emptyMessage = "Selecione um arquivo para ver as diferenças.";
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _split = true;

    /// <summary>
    /// Quebra a linha longa em vez de rolar na horizontal. Desligado por padrão: a
    /// quebra desalinha a indentação, e em código isso atrapalha mais do que ajuda.
    /// </summary>
    [ObservableProperty] private bool _wrap;

    /// <summary>Largura de um caractere da fonte monoespaçada, medida pela View.</summary>
    [ObservableProperty] private double _charWidth = 7.2;

    /// <summary>Largura útil do painel, informada pela View a cada redimensionamento.</summary>
    [ObservableProperty] private double _viewportWidth;

    /// <summary>
    /// Larguras das colunas de texto. São calculadas pelo conteúdo — com coluna
    /// proporcional o texto longo era cortado e não havia o que rolar na horizontal.
    /// </summary>
    [ObservableProperty] private double _leftWidth = 400;
    [ObservableProperty] private double _rightWidth = 400;
    [ObservableProperty] private double _unifiedWidth = 800;

    public bool HasContent => !IsEmpty;

    /// <summary>Quebra de linha do texto do diff, no formato que o TextBlock espera.</summary>
    public Avalonia.Media.TextWrapping Wrapping =>
        Wrap ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap;

    /// <summary>Com quebra de linha não sobra nada para rolar na horizontal.</summary>
    public Avalonia.Controls.Primitives.ScrollBarVisibility RolagemHorizontal =>
        Wrap ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
             : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(HasContent));

    partial void OnSplitChanged(bool value) => Rebuild();

    partial void OnWrapChanged(bool value)
    {
        OnPropertyChanged(nameof(Wrapping));
        OnPropertyChanged(nameof(RolagemHorizontal));
        MeasureColumns();
    }

    partial void OnCharWidthChanged(double value) => MeasureColumns();

    partial void OnViewportWidthChanged(double value) => MeasureColumns();

    public void Clear(string message)
    {
        _parsed = null;
        _escolhidas.Clear();
        _apply = null;
        Rows = new ObservableCollection<DiffRowBase>();
        EmptyMessage = message;
        IsEmpty = true;
    }

    /// <param name="apply">Ação por bloco; null deixa o diff somente leitura.</param>
    /// <param name="reverso">
    /// O patch será aplicado com --reverse (tirar do índice). Muda como as linhas não
    /// escolhidas entram no patch parcial.
    /// </param>
    public void Load(string raw, string applyLabel = "", Func<string, Task>? apply = null, bool reverso = false)
    {
        _applyLabel = applyLabel;
        _apply = apply;
        _reverso = reverso;
        _escolhidas.Clear();
        var diff = DiffParser.Parse(raw);

        if (diff.Binary)
        {
            Clear("Arquivo binário — sem visualização de diferenças.");
            return;
        }
        if (diff.Hunks.Count == 0)
        {
            Clear("Sem alterações de conteúdo neste arquivo.");
            return;
        }

        _parsed = diff;
        Rebuild();
    }

    // ----------------------------------------------- escolha de linhas
    // Clicar numa linha + ou − a marca; o cabeçalho do bloco ganha "Preparar N linha(s)".
    // A escolha sobrevive à troca entre lado a lado e unificado, mas não a um diff novo.

    private bool _reverso;
    private readonly Dictionary<Hunk, HashSet<DiffLine>> _escolhidas = new();

    private HashSet<DiffLine> Escolhidas(Hunk hunk)
    {
        if (!_escolhidas.TryGetValue(hunk, out var set)) _escolhidas[hunk] = set = new HashSet<DiffLine>();
        return set;
    }

    /// <summary>Marca ou desmarca a linha clicada. <paramref name="direita"/> vale no lado a lado.</summary>
    public void AlternarLinha(DiffRowBase row, bool direita)
    {
        if (_apply is null || row.Hunk is null) return;

        var linha = row switch
        {
            DiffTextRow t => t.Line,
            DiffSplitRow s => direita ? s.Row.Right : s.Row.Left,
            _ => null,
        };
        if (linha is null || linha.Kind is not (DiffLineKind.Add or DiffLineKind.Del)) return;

        var set = Escolhidas(row.Hunk);
        var marcada = set.Add(linha) || !set.Remove(linha);

        switch (row)
        {
            case DiffTextRow t: t.Escolhida = marcada; break;
            case DiffSplitRow s when direita: s.RightEscolhida = marcada; break;
            case DiffSplitRow s: s.LeftEscolhida = marcada; break;
        }

        foreach (var h in Rows.OfType<DiffHeaderRow>())
            if (h.Hunk == row.Hunk) h.Escolhidas = set.Count;
    }

    private void Rebuild()
    {
        if (_parsed is null) return;

        var rows = new List<DiffRowBase>();
        foreach (var hunk in _parsed.Hunks)
        {
            var parsed = _parsed;
            var patch = _apply is null ? null : DiffParser.BuildHunkPatch(parsed, hunk);
            var escolhidas = Escolhidas(hunk);
            rows.Add(new DiffHeaderRow(
                patch is null ? null : () => _apply!(patch),
                _apply is null ? null : () => _apply!(DiffParser.BuildLinesPatch(parsed, hunk, escolhidas, _reverso)))
            {
                Hunk = hunk,
                Header = hunk.Header,
                CanApply = _apply is not null,
                ApplyLabel = _applyLabel,
                Verbo = _reverso ? "Remover" : "Preparar",
                Escolhidas = escolhidas.Count,
            });

            if (Split)
            {
                foreach (var r in DiffParser.SideBySide(hunk))
                    rows.Add(new DiffSplitRow
                    {
                        Hunk = hunk,
                        Row = r,
                        LeftEscolhida = r.Left is { } esq && escolhidas.Contains(esq),
                        RightEscolhida = r.Right is { } dir && escolhidas.Contains(dir),
                    });
            }
            else
            {
                foreach (var l in hunk.Lines)
                {
                    if (l.Kind == DiffLineKind.NoNewline) continue;
                    rows.Add(new DiffTextRow { Hunk = hunk, Line = l, Escolhida = escolhidas.Contains(l) });
                }
            }
        }

        Rows = new ObservableCollection<DiffRowBase>(rows);
        IsEmpty = false;
        MeasureColumns();
    }

    /// <summary>
    /// Dimensiona as colunas pela linha mais longa, com folga de um caractere. Quando o
    /// conteúdo é curto, cada lado ocupa metade do painel em vez de ficar espremido.
    /// </summary>
    private void MeasureColumns()
    {
        const double padding = 18;
        const double splitGutters = 88;   // duas colunas de número, 44 cada
        const double unifiedGutters = 92; // duas colunas de número, 46 cada

        var half = Math.Max(0, (ViewportWidth - splitGutters) / 2);
        var full = Math.Max(0, ViewportWidth - unifiedGutters);
        var minimum = Math.Max(120, half);

        // Com quebra de linha a coluna é a que cabe na tela: medir pelo conteúdo
        // devolveria a largura da linha mais longa e a rolagem horizontal voltaria.
        if (Wrap)
        {
            LeftWidth = minimum;
            RightWidth = minimum;
            UnifiedWidth = Math.Max(120, full);
            return;
        }

        var left = 0;
        var right = 0;
        var unified = 0;

        foreach (var row in Rows)
        {
            switch (row)
            {
                case DiffSplitRow s:
                    if (s.LeftText.Length > left) left = s.LeftText.Length;
                    if (s.RightText.Length > right) right = s.RightText.Length;
                    break;
                case DiffTextRow t:
                    if (t.Text.Length > unified) unified = t.Text.Length;
                    break;
                case DiffHeaderRow h:
                    if (h.Header.Length > unified) unified = h.Header.Length;
                    break;
            }
        }

        LeftWidth = Math.Max(minimum, (left + 1) * CharWidth + padding);
        RightWidth = Math.Max(minimum, (right + 1) * CharWidth + padding);
        UnifiedWidth = Math.Max(Math.Max(120, full), (unified + 1) * CharWidth + padding);
    }
}
