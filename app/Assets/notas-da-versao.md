## 1.0.0.40 — 09/10/2026
- Busca do Histórico pelo código do commit: quando mais de um commit começa com o que foi digitado, aparecem todos no topo, do mais novo para o mais antigo — antes não aparecia nenhum.
- O código colado com **#** na frente (`#c32497c`) também acha o commit.

## 1.0.0.39 — 07/10/2026
- Histórico: botão **Markdown** mostra a mensagem do commit renderizada (título, lista, link clicável). A escolha é lembrada.
- **Copiar texto** leva a mensagem como foi escrita; **Copiar formatado** leva a renderizada, que cola com título, lista e link no Word, no Outlook e no Teams — e como texto limpo onde a formatação não chega.
- No Markdown renderizado a seleção atravessa os parágrafos: dá para arrastar pela mensagem inteira, usar Ctrl+A e Ctrl+C, ou o clique direito. Vale também para o Leia-me, as Novidades e as descrições de PR e de issue.
- Clicar numa linha com link não abre mais o navegador: só o clique em cima do próprio link, que agora aparece sublinhado.
- As ações do commit viraram botões em pílula, separados da mensagem.

## 1.0.0.38 — 07/10/2026
- **Avisos repaginados**: o cartão ganhou cor por resultado (verde deu certo, vermelho falhou, amarelo ainda está rodando, azul é informação), ícone, título e detalhe. Sucesso e informação somem sozinhos; erro fica até ser fechado.
- Obter, Puxar e Enviar dizem em português o que aconteceu: quantos commits chegaram, quantos foram enviados, ou que já estava em dia. Os erros mais comuns do git vêm explicados, com o que fazer.
- O histórico abre já no commit mais recente, com o detalhe carregado, e mantém o commit escolhido ao recarregar.
- A janela reabre como ficou da última vez: maximizada, ou no mesmo tamanho e centralizada no monitor.

## 1.0.0.37 — 07/10/2026
- **Arrastar e soltar na árvore**: arraste um repositório para cima de outro grupo para mudá-lo de grupo, ou um grupo para cima de outro para virar subgrupo dele. Soltar um grupo na linha **Painel** o leva ao nível principal. O grupo que vai receber fica destacado enquanto você arrasta.
- O cartão de perfil do painel aparece na hora, com o que foi mostrado da última vez, e se atualiza por trás. O botão **Atualizar** busca os dados novos.
- Cores: digitar o código muda a cor imediatamente, sem precisar sair do campo.

## 1.0.0.36 — 07/10/2026
- **Grupos dentro de grupos**: pasta, subpasta e repositórios, em quantos níveis quiser. Crie um subgrupo pelo clique direito no grupo, pelo campo **Dentro de** do diálogo de grupo ou pelo botão **Subgrupo** nas Preferências. O mesmo campo serve para mudar um grupo de lugar.
- O contador, o painel e o lote de um grupo passam a incluir os subgrupos. Excluir um grupo sobe os subgrupos e os repositórios dele um nível.
- **Painel de cores**: a amostra ao lado do código da cor abre uma paleta para escolher. Vale para a cor de destaque, que agora aceita qualquer cor, e para a cor de cada grupo.
- Esteira: passo que o workflow pula de propósito aparece como pulado, em vez de sem informação e com "0s".

## 1.0.0.35 — 07/10/2026
- **Issues** dentro do aplicativo: botão na barra, no cartão do painel e na paleta. Lista as abertas e as fechadas, filtra pelas etiquetas e por texto, e permite criar, editar (título, descrição e etiquetas), comentar, fechar e reabrir.
- Pull requests ganharam as abas **Conversa**, **Commits** e **Arquivos**, com os comentários e as revisões em ordem de tempo e o diff de cada arquivo.
- Na conversa do pull request dá para comentar, aprovar, pedir mudanças e editar o título e a descrição. Seus próprios comentários podem ser editados e excluídos, no PR e nas issues.
- O pull request acompanha o GitHub Actions sozinho: avisa quando está aguardando começar, quantas verificações já terminaram, o passo que está rodando e o tempo de cada uma.
- Alterações: a coluna de arquivos tem largura mínima e os botões não saem mais por cima do painel de diferenças.
- Alterações: num repositório sem pendências o painel fica vazio de verdade, sem o nome do último arquivo que tinha aparecido.

## 1.0.0.34 — 07/10/2026
- **Pull requests** dentro do aplicativo: botão **PRs** na barra e no cartão do painel. Lista os abertos com a situação das verificações, cria o da branch atual, mescla (merge, squash ou rebase) e fecha. Tudo que escreve no GitHub confirma antes.
- **Resolver conflito bloco a bloco**: botão **Resolver** no arquivo em conflito mostra o seu lado, o deles e o resultado, que pode ser editado. O arquivo é salvo no mesmo encoding e já marcado como resolvido.
- **Arrastar uma branch sobre outra**, na tela de branches e no grafo do histórico, para mesclar, fazer rebase ou abrir pull request. O clique direito na branch oferece o mesmo contra a branch atual.
- **Paleta de comandos** com Ctrl+P: digite um pedaço do nome para ir a um repositório, trocar de branch ou rodar uma ação.
- **Pastas de trabalho** (worktrees), pela tela de branches: abrir outra branch em outra pasta, sem guardar o que está em andamento.
- **Em lote**, no painel e no par Origem × Destino: obter, puxar, enviar ou trocar de branch em vários repositórios de uma vez. O puxar em lote só avança; onde há commit local ainda não enviado, o repositório fica como está e avisa.
- **Comparar** duas branches, tags ou commits: quantos commits cada lado tem a mais, os arquivos diferentes e o diff de cada um.

## 1.0.0.33 — 07/10/2026
- Esteira: filtrar por uma situação sem execuções deixa de mostrar o passo a passo da execução anterior por baixo dos avisos, e o aviso passa a dizer que não há execução naquela situação, em vez de dizer que a branch não tem nenhuma.
- Esteira: ao trocar de execução, os passos da anterior somem enquanto os novos carregam.

## 1.0.0.32 — 07/10/2026
- Terminal embutido: texto fosco aparece apagado, como nos outros terminais. A sugestão que o Claude Code oferece para completar com Tab deixa de se confundir com o que você digitou.

## 1.0.0.31 — 06/10/2026
- Com muitos repositórios o aplicativo abre sem travar: o painel aparece na hora e cada repositório mostra a branch e as pendências assim que responde, em vez de todos esperarem o último.
- **Atualizar** clicado durante a varredura automática deixa de ser ignorado, e o painel passa a acompanhar a varredura automática.
- Barra de status: o clique em procurar atualização e em baixar a versão nova pega de primeira (a dica do botão abria por cima dele).
- Procurar atualização avisa quando a consulta falha, em vez de dizer que você já está na versão mais recente.

## 1.0.0.30 — 06/10/2026
- Preferências: nova seção **Notas da versão**, com o que mudou em cada versão. Ela vem dentro do aplicativo e funciona sem internet.

## 1.0.0.29 — 06/10/2026
- Diff no desenho do GitHub: realce de sintaxe (Delphi, C#, XML, SQL, JSON e outras), coluna do `+` e `−` separada do código, número da linha alterada em destaque e faixa em cada bloco.
- Contador `+N −N` com os cinco quadrinhos no cabeçalho do arquivo e botão **Arquivo inteiro**, que mostra o arquivo todo com as alterações marcadas.
- **Diff externo**: informe nas Preferências a pasta de instalação (ou o `.exe`) da sua ferramenta — Beyond Compare, WinMerge, Meld, KDiff3, P4Merge, TortoiseGitMerge e VS Code são reconhecidos — e abra pelo botão do painel de diferenças ou pelo clique direito.
- Preferências separadas por seção, com a lista à esquerda.

## 1.0.0.28 — 06/10/2026
- **Ignorar alterações** só nesta máquina: por arquivo, pelos selecionados ou todos, sem mexer no repositório. **Ver ignorados** traz de volta.
- Arquivo novo pode ir para o `.gitignore` pelo clique direito.
- Windows 10: a barra de título passa a seguir o tema escuro.

## 1.0.0.27 — 02/10/2026
- **Enviar** funciona em branch vinculada a outra de nome diferente (feature criada a partir de `origin/develop`): cria a remota de mesmo nome e refaz o vínculo.

## 1.0.0.26 — 02/10/2026
- Alterações: seleção de vários arquivos com Ctrl/Shift para preparar ou descartar só eles, **Reverter** (voltar ao último commit ou ao remoto), commit com Ctrl+Enter e emendar já com a mensagem anterior.
- Conflitos: **Meu**, **Deles** ou resolvido por arquivo, e faixa de merge/rebase em andamento com Continuar e Abortar.
- Diff: preparar ou remover só as linhas escolhidas.
- Histórico: copiar hash, criar branch ou tag no commit, cherry-pick, reverter commit, reset (soft, mixed, hard), busca por mensagem, autor ou hash e reorganizar commits (rebase interativo sem editor).
- Desfazer a última ação, histórico do arquivo e autoria das linhas (blame).
- Opções de trazer todas as tags e de sincronizar todas as branches em Obter e Puxar.

## 1.0.0.25 — 02/10/2026
- Consultas ao GitHub são refeitas uma vez quando demoram, e a mensagem de erro sai em português.

## 1.0.0.24 — 02/10/2026
- **Clonar** um repositório do GitHub direto pela tela de adicionar.

## 1.0.0.23 — 02/10/2026
- Caminhos com acento aparecem certos e podem ser preparados.
- Link do remoto mais limpo, sem botões ao lado.

## 1.0.0.22 — 02/10/2026
- O diff mostra os acentos de arquivos em ANSI (Windows-1252), como os fontes Delphi.

## 1.0.0.21 — 01/10/2026
- **Terminal** Git Bash embutido no aplicativo, com botão para abri-lo em janela separada.

## 1.0.0.20 — 01/10/2026
- A tela de branches abre com as pastas recolhidas.

## 1.0.0.19 — 01/10/2026
- Cartão de perfil compacto, com o gráfico de contribuições.
- Grupos recolhíveis no painel.

## 1.0.0.18 — 01/10/2026
- Cartões do painel com o contorno inteiro.
- A contagem de linhas considera só os seus commits.

## 1.0.0.17 — 01/10/2026
- Várias contas do GitHub, com a conta escolhida por repositório.
- Foto no cartão de perfil e painel que não pisca ao atualizar.

## 1.0.0.16 — 01/10/2026
- O cartão do painel expande para mostrar a esteira.
- A pílula da branch não some mais ao recolher e abrir o grupo.

## 1.0.0.15 — 01/10/2026
- Pílula com a branch atual em cada repositório da árvore.
- Tela de branches organizada por pasta, com suporte a git-flow.

## 1.0.0.13 — 30/09/2026
- Esteira mostra as últimas execuções, recolhe o resto e filtra por situação.
- Painel separado por grupo, com botão de atualizar discreto.

## 1.0.0.12 — 30/09/2026
- O aplicativo abre no **Painel**, com cartões clicáveis que preenchem a largura da janela.

## 1.0.0.11 — 30/09/2026
- **Novidades**: changelog dentro do aplicativo, lido das releases do GitHub.
- Aba **Leia-me** com o README do repositório renderizado, incluindo tabelas e imagens.
- Cartão de perfil no painel geral e contagem de pull requests abertos em cada cartão.
- Estilo minimalista da árvore, com a opção de voltar às pílulas.
- A esteira passa a mostrar também os builds disparados por tag.

## 1.0.0.10 — 30/09/2026
- **Painel** de repositórios por grupo e para o workspace inteiro: branch, pendências e situação da esteira em cartões.
- A atualização automática deixou de piscar o botão "Atualizar todos".

## 1.0.0.9 — 30/09/2026
- O caminho do modo portátil não some mais da tela quando a troca falha.
- Rodapé alinhado, e o ícone de atualização acende quando há versão nova.

## 1.0.0.8 — 30/09/2026
- Ícone no rodapé, ao lado da versão, para procurar atualização na hora.

## 1.0.0.7 — 30/09/2026
- A esteira se atualiza sozinha enquanto a janela está aberta, sem perder a rolagem nem a seleção.

## 1.0.0.6 — 30/09/2026
- Botões do corpo dos diálogos menores e discretos.
- Barra de rolagem fina.

## 1.0.0.5 — 30/09/2026
- **Modo portátil** opcional: guardar as configurações junto do executável.

## 1.0.0.4 — 30/09/2026
- A atualização automática reconhece o executável de arquivo único mesmo quando ele foi renomeado.
- **Procurar agora** nas Preferências.

## 1.0.0.3 — 30/09/2026
- Rodapé de Configurar Repositório centralizado.

## 1.0.0.2 — 30/09/2026
- Ctrl+F leva ao filtro de repositórios; Esc limpa.
- **Atualização automática**: o aplicativo avisa quando sai versão nova, baixa, troca e reabre.
- A versão aparece na barra de status.

## 1.0.0.1 — 30/09/2026
- Primeira versão publicada: grupos na sidebar, par Origem × Destino, alterações com diff e preparação por bloco, histórico com grafo, branches, stash e esteira do GitHub Actions.
- Dois executáveis por release: o que depende do .NET 8 e o autossuficiente.
