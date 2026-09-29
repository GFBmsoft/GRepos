using System;
using System.IO;

namespace GRepos.Services;

/// <summary>
/// Observa o repositório em disco e avisa quando algo muda — arquivo salvo no editor,
/// commit feito por fora, troca de branch. Sem isso a lista de alterações só era
/// recarregada quando o usuário trocava de aba.
/// </summary>
public sealed class RepoWatcher : IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Timers.Timer _debounce;
    private readonly Action _onChanged;

    /// <summary>Janela de silêncio antes de recarregar: um "salvar" dispara vários eventos.</summary>
    private const int DebounceMs = 400;

    public RepoWatcher(string path, Action onChanged)
    {
        _onChanged = onChanged;

        _debounce = new System.Timers.Timer(DebounceMs) { AutoReset = false };
        _debounce.Elapsed += (_, _) => _onChanged();

        if (!Directory.Exists(path)) return;

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };

            _watcher.Changed += OnEvent;
            _watcher.Created += OnEvent;
            _watcher.Deleted += OnEvent;
            _watcher.Renamed += OnEvent;
            // estouro de buffer (build gerando milhares de arquivos) também merece recarga
            _watcher.Error += (_, _) => Poke();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            // rede, permissão ou limite do SO: o app segue funcionando sem atualização automática
            _watcher = null;
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (ShouldIgnore(e.FullPath)) return;
        Poke();
    }

    private void Poke()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Ruído de dentro do .git é ignorado, menos o que muda o estado visível
    /// (HEAD, index, refs) — é o que denuncia commit ou checkout feito por fora.
    /// </summary>
    public static bool ShouldIgnore(string fullPath)
    {
        var p = fullPath.Replace('\\', '/');
        var i = p.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return false;

        var inside = p[(i + 6)..];
        if (inside.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) return true;

        return !(inside.StartsWith("HEAD", StringComparison.OrdinalIgnoreCase)
                 || inside.StartsWith("index", StringComparison.OrdinalIgnoreCase)
                 || inside.StartsWith("refs/", StringComparison.OrdinalIgnoreCase)
                 || inside.StartsWith("MERGE_", StringComparison.OrdinalIgnoreCase)
                 || inside.StartsWith("logs/refs/stash", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _debounce.Stop();
        _debounce.Dispose();
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
