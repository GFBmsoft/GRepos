using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GRepos.ViewModels;

namespace GRepos.Views;

/// <summary>
/// Arrastar e soltar na árvore da sidebar: um repositório para outro grupo, um grupo
/// para dentro de outro. Quem decide o que cabe e faz a mudança é o
/// <see cref="MainViewModel"/>; aqui ficam o gesto e o destaque do destino.
///
/// O clique passa a valer ao soltar o botão, não ao apertar: a lista selecionava no
/// aperto, e começar a arrastar um grupo já o recolhia e abria o painel dele.
/// </summary>
public static class ArrasteNaArvore
{
    private static readonly DataFormat<string> Formato = DataFormat.CreateStringApplicationFormat("grepos-no-da-arvore");

    /// <summary>Distância antes de o clique virar arraste: um clique tremido não arrasta.</summary>
    private const double Folga = 6;

    public static void Habilitar(ListBox arvore, MainViewModel vm)
    {
        Point? inicio = null;
        SidebarNode? origem = null;
        SidebarNode? destacado = null;
        var arrastando = false;

        DragDrop.SetAllowDrop(arvore, true);

        void Destacar(SidebarNode? no)
        {
            if (ReferenceEquals(destacado, no)) return;
            if (destacado is not null) destacado.AlvoDeSoltar = false;
            destacado = no;
            if (destacado is not null) destacado.AlvoDeSoltar = true;
        }

        arvore.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            inicio = null;
            origem = null;
            if (!e.GetCurrentPoint(arvore).Properties.IsLeftButtonPressed) return;

            // a pílula da branch é um botão dentro da linha: é clique dela, não da linha
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

            if (NoSob(e.Source) is not { } no) return;

            origem = no;
            inicio = e.GetPosition(arvore);

            // segura a seleção até soltar: se virar arraste, o item não chega a ser "clicado"
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        arvore.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (arrastando || inicio is not { } p || origem is not (RepoNode or GroupNode { Id.Length: > 0 })) return;

            var agora = e.GetPosition(arvore);
            if (Math.Abs(agora.X - p.X) < Folga && Math.Abs(agora.Y - p.Y) < Folga) return;

            inicio = null;
            arrastando = true;
            try
            {
                var dados = new DataTransfer();
                dados.Add(DataTransferItem.Create(Formato, "no"));
                await DragDrop.DoDragDropAsync(e, dados, DragDropEffects.Move);
            }
            finally
            {
                arrastando = false;
                origem = null;
                Destacar(null);
            }
        }, RoutingStrategies.Tunnel);

        arvore.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            var clicado = inicio is not null ? origem : null;
            inicio = null;
            if (arrastando || clicado is null) return;
            origem = null;

            // soltou sem arrastar, em cima do mesmo item: é o clique de sempre
            if (!ReferenceEquals(NoSob(e.Source), clicado)) return;
            arvore.Focus();
            arvore.SelectedItem = clicado;
        }, RoutingStrategies.Tunnel);

        arvore.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var alvo = e.DataTransfer.Contains(Formato) ? NoSob(e.Source) : null;
            var cabe = vm.PodeSoltar(origem, alvo, out var destino);

            // o destaque vai no grupo que recebe, mesmo com o ponteiro sobre um repositório dele
            Destacar(cabe ? Recebedor(vm, alvo, destino) : null);
            e.DragEffects = cabe ? DragDropEffects.Move : DragDropEffects.None;
        });

        arvore.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Destacar(null));

        arvore.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var arrastado = origem;
            var alvo = NoSob(e.Source);
            Destacar(null);
            if (e.DataTransfer.Contains(Formato)) vm.Soltar(arrastado, alvo);
        });
    }

    /// <summary>O nó que fica destacado: o grupo de destino, ou o Painel quando é o nível principal.</summary>
    private static SidebarNode? Recebedor(MainViewModel vm, SidebarNode? alvo, string? destino)
    {
        if (alvo is GroupNode or PainelNode) return alvo;
        foreach (var no in vm.Tree)
            if (no is GroupNode g && g.Id == (destino ?? "")) return g;
        return null;
    }

    private static SidebarNode? NoSob(object? fonte)
    {
        // sobe da ponta clicada até a linha: o DataContext vira o nó quando chega no item
        for (var v = fonte as Visual; v is not null; v = v.GetVisualParent())
            if (v is StyledElement { DataContext: SidebarNode no }) return no;
        return null;
    }
}
