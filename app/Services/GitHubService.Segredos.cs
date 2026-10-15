using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Segredo ou variável do GitHub Actions. O valor do segredo nunca volta da API.</summary>
public sealed class ItemDeActions
{
    public string Nome { get; init; } = "";

    /// <summary>Só variável tem valor legível; segredo vem vazio.</summary>
    public string Valor { get; init; } = "";
    public DateTime? Atualizado { get; init; }
}

/// <summary>Segredos e variáveis do Actions — o que o "gh secret/variable set" faz.</summary>
public static partial class GitHubService
{
    private const string PermissaoDeSegredos =
        "segredos e variáveis do Actions. Um PAT clássico precisa do escopo \"repo\"; um fine-grained, " +
        "de \"Secrets\" e \"Variables: Read and write\"";

    private static readonly Regex NomeValido = new("^[A-Z_][A-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// O que há de errado com o nome, ou vazio se ele serve. As regras são as do GitHub;
    /// letra minúscula não é erro — ele guarda tudo em maiúsculas, e o app também.
    /// </summary>
    public static string ProblemaNoNome(string nome)
    {
        var n = nome.Trim().ToUpperInvariant();
        if (n.Length == 0) return "Informe o nome.";
        if (n.StartsWith("GITHUB_", StringComparison.Ordinal)) return "O nome não pode começar com GITHUB_.";
        if (char.IsAsciiDigit(n[0])) return "O nome não pode começar com número.";
        if (!NomeValido.IsMatch(n)) return "Use só letras, números e sublinhado (_), sem espaço nem acento.";
        return "";
    }

    public static async Task<List<ItemDeActions>> SegredosAsync(string slug, string usuario) =>
        LerItensDeActions(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/actions/secrets?per_page=100", usuario, "os segredos"), "secrets");

    public static async Task<List<ItemDeActions>> VariaveisAsync(string slug, string usuario) =>
        LerItensDeActions(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/actions/variables?per_page=30", usuario, "as variáveis"), "variables");

    public static List<ItemDeActions> LerItensDeActions(string json, string campo)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(campo, out var lista) || lista.ValueKind != JsonValueKind.Array)
            return new List<ItemDeActions>();

        return lista.EnumerateArray().Select(i => new ItemDeActions
        {
            Nome = Texto(i, "name"),
            Valor = Texto(i, "value"),
            Atualizado = Data(i, "updated_at") ?? Data(i, "created_at"),
        }).OrderBy(i => i.Nome, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Cria ou troca o valor de um segredo. O GitHub só aceita o valor cifrado com a
    /// chave pública do repositório (caixa selada do libsodium): nem ele consegue ler de
    /// volta, só as execuções do Actions.
    /// </summary>
    public static async Task GravarSegredoAsync(string slug, string nome, string valor, string usuario)
    {
        using var doc = JsonDocument.Parse(await BaixarAsync(
            $"https://api.github.com/repos/{slug}/actions/secrets/public-key", usuario, "a chave do repositório"));
        var chaveId = Texto(doc.RootElement, "key_id");
        var chave = Convert.FromBase64String(Texto(doc.RootElement, "key"));

        await EscreverPrAsync(HttpMethod.Put,
            $"https://api.github.com/repos/{slug}/actions/secrets/{Uri.EscapeDataString(nome.Trim().ToUpperInvariant())}",
            new { encrypted_value = Cifrar(valor, chave), key_id = chaveId },
            usuario, "gravar o segredo", PermissaoDeSegredos);
    }

    /// <summary>Caixa selada (crypto_box_seal) em base64, como a API espera.</summary>
    public static string Cifrar(string valor, byte[] chavePublica) =>
        Convert.ToBase64String(Sodium.SealedPublicKeyBox.Create(Encoding.UTF8.GetBytes(valor), chavePublica));

    public static Task ExcluirSegredoAsync(string slug, string nome, string usuario) =>
        EscreverPrAsync(HttpMethod.Delete,
            $"https://api.github.com/repos/{slug}/actions/secrets/{Uri.EscapeDataString(nome)}",
            new { }, usuario, "excluir o segredo", PermissaoDeSegredos);

    /// <param name="existe">Variável que já existe é editada (PATCH); nova é criada (POST).</param>
    public static Task GravarVariavelAsync(string slug, string nome, string valor, bool existe, string usuario)
    {
        var n = nome.Trim().ToUpperInvariant();
        return existe
            ? EscreverPrAsync(HttpMethod.Patch,
                $"https://api.github.com/repos/{slug}/actions/variables/{Uri.EscapeDataString(n)}",
                new { name = n, value = valor }, usuario, "gravar a variável", PermissaoDeSegredos)
            : EscreverPrAsync(HttpMethod.Post, $"https://api.github.com/repos/{slug}/actions/variables",
                new { name = n, value = valor }, usuario, "criar a variável", PermissaoDeSegredos);
    }

    public static Task ExcluirVariavelAsync(string slug, string nome, string usuario) =>
        EscreverPrAsync(HttpMethod.Delete,
            $"https://api.github.com/repos/{slug}/actions/variables/{Uri.EscapeDataString(nome)}",
            new { }, usuario, "excluir a variável", PermissaoDeSegredos);
}
