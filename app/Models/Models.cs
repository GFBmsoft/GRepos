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

    /// <summary>
    /// Conta do GitHub deste repositório. Vazio é "automática": vale o usuário da URL do
    /// remoto e, sem ele, a conta principal das preferências.
    /// </summary>
    public string? Conta { get; set; }
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

    /// <summary>Árvore sem pílulas: marcador redondo e nome na cor do grupo.</summary>
    public bool ArvoreMinimalista { get; set; } = true;

    /// <summary>Quantas execuções a esteira mostra antes de recolher o resto.</summary>
    public int EsteirasVisiveis { get; set; } = 6;

    /// <summary>Largura da sidebar em pixels, como o usuário deixou o divisor.</summary>
    public double SidebarWidth { get; set; } = 280;

    /// <summary>Aba aberta ao selecionar um repositório: "alteracoes" ou "historico".</summary>
    public string DefaultTab { get; set; } = "alteracoes";

    /// <summary>
    /// Conta principal do GitHub: a que vale quando o repositório não diz outra. O token
    /// fica no gerenciador de credenciais, nunca aqui.
    /// </summary>
    public string GithubUser { get; set; } = "";

    /// <summary>
    /// Todas as contas cadastradas, a principal inclusive. Cada uma tem o próprio token
    /// no gerenciador de credenciais, que guarda por usuário.
    /// </summary>
    public List<string> GithubContas { get; set; } = new();

    /// <summary>Pasta onde o último clone foi criado; o próximo sugere a mesma.</summary>
    public string PastaDeClone { get; set; } = "";

    /// <summary>Avisar quando sair uma release nova do próprio GRepos.</summary>
    public bool AvisarAtualizacao { get; set; } = true;

    /// <summary>Quando a release foi consultada pela última vez (ISO, UTC).</summary>
    public string UltimaChecagem { get; set; } = "";

    /// <summary>Tag vista na última consulta, para não bater na API a cada abertura.</summary>
    public string UltimaTagVista { get; set; } = "";

    /// <summary>
    /// Última contagem de linhas do cartão de perfil, por conta (login em minúsculas).
    /// Contar é caro; sem guardar, o número sumia a cada vez que o painel era remontado.
    /// </summary>
    public Dictionary<string, ContagemDeLinhas> Linhas { get; set; } = new();

    /// <summary>
    /// Pasta de instalação do Git (ou o caminho do git-bash.exe) para o botão Terminal.
    /// Vazio procura sozinho no PATH e nas pastas padrão do instalador.
    /// </summary>
    public string GitBashPath { get; set; } = "";

    /// <summary>
    /// Pasta de instalação (ou o .exe) da ferramenta de comparação: Beyond Compare,
    /// WinMerge, Meld… Vazio procura sozinho nas pastas padrão dos instaladores.
    /// </summary>
    public string DiffExternoPath { get; set; } = "";

    /// <summary>
    /// Linha de comando da ferramenta, com <c>$LOCAL</c> e <c>$REMOTE</c>. Vazio usa a
    /// padrão da ferramenta reconhecida.
    /// </summary>
    public string DiffExternoArgs { get; set; } = "";

    /// <summary>Obter traz todas as tags do remoto, não só as dos commits trazidos.</summary>
    public bool BuscarTodasAsTags { get; set; } = true;

    /// <summary>
    /// Depois de obter ou puxar, cria uma branch local para cada remota e avança as
    /// locais que só estão atrás — como o "todas as branches" do Sourcetree.
    /// </summary>
    public bool SincronizarTodasAsBranches { get; set; }
}

public sealed class ContagemDeLinhas
{
    public long Adicionadas { get; set; }
    public long Removidas { get; set; }

    /// <summary>Quando foi contado (ISO, UTC) — o cartão mostra a data junto do número.</summary>
    public string Quando { get; set; } = "";
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

    /// <summary>
    /// Só em conflito: o XY do git (UU, AA, DU, UD…). X é o lado "ours", Y o "theirs";
    /// D diz que aquele lado apagou o arquivo.
    /// </summary>
    public string Conflito { get; set; } = "";
}

/// <summary>Uma linha do blame: quem a deixou como está, quando e em que commit.</summary>
public sealed class BlameLine
{
    public string Hash { get; set; } = "";
    public int Linha { get; set; }
    public string Autor { get; set; } = "";

    /// <summary>Data do autor em segundos Unix.</summary>
    public long Quando { get; set; }
    public string Assunto { get; set; } = "";
    public string Texto { get; set; } = "";

    /// <summary>Linha alterada e ainda não commitada: o git usa um hash de zeros.</summary>
    public bool NaoCommitada => Hash.Length > 0 && Hash.Trim('0').Length == 0;
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
