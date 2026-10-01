using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GRepos.Services;

/// <summary>
/// URL de remoto com variáveis no estilo Postman: {{user}} e {{token}}. O modelo é o
/// que fica salvo; o valor real do token só aparece no momento de aplicar no git.
///
/// O caminho recomendado é só {{user}} ("https://{{user}}@github.com/..."): o git manda
/// o usuário ao credential manager, que devolve o token daquela conta. {{token}} continua
/// aceito, mas grava o segredo em texto puro no .git/config.
/// </summary>
public static class UrlTemplate
{
    private static readonly Regex Variavel = new(@"\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}", RegexOptions.Compiled);

    public const string VariavelUsuario = "user";
    public const string VariavelToken = "token";

    /// <summary>Nomes usados no modelo, na ordem em que aparecem.</summary>
    public static IReadOnlyList<string> Variaveis(string modelo) =>
        Variavel.Matches(modelo ?? "").Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().ToList();

    public static bool UsaToken(string modelo) =>
        Variaveis(modelo).Contains(VariavelToken, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Troca as variáveis pelos valores. Variável sem valor conhecido fica como está,
    /// para o usuário ver o que faltou em vez de gerar uma URL quebrada.
    /// </summary>
    public static string Expandir(string modelo, string usuario, string? token)
    {
        if (string.IsNullOrEmpty(modelo)) return "";

        return Variavel.Replace(modelo, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            VariavelUsuario => Uri.EscapeDataString(usuario ?? ""),
            VariavelToken => string.IsNullOrEmpty(token) ? m.Value : Uri.EscapeDataString(token),
            _ => m.Value,
        });
    }

    /// <summary>Versão segura para exibir e registrar: o token vira ●●●●.</summary>
    public static string Mascarar(string url, string? token)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var saida = string.IsNullOrEmpty(token) ? url : url.Replace(token, "●●●●●●");
        return Variavel.Replace(saida, m => m.Value);
    }

    /// <summary>
    /// Modelo a partir de uma URL comum: troca a credencial embutida por {{user}}. O token
    /// fica fora de propósito — quem entrega é o credential manager, pela conta do usuário.
    /// </summary>
    public static string Sugerir(string remoteUrl)
    {
        var url = (remoteUrl ?? "").Trim();
        if (url.Length == 0) return "";

        var (esquema, _, resto) = Partes(url);
        if (esquema.Length == 0) return url;

        return $"{esquema}{{{{user}}}}@{resto}";
    }

    /// <summary>
    /// Token ou senha gravado dentro da URL — "https://ghp_x@github.com/..." ou
    /// "https://user:senha@...". É como o SourceTree deixa os remotos: o segredo fica em
    /// texto puro no .git/config. Só o usuário ("https://GFBmsoft@...") não conta.
    /// </summary>
    public static string? SegredoEmbutido(string remoteUrl)
    {
        var (esquema, credencial, _) = Partes((remoteUrl ?? "").Trim());
        if (esquema.Length == 0 || credencial.Length == 0) return null;

        var doisPontos = credencial.IndexOf(':');
        if (doisPontos >= 0) return credencial[(doisPontos + 1)..] is { Length: > 0 } s ? Uri.UnescapeDataString(s) : null;

        // sem dois-pontos é só um nome; vira segredo quando tem cara de token do GitHub
        return PareceToken(credencial) ? credencial : null;
    }

    private static bool PareceToken(string texto) =>
        texto.StartsWith("ghp_", StringComparison.Ordinal) ||
        texto.StartsWith("gho_", StringComparison.Ordinal) ||
        texto.StartsWith("ghu_", StringComparison.Ordinal) ||
        texto.StartsWith("ghs_", StringComparison.Ordinal) ||
        texto.StartsWith("github_pat_", StringComparison.Ordinal);

    /// <summary>"https://", o que vem antes do @ (vazio se nada) e o resto.</summary>
    private static (string Esquema, string Credencial, string Resto) Partes(string url)
    {
        var i = url.IndexOf("://", StringComparison.Ordinal);
        if (i < 0) return ("", "", url);

        var esquema = url[..(i + 3)];
        var resto = url[(i + 3)..];

        var arroba = resto.IndexOf('@');
        var barra = resto.IndexOf('/');
        if (arroba > 0 && (barra < 0 || arroba < barra))
            return (esquema, resto[..arroba], resto[(arroba + 1)..]);

        return (esquema, "", resto);
    }
}
