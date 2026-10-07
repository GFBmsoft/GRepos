using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.Services;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();

        // arrastar o commit de uma branch sobre o de outra. O DataContext troca a cada
        // repositório, então é lido na hora, não guardado aqui
        if (this.FindControl<ListBox>("ListaDeCommits") is { } lista)
            ArrasteDeBranchNaTela.Habilitar(lista,
                dc => dc is CommitRowViewModel linha ? ArrasteDeBranch.DoCommit(linha.Commit.Refs) : null,
                () => DataContext is HistoryViewModel { TemGitHub: true },
                (opcao, origem, destino) => DataContext is HistoryViewModel vm
                    ? vm.ExecutarOpcaoAsync(opcao, origem, destino)
                    : Task.CompletedTask);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Área de transferência é da janela: por isso fica aqui, não no view model.</summary>
    private async void CopiarHash_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel { SelectedCommit: { } row }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(row.Commit.Hash);
    }

    /// <summary>A mensagem como foi escrita, com os sinais do Markdown.</summary>
    private async void CopiarTexto_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel { HasDetail: true } vm) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        await clipboard.SetTextAsync(vm.MensagemCompleta);
        vm.AvisarCopia("Mensagem copiada como texto.");
    }

    /// <summary>
    /// A mensagem renderizada. Vai em dois formatos de uma vez: HTML, que o Word, o
    /// Outlook e o Teams colam com título, lista e link; e texto limpo, para o Bloco de
    /// Notas e qualquer lugar que só aceite texto.
    /// </summary>
    private async void CopiarFormatado_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel { HasDetail: true } vm) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;

        var formatado = await CopiaFormatada.CopiarAsync(clipboard, vm.MensagemEmHtml, vm.MensagemEmTextoLimpo);
        vm.AvisarCopia(formatado ? "Mensagem copiada com formatação." : "Mensagem copiada como texto limpo.");
    }
}
