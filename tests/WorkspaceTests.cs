using System.IO;
using System.Linq;
using GRepos.Models;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class WorkspaceStoreTests
{
    private const string Json = """
    {
      "groups": [ { "id": "g1", "name": "Módulos", "color": "#4F8CFF", "collapsed": false } ],
      "repos": [
        { "id": "r1", "name": "Financeiro (DBISAM)", "path": "C:\\tmp\\a", "groupId": "g1", "pairKey": "Financeiro", "role": "origem" },
        { "id": "r2", "name": "Financeiro (MySQL)", "path": "C:\\tmp\\b", "groupId": "g1", "pairKey": "Financeiro", "role": "destino" }
      ],
      "settings": { "accent": "#4F8CFF", "theme": "dark", "density": "compacta", "autoRefreshSeconds": 60, "logLimit": 300, "splitDiff": true }
    }
    """;

    [Fact]
    public void Le_o_workspace_gravado_em_disco()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-test-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "workspace.json");
        File.WriteAllText(file, Json);

        var ws = WorkspaceStore.LoadFrom(file);

        Assert.Single(ws.Groups);
        Assert.Equal(2, ws.Repos.Count);
        Assert.Equal("Financeiro", ws.Repos[0].PairKey);
        Assert.Equal("destino", ws.Repos[1].Role);
        Assert.Equal(300, ws.Settings.LogLimit);

        Directory.Delete(dir, true);
    }

    [Fact]
    public void Grava_e_le_de_volta_sem_perder_pares()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-test-" + Path.GetRandomFileName());
        var file = Path.Combine(dir, "workspace.json");

        var ws = new Workspace();
        ws.Groups.Add(new Group { Id = "g1", Name = "Módulos" });
        ws.Repos.Add(new Repo { Id = "r1", Name = "A", Path = @"C:\tmp\a", GroupId = "g1", PairKey = "Fin", Role = "origem" });
        WorkspaceStore.SaveTo(file, ws);

        var back = WorkspaceStore.LoadFrom(file);

        Assert.Equal("Fin", back.Repos.Single().PairKey);
        Assert.Equal("g1", back.Repos.Single().GroupId);
        Assert.Equal("Módulos", back.Groups.Single().Name);

        Directory.Delete(dir, true);
    }

    /// <summary>
    /// GREPOS_HOME manda no local do workspace. Isso é o que segura os testes longe do
    /// arquivo real do usuário, então precisa valer inclusive sobre o modo portátil.
    /// </summary>
    [Fact]
    public void GREPOS_HOME_vence_o_modo_portatil()
    {
        var antes = System.Environment.GetEnvironmentVariable("GREPOS_HOME");
        var dir = Path.Combine(Path.GetTempPath(), "grepos-home-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", dir);

            Assert.Equal(dir, WorkspaceStore.Directory);
            Assert.False(WorkspaceStore.Portatil);
            Assert.Equal(Path.Combine(dir, WorkspaceStore.NomeDoArquivo), WorkspaceStore.FilePath);

            // com a variável definida, escolher o local pela tela seria ambíguo
            Assert.Throws<System.InvalidOperationException>(() => WorkspaceStore.MoverPara(true));
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(dir, true); } catch (System.Exception) { /* temporária */ }
        }
    }

    /// <summary>
    /// Ida e volta do modo portátil. O caminho mostrado na tela sai de FilePath, e é
    /// ele que precisa acompanhar a mudança nos dois sentidos — o defeito era a tela
    /// exibir o que a ação devolveu, em vez de reler onde o arquivo está de fato.
    /// </summary>
    [Fact]
    public void Ligar_e_desligar_o_portatil_leva_o_arquivo_junto()
    {
        var antes = System.Environment.GetEnvironmentVariable("GREPOS_HOME");
        System.Environment.SetEnvironmentVariable("GREPOS_HOME", null);

        // pastas de mentira nos dois lados: o workspace real do usuário não entra nisso
        var perfil = Path.Combine(Path.GetTempPath(), "grepos-perfil-" + Path.GetRandomFileName());
        var pastaApp = Path.Combine(Path.GetTempPath(), "grepos-app-" + Path.GetRandomFileName());
        Directory.CreateDirectory(perfil);
        Directory.CreateDirectory(pastaApp);

        // guarda os desvios globais em vez de zerá-los depois: zerar devolveria os
        // testes ao %APPDATA% real, que é justamente o que não pode acontecer
        var perfilAntes = WorkspaceStore.PastaPadraoDeTeste;
        var appAntes = WorkspaceStore.PastaDoAppDeTeste;

        WorkspaceStore.PastaPadraoDeTeste = perfil;
        WorkspaceStore.PastaDoAppDeTeste = pastaApp;

        var doApp = Path.Combine(pastaApp, WorkspaceStore.NomeDoArquivo);

        try
        {
            var ws = new Workspace { Groups = { new Group { Id = "g1", Name = "Marcador" } } };
            WorkspaceStore.Save(ws);
            Assert.False(WorkspaceStore.Portatil);
            Assert.Equal(Path.Combine(perfil, WorkspaceStore.NomeDoArquivo), WorkspaceStore.FilePath);

            WorkspaceStore.MoverPara(true);
            Assert.True(WorkspaceStore.Portatil);
            Assert.Equal(doApp, WorkspaceStore.FilePath);
            Assert.True(File.Exists(doApp));
            Assert.False(File.Exists(Path.Combine(perfil, WorkspaceStore.NomeDoArquivo)));

            // o conteúdo foi junto: mover não pode significar recomeçar do zero
            Assert.Equal("Marcador", WorkspaceStore.Load().Groups.Single().Name);

            WorkspaceStore.MoverPara(false);
            Assert.False(WorkspaceStore.Portatil);
            Assert.Equal(Path.Combine(perfil, WorkspaceStore.NomeDoArquivo), WorkspaceStore.FilePath);
            Assert.False(File.Exists(doApp));
            Assert.Equal("Marcador", WorkspaceStore.Load().Groups.Single().Name);
        }
        finally
        {
            WorkspaceStore.PastaPadraoDeTeste = perfilAntes;
            WorkspaceStore.PastaDoAppDeTeste = appAntes;
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
            try { Directory.Delete(perfil, true); } catch (System.Exception) { /* temporária */ }
            try { Directory.Delete(pastaApp, true); } catch (System.Exception) { /* temporária */ }
        }
    }

    /// <summary>
    /// A trava que faltava. Um teste apagou o workspace real do usuário porque o xunit
    /// roda classes em paralelo e GREPOS_HOME é global: uma classe zerou a variável
    /// enquanto outra gravava. Agora o caminho real não é alcançável de dentro dos
    /// testes, e este teste existe para que ninguém desfaça isso sem perceber.
    /// </summary>
    [Fact]
    public void Nenhum_teste_consegue_escrever_no_workspace_real()
    {
        var real = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "GRepos");

        Assert.NotNull(WorkspaceStore.PastaPadraoDeTeste);
        Assert.NotEqual(real, WorkspaceStore.PastaPadrao);
        Assert.NotEqual(real, WorkspaceStore.PastaDoApp);

        // com ou sem GREPOS_HOME, o destino continua fora do perfil do usuário
        var antes = System.Environment.GetEnvironmentVariable("GREPOS_HOME");
        try
        {
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", null);
            Assert.DoesNotContain(real, WorkspaceStore.FilePath);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
        }
    }

    [Fact]
    public void Sem_arquivo_ao_lado_do_executavel_o_workspace_fica_no_perfil()
    {
        var antes = System.Environment.GetEnvironmentVariable("GREPOS_HOME");
        try
        {
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", null);

            // o executável dos testes não tem workspace.json ao lado
            Assert.False(WorkspaceStore.Portatil);
            Assert.Equal(WorkspaceStore.PastaPadrao, WorkspaceStore.Directory);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("GREPOS_HOME", antes);
        }
    }
}
