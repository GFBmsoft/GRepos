using System;
using System.IO;
using System.Threading.Tasks;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// O app pode ser aberto por um terminal ou agente que desliga os prompts do git.
/// Se essas variáveis vazarem para o processo do git, push e pull quebram com
/// "terminal prompts disabled" e o credential manager nunca é chamado.
/// </summary>
[Collection(WorkspaceGlobal.Nome)]
public class CredentialEnvTests
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
    public async Task Variaveis_que_desligam_o_prompt_nao_sao_herdadas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-env-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        var antesPrompt = Environment.GetEnvironmentVariable("GIT_TERMINAL_PROMPT");
        var antesGcm = Environment.GetEnvironmentVariable("GCM_INTERACTIVE");

        try
        {
            await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });

            // simula o app aberto por um terminal com prompts desligados
            Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0");
            Environment.SetEnvironmentVariable("GCM_INTERACTIVE", "never");

            // o git filho precisa enxergar as variáveis limpas
            var saida = await GitService.RunAsync(dir, new[] { "var", "GIT_EDITOR" });
            Assert.False(string.IsNullOrWhiteSpace(saida));

            var prompt = await GitService.RunAsync(dir, new[] { "config", "--get", "credential.interactive" })
                .ContinueWith(t => t.IsFaulted ? "" : t.Result);
            Assert.True(string.IsNullOrWhiteSpace(prompt));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", antesPrompt);
            Environment.SetEnvironmentVariable("GCM_INTERACTIVE", antesGcm);
            Limpar(dir);
        }
    }

    [Fact]
    public async Task Quem_pede_explicitamente_continua_sem_prompt()
    {
        // a consulta de token precisa falhar em silêncio, nunca abrir janela
        var saida = await GitService.RunWithEnvAsync(
            Path.GetTempPath(),
            new[] { "var", "GIT_EDITOR" },
            null,
            ("GIT_TERMINAL_PROMPT", "0"), ("GCM_INTERACTIVE", "never"));

        Assert.False(string.IsNullOrWhiteSpace(saida));
    }

    [Fact]
    public void Usuario_configurado_entra_como_parametro_do_credential_manager()
    {
        var antes = GitService.CredentialUser;
        try
        {
            GitService.CredentialUser = "GFBmsoft";
            var args = Chamar("push");

            Assert.Equal("-c", args[0]);
            Assert.Equal("credential.https://github.com.username=GFBmsoft", args[1]);
            Assert.Equal("push", args[2]);
        }
        finally
        {
            GitService.CredentialUser = antes;
        }
    }

    [Fact]
    public void Sem_usuario_configurado_os_argumentos_ficam_intactos()
    {
        var antes = GitService.CredentialUser;
        try
        {
            GitService.CredentialUser = "";
            Assert.Equal(new[] { "push" }, Chamar("push"));
        }
        finally
        {
            GitService.CredentialUser = antes;
        }
    }

    [Fact]
    public void Repositorio_com_conta_propria_usa_ela_e_os_outros_a_principal()
    {
        var antes = GitService.CredentialUser;
        var outro = Path.Combine(Path.GetTempPath(), "grepos-conta-" + Path.GetRandomFileName());
        try
        {
            GitService.CredentialUser = "GFBmsoft";
            GitService.DefinirConta(outro, "bmsoftsistemas");

            Assert.Equal("credential.https://github.com.username=bmsoftsistemas", ChamarEm(outro, "push")[1]);
            Assert.Equal("credential.https://github.com.username=GFBmsoft", Chamar("push")[1]);

            // a mesma pasta escrita de outro jeito continua sendo o mesmo repositório
            Assert.Equal("bmsoftsistemas", GitService.ContaDe(outro + Path.DirectorySeparatorChar));

            // voltar para a automática devolve a principal
            GitService.DefinirConta(outro, null);
            Assert.Equal("GFBmsoft", GitService.ContaDe(outro));
        }
        finally
        {
            GitService.DefinirConta(outro, null);
            GitService.CredentialUser = antes;
        }
    }

    private static string[] Chamar(params string[] args) => ChamarEm(Path.GetTempPath(), args);

    private static string[] ChamarEm(string repo, params string[] args) =>
        (string[])typeof(GitService)
            .GetMethod("ComCredencial", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { repo, args })!;
}
