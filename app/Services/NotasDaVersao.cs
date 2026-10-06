using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Platform;

namespace GRepos.Services;

/// <summary>Uma versão das notas: o número, a data e o que mudou, em markdown.</summary>
public sealed record NotaDaVersao(string Versao, string Data, string Notas)
{
    public string Rotulo => Data.Length > 0 ? $"{Versao} — {Data}" : Versao;
}

/// <summary>
/// Notas de cada versão, escritas em <c>Assets/notas-da-versao.md</c> e embutidas no
/// executável. Diferente de "Novidades", que lê as releases do GitHub, estas funcionam
/// sem internet e dizem o que mudou em linguagem de quem usa, não em assunto de commit.
/// </summary>
public static class NotasDaVersao
{
    private const string Recurso = "avares://GRepos/Assets/notas-da-versao.md";

    /// <summary>
    /// Cada versão começa numa linha <c>## 1.0.0.N — dd/mm/aaaa</c>; o que vem até a
    /// próxima é a nota. Texto antes da primeira versão é ignorado.
    /// </summary>
    public static List<NotaDaVersao> Ler(string texto)
    {
        var lista = new List<NotaDaVersao>();
        string? versao = null;
        var data = "";
        var corpo = new StringBuilder();

        void Fechar()
        {
            if (versao is not null) lista.Add(new NotaDaVersao(versao, data, corpo.ToString().Trim()));
            corpo.Clear();
        }

        foreach (var bruta in texto.Replace("\r\n", "\n").Split('\n'))
        {
            if (bruta.StartsWith("## ", StringComparison.Ordinal))
            {
                Fechar();
                var titulo = bruta[3..].Trim();
                var corte = titulo.IndexOf(" — ", StringComparison.Ordinal);
                versao = corte < 0 ? titulo : titulo[..corte].Trim();
                data = corte < 0 ? "" : titulo[(corte + 3)..].Trim();
                continue;
            }
            if (versao is not null) corpo.Append(bruta).Append('\n');
        }
        Fechar();
        return lista;
    }

    /// <summary>As notas embutidas, da mais nova para a mais antiga; vazio se o recurso faltar.</summary>
    public static List<NotaDaVersao> Carregar()
    {
        try
        {
            using var leitor = new StreamReader(AssetLoader.Open(new Uri(Recurso)), Encoding.UTF8);
            return Ler(leitor.ReadToEnd());
        }
        catch (Exception)
        {
            return new List<NotaDaVersao>(); // a seção mostra "sem notas" em vez de derrubar as Preferências
        }
    }

    /// <summary>
    /// Posição da versão em uso na lista. O build local e o de desenvolvimento
    /// (<c>1.0.0.30-dev.2</c>) contam como a versão de base; sem correspondência, a mais nova.
    /// </summary>
    public static int IndiceDa(IReadOnlyList<NotaDaVersao> notas, string emUso)
    {
        var limpa = emUso.Trim().TrimStart('v', 'V').Split('-')[0];
        for (var i = 0; i < notas.Count; i++)
            if (notas[i].Versao == limpa) return i;
        return 0;
    }
}
