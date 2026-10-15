using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Uma issue ou PR que espera alguma coisa de quem está usando o app.</summary>
public sealed class Pendencia
{
    public int Numero { get; init; }
    public string Titulo { get; init; } = "";
    public string Repo { get; init; } = "";
    public string Url { get; init; } = "";
    public string Autor { get; init; } = "";
    public bool EhPr { get; init; }
    public bool Rascunho { get; init; }
    public DateTime? Atualizada { get; init; }
}

/// <summary>
/// O "gh status": PRs pedindo sua revisão, o que está atribuído a você e os seus PRs
/// abertos, de todos os repositórios. Sai da busca do GitHub (com @me, a conta do token),
/// não das notificações — essas não funcionam com token fine-grained.
/// </summary>
public static partial class GitHubService
{
    /// <summary>
    /// A busca tem cota própria, de 30 por minuto, e o painel é remontado a cada vez que
    /// aparece: sem guardar, alternar entre painel e repositório esgotaria a cota.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,
        (DateTime Quando, (List<Pendencia>, List<Pendencia>, List<Pendencia>) Listas)> CachePendencias = new();

    private static readonly TimeSpan ValidadePendencias = TimeSpan.FromMinutes(2);

    public static async Task<(List<Pendencia> Revisar, List<Pendencia> Atribuidas, List<Pendencia> MeusPrs)>
        PendenciasAsync(string usuario, bool forcar = false)
    {
        var chave = usuario.ToLowerInvariant();
        if (!forcar && CachePendencias.TryGetValue(chave, out var guardado) &&
            DateTime.UtcNow - guardado.Quando < ValidadePendencias)
            return guardado.Listas;

        var revisar = BuscarAsync("is:open is:pr archived:false review-requested:@me", usuario);
        var atribuidas = BuscarAsync("is:open archived:false assignee:@me", usuario);
        var meus = BuscarAsync("is:open is:pr archived:false author:@me", usuario);
        await Task.WhenAll(revisar, atribuidas, meus);
        var listas = (revisar.Result, atribuidas.Result, meus.Result);
        CachePendencias[chave] = (DateTime.UtcNow, listas);
        return listas;
    }

    private static async Task<List<Pendencia>> BuscarAsync(string consulta, string usuario) =>
        LerPendencias(await BaixarAsync(
            $"https://api.github.com/search/issues?q={Uri.EscapeDataString(consulta)}&sort=updated&order=desc&per_page=30",
            usuario, "o que espera por você"));

    public static List<Pendencia> LerPendencias(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var itens) || itens.ValueKind != JsonValueKind.Array)
            return new List<Pendencia>();

        const string prefixo = "https://api.github.com/repos/";
        return itens.EnumerateArray().Select(i =>
        {
            var repo = Texto(i, "repository_url");
            return new Pendencia
            {
                Numero = Inteiro(i, "number"),
                Titulo = Texto(i, "title"),
                Repo = repo.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase) ? repo[prefixo.Length..] : repo,
                Url = Texto(i, "html_url"),
                Autor = Login(i),
                EhPr = i.TryGetProperty("pull_request", out _),
                Rascunho = i.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True,
                Atualizada = Data(i, "updated_at"),
            };
        }).ToList();
    }
}
