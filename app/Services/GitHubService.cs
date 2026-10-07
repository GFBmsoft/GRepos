using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Resultado da última execução do GitHub Actions para uma branch.</summary>
public sealed class CiRun
{
    /// <summary>"sucesso", "falha", "rodando", "cancelado", "nenhum" ou "indisponivel".</summary>
    public string Situacao { get; init; } = "nenhum";
    public string Workflow { get; init; } = "";
    public string Url { get; init; } = "";
    public string Detalhe { get; init; } = "";
}

/// <summary>Arquivo anexado a uma release.</summary>
public sealed class ReleaseAsset
{
    public string Nome { get; init; } = "";
    public long Tamanho { get; init; }
    public string Url { get; init; } = "";
}

/// <summary>Release publicada no GitHub — a fonte da atualização do próprio app.</summary>
public sealed class Release
{
    public string Tag { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Url { get; init; } = "";

    /// <summary>Corpo das notas, em markdown — é o changelog da versão.</summary>
    public string Notas { get; init; } = "";
    public DateTime? Publicada { get; init; }
    public List<ReleaseAsset> Arquivos { get; init; } = new();

    /// <summary>
    /// O executável que não precisa de nada instalado. É o único que serve para trocar
    /// sozinho: o outro depende do .NET 8 estar na máquina de destino.
    /// </summary>
    public ReleaseAsset? Standalone => Arquivos.FirstOrDefault(a =>
        a.Nome.EndsWith("-standalone.exe", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Conta do GitHub, no estilo do cartão de perfil.</summary>
public sealed class Perfil
{
    public string Login { get; init; } = "";
    public string Nome { get; init; } = "";
    public string Bio { get; init; } = "";
    public string Local { get; init; } = "";
    public string Empresa { get; init; } = "";
    public int RepositoriosPublicos { get; init; }
    public int Seguidores { get; init; }
    public int Seguindo { get; init; }

    /// <summary>Endereço da foto da conta, como o GitHub devolve.</summary>
    public string AvatarUrl { get; init; } = "";

    /// <summary>Somadas dos repositórios próprios; -1 enquanto não foi consultado.</summary>
    public int Estrelas { get; init; } = -1;

    /// <summary>Linguagens mais frequentes nos repositórios, da mais para a menos usada.</summary>
    public IReadOnlyList<string> Linguagens { get; init; } = Array.Empty<string>();
}

/// <summary>Um dia do calendário de contribuições; Nivel vai de 0 (nada) a 4 (o máximo).</summary>
public sealed record DiaContribuicao(DateTime Data, int Quantidade, int Nivel);

/// <summary>O último ano de contribuições da conta, dia a dia, do domingo mais antigo ao hoje.</summary>
public sealed record Contribuicoes(int Total, IReadOnlyList<DiaContribuicao> Dias);

/// <summary>Pull request, como aparece no cartão do painel.</summary>
public sealed class PullRequest
{
    public int Numero { get; init; }
    public string Titulo { get; init; } = "";
    public string Autor { get; init; } = "";
    public string Url { get; init; } = "";
    public bool Rascunho { get; init; }

    /// <summary>"aberto", "mesclado" ou "fechado".</summary>
    public string Estado { get; init; } = "aberto";

    public DateTime? Atualizado { get; init; }

    public bool Aberto => Estado == "aberto";

    /// <summary>Branch de onde o PR sai (head) e para onde vai (base).</summary>
    public string Origem { get; init; } = "";
    public string Destino { get; init; } = "";

    /// <summary>Commit da ponta da origem: é por ele que se consultam as verificações.</summary>
    public string Sha { get; init; } = "";
    public string Corpo { get; init; } = "";

    // só na consulta de um PR; a listagem não traz estes campos
    public int Commits { get; init; }
    public int Arquivos { get; init; }
    public int Adicionadas { get; init; }
    public int Removidas { get; init; }

    /// <summary>
    /// "clean", "dirty" (conflito), "blocked", "behind", "unstable", "draft" ou
    /// "unknown" — o GitHub calcula em segundo plano e pode demorar a responder.
    /// </summary>
    public string Mesclagem { get; init; } = "";

    /// <summary>Comentários da conversa mais os feitos em linhas do código.</summary>
    public int Comentarios { get; init; }
}

/// <summary>Etiqueta (label) de uma issue: "bug", "enhancement"… com a cor que o GitHub dá a ela.</summary>
public sealed record Etiqueta(string Nome, string Cor);

/// <summary>
/// Uma fala na conversa de um PR ou de uma issue: comentário, revisão (aprovou, pediu
/// mudanças) ou comentário numa linha do código.
/// </summary>
public sealed class Comentario
{
    public string Autor { get; init; } = "";
    public string Corpo { get; init; } = "";
    public DateTime? Quando { get; init; }

    /// <summary>"comentou", "aprovou", "pediu mudanças", "revisou" ou "comentou no código".</summary>
    public string Tipo { get; init; } = "comentou";

    /// <summary>Identificador no GitHub; zero quando não veio (não dá para editar).</summary>
    public long Id { get; init; }

    /// <summary>"conversa", "codigo" ou "revisao": cada um é editado por um endereço diferente da API.</summary>
    public string Origem { get; init; } = "conversa";

    /// <summary>Arquivo e linha, quando o comentário é no código.</summary>
    public string Onde { get; init; } = "";
}

public sealed class CommitDoPr
{
    public string Sha { get; init; } = "";
    public string Assunto { get; init; } = "";
    public string Autor { get; init; } = "";
    public DateTime? Quando { get; init; }
}

/// <summary>Um arquivo mexido pelo PR, com o trecho de diff que a API devolve.</summary>
public sealed class ArquivoDoPr
{
    public string Caminho { get; init; } = "";

    /// <summary>"A", "M", "D" ou "R", como no resto do app.</summary>
    public string Status { get; init; } = "M";
    public int Adicionadas { get; init; }
    public int Removidas { get; init; }

    /// <summary>Vazio em arquivo binário ou grande demais para a API mandar.</summary>
    public string Patch { get; init; } = "";
}

public sealed class Issue
{
    public int Numero { get; init; }
    public string Titulo { get; init; } = "";
    public string Autor { get; init; } = "";
    public string Corpo { get; init; } = "";
    public string Url { get; init; } = "";
    public bool Aberta { get; init; } = true;
    public int Comentarios { get; init; }
    public DateTime? Criada { get; init; }
    public DateTime? Atualizada { get; init; }
    public List<Etiqueta> Etiquetas { get; init; } = new();
    public List<string> Responsaveis { get; init; } = new();
}

/// <summary>Uma verificação (check run) do commit de um PR.</summary>
public sealed class Verificacao
{
    public string Nome { get; init; } = "";
    public string Situacao { get; init; } = "nenhum";
    public string Url { get; init; } = "";

    public DateTime? Iniciada { get; init; }
    public DateTime? Concluida { get; init; }

    /// <summary>
    /// Execução do Actions por trás da verificação, tirada do link de detalhes
    /// (".../actions/runs/123/job/456"). Zero quando a verificação vem de outro serviço.
    /// </summary>
    public long RunId { get; init; }
}

/// <summary>Uma execução do GitHub Actions, como aparece no cartão da esteira.</summary>
public sealed class CiExecucao
{
    public long Id { get; init; }
    public int Numero { get; init; }
    public string Situacao { get; init; } = "nenhum";
    public string Workflow { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Branch { get; init; } = "";
    public string Autor { get; init; } = "";
    public string Url { get; init; } = "";
    public DateTime? Criada { get; init; }
    public DateTime? Atualizada { get; init; }
}

/// <summary>Um passo de um job: é o detalhe que o usuário abre para achar o que quebrou.</summary>
public sealed class CiEtapa
{
    public int Numero { get; init; }
    public string Nome { get; init; } = "";
    public string Situacao { get; init; } = "nenhum";
    public TimeSpan? Duracao { get; init; }
}

/// <summary>Job de uma execução, com seus passos em ordem.</summary>
public sealed class CiJob
{
    public string Nome { get; init; } = "";
    public string Situacao { get; init; } = "nenhum";
    public string Url { get; init; } = "";
    public TimeSpan? Duracao { get; init; }
    public List<CiEtapa> Etapas { get; init; } = new();
}

/// <summary>
/// Status da esteira pela API do GitHub. O token sai do credential manager que o
/// próprio git usa — nada de pedir senha nem guardar credencial aqui.
/// </summary>
public static class GitHubService
{
    private static readonly HttpClient Http = CriarCliente();

    /// <summary>Resposta guardada por um tempo: a API tem limite por hora.</summary>
    private static readonly ConcurrentDictionary<string, (DateTime Quando, CiRun Run)> Cache = new();
    private static readonly TimeSpan Validade = TimeSpan.FromSeconds(90);

    /// <summary>Token por usuário: o credential manager guarda um para cada conta.</summary>
    private static readonly ConcurrentDictionary<string, string?> Tokens = new();

    private static HttpClient CriarCliente()
    {
        // o prazo de verdade é por tentativa, no PrazoHttp; este é só o teto geral
        var http = new HttpClient(new PrazoHttp(new HttpClientHandler())) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GRepos", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>"owner/repo" quando a URL aponta para o GitHub; null caso contrário.</summary>
    public static string? Slug(string remoteUrl)
    {
        var url = GitService.WebUrl(remoteUrl);
        const string marca = "github.com/";
        var i = url.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;

        var caminho = url[(i + marca.Length)..].Trim('/');
        var partes = caminho.Split('/');
        return partes.Length >= 2 && partes[0].Length > 0 && partes[1].Length > 0
            ? $"{partes[0]}/{partes[1]}"
            : null;
    }

    /// <summary>
    /// Conta de um repositório com várias contas cadastradas: a escolhida para ele, se
    /// houver; senão a que a URL do remoto indica (ver <see cref="Usuario"/>).
    /// </summary>
    public static string ContaDoRepositorio(string? escolhida, string remoteUrl) =>
        string.IsNullOrWhiteSpace(escolhida) ? Usuario(remoteUrl) : escolhida.Trim();

    /// <summary>
    /// Usuário embutido na URL do remoto (https://GFBmsoft@github.com/...). O credential
    /// manager guarda a credencial por conta, e sem o usuário ele não acha nada.
    ///
    /// Sem usuário na URL vale a conta configurada no app: consultar sem nenhuma manda a
    /// chamada anônima, que tem cota de 60 por hora para a máquina inteira — o painel
    /// esgota isso em minutos, e aí a esteira de todo mundo some.
    /// </summary>
    public static string Usuario(string remoteUrl)
    {
        var i = remoteUrl.IndexOf("://", StringComparison.Ordinal);
        if (i < 0) return remoteUrl.Length > 0 ? GitService.CredentialUser : "";
        var resto = remoteUrl[(i + 3)..];
        var arroba = resto.IndexOf('@');
        var barra = resto.IndexOf('/');
        if (arroba <= 0 || (barra >= 0 && barra < arroba)) return GitService.CredentialUser;

        var usuario = resto[..arroba];
        var doisPontos = usuario.IndexOf(':'); // usuário:senha
        if (doisPontos > 0) return usuario[..doisPontos];

        // "https://ghp_xxx@github.com/...", como o SourceTree deixa: o que está antes do @
        // é o token, não uma conta. Procurar credencial com ele como usuário não acha
        // nada, e a API era chamada sem autenticação — repositório privado não respondia
        // e a cota anônima acabava. Vale a conta configurada no app.
        return UrlTemplate.SegredoEmbutido(remoteUrl) == usuario ? GitService.CredentialUser : usuario;
    }

    /// <summary>
    /// Token do Git Credential Manager. GCM_INTERACTIVE=never e GIT_TERMINAL_PROMPT=0
    /// garantem que nada abra janela de login: sem credencial salva, simplesmente falha.
    /// </summary>
    private static async Task<string?> TokenAsync(string usuario)
    {
        if (Tokens.TryGetValue(usuario, out var guardado)) return guardado;

        string? token = null;
        try
        {
            var entrada = "protocol=https\nhost=github.com\n" +
                          (usuario.Length > 0 ? $"username={usuario}\n" : "") + "\n";

            var saida = await GitService.RunWithEnvAsync(
                System.IO.Path.GetTempPath(),
                new[] { "credential", "fill" },
                entrada,
                ("GCM_INTERACTIVE", "never"), ("GIT_TERMINAL_PROMPT", "0"));

            foreach (var linha in saida.Split('\n'))
                if (linha.StartsWith("password=", StringComparison.Ordinal))
                    token = linha[9..].Trim();
        }
        catch (Exception)
        {
            token = null; // repositório público ainda funciona sem token
        }

        Tokens[usuario] = token;
        return token;
    }

    /// <summary>
    /// Guarda o token no gerenciador de credenciais do Windows, pelo próprio git.
    /// O token nunca entra no workspace.json — lá fica só o nome de usuário.
    /// </summary>
    public static async Task SalvarCredencialAsync(string usuario, string token)
    {
        if (string.IsNullOrWhiteSpace(usuario)) throw new ArgumentException("Informe o usuário.");
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Informe o token.");

        await GitService.RunWithEnvAsync(
            System.IO.Path.GetTempPath(),
            new[] { "credential", "approve" },
            $"protocol=https\nhost=github.com\nusername={usuario.Trim()}\npassword={token.Trim()}\n\n",
            ("GCM_INTERACTIVE", "never"), ("GIT_TERMINAL_PROMPT", "0"));

        Tokens[usuario.Trim()] = token.Trim();
        Cache.Clear();
    }

    public static async Task RemoverCredencialAsync(string usuario)
    {
        await GitService.RunWithEnvAsync(
            System.IO.Path.GetTempPath(),
            new[] { "credential", "reject" },
            $"protocol=https\nhost=github.com\nusername={usuario.Trim()}\n\n",
            ("GCM_INTERACTIVE", "never"), ("GIT_TERMINAL_PROMPT", "0"));

        Tokens.TryRemove(usuario.Trim(), out _);
        Cache.Clear();
    }

    /// <summary>
    /// Helper de credenciais que o git realmente vai usar. A leitura é sem escopo de
    /// propósito: o Git for Windows instala o "manager" no gitconfig do sistema, e
    /// perguntar só pelo --global dizia "nenhum" numa máquina que tinha helper.
    /// Vazio aqui significa mesmo que nada será guardado entre um push e outro.
    /// </summary>
    public static async Task<string> HelperAsync()
    {
        try
        {
            var saida = await GitService.RunWithEnvAsync(
                System.IO.Path.GetTempPath(),
                new[] { "config", "--get-all", "credential.helper" },
                null,
                ("GCM_INTERACTIVE", "never"), ("GIT_TERMINAL_PROMPT", "0"));

            return string.Join(", ", saida
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        catch (Exception)
        {
            return ""; // "não configurado" sai do git como código de erro
        }
    }

    /// <summary>Nome do helper do Windows. O git &gt;= 2.39 chama de "manager".</summary>
    public const string HelperPadrao = "manager";

    /// <summary>
    /// Aponta o git global para o Gerenciador de Credenciais do Windows. É o que faz a
    /// autenticação ser pedida uma vez só: sem helper, o git esquece o token a cada push.
    /// </summary>
    public static async Task ConfigurarHelperAsync()
    {
        await GitService.RunWithEnvAsync(
            System.IO.Path.GetTempPath(),
            new[] { "config", "--global", "credential.helper", HelperPadrao },
            null,
            ("GCM_INTERACTIVE", "never"), ("GIT_TERMINAL_PROMPT", "0"));

        Tokens.Clear();
    }

    /// <summary>
    /// Pergunta ao helper se já existe credencial guardada para o usuário — a mesma
    /// consulta que o push faria, sem chance de abrir janela.
    /// </summary>
    public static async Task<bool> TemCredencialAsync(string usuario)
    {
        Tokens.TryRemove(usuario.Trim(), out _);
        return !string.IsNullOrEmpty(await TokenAsync(usuario.Trim()));
    }

    public static bool TemTokenGuardado(string usuario) =>
        Tokens.TryGetValue(usuario, out var t) && !string.IsNullOrEmpty(t);

    /// <summary>Token salvo para o usuário (usado ao expandir {{token}} na URL do remoto).</summary>
    public static Task<string?> TokenDoUsuarioAsync(string usuario) => TokenAsync(usuario);

    /// <summary>Confere o token contra a API e devolve o login e o nome da conta.</summary>
    /// <param name="tokenInformado">
    /// Token digitado na tela, ainda não salvo. Testar o que está na caixa é o que o
    /// usuário espera do botão — sem isso ele conferiria a credencial antiga.
    /// </param>
    public static async Task<string> TestarAsync(string usuario, string? tokenInformado = null)
    {
        var token = string.IsNullOrWhiteSpace(tokenInformado)
            ? await TokenAsync(usuario)
            : tokenInformado.Trim();

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                "Nenhum token guardado para este usuário. Cole o token no campo acima e clique " +
                "em Testar, ou em Salvar token para guardá-lo antes.");

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub recusou o token (HTTP {(int)resp.StatusCode}).");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var login = Texto(doc.RootElement, "login");
        var nome = Texto(doc.RootElement, "name");
        return nome.Length > 0 ? $"{login} ({nome})" : login;
    }

    /// <summary>Esquece o token em memória, forçando nova leitura do gerenciador.</summary>
    public static void EsquecerTokens() => Tokens.Clear();

    public static async Task<CiRun> UltimaExecucaoAsync(string slug, string branch, string usuario = "")
    {
        var chave = slug + "@" + branch;
        if (Cache.TryGetValue(chave, out var guardado) && DateTime.UtcNow - guardado.Quando < Validade)
            return guardado.Run;

        var run = await ConsultarAsync(slug, branch, usuario);
        Cache[chave] = (DateTime.UtcNow, run);
        return run;
    }

    public static void LimparCache() => Cache.Clear();

    /// <summary>
    /// Qual execução representa o repositório na barra: a mais recente da branch, a não
    /// ser que exista outra **ainda rodando**, mais nova, em qualquer branch.
    ///
    /// O segundo caso é o build de tag: o GitHub põe o nome da tag no head_branch, então
    /// um build da 1.0.0.10 não é "main" e some do filtro por branch — mas é justamente
    /// o que o usuário acabou de disparar e quer ver andando.
    /// </summary>
    public static CiExecucao? EscolherExecucao(IReadOnlyList<CiExecucao> execucoes, string branch)
    {
        if (execucoes.Count == 0) return null;

        var daBranch = branch.Length == 0
            ? execucoes[0]
            : execucoes.FirstOrDefault(e => e.Branch == branch);

        // "mais nova" pela ordem da resposta, que vem da mais recente para a mais antiga
        var posRodando = Posicao(execucoes, e => e.Situacao == "rodando");
        if (posRodando < 0) return daBranch ?? execucoes[0];
        if (daBranch is null) return execucoes[posRodando];

        var posBranch = Posicao(execucoes, e => ReferenceEquals(e, daBranch));
        return posRodando < posBranch ? execucoes[posRodando] : daBranch;
    }

    private static int Posicao(IReadOnlyList<CiExecucao> lista, Func<CiExecucao, bool> criterio)
    {
        for (var i = 0; i < lista.Count; i++)
            if (criterio(lista[i])) return i;
        return -1;
    }

    // ------------------------------------------------------ esteira detalhada

    /// <summary>
    /// Últimas execuções da branch (ou de todas, quando a branch vem vazia). É a lista
    /// de cartões da janela da esteira.
    /// </summary>
    public static async Task<List<CiExecucao>> ExecucoesAsync(
        string slug, string branch, string usuario = "", int limite = 12)
    {
        var url = $"https://api.github.com/repos/{slug}/actions/runs?per_page={limite}" +
                  (string.IsNullOrEmpty(branch) ? "" : $"&branch={Uri.EscapeDataString(branch)}");

        return LerExecucoes(await BaixarAsync(url, usuario));
    }

    /// <summary>Jobs e passos de uma execução — o passo a passo do cartão aberto.</summary>
    public static async Task<List<CiJob>> JobsAsync(string slug, long runId, string usuario = "")
    {
        var url = $"https://api.github.com/repos/{slug}/actions/runs/{runId}/jobs?per_page=100";
        return LerJobs(await BaixarAsync(url, usuario));
    }

    private static async Task<string> BaixarAsync(string url, string usuario, string oque = "a esteira")
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var token = await TokenAsync(usuario);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GitHub respondeu {(int)resp.StatusCode} ao consultar {oque}." +
                (resp.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? " Repositório privado costuma exigir token em Preferências → Autenticação."
                    : ""));

        return await resp.Content.ReadAsStringAsync();
    }

    /// <summary>Separado da rede para poder ser testado com uma resposta de verdade.</summary>
    public static List<CiExecucao> LerExecucoes(string json)
    {
        var lista = new List<CiExecucao>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("workflow_runs", out var runs)) return lista;

        foreach (var r in runs.EnumerateArray())
        {
            lista.Add(new CiExecucao
            {
                Id = r.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : 0,
                Numero = r.TryGetProperty("run_number", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetInt32() : 0,
                Situacao = Traduzir(Texto(r, "status"), Texto(r, "conclusion")),
                Workflow = Texto(r, "name"),
                Titulo = Texto(r, "display_title"),
                Branch = Texto(r, "head_branch"),
                Autor = r.TryGetProperty("actor", out var a) ? Texto(a, "login") : "",
                Url = Texto(r, "html_url"),
                Criada = Data(r, "run_started_at") ?? Data(r, "created_at"),
                Atualizada = Data(r, "updated_at"),
            });
        }
        return lista;
    }

    public static List<CiJob> LerJobs(string json)
    {
        var lista = new List<CiJob>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("jobs", out var jobs)) return lista;

        foreach (var j in jobs.EnumerateArray())
        {
            var etapas = new List<CiEtapa>();
            if (j.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
                foreach (var s in steps.EnumerateArray())
                    etapas.Add(new CiEtapa
                    {
                        Numero = s.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number
                            ? n.GetInt32() : etapas.Count + 1,
                        Nome = Texto(s, "name"),
                        Situacao = Traduzir(Texto(s, "status"), Texto(s, "conclusion")),
                        Duracao = Intervalo(s),
                    });

            lista.Add(new CiJob
            {
                Nome = Texto(j, "name"),
                Situacao = Traduzir(Texto(j, "status"), Texto(j, "conclusion")),
                Url = Texto(j, "html_url"),
                Duracao = Intervalo(j),
                Etapas = etapas,
            });
        }
        return lista;
    }

    // ------------------------------------------------------------- atualização

    /// <summary>
    /// Última release publicada. Repositório público não exige token; se houver um
    /// guardado ele é usado só para não esbarrar no limite por hora da API.
    /// </summary>
    public static async Task<Release?> UltimaReleaseAsync(string slug, string usuario = "")
    {
        var json = await BaixarAsync($"https://api.github.com/repos/{slug}/releases/latest", usuario);
        return LerRelease(json);
    }

    // ------------------------------------------------ disparar e reexecutar

    /// <summary>Um workflow do repositório, para escolher qual disparar.</summary>
    public sealed class Workflow
    {
        public long Id { get; init; }
        public string Nome { get; init; } = "";
        public string Arquivo { get; init; } = "";

        /// <summary>Workflow desativado não aceita disparo.</summary>
        public bool Ativo { get; init; } = true;
    }

    public static async Task<List<Workflow>> WorkflowsAsync(string slug, string usuario = "")
    {
        var json = await BaixarAsync($"https://api.github.com/repos/{slug}/actions/workflows", usuario);
        return LerWorkflows(json);
    }

    public static List<Workflow> LerWorkflows(string json)
    {
        var lista = new List<Workflow>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("workflows", out var ws)) return lista;

        foreach (var w in ws.EnumerateArray())
            lista.Add(new Workflow
            {
                Id = w.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                    ? id.GetInt64() : 0,
                Nome = Texto(w, "name"),
                Arquivo = System.IO.Path.GetFileName(Texto(w, "path")),
                Ativo = Texto(w, "state") == "active",
            });

        return lista;
    }

    /// <summary>
    /// POST autenticado. Diferente de todo o resto desta classe, estas chamadas
    /// **escrevem** no repositório: exigem token com permissão de Actions, e sem ela o
    /// GitHub responde 403 — a mensagem diz isso em vez de repetir o código.
    /// </summary>
    private static async Task EnviarAsync(string url, string? corpo, string usuario)
    {
        var token = await TokenAsync(usuario);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                "Nenhum token encontrado. Configure em Preferências → Autenticação.");

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null)
            req.Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        if (resp.IsSuccessStatusCode) return;

        var detalhe = (int)resp.StatusCode switch
        {
            401 => "o token não foi aceito.",
            403 => "o token não tem permissão de escrita em Actions. " +
                   "Um PAT clássico precisa do escopo \"workflow\"; um fine-grained, " +
                   "de \"Actions: Read and write\" neste repositório.",
            404 => "workflow ou execução não encontrada — ou o token não enxerga este repositório.",
            422 => "o GitHub recusou: o workflow precisa declarar \"workflow_dispatch\" " +
                   "e existir na branch escolhida.",
            _ => $"o GitHub respondeu {(int)resp.StatusCode}.",
        };

        throw new InvalidOperationException("Não foi possível executar: " + detalhe);
    }

    /// <summary>Dispara um workflow numa branch ou tag (precisa de "workflow_dispatch").</summary>
    public static Task DispararWorkflowAsync(string slug, long workflowId, string referencia, string usuario = "")
    {
        if (string.IsNullOrWhiteSpace(referencia))
            throw new InvalidOperationException("Informe a branch ou tag para executar.");

        var corpo = JsonSerializer.Serialize(new { @ref = referencia.Trim() });
        return EnviarAsync(
            $"https://api.github.com/repos/{slug}/actions/workflows/{workflowId}/dispatches",
            corpo, usuario);
    }

    /// <param name="somenteFalhas">Reexecuta só os jobs que falharam, não a execução toda.</param>
    public static Task ReexecutarAsync(string slug, long runId, string usuario = "", bool somenteFalhas = false)
    {
        var acao = somenteFalhas ? "rerun-failed-jobs" : "rerun";
        return EnviarAsync(
            $"https://api.github.com/repos/{slug}/actions/runs/{runId}/{acao}", null, usuario);
    }

    /// <summary>Pede ao GitHub para parar uma execução em andamento.</summary>
    public static Task CancelarAsync(string slug, long runId, string usuario = "") =>
        EnviarAsync($"https://api.github.com/repos/{slug}/actions/runs/{runId}/cancel", null, usuario);

    // --------------------------------------------------------------- perfil

    /// <summary>
    /// Conta do GitHub com o que dá para saber em duas chamadas: os dados do usuário e
    /// a lista de repositórios, de onde saem estrelas somadas e linguagens.
    ///
    /// Total de commits fica de fora de propósito: só sai pela API de busca, que tem
    /// limite próprio e bem mais apertado, e erraria com frequência.
    /// </summary>
    public static async Task<Perfil> PerfilAsync(string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            throw new InvalidOperationException(
                "Informe o usuário do GitHub em Preferências → Autenticação.");

        login = login.Trim();
        var perfil = LerPerfil(await BaixarAsync($"https://api.github.com/users/{login}", login));

        try
        {
            var repos = await BaixarAsync(
                $"https://api.github.com/users/{login}/repos?per_page=100&type=owner", login);
            var (estrelas, linguagens) = LerEstatisticasDeRepos(repos);

            return new Perfil
            {
                Login = perfil.Login,
                Nome = perfil.Nome,
                Bio = perfil.Bio,
                Local = perfil.Local,
                Empresa = perfil.Empresa,
                RepositoriosPublicos = perfil.RepositoriosPublicos,
                Seguidores = perfil.Seguidores,
                Seguindo = perfil.Seguindo,
                AvatarUrl = perfil.AvatarUrl,
                Estrelas = estrelas,
                Linguagens = linguagens,
            };
        }
        catch (Exception)
        {
            return perfil; // sem a lista de repositórios o resto ainda vale
        }
    }

    public static Perfil LerPerfil(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        return new Perfil
        {
            Login = Texto(r, "login"),
            Nome = Texto(r, "name"),
            Bio = Texto(r, "bio"),
            Local = Texto(r, "location"),
            Empresa = Texto(r, "company"),
            RepositoriosPublicos = Inteiro(r, "public_repos"),
            Seguidores = Inteiro(r, "followers"),
            Seguindo = Inteiro(r, "following"),
            AvatarUrl = Texto(r, "avatar_url"),
        };
    }

    private static readonly ConcurrentDictionary<string, byte[]> CacheFotos = new();

    /// <summary>
    /// Foto da conta, já no tamanho do cartão. Fica em memória: o painel é remontado a
    /// cada clique na árvore, e baixar a mesma imagem toda vez seria desperdício.
    /// </summary>
    public static async Task<byte[]?> FotoAsync(string avatarUrl, int tamanho = 96)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return null;

        var url = avatarUrl + (avatarUrl.Contains('?') ? "&" : "?") + "s=" + tamanho;
        if (CacheFotos.TryGetValue(url, out var guardada)) return guardada;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            CacheFotos[url] = bytes;
            return bytes;
        }
        catch (Exception)
        {
            return null; // sem foto o cartão mostra a inicial
        }
    }

    // -------------------------------------------------------- contribuições

    private static readonly ConcurrentDictionary<string, (DateTime Quando, Contribuicoes Dados)> CacheContribuicoes = new();

    /// <summary>
    /// O calendário de contribuições do último ano, o mesmo quadriculado do perfil no
    /// GitHub. Só existe na API GraphQL, que não aceita chamada anônima: sem token da
    /// conta, devolve nulo e o cartão fica sem o quadriculado.
    ///
    /// Fica guardado por uma hora — o painel é remontado a cada clique na árvore e o
    /// número muda pouco ao longo do dia.
    /// </summary>
    public static async Task<Contribuicoes?> ContribuicoesAsync(string login)
    {
        if (string.IsNullOrWhiteSpace(login)) return null;
        login = login.Trim();

        if (CacheContribuicoes.TryGetValue(login.ToLowerInvariant(), out var guardado) &&
            DateTime.UtcNow - guardado.Quando < TimeSpan.FromHours(1))
            return guardado.Dados;

        var token = await TokenAsync(login);
        if (string.IsNullOrEmpty(token)) return null;

        const string consulta =
            "query($login:String!){user(login:$login){contributionsCollection{contributionCalendar{" +
            "totalContributions weeks{contributionDays{date contributionCount contributionLevel}}}}}}";
        var corpo = JsonSerializer.Serialize(new { query = consulta, variables = new { login } });

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;

        var dados = LerContribuicoes(await resp.Content.ReadAsStringAsync());
        if (dados is not null) CacheContribuicoes[login.ToLowerInvariant()] = (DateTime.UtcNow, dados);
        return dados;
    }

    /// <summary>Separado da rede para poder ser testado com uma resposta de verdade.</summary>
    public static Contribuicoes? LerContribuicoes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("user", out var user) || user.ValueKind != JsonValueKind.Object ||
            !user.TryGetProperty("contributionsCollection", out var col) ||
            !col.TryGetProperty("contributionCalendar", out var cal))
            return null;

        var dias = new List<DiaContribuicao>();
        if (cal.TryGetProperty("weeks", out var semanas) && semanas.ValueKind == JsonValueKind.Array)
            foreach (var semana in semanas.EnumerateArray())
            {
                if (!semana.TryGetProperty("contributionDays", out var ds)) continue;
                foreach (var d in ds.EnumerateArray())
                {
                    if (!DateTime.TryParse(Texto(d, "date"), System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var data2))
                        continue;

                    dias.Add(new DiaContribuicao(data2.Date, Inteiro(d, "contributionCount"),
                        Texto(d, "contributionLevel") switch
                        {
                            "FIRST_QUARTILE" => 1,
                            "SECOND_QUARTILE" => 2,
                            "THIRD_QUARTILE" => 3,
                            "FOURTH_QUARTILE" => 4,
                            _ => 0,
                        }));
                }
            }

        return new Contribuicoes(Inteiro(cal, "totalContributions"), dias);
    }

    /// <summary>Estrelas somadas e linguagens por frequência, a partir da lista de repos.</summary>
    public static (int Estrelas, List<string> Linguagens) LerEstatisticasDeRepos(string json)
    {
        var estrelas = 0;
        var contagem = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return (0, new List<string>());

        foreach (var repo in doc.RootElement.EnumerateArray())
        {
            // fork infla a conta com estrelas que não são suas
            if (repo.TryGetProperty("fork", out var f) && f.ValueKind == JsonValueKind.True) continue;

            estrelas += Inteiro(repo, "stargazers_count");

            var lang = Texto(repo, "language");
            if (lang.Length > 0) contagem[lang] = contagem.GetValueOrDefault(lang) + 1;
        }

        var linguagens = contagem.OrderByDescending(p => p.Value)
                                 .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                                 .Select(p => p.Key)
                                 .ToList();

        return (estrelas, linguagens);
    }

    private static int Inteiro(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    // -------------------------------------------------------- pull requests

    /// <summary>Guardado como o status da esteira: o painel consulta vários repositórios.</summary>
    private static readonly ConcurrentDictionary<string, (DateTime Quando, List<PullRequest> Prs)> CachePrs = new();

    /// <summary>
    /// Pull requests mais recentemente mexidos, abertos ou não. Vem de "state=all" porque
    /// o cartão mostra a contagem de abertos **e** o último movimento, seja ele qual for.
    /// </summary>
    public static async Task<List<PullRequest>> PullRequestsAsync(string slug, string usuario = "", int limite = 10)
    {
        if (CachePrs.TryGetValue(slug, out var guardado) && DateTime.UtcNow - guardado.Quando < Validade)
            return guardado.Prs;

        var url = $"https://api.github.com/repos/{slug}/pulls" +
                  $"?state=all&sort=updated&direction=desc&per_page={limite}";

        var lista = LerPullRequests(await BaixarAsync(url, usuario));
        CachePrs[slug] = (DateTime.UtcNow, lista);
        return lista;
    }

    /// <summary>Separado da rede para poder ser testado com uma resposta de verdade.</summary>
    public static List<PullRequest> LerPullRequests(string json)
    {
        var lista = new List<PullRequest>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return lista;

        foreach (var p in doc.RootElement.EnumerateArray()) lista.Add(LerPr(p));
        return lista;
    }

    /// <summary>Um PR só, como vem da consulta e da criação.</summary>
    public static PullRequest? LerPullRequest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object ? LerPr(doc.RootElement) : null;
    }

    private static PullRequest LerPr(JsonElement p)
    {
        // "merged_at" preenchido é o que separa mesclado de simplesmente fechado
        var mesclado = p.TryGetProperty("merged_at", out var m) && m.ValueKind == JsonValueKind.String;
        var estado = Texto(p, "state") == "open" ? "aberto" : mesclado ? "mesclado" : "fechado";

        var temOrigem = p.TryGetProperty("head", out var origem) && origem.ValueKind == JsonValueKind.Object;
        var temDestino = p.TryGetProperty("base", out var destino) && destino.ValueKind == JsonValueKind.Object;

        return new PullRequest
        {
            Numero = Inteiro(p, "number"),
            Titulo = Texto(p, "title"),
            Autor = p.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object ? Texto(u, "login") : "",
            Url = Texto(p, "html_url"),
            Rascunho = p.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True,
            Estado = estado,
            Atualizado = Data(p, "updated_at"),
            Origem = temOrigem ? Texto(origem, "ref") : "",
            Destino = temDestino ? Texto(destino, "ref") : "",
            Sha = temOrigem ? Texto(origem, "sha") : "",
            Corpo = Texto(p, "body"),
            Commits = Inteiro(p, "commits"),
            Arquivos = Inteiro(p, "changed_files"),
            Adicionadas = Inteiro(p, "additions"),
            Removidas = Inteiro(p, "deletions"),
            Mesclagem = Texto(p, "mergeable_state"),
            Comentarios = Inteiro(p, "comments") + Inteiro(p, "review_comments"),
        };
    }

    // ------------------------------------------ pull requests: a janela do app

    /// <summary>
    /// A lista da janela de pull requests: sem o cache do painel, porque quem acabou de
    /// criar ou mesclar precisa ver o resultado na hora.
    /// </summary>
    public static async Task<List<PullRequest>> ListarPullRequestsAsync(
        string slug, string usuario = "", bool somenteAbertos = true, int limite = 30)
    {
        var url = $"https://api.github.com/repos/{slug}/pulls" +
                  $"?state={(somenteAbertos ? "open" : "all")}&sort=updated&direction=desc&per_page={limite}";
        return LerPullRequests(await BaixarAsync(url, usuario, "os pull requests"));
    }

    /// <summary>Um PR com o que a listagem não traz: tamanho e situação de mesclagem.</summary>
    public static async Task<PullRequest?> PullRequestAsync(string slug, int numero, string usuario = "") =>
        LerPullRequest(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/pulls/{numero}", usuario, "o pull request"));

    /// <summary>Verificações (Actions e afins) do commit da ponta do PR.</summary>
    public static async Task<List<Verificacao>> VerificacoesAsync(string slug, string sha, string usuario = "")
    {
        if (sha.Length == 0) return new List<Verificacao>();
        return LerVerificacoes(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/commits/{sha}/check-runs?per_page=100", usuario, "as verificações"));
    }

    public static List<Verificacao> LerVerificacoes(string json)
    {
        var lista = new List<Verificacao>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("check_runs", out var runs) ||
            runs.ValueKind != JsonValueKind.Array) return lista;

        foreach (var r in runs.EnumerateArray())
        {
            var conclusao = Texto(r, "conclusion");
            lista.Add(new Verificacao
            {
                Nome = Texto(r, "name"),
                // pulada e neutra não reprovam nada: contam como passou
                Situacao = conclusao is "skipped" or "neutral"
                    ? "sucesso"
                    : Traduzir(Texto(r, "status"), conclusao),
                Url = Texto(r, "html_url"),
                Iniciada = Data(r, "started_at"),
                Concluida = Data(r, "completed_at"),
                RunId = RunDoLink(Texto(r, "details_url") is { Length: > 0 } d ? d : Texto(r, "html_url")),
            });
        }
        return lista;
    }

    /// <summary>O número da execução dentro de um link do Actions; zero se não for um.</summary>
    public static long RunDoLink(string url)
    {
        const string marca = "/actions/runs/";
        var i = url.IndexOf(marca, StringComparison.Ordinal);
        if (i < 0) return 0;

        var resto = url[(i + marca.Length)..];
        var fim = 0;
        while (fim < resto.Length && char.IsDigit(resto[fim])) fim++;
        return fim > 0 && long.TryParse(resto[..fim], out var id) ? id : 0;
    }

    /// <summary>
    /// Resumo das verificações numa situação só: uma quebrada reprova, uma rodando
    /// segura, e só sem nenhuma das duas é sucesso. Sem verificação, "nenhum".
    /// </summary>
    public static string ResumoDasVerificacoes(IReadOnlyCollection<Verificacao> verificacoes)
    {
        if (verificacoes.Count == 0) return "nenhum";
        if (verificacoes.Any(v => v.Situacao == "falha")) return "falha";
        if (verificacoes.Any(v => v.Situacao == "rodando")) return "rodando";
        return verificacoes.Any(v => v.Situacao == "sucesso") ? "sucesso" : "cancelado";
    }

    /// <summary>Branch padrão do repositório no GitHub: o destino sugerido de um PR novo.</summary>
    public static async Task<string> BranchPadraoAsync(string slug, string usuario = "")
    {
        using var doc = JsonDocument.Parse(await BaixarAsync(
            $"https://api.github.com/repos/{slug}", usuario, "o repositório"));
        return Texto(doc.RootElement, "default_branch");
    }

    /// <summary>
    /// Escrita nos pull requests. O GitHub explica a recusa no corpo da resposta
    /// ("A pull request already exists…"), e é isso que interessa mostrar.
    /// </summary>
    private static async Task<string> EscreverPrAsync(HttpMethod metodo, string url, object corpo, string usuario, string oque)
    {
        var token = await TokenAsync(usuario);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                "Nenhum token encontrado. Configure em Preferências → Autenticação.");

        using var req = new HttpRequestMessage(metodo, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(corpo), System.Text.Encoding.UTF8, "application/json");

        using var resp = await Http.SendAsync(req);
        var texto = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode) return texto;

        throw new InvalidOperationException(
            $"Não foi possível {oque}: " + MotivoDaRecusa((int)resp.StatusCode, texto));
    }

    /// <summary>Traduz a recusa do GitHub; separado da rede para ser testado.</summary>
    public static string MotivoDaRecusa(int codigo, string json)
    {
        var mensagens = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Array)
                    foreach (var e in erros.EnumerateArray())
                    {
                        var m = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : Texto(e, "message");
                        if (m.Length == 0 && e.ValueKind == JsonValueKind.Object)
                            m = $"{Texto(e, "field")} {Texto(e, "code")}".Trim();
                        if (m.Length > 0) mensagens.Add(m);
                    }

                if (mensagens.Count == 0 && Texto(doc.RootElement, "message") is { Length: > 0 } geral)
                    mensagens.Add(geral);
            }
        }
        catch (JsonException)
        {
            // resposta sem JSON: vale só o código
        }

        var bruto = string.Join(" ", mensagens);
        bool Tem(string trecho) => bruto.Contains(trecho, StringComparison.OrdinalIgnoreCase);

        if (Tem("already exists"))
            return "já existe um pull request aberto desta branch para o mesmo destino.";
        if (Tem("No commits between"))
            return "não há commits de diferença entre a origem e o destino.";
        if (Tem("head invalid"))
            return "a branch de origem não existe no GitHub. Envie a branch (Enviar) e tente de novo.";
        if (Tem("base invalid"))
            return "a branch de destino não existe no GitHub.";
        if (Tem("your own pull request"))
            return "o GitHub não deixa aprovar nem pedir mudanças no próprio pull request. Comentar pode.";
        if (Tem("not mergeable"))
            return "o pull request não pode ser mesclado agora (conflito ou verificação pendente).";
        if (Tem("merge method") || Tem("merges are not allowed"))
            return "este repositório não permite essa forma de mesclar. Escolha outra.";

        return codigo switch
        {
            401 => "o token não foi aceito.",
            403 => "o token não tem permissão de escrita em pull requests e issues. Um PAT clássico precisa do " +
                   "escopo \"repo\"; um fine-grained, de \"Pull requests\" e \"Issues: Read and write\" neste repositório.",
            404 => "repositório ou pull request não encontrado — ou o token não enxerga este repositório.",
            405 or 409 => "o pull request não pode ser mesclado agora" + (bruto.Length > 0 ? $": {bruto}" : "."),
            _ => bruto.Length > 0 ? bruto : $"o GitHub respondeu {codigo}.",
        };
    }

    public static async Task<PullRequest?> CriarPullRequestAsync(
        string slug, string titulo, string corpo, string origem, string destino, bool rascunho, string usuario = "")
    {
        var json = await EscreverPrAsync(HttpMethod.Post, $"https://api.github.com/repos/{slug}/pulls",
            new { title = titulo.Trim(), body = corpo, head = origem, @base = destino, draft = rascunho },
            usuario, "criar o pull request");

        CachePrs.TryRemove(slug, out _);
        return LerPullRequest(json);
    }

    /// <param name="metodo">"merge", "squash" ou "rebase".</param>
    public static async Task MesclarPullRequestAsync(string slug, int numero, string metodo, string usuario = "")
    {
        await EscreverPrAsync(HttpMethod.Put, $"https://api.github.com/repos/{slug}/pulls/{numero}/merge",
            new { merge_method = metodo }, usuario, "mesclar");
        CachePrs.TryRemove(slug, out _);
    }

    /// <summary>Fecha sem mesclar. A branch continua lá.</summary>
    public static async Task FecharPullRequestAsync(string slug, int numero, string usuario = "")
    {
        await EscreverPrAsync(HttpMethod.Patch, $"https://api.github.com/repos/{slug}/pulls/{numero}",
            new { state = "closed" }, usuario, "fechar o pull request");
        CachePrs.TryRemove(slug, out _);
    }

    // ------------------------------- conversa, commits e arquivos de um PR

    private static string Login(JsonElement e, string campo = "user") =>
        e.TryGetProperty(campo, out var u) && u.ValueKind == JsonValueKind.Object ? Texto(u, "login") : "";

    private static long Longo(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    private static IEnumerable<JsonElement> Lista(JsonDocument doc) =>
        doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    /// <summary>Comentários da conversa (o mesmo formato vale para PR e para issue).</summary>
    public static List<Comentario> LerComentarios(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Lista(doc).Select(c => new Comentario
        {
            Id = Longo(c, "id"), Autor = Login(c), Corpo = Texto(c, "body"), Quando = Data(c, "created_at"),
        }).ToList();
    }

    /// <summary>
    /// Revisões do PR. A que só carrega comentários de código, sem texto próprio, fica de
    /// fora: os comentários dela já aparecem um a um, e a linha vazia seria só ruído.
    /// </summary>
    public static List<Comentario> LerRevisoes(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var lista = new List<Comentario>();
        foreach (var r in Lista(doc))
        {
            var estado = Texto(r, "state");
            var corpo = Texto(r, "body");
            if (estado is "PENDING" or "DISMISSED") continue;
            if (estado == "COMMENTED" && corpo.Trim().Length == 0) continue;

            lista.Add(new Comentario
            {
                Id = Longo(r, "id"), Origem = "revisao",
                Autor = Login(r),
                Corpo = corpo,
                Quando = Data(r, "submitted_at"),
                Tipo = estado switch
                {
                    "APPROVED" => "aprovou",
                    "CHANGES_REQUESTED" => "pediu mudanças",
                    _ => "revisou",
                },
            });
        }
        return lista;
    }

    /// <summary>Comentários feitos numa linha do código, com o arquivo e a linha.</summary>
    public static List<Comentario> LerComentariosDeCodigo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Lista(doc).Select(c =>
        {
            var linha = Inteiro(c, "line") is > 0 and var l ? l : Inteiro(c, "original_line");
            return new Comentario
            {
                Id = Longo(c, "id"), Origem = "codigo",
                Autor = Login(c), Corpo = Texto(c, "body"), Quando = Data(c, "created_at"),
                Tipo = "comentou no código",
                Onde = Texto(c, "path") + (linha > 0 ? $":{linha}" : ""),
            };
        }).ToList();
    }

    /// <summary>A conversa inteira do PR em ordem de tempo: comentários, revisões e os do código.</summary>
    public static async Task<List<Comentario>> ConversaDoPrAsync(string slug, int numero, string usuario = "")
    {
        var raiz = $"https://api.github.com/repos/{slug}";
        var comentarios = BaixarAsync($"{raiz}/issues/{numero}/comments?per_page=100", usuario, "a conversa");
        var revisoes = BaixarAsync($"{raiz}/pulls/{numero}/reviews?per_page=100", usuario, "as revisões");
        var codigo = BaixarAsync($"{raiz}/pulls/{numero}/comments?per_page=100", usuario, "os comentários do código");
        await Task.WhenAll(comentarios, revisoes, codigo);

        return LerComentarios(comentarios.Result)
            .Concat(LerRevisoes(revisoes.Result))
            .Concat(LerComentariosDeCodigo(codigo.Result))
            .OrderBy(c => c.Quando ?? DateTime.MaxValue)
            .ToList();
    }

    public static async Task<List<Comentario>> ComentariosDaIssueAsync(string slug, int numero, string usuario = "") =>
        LerComentarios(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/issues/{numero}/comments?per_page=100", usuario, "os comentários"));

    public static List<CommitDoPr> LerCommitsDoPr(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var lista = new List<CommitDoPr>();
        foreach (var c in Lista(doc))
        {
            var temCommit = c.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.Object;
            var temAutor = temCommit && commit.TryGetProperty("author", out var autor) && autor.ValueKind == JsonValueKind.Object;
            var git = temAutor ? commit.GetProperty("author") : default;

            // o login do GitHub quando a conta é conhecida; senão o nome gravado no commit
            var login = Login(c, "author");
            lista.Add(new CommitDoPr
            {
                Sha = Texto(c, "sha"),
                Assunto = temCommit ? Texto(commit, "message").Split('\n')[0].Trim() : "",
                Autor = login.Length > 0 ? login : temAutor ? Texto(git, "name") : "",
                Quando = temAutor ? Data(git, "date") : null,
            });
        }
        return lista;
    }

    public static async Task<List<CommitDoPr>> CommitsDoPrAsync(string slug, int numero, string usuario = "") =>
        LerCommitsDoPr(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/pulls/{numero}/commits?per_page=100", usuario, "os commits"));

    public static List<ArquivoDoPr> LerArquivosDoPr(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Lista(doc).Select(f => new ArquivoDoPr
        {
            Caminho = Texto(f, "filename"),
            Status = Texto(f, "status") switch
            {
                "added" => "A",
                "removed" => "D",
                "renamed" => "R",
                _ => "M",
            },
            Adicionadas = Inteiro(f, "additions"),
            Removidas = Inteiro(f, "deletions"),
            Patch = Texto(f, "patch"),
        }).ToList();
    }

    public static async Task<List<ArquivoDoPr>> ArquivosDoPrAsync(string slug, int numero, string usuario = "") =>
        LerArquivosDoPr(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/pulls/{numero}/files?per_page=100", usuario, "os arquivos"));

    /// <summary>
    /// O trecho que a API manda não tem o cabeçalho do arquivo; com ele o painel de
    /// diferenças lê como qualquer outro diff do git.
    /// </summary>
    public static string DiffDoArquivo(ArquivoDoPr arquivo) =>
        arquivo.Patch.Length == 0
            ? ""
            : $"diff --git a/{arquivo.Caminho} b/{arquivo.Caminho}\n--- a/{arquivo.Caminho}\n+++ b/{arquivo.Caminho}\n{arquivo.Patch}\n";

    // ------------------------------------------- escrever: comentários e issues

    /// <summary>Comenta numa issue ou num pull request (para o GitHub é o mesmo endereço).</summary>
    public static async Task<Comentario?> ComentarAsync(string slug, int numero, string corpo, string usuario = "")
    {
        var json = await EscreverPrAsync(HttpMethod.Post,
            $"https://api.github.com/repos/{slug}/issues/{numero}/comments",
            new { body = corpo }, usuario, "comentar");
        CachePrs.TryRemove(slug, out _);
        return LerComentarios("[" + json + "]").FirstOrDefault();
    }

    /// <summary>Edita um comentário; o endereço depende de onde ele foi feito.</summary>
    public static Task EditarComentarioAsync(string slug, Comentario comentario, string corpo, string usuario = "") =>
        EscreverPrAsync(HttpMethod.Patch, EnderecoDoComentario(slug, comentario),
            new { body = corpo }, usuario, "editar o comentário");

    public static Task ExcluirComentarioAsync(string slug, Comentario comentario, string usuario = "") =>
        EscreverPrAsync(HttpMethod.Delete, EnderecoDoComentario(slug, comentario),
            new { }, usuario, "excluir o comentário");

    public static string EnderecoDoComentario(string slug, Comentario comentario) => comentario.Origem switch
    {
        "codigo" => $"https://api.github.com/repos/{slug}/pulls/comments/{comentario.Id}",
        _ => $"https://api.github.com/repos/{slug}/issues/comments/{comentario.Id}",
    };

    /// <param name="evento">"APPROVE", "REQUEST_CHANGES" ou "COMMENT".</param>
    public static Task RevisarPullRequestAsync(string slug, int numero, string evento, string corpo, string usuario = "") =>
        EscreverPrAsync(HttpMethod.Post, $"https://api.github.com/repos/{slug}/pulls/{numero}/reviews",
            new { @event = evento, body = corpo }, usuario,
            evento == "APPROVE" ? "aprovar" : evento == "REQUEST_CHANGES" ? "pedir mudanças" : "revisar");

    public static async Task EditarPullRequestAsync(string slug, int numero, string titulo, string corpo, string usuario = "")
    {
        await EscreverPrAsync(HttpMethod.Patch, $"https://api.github.com/repos/{slug}/pulls/{numero}",
            new { title = titulo.Trim(), body = corpo }, usuario, "editar o pull request");
        CachePrs.TryRemove(slug, out _);
    }

    public static async Task<Issue?> CriarIssueAsync(
        string slug, string titulo, string corpo, IEnumerable<string> etiquetas, string usuario = "")
    {
        var json = await EscreverPrAsync(HttpMethod.Post, $"https://api.github.com/repos/{slug}/issues",
            new { title = titulo.Trim(), body = corpo, labels = etiquetas.ToArray() }, usuario, "criar a issue");
        return LerIssues("[" + json + "]").FirstOrDefault();
    }

    public static async Task<Issue?> EditarIssueAsync(
        string slug, int numero, string titulo, string corpo, IEnumerable<string> etiquetas, string usuario = "")
    {
        var json = await EscreverPrAsync(HttpMethod.Patch, $"https://api.github.com/repos/{slug}/issues/{numero}",
            new { title = titulo.Trim(), body = corpo, labels = etiquetas.ToArray() }, usuario, "editar a issue");
        return LerIssues("[" + json + "]").FirstOrDefault();
    }

    /// <summary>Fecha (como concluída) ou reabre a issue.</summary>
    public static async Task<Issue?> MudarEstadoDaIssueAsync(string slug, int numero, bool abrir, string usuario = "")
    {
        object corpo = abrir
            ? new { state = "open" }
            : new { state = "closed", state_reason = "completed" };
        var json = await EscreverPrAsync(HttpMethod.Patch, $"https://api.github.com/repos/{slug}/issues/{numero}",
            corpo, usuario, abrir ? "reabrir a issue" : "fechar a issue");
        return LerIssues("[" + json + "]").FirstOrDefault();
    }

    /// <summary>Todas as etiquetas que o repositório tem, para escolher ao criar ou editar uma issue.</summary>
    public static async Task<List<Etiqueta>> EtiquetasDoRepoAsync(string slug, string usuario = "")
    {
        using var doc = JsonDocument.Parse(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/labels?per_page=100", usuario, "as etiquetas"));
        return Lista(doc)
            .Where(l => Texto(l, "name").Length > 0)
            .Select(l => new Etiqueta(Texto(l, "name"), Texto(l, "color") is { Length: > 0 } cor ? "#" + cor : ""))
            .ToList();
    }

    // --------------------------------------------------------------- issues

    /// <summary>
    /// Issues do repositório. A API mistura os pull requests na mesma lista (todo PR é
    /// uma issue para o GitHub); eles saem daqui, porque têm a janela deles.
    /// </summary>
    public static List<Issue> LerIssues(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var lista = new List<Issue>();
        foreach (var i in Lista(doc))
        {
            if (i.TryGetProperty("pull_request", out _)) continue;

            var etiquetas = new List<Etiqueta>();
            if (i.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
                foreach (var l in labels.EnumerateArray())
                    if (l.ValueKind == JsonValueKind.Object && Texto(l, "name") is { Length: > 0 } nome)
                        etiquetas.Add(new Etiqueta(nome, Texto(l, "color") is { Length: > 0 } cor ? "#" + cor : ""));

            var responsaveis = new List<string>();
            if (i.TryGetProperty("assignees", out var ass) && ass.ValueKind == JsonValueKind.Array)
                foreach (var a in ass.EnumerateArray())
                    if (a.ValueKind == JsonValueKind.Object && Texto(a, "login") is { Length: > 0 } login)
                        responsaveis.Add(login);

            lista.Add(new Issue
            {
                Numero = Inteiro(i, "number"),
                Titulo = Texto(i, "title"),
                Autor = Login(i),
                Corpo = Texto(i, "body"),
                Url = Texto(i, "html_url"),
                Aberta = Texto(i, "state") == "open",
                Comentarios = Inteiro(i, "comments"),
                Criada = Data(i, "created_at"),
                Atualizada = Data(i, "updated_at"),
                Etiquetas = etiquetas,
                Responsaveis = responsaveis,
            });
        }
        return lista;
    }

    /// <summary>
    /// As issues abertas ou as fechadas, da mexida mais recente para a mais antiga. Vão
    /// até três páginas: como os PRs vêm na mesma lista e são descartados, uma página só
    /// podia trazer bem menos issues do que o repositório tem.
    /// </summary>
    public static async Task<List<Issue>> IssuesAsync(string slug, bool abertas, string usuario = "")
    {
        var lista = new List<Issue>();
        for (var pagina = 1; pagina <= 3; pagina++)
        {
            var json = await BaixarAsync(
                $"https://api.github.com/repos/{slug}/issues?state={(abertas ? "open" : "closed")}" +
                $"&sort=updated&direction=desc&per_page=100&page={pagina}", usuario, "as issues");

            lista.AddRange(LerIssues(json));

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 100) break;
        }
        return lista;
    }

    /// <summary>Releases mais recentes, da mais nova para a mais antiga.</summary>
    public static async Task<List<Release>> ReleasesAsync(string slug, string usuario = "", int limite = 20)
    {
        var json = await BaixarAsync(
            $"https://api.github.com/repos/{slug}/releases?per_page={limite}", usuario);

        var lista = new List<Release>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return lista;

        foreach (var r in doc.RootElement.EnumerateArray())
        {
            var release = LerRelease(r.GetRawText());
            if (release is not null) lista.Add(release);
        }
        return lista;
    }

    /// <summary>Separado da rede para poder ser testado com uma resposta de verdade.</summary>
    public static Release? LerRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("tag_name", out _)) return null;

        var arquivos = new List<ReleaseAsset>();
        if (raiz.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
                arquivos.Add(new ReleaseAsset
                {
                    Nome = Texto(a, "name"),
                    Tamanho = a.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number
                        ? s.GetInt64() : 0,
                    Url = Texto(a, "browser_download_url"),
                });

        return new Release
        {
            Tag = Texto(raiz, "tag_name"),
            Nome = Texto(raiz, "name"),
            Url = Texto(raiz, "html_url"),
            Notas = Texto(raiz, "body"),
            Publicada = Data(raiz, "published_at") ?? Data(raiz, "created_at"),
            Arquivos = arquivos,
        };
    }

    private static DateTime? Data(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var d)
            ? d
            : null;

    /// <summary>Duração entre started_at e completed_at; nula enquanto o passo roda.</summary>
    private static TimeSpan? Intervalo(JsonElement e)
    {
        var inicio = Data(e, "started_at");
        var fim = Data(e, "completed_at");
        return inicio is null || fim is null || fim < inicio ? null : fim - inicio;
    }

    private static async Task<CiRun> ConsultarAsync(string slug, string branch, string usuario)
    {
        try
        {
            // sem filtro de branch, e escolhendo depois: uma chamada só serve para achar
            // tanto a execução da branch quanto um build de tag em andamento
            var url = $"https://api.github.com/repos/{slug}/actions/runs?per_page=10";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var token = await TokenAsync(usuario);
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return new CiRun { Situacao = "indisponivel", Detalhe = $"GitHub respondeu {(int)resp.StatusCode}" };

            var escolhida = EscolherExecucao(LerExecucoes(await resp.Content.ReadAsStringAsync()), branch);
            if (escolhida is null)
                return new CiRun { Situacao = "nenhum", Detalhe = "Sem execuções neste repositório" };

            // a branch entra no detalhe: é o que explica um "rodando" que não é da sua
            return new CiRun
            {
                Situacao = escolhida.Situacao,
                Workflow = escolhida.Workflow,
                Url = escolhida.Url,
                Detalhe = escolhida.Branch.Length > 0 && escolhida.Branch != branch
                    ? $"{escolhida.Titulo} ({escolhida.Branch})"
                    : escolhida.Titulo,
            };
        }
        catch (Exception e)
        {
            return new CiRun { Situacao = "indisponivel", Detalhe = e.Message };
        }
    }

    private static string Texto(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Traduzir(string status, string conclusao) => status switch
    {
        "queued" or "in_progress" or "waiting" or "pending" or "requested" => "rodando",
        _ => conclusao switch
        {
            "success" => "sucesso",
            "failure" or "timed_out" or "startup_failure" => "falha",
            "cancelled" => "cancelado",
            // passo com "if:" que não se aplicou (o artefato num build de tag): não rodou
            // de propósito, o que é diferente de não ter informação
            "skipped" => "pulado",
            "" => "rodando",
            _ => "indisponivel",
        },
    };
}
