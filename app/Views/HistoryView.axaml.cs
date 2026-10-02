using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Área de transferência é da janela: por isso fica aqui, não no view model.</summary>
    private async void CopiarHash_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HistoryViewModel { SelectedCommit: { } row }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(row.Commit.Hash);
    }
}
