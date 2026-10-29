# What's new in MouseModes

[Português](CHANGELOG.md) · **English**

Each version has a `## X.Y.Z` section, the same as in `CHANGELOG.md`. The GitHub Release shows both, Portuguese first.

## 1.9.0

### New

- **Privacy** (Settings → Privacy): a new page gathers everything that leaves the PC (the new version check and the
  network X-ray ping) and how to turn it off. The two new version switches moved there from the General page, and
  there is a new "Measure the network in the X-ray" switch: off, the app sends no ping. The installer shows the same
  notice before installing.

## 1.8.0

### New

- **Per-game resolution** (Settings → Games → Own settings): pick 1280×960, 1440×1080, 1024×768 or 1280×1024 for a
  game, and the main monitor switches to it, at the highest refresh rate it supports, while the game is open in
  Performance. When the game closes, Performance ends or the app quits, it goes back to the usual one. The change is
  not saved in Windows (restarting also undoes it) and, if the app crashes, it undoes it when it opens again. If the
  monitor does not support the resolution, nothing changes and the reason goes to Diagnostics.

## 1.7.1

### Fixes

- Self-update: anyone who leaves Performance on without a game open is told that the new version is already downloaded
  and installs when they leave it (before, it waited silently, forever). The automatic check downloads nothing with a
  game open. A loose copy of the exe (outside the installed folder) only notifies, instead of installing into another
  folder every day. If the install fails with the old app still open, the window no longer opens by itself.
- Calm background: a game opened through a middle process (the anti-cheat bootstrapper, the EA or Rockstar launcher)
  from a calmed Steam also goes back to Normal, even if the middle process has already closed; before, it could stay
  below normal. A calmed program that is added to the games list goes back to normal right away. The original priority
  is no longer given back to another process that reused the game's PID.
- Network X-ray: a single long outage (e.g. 30 s without internet) no longer comes out as "network stable". Packet
  loss without spikes no longer shows as "0 spikes". The Wi-Fi dropping mid-match counts as a local problem, and the
  end of the match no longer freezes the tray when the network does not answer. Whoever was busy at the time of a
  spike shows as "read/wrote": those bytes are disk, and Windows does not split them from network per program.
- Match X-ray: an off-cycle check (when saving Settings) no longer inflates the CPU peak. A report taller than the
  screen (high Windows scaling) scrolls.
- The polling meter, click test and real DPI opened at the same time no longer steal the mouse from each other, and
  closing one no longer turns the others off. On remote desktop and in virtual machines, absolute movement is no
  longer added up as sensor counts.
- Real DPI: changing the mouse DPI resets the runs; the value is only saved with two or more runs that agree; alt-tab
  mid-drag ends the run; quitting the app with the screen open also saves. The converter now shows the DPI entered in
  the measurement, without overwriting it on close.

## 1.7.0

### New

- **Network X-ray** (in the Match X-ray): during the match, one ping per second to the router and to 1.1.1.1
  (Cloudflare), on its own thread and without administrator rights. At the end, the report shows ping, jitter and
  loss, the three worst spikes (e.g. at 23 min, 210 ms for 3 s) and where they came from: the Wi-Fi or your network,
  when the router got worse too, or the internet, when everything was fine up to the router. It also points at who was
  using a lot of data at the time (e.g. OneDrive with 120 MB). With a VPN on, it measures the router of the real
  network adapter and warns that the spikes may come from the VPN. If the router does not answer ping, it says the
  Wi-Fi and the internet cannot be told apart.
- **Real mouse DPI** (sensitivity converter → Measure the real DPI, or tray icon → Measure the real DPI): with a ruler
  on the desk, you drag the mouse 4 in and the app counts the sensor reports through Raw Input. It shows the true DPI,
  how far it is from the advertised one (e.g. 800 in the mouse software, 763 on the ruler, 4.6% lower) and how much
  the runs varied. The converter then shows the turn (cm/360) with the real DPI while the mouse stays at that DPI.

## 1.6.0

### New

- **Right cores** (Settings → Turbo → Calm background): on hybrid Intel CPUs (12th gen or newer), the calmed programs
  also move to the efficiency cores (E), leaving the performance cores (P) free for the game, like Process Lasso does.
  It uses Windows CPU sets, the gentle way (it is not affinity), without administrator rights, and it is undone when
  leaving Performance, on alt-tab and after a crash. The game is not touched. Nothing changes on other CPUs.

### Fixes

- Calm background: Windows passes below normal priority on to whatever a calmed program opens. A game opened by a
  calmed Steam started at low priority (and, with high priority on, went back to it when leaving Performance), and new
  browser tabs stayed below normal afterwards. Now the game goes back to normal right away, and the rest goes back
  together with the program that opened it.

## 1.5.0

### New

- **Updates by itself** (Settings → General, on by default): when a new version is out, the app downloads the
  installer from the Release, checks the SHA-256 published there, that it is an executable and that it is MouseModes
  of the right version, and installs it silently outside games. The app closes, installs and opens again in the tray
  with the same settings. If anything does not match, it does not install and shows the old notice, to download by
  hand. It works from this version on: anyone on 1.4.0 or earlier updates by hand one last time.
- **Match X-ray**: while a game from the list is open in Performance, the app notes every 3 s how much CPU and data
  each background program used. When the game closes, a notice shows what weighed the most (e.g. chrome averaging 18%
  with 61% peaks, OneDrive with 2.1 GB) and, in one click, adds the program to the auto-close list or turns on calm
  background. It stays in the tray icon → Last match X-ray. It only reads, without administrator rights; it measures
  what weighed, not FPS.
- **Sensitivity converter** (main screen → Sensitivity, or the tray icon): type your game's sensitivity and DPI and
  see the cm/360 (how far the mouse travels for a full turn) and the value that gives the same aim in CS2, Apex, TF2,
  Valorant, Overwatch 2 and Call of Duty. Changed the DPI? Enter the new one and the app recalculates each game's
  sensitivity so your aim does not change. For another game, just enter its yaw.
- **Calm background** (Settings → Turbo, off by default): instead of closing them, keeps the other open programs at
  below normal priority and, on Windows 11, in efficiency mode. It helps with stutters when the CPU is maxed out
  (browser, Steam downloading, OneDrive syncing). It does not touch the game, the program in the foreground (on
  alt-tab it goes back to normal), Windows processes, or voice, music, streaming, overlay, mouse software and
  anti-cheat (Discord, Spotify, OBS, NVIDIA, G HUB, Razer etc.). If the app crashes with the background calmed, it
  undoes it when it opens again.

## 1.4.0

### New

- **Switch by command:** `MouseModes.exe --performance`, `--daily` or `--toggle` switches the mode of the running
  app, like the hotkey. Use it for a Stream Deck button, a desktop shortcut or Task Scheduler. If the app is not
  running, it opens in the tray already in that mode.
- **Diagnostics log in English:** with the app in English, the log messages are in English too (WARN and ERROR
  instead of AVISO and ERRO). What was already written to `log.txt` stays as it was.

## 1.3.0

### New

- **Aim and DPI test** (button on the main screen and in the tray icon): 20 s of targets. The app measures whether you
  overshoot the target, stop short, how many corrections you make and how precise the click is, and suggests how much
  to raise or lower the DPI in your mouse software. The rounds stay side by side to compare DPIs.
- **English**: every screen follows the Windows language (Portuguese or English), with a choice in the tray icon →
  Idioma / Language. Diagnostics log messages stay in Portuguese. README in English (`README.en.md`).

### Fixes

- With the monitor warning off, the main screen said to turn it on in Settings → Turbo; the option is in
  Settings → General.

## 1.2.0

### New

- **Find games** (Settings → Games → Find): lists the games installed through Steam and Epic, plus the open programs,
  to add several at once.
- **Game mouse only with the game on screen** (Settings → Performance, off by default): on alt-tab and on the desktop,
  the mouse goes back to the Daily speed and acceleration.
- **"Previous" power plan** in Daily: when leaving Performance, it goes back to the plan that was active before
  (including the manufacturer's or a custom one), and no longer always to Balanced. It is the default on new installs.
- **Custom hotkey** (Settings → General): any combination with Ctrl or Alt. If another program already uses it, the app
  tries the previous options and tells you which one it got.
- **Export and import settings** (Settings → General), to take games, profiles and Turbo to another PC.
- **Windows for games** (in Diagnostics): shows Game Mode, background recording, GPU scheduling and optimizations for
  windowed games, with a button to the right Windows page. The app changes none of them on its own.
- **New version notice**, once a day and never during a game (Settings → General to turn it off, or tray icon → Check
  for a new version).

### Fixes

- Blocking the Windows key no longer delays the keyboard during a game.
- When it opens, the app no longer rewrites the mouse or the power plan. Daily mode now follows what you change in
  Windows itself.
- Switching modes no longer freezes the window while the power plan changes, and a stuck power plan no longer holds
  up the app.
- On PCs that hide power plans, the app no longer creates a new Ultimate Performance copy every time, and Balanced is
  found directly.
- Settings are no longer lost if the PC shuts down in the middle of saving them.

### Download

- The Release has the installer SHA-256 (in the text and in the `MouseModes-Setup.exe.sha256` file), to check the
  download.

## 1.1.1

- The Release has only the installer, with a fixed name: the README button always downloads the latest version.

## 1.1.0

- First version: Performance and Daily modes, automatic switch per game, per-game profiles, Turbo (priority, Windows
  key, Sticky Keys, notifications, closing programs), monitor refresh rate warning, polling meter, click test,
  Diagnostics and an installer that needs no administrator rights.
