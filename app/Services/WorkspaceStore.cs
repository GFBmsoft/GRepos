using System;
using System.IO;
using System.Text.Json;
using GRepos.Models;

namespace GRepos.Services;

/// <summary>Persistência do workspace (grupos, repositórios, pares e preferências).</summary>
public static class WorkspaceStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public const string NomeDoArquivo = "workspace.json";

    /// <summary>
    /// Desvios usados só pelos testes: sem eles, exercitar a troca entre perfil e modo
    /// portátil significaria mover o workspace real do usuário de um lado para o outro.
    /// </summary>
    internal static string? PastaDoAppDeTeste { get; set; }
    internal static string? PastaPadraoDeTeste { get; set; }

    /// <summary>Pasta do executável em uso; vazia quando não dá para descobrir.</summary>
    public static string PastaDoApp =>
        PastaDoAppDeTeste ??
        (Environment.ProcessPath is { Length: > 0 } exe ? Path.GetDirectoryName(exe) ?? "" : "");

    /// <summary>
    /// Modo portátil: existir um workspace.json ao lado do executável **é** o próprio
    /// sinal. Guardar essa escolha dentro do arquivo seria circular — é preciso saber
    /// onde ele está antes de conseguir lê-lo.
    /// </summary>
    public static bool Portatil =>
        Environment.GetEnvironmentVariable("GREPOS_HOME") is not { Length: > 0 } &&
        PastaDoApp.Length > 0 &&
        File.Exists(Path.Combine(PastaDoApp, NomeDoArquivo));

    /// <summary>
    /// %APPDATA%\GRepos por padrão, a pasta do executável em modo portátil.
    /// GREPOS_HOME vence os dois — é como os testes trabalham sem tocar no workspace real.
    /// </summary>
    public static string Directory
    {
        get
        {
            if (Environment.GetEnvironmentVariable("GREPOS_HOME") is { Length: > 0 } custom) return custom;
            if (Portatil) return PastaDoApp;
            return PastaPadrao;
        }
    }

    public static string PastaPadrao =>
        PastaPadraoDeTeste ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GRepos");

    public static string FilePath => Path.Combine(Directory, NomeDoArquivo);

    /// <summary>
    /// Liga ou desliga o modo portátil movendo o arquivo — nunca copiando: duas cópias
    /// divergindo seria pior que qualquer inconveniente de caminho. Devolve onde ficou.
    /// </summary>
    public static string MoverPara(bool portatil)
    {
        if (Environment.GetEnvironmentVariable("GREPOS_HOME") is { Length: > 0 })
            throw new InvalidOperationException(
                "GREPOS_HOME está definido e manda no local do workspace; " +
                "remova a variável para escolher aqui.");

        if (PastaDoApp.Length == 0)
            throw new InvalidOperationException("Não foi possível descobrir a pasta do executável.");

        var doApp = Path.Combine(PastaDoApp, NomeDoArquivo);
        var doPerfil = Path.Combine(PastaPadrao, NomeDoArquivo);

        var origem = portatil ? doPerfil : doApp;
        var destino = portatil ? doApp : doPerfil;

        if (portatil && !Atualizador.PastaGravavel(PastaDoApp))
            throw new InvalidOperationException(
                "A pasta do aplicativo não aceita escrita. Mova o GRepos para uma pasta " +
                "sua (fora de Arquivos de Programas) ou mantenha as configurações no perfil.");

        var pastaDestino = Path.GetDirectoryName(destino);
        if (!string.IsNullOrEmpty(pastaDestino)) System.IO.Directory.CreateDirectory(pastaDestino);

        if (File.Exists(origem)) File.Move(origem, destino, overwrite: true);
        else if (!File.Exists(destino)) SaveTo(destino, new Workspace());

        return destino;
    }

    public static Workspace Load() => LoadFrom(FilePath);

    public static Workspace LoadFrom(string file)
    {
        try
        {
            if (!File.Exists(file)) return new Workspace();
            var raw = File.ReadAllText(file);
            return JsonSerializer.Deserialize<Workspace>(raw, Options) ?? new Workspace();
        }
        catch (Exception)
        {
            // workspace corrompido não pode impedir a abertura do aplicativo
            return new Workspace();
        }
    }

    public static void Save(Workspace ws) => SaveTo(FilePath, ws);

    public static void SaveTo(string file, Workspace ws)
    {
        var dir = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(ws, Options);
        // grava em arquivo temporário e substitui: evita workspace truncado se o
        // processo morrer no meio da escrita
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, file, overwrite: true);
    }
}
