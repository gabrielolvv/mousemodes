# Novidades do MouseModes

**Português** · [English](CHANGELOG.en.md)

Cada versão tem uma seção `## X.Y.Z`, aqui e no `CHANGELOG.en.md`. A Release do GitHub usa a seção da própria versão
dos dois arquivos como texto, primeiro em português e depois em inglês.

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
