using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Um marco (milestone) do repositório.</summary>
public sealed record Marco(int Numero, string Titulo);

/// <summary>
/// Quem responde por uma issue ou PR, quem deve revisar e o marco — o que o
/// "gh pr edit --add-reviewer/--add-assignee/--milestone" faz.
/// </summary>
public static partial class GitHubService
{
    /// <summary>Quem pode ser responsável muda pouco: uma consulta por repositório na sessão.</summary>
    private static readonly ConcurrentDictionary<string, List<string>> CachePessoas = new();

    private static List<string> Logins(JsonElement e, string campo)
    {
        var lista = new List<string>();
        if (e.TryGetProperty(campo, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var u in arr.EnumerateArray())
                if (u.ValueKind == JsonValueKind.Object && Texto(u, "login") is { Length: > 0 } login)
                    lista.Add(login);
        return lista;
    }

    private static Marco MarcoDe(JsonElement e) =>
        e.TryGetProperty("milestone", out var m) && m.ValueKind == JsonValueKind.Object
            ? new Marco(Inteiro(m, "number"), Texto(m, "title"))
            : new Marco(0, "");

    /// <summary>
    /// Quem pode ser responsável (e revisor): as contas com acesso de escrita, na lista
    /// que o GitHub expõe para atribuição.
    /// </summary>
    public static async Task<List<string>> PessoasDoRepoAsync(string slug, string usuario = "")
    {
        if (CachePessoas.TryGetValue(slug, out var guardado)) return guardado;

        using var doc = JsonDocument.Parse(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/assignees?per_page=100", usuario, "as pessoas do repositório"));
        var lista = Lista(doc)
            .Select(u => Texto(u, "login"))
            .Where(l => l.Length > 0)
            .OrderBy(l => l, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        CachePessoas[slug] = lista;
        return lista;
    }

    public static async Task<List<Marco>> MarcosAsync(string slug, string usuario = "")
    {
        using var doc = JsonDocument.Parse(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/milestones?state=open&per_page=100", usuario, "os marcos"));
        return Lista(doc).Select(m => new Marco(Inteiro(m, "number"), Texto(m, "title")))
            .Where(m => m.Numero > 0)
            .ToList();
    }

    /// <summary>
    /// Responsáveis e marco numa chamada só. Vale para issue e para PR: para o GitHub
    /// todo PR é também uma issue, e é por esse endereço que os dois se editam.
    /// </summary>
    public static Task DefinirResponsaveisEMarcoAsync(string slug, int numero, IEnumerable<string> responsaveis,
        int marco, string usuario = "")
    {
        object corpo = marco > 0
            ? new { assignees = responsaveis.ToArray(), milestone = (int?)marco }
            : new { assignees = responsaveis.ToArray(), milestone = (int?)null };
        return EscreverPrAsync(HttpMethod.Patch, $"https://api.github.com/repos/{slug}/issues/{numero}",
            corpo, usuario, "salvar responsáveis e marco");
    }

    /// <summary>Pede revisão a quem entrou e tira o pedido de quem saiu.</summary>
    public static async Task AjustarRevisoresAsync(string slug, int numero, IReadOnlyCollection<string> pedir,
        IReadOnlyCollection<string> tirar, string usuario = "")
    {
        var url = $"https://api.github.com/repos/{slug}/pulls/{numero}/requested_reviewers";
        if (pedir.Count > 0)
            await EscreverPrAsync(HttpMethod.Post, url, new { reviewers = pedir.ToArray() }, usuario, "pedir revisão");
        if (tirar.Count > 0)
            await EscreverPrAsync(HttpMethod.Delete, url, new { reviewers = tirar.ToArray() }, usuario, "tirar o pedido de revisão");
        CachePrs.TryRemove(slug, out _);
    }
}
