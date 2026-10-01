using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaTerminal;
using CommunityToolkit.Mvvm.ComponentModel;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>
/// Um Git Bash embutido, preso a um repositório. Continua vivo ao trocar de repositório
/// (como as abas de terminal do VS Code): voltar a ele devolve o mesmo shell, com o
/// histórico e o que estiver rodando.
/// </summary>
public sealed partial class TerminalSessao : ObservableObject, IDisposable
{
    private readonly Func<string?> _gitBashConfigurado;
    private ConPty? _pty;
    private int _colunas = 100;
    private int _linhas = 24;

    public string RepoId { get; }
    public string Nome { get; }
    public string Pasta { get; }
    public TerminalControlModel Modelo { get; }

    /// <summary>A sessão do repositório selecionado — a única visível no painel.</summary>
    [ObservableProperty] private bool _ativa;

    /// <summary>O shell saiu (exit, Ctrl+D): Enter abre outro na mesma pasta.</summary>
    [ObservableProperty] private bool _encerrada;

    public TerminalSessao(string repoId, string nome, string pasta, Func<string?> gitBashConfigurado)
    {
        RepoId = repoId;
        Nome = nome;
        Pasta = pasta;
        _gitBashConfigurado = gitBashConfigurado;

        // sem reflow: TUIs de tela cheia (vim, less, git log) se desmancham ao redimensionar
        Modelo = new TerminalControlModel(new TerminalOptions
        {
            Cols = _colunas,
            Rows = _linhas,
            Scrollback = 5000,
            ReflowOnResize = false,
        });

        Modelo.UserInput += bytes =>
        {
            if (!Encerrada)
                _pty?.Escrever(bytes);
            else if (Array.IndexOf(bytes, (byte)'\r') >= 0)
                Iniciar();
        };
        Modelo.SizeChanged += (colunas, linhas, _, _) =>
        {
            _colunas = colunas;
            _linhas = linhas;
            _pty?.Redimensionar(colunas, linhas);
        };
    }

    public void Iniciar()
    {
        if (!Directory.Exists(Pasta))
            throw new DirectoryNotFoundException("Pasta não encontrada: " + Pasta);

        var configurado = _gitBashConfigurado();
        var bash = GitBash.LocalizarBash(configurado) ?? throw new FileNotFoundException(
            string.IsNullOrWhiteSpace(configurado)
                ? "Git Bash não encontrado. Informe a pasta do Git em Preferências."
                : "bash.exe não encontrado em " + configurado.Trim() + ". Confira o caminho em Preferências.");

        _pty?.Dispose();
        var pty = ConPty.Iniciar($"\"{bash}\" --login -i", Pasta, _colunas, _linhas, Ambiente());
        _pty = pty;
        Encerrada = false;

        pty.Encerrou += () => Dispatcher.UIThread.Post(() =>
        {
            if (_pty != pty) return; // já foi trocado por um novo
            Encerrada = true;
            Modelo.Feed("\r\n\x1b[2m[processo encerrado — Enter abre outro]\x1b[0m\r\n");
        });

        Task.Run(() => LerSaida(pty));
    }

    private void LerSaida(ConPty pty)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                var lidos = pty.Saida.Read(buffer, 0, buffer.Length);
                if (lidos <= 0) break;

                var copia = new byte[lidos];
                Buffer.BlockCopy(buffer, 0, copia, 0, lidos);
                Dispatcher.UIThread.Post(() => Modelo.Feed(copia, copia.Length));
            }
        }
        catch (IOException)
        {
            // pipe fechado: o processo saiu ou a sessão foi descartada
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static Dictionary<string, string?> Ambiente() => new()
    {
        // o /etc/profile do Git Bash vai para o HOME sem isto
        ["CHERE_INVOKING"] = "1",
        ["MSYSTEM"] = "MINGW64",
        ["TERM"] = "xterm-256color",

        // mesmas que o GitService não herda: com elas o git do terminal não pede login
        ["GIT_TERMINAL_PROMPT"] = null,
        ["GCM_INTERACTIVE"] = null,
        ["GIT_ASKPASS"] = null,
        ["SSH_ASKPASS"] = null,
    };

    public void Dispose()
    {
        var pty = _pty;
        _pty = null;
        pty?.Dispose();
    }
}
