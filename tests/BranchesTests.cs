using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GRepos.Models;
using GRepos.Services;
using GRepos.ViewModels;
using Xunit;

namespace GRepos.Tests;

public class BranchesViewModelTests
{
    private sealed class FakeDialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string t, string m) => Task.FromResult(true);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
        public Task<string?> PromptAsync(string t, string l, string i = "") => Task.FromResult<string?>(null);
        public Task<(string Nome, string Cor)?> ShowGroupAsync(string t, string n, string c) => Task.FromResult<(string, string)?>(null);
        public Task ShowAddRepoAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowRepoConfigAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowSettingsAsync(MainViewModel m) => Task.CompletedTask;
        public Task ShowBranchesAsync(MainViewModel m, Repo r) => Task.CompletedTask;
        public Task ShowEsteiraAsync(string s, string b, string u, string n) => Task.CompletedTask;
        public Task ShowNovidadesAsync() => Task.CompletedTask;
        public Task ShowStashAsync(MainViewModel m, Repo r) => Task.CompletedTask;
    }

    private static void Limpar(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
        catch (Exception) { /* pasta temporária */ }
    }

    private static async Task<string> RepoComBranches()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-br-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        await GitService.RunAsync(dir, new[] { "init", "-q", "-b", "main" });
        await GitService.RunAsync(dir, new[] { "config", "user.email", "t@t" });
        await GitService.RunAsync(dir, new[] { "config", "user.name", "Teste" });
        File.WriteAllText(Path.Combine(dir, "a.txt"), "um\n");
        await GitService.RunAsync(dir, new[] { "add", "." });
        await GitService.RunAsync(dir, new[] { "commit", "-qm", "Feat: melhoria na rotina de estoques" });
        await GitService.RunAsync(dir, new[] { "branch", "feat/SincEstoques" });
        await GitService.RunAsync(dir, new[] { "branch", "fix/WatermarkProdutoNaoIntegrado" });
        return dir;
    }

    private static BranchesViewModel Vm(string dir) =>
        new(new Repo { Id = "r1", Name = "BM2Maga", Path = dir }, new MainViewModel(new FakeDialogs()));

    [Fact]
    public async Task Separa_locais_de_remotas_com_a_atual_no_topo()
    {
        var dir = await RepoComBranches();
        try
        {
            var vm = Vm(dir);
            await vm.CarregarAsync();

            Assert.False(vm.TemErro);
            Assert.Equal(3, vm.TotalLocais);
            Assert.Equal(0, vm.TotalRemotas);
            Assert.Equal("main", vm.Locais[0].Name);   // a atual vem primeiro
            Assert.True(vm.Locais[0].IsHead);
            Assert.False(vm.Locais[0].CanCheckout);    // não se troca para a que já está
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Cores_distinguem_atual_so_local_e_rastreada()
    {
        var dir = await RepoComBranches();
        try
        {
            var vm = Vm(dir);
            await vm.CarregarAsync();

            var atual = vm.Locais.Single(b => b.IsHead);
            Assert.Equal("Green", atual.NameColor);
            Assert.Equal("SemiBold", atual.NameWeight);

            // criadas localmente, sem remoto configurado
            var soLocal = vm.Locais.First(b => !b.IsHead);
            Assert.True(soLocal.SoLocal);
            Assert.Equal("Yellow", soLocal.NameColor);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public void Branch_rastreando_remoto_usa_o_texto_normal()
    {
        var item = new BranchItemViewModel
        {
            Branch = new Branch { Name = "develop", Upstream = "origin/develop", Subject = "x" },
        };

        Assert.False(item.SoLocal);
        Assert.Equal("Text", item.NameColor);
        Assert.Contains("rastreando origin/develop", item.Tooltip);
    }

    [Fact]
    public void Branch_remota_fica_apagada()
    {
        var item = new BranchItemViewModel
        {
            Branch = new Branch { Name = "origin/feat/x", IsRemote = true, Subject = "x" },
        };

        Assert.Equal("TextDim", item.NameColor);
        Assert.False(item.SoLocal); // remota não é "só local"
    }

    [Fact]
    public async Task Filtro_procura_por_nome_e_por_assunto()
    {
        var dir = await RepoComBranches();
        try
        {
            var vm = Vm(dir);
            await vm.CarregarAsync();

            vm.Filtro = "watermark";
            Assert.Equal(1, vm.TotalLocais);
            Assert.Equal("fix/WatermarkProdutoNaoIntegrado", vm.Locais[0].Name);

            vm.Filtro = "estoques";   // está no assunto do commit de todas
            Assert.Equal(3, vm.TotalLocais);

            vm.Filtro = "";
            Assert.Equal(3, vm.TotalLocais);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Trocar_de_branch_muda_a_atual()
    {
        var dir = await RepoComBranches();
        try
        {
            var vm = Vm(dir);
            await vm.CarregarAsync();

            var destino = vm.Locais.First(b => b.Name == "feat/SincEstoques");
            await vm.TrocarCommand.ExecuteAsync(destino);

            Assert.False(vm.TemErro);
            Assert.Equal("feat/SincEstoques", vm.Locais.Single(b => b.IsHead).Name);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Criar_branch_ja_deixa_ela_como_atual()
    {
        var dir = await RepoComBranches();
        try
        {
            var vm = Vm(dir);
            await vm.CarregarAsync();

            vm.NovaBranch = "feat/RefatoracaoRest";
            Assert.True(vm.PodeCriar);
            await vm.CriarCommand.ExecuteAsync(null);

            Assert.Equal("feat/RefatoracaoRest", vm.Locais.Single(b => b.IsHead).Name);
            Assert.Equal("", vm.NovaBranch);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Repositorio_inacessivel_mostra_o_erro_na_propria_tela()
    {
        var dir = Path.Combine(Path.GetTempPath(), "grepos-vazio-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var vm = Vm(dir); // pasta sem git
            await vm.CarregarAsync();

            Assert.True(vm.TemErro);
            Assert.False(string.IsNullOrWhiteSpace(vm.Erro));
            Assert.Equal(0, vm.TotalLocais);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public void Dono_diferente_vira_mensagem_em_portugues_com_saida_de_um_clique()
    {
        var vm = Vm(Path.GetTempPath());

        // mensagem real do git quando a pasta é de outro usuário do Windows
        typeof(BranchesViewModel)
            .GetMethod("MostrarErro", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, new object[] { "fatal: detected dubious ownership in repository at 'D:/Projetos/Private/BM2Maga'" });

        Assert.True(vm.TemErro);
        Assert.True(vm.PodeConfiar);
        Assert.Contains("outro usuário", vm.Erro);
        Assert.DoesNotContain("fatal:", vm.Erro);
    }

    [Fact]
    public void Erro_comum_nao_oferece_o_botao_de_confiar()
    {
        var vm = Vm(Path.GetTempPath());

        typeof(BranchesViewModel)
            .GetMethod("MostrarErro", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, new object[] { "fatal: not a git repository" });

        Assert.True(vm.TemErro);
        Assert.False(vm.PodeConfiar);
    }
}
