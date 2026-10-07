using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;

namespace GRepos.Views;

public partial class MainWindow : Window, IDialogService
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(this);
        DataContext = _vm;

        // arrastar um repositório para outro grupo, ou um grupo para dentro de outro. Fica
        // aqui, e não com o resto da árvore, porque precisa do view model já criado
        if (this.FindControl<ListBox>("TreeList") is { } arvore) ArrasteNaArvore.Habilitar(arvore, _vm);

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.TerminalVisivel)) AjustarTerminal();
        };
        Closed += (_, _) => _vm.EncerrarTerminais();

        Opened += async (_, _) =>
        {
            try
            {
                await _vm.InitAsync();
                ApplyTheme(_vm.Settings);
                AplicarLarguraSidebar();
            }
            catch (System.Exception ex)
            {
                // falha na carga não pode deixar a janela em branco sem explicação
                _vm.Notify($"Falha ao carregar o workspace: {ex.Message}", true);
            }
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        var divisor = this.FindControl<GridSplitter>("Divisor");
        if (divisor is not null) divisor.DragCompleted += (_, _) => GuardarLarguraSidebar();

        // Tunnel: o atalho precisa chegar antes do controle com o foco. Pelo caminho
        // normal (bubble) um TextBox no meio da tela poderia engolir a tecla.
        AddHandler(KeyDownEvent, AtalhoDeFiltro, RoutingStrategies.Tunnel);

        // clique direito num grupo da árvore: subgrupo, editar e lote, sem ir às Preferências
        if (this.FindControl<ListBox>("TreeList") is { } arvore) arvore.ContextRequested += MenuDoGrupo;
    }

    /// <summary>Ctrl+F leva o foco ao filtro; Esc, estando nele, limpa e devolve a lista.</summary>
    private void AtalhoDeFiltro(object? sender, KeyEventArgs e)
    {
        var caixa = this.FindControl<TextBox>("CaixaFiltro");
        if (caixa is null) return;

        // no terminal o Ctrl+F é do bash (avança o cursor), não do filtro
        if ((e.Source as Visual)?.FindAncestorOfType<AvaloniaTerminal.TerminalControl>(includeSelf: true) is not null)
            return;

        // Ctrl+P: a paleta, de qualquer lugar da janela
        if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.Control)
        {
            if (DataContext is MainViewModel vm) vm.AbrirPaletaCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            caixa.Focus();
            caixa.SelectAll(); // digitar já troca o filtro, sem precisar apagar antes
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && caixa.IsFocused)
        {
            caixa.Text = "";
            this.FindControl<ListBox>("TreeList")?.Focus();
            e.Handled = true;
        }
    }

    private double _alturaTerminal = 260;

    /// <summary>
    /// Abre ou recolhe a linha do terminal. A altura é guardada ao recolher, para voltar
    /// do tamanho em que o usuário deixou o divisor.
    /// </summary>
    private void AjustarTerminal()
    {
        var area = this.FindControl<Grid>("AreaTerminal");
        if (area is null) return;
        var linha = area.RowDefinitions[2];

        if (_vm.TerminalVisivel)
        {
            linha.MinHeight = 80;
            linha.Height = new GridLength(_alturaTerminal);
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => this.FindControl<TerminalPanel>("PainelTerminal")?.Focar(),
                Avalonia.Threading.DispatcherPriority.Background);
        }
        else
        {
            if (linha.ActualHeight > 0) _alturaTerminal = linha.ActualHeight;
            linha.MinHeight = 0;
            linha.Height = new GridLength(0);
        }
    }

    /// <summary>Devolve a sidebar à largura que o usuário deixou na última sessão.</summary>
    private void AplicarLarguraSidebar()
    {
        var coluna = Coluna();
        if (coluna is null) return;

        var largura = _vm.Settings.SidebarWidth;
        if (largura >= coluna.MinWidth && largura > 0)
            coluna.Width = new GridLength(largura);
    }

    private void GuardarLarguraSidebar()
    {
        var coluna = Coluna();
        if (coluna is not null) _vm.SetSidebarWidth(coluna.ActualWidth);
    }

    private ColumnDefinition? Coluna() =>
        this.FindControl<Grid>("Layout") is { } g && g.ColumnDefinitions.Count > 0
            ? g.ColumnDefinitions[0]
            : null;

    /// <summary>Tema e cor de destaque vêm do workspace, não de constantes.</summary>
    public void ApplyTheme(Settings settings)
    {
        var variant = settings.Theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark;
        RequestedThemeVariant = variant;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = variant;
            if (Color.TryParse(settings.Accent, out var accent))
            {
                app.Resources["Accent"] = new SolidColorBrush(accent);
                // o texto sobre o destaque segue o brilho da cor escolhida, senão
                // some no claro (ou no escuro, dependendo do que o usuário pegou)
                app.Resources["OnAccent"] = new SolidColorBrush(Contraste(accent));
            }
        }

        // as cores de estado vêm do tema por nome: as listas precisam ser refeitas
        // para pegar a paleta nova
        _vm.RebuildTree();
        _vm.ReloadCurrentTab();
    }

    private static Color Contraste(Color c)
    {
        var luminancia = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminancia > 0.55 ? Color.Parse("#08121F") : Colors.White;
    }

    private void MenuDoGrupo(object? sender, ContextRequestedEventArgs e)
    {
        GroupNode? grupo = null;
        for (var v = e.Source as Visual; v is not null && grupo is null; v = v.GetVisualParent())
            grupo = (v as StyledElement)?.DataContext as GroupNode;

        // "Sem grupo" não é um grupo de verdade: não tem o que editar nem onde pôr subgrupo
        if (grupo is null || grupo.Id.Length == 0 || e.Source is not Control onde) return;

        var id = grupo.Id;
        var nome = grupo.Name;
        var menu = new MenuFlyout();

        var sub = new MenuItem { Header = $"Novo subgrupo em {nome}…" };
        sub.Click += async (_, _) => await _vm.NovoGrupoAsync(id);
        var editar = new MenuItem { Header = "Editar grupo… (nome, cor e onde fica)" };
        editar.Click += async (_, _) => await _vm.EditGroupAsync(id);
        var lote = new MenuItem { Header = "Em lote… (obter, puxar, enviar, trocar de branch)" };
        lote.Click += async (_, _) => await _vm.AbrirLoteAsync(nome, _vm.ReposDoGrupo(id));

        menu.Items.Add(sub);
        menu.Items.Add(editar);
        menu.Items.Add(new Separator());
        menu.Items.Add(lote);
        menu.ShowAt(onde, showAtPointer: true);
        e.Handled = true;
    }

    // a ListBox mistura grupos, pares e repositórios: só repositório vira seleção
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list) return;

        switch (list.SelectedItem)
        {
            case RepoNode node:
                _vm.SelectedNode = node;
                break;
            // clicar no grupo abre o painel dele e recolhe/expande de uma vez: era
            // estranho continuar vendo as alterações de um repositório não selecionado
            case GroupNode group:
                list.SelectedItem = null;
                _vm.MostrarPainel(group.Id.Length == 0 ? "" : group.Id);
                _vm.ToggleGroupCommand.Execute(group);
                break;
            case PainelNode:
                list.SelectedItem = null;
                _vm.MostrarPainel(null);
                break;
            case SeparadorNode:
            case PairNode:
                list.SelectedItem = _vm.SelectedNode;
                break;
        }
    }

    // ------------------------------------------------------- IDialogService

    public Task<bool> ConfirmAsync(string title, string message) =>
        new ConfirmWindow(title, message).ShowDialog<bool>(this);

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public Task<string?> PromptAsync(string title, string label, string initial = "") =>
        new PromptWindow(title, label, initial).ShowDialog<string?>(this);

    public async Task<(string Nome, string Cor)?> ShowGroupAsync(string titulo, string nome, string cor)
    {
        var r = await new GroupWindow(titulo, nome, cor).ShowDialog<GroupResult?>(this);
        return r is null ? null : (r.Nome, r.Cor);
    }

    public async Task<(string Nome, string Cor, string? PaiId)?> ShowGrupoAsync(
        string titulo, string nome, string cor, string? paiId, IReadOnlyList<GrupoNaArvore> pais)
    {
        var r = await new GroupWindow(titulo, nome, cor, paiId, pais).ShowDialog<GroupResult?>(this);
        return r is null ? null : (r.Nome, r.Cor, r.PaiId);
    }

    public Task ShowAddRepoAsync(MainViewModel main) => new AddRepoWindow(main, this).ShowDialog(this);

    public Task ShowRepoConfigAsync(MainViewModel main, Repo repo) =>
        new RepoConfigWindow(main, repo, this).ShowDialog(this);

    public async Task ShowSettingsAsync(MainViewModel main)
    {
        await new SettingsWindow(main).ShowDialog(this);
        ApplyTheme(main.Settings);
    }

    public Task ShowBranchesAsync(MainViewModel main, Repo repo) =>
        new BranchesWindow(main, repo).ShowDialog(this);

    public Task ShowNovidadesAsync() => new NovidadesWindow().ShowDialog(this);

    public Task ShowEsteiraAsync(string slug, string branch, string usuario, string repoNome, int visiveis) =>
        new EsteiraWindow(slug, branch, usuario, repoNome, this, visiveis).ShowDialog(this);

    public Task ShowIssuesAsync(IssuesViewModel vm) => new IssuesWindow(vm).ShowDialog(this);

    public Task ShowCompararAsync(CompararViewModel vm) => new CompararWindow(vm).ShowDialog(this);

    public Task ShowLoteAsync(LoteViewModel vm) => new LoteWindow(vm).ShowDialog(this);

    public Task ShowWorktreesAsync(MainViewModel main, Repo repo) =>
        new WorktreesWindow(main, repo).ShowDialog(this);

    public Task ShowPaletaAsync(PaletaViewModel vm) => new PaletaWindow(vm).ShowDialog(this);

    public Task ShowConflitoAsync(ConflitoViewModel vm) => new ConflitoWindow(vm).ShowDialog(this);

    public Task ShowPullRequestsAsync(PullRequestsViewModel vm) =>
        new PullRequestsWindow(vm).ShowDialog(this);

    public Task ShowStashAsync(MainViewModel main, Repo repo) =>
        new StashWindow(main, repo).ShowDialog(this);

    public Task ShowFileHistoryAsync(Repo repo, string caminho, bool blame, bool split) =>
        new FileHistoryWindow(repo, caminho, blame, split).ShowDialog(this);

    public Task ShowIgnoradosAsync(MainViewModel main, Repo repo) =>
        new IgnoradosWindow(main, repo).ShowDialog(this);

    public Task ShowRebaseAsync(MainViewModel main, Repo repo, string hash) =>
        new RebaseWindow(main, repo, hash).ShowDialog(this);
}
