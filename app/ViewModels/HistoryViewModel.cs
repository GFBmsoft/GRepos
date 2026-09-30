using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed class RefChipViewModel
{
    public string Text { get; init; } = "";
    public string Color { get; init; } = "TextDim";
    public string Tooltip { get; init; } = "";
}

public sealed class CommitRowViewModel
{
    public Commit Commit { get; init; } = new();
    public GraphRow Row { get; init; } = new();
    public int MaxLanes { get; init; }

    /// <summary>Largura da coluna do grafo: 14px por raia, com teto para não comer a lista.</summary>
    public double GraphWidth => Math.Min(MaxLanes * 14 + 12, 180);

    public string Subject => Commit.Subject;
    public string Author => Commit.Author;
    public string Short => Commit.Short;
    public string DateText => FormatDate(Commit.Date, false);

    /// <summary>
    /// Só o chip mais relevante fica na lista; o resto vira "+N". As refs completas
    /// estão no painel lateral, e repetir tudo em cada linha polui a leitura.
    /// </summary>
    private const int MaxChips = 1;

    /// <summary>Data curta e relativa: "3 h", "ontem", "12/08". O completo fica no painel.</summary>
    public string ShortDate
    {
        get
        {
            if (!DateTimeOffset.TryParse(Commit.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return "";

            var local = d.ToLocalTime();
            var agora = DateTimeOffset.Now;
            var diff = agora - local;

            if (diff.TotalMinutes < 1) return "agora";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min";
            if (diff.TotalHours < 24 && local.Date == agora.Date) return $"{(int)diff.TotalHours} h";
            if (local.Date == agora.Date.AddDays(-1)) return "ontem";
            // o formato já fixa dia/mês; pedir a cultura pt-BR pelo nome só acrescenta
            // um jeito de falhar (ela não existe em modo globalização-invariante)
            if (local.Year == agora.Year) return local.ToString("dd/MM", CultureInfo.InvariantCulture);
            return local.ToString("dd/MM/yy", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Tag em laranja (o mesmo do app), branch atual no azul de destaque e remotos
    /// apagados: o olho acha a versão sem ler chip por chip. O excesso vira "+N",
    /// senão o último chip fica cortado ao meio e o assunto perde espaço.
    /// </summary>
    public List<RefChipViewModel> Refs
    {
        get
        {
            var ordenados = Commit.Refs.OrderBy(Prioridade).ToList();
            var chips = ordenados.Take(MaxChips).Select(r => new RefChipViewModel
            {
                Text = r.Replace("tag: ", ""),
                Color = RefColor(r),
            }).ToList();

            var resto = ordenados.Count - chips.Count;
            if (resto > 0)
                chips.Add(new RefChipViewModel
                {
                    Text = $"+{resto}",
                    Color = "TextDim",
                    Tooltip = string.Join("\n", ordenados.Skip(MaxChips).Select(r => r.Replace("tag: ", ""))),
                });

            return chips;
        }
    }

    public static string RefColor(string r) =>
        r.StartsWith("tag:") ? "Orange" : r.StartsWith("HEAD") ? "Accent" : "TextDim";

    private static int Prioridade(string r) =>
        r.StartsWith("HEAD") ? 0 : r.StartsWith("tag:") ? 1 : r.Contains('/') ? 3 : 2;

    public static string FormatDate(string iso, bool full)
    {
        if (!DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return iso;
        var local = d.ToLocalTime();
        return full
            ? local.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo("pt-BR"))
            : local.ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("pt-BR"));
    }
}

public sealed class CommitFileViewModel
{
    public CommitFile File { get; init; } = new();
    public string Path => File.Path;
    public string Status => File.Status;
    public string AddedText => $"+{File.Added}";
    public string RemovedText => $"-{File.Removed}";

    public string StatusColor => Status switch
    {
        "M" => "Yellow",
        "A" => "Green",
        "D" => "Red",
        "R" or "C" => "Purple",
        _ => "TextDim",
    };
}

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly MainViewModel _main;
    private readonly int _limit;

    public HistoryViewModel(Repo repo, MainViewModel main, int limit, bool split)
    {
        _repo = repo;
        _main = main;
        _limit = limit;
        Diff = new DiffViewModel { Split = split };
    }

    public DiffViewModel Diff { get; }

    [ObservableProperty] private ObservableCollection<CommitRowViewModel> _commits = new();
    [ObservableProperty] private CommitRowViewModel? _selectedCommit;
    [ObservableProperty] private ObservableCollection<CommitFileViewModel> _files = new();
    [ObservableProperty] private CommitFileViewModel? _selectedFile;
    [ObservableProperty] private bool _allBranches = true;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private string _detailSubject = "";
    [ObservableProperty] private string _detailAuthor = "";
    [ObservableProperty] private string _detailDate = "";
    [ObservableProperty] private string _detailHash = "";
    [ObservableProperty] private string _detailParents = "";
    [ObservableProperty] private string _detailBody = "";

    /// <summary>Refs completas do commit — na lista fica só a principal.</summary>
    [ObservableProperty] private ObservableCollection<RefChipViewModel> _detailRefs = new();

    public bool HasRefs => DetailRefs.Count > 0;

    partial void OnDetailRefsChanged(ObservableCollection<RefChipViewModel> value) =>
        OnPropertyChanged(nameof(HasRefs));

    public int Count => Commits.Count;

    public bool HasBody => !string.IsNullOrWhiteSpace(DetailBody);
    public string DetailParentsText => string.IsNullOrEmpty(DetailParents) ? "" : $"pais: {DetailParents}";

    partial void OnDetailBodyChanged(string value) => OnPropertyChanged(nameof(HasBody));

    partial void OnDetailParentsChanged(string value) => OnPropertyChanged(nameof(DetailParentsText));

    partial void OnAllBranchesChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedCommitChanged(CommitRowViewModel? value) => _ = LoadDetailAsync(value);

    partial void OnSelectedFileChanged(CommitFileViewModel? value) => _ = LoadFileDiffAsync(value);

    public async Task LoadAsync()
    {
        try
        {
            var commits = await GitService.LogAsync(_repo.Path, _limit, AllBranches);
            var rows = GraphBuilder.Build(commits);
            var max = GraphBuilder.MaxLanes(rows);

            Commits = new ObservableCollection<CommitRowViewModel>(
                rows.Select(r => new CommitRowViewModel { Commit = r.Commit, Row = r, MaxLanes = max }));

            OnPropertyChanged(nameof(Count));
            SelectedCommit = null;
            HasDetail = false;
            Files = new ObservableCollection<CommitFileViewModel>();
            Diff.Clear("Selecione um commit para ver os detalhes.");
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private async Task LoadDetailAsync(CommitRowViewModel? row)
    {
        if (row is null)
        {
            HasDetail = false;
            return;
        }
        try
        {
            var d = await GitService.CommitDetailAsync(_repo.Path, row.Commit.Hash);
            DetailSubject = d.Commit.Subject;
            DetailAuthor = $"{d.Commit.Author} <{d.Commit.Email}>";
            DetailDate = CommitRowViewModel.FormatDate(d.Commit.Date, true);
            DetailHash = d.Commit.Short;
            DetailParents = d.Commit.Parents.Count == 0
                ? "—"
                : string.Join(", ", d.Commit.Parents.Select(p => p.Length >= 7 ? p[..7] : p));
            DetailBody = d.Body;
            DetailRefs = new ObservableCollection<RefChipViewModel>(
                d.Commit.Refs.Select(r => new RefChipViewModel
                {
                    Text = r.Replace("tag: ", ""),
                    Color = CommitRowViewModel.RefColor(r),
                }));
            HasDetail = true;

            Files = new ObservableCollection<CommitFileViewModel>(
                d.Files.Select(f => new CommitFileViewModel { File = f }));
            SelectedFile = Files.FirstOrDefault();
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }

    private async Task LoadFileDiffAsync(CommitFileViewModel? file)
    {
        if (file is null || SelectedCommit is null)
        {
            Diff.Clear("Selecione um arquivo do commit.");
            return;
        }
        try
        {
            var raw = await GitService.CommitFileDiffAsync(_repo.Path, SelectedCommit.Commit.Hash, file.Path);
            Diff.Title = file.Path;
            Diff.Load(raw);
        }
        catch (Exception e)
        {
            _main.Notify(e.Message, true);
        }
    }
}
