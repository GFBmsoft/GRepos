using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>Criar repositório no GitHub a partir do local, e fork — o "gh repo create/fork".</summary>
public static partial class GitHubService
{
    private const string PermissaoDeCriar =
        "repositórios da conta. Um PAT clássico precisa do escopo \"repo\"; um fine-grained, " +
        "de \"Administration: Read and write\" em todos os repositórios";

    /// <summary>Organizações da conta: donas possíveis do repositório novo, além dela.</summary>
    public static async Task<List<string>> OrganizacoesAsync(string usuario)
    {
        using var doc = JsonDocument.Parse(await BaixarAsync(
            "https://api.github.com/user/orgs?per_page=100", usuario, "as organizações"));
        return Lista(doc).Select(o => Texto(o, "login")).Where(l => l.Length > 0).ToList();
    }

    /// <summary>Cria o repositório vazio; devolve o "dono/nome" que o GitHub deu.</summary>
    /// <param name="organizacao">Vazio cria na conta do próprio usuário.</param>
    public static async Task<string> CriarRepositorioAsync(string nome, bool privado, string descricao,
        string organizacao, string usuario)
    {
        var url = organizacao.Length > 0
            ? $"https://api.github.com/orgs/{organizacao}/repos"
            : "https://api.github.com/user/repos";

        var json = await EscreverPrAsync(HttpMethod.Post, url,
            new { name = nome.Trim(), @private = privado, description = descricao.Trim() },
            usuario, "criar o repositório", PermissaoDeCriar);

        using var doc = JsonDocument.Parse(json);
        return Texto(doc.RootElement, "full_name");
    }

    /// <summary>
    /// Fork na conta do usuário. O GitHub responde na hora e copia em segundo plano;
    /// o endereço já vale para o remoto.
    /// </summary>
    public static async Task<string> ForkAsync(string slug, string usuario)
    {
        var json = await EscreverPrAsync(HttpMethod.Post, $"https://api.github.com/repos/{slug}/forks",
            new { }, usuario, "fazer o fork", PermissaoDeCriar);
        using var doc = JsonDocument.Parse(json);
        return Texto(doc.RootElement, "full_name");
    }

    /// <summary>
    /// Endereço do remoto com a conta na frente (https://conta@github.com/dono/nome.git):
    /// é por ela que o credential manager acha o token certo quando há mais de uma conta.
    /// </summary>
    public static string UrlDoRemoto(string usuario, string nomeCompleto) =>
        usuario.Trim().Length > 0
            ? $"https://{Uri.EscapeDataString(usuario.Trim())}@github.com/{nomeCompleto}.git"
            : $"https://github.com/{nomeCompleto}.git";

    /// <summary>
    /// Nome aceito pelo GitHub a partir do nome da pasta: letras, números, ponto, hífen e
    /// sublinhado; o resto vira hífen (é o que o próprio GitHub faz).
    /// </summary>
    public static string NomeDeRepositorio(string pasta)
    {
        var chars = pasta.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-').ToArray();
        var nome = new string(chars);
        while (nome.Contains("--", StringComparison.Ordinal)) nome = nome.Replace("--", "-");
        return nome.Trim('-');
    }
}
