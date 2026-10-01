using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GRepos.Services;
using GRepos.ViewModels;
using GRepos.Views;
using Xunit;

namespace GRepos.Tests;

public class TerminalTests
{
    /// <summary>
    /// O bash de verdade dentro do ConPTY: o que se escreve chega ao shell, a saída volta
    /// em UTF-8 (acento inteiro) e o diretório inicial é a pasta pedida, não o HOME.
    /// Sem Git instalado não há o que testar.
    /// </summary>
    [Fact]
    public async Task BashNoPseudoconsoleRespondeNaPastaPedida()
    {
        var bash = GitBash.LocalizarBash(null);
        if (bash is null) return;

        var pasta = Directory.CreateTempSubdirectory("grepos-term-").FullName;
        try
        {
            using var pty = ConPty.Iniciar($"\"{bash}\" --login -i", pasta, 120, 30,
                new System.Collections.Generic.Dictionary<string, string?> { ["CHERE_INVOKING"] = "1", ["MSYSTEM"] = "MINGW64" });

            var saida = new StringBuilder();
            var decoder = Encoding.UTF8.GetDecoder();
            _ = Task.Run(() =>
            {
                var buf = new byte[4096];
                var chars = new char[8192];
                try
                {
                    int n;
                    while ((n = pty.Saida.Read(buf, 0, buf.Length)) > 0)
                    {
                        var c = decoder.GetChars(buf, 0, n, chars, 0);
                        lock (saida) saida.Append(chars, 0, c);
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            });

            pty.Escrever(Encoding.UTF8.GetBytes("echo \"fim-$((40+2))-ação\"; pwd -W\r"));

            var relogio = Stopwatch.StartNew();
            string texto;
            do
            {
                await Task.Delay(100);
                lock (saida) texto = saida.ToString();
            } while (!texto.Contains("fim-42-ação") && relogio.Elapsed < TimeSpan.FromSeconds(20));

            Assert.Contains("fim-42-ação", texto);
            Assert.Contains(Path.GetFileName(pasta), texto);
        }
        finally
        {
            try { Directory.Delete(pasta, true); } catch (IOException) { }
        }
    }

    /// <summary>O painel monta com uma sessão (sem processo) e mostra o que ela recebe.</summary>
    [AvaloniaFact]
    public void PainelDoTerminalMontaComSessao()
    {
        var sessao = new TerminalSessao("r1", "Financeiro", Directory.GetCurrentDirectory(), () => "")
        {
            Ativa = true,
        };
        sessao.Modelo.Feed("\x1b[32mverde\x1b[0m olá\r\n");

        var main = new MainViewModel(new SemDialogos());
        main.Terminais.Add(sessao);

        var painel = new TerminalPanel { DataContext = main };
        var janela = new Avalonia.Controls.Window { Width = 900, Height = 300, Content = painel };
        janela.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("verde olá", sessao.Modelo.Terminal.Engine.GetLine(0));
        janela.Close();
    }

    /// <summary>
    /// Bash vivo no painel, rodando git com cor. Só grava PNG com GREPOS_SHOTS; serve para
    /// conferir cores, fonte e prompt sem abrir o app.
    /// </summary>
    [AvaloniaFact]
    public async Task PainelComBashDeVerdade()
    {
        var pastaShots = Environment.GetEnvironmentVariable("GREPOS_SHOTS");
        if (pastaShots is null || GitBash.LocalizarBash(null) is null) return;

        var repo = Directory.CreateTempSubdirectory("grepos-shot-").FullName;
        var sessao = new TerminalSessao("r1", "Financeiro", repo, () => "") { Ativa = true };
        try
        {
            var main = new MainViewModel(new SemDialogos());
            main.Terminais.Add(sessao);
            var janela = new Avalonia.Controls.Window
            {
                Width = 1000, Height = 360, Content = new TerminalPanel { DataContext = main },
            };
            janela.Show();
            sessao.Iniciar();

            sessao.Modelo.Send("git init -q -b main && echo olá > leia-me.txt && git add . && " +
                               "git -c user.name=GRepos -c user.email=g@r commit -qm 'primeiro commit' && " +
                               "echo mudou >> leia-me.txt && git status -sb && git log --oneline --decorate --color && ls --color\r");

            for (var i = 0; i < 60; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(100);
            }

            Directory.CreateDirectory(pastaShots);
            ((TerminalPanel)janela.Content!).Focar(); // com foco aparece o cursor em barra
            Dispatcher.UIThread.RunJobs();
            using var frame = janela.CaptureRenderedFrame();
            frame?.Save(Path.Combine(pastaShots, "terminal.png"));
            janela.Close();
        }
        finally
        {
            sessao.Dispose();
            try { Directory.Delete(repo, true); } catch (Exception) { /* .git pode estar preso */ }
        }
    }

    /// <summary>
    /// Tecla vai ao shell ao apertar (não ao soltar), texto sai uma vez só pelo TextInput,
    /// acento composto chega inteiro e AltGr do ABNT2 não vira Ctrl+letra.
    /// </summary>
    [AvaloniaFact]
    public void TecladoMandaCadaTeclaUmaVezAoApertar()
    {
        var modelo = new AvaloniaTerminal.TerminalControlModel();
        var enviado = new System.Collections.Generic.List<byte>();
        modelo.UserInput += b => enviado.AddRange(b);

        var controle = new AvaloniaTerminal.TerminalControl { Model = modelo };
        var janela = new Avalonia.Controls.Window { Width = 600, Height = 300, Content = controle };
        janela.Show();
        controle.Focus();

        string Tecla(Avalonia.Input.Key k, Avalonia.Input.RawInputModifiers m = default, string? simbolo = null, string? texto = null, bool soltar = true)
        {
            enviado.Clear();
            janela.KeyPress(k, m, Avalonia.Input.PhysicalKey.None, simbolo);
            if (texto is not null) janela.KeyTextInput(texto);
            if (soltar) janela.KeyRelease(k, m, Avalonia.Input.PhysicalKey.None, simbolo);
            return Encoding.UTF8.GetString(enviado.ToArray());
        }

        Assert.Equal("a", Tecla(Avalonia.Input.Key.A, simbolo: "a", texto: "a"));
        Assert.Equal("a", Tecla(Avalonia.Input.Key.A, simbolo: "a", texto: "a", soltar: false)); // sem esperar soltar
        Assert.Equal(" ", Tecla(Avalonia.Input.Key.Space, simbolo: " ", texto: " "));
        Assert.Equal("á", Tecla(Avalonia.Input.Key.A, simbolo: "a", texto: "á"));
        Assert.Equal("\r", Tecla(Avalonia.Input.Key.Enter, simbolo: "\r", texto: "\r"));
        Assert.Equal("\x7f", Tecla(Avalonia.Input.Key.Back, simbolo: "\b", texto: "\b"));
        Assert.Equal("\t", Tecla(Avalonia.Input.Key.Tab, simbolo: "\t", texto: "\t"));
        Assert.Equal("\u0003", Tecla(Avalonia.Input.Key.C, Avalonia.Input.RawInputModifiers.Control, "c"));
        Assert.Equal("/", Tecla(Avalonia.Input.Key.Q,
            Avalonia.Input.RawInputModifiers.Control | Avalonia.Input.RawInputModifiers.Alt, "/", "/"));
        Assert.Equal("\x1b[D", Tecla(Avalonia.Input.Key.Left));
        Assert.Equal("\x1b[1;5D", Tecla(Avalonia.Input.Key.Left, Avalonia.Input.RawInputModifiers.Control));

        Assert.Equal(AvaloniaTerminal.TerminalCaretStyle.Bar, controle.CaretStyle);
        janela.Close();
    }

    private sealed class SemDialogos : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string title, string label, string initial = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel main) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel main, GRepos.Models.Repo repo) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel main) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel main, GRepos.Models.Repo repo) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel main, GRepos.Models.Repo repo) => Task.CompletedTask;
    }
}
