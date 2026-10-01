using System.Collections.Generic;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class GitBashTests
{
    private const string Raiz = @"C:\Program Files\Git";
    private static readonly string GitBashExe = Raiz + @"\git-bash.exe";

    private static System.Func<string, bool> Existem(params string[] arquivos)
    {
        var set = new HashSet<string>(arquivos, System.StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Theory]
    [InlineData(@"C:\Program Files\Git")]
    [InlineData(@"C:\Program Files\Git\")]
    [InlineData(@"""C:\Program Files\Git""")]
    [InlineData(@"C:\Program Files\Git\cmd")]
    [InlineData(@"C:\Program Files\Git\mingw64\bin")]
    public void PastaConfiguradaAchaOGitBashSubindoNiveis(string configurado)
    {
        Assert.Equal(GitBashExe, GitBash.Localizar(configurado, Existem(GitBashExe), path: ""));
    }

    [Fact]
    public void ExecutavelConfiguradoValeSeExistir()
    {
        Assert.Equal(GitBashExe, GitBash.Localizar(GitBashExe, Existem(GitBashExe), path: ""));
        Assert.Null(GitBash.Localizar(@"D:\nada\git-bash.exe", Existem(GitBashExe), path: ""));
    }

    [Fact]
    public void CaminhoConfiguradoErradoNaoCaiNaBuscaAutomatica()
    {
        // o PATH tem o Git, mas o usuário apontou para outro lugar: avisar, não disfarçar
        Assert.Null(GitBash.Localizar(@"D:\Ferramentas", Existem(GitBashExe), path: Raiz + @"\cmd"));
    }

    [Fact]
    public void VazioProcuraNoPath()
    {
        var path = @"C:\Windows\system32;C:\Program Files\Git\cmd;C:\outros";
        Assert.Equal(GitBashExe, GitBash.Localizar("", Existem(GitBashExe), path));
    }

    [Fact]
    public void SemGitBashUsaOBashDaPastaBin()
    {
        var bash = @"D:\PortableGit\bin\bash.exe";
        Assert.Equal(bash, GitBash.Localizar(@"D:\PortableGit", Existem(bash), path: ""));
    }

    [Fact]
    public void EmbutidoUsaOBashAoLadoDoGitBash()
    {
        var bash = Raiz + @"\bin\bash.exe";
        Assert.Equal(bash, GitBash.LocalizarBash(Raiz, Existem(GitBashExe, bash), path: ""));
        Assert.Null(GitBash.LocalizarBash(Raiz, Existem(GitBashExe), path: ""));
    }

    [Fact]
    public void PastaSemNadaDevolveNull()
    {
        Assert.Null(GitBash.Localizar(@"D:\vazio", Existem(), path: ""));
    }
}
