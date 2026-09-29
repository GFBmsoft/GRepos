using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using GRepos.Services;
using Xunit;

namespace GRepos.Tests;

/// <summary>
/// A lista de arquivos é atualizada no lugar. Trocar a coleção inteira faz a tela
/// recriar tudo — a piscada a cada stage vinha daí.
/// </summary>
public class ListaSyncTests
{
    private sealed record Item(string Nome, string Estado);

    private static ObservableCollection<Item> Colecao(params string[] nomes) =>
        new(nomes.Select(n => new Item(n, "M")));

    private static void Sincronizar(ObservableCollection<Item> destino, params Item[] novos) =>
        ListaSync.Aplicar(destino, novos, i => i.Nome, (a, b) => a == b);

    private static int ContarEventos(ObservableCollection<Item> col, System.Action acao)
    {
        var eventos = 0;
        void Handler(object? s, NotifyCollectionChangedEventArgs e) => eventos++;
        col.CollectionChanged += Handler;
        acao();
        col.CollectionChanged -= Handler;
        return eventos;
    }

    [Fact]
    public void Lista_identica_nao_gera_nenhum_evento()
    {
        var col = Colecao("a.txt", "b.txt");
        var eventos = ContarEventos(col, () => Sincronizar(col, new Item("a.txt", "M"), new Item("b.txt", "M")));

        Assert.Equal(0, eventos); // nada mudou: a tela não pode mexer
        Assert.Equal(new[] { "a.txt", "b.txt" }, col.Select(i => i.Nome));
    }

    [Fact]
    public void Remover_um_item_mexe_so_nele()
    {
        var col = Colecao("a.txt", "b.txt", "c.txt");
        var eventos = ContarEventos(col, () => Sincronizar(col, new Item("a.txt", "M"), new Item("c.txt", "M")));

        Assert.Equal(1, eventos);
        Assert.Equal(new[] { "a.txt", "c.txt" }, col.Select(i => i.Nome));
    }

    [Fact]
    public void Preparar_arquivo_tira_da_lista_sem_recriar_o_resto()
    {
        // é o caso do botão "+": o arquivo sai de alterações locais
        var col = Colecao("Unit1.pas", "Unit2.pas", "Unit3.pas");
        var restantes = col.Where(i => i.Nome != "Unit2.pas").ToArray();
        var primeiro = col[0];

        Sincronizar(col, restantes);

        Assert.Equal(2, col.Count);
        Assert.Same(primeiro, col[0]); // a instância continua a mesma: sem recriar item
    }

    [Fact]
    public void Item_novo_entra_na_posicao_certa()
    {
        var col = Colecao("a.txt", "c.txt");
        Sincronizar(col, new Item("a.txt", "M"), new Item("b.txt", "M"), new Item("c.txt", "M"));

        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, col.Select(i => i.Nome));
    }

    [Fact]
    public void Mudanca_de_estado_substitui_apenas_aquele_item()
    {
        var col = Colecao("a.txt", "b.txt");
        var b = col[1];

        Sincronizar(col, new Item("a.txt", "M"), new Item("b.txt", "D")); // b virou apagado

        Assert.Equal("D", col[1].Estado);
        Assert.NotSame(b, col[1]);
        Assert.Equal("a.txt", col[0].Nome); // o outro ficou intacto
    }

    [Fact]
    public void Reordenacao_usa_move_em_vez_de_recriar()
    {
        var col = Colecao("b.txt", "a.txt");
        var a = col[1];

        Sincronizar(col, new Item("a.txt", "M"), new Item("b.txt", "M"));

        Assert.Equal(new[] { "a.txt", "b.txt" }, col.Select(i => i.Nome));
        Assert.Same(a, col[0]);
    }

    [Fact]
    public void Lista_esvaziada_fica_vazia()
    {
        var col = Colecao("a.txt", "b.txt");
        Sincronizar(col);
        Assert.Empty(col);
    }

    [Fact]
    public void Lista_vazia_recebe_todos()
    {
        var col = new ObservableCollection<Item>();
        Sincronizar(col, new Item("a.txt", "M"), new Item("b.txt", "M"));
        Assert.Equal(2, col.Count);
    }
}
