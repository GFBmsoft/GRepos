using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;

namespace GRepos.Views;

/// <summary>Base dos diálogos: moldura, título e rodapé de botões no mesmo padrão.</summary>
public abstract class DialogWindow : Window
{
    protected DialogWindow(string title, double width = 460)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
    }

    protected static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        Margin = new Thickness(0, 0, 0, 4),
        TextWrapping = TextWrapping.Wrap,
        Classes = { "faint" },
    };

    protected static StackPanel Field(string label, Control input) => new()
    {
        Margin = new Thickness(0, 0, 0, 10),
        Children = { Label(label), input },
    };

    /// <summary>Moldura com a borda do tema (segue claro/escuro).</summary>
    protected static Border Framed(Control child)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = child,
        };
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("Border"));
        return border;
    }

    protected static Button Btn(string text, bool primary = false)
    {
        var b = new Button { Content = text, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) b.Classes.Add("primary");
        return b;
    }

    protected void Compose(string heading, IEnumerable<Control> body, IEnumerable<Control> footer)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 14.5,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        foreach (var c in body) panel.Children.Add(c);

        var foot = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
        };
        foreach (var c in footer) foot.Children.Add(c);
        panel.Children.Add(foot);

        Content = new Border { Padding = new Thickness(16), Child = panel };
    }
}

// ------------------------------------------------------------------ confirmar

public sealed class ConfirmWindow : DialogWindow
{
    public ConfirmWindow(string title, string message) : base(title, 420)
    {
        var no = Btn("Cancelar");
        var yes = Btn("Confirmar", true);
        no.Click += (_, _) => Close(false);
        yes.Click += (_, _) => Close(true);

        Compose(title,
            new Control[]
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) },
            },
            new[] { no, yes });
    }
}

// --------------------------------------------------------------------- texto

public sealed class PromptWindow : DialogWindow
{
    public PromptWindow(string title, string label, string initial) : base(title, 420)
    {
        var input = new TextBox { Text = initial };
        var cancel = Btn("Cancelar");
        var ok = Btn("Confirmar", true);

        cancel.Click += (_, _) => Close(null);
        ok.Click += (_, _) => Close(input.Text);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) Close(input.Text);
        };

        Compose(title, new Control[] { Field(label, input) }, new[] { cancel, ok });
        Opened += (_, _) => input.Focus();
    }
}

// ------------------------------------------------------------------- grupo

/// <summary>Nome e cor do grupo. Devolve null quando o usuário cancela.</summary>
public sealed class GroupWindow : DialogWindow
{
    public GroupWindow(string titulo, string nome, string cor) : base(titulo, 420)
    {
        var escolhida = GroupPalette.Normalizar(cor) ?? GroupPalette.Padrao;

        var nomeBox = new TextBox { Text = nome, Watermark = "Ex.: Módulos BMSoft" };
        var hexBox = new TextBox { Text = escolhida, Width = 96 };

        var amostra = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.Parse(escolhida)),
            Margin = new Thickness(8, 0, 0, 0),
        };

        var swatches = new WrapPanel();
        var botoes = new List<Button>();

        void Selecionar(string valor, bool atualizaHex)
        {
            escolhida = valor;
            amostra.Background = new SolidColorBrush(Color.Parse(valor));
            if (atualizaHex) hexBox.Text = valor;
            foreach (var b in botoes)
                b.BorderThickness = new Thickness(
                    b.Tag as string == valor ? 3 : 0);
        }

        foreach (var c in GroupPalette.Cores)
        {
            var b = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.Parse(c)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(c == escolhida ? 3 : 0),
                Padding = new Thickness(0),
                Tag = c,
            };
            b.Click += (_, _) => Selecionar(c, true);
            botoes.Add(b);
            swatches.Children.Add(b);
        }

        hexBox.LostFocus += (_, _) =>
        {
            var v = GroupPalette.Normalizar(hexBox.Text);
            if (v is null) hexBox.Text = escolhida; // texto inválido volta ao que valia
            else Selecionar(v, false);
        };

        var linhaCor = new StackPanel { Orientation = Orientation.Horizontal };
        linhaCor.Children.Add(hexBox);
        linhaCor.Children.Add(amostra);

        var cancelar = Btn("Cancelar");
        var salvar = Btn("Salvar", true);
        cancelar.Click += (_, _) => Close(null);
        salvar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nomeBox.Text)) return;
            Close(new GroupResult(nomeBox.Text!.Trim(), escolhida));
        };

        Compose(titulo,
            new Control[]
            {
                Field("Nome do grupo", nomeBox),
                Field("Cor", swatches),
                Field("…ou informe o código da cor", linhaCor),
            },
            new[] { cancelar, salvar });

        Opened += (_, _) => nomeBox.Focus();
    }
}

public sealed record GroupResult(string Nome, string Cor);

// ------------------------------------------------------------ adicionar repo

public sealed class AddRepoWindow : DialogWindow
{
    public AddRepoWindow(MainViewModel main, IDialogService dialogs) : base("Adicionar repositório")
    {
        var path = new TextBox { Watermark = @"D:\Projetos\..." };
        var name = new TextBox();
        var group = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var newGroup = new TextBox { Watermark = "Ex.: Financeiro" };

        var groups = main.Groups.ToList();
        group.ItemsSource = new[] { "Sem grupo" }.Concat(groups.Select(g => g.Name)).ToList();
        group.SelectedIndex = 0;

        var browse = Btn("Procurar");
        browse.Click += async (_, _) =>
        {
            var chosen = await dialogs.PickFolderAsync("Selecione o repositório");
            if (chosen is null) return;
            path.Text = chosen;
            try
            {
                name.Text = await GitService.InspectPathAsync(chosen);
            }
            catch (Exception ex)
            {
                main.Notify(ex.Message, true);
                name.Text = "";
            }
        };

        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        browse.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(path);
        pathRow.Children.Add(browse);

        var cancel = Btn("Cancelar");
        var add = Btn("Adicionar", true);
        cancel.Click += (_, _) => Close();
        add.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(path.Text) || string.IsNullOrWhiteSpace(name.Text)) return;

            string? groupId = null;
            if (!string.IsNullOrWhiteSpace(newGroup.Text)) groupId = main.CreateGroup(newGroup.Text!.Trim());
            else if (group.SelectedIndex > 0) groupId = groups[group.SelectedIndex - 1].Id;

            main.AddRepository(path.Text!.Trim(), name.Text!.Trim(), groupId);
            Close();
        };

        Compose("Adicionar repositório",
            new Control[]
            {
                Field("Pasta do repositório", pathRow),
                Field("Nome exibido", name),
                Field("Grupo", group),
                Field("…ou criar um grupo novo", newGroup),
            },
            new[] { cancel, add });
    }
}

// --------------------------------------------------------- configurar repo

public sealed class RepoConfigWindow : DialogWindow
{
    public RepoConfigWindow(MainViewModel main, Repo repo, IDialogService dialogs) : base("Configurar repositório")
    {
        var name = new TextBox { Text = repo.Name, Watermark = "como aparece na lista" };
        var pathBox = new TextBox { Text = repo.Path, IsReadOnly = true };

        var dicaNome = new TextBlock
        {
            Text = "Pasta no disco: " + System.IO.Path.GetFileName(repo.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            Classes = { "faint" },
        };
        var group = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var pairKey = new TextBox { Text = repo.PairKey ?? "", Watermark = "Ex.: Financeiro" };
        var role = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "origem", "destino" },
            SelectedIndex = repo.Role == "destino" ? 1 : 0,
        };

        var groups = main.Groups.ToList();
        group.ItemsSource = new[] { "Sem grupo" }.Concat(groups.Select(g => g.Name)).ToList();
        group.SelectedIndex = repo.GroupId is null ? 0 : groups.FindIndex(g => g.Id == repo.GroupId) + 1;

        var known = main.Repos.Where(r => !string.IsNullOrEmpty(r.PairKey))
                              .Select(r => r.PairKey!)
                              .Distinct()
                              .ToList();
        var explicacaoPapel = new TextBlock
        {
            Text = "O papel só vale quando há um par: marque “origem” no repositório de onde as alterações " +
                   "saem (ex.: o DBISAM) e “destino” no que as recebe (ex.: o MySQL). Isso define a ordem em " +
                   "que os dois aparecem na lista e qual fica à esquerda na aba Par. Sem par, deixe em branco.",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "faint" },
        };

        var hint = new TextBlock
        {
            Text = known.Count > 0 ? "Pares já usados: " + string.Join(", ", known) : "",
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = known.Count > 0,
            Classes = { "faint" },
        };


        // ------------------------------------------- URL do remoto com variáveis

        var urlBox = new TextBox
        {
            Text = repo.RemoteTemplate ?? "",
            Watermark = "https://{{user}}:{{token}}@github.com/owner/repo.git",
        };

        var urlAtual = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Classes = { "faint" },
            Text = "Use {{user}} e {{token}}: o modelo é salvo assim, e o token entra só na hora de gravar no git.",
        };

        var sugerir = Btn("Usar o remoto atual");
        var aplicar = Btn("Aplicar no git");
        sugerir.MinWidth = 140;
        aplicar.MinWidth = 120;

        async void MostrarRemotoAtual()
        {
            var atual = await GitService.RemoteUrlAsync(repo.Path);
            urlAtual.Text = atual.Length > 0
                ? "Remoto atual: " + GitService.WebUrl(atual)
                : "Este repositório ainda não tem remoto configurado.";
        }

        sugerir.Click += async (_, _) =>
        {
            var atual = await GitService.RemoteUrlAsync(repo.Path);
            urlBox.Text = UrlTemplate.Sugerir(atual);
        };

        aplicar.Click += async (_, _) =>
        {
            try
            {
                var modelo = (urlBox.Text ?? "").Trim();
                if (modelo.Length == 0) return;

                var usuario = main.Settings.GithubUser;
                var token = UrlTemplate.UsaToken(modelo)
                    ? await GitHubService.TokenDoUsuarioAsync(usuario)
                    : null;

                if (UrlTemplate.UsaToken(modelo) && string.IsNullOrEmpty(token))
                {
                    urlAtual.Text = $"Nenhum token salvo para “{usuario}”. Configure em Preferências → Autenticação.";
                    return;
                }

                await GitService.SetRemoteUrlAsync(repo.Path, UrlTemplate.Expandir(modelo, usuario, token));
                urlAtual.Text = "Remoto atualizado: " + UrlTemplate.Mascarar(UrlTemplate.Expandir(modelo, usuario, token), token);
            }
            catch (Exception ex)
            {
                urlAtual.Text = ex.Message;
            }
        };

        var acoesUrl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        acoesUrl.Children.Add(sugerir);
        acoesUrl.Children.Add(aplicar);

        var remove = Btn("Remover da lista");
        remove.Classes.Add("danger");
        var cancel = Btn("Cancelar");
        var save = Btn("Salvar", true);

        remove.Click += async (_, _) =>
        {
            var ok = await dialogs.ConfirmAsync("Remover repositório",
                $"Remover \"{repo.Name}\" do GRepos?\n\nO repositório em disco não é apagado.");
            if (!ok) return;
            main.RemoveRepository(repo);
            Close();
        };
        cancel.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            var gid = group.SelectedIndex > 0 ? groups[group.SelectedIndex - 1].Id : null;
            main.UpdateRepository(repo,
                string.IsNullOrWhiteSpace(name.Text) ? repo.Name : name.Text!.Trim(),
                gid,
                pairKey.Text,
                role.SelectedItem as string,
                urlBox.Text);
            Close();
        };

        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        footer.Children.Add(remove);
        footer.Children.Add(cancel);
        footer.Children.Add(save);

        Opened += (_, _) => MostrarRemotoAtual();

        Compose("Configurar repositório",
            new Control[]
            {
                Field("Nome de exibição", name),
                dicaNome,
                Field("Caminho", pathBox),
                Field("Grupo", group),
                Field("URL do remoto (modelo)", urlBox),
                acoesUrl,
                urlAtual,
                Field("Par (mesmo módulo em outro banco) — use a mesma chave nos dois repositórios", pairKey),
                hint,
                Field("Papel neste par", role),
                explicacaoPapel,
            },
            new Control[] { footer });
    }
}

// ---------------------------------------------------------------- ajustes

public sealed class SettingsWindow : DialogWindow
{
    private static readonly string[] Accents =
        { "#4F8CFF", "#3FB950", "#F0883E", "#D2A8FF", "#E3B341", "#56D4BC" };

    public SettingsWindow(MainViewModel main) : base("Preferências", 480)
    {
        var s = main.Settings;

        var theme = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Escuro", "Claro" },
            SelectedIndex = s.Theme == "light" ? 1 : 0,
        };
        var density = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Compacta", "Confortável" },
            SelectedIndex = s.Density == "confortavel" ? 1 : 0,
        };
        var abaInicial = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Alterações", "Histórico" },
            SelectedIndex = s.DefaultTab == "historico" ? 1 : 0,
        };
        var refresh = new NumericUpDown { Minimum = 0, Maximum = 3600, Value = s.AutoRefreshSeconds, Increment = 10 };
        var logLimit = new NumericUpDown { Minimum = 50, Maximum = 5000, Value = s.LogLimit, Increment = 50 };

        var accent = s.Accent;
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var color in Accents)
        {
            var dot = new Button
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.Parse(color)),
                BorderThickness = new Thickness(color == accent ? 2 : 0),
                BorderBrush = Brushes.White,
                Padding = new Thickness(0),
            };
            dot.Click += (_, _) =>
            {
                accent = color;
                foreach (var child in swatches.Children.OfType<Button>())
                    child.BorderThickness = new Thickness(
                        child.Background is SolidColorBrush b && b.Color == Color.Parse(accent) ? 2 : 0);
            };
            swatches.Children.Add(dot);
        }

        var groupsPanel = new StackPanel { Spacing = 5 };
        foreach (var g in main.Groups.ToList())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };

            var ponto = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse(GroupPalette.Normalizar(g.Color) ?? GroupPalette.Padrao)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 8, 0),
            };
            var nome = new TextBlock { Text = g.Name, VerticalAlignment = VerticalAlignment.Center };
            var editar = Btn("Editar");
            var del = Btn("Excluir");
            del.Classes.Add("danger");
            editar.MinWidth = 70;
            del.MinWidth = 70;
            editar.Margin = new Thickness(6, 0, 0, 0);
            del.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(nome, 1);
            Grid.SetColumn(editar, 2);
            Grid.SetColumn(del, 3);

            editar.Click += async (_, _) =>
            {
                await main.EditGroupAsync(g.Id);
                var atual = main.Groups.FirstOrDefault(x => x.Id == g.Id);
                if (atual is null) return;
                nome.Text = atual.Name;
                ponto.Background = new SolidColorBrush(
                    Color.Parse(GroupPalette.Normalizar(atual.Color) ?? GroupPalette.Padrao));
            };
            del.Click += (_, _) =>
            {
                main.RemoveGroup(g.Id);
                groupsPanel.Children.Remove(row);
            };

            row.Children.Add(ponto);
            row.Children.Add(nome);
            row.Children.Add(editar);
            row.Children.Add(del);
            groupsPanel.Children.Add(row);
        }

        // ---------------------------------------------------- autenticação

        var usuario = new TextBox { Text = s.GithubUser, Watermark = "seu usuário no GitHub" };
        var token = new TextBox
        {
            PasswordChar = '●',
            Watermark = GitHubService.TemTokenGuardado(s.GithubUser)
                ? "token já salvo — preencha só para trocar"
                : "cole aqui o personal access token",
        };
        var situacao = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Classes = { "faint" },
            Text = "O token é guardado no Gerenciador de Credenciais do Windows, junto com o do git. " +
                   "Só o nome de usuário fica no arquivo de configuração.",
        };

        var salvarToken = Btn("Salvar token");
        var testar = Btn("Testar");
        var remover = Btn("Remover");
        remover.Classes.Add("danger");
        foreach (var b in new[] { salvarToken, testar, remover }) b.MinWidth = 96;

        async void ComAviso(Func<Task<string>> acao)
        {
            try
            {
                situacao.Text = "Aguarde…";
                situacao.Foreground = null;
                situacao.Text = await acao();
            }
            catch (Exception ex)
            {
                situacao.Text = ex.Message;
                situacao.Foreground = new SolidColorBrush(Color.Parse("#E5534B"));
            }
        }

        salvarToken.Click += (_, _) => ComAviso(async () =>
        {
            await GitHubService.SalvarCredencialAsync(usuario.Text ?? "", token.Text ?? "");
            token.Text = "";
            token.Watermark = "token já salvo — preencha só para trocar";
            return "Token salvo para " + usuario.Text;
        });

        testar.Click += (_, _) => ComAviso(async () =>
            "Conectado como " + await GitHubService.TestarAsync(usuario.Text ?? ""));

        remover.Click += (_, _) => ComAviso(async () =>
        {
            await GitHubService.RemoverCredencialAsync(usuario.Text ?? "");
            return "Token removido do gerenciador de credenciais.";
        });

        var acoesToken = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        acoesToken.Children.Add(salvarToken);
        acoesToken.Children.Add(testar);
        acoesToken.Children.Add(remover);

        var close = Btn("Fechar");
        var save = Btn("Salvar", true);
        close.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            main.SetGithubUser(usuario.Text ?? "");
            main.ApplySettings(
                theme.SelectedIndex == 1 ? "light" : "dark",
                accent,
                density.SelectedIndex == 1 ? "confortavel" : "compacta",
                (int)(refresh.Value ?? 60),
                (int)(logLimit.Value ?? 300),
                abaInicial.SelectedIndex == 1 ? "historico" : "alteracoes");
            Close();
        };

        var body = new List<Control>
        {
            Field("Tema", theme),
            Field("Cor de destaque", swatches),
            Field("Densidade das listas", density),
            Field("Abrir o repositório em", abaInicial),
            Field("Atualizar status automaticamente (segundos, 0 desliga)", refresh),
            Field("Commits carregados no histórico", logLimit),
            new TextBlock { Text = "AUTENTICAÇÃO NO GITHUB", Classes = { "sectionTitle" }, Margin = new Thickness(0, 6, 0, 8) },
            Field("Usuário", usuario),
            Field("Token de acesso pessoal", token),
            acoesToken,
            situacao,
        };
        if (groupsPanel.Children.Count > 0) body.Add(Field("Grupos", groupsPanel));

        Compose("Preferências", body, new[] { close, save });
    }
}

// ------------------------------------------------------------------ stash

public sealed class StashWindow : DialogWindow
{
    private readonly MainViewModel _main;
    private readonly Repo _repo;
    private readonly ListBox _list = new() { MaxHeight = 260 };
    private readonly TextBlock _empty = new()
    {
        Text = "Nenhum stash guardado.",
        Margin = new Thickness(0, 10, 0, 0),
        Classes = { "faint" },
    };

    public StashWindow(MainViewModel main, Repo repo) : base($"Stash — {repo.Name}", 500)
    {
        _main = main;
        _repo = repo;

        var msg = new TextBox { Watermark = "Descrição (opcional)" };
        var push = Btn("Guardar", true);
        push.Margin = new Thickness(6, 0, 0, 0);
        push.Click += async (_, _) =>
        {
            await RunAsync(() => GitService.StashPushAsync(_repo.Path, msg.Text ?? "", false));
            msg.Text = "";
        };

        var pushRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(push, 1);
        pushRow.Children.Add(msg);
        pushRow.Children.Add(push);

        _list.ItemTemplate = new FuncDataTemplate<StashEntry>((s, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Height = 26 };
            var text = new TextBlock
            {
                Text = $"{s.Label} — {s.Subject}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var apply = new Button { Content = "Aplicar", Classes = { "tiny" } };
            var drop = new Button { Content = "✕", Classes = { "tiny", "danger" } };
            Grid.SetColumn(apply, 1);
            Grid.SetColumn(drop, 2);

            apply.Click += async (_, _) => await RunAsync(() => GitService.StashApplyAsync(_repo.Path, s.Index, true));
            drop.Click += async (_, _) => await RunAsync(() => GitService.StashDropAsync(_repo.Path, s.Index));

            row.Children.Add(text);
            row.Children.Add(apply);
            row.Children.Add(drop);
            return row;
        });

        var close = Btn("Fechar");
        close.Click += (_, _) => Close();

        Compose($"Stash — {repo.Name}",
            new Control[] { Field("Guardar alterações atuais", pushRow), _list, _empty },
            new[] { close });

        Opened += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var list = await GitService.StashesAsync(_repo.Path);
            _list.ItemsSource = list;
            _empty.IsVisible = list.Count == 0;
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            await ReloadAsync();
            await _main.RefreshRepoAsync(_repo.Id);
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }
}
