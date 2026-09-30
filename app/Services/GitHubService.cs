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
    public string Url { get; init; } = "";
    public List<ReleaseAsset> Arquivos { get; init; } = new();

    /// <summary>
    /// O executável que não precisa de nada instalado. É o único que serve para trocar
    /// sozinho: o outro depende do .NET 8 estar na máquina de destino.
    /// </summary>
    public ReleaseAsset? Standalone => Arquivos.FirstOrDefault(a =>
        a.Nome.EndsWith("-standalone.exe", StringComparison.OrdinalIgnoreCase));
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
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
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
    /// Usuário embutido na URL do remoto (https://GFBmsoft@github.com/...). O credential
    /// manager guarda a credencial por conta, e sem o usuário ele não acha nada.
    /// </summary>
    public static string Usuario(string remoteUrl)
    {
        var i = remoteUrl.IndexOf("://", StringComparison.Ordinal);
        if (i < 0) return "";
        var resto = remoteUrl[(i + 3)..];
        var arroba = resto.IndexOf('@');
        var barra = resto.IndexOf('/');
        if (arroba <= 0 || (barra >= 0 && barra < arroba)) return "";

        var usuario = resto[..arroba];
        var doisPontos = usuario.IndexOf(':'); // usuário:senha
        return doisPontos > 0 ? usuario[..doisPontos] : usuario;
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

    private static async Task<string> BaixarAsync(string url, string usuario)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var token = await TokenAsync(usuario);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GitHub respondeu {(int)resp.StatusCode} ao consultar a esteira." +
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
            Url = Texto(raiz, "html_url"),
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
            var url = $"https://api.github.com/repos/{slug}/actions/runs?per_page=1" +
                      (string.IsNullOrEmpty(branch) ? "" : $"&branch={Uri.EscapeDataString(branch)}");

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var token = await TokenAsync(usuario);
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return new CiRun { Situacao = "indisponivel", Detalhe = $"GitHub respondeu {(int)resp.StatusCode}" };

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var runs = doc.RootElement.GetProperty("workflow_runs");
            if (runs.GetArrayLength() == 0)
                return new CiRun { Situacao = "nenhum", Detalhe = "Sem execuções para esta branch" };

            var r = runs[0];
            var status = Texto(r, "status");        // queued, in_progress, completed
            var conclusao = Texto(r, "conclusion"); // success, failure, cancelled, ...

            return new CiRun
            {
                Situacao = Traduzir(status, conclusao),
                Workflow = Texto(r, "name"),
                Url = Texto(r, "html_url"),
                Detalhe = Texto(r, "display_title"),
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
            "" => "rodando",
            _ => "indisponivel",
        },
    };
}
