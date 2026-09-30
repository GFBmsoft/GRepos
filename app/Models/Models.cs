using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GRepos.Models;

public sealed class Group
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4F8CFF";
    public bool Collapsed { get; set; }
}

public sealed class Repo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? GroupId { get; set; }

    /// <summary>Repositórios com a mesma chave são o mesmo módulo em bancos diferentes.</summary>
    public string? PairKey { get; set; }

    /// <summary>"origem" ou "destino"; só faz sentido com PairKey preenchida.</summary>
    public string? Role { get; set; }

    /// <summary>
    /// Modelo da URL do remoto com variáveis ({{user}}, {{token}}). Guarda o modelo,
    /// não o segredo: o token entra só na hora de aplicar no git.
    /// </summary>
    public string? RemoteTemplate { get; set; }
}

public sealed class Settings
{
    public string Accent { get; set; } = "#4F8CFF";
    public string Theme { get; set; } = "dark";
    public string Density { get; set; } = "compacta";
    public int AutoRefreshSeconds { get; set; } = 60;
    public int LogLimit { get; set; } = 300;
    public bool SplitDiff { get; set; } = true;

    /// <summary>Quebra linhas longas no diff em vez de rolar na horizontal.</summary>
    public bool WrapDiff { get; set; }

    /// <summary>Largura da sidebar em pixels, como o usuário deixou o divisor.</summary>
    public double SidebarWidth { get; set; } = 280;

    /// <summary>Aba aberta ao selecionar um repositório: "alteracoes" ou "historico".</summary>
    public string DefaultTab { get; set; } = "alteracoes";

    /// <summary>Usuário do GitHub. O token fica no gerenciador de credenciais, nunca aqui.</summary>
    public string GithubUser { get; set; } = "";

    /// <summary>Avisar quando sair uma release nova do próprio GRepos.</summary>
    public bool AvisarAtualizacao { get; set; } = true;

    /// <summary>Quando a release foi consultada pela última vez (ISO, UTC).</summary>
    public string UltimaChecagem { get; set; } = "";

    /// <summary>Tag vista na última consulta, para não bater na API a cada abertura.</summary>
    public string UltimaTagVista { get; set; } = "";
}

public sealed class Workspace
{
    public List<Group> Groups { get; set; } = new();
    public List<Repo> Repos { get; set; } = new();
    public Settings Settings { get; set; } = new();
}

public sealed class RepoStatus
{
    public string Branch { get; set; } = "";
    public string? Upstream { get; set; }
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public int Staged { get; set; }
    public int Unstaged { get; set; }
    public int Untracked { get; set; }
    public int Conflicted { get; set; }
    public int Stashes { get; set; }
    public string Head { get; set; } = "";
    public string? Error { get; set; }

    /// <summary>
    /// Arquivos distintos com alteração pendente. Os contadores acima somam situações
    /// (um arquivo preparado e alterado de novo conta nos dois), o que não serve para
    /// dizer "quantos arquivos mexi".
    /// </summary>
    public int PendingFiles { get; set; }

    [JsonIgnore]
    public int PendingCount => Staged + Unstaged + Untracked + Conflicted;

    [JsonIgnore]
    public bool IsDirty => PendingFiles > 0 || PendingCount > 0;
}

public enum ChangeKind { Tracked, Untracked, Conflict }

public sealed class FileChange
{
    public string Path { get; set; } = "";
    public string? OrigPath { get; set; }
    public string Index { get; set; } = ".";
    public string Worktree { get; set; } = ".";
    public ChangeKind Kind { get; set; }
}

public sealed class Commit
{
    public string Hash { get; set; } = "";
    public string Short => Hash.Length >= 7 ? Hash[..7] : Hash;
    public List<string> Parents { get; set; } = new();
    public string Author { get; set; } = "";
    public string Email { get; set; } = "";
    public string Date { get; set; } = "";
    public string Subject { get; set; } = "";
    public List<string> Refs { get; set; } = new();
}

public sealed class CommitFile
{
    public string Path { get; set; } = "";
    public int Added { get; set; }
    public int Removed { get; set; }
    public string Status { get; set; } = "M";
}

public sealed class CommitDetail
{
    public Commit Commit { get; set; } = new();
    public string Body { get; set; } = "";
    public List<CommitFile> Files { get; set; } = new();
}

public sealed class Branch
{
    public string Name { get; set; } = "";
    public bool IsHead { get; set; }
    public bool IsRemote { get; set; }
    public string? Upstream { get; set; }
    public string Subject { get; set; } = "";
}

public sealed class StashEntry
{
    public int Index { get; set; }
    public string Label { get; set; } = "";
    public string Subject { get; set; } = "";
}
