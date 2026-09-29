using System;
using System.Linq;
using GRepos.Models;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

public class RefChipTests
{
    private static CommitRowViewModel Linha(params string[] refs) => new()
    {
        Commit = new Commit { Hash = "abc1234", Subject = "assunto", Refs = refs.ToList() },
    };

    [Fact]
    public void Tag_recebe_o_laranja_de_destaque()
    {
        var chip = Linha("tag: 1.0.0.27").Refs.Single();
        Assert.Equal("1.0.0.27", chip.Text);
        Assert.Equal("Orange", chip.Color);
    }

    [Fact]
    public void Cores_seguem_a_relevancia_da_ref()
    {
        Assert.Equal("Accent", CommitRowViewModel.RefColor("HEAD -> main"));
        Assert.Equal("Orange", CommitRowViewModel.RefColor("tag: v2"));
        Assert.Equal("TextDim", CommitRowViewModel.RefColor("origin/main"));
    }

    [Fact]
    public void Lista_mostra_so_a_ref_principal_e_conta_o_resto()
    {
        // a lista precisa ficar enxuta: o painel lateral tem as refs completas
        var chips = Linha("origin/main", "tag: v2", "HEAD -> main").Refs;

        Assert.Equal(2, chips.Count);
        Assert.Equal("HEAD -> main", chips[0].Text);
        Assert.Equal("+2", chips[1].Text);
        Assert.Contains("v2", chips[1].Tooltip);
        Assert.Contains("origin/main", chips[1].Tooltip);
    }

    [Fact]
    public void Tag_vence_o_remoto_quando_nao_ha_head()
    {
        var chips = Linha("origin/main", "tag: 1.0.0.27").Refs;
        Assert.Equal("1.0.0.27", chips[0].Text);
        Assert.Equal("Orange", chips[0].Color);
    }

    [Fact]
    public void Sem_refs_nao_gera_chip()
    {
        Assert.Empty(Linha().Refs);
    }
}

public class ShortDateTests
{
    private static CommitRowViewModel Em(DateTimeOffset quando) => new()
    {
        Commit = new Commit { Hash = "abc", Subject = "s", Date = quando.ToString("o") },
    };

    [Fact]
    public void Minutos_horas_e_ontem_aparecem_em_forma_curta()
    {
        var agora = DateTimeOffset.Now;
        Assert.Equal("agora", Em(agora).ShortDate);
        Assert.Equal("20 min", Em(agora.AddMinutes(-20)).ShortDate);
        Assert.Equal("ontem", Em(agora.Date.AddDays(-1).AddHours(10)).ShortDate);
    }

    [Fact]
    public void Datas_antigas_usam_dia_e_mes()
    {
        var antiga = DateTimeOffset.Now.AddYears(-2);
        var texto = Em(antiga).ShortDate;
        Assert.Matches(@"^\d{2}/\d{2}/\d{2}$", texto); // inclui o ano quando não é o corrente
    }

    [Fact]
    public void Data_ilegivel_nao_quebra_a_lista()
    {
        var linha = new CommitRowViewModel
        {
            Commit = new Commit { Hash = "abc", Subject = "s", Date = "nao e data" },
        };
        Assert.Equal("", linha.ShortDate);
    }
}
