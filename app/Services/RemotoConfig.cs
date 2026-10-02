using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GRepos.Services;

/// <summary>
/// Link do remoto no padrão do GRepos: "https://{{user}}@github.com/...", com o token no
/// Credential Manager e nunca no .git/config. Antes eram dois botões ("Usar o remoto
/// atual", "Aplicar no git"); agora o modelo é sugerido ao abrir e aplicado ao salvar.
/// </summary>
public static class RemotoConfig
{
    /// <summary>Modelo para um repositório que ainda não tem: o remoto atual com {{user}}.</summary>
    public static async Task<string> SugerirAsync(string repo) =>
        UrlTemplate.Sugerir(await GitService.RemoteUrlAsync(repo));

    /// <summary>
    /// Leva o modelo ao git, se o link de lá for diferente. Tirar um token da URL sem ter
    /// outro guardado deixaria o push sem senha, então ele vai antes para o Credential
    /// Manager. Devolve o que mudou, em texto para a tela, ou null se nada mudou.
    /// </summary>
    public static async Task<string?> AplicarAsync(string repo, string? modelo, string usuario)
    {
        modelo = (modelo ?? "").Trim();
        if (modelo.Length == 0) return null;

        if (modelo.Contains("{{user}}", StringComparison.OrdinalIgnoreCase) && usuario.Length == 0)
            throw new GitException("Defina o usuário do GitHub em Preferências → Autenticação.");

        var guardado = usuario.Length > 0 ? await GitHubService.TokenDoUsuarioAsync(usuario) : null;
        var usaToken = UrlTemplate.UsaToken(modelo);
        if (usaToken && string.IsNullOrEmpty(guardado))
            throw new GitException($"Nenhum token salvo para “{usuario}”. Salve em Preferências → Autenticação.");

        var atual = await GitService.RemoteUrlAsync(repo);
        var token = usaToken ? guardado : null;
        var final = UrlTemplate.Expandir(modelo, usuario, token);
        if (final == atual) return null;

        var migrou = false;
        var segredo = UrlTemplate.SegredoEmbutido(atual);
        if (!usaToken && string.IsNullOrEmpty(guardado) && segredo is not null && usuario.Length > 0)
        {
            await GitHubService.SalvarCredencialAsync(usuario, segredo);
            migrou = true;
        }

        if (atual.Length == 0) await GitService.RunAsync(repo, new[] { "remote", "add", "origin", final });
        else await GitService.SetRemoteUrlAsync(repo, final);

        return "Link do git atualizado: " + UrlTemplate.Mascarar(final, token) +
               (migrou ? ". O token que estava no link foi guardado no Windows." : ".");
    }

    /// <summary>Nome da pasta que o "git clone" criaria: o último trecho do link, sem ".git".</summary>
    public static string NomeDoLink(string? url)
    {
        var u = (url ?? "").Trim().TrimEnd('/', '\\');
        if (u.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) u = u[..^4];
        var i = u.LastIndexOfAny(new[] { '/', '\\', ':' });
        return i >= 0 ? u[(i + 1)..] : u;
    }

    /// <summary>
    /// Clona já no padrão: o link que fica no .git/config leva só o usuário da conta. Se
    /// o link colado trouxer um token (como o SourceTree copia), ele não é gravado — vai
    /// para o Credential Manager quando a conta ainda não tem nenhum lá.
    /// </summary>
    public static async Task ClonarAsync(string url, string destino, string usuario)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) throw new GitException("Informe o link do repositório.");
        if (Directory.Exists(destino) && Directory.EnumerateFileSystemEntries(destino).Any())
            throw new GitException($"A pasta “{destino}” já existe e não está vazia.");

        var link = url;
        if (usuario.Length > 0 && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var segredo = UrlTemplate.SegredoEmbutido(url);
            if (segredo is not null && string.IsNullOrEmpty(await GitHubService.TokenDoUsuarioAsync(usuario)))
                await GitHubService.SalvarCredencialAsync(usuario, segredo);
            link = UrlTemplate.Expandir(UrlTemplate.Sugerir(url), usuario, null);
        }

        var pai = Path.GetDirectoryName(Path.GetFullPath(destino))!;
        Directory.CreateDirectory(pai);
        await GitService.CloneAsync(pai, link, destino, usuario);
    }

    /// <summary>
    /// Uma frase sobre o que acontece ao salvar, para a tela. Null quando o link do git
    /// já está como o modelo pede.
    /// </summary>
    public static string? Previa(string atual, string? modelo, string usuario)
    {
        modelo = (modelo ?? "").Trim();
        if (modelo.Length == 0 || UrlTemplate.UsaToken(modelo)) return null;

        var final = UrlTemplate.Expandir(modelo, usuario, null);
        if (final == atual) return null;

        return UrlTemplate.SegredoEmbutido(atual) is not null
            ? "O link atual tem um token gravado. Ao salvar, o token sai do link e o git passa a usar o da conta acima, guardado no Windows."
            : "Ao salvar, o link do git passa a ser " + final + ".";
    }
}
