using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Criar, editar e anexar arquivos a releases — o que o "gh release" faz.</summary>
public static partial class GitHubService
{
    private const string PermissaoDeConteudo =
        "conteúdo do repositório. Um PAT clássico precisa do escopo \"repo\"; um fine-grained, " +
        "de \"Contents: Read and write\"";

    /// <summary>
    /// Para arquivos grandes (artefato, anexo de release): o prazo por tentativa do
    /// cliente normal derrubaria um .exe de 90 MB no meio.
    /// </summary>
    private static readonly HttpClient HttpLongo = CriarClienteLongo();

    private static HttpClient CriarClienteLongo()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GRepos", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>
    /// Releases com os rascunhos: a consulta autenticada de quem pode escrever traz os
    /// dois. É a lista da janela de releases de um repositório.
    /// </summary>
    public static Task<System.Collections.Generic.List<Release>> ReleasesDoRepoAsync(string slug, string usuario) =>
        ReleasesAsync(slug, usuario, 30);

    /// <param name="alvo">Branch ou commit de onde a tag nasce, quando ela ainda não existe.</param>
    public static async Task<Release?> CriarReleaseAsync(string slug, string tag, string alvo, string titulo,
        string notas, bool rascunho, bool preRelease, string usuario = "")
    {
        if (string.IsNullOrWhiteSpace(tag)) throw new InvalidOperationException("Informe a tag da release.");

        object corpo = string.IsNullOrWhiteSpace(alvo)
            ? new { tag_name = tag.Trim(), name = titulo.Trim(), body = notas, draft = rascunho, prerelease = preRelease }
            : new { tag_name = tag.Trim(), target_commitish = alvo.Trim(), name = titulo.Trim(), body = notas,
                    draft = rascunho, prerelease = preRelease };

        var json = await EscreverPrAsync(HttpMethod.Post, $"https://api.github.com/repos/{slug}/releases",
            corpo, usuario, "criar a release", PermissaoDeConteudo);
        return LerRelease(json);
    }

    public static async Task<Release?> EditarReleaseAsync(string slug, long id, string tag, string titulo,
        string notas, bool rascunho, bool preRelease, string usuario = "")
    {
        var json = await EscreverPrAsync(HttpMethod.Patch, $"https://api.github.com/repos/{slug}/releases/{id}",
            new { tag_name = tag.Trim(), name = titulo.Trim(), body = notas, draft = rascunho, prerelease = preRelease },
            usuario, "salvar a release", PermissaoDeConteudo);
        return LerRelease(json);
    }

    /// <summary>Apaga a release. A tag continua no repositório.</summary>
    public static Task ExcluirReleaseAsync(string slug, long id, string usuario = "") =>
        EscreverPrAsync(HttpMethod.Delete, $"https://api.github.com/repos/{slug}/releases/{id}",
            new { }, usuario, "excluir a release", PermissaoDeConteudo);

    public static Task ExcluirAnexoAsync(string slug, long anexoId, string usuario = "") =>
        EscreverPrAsync(HttpMethod.Delete, $"https://api.github.com/repos/{slug}/releases/assets/{anexoId}",
            new { }, usuario, "excluir o arquivo", PermissaoDeConteudo);

    /// <summary>
    /// As notas que o GitHub monta sozinho: PRs mesclados e quem contribuiu desde a tag
    /// anterior. Devolve o texto; quem chama decide se substitui o que estava escrito.
    /// </summary>
    public static async Task<string> GerarNotasAsync(string slug, string tag, string alvo, string usuario = "")
    {
        object corpo = string.IsNullOrWhiteSpace(alvo)
            ? new { tag_name = tag.Trim() }
            : new { tag_name = tag.Trim(), target_commitish = alvo.Trim() };

        var json = await EscreverPrAsync(HttpMethod.Post,
            $"https://api.github.com/repos/{slug}/releases/generate-notes",
            corpo, usuario, "gerar as notas", PermissaoDeConteudo);

        using var doc = JsonDocument.Parse(json);
        return Texto(doc.RootElement, "body");
    }

    /// <summary>Anexa um arquivo à release. O envio vai para outro endereço da API (uploads).</summary>
    public static async Task<ReleaseAsset> AnexarAsync(string slug, long releaseId, string caminho, string usuario = "")
    {
        var token = await TokenAsync(usuario);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Nenhum token encontrado. Configure em Preferências → Autenticação.");

        var nome = Path.GetFileName(caminho);
        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://uploads.github.com/repos/{slug}/releases/{releaseId}/assets?name={Uri.EscapeDataString(nome)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await using var arquivo = File.OpenRead(caminho);
        req.Content = new StreamContent(arquivo);
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var resp = await HttpLongo.SendAsync(req);
        var texto = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            var motivo = MotivoDaRecusa((int)resp.StatusCode, texto, PermissaoDeConteudo);
            if (texto.Contains("already_exists", StringComparison.Ordinal))
                motivo = "já existe um arquivo com esse nome nesta release. Exclua o antigo antes.";
            throw new InvalidOperationException($"Não foi possível enviar {nome}: {motivo}");
        }

        using var doc = JsonDocument.Parse(texto);
        return new ReleaseAsset
        {
            Id = Longo(doc.RootElement, "id"),
            Nome = Texto(doc.RootElement, "name"),
            Tamanho = Longo(doc.RootElement, "size"),
            Url = Texto(doc.RootElement, "browser_download_url"),
        };
    }
}
