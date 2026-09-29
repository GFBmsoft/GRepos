using System;
using System.Collections.Concurrent;
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

    public static bool TemTokenGuardado(string usuario) =>
        Tokens.TryGetValue(usuario, out var t) && !string.IsNullOrEmpty(t);

    /// <summary>Token salvo para o usuário (usado ao expandir {{token}} na URL do remoto).</summary>
    public static Task<string?> TokenDoUsuarioAsync(string usuario) => TokenAsync(usuario);

    /// <summary>Confere o token contra a API e devolve o login e o nome da conta.</summary>
    public static async Task<string> TestarAsync(string usuario)
    {
        var token = await TokenAsync(usuario);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                "Nenhum token encontrado para este usuário. Informe o token e salve antes de testar.");

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
