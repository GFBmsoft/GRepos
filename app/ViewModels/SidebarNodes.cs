using CommunityToolkit.Mvvm.ComponentModel;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public abstract partial class SidebarNode : ObservableObject
{
}

/// <summary>Entrada do painel geral, no topo da árvore.</summary>
public sealed partial class PainelNode : SidebarNode
{
    public string Titulo { get; init; } = "Painel";
    public string Subtitulo { get; init; } = "";
}

/// <summary>Linha divisória: separa o painel dos grupos, sem virar item clicável.</summary>
public sealed partial class SeparadorNode : SidebarNode
{
}

public sealed partial class GroupNode : SidebarNode
{
    public string Id { get; init; } = "";

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _color = "#2F7BE8";
    [ObservableProperty] private bool _collapsed;
    [ObservableProperty] private int _count;

    /// <summary>
    /// Estilo minimalista: sem a pílula de fundo, com o nome na cor do grupo. A escolha
    /// chega pronta no nó porque a cor do texto depende do dado — um estilo de XAML não
    /// troca uma cor que vem do próprio item.
    /// </summary>
    public bool Minimalista { get; init; }

    public bool MostraPilula => !Minimalista;

    /// <summary>Seta do cabeçalho: para a direita quando recolhido, para baixo quando aberto.</summary>
    public string Chevron => Collapsed ? "" : "";

    partial void OnCollapsedChanged(bool value) => OnPropertyChanged(nameof(Chevron));
}

/// <summary>Título do par Origem × Destino: os dois repositórios sob uma linha só.</summary>
public sealed partial class PairNode : SidebarNode
{
    public string Key { get; init; } = "";
}

public sealed partial class RepoNode : SidebarNode
{
    public Repo Repo { get; init; } = new();

    [ObservableProperty] private RepoStatus? _status;
    [ObservableProperty] private bool _isPaired;

    /// <summary>Marcador redondo vazado no lugar do traço lateral, como no EV03.</summary>
    public bool Minimalista { get; init; }

    public bool MostraTraco => !Minimalista;
    public bool MostraPonto => Minimalista;

    /// <summary>Cor do grupo a que pertence — pinta o traço que liga ao cabeçalho.</summary>
    [ObservableProperty] private string _groupColor = "#2F7BE8";

    public string Id => Repo.Id;
    public string Name => Repo.Name;
    public string Path => Repo.Path;

    public string RoleTag => Repo.Role switch { "origem" => "ORI", "destino" => "DES", _ => "" };
    public bool HasRole => !string.IsNullOrEmpty(Repo.Role);

    public string Tooltip => Status?.Error is { Length: > 0 } e ? $"{Repo.Path}\n{e}" : Repo.Path;

    public bool HasError => Status?.Error is { Length: > 0 };
    public bool IsDirty => Status is { IsDirty: true };

    public bool ShowAhead => Status is { Ahead: > 0 };
    public bool ShowBehind => Status is { Behind: > 0 };
    public bool ShowConflict => Status is { Conflicted: > 0 };
    public bool ShowDirty => Status is { IsDirty: true, Conflicted: 0 };

    public string AheadText => $"↑{Status?.Ahead ?? 0}";
    public string BehindText => $"↓{Status?.Behind ?? 0}";
    public string ConflictText => $"!{Status?.Conflicted ?? 0}";
    public string DirtyText => $"●{(Status?.Staged ?? 0) + (Status?.Unstaged ?? 0) + (Status?.Untracked ?? 0)}";

    public void Refreshed() => OnPropertyChanged(string.Empty);
}
