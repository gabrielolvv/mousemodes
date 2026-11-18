# What's new in MouseModes

[Português](CHANGELOG.md) · **English**

Each version has a `## X.Y.Z` section, the same as in `CHANGELOG.md`. The GitHub Release shows both, Portuguese first.

## Next version

### New

- **Hotkey as keys in Settings:** the hotkey field shows the combination as drawn keys, the same as in the main window.
  It works as before: click or Tab, press the new combination; Esc cancels.

### Fixes

- **Short screen or high scaling:** when a window does not fit and scrolls, the scroll bar is the theme's thin bar
  instead of the dark Windows gutter.
- **Match X-ray with the comparison on a 1080 px screen:** the closing note and the Close button were outside the
  scroll area and never showed. The scroll now reaches them.
- **History:** no more empty space between "3 matches saved." and the buttons.
- **Settings, Daily page:** fits whole, without a scroll of a few pixels.

## 1.18.0

### New

- **New look, cleaner and more modern:** graphite background, the mode color in solid (orange in Performance, blue in
  Daily), no neon glow, sentence case and numbers in Bahnschrift. Switching modes is now a short fade, without passing
  through mixed colors.
- **Grouped settings:** each page's options sit in cards, with a thin scroll bar, and Save takes the color of the open
  page.
- **Match X-ray in cards:** average FPS, 1% low and stutters at the top; programs, stutters, processor, network and the
  before and after in separate cards, with each number's change in green or red.
- **Dark tray menu**, matching the windows.
- Diagnostics, history, confirmation and the mouse tools (polling, click, aim, DPI, sensitivity) in the same look. The
  Diagnostics log wraps lines instead of cutting the text.

## 1.17.0

### New

- **Watch the mouse** (Settings → Performance, on by default): if another program turns acceleration back on, changes
  the speed or the scroll during Performance, the app puts the mode's mouse back within 3 s and says what changed and
  which open mouse program may have done it (G HUB, Synapse, iCUE...). If it keeps changing (3 times in 10 min), it
  stops fighting until the next mode switch. With notifications silenced in Performance, the notice shows up when
  leaving it. It does not watch in Daily.

## 1.16.0

### New

- **Previous matches** (tray icon): the X-ray of each match is kept on this PC (the last 100), and the "Last match
  X-ray" is still there after closing the app. Each report compares with the previous match of the same game, in
  average FPS, 1% low, stutters per hour, ping, loss and CPU throttling, and says which app settings changed between
  them: you can see whether turning on calm background or the FPS limit really helped. Turn it on and off in
  Settings → Privacy; the list has the button to delete everything.

## 1.15.0

### New

- **Broad line warning in the game list** (Settings → Games): each line counts as the start of the process name, and a
  line like `s` or `cod` also catches Steam, Spotify or VS Code (`Code.exe`). With them open, Performance turned on by
  itself and they got high priority. Now adding such a line asks for confirmation, saying what else it catches, and
  the ones already in the list show as BROAD.

## 1.14.0

### New

- **Stutter finder for Vulkan and OpenGL**: games that do not use DirectX (Minecraft Java, Doom, those running on
  Vulkan) now get FPS, 1% low and stutters in the X-ray. Frames are read from the graphics driver's present events,
  as PresentMon does; each one's time is slightly less precise than DirectX's, and the report says when that was the
  case. DirectX games are read as before. Not tested on a real game yet: `tools/frame-driver-check.ps1` runs that
  test.

## 1.13.0

### New

- **Per-core culprit** (Stutter finder): besides programs using 20% of the whole CPU, the X-ray now points to the
  program that, at the time of the stutter, jumped to almost a whole core, well above its own usage during the match
  (or opened midway already keeping a core busy). On a 16-thread CPU, a whole core is only 6% of the total and went
  unnoticed. Programs that are heavy all the time, like the browser, do not become the cause of every stutter.
- **More accurate polling meter and click test**: the mouse is now read on its own high-priority thread, which
  timestamps each reading as soon as Windows delivers it. Before, the time was when the screen got to the reading, and
  a screen pause (the game check, a repaint) inflated the polling jitter or reported a double click that never
  happened.

## 1.12.0

### New

- **CPU during the match** (X-ray): average speed compared with nominal and the stretches when the CPU was throttled
  by heat, a power limit or the battery. Stutters in those stretches get that cause.

### Fixes

- **Stutter finder**: the time of each stutter was wrong (the frame clock was read in the wrong unit), so the loading
  screen was counted and no stutter got a cause. Time and causes are now right, and only the game's frames are kept in
  memory.
- **Shutting down or signing out in Performance** puts Windows back to Daily (mouse, plan, USB and power mode), and if
  Windows ends the app midway, the next start finishes undoing it. Cancelling the shutdown no longer leaves the
  Windows key and notifications without the Performance setting.
- **Saving Settings** no longer undoes mouse changes made in Windows, no longer goes back to Performance when you
  chose Daily with the game open, and no longer undoes the Automatic switch changed from the tray.
- **Uninstalling** removes the FPS limit written to the driver, restores the power mode and resolution, deletes the
  power plan copies the app created and removes the Stutter finder permission the app granted.
- **FPS limit**: AUTO follows the monitor refresh rate when it changes; it goes to the executable the game really uses
  (FiveM, Valorant, Fortnite); it never asks for the administrator by itself; and the app only asks for the
  administrator for the same file that is running, with the graphics card libraries loaded only from the Windows
  folder.
- **Windows preserved**: "one screen at a time" scrolling, USB suspend and the previous power plan come back as they
  were; games with a generic name ("Game.exe") no longer make Windows processes look like a game; and the Windows key
  no longer gets stuck when bringing the game to the front with Win+number.
- **Calm background** gives back the priority of programs opened by a calmed program through an intermediate one that
  already closed.
- **Self-update** checks the installer again right before running it.
- **Screens on high-scale laptops**: windows that do not fit scroll without hiding buttons, and option text no longer
  gets cut.

## 1.11.0

### New

- **Per-game FPS limit** (Settings → Games, beta): saves the limit in the graphics driver's limiter (NVIDIA or Intel
  Arc), with AUTO = monitor refresh rate minus 3. Saved when you save Settings, read back to confirm and valid from
  the next time the game opens. Not tested on a real NVIDIA card yet: `tools/fps-cap-check.ps1` runs that test.
- **Laptop power mode**: with the Balanced plan in Performance, the Windows power mode goes to Best performance while
  plugged in and comes back when leaving (and when the app opens again, if it crashed). When the PC does not accept
  High performance or Ultimate Performance, the app now stays on Balanced with Best performance instead of only
  warning. It also warns when a game opens on battery.

## 1.10.0

### New

- **Stutter finder** (Match X-ray; Settings → General → Allow): the match's average FPS, 1% low and stutters, each
  with what stood out at the time (CPU, disk or internet), or the note that it may be the game itself. It reads
  DirectX 9 to 12 Present events through Windows tracing, without touching the game. It needs a one-time permission:
  the Allow button adds the account to the "Performance Log Users" group (it asks for the administrator once) and
  applies after signing out and back in.

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
