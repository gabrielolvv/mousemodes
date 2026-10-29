# What's new in MouseModes

[Português](CHANGELOG.md) · **English**

Each version has a `## X.Y.Z` section, the same as in `CHANGELOG.md`. The GitHub Release shows both, Portuguese first.

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
