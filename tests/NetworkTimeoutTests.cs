using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Operação de rede pendurada (o caso clássico: janela do credential manager esperando
/// resposta) não pode deixar a barra do app desabilitada para sempre.
/// </summary>
public class NetworkTimeoutTests
{
    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (Exception) { /* pasta temporária */ }
    }

    [Fact]
    public async Task Push_pendurado_e_cancelado_com_mensagem_util()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-timeout-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        var antes = GitService.TempoLimiteRede;
        GitService.TempoLimiteRede = TimeSpan.FromSeconds(3);

        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
            await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
            File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });

            // transporte que trava sem depender de rede: simula o git parado
            // esperando resposta (é o que acontece com a janela do credential manager)
            await GitService.RunAsync(dir, new[] { "remote", "add", "origin", "ext::sleep 120" });
            // o git bloqueia o transporte "ext" por padrão; liberado só neste repositório de teste
            await GitService.RunAsync(dir, new[] { "config", "protocol.ext.allow", "always" });

            var relogio = Stopwatch.StartNew();
            var erro = await Assert.ThrowsAsync<GitException>(() => GitService.PushAsync(dir, setUpstream: true));
            relogio.Stop();

            Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(20),
                $"deveria desistir perto do prazo de 3s; levou {relogio.Elapsed.TotalSeconds:F1}s. " +
                $"Mensagem: {erro.Message}");
            Assert.Contains("cancelada", erro.Message);
            Assert.Contains("Credential Manager", erro.Message);
        }
        finally
        {
            GitService.TempoLimiteRede = antes;
            Limpar(dir);
        }
    }

    [Fact]
    public async Task Operacao_normal_nao_e_afetada_pelo_prazo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-ok-" + Path.GetRandomFileName());
        var remoto = Path.Combine(Path.GetTempPath(), "grepos-bare-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(remoto);

        try
        {
            await GitService.RunAsync(remoto, new[] { "init", "-q", "--bare" });
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
            await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
            await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
            File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "inicial" });
            await GitService.RunAsync(dir, new[] { "remote", "add", "origin", remoto });

            await GitService.PushAsync(dir, setUpstream: true);

            var status = await GitService.StatusAsync(dir);
            Assert.Equal("origin/main", status.Upstream);
            Assert.Equal(0, status.Ahead);
        }
        finally
        {
            Limpar(dir);
            Limpar(remoto);
        }
    }
}
