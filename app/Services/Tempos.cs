using System;
using System.Diagnostics;
using System.IO;

namespace GRepos.Services;

/// <summary>
/// Marcas de tempo da abertura, para medir antes de otimizar. Só grava com
/// <c>GREPOS_TEMPOS</c> apontando para um arquivo; sem a variável, cada marca é um teste
/// de nulo e nada mais. O relógio parte do início do processo, não do Main: o .exe de
/// arquivo único gasta um tempo antes de chegar ao código.
/// </summary>
public static class Tempos
{
    private static readonly string? Arquivo = Environment.GetEnvironmentVariable("GREPOS_TEMPOS") is { Length: > 0 } a ? a : null;
    private static readonly DateTime Inicio = Arquivo is null ? default : InicioDoProcesso();
    private static readonly object Trava = new();

    public static bool Ligado => Arquivo is not null;

    public static void Marcar(string o)
    {
        if (Arquivo is null) return;
        var ms = (DateTime.Now - Inicio).TotalMilliseconds;
        lock (Trava)
        {
            try { File.AppendAllText(Arquivo, $"{ms,8:0} ms  {o}{Environment.NewLine}"); }
            catch (Exception) { /* medição não derruba o app */ }
        }
    }

    private static DateTime InicioDoProcesso()
    {
        try { return Process.GetCurrentProcess().StartTime; }
        catch (Exception) { return DateTime.Now; }
    }
}
