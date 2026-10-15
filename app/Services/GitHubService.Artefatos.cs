using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Arquivo gerado por uma execução (upload-artifact): o .exe do build, por exemplo.</summary>
public sealed class Artefato
{
    public long Id { get; init; }
    public string Nome { get; init; } = "";
    public long Tamanho { get; init; }

    /// <summary>O GitHub apaga o artefato depois do prazo de retenção; aí não há o que baixar.</summary>
    public bool Expirado { get; init; }
    public DateTime? ExpiraEm { get; init; }
}

/// <summary>Log dos passos e artefatos de uma execução — o que o "gh run view/download" faz.</summary>
public static partial class GitHubService
{
    /// <summary>
    /// Log de um job inteiro, em texto. A API responde com um redirecionamento para um
    /// endereço assinado, que o HttpClient segue sozinho (sem levar o token junto).
    /// </summary>
    public static Task<string> LogDoJobAsync(string slug, long jobId, string usuario = "") =>
        BaixarAsync($"https://api.github.com/repos/{slug}/actions/jobs/{jobId}/logs", usuario, "o log");

    public static async Task<List<Artefato>> ArtefatosAsync(string slug, long runId, string usuario = "") =>
        LerArtefatos(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/actions/runs/{runId}/artifacts?per_page=100", usuario, "os artefatos"));

    public static List<Artefato> LerArtefatos(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("artifacts", out var lista) || lista.ValueKind != JsonValueKind.Array)
            return new List<Artefato>();

        return lista.EnumerateArray().Select(a => new Artefato
        {
            Id = Longo(a, "id"),
            Nome = Texto(a, "name"),
            Tamanho = Longo(a, "size_in_bytes"),
            Expirado = a.TryGetProperty("expired", out var e) && e.ValueKind == JsonValueKind.True,
            ExpiraEm = Data(a, "expires_at"),
        }).ToList();
    }

    /// <summary>
    /// Baixa o artefato e extrai em <paramref name="pasta"/>/&lt;nome&gt;, como o
    /// "gh run download". Devolve a pasta onde os arquivos ficaram. O download exige token
    /// mesmo em repositório público.
    /// </summary>
    public static async Task<string> BaixarArtefatoAsync(string slug, Artefato artefato, string pasta, string usuario = "")
    {
        if (artefato.Expirado)
            throw new InvalidOperationException($"O artefato \"{artefato.Nome}\" expirou e o GitHub já o apagou.");

        var token = await TokenAsync(usuario);
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                "Baixar artefato exige token, mesmo em repositório público. Configure em Preferências → Autenticação.");

        var zip = Path.Combine(Path.GetTempPath(), $"grepos-artefato-{artefato.Id}.zip");
        try
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get,
                       $"https://api.github.com/repos/{slug}/actions/artifacts/{artefato.Id}/zip"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await HttpLongo.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException((int)resp.StatusCode switch
                    {
                        410 => $"O artefato \"{artefato.Nome}\" expirou e o GitHub já o apagou.",
                        403 => "o token não tem permissão de leitura em Actions neste repositório.",
                        _ => $"GitHub respondeu {(int)resp.StatusCode} ao baixar o artefato.",
                    });

                await using var arquivo = File.Create(zip);
                await resp.Content.CopyToAsync(arquivo);
            }

            var destino = Path.Combine(pasta, NomeDePasta(artefato.Nome));
            Directory.CreateDirectory(destino);
            ZipFile.ExtractToDirectory(zip, destino, overwriteFiles: true);
            return destino;
        }
        finally
        {
            try { File.Delete(zip); } catch (IOException) { }
        }
    }

    /// <summary>Nome de artefato pode ter caractere que o Windows não aceita em pasta.</summary>
    public static string NomeDePasta(string nome)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpo = new string(nome.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return limpo.Length > 0 ? limpo : "artefato";
    }
}
