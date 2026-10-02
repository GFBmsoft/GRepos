using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GRepos.Models;
using GRepos.Services;

namespace GRepos.ViewModels;

public sealed partial class RebaseItemViewModel : ObservableObject
{
    private readonly RebaseViewModel _dono;

    public RebaseItemViewModel(ItemRebase item, RebaseViewModel dono)
    {
        Item = item;
        _dono = dono;
        _mensagem = item.Mensagem;
        item.NovaMensagem = item.Mensagem; // a caixa começa com a mensagem atual
    }

    public ItemRebase Item { get; }
    public string Short => Item.Hash[..7];

    public static IReadOnlyList<string> Acoes { get; } = new[]
    {
        "Manter", "Renomear", "Juntar com o anterior", "Juntar e descartar a mensagem", "Apagar",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Renomeando), nameof(Apagado))]
    private int _acao;

    [ObservableProperty] private string _mensagem;

    public bool Renomeando => (AcaoRebase)Acao == AcaoRebase.Renomear;
    public bool Apagado => (AcaoRebase)Acao == AcaoRebase.Apagar;

    partial void OnAcaoChanged(int value)
    {
        Item.Acao = (AcaoRebase)value;
        _dono.Revalidar();
    }

    partial void OnMensagemChanged(string value)
    {
        Item.NovaMensagem = value;
        _dono.Revalidar();
    }
}

/// <summary>
/// Janela do rebase interativo. A lista aparece do mais novo para o mais antigo, como no
/// histórico; "juntar com o anterior" junta no commit de baixo (o mais antigo).
/// </summary>
public sealed partial class RebaseViewModel : ObservableObject
{
    private readonly Repo _repo;
    private readonly string _desde;

    public RebaseViewModel(Repo repo, string desde)
    {
        _repo = repo;
        _desde = desde;
    }

    public ObservableCollection<RebaseItemViewModel> Itens { get; } = new();

    [ObservableProperty] private string _erro = "";
    [ObservableProperty] private string _aviso = "";
    [ObservableProperty] private bool _podeAplicar;
    [ObservableProperty] private bool _busy;

    /// <summary>Fecha a janela; true quando o rebase rodou (mesmo que tenha parado em conflito).</summary>
    public Action<bool>? Fechar { get; set; }
    public Func<string, string, Task<bool>>? Confirmar { get; set; }

    public bool TemErro => Erro.Length > 0;
    partial void OnErroChanged(string value) => OnPropertyChanged(nameof(TemErro));

    private bool _jaEnviado;

    public async Task CarregarAsync()
    {
        try
        {
            var itens = await RebaseInterativo.ListarAsync(_repo.Path, _desde);
            foreach (var i in Enumerable.Reverse(itens)) Itens.Add(new RebaseItemViewModel(i, this));
            _jaEnviado = await RebaseInterativo.JaEnviadoAsync(_repo.Path, _desde);
            Aviso = _jaEnviado
                ? "Alguns destes commits já estão no remoto: depois de reorganizar, o envio vai exigir push forçado."
                : "";
            Revalidar();
        }
        catch (Exception e)
        {
            Erro = e.Message;
        }
    }

    private List<ItemRebase> DoMaisAntigo() => Itens.Reverse().Select(i => i.Item).ToList();

    private List<string> _ordemOriginal = new();

    public void Revalidar()
    {
        if (Itens.Count == 0) { PodeAplicar = false; return; }
        if (_ordemOriginal.Count == 0) _ordemOriginal = Itens.Select(i => i.Item.Hash).ToList();

        var problema = RebaseInterativo.Validar(DoMaisAntigo());
        Erro = problema ?? "";

        // sem mudança nenhuma não há o que aplicar — mas isso não é erro, só botão desligado
        var mudou = Itens.Any(i => i.Item.Acao != AcaoRebase.Manter) ||
                    !Itens.Select(i => i.Item.Hash).SequenceEqual(_ordemOriginal);
        PodeAplicar = problema is null && mudou;
    }

    [RelayCommand]
    private void Subir(RebaseItemViewModel item) => Mover(item, -1);

    [RelayCommand]
    private void Descer(RebaseItemViewModel item) => Mover(item, +1);

    private void Mover(RebaseItemViewModel item, int passo)
    {
        var i = Itens.IndexOf(item);
        var j = i + passo;
        if (i < 0 || j < 0 || j >= Itens.Count) return;
        Itens.Move(i, j);
        Revalidar();
    }

    [RelayCommand]
    private async Task Aplicar()
    {
        Revalidar();
        if (!PodeAplicar || Busy) return;

        if (Confirmar is not null && !await Confirmar("Reorganizar commits",
                "Reescrever estes commits com as mudanças da lista?" +
                (_jaEnviado ? "\n\nEles já estão no remoto: o próximo envio vai exigir push forçado." : "") +
                "\n\nSe parar em conflito, resolva na aba Alterações e use Continuar (ou Abortar)."))
            return;

        Busy = true;
        try
        {
            await RebaseInterativo.AplicarAsync(_repo.Path, DoMaisAntigo());
            Fechar?.Invoke(true);
        }
        catch (Exception e)
        {
            if (GitService.OperacaoEmAndamento(_repo.Path) == GitService.Operacao.Rebase)
            {
                // parou no meio: a resolução é na aba Alterações, que mostra a faixa do rebase
                Fechar?.Invoke(true);
                return;
            }
            Erro = e.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}
