using System;
using System.IO;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

public class LeiameTests
{
    private static string Pasta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-leiame-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Limpar(string dir)
    {
        try { Directory.Delete(dir, true); } catch (Exception) { /* temporária */ }
    }

    [Fact]
    public void Acha_o_README_e_le_o_conteudo()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "README.md"), "# Projeto\n\nDescrição.");

            Assert.NotNull(Leiame.Caminho(dir));
            Assert.Contains("# Projeto", Leiame.Ler(dir));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public void Repositorio_sem_README_devolve_vazio()
    {
        var dir = Pasta();
        try
        {
            Assert.Null(Leiame.Caminho(dir));
            Assert.Equal("", Leiame.Ler(dir));
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public void Pasta_inexistente_nao_quebra()
    {
        Assert.Null(Leiame.Caminho(@"Z:\nao\existe"));
        Assert.Equal("", Leiame.Ler(@"Z:\nao\existe"));
        Assert.Equal("", Leiame.Ler(""));
    }

    [Fact]
    public void README_gigante_e_cortado_com_aviso()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "README.md"), new string('x', 500));

            var texto = Leiame.Ler(dir, limiteDeCaracteres: 100);

            Assert.StartsWith(new string('x', 100), texto);
            Assert.Contains("cortado", texto);
            Assert.True(texto.Length < 300);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public void README_sem_extensao_tambem_serve()
    {
        var dir = Pasta();
        try
        {
            File.WriteAllText(Path.Combine(dir, "README"), "projeto antigo");
            Assert.Contains("projeto antigo", Leiame.Ler(dir));
        }
        finally { Limpar(dir); }
    }
}
