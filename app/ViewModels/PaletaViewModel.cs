using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GRepos.Services;

namespace GRepos.ViewModels;

/// <summary>
/// Paleta de comandos (Ctrl+P): digita-se um pedaço do nome e o Enter abre o
/// repositório, troca de branch ou roda a ação. A janela só mostra; quem sabe o que
/// existe é quem montou os itens.
/// </summary>
public sealed partial class PaletaViewModel : ObservableObject
{
    private readonly List<ItemDaPaleta> _itens;

    public PaletaViewModel(IEnumerable<ItemDaPaleta> itens)
    {
        _itens = itens.ToList();
        Filtrar();
    }

    [ObservableProperty] private string _consulta = "";
    [ObservableProperty] private ObservableCollection<ItemDaPaleta> _resultados = new();
    [ObservableProperty] private ItemDaPaleta? _selecionado;

    public bool Vazio => Resultados.Count == 0;

    partial void OnConsultaChanged(string value) => Filtrar();

    /// <summary>Itens que chegam depois de a janela abrir (as branches, lidas do git).</summary>
    public void Acrescentar(IEnumerable<ItemDaPaleta> itens)
    {
        var escolhido = Selecionado;
        _itens.AddRange(itens);
        Filtrar();

        // quem já estava com a seta em cima de um item não perde o lugar
        if (escolhido is not null && Resultados.Contains(escolhido)) Selecionado = escolhido;
    }

    private void Filtrar()
    {
        Resultados = new ObservableCollection<ItemDaPaleta>(Paleta.Filtrar(_itens, Consulta));
        Selecionado = Resultados.FirstOrDefault();
        OnPropertyChanged(nameof(Vazio));
    }

    /// <summary>Seta para cima e para baixo, dando a volta nas pontas.</summary>
    public void Mover(int passo)
    {
        if (Resultados.Count == 0) return;

        var atual = Selecionado is null ? -1 : Resultados.IndexOf(Selecionado);
        var novo = (atual + passo) % Resultados.Count;
        if (novo < 0) novo += Resultados.Count;
        Selecionado = Resultados[novo];
    }

    /// <summary>O item que o Enter executa. A janela fecha antes de rodá-lo.</summary>
    public ItemDaPaleta? Escolhido { get; private set; }

    public bool Escolher()
    {
        Escolhido = Selecionado;
        return Escolhido is not null;
    }
}
