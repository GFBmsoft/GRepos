using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class ChangesView : UserControl
{
    public ChangesView()
    {
        InitializeComponent();

        // a seleção múltipla vai para o view model a cada mudança: é ela que decide se os
        // botões do cabeçalho valem para os marcados ou para a lista inteira
        foreach (var (nome, staged) in new[] { ("ListaStaged", true), ("ListaUnstaged", false) })
        {
            var lista = this.FindControl<ListBox>(nome)!;
            lista.SelectionChanged += (_, _) =>
            {
                if (DataContext is ChangesViewModel vm)
                    vm.DefinirSelecao(staged, lista.SelectedItems?.OfType<FileItemViewModel>()
                                              ?? Enumerable.Empty<FileItemViewModel>());
            };
        }

        // no túnel: com AcceptsReturn, a TextBox trataria o Enter como quebra de linha
        // antes de qualquer KeyBinding ter a chance de agir
        this.FindControl<TextBox>("Mensagem")!.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            e.Handled = true;
            if (DataContext is ChangesViewModel { CanCommit: true } vm) vm.CommitCommand.Execute(null);
        }, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
