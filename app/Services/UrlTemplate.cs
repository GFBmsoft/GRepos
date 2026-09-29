using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GRepos.Services;

/// <summary>
/// URL de remoto com variáveis no estilo Postman: {{user}} e {{token}}. O modelo é o
/// que fica salvo; o valor real do token só aparece no momento de aplicar no git.
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
    /// Modelo a partir de uma URL comum: troca a credencial embutida pelas variáveis,
    /// para o usuário não precisar montar na mão.
    /// </summary>
    public static string Sugerir(string remoteUrl)
    {
        var url = (remoteUrl ?? "").Trim();
        if (url.Length == 0) return "";

        var i = url.IndexOf("://", StringComparison.Ordinal);
        if (i < 0) return url;

        var esquema = url[..(i + 3)];
        var resto = url[(i + 3)..];

        var arroba = resto.IndexOf('@');
        var barra = resto.IndexOf('/');
        if (arroba > 0 && (barra < 0 || arroba < barra)) resto = resto[(arroba + 1)..];

        return $"{esquema}{{{{user}}}}:{{{{token}}}}@{resto}";
    }
}
