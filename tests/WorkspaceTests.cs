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
}
