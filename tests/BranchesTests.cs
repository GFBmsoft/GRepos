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
        public Task ShowEsteiraAsync(string s, string b, string u, string n, int v) => Task.CompletedTask;
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

    private static BranchItemViewModel Item(string nome, bool remota = false, bool head = false) => new()
    {
        Branch = new Branch { Name = nome, IsRemote = remota, IsHead = head },
        Tipo = GitFlow.Classificar(GitFlow.SemRemoto(nome, remota)),
    };

    [Fact]
    public void Agrupa_por_pasta_com_as_principais_no_topo()
    {
        var grupos = BranchesViewModel.Agrupar(new[]
        {
            Item("fix/CorrecaoTbEdit"), Item("feat/cartaopix"), Item("master"),
            Item("develop", head: true), Item("feat/AjustaVisual"), Item("avulsa"),
        });

        Assert.Equal(new[] { "principais", "feat/", "fix/", "sem pasta" }, grupos.Select(g => g.Titulo));
        Assert.Equal("develop", grupos[0].Itens[0].Name);          // a atual primeiro
        Assert.Equal(new[] { "AjustaVisual", "cartaopix" }, grupos[1].Itens.Select(i => i.Curto));
        Assert.Equal("Purple", grupos[1].Cor);
        Assert.Equal("Orange", grupos[2].Cor);
    }

    [Fact]
    public void Remota_agrupa_pela_pasta_sem_o_nome_do_remoto()
    {
        var grupos = BranchesViewModel.Agrupar(new[] { Item("origin/fix/6329_email", remota: true), Item("origin/develop", remota: true) });

        Assert.Equal(new[] { "principais", "fix/" }, grupos.Select(g => g.Titulo));
        Assert.Equal("6329_email", grupos[1].Itens.Single().Curto);
    }

    [Fact]
    public void Grupo_mantem_o_estado_que_o_usuario_deixou_ao_filtrar()
    {
        var antes = BranchesViewModel.Agrupar(new[] { Item("feat/a"), Item("fix/b") });
        antes.Single(g => g.Titulo == "feat/").AlternarCommand.Execute(null); // abre

        var depois = BranchesViewModel.Agrupar(new[] { Item("feat/a"), Item("fix/b") }, antes);

        Assert.False(depois.Single(g => g.Titulo == "feat/").Recolhido);
        Assert.True(depois.Single(g => g.Titulo == "fix/").Recolhido);
    }

    [Fact]
    public void Pastas_chegam_recolhidas_menos_as_principais_e_a_da_branch_atual()
    {
        var grupos = BranchesViewModel.Agrupar(new[]
        {
            Item("master"), Item("develop"), Item("feat/a"), Item("feat/b"),
            Item("fix/c", head: true), Item("imp/d"), Item("avulsa"),
            Item("origin/feat/a", remota: true),
        });

        bool Recolhido(string titulo) => grupos.Single(g => g.Titulo == titulo).Recolhido;

        Assert.False(Recolhido("principais"));
        Assert.False(Recolhido("fix/"));       // é onde você está
        Assert.True(Recolhido("feat/"));
        Assert.True(Recolhido("imp/"));
        Assert.True(Recolhido("sem pasta"));
    }

    [Fact]
    public async Task Git_flow_le_a_configuracao_do_sourcetree_e_cria_feature_a_partir_da_develop()
    {
        var dir = await RepoComBranches();
        try
        {
            // o que o SourceTree grava ao inicializar (EV04): os prefixos dos projetos
            await GitService.RunAsync(dir, new[] { "config", "gitflow.branch.master", "main" });
            await GitService.RunAsync(dir, new[] { "config", "gitflow.branch.develop", "develop" });
            await GitService.RunAsync(dir, new[] { "config", "gitflow.prefix.feature", "imp/" });
            await GitService.RunAsync(dir, new[] { "config", "gitflow.prefix.hotfix", "defeito/" });
            await GitService.RunAsync(dir, new[] { "branch", "develop" });

            var vm = Vm(dir);
            vm.PedirTexto = (_, _) => Task.FromResult<string?>("estoque novo");
            await vm.CarregarAsync();

            Assert.True(vm.Fluxo.Inicializado);
            Assert.Equal("imp/", vm.Fluxo.Feature);

            await vm.NovaFeatureCommand.ExecuteAsync(null);

            Assert.Equal("imp/estoque-novo", vm.BranchAtual);
            Assert.True(vm.PodeFinalizar);
        }
        finally { Limpar(dir); }
    }

    [Fact]
    public async Task Git_flow_inicializa_criando_a_develop_e_finaliza_a_feature()
    {
        var dir = await RepoComBranches();
        try
        {
            var vm = Vm(dir);
            vm.EditarFluxo = atual => Task.FromResult<GitFlowConfig?>(atual with { Master = "main" });
            vm.PedirTexto = (_, _) => Task.FromResult<string?>("relatorio");
            vm.Confirmar = (_, _) => Task.FromResult(true);
            await vm.CarregarAsync();
            Assert.False(vm.Fluxo.Inicializado);

            await vm.ConfigurarFluxoCommand.ExecuteAsync(null);
            Assert.True(vm.Fluxo.Inicializado);
            Assert.Contains(vm.Locais, b => b.Name == "develop");

            await vm.NovaFeatureCommand.ExecuteAsync(null);
            Assert.Equal("feat/relatorio", vm.BranchAtual);

            File.WriteAllText(Path.Combine(dir, "b.txt"), "novo\n");
            await GitService.RunAsync(dir, new[] { "add", "." });
            await GitService.RunAsync(dir, new[] { "commit", "-qm", "relatório" });

            await vm.FinalizarCommand.ExecuteAsync(null);

            Assert.False(vm.TemErro, vm.Erro);
            Assert.Equal("develop", vm.BranchAtual);
            Assert.DoesNotContain(vm.Locais, b => b.Name == "feat/relatorio");
            Assert.True(File.Exists(Path.Combine(dir, "b.txt")));
        }
        finally { Limpar(dir); }
    }
}

public class GitFlowTests
{
    [Theory]
    [InlineData("master", TipoBranch.Principal)]
    [InlineData("main", TipoBranch.Principal)]
    [InlineData("develop", TipoBranch.Develop)]
    [InlineData("feat/cartaopix", TipoBranch.Feature)]
    [InlineData("feature/6453-usar-boleto-online", TipoBranch.Feature)]
    [InlineData("imp/x", TipoBranch.Feature)]
    [InlineData("fix/FNOBS", TipoBranch.Fix)]
    [InlineData("bugfix/5736", TipoBranch.Fix)]
    [InlineData("defeito/x", TipoBranch.Fix)]
    [InlineData("release/5.6.0.0", TipoBranch.Release)]
    [InlineData("OFX", TipoBranch.Outra)]
    public void Classifica_pelos_nomes_usados_nos_projetos(string nome, TipoBranch tipo) =>
        Assert.Equal(tipo, GitFlow.Classificar(nome));

    [Fact]
    public void Prefixo_configurado_vale_mesmo_fora_da_lista_conhecida()
    {
        var cfg = new GitFlowConfig { Feature = "tarefa/", Master = "producao" };

        Assert.Equal(TipoBranch.Feature, GitFlow.Classificar("tarefa/x", cfg));
        Assert.Equal(TipoBranch.Principal, GitFlow.Classificar("producao", cfg));
    }

    [Fact]
    public void Nome_completo_nao_duplica_o_prefixo()
    {
        var cfg = new GitFlowConfig();
        Assert.Equal("feat/estoque", GitFlow.NomeCompleto(TipoBranch.Feature, "estoque", cfg));
        Assert.Equal("feat/estoque", GitFlow.NomeCompleto(TipoBranch.Feature, "feat/estoque", cfg));
        Assert.Equal("fix/tela-branca", GitFlow.NomeCompleto(TipoBranch.Fix, "tela branca", cfg));
    }

    [Fact]
    public void Finalizar_fix_entra_na_principal_e_na_develop_e_so_apaga_no_fim()
    {
        var passos = GitFlow.PassosFinalizar("fix/FNOBS", new GitFlowConfig());
        var texto = GitFlow.Descrever(passos).Split('\n');

        Assert.StartsWith("git checkout master", texto[0]);
        Assert.StartsWith("git merge --no-ff fix/FNOBS", texto[1]);
        Assert.StartsWith("git checkout develop", texto[2]);
        Assert.Equal("git branch -d fix/FNOBS", texto[^1]);
        Assert.DoesNotContain(texto, l => l.StartsWith("git tag"));
    }

    [Fact]
    public void Finalizar_release_cria_a_tag_da_versao()
    {
        var passos = GitFlow.PassosFinalizar("release/5.6.0.0", new GitFlowConfig { VersionTag = "v" });
        Assert.Contains(passos, p => p[0] == "tag" && p.Contains("v5.6.0.0"));
    }

    [Fact]
    public void Principal_e_develop_nao_se_finalizam()
    {
        Assert.Empty(GitFlow.PassosFinalizar("master", new GitFlowConfig()));
        Assert.Empty(GitFlow.PassosFinalizar("develop", new GitFlowConfig()));
    }
}
