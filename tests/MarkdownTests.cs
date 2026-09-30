using System.Linq;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Subconjunto de markdown que README e notas de release usam. O que não é suportado
/// precisa sair como texto, nunca quebrar a tela.
/// </summary>
public class MarkdownTests
{
    [Fact]
    public void Titulos_saem_com_o_nivel_certo()
    {
        var blocos = MarkdownParser.Blocos("# Um\n\n### Três\n\nTexto solto");

        Assert.Equal(BlocoMdTipo.Titulo, blocos[0].Tipo);
        Assert.Equal(1, blocos[0].Nivel);
        Assert.Equal("Um", blocos[0].Trechos.Single().Texto);

        Assert.Equal(3, blocos[1].Nivel);
        Assert.Equal(BlocoMdTipo.Paragrafo, blocos[2].Tipo);
    }

    [Fact]
    public void Sustenido_sem_espaco_nao_e_titulo()
    {
        // "#3 na fila" é texto, não cabeçalho
        var blocos = MarkdownParser.Blocos("#3 na fila");
        Assert.Equal(BlocoMdTipo.Paragrafo, blocos.Single().Tipo);
    }

    [Fact]
    public void Listas_simples_e_numeradas()
    {
        var blocos = MarkdownParser.Blocos("- um\n- dois\n\n1. primeiro\n2. segundo");

        Assert.Equal(4, blocos.Count);
        Assert.All(blocos, b => Assert.Equal(BlocoMdTipo.Item, b.Tipo));
        Assert.Equal("•", blocos[0].Marcador);
        Assert.Equal("1.", blocos[2].Marcador);
        Assert.Equal("segundo", blocos[3].Trechos.Single().Texto);
    }

    [Fact]
    public void Item_recuado_vira_subnivel()
    {
        var blocos = MarkdownParser.Blocos("- pai\n  - filho");

        Assert.Equal(0, blocos[0].Nivel);
        Assert.Equal(1, blocos[1].Nivel);
    }

    [Fact]
    public void Bloco_de_codigo_nao_interpreta_nada_dentro()
    {
        var blocos = MarkdownParser.Blocos("```csharp\nvar x = **isto**;\n# nao é título\n```");

        var codigo = Assert.Single(blocos);
        Assert.Equal(BlocoMdTipo.Codigo, codigo.Tipo);
        Assert.Contains("**isto**", codigo.Texto);
        Assert.Contains("# nao é título", codigo.Texto);
    }

    [Fact]
    public void Enfase_codigo_e_link_no_meio_da_frase()
    {
        var trechos = MarkdownParser.Trechos("Use **isto** ou *aquilo*, veja `git log` e o [manual](https://x.dev).");

        Assert.Contains(trechos, t => t.Texto == "isto" && t.Negrito);
        Assert.Contains(trechos, t => t.Texto == "aquilo" && t.Italico);
        Assert.Contains(trechos, t => t.Texto == "git log" && t.Codigo);

        var link = Assert.Single(trechos, t => t.Link.Length > 0);
        Assert.Equal("manual", link.Texto);
        Assert.Equal("https://x.dev", link.Link);

        // o texto em volta não pode se perder no caminho
        Assert.Contains(trechos, t => t.Texto.StartsWith("Use "));
    }

    [Fact]
    public void Asterisco_dentro_de_crases_nao_vira_negrito()
    {
        var trechos = MarkdownParser.Trechos("o glob `**/*.cs` pega tudo");

        var codigo = Assert.Single(trechos, t => t.Codigo);
        Assert.Equal("**/*.cs", codigo.Texto);
        Assert.DoesNotContain(trechos, t => t.Negrito);
    }

    [Fact]
    public void Marcacao_sem_fechamento_fica_como_texto()
    {
        var trechos = MarkdownParser.Trechos("2 * 3 e um **sem fim");
        Assert.All(trechos, t => Assert.False(t.Negrito || t.Italico || t.Codigo));
    }

    [Fact]
    public void Citacao_e_regua()
    {
        var blocos = MarkdownParser.Blocos("> um aviso\n\n---\n\nfim");

        Assert.Equal(BlocoMdTipo.Citacao, blocos[0].Tipo);
        Assert.Equal("um aviso", blocos[0].Trechos.Single().Texto);
        Assert.Equal(BlocoMdTipo.Regua, blocos[1].Tipo);
    }

    [Fact]
    public void Linhas_seguidas_viram_um_paragrafo_so()
    {
        var blocos = MarkdownParser.Blocos("primeira linha\nsegunda linha\n\noutro parágrafo");

        Assert.Equal(2, blocos.Count);
        Assert.Equal("primeira linha segunda linha", blocos[0].Trechos.Single().Texto);
    }

    [Fact]
    public void Tabela_vira_linhas_e_celulas()
    {
        var blocos = MarkdownParser.Blocos(
            "| Arquivo | Tamanho | Exige |\n" +
            "| --- | ---: | :---: |\n" +
            "| `GRepos.exe` | ~25 MB | [.NET 8](https://dot.net) |\n" +
            "| standalone | ~89 MB | nada |");

        var tabela = Assert.Single(blocos);
        Assert.Equal(BlocoMdTipo.Tabela, tabela.Tipo);
        Assert.Equal(3, tabela.Linhas.Count);

        Assert.True(tabela.Linhas[0].Cabecalho);
        Assert.False(tabela.Linhas[1].Cabecalho);
        Assert.Equal(3, tabela.Linhas[0].Celulas.Count);
        Assert.Equal("Tamanho", tabela.Linhas[0].Celulas[1].Single().Texto);

        // a formatação dentro da célula continua valendo
        Assert.True(tabela.Linhas[1].Celulas[0].Single().Codigo);
        Assert.Equal("https://dot.net", tabela.Linhas[1].Celulas[2].Single().Link);
    }

    [Fact]
    public void Linha_com_barra_sem_separador_nao_e_tabela()
    {
        // "a | b" solto no texto é parágrafo, não cabeçalho de tabela
        var blocos = MarkdownParser.Blocos("use a | b para escolher\n\noutra linha");
        Assert.All(blocos, b => Assert.Equal(BlocoMdTipo.Paragrafo, b.Tipo));
    }

    [Fact]
    public void Imagem_vira_apenas_o_texto_alternativo()
    {
        var trechos = MarkdownParser.Trechos("![build](https://x.dev/badge.svg) e texto");

        // o "!" solto no meio da frase era pior que não mostrar a imagem
        Assert.DoesNotContain(trechos, t => t.Texto.Contains('!'));
        Assert.Contains(trechos, t => t.Texto == "build" && t.Italico);
        Assert.Contains(trechos, t => t.Texto.Contains("e texto"));
    }

    [Fact]
    public void Selo_de_build_e_um_link_com_imagem_dentro()
    {
        // é a primeira linha do README deste projeto
        var trechos = MarkdownParser.Trechos(
            "[![build](https://github.com/x/y/badge.svg)](https://github.com/x/y/actions)");

        var link = Assert.Single(trechos, t => t.Link.Length > 0);
        Assert.Equal("build", link.Texto);
        Assert.Equal("https://github.com/x/y/actions", link.Link);

        // nada de marcação vazando para a tela
        Assert.All(trechos, t => Assert.DoesNotContain("](", t.Texto));
    }

    [Fact]
    public void Entrada_vazia_ou_nula_devolve_lista_vazia()
    {
        Assert.Empty(MarkdownParser.Blocos(""));
        Assert.Empty(MarkdownParser.Blocos("   \n\n  "));
        Assert.Empty(MarkdownParser.Trechos(""));
    }

    [Fact]
    public void Notas_de_release_de_verdade_sao_lidas_inteiras()
    {
        // é o formato que o workflow gera
        var notas = """
        ## Downloads

        - `GRepos-1.0.0.9.exe`: precisa do [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) instalado.
        - `GRepos-1.0.0.9-standalone.exe`: não precisa de nada instalado.

        ## Mudanças desde 1.0.0.8

        - Corrige o caminho do modo portátil
        - Ícone de procurar atualização no rodapé
        """;

        var blocos = MarkdownParser.Blocos(notas);

        Assert.Equal(2, blocos.Count(b => b.Tipo == BlocoMdTipo.Titulo));
        Assert.Equal(4, blocos.Count(b => b.Tipo == BlocoMdTipo.Item));
        Assert.Contains(blocos.SelectMany(b => b.Trechos), t => t.Link.Contains("dotnet.microsoft.com"));
    }
}
