using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GRepos.Services;

namespace GRepos.Views;

/// <summary>
/// O gesto de arrastar uma branch e soltar sobre outra, igual na janela de branches e
/// no grafo do histórico. Quem usa diz como achar a branch de uma linha e o que fazer
/// com a opção escolhida; aqui ficam o arraste e o menu que abre ao soltar.
/// </summary>
public static class ArrasteDeBranchNaTela
{
    private static readonly DataFormat<string> Formato = DataFormat.CreateStringApplicationFormat("grepos-branch");

    /// <summary>Distância antes de o clique virar arraste: um clique tremido não arrasta.</summary>
    private const double Folga = 6;

    /// <param name="branchDe">A branch do item sob o ponteiro (pelo DataContext), ou null.</param>
    /// <param name="temGitHub">Se cabe oferecer pull request.</param>
    /// <param name="executar">Roda a opção escolhida no menu.</param>
    public static void Habilitar(
        Control raiz,
        Func<object?, RefDeBranch?> branchDe,
        Func<bool> temGitHub,
        Func<OpcaoDeArraste, RefDeBranch, RefDeBranch, Task> executar)
    {
        Point? inicio = null;
        RefDeBranch? origem = null;

        DragDrop.SetAllowDrop(raiz, true);

        raiz.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            inicio = null;
            origem = null;
            if (!e.GetCurrentPoint(raiz).Properties.IsLeftButtonPressed) return;

            // botão dentro da linha (Trocar) é clique, não começo de arraste
            if (e.Source is Visual v && v.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

            origem = BranchSob(e.Source, branchDe);
            if (origem is not null) inicio = e.GetPosition(raiz);
        }, RoutingStrategies.Tunnel);

        raiz.AddHandler(InputElement.PointerMovedEvent, async (_, e) =>
        {
            if (inicio is not { } p || origem is not { } arrastada) return;

            var agora = e.GetPosition(raiz);
            if (Math.Abs(agora.X - p.X) < Folga && Math.Abs(agora.Y - p.Y) < Folga) return;

            inicio = null;
            var dados = new DataTransfer();
            dados.Add(DataTransferItem.Create(Formato, arrastada.Nome));
            await DragDrop.DoDragDropAsync(e, dados, DragDropEffects.Link);
        }, RoutingStrategies.Tunnel);

        raiz.AddHandler(InputElement.PointerReleasedEvent, (_, _) => inicio = null, RoutingStrategies.Tunnel);

        raiz.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var alvo = BranchSob(e.Source, branchDe);
            e.DragEffects = origem is not null && alvo is not null && alvo.Nome != origem.Nome &&
                            e.DataTransfer.Contains(Formato)
                ? DragDropEffects.Link
                : DragDropEffects.None;
        });

        raiz.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var arrastada = origem;
            origem = null;
            if (arrastada is null || !e.DataTransfer.Contains(Formato)) return;
            if (BranchSob(e.Source, branchDe) is not { } alvo || alvo.Nome == arrastada.Nome) return;

            var opcoes = ArrasteDeBranch.Opcoes(arrastada, alvo, temGitHub());
            if (opcoes.Count == 0 || e.Source is not Control onde) return;

            Menu(opcoes, arrastada, alvo, executar).ShowAt(onde, showAtPointer: true);
        });
    }

    /// <summary>O menu de soltar; a opção indisponível aparece apagada, com o motivo na dica.</summary>
    public static MenuFlyout Menu(
        IReadOnlyList<OpcaoDeArraste> opcoes, RefDeBranch origem, RefDeBranch destino,
        Func<OpcaoDeArraste, RefDeBranch, RefDeBranch, Task> executar)
    {
        var menu = new MenuFlyout();
        foreach (var opcao in opcoes)
        {
            var item = new MenuItem { Header = opcao.Rotulo, IsEnabled = opcao.Disponivel };
            if (!opcao.Disponivel)
            {
                item.Header = $"{opcao.Rotulo} — {opcao.Motivo}";
            }
            else
            {
                var escolhida = opcao;
                item.Click += async (_, _) => await executar(escolhida, origem, destino);
            }
            menu.Items.Add(item);
        }
        return menu;
    }

    private static RefDeBranch? BranchSob(object? fonte, Func<object?, RefDeBranch?> branchDe)
    {
        // sobe da ponta clicada até a linha: o DataContext muda quando chega no item
        for (var v = fonte as Visual; v is not null; v = v.GetVisualParent())
            if (v is StyledElement { DataContext: { } dc } && branchDe(dc) is { } b)
                return b;
        return null;
    }
}
