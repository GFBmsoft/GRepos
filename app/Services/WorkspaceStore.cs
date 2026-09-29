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

    /// <summary>
    /// %APPDATA%\GRepos por padrão. GREPOS_HOME aponta para outra pasta — usado para
    /// instalação portátil e para testar sem mexer no workspace real.
    /// </summary>
    public static string Directory =>
        Environment.GetEnvironmentVariable("GREPOS_HOME") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GRepos");

    public static string FilePath => Path.Combine(Directory, "workspace.json");

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
