using System.Text.RegularExpressions;

namespace GRepos.Services;

/// <summary>
/// A tag sugerida para uma release nova: a última com o número final somado de um
/// (1.0.0.46 → 1.0.0.47, v2.3 → v2.4). Quem segue outro esquema só apaga e digita.
/// </summary>
public static class ProximaVersao
{
    private static readonly Regex UltimoNumero = new(@"(\d+)(?!.*\d)", RegexOptions.Compiled);

    public static string Sugerir(string? ultima)
    {
        if (string.IsNullOrWhiteSpace(ultima)) return "";
        var m = UltimoNumero.Match(ultima);
        if (!m.Success) return "";

        var proximo = (long.Parse(m.Value) + 1).ToString().PadLeft(m.Value.Length, '0');
        return ultima[..m.Index] + proximo + ultima[(m.Index + m.Length)..];
    }
}
