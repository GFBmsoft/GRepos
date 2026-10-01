using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace GRepos.Services;

/// <summary>
/// Processo rodando num pseudoconsole do Windows (ConPTY, Windows 10 1809+): ele acha que
/// está num console de verdade, e a gente lê e escreve o fluxo VT por dois pipes. É o
/// mesmo mecanismo do terminal do VS Code e do Windows Terminal.
/// </summary>
public sealed class ConPty : IDisposable
{
    private IntPtr _hpc;
    private IntPtr _processo;
    private readonly FileStream _entrada;
    private bool _encerrado;

    /// <summary>Saída do processo, já com as sequências VT (UTF-8).</summary>
    public Stream Saida { get; }

    /// <summary>Disparado (fora da thread de UI) quando o processo termina.</summary>
    public event Action? Encerrou;

    private ConPty(IntPtr hpc, IntPtr processo, FileStream entrada, FileStream saida)
    {
        _hpc = hpc;
        _processo = processo;
        _entrada = entrada;
        Saida = saida;

        Task.Run(() =>
        {
            WaitForSingleObject(processo, Infinito);
            _encerrado = true;
            Encerrou?.Invoke();
        });
    }

    public static ConPty Iniciar(string linhaDeComando, string pasta, int colunas, int linhas,
                                 IDictionary<string, string?>? ambiente = null)
    {
        // pipe de entrada: nós escrevemos, o console lê; de saída: o contrário
        if (!CreatePipe(out var entradaLeitura, out var entradaEscrita, IntPtr.Zero, 0) ||
            !CreatePipe(out var saidaLeitura, out var saidaEscrita, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var hr = CreatePseudoConsole(Tamanho(colunas, linhas), entradaLeitura, saidaEscrita, 0, out var hpc);
        if (hr != 0) throw new Win32Exception(hr, "Não foi possível criar o pseudoconsole");

        // as pontas do console agora pertencem a ele
        entradaLeitura.Dispose();
        saidaEscrita.Dispose();

        var atributos = IntPtr.Zero;
        var blocoAmbiente = IntPtr.Zero;
        try
        {
            var tamanho = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref tamanho);
            atributos = Marshal.AllocHGlobal(tamanho);
            if (!InitializeProcThreadAttributeList(atributos, 1, 0, ref tamanho) ||
                !UpdateProcThreadAttribute(atributos, 0, (IntPtr)ProcThreadAttributePseudoConsole,
                                           hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var si = new STARTUPINFOEX { lpAttributeList = atributos };
            si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

            // handles nulos de propósito: sem esta flag o filho herda a saída do pai quando
            // ela está redirecionada (testes, app aberto por script) e escreve lá, não no
            // pseudoconsole
            si.StartupInfo.dwFlags = StartfUseStdHandles;

            blocoAmbiente = MontarAmbiente(ambiente);
            if (!CreateProcess(null, linhaDeComando, IntPtr.Zero, IntPtr.Zero, false,
                               ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                               blocoAmbiente, pasta, ref si, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível iniciar " + linhaDeComando);

            CloseHandle(pi.hThread);
            return new ConPty(hpc, pi.hProcess,
                              new FileStream(entradaEscrita, FileAccess.Write, 1),
                              new FileStream(saidaLeitura, FileAccess.Read, 1));
        }
        catch
        {
            ClosePseudoConsole(hpc);
            entradaEscrita.Dispose();
            saidaLeitura.Dispose();
            throw;
        }
        finally
        {
            if (atributos != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(atributos);
                Marshal.FreeHGlobal(atributos);
            }
            if (blocoAmbiente != IntPtr.Zero) Marshal.FreeHGlobal(blocoAmbiente);
        }
    }

    public void Escrever(byte[] dados)
    {
        if (_encerrado) return;
        try
        {
            _entrada.Write(dados, 0, dados.Length);
            _entrada.Flush();
        }
        catch (IOException)
        {
            // o processo saiu entre a tecla e a escrita: nada a fazer
        }
    }

    public void Redimensionar(int colunas, int linhas)
    {
        if (_hpc != IntPtr.Zero && colunas > 0 && linhas > 0)
            ResizePseudoConsole(_hpc, Tamanho(colunas, linhas));
    }

    public void Dispose()
    {
        if (_processo != IntPtr.Zero && !_encerrado) TerminateProcess(_processo, 0);

        // fechar o pseudoconsole encerra a leitura pendente da saída
        if (_hpc != IntPtr.Zero) ClosePseudoConsole(_hpc);
        _hpc = IntPtr.Zero;

        _entrada.Dispose();
        Saida.Dispose();
        if (_processo != IntPtr.Zero) CloseHandle(_processo);
        _processo = IntPtr.Zero;
    }

    private static COORD Tamanho(int colunas, int linhas) =>
        new() { X = (short)Math.Clamp(colunas, 1, short.MaxValue), Y = (short)Math.Clamp(linhas, 1, short.MaxValue) };

    /// <summary>
    /// Ambiente herdado com os ajustes pedidos (null remove a variável), no formato do
    /// CreateProcess: pares "nome=valor" separados por \0 e terminados por \0\0.
    /// </summary>
    private static IntPtr MontarAmbiente(IDictionary<string, string?>? ajustes)
    {
        if (ajustes is null || ajustes.Count == 0) return IntPtr.Zero;

        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            vars[(string)e.Key] = (string?)e.Value ?? "";
        foreach (var (nome, valor) in ajustes)
            if (valor is null) vars.Remove(nome);
            else vars[nome] = valor;

        var sb = new StringBuilder();
        foreach (var (nome, valor) in vars) sb.Append(nome).Append('=').Append(valor).Append('\0');
        sb.Append('\0');
        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    // ------------------------------------------------------------------ Win32

    private const uint Infinito = 0xFFFFFFFF;
    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const int StartfUseStdHandles = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle leitura, out SafeFileHandle escrita, IntPtr atributos, int tamanho);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(COORD tamanho, SafeFileHandle entrada, SafeFileHandle saida, uint flags, out IntPtr hpc);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr hpc, COORD tamanho);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hpc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lista, int quantos, int flags, ref IntPtr tamanho);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lista, uint flags, IntPtr atributo, IntPtr valor,
                                                         IntPtr tamanho, IntPtr anterior, IntPtr retorno);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lista);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(string? aplicacao, string linhaDeComando, IntPtr atributosProcesso,
                                             IntPtr atributosThread, bool herdarHandles, uint flags, IntPtr ambiente,
                                             string? pasta, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint ms);

    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr processo, uint codigo);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
