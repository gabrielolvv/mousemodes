# Novidades do MouseModes

**Português** · [English](CHANGELOG.en.md)

Cada versão tem uma seção `## X.Y.Z`, aqui e no `CHANGELOG.en.md`. A Release do GitHub usa a seção da própria versão
dos dois arquivos como texto, primeiro em português e depois em inglês.

## Próxima versão

### Novidades

- **Processador na partida** (Raio-X): velocidade média em relação à nominal e os trechos em que o processador foi
  freado por calor, limite de energia ou bateria. As travadinhas nesses trechos ganham essa causa.

## 1.11.0

### Novidades

- **Limite de FPS por jogo** (Ajustes → Jogos, beta): grava o limite no limitador do driver da placa (NVIDIA ou Intel
  Arc), com AUTO = taxa do monitor menos 3. Gravado ao salvar os Ajustes, relido para confirmar e válido da próxima
  vez que o jogo abrir. Ainda sem teste numa placa NVIDIA de verdade: o `tools/fps-cap-check.ps1` faz esse teste.
- **Modo de energia no notebook**: com o plano Equilibrado no Performance, o modo de energia do Windows vai para
  Melhor desempenho na tomada e volta ao sair (e ao abrir de novo, se o app cair). Quando o PC não aceita o Alto
  desempenho ou o Desempenho máximo, o app agora fica no Equilibrado com Melhor desempenho em vez de só avisar. E
  avisa quando um jogo abre na bateria.

## 1.10.0

### Novidades

- **Caça-travadinhas** (Raio-X da partida; Ajustes → Geral → Permitir): o FPS médio, o 1% low e as travadinhas da
  partida, cada uma com o que se destacou na hora (processador, disco ou internet), ou o aviso de que pode ser do
  próprio jogo. Lê os eventos de Present do DirectX 9 ao 12 pelo rastreamento do Windows, sem entrar no jogo. Precisa
  de uma permissão única: o botão Permitir põe a conta no grupo "Performance Log Users" (pede o administrador uma vez)
  e vale depois de sair e entrar de novo.

## 1.9.0

### Novidades

- **Privacidade** (Ajustes → Privacidade): uma página nova junta tudo o que sai do PC (a consulta de versão nova e o
  ping do Raio-X da rede) e como desligar. As duas chaves de versão nova saíram da página Geral para lá, e entrou a
  chave "Medir a rede no Raio-X": desligada, o app não manda nenhum ping. O instalador mostra o mesmo aviso antes de
  instalar.

## 1.8.0

### Novidades

- **Resolução por jogo** (Ajustes → Jogos → Configurações próprias): escolha 1280×960, 1440×1080, 1024×768 ou
  1280×1024 para um jogo, e o monitor principal vai para ela, na maior taxa que aceita, com o jogo aberto no
  Performance. Ao fechar o jogo, sair do Performance ou fechar o app, volta à de sempre. A troca não é gravada no
  Windows (reiniciar também desfaz) e, se o app travar, ele desfaz ao abrir de novo. Se o monitor não aceitar a
  resolução, nada muda e o motivo fica no Diagnóstico.

## 1.7.1

### Correções

- Atualização sozinha: quem deixa o Performance ligado sem jogo aberto é avisado de que a versão nova já foi baixada e
  instala ao sair dele (antes ela esperava em silêncio, para sempre). A checagem automática não baixa nada com jogo
  aberto. Uma cópia solta do exe (fora da pasta instalada) só avisa, em vez de instalar em outra pasta todo dia. Se a
  instalação falhar com o app antigo ainda aberto, a janela não abre sozinha.
- Segundo plano calmo: um jogo aberto por um processo do meio (o bootstrapper do anti-cheat, o launcher da EA ou da
  Rockstar) a partir da Steam acalmada também volta ao Normal, mesmo que o processo do meio já tenha fechado; antes
  ele podia ficar abaixo do normal. Um programa acalmado que entra na lista de jogos volta ao normal na hora. A
  prioridade original não é mais devolvida a outro processo que reaproveitou o PID do jogo.
- Raio-X da rede: uma queda única e longa (ex.: 30 s sem internet) não sai mais como "rede estável". Perda de pacotes
  sem pico não aparece mais como "0 picos". O Wi-Fi caindo no meio da partida conta como problema local, e o fim da
  partida não trava mais a bandeja quando a rede não responde. Quem estava em atividade na hora do pico aparece como
  "lia/gravava": esses bytes são de disco, e o Windows não os separa da rede por programa.
- Raio-X da partida: uma checagem fora do ritmo (ao salvar os Ajustes) não infla mais o pico de processador. Um
  relatório maior que a tela (escala do Windows alta) rola.
- Medidor de polling, teste de clique e DPI real abertos ao mesmo tempo não roubam mais o mouse um do outro, e fechar
  um não desliga os outros. Na área de trabalho remota e em máquina virtual, o movimento absoluto não é mais somado
  como contagem do sensor.
- DPI real: trocar o DPI do mouse zera as medições; o valor só é guardado com duas ou mais medições que batem entre
  si; alt-tab no meio do arrasto termina a medição; fechar o app com a tela aberta também guarda. O conversor passa a
  mostrar o DPI informado na medição, sem sobrescrevê-lo ao fechar.

## 1.7.0

### Novidades

- **Raio-X da rede** (no Raio-X da partida): durante a partida, um ping por segundo ao roteador e ao 1.1.1.1
  (Cloudflare), numa thread própria e sem administrador. No fim, o relatório mostra ping, jitter e perda, os três
  piores picos (ex.: aos 23 min, 210 ms por 3 s) e de onde vieram: do Wi-Fi ou da sua rede, quando o roteador também
  piorou, ou da internet, quando até o roteador estava tudo bem. Aponta também quem usava muitos dados na hora (ex.:
  OneDrive com 120 MB). Com uma VPN ligada, mede o roteador da placa de verdade e avisa que os picos podem vir dela.
  Se o roteador não responde a ping, diz que não dá para separar Wi-Fi de internet.
- **DPI real do mouse** (conversor de sensibilidade → Medir o DPI real, ou ícone da bandeja → Medir o DPI real): com
  uma régua na mesa, você arrasta o mouse 10 cm e o app conta as leituras do sensor pelo Raw Input. Mostra o DPI de
  verdade, a diferença para o anunciado (ex.: 800 no programa do mouse, 763 na régua, 4,6% a menos) e quanto as
  medições variaram. O conversor passa a mostrar a volta (cm/360) com o DPI real enquanto o mouse estiver naquele DPI.

## 1.6.0

### Novidades

- **Núcleos certos** (Ajustes → Turbo → Segundo plano calmo): nos processadores Intel híbridos (12ª geração ou mais
  nova), os programas acalmados também vão para os núcleos de eficiência (E), e os de desempenho (P) ficam livres para
  o jogo, como no Process Lasso. Usa os CPU sets do Windows, o jeito brando (não é afinidade), sem administrador, e
  desfaz ao sair do Performance, no alt-tab e depois de um travamento. O jogo não é mexido. Nos outros processadores
  nada muda.

### Correções

- Segundo plano calmo: o Windows passa a prioridade abaixo do normal para o que um programa acalmado abre. Um jogo
  aberto pela Steam acalmada nascia com prioridade baixa (e, com a prioridade alta ligada, voltava a ela ao sair do
  Performance), e abas novas do navegador ficavam abaixo do normal depois. Agora o jogo volta ao normal na hora, e o
  resto volta junto com o programa que o abriu.

## 1.5.0

### Novidades

- **Atualiza sozinho** (Ajustes → Geral, ligado por padrão): quando sai versão nova, o app baixa o instalador da
  Release, confere o SHA-256 publicado nela, que é um executável e que é o MouseModes da versão certa, e instala em
  silêncio fora do jogo. O app fecha, instala e abre de novo na bandeja com as mesmas configurações. Se algo não
  bater, ele não instala e mostra o aviso de antes, para baixar à mão. Vale a partir desta versão: quem está na 1.4.0
  ou antes atualiza à mão uma última vez.
- **Raio-X da partida**: enquanto um jogo da lista estiver aberto no Performance, o app anota a cada 3 s quanto
  processador e quantos dados cada programa em segundo plano usou. Ao fechar o jogo, um aviso mostra quem mais pesou
  (ex.: chrome com média de 18% e pico de 61%, OneDrive com 2,1 GB) e, com um clique, põe o programa na lista para
  fechar ao jogar ou liga o segundo plano calmo. Fica no ícone da bandeja → Raio-X da última partida. Só lê, sem
  administrador; mede quem pesou, não o FPS.
- **Conversor de sensibilidade** (tela inicial → Sensibilidade, ou ícone da bandeja): digite a sensibilidade e o DPI
  do seu jogo e veja o cm/360 (quanto o mouse anda para uma volta completa) e o valor que dá a mesma mira em CS2,
  Apex, TF2, Valorant, Overwatch 2 e Call of Duty. Trocou o DPI? Informe o novo e o app recalcula a sensibilidade de
  cada jogo para a mira não mudar. Para outro jogo, basta informar o yaw dele.
- **Segundo plano calmo** (Ajustes → Turbo, desligado por padrão): em vez de fechar, deixa os outros programas abertos
  com prioridade abaixo do normal e, no Windows 11, no modo de eficiência. Ajuda nas travadinhas quando o processador
  está no limite (navegador, Steam baixando, OneDrive sincronizando). Não mexe no jogo, no programa em primeiro plano
  (no alt-tab ele volta ao normal), nos processos do Windows nem em voz, música, transmissão, overlay, software do
  mouse e anti-cheat (Discord, Spotify, OBS, NVIDIA, G HUB, Razer etc.). Se o app travar com o segundo plano acalmado,
  ele desfaz ao abrir de novo.

## 1.4.0

### Novidades

- **Troca por comando:** `MouseModes.exe --performance`, `--daily` ou `--toggle` troca o modo do app aberto, como o
  atalho. Serve para um botão do Stream Deck, um atalho na área de trabalho ou o Agendador de Tarefas. Se o app não
  estiver aberto, ele abre na bandeja já no modo pedido.
- **Registro do Diagnóstico em inglês:** com o app em inglês, as mensagens do registro também saem em inglês
  (WARN e ERROR no lugar de AVISO e ERRO). O que já estava gravado no `log.txt` continua como foi escrito.

## 1.3.0

### Novidades

- **Teste de mira e DPI** (botão na tela inicial e no ícone da bandeja): alvos por 20 s. O app mede se você passa do
  alvo, se para antes, quantas correções faz e a precisão do clique, e sugere quanto subir ou baixar o DPI no programa
  do seu mouse. As rodadas ficam lado a lado para comparar DPIs.
- **Inglês**: todas as telas seguem o idioma do Windows (português ou inglês), com escolha em ícone da bandeja →
  Idioma / Language. As mensagens do registro do Diagnóstico continuam em português. README em inglês
  (`README.en.md`).

### Correções

- O aviso de monitor desligado mandava ligar em Ajustes → Turbo; a opção fica em Ajustes → Geral.

## 1.2.0

### Novidades

- **Buscar jogos** (Ajustes → Jogos → Buscar): lista os jogos instalados pela Steam e pela Epic, além dos programas
  abertos, para incluir vários de uma vez.
- **Mouse do jogo só com o jogo na tela** (Ajustes → Performance, desligado por padrão): no alt-tab e no desktop, o
  mouse volta à velocidade e à aceleração do Dia a dia.
- **Plano de energia "Anterior"** no Dia a dia: ao sair do Performance, volta ao plano que estava ativo antes (inclusive
  o do fabricante ou um personalizado), e não mais sempre ao Equilibrado. É o padrão em instalações novas.
- **Atalho personalizável** (Ajustes → Geral): qualquer combinação com Ctrl ou Alt. Se outro programa já usar, o app
  tenta as opções de antes e avisa qual ficou.
- **Exportar e importar a configuração** (Ajustes → Geral), para levar jogos, perfis e Turbo para outro PC.
- **Windows para jogos** (no Diagnóstico): mostra o Modo de Jogo, a gravação em segundo plano, o agendamento de GPU e as
  otimizações para jogos em janela, com um botão para a tela certa do Windows. O app não muda nada disso sozinho.
- **Aviso de versão nova**, uma vez por dia e nunca durante o jogo (Ajustes → Geral para desligar, ou ícone da
  bandeja → Procurar versão nova).

### Correções

- O bloqueio da tecla Windows não atrasa mais o teclado durante o jogo.
- Ao abrir, o app não regrava mais o mouse nem o plano de energia. O Dia a dia passa a acompanhar o que você muda no
  próprio Windows.
- A troca de modo não congela mais a janela enquanto o plano de energia é trocado, e um plano de energia que trava não
  segura mais o app.
- Em PCs que escondem planos de energia, o app não cria mais uma cópia nova do Desempenho máximo a cada vez, e o
  Equilibrado é achado direto.
- A configuração não se perde se o PC desligar no meio da gravação.

### Download

- A Release traz o SHA-256 do instalador (no texto e no arquivo `MouseModes-Setup.exe.sha256`), para conferir o
  download.

## 1.1.1

- A Release passa a ter só o instalador, com nome fixo: o botão do README sempre baixa a versão mais recente.

## 1.1.0

- Primeira versão: modos Performance e Dia a dia, troca automática por jogo, perfis por jogo, Turbo (prioridade, tecla
  Windows, Teclas de Aderência, notificações, fechar programas), aviso de taxa do monitor, medidor de polling, teste de
  clique, Diagnóstico e instalador sem administrador.
