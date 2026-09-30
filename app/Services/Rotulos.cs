using System;

namespace GRepos.Services;

/// <summary>Encurtamento de textos da barra de ferramentas, onde o espaço é curto.</summary>
public static class Rotulos
{
    public const int LimiteBranch = 14;

    /// <summary>
    /// Nome de branch no tamanho do botão. Primeiro tenta o último segmento — em
    /// "fix/CorrecaoTbEdit" o prefixo diz menos do que o nome — e só então corta com
    /// reticências. O nome inteiro continua na dica do botão.
    /// </summary>
    public static string Branch(string nome, int limite = LimiteBranch)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "—";

        nome = nome.Trim();
        if (nome.Length <= limite) return nome;

        var barra = nome.LastIndexOf('/');
        if (barra >= 0 && barra < nome.Length - 1)
        {
            var ultimo = nome[(barra + 1)..];
            if (ultimo.Length <= limite) return ultimo;
            nome = ultimo;
        }

        return nome[..(limite - 1)] + "…";
    }

    /// <summary>
    /// Versão a mostrar na barra de status, a partir do InformationalVersion.
    /// Só conta a que o workflow carimbou, que tem quatro números (1.0.0.1, 0.0.0.0-dev.7);
    /// build local fica com o "1.0.0" padrão do SDK e devolve vazio, para não parecer
    /// uma versão lançada. O "+sha" do fim não interessa a quem está olhando a tela.
    /// </summary>
    public static string VersaoPublicada(string? informacional)
    {
        if (string.IsNullOrWhiteSpace(informacional)) return "";

        var texto = informacional.Split('+')[0].Trim();
        var numeros = texto.Split('-')[0];

        return numeros.Split('.').Length == 4 ? texto : "";
    }

    /// <summary>"há 3 min", "há 2 h", "há 5 d" — data absoluta só quando passa de uma semana.</summary>
    public static string Quando(DateTime? utc, DateTime? agora = null)
    {
        if (utc is null) return "";

        var decorrido = (agora ?? DateTime.UtcNow) - utc.Value;
        if (decorrido < TimeSpan.Zero) decorrido = TimeSpan.Zero;

        if (decorrido.TotalMinutes < 1) return "agora";
        if (decorrido.TotalMinutes < 60) return $"há {(int)decorrido.TotalMinutes} min";
        if (decorrido.TotalHours < 24) return $"há {(int)decorrido.TotalHours} h";
        if (decorrido.TotalDays < 7) return $"há {(int)decorrido.TotalDays} d";
        return utc.Value.ToLocalTime().ToString("dd/MM/yyyy");
    }

    /// <summary>Duração de um passo: "8s", "1m 12s", "1h 04m".</summary>
    public static string Duracao(TimeSpan? tempo)
    {
        if (tempo is null) return "";

        var t = tempo.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes}m {t.Seconds:00}s";
        return $"{Math.Max(0, (int)t.TotalSeconds)}s";
    }
}
