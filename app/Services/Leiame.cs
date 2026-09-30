using System;
using System.IO;

namespace GRepos.Services;

/// <summary>Localiza e lê o README do repositório.</summary>
public static class Leiame
{
    /// <summary>
    /// Nomes aceitos, na ordem de preferência. O Windows não diferencia maiúsculas, mas
    /// a lista vale para repositórios que vieram de outro sistema e para a extensão
    /// (.md é o comum; README sem extensão ainda aparece em projetos antigos).
    /// </summary>
    private static readonly string[] Nomes =
    {
        "README.md", "readme.md", "Readme.md",
        "README.markdown", "README.txt", "README",
    };

    /// <summary>Caminho do README, ou null quando o repositório não tem um.</summary>
    public static string? Caminho(string pastaDoRepo)
    {
        if (string.IsNullOrWhiteSpace(pastaDoRepo) || !Directory.Exists(pastaDoRepo)) return null;

        foreach (var nome in Nomes)
        {
            var caminho = Path.Combine(pastaDoRepo, nome);
            if (File.Exists(caminho)) return caminho;
        }
        return null;
    }

    /// <summary>
    /// Conteúdo do README, ou vazio se não houver. README gigante é cortado: a aba é
    /// para dar contexto do projeto, não para servir de editor.
    /// </summary>
    public static string Ler(string pastaDoRepo, int limiteDeCaracteres = 120_000)
    {
        try
        {
            var caminho = Caminho(pastaDoRepo);
            if (caminho is null) return "";

            var texto = File.ReadAllText(caminho);
            return texto.Length <= limiteDeCaracteres
                ? texto
                : texto[..limiteDeCaracteres] + "\n\n---\n\n*(arquivo cortado para exibição)*";
        }
        catch (Exception)
        {
            // arquivo em uso ou sem permissão não pode impedir a aba de abrir
            return "";
        }
    }
}
