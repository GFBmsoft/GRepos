using GRepos.Models;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// Botão que liga sem ter o que fazer: o Enviar com a branch em dia, o Salvar de um
/// comentário vazio. Cada regra de "ligado" fica aqui, sem tela.
/// </summary>
public class BotoesDesligadosTests
{
    [Theory]
    [InlineData("main", "origin/main", 2, true)]   // commits à frente
    [InlineData("main", "origin/main", 0, false)]  // em dia com o remoto
    [InlineData("feat/x", null, 0, true)]          // branch nova: o primeiro envio a cria
    [InlineData("feat/x", "", 0, true)]
    [InlineData("(detached)", null, 3, false)]     // HEAD solto: não há branch para onde enviar
    [InlineData("", null, 1, false)]               // status ainda não lido
    public void Enviar_so_com_o_que_enviar(string branch, string? upstream, int frente, bool esperado)
    {
        var status = new RepoStatus { Branch = branch, Upstream = upstream, Ahead = frente };
        Assert.Equal(esperado, status.TemOQueEnviar);
    }

    /// <summary>
    /// Par apagado no remoto: 0 à frente e 0 atrás, mas não está em dia, não há com quem
    /// comparar. O Enviar liga, para criar a branch lá de novo.
    /// </summary>
    [Fact]
    public void Par_apagado_no_remoto_tem_o_que_enviar()
    {
        var status = new RepoStatus { Branch = "feat/x", Upstream = "origin/feat/x", ParAusente = true };

        Assert.False(status.TemPar);
        Assert.True(status.TemOQueEnviar);
        Assert.True(new RepoStatus { Branch = "feat/x", Upstream = "origin/feat/x" }.TemPar);
    }

    [Fact]
    public void Repositorio_com_erro_nao_tem_o_que_enviar()
    {
        var status = new RepoStatus { Branch = "main", Ahead = 1, Error = "pasta não encontrada" };
        Assert.False(status.TemOQueEnviar);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   \n ", false)]
    [InlineData("corrigido no #42", true)]
    public void Comentario_vazio_nao_se_salva(string rascunho, bool esperado)
    {
        var item = new ComentarioViewModel { Editando = true, Rascunho = rascunho };
        Assert.Equal(esperado, item.PodeSalvar);
    }

    [Fact]
    public void Salvar_do_comentario_acompanha_o_rascunho()
    {
        var item = new ComentarioViewModel { Editando = true };
        var avisos = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ComentarioViewModel.PodeSalvar)) avisos++; };

        item.Rascunho = "texto";

        Assert.True(item.PodeSalvar);
        Assert.Equal(1, avisos);
    }
}
