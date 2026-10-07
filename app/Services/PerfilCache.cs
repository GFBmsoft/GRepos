using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GRepos.Services;

/// <summary>O que o cartão de perfil mostrou da última vez, para mostrar de novo na hora.</summary>
public sealed class PerfilGuardado
{
    public Perfil Perfil { get; set; } = new();
    public int TotalContribuicoes { get; set; }
    public List<DiaContribuicao> Dias { get; set; } = new();

    /// <summary>A foto em base64; vazia quando a conta não tem ou ela não chegou.</summary>
    public string Foto { get; set; } = "";

    /// <summary>Quando foi buscado no GitHub (UTC).</summary>
    public DateTime Quando { get; set; }
}

/// <summary>
/// Cache do cartão de perfil, em memória e em disco. O cartão custa quatro consultas ao
/// GitHub (conta, repositórios, foto e contribuições) e mais a busca do token — cerca de
/// um segundo e meio medido, toda vez que o painel abria. Com o que foi guardado ele
/// aparece na hora, e a atualização acontece por trás quando o guardado já está velho.
///
/// É só cache: perder o arquivo custa uma consulta, então erro de leitura ou de escrita
/// nunca vira aviso na tela.
/// </summary>
public static class PerfilCache
{
    /// <summary>Dentro disto o guardado vale como atual, e o GitHub nem é consultado.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(15);

    private static readonly ConcurrentDictionary<string, PerfilGuardado> Memoria = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Fica ao lado do workspace, numa pasta própria: é o que se apaga sem medo.</summary>
    public static string Arquivo(string login)
    {
        var nome = new string(login.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
        return Path.Combine(WorkspaceStore.Directory, "cache", $"perfil-{nome}.json");
    }

    public static PerfilGuardado? Ler(string login)
    {
        if (string.IsNullOrWhiteSpace(login)) return null;

        var arquivo = Arquivo(login);
        if (Memoria.TryGetValue(arquivo, out var naMemoria)) return naMemoria;

        try
        {
            if (!File.Exists(arquivo)) return null;

            var lido = JsonSerializer.Deserialize<PerfilGuardado>(File.ReadAllText(arquivo), Options);
            if (lido is null || lido.Perfil.Login.Length == 0) return null;

            Memoria[arquivo] = lido;
            return lido;
        }
        catch (Exception)
        {
            return null; // arquivo de outra versão, cortado ou ilegível: busca-se de novo
        }
    }

    public static void Guardar(string login, PerfilGuardado guardado)
    {
        if (string.IsNullOrWhiteSpace(login)) return;

        var arquivo = Arquivo(login);
        Memoria[arquivo] = guardado;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);

            // .tmp e troca, como o workspace: queda no meio não deixa o cache pela metade
            var tmp = arquivo + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(guardado, Options));
            File.Move(tmp, arquivo, overwrite: true);
        }
        catch (Exception)
        {
            // pasta sem escrita: o cache fica só na memória desta execução
        }
    }

    public static bool Atual(PerfilGuardado guardado, DateTime? agora = null) =>
        (agora ?? DateTime.UtcNow) - guardado.Quando < Validade;

    /// <summary>Esquece o que está na memória; os testes usam para ler do disco de novo.</summary>
    public static void EsquecerMemoria() => Memoria.Clear();
}
