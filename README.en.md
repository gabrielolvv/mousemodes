# MouseModes

[Português](README.md) · **English**

Windows 10/11 app that switches your mouse and PC between two modes: **Performance** (games) and **Daily**.
Switch from the main screen, with a global hotkey, or automatically when a game opens.

**Website:** [gabrielolvv.github.io/mousemodes](https://gabrielolvv.github.io/mousemodes/): what the app does, screens and download.

<p align="center">
  <a href="https://github.com/gabrielolvv/mousemodes/releases/latest/download/MouseModes-Setup.exe">
    <img alt="Download the Windows installer" src="https://img.shields.io/badge/Download%20installer-Windows%2010%2F11-E84C28?style=for-the-badge&logo=windows&logoColor=white" height="48">
  </a>
</p>

![MouseModes screens](docs/preview.png)

The interface follows the Windows language: Portuguese on a Portuguese Windows, English on any other. You can pick
another one in the tray icon → Idioma / Language (it applies the next time the app opens). Every screen and the
Diagnostics log are translated.

## Install

1. Click **Download installer** above.
2. Open `MouseModes-Setup.exe` and click **Next** until the end.

The first time, Windows may show "Windows protected your PC", because the app is not digitally signed:
click **More info → Run anyway**.

To check that the installer you downloaded is the one GitHub built, compare its SHA-256 with the one on the Release
page (also in the `MouseModes-Setup.exe.sha256` file). In PowerShell, in the download folder:

```powershell
(Get-FileHash .\MouseModes-Setup.exe -Algorithm SHA256).Hash
```

- It installs only for your user, without asking for administrator rights, and adds a Start menu shortcut.
- It updates by itself: once a day the app checks the GitHub releases page, sending nothing from your PC. When a new
  version is out, it downloads the installer from the Release, checks the SHA-256 published there and installs over
  the current one, never during a game: the app closes, installs and opens again in the tray within seconds, with the
  same settings. If anything does not match, it does not install and only shows a notice. You can turn it off in
  Settings → Privacy (then it only notifies) or check right away from the tray icon → Check for a new version. To
  update by hand, download and run the installer again.
- To uninstall: Settings → Apps → Installed apps → MouseModes. Mouse and power go back to Daily mode.

## What each mode does

| | Performance (games) | Daily |
|---|---|---|
| Pointer speed | 10/20 (1:1, no multiplier) | whatever you already used |
| Acceleration ("Enhance pointer precision") | off | whatever you already used |
| Power plan | High performance or Ultimate Performance | whatever you already used ("Previous") |
| USB selective suspend | disabled (the mouse never "falls asleep") | Windows default |

Everything can be changed in **Settings**. Closing the window with the X does not quit the app: it stays in the icon
next to the clock.

Daily mode follows Windows itself: if you change the speed or acceleration in the Control Panel, that is what comes
back after Performance. When it opens, the app does not rewrite anything; it only undoes a Performance mode that was
left applied because the app was killed or the PC shut down in the middle.

### Turbo (Performance mode only)

- **High priority for the game**, restored when leaving. Some anti-cheats block it; the app then skips it and logs it
  in Diagnostics.
- **Blocks the Windows key**, only while a game from the list is in the foreground.
- **No Sticky Keys prompt** (Shift 5x and the like).
- **Silences Windows notifications.** If the app crashes while they are silenced, it restores them when it opens again.
- **Calm background** (off by default): instead of closing them, keeps the other open programs at below normal
  priority and, on Windows 11, in efficiency mode. On hybrid Intel CPUs (12th gen or newer), they also move to the
  efficiency cores (E), leaving the performance cores (P) for the game. It helps with stutters when the CPU is maxed
  out (browser, Steam downloading, OneDrive syncing). It does not touch the game, the program in the foreground (on
  alt-tab it goes back to normal), Windows processes, or voice, music, streaming, overlay, mouse software and
  anti-cheat (Discord, Spotify, OBS, NVIDIA, G HUB, Razer etc.). A game opened by a calmed Steam goes back to normal
  right away. If the app crashes with the background calmed, it undoes it when it opens again.
- **Closes programs from your list** (e.g. OneDrive, launchers). Games from the list and Windows processes are never
  closed.

Everything goes back to normal when you leave Performance, quit the app or shut down the PC.

### More

- **Per-game profiles** (Settings → Games): speed, acceleration, power plan and resolution for each game. With a
  resolution (e.g. 1280×960 for 4:3 stretched in CS2 or Valorant), the main monitor switches to it, at the highest
  refresh rate it supports, when the game opens in Performance, and goes back to the usual one when the game closes,
  when leaving Performance or when the app quits. The change is not saved in Windows: restarting the PC also undoes
  it, and if the app crashes it undoes it when it opens again. To stretch, the graphics card scaling must be set to
  "Full screen".
- **Game mouse only with the game on screen** (Settings → Performance, off by default): on alt-tab and on the
  desktop, the mouse goes back to the Daily speed and acceleration; back in the game, it uses the Performance one.
  Power, USB and Turbo do not change.
- **Automatic switch**: compares the *start* of the process name (`FiveM` matches `FiveM_b3095_GTAProcess`).
  A manual switch wins over the automatic one.
- **Find games** (Settings → Games → Find): lists the games installed through Steam and Epic, plus the open programs,
  to add several at once. The app picks the largest `.exe` in the game folder as the process, skipping uninstallers,
  launchers and anti-cheat; if it gets it wrong, you can fix the name by hand.
- **Global hotkey** (Settings → General): click the field and press the combination, with Ctrl or Alt (default:
  Ctrl+Alt+M). If another program already uses it, the app tries Ctrl+Alt+M, Ctrl+Shift+M, Ctrl+Alt+F12 and
  Ctrl+Shift+F12 and shows on the main screen which one it got.
- **Switch by command**: `MouseModes.exe --performance`, `--daily` or `--toggle` switches the mode of the running
  app, like the hotkey. Use it for a Stream Deck button, a desktop shortcut or Task Scheduler. If the app is not
  running, it opens in the tray already in that mode.
- **Monitor refresh rate**: warns if a monitor is below its maximum (e.g. 60 Hz on a 144 Hz one) and fixes it in
  one click.
- **Polling rate meter** and **double-click test** (worn switch), reading the mouse through Raw Input.
- **Aim and DPI test** (main screen or tray icon): 20 s of targets. The app measures whether you overshoot the target,
  stop short, how many corrections you make and how precise the click is, and suggests raising or lowering the DPI
  in your mouse software. Changed the DPI? Run it again: the rounds stay side by side while the app is open.
- **Sensitivity converter** (main screen → Sensitivity, or the tray icon): type your game's sensitivity and DPI and
  see the cm/360 (how far the mouse travels for a full turn) and the value that gives the same aim in CS2, Apex, TF2,
  Valorant, Overwatch 2 and Call of Duty. Changed the DPI? Enter the new one and the app recalculates each game's
  sensitivity so your aim does not change. For another game, just enter its yaw.
- **Real mouse DPI** (sensitivity converter → Measure the real DPI, or the tray icon): the sensor rarely delivers the
  advertised DPI. With a ruler on the desk, hold the left button on the box, drag the mouse in a straight line over
  the distance (4 in) and let go; the app counts the sensor reports through Raw Input and shows the true DPI (e.g. 800
  in the mouse software, 763 on the ruler). After that, the converter shows the turn (cm/360) with the real DPI. The
  Windows speed does not count.
- **Match X-ray**: while a game from the list is open in Performance, the app notes every 3 s how much CPU and data
  each background program used, and measures the network with one ping per second to the router and to 1.1.1.1
  (Cloudflare). When the game closes, a notice shows what weighed the most (e.g. chrome averaging 18% with 61% peaks,
  OneDrive with 2.1 GB) and, in one click, adds the program to the auto-close list or turns on calm background. The
  network part shows ping, jitter and loss, the worst spikes and where they came from: the Wi-Fi or your network (the
  router got worse too) or the internet (everything was fine up to the router), and who was using a lot of data at the
  time. It stays in the tray icon → Last match X-ray. It only reads, without administrator rights; it measures what
  weighed, not FPS.
- **Stutter finder** (in the Match X-ray; Settings → General → Allow): measures every frame the game delivers to the
  screen (DirectX 9 to 12 Present events, like PresentMon) and shows at the end of the match the average FPS, the 1%
  low and the stutters, with what stood out at the time: a program using a lot of CPU, another reading/writing the
  disk or the internet not answering. If nothing in the background explains it, it says it may be the game itself
  (shaders, loading). It does not touch the game. Windows only lets administrators or the "Performance Log Users"
  group read frames: the Allow button adds your account to the group once (it asks for the administrator) and applies
  after signing out and back in; the same button removes it. Vulkan and OpenGL games are not read yet.
- **Diagnostics** (Settings → General): a log of what the app did and what failed. File at
  `%APPDATA%\MouseModes\log.txt`.
- **Windows for games** (in Diagnostics): shows Game Mode, background recording, hardware-accelerated GPU scheduling
  and, on Windows 11, optimizations for windowed games, with what usually helps. The app changes none of them: the
  "Open" button takes you to the Windows page where you decide.
- **Restore Windows defaults** (Settings → General).
- **Export and import** (Settings → General): takes games, profiles, Turbo and general settings to another PC in an
  `.xml` file. What only applies to this PC (applied mode, this PC's power plans, notification state) is left out.
  Before importing, the app shows how many games and which programs to close the file brings.

Settings are stored in `%APPDATA%\MouseModes\config.xml`.

## What really reduces delay (and what doesn't)

- **Polling rate** matters most and is set **in the mouse** (the brand's software). 1000 Hz = one report per ms;
  125 Hz = one every 8 ms. Use the meter to check it.
- **Acceleration off + speed 10** makes movement predictable. Games with *raw input* (CS2, Valorant, Apex) already
  ignore these options during a match; they apply to menus, the desktop and games without raw input.
- **Power plan** and **USB without suspend** keep the CPU and the USB port from "sleeping" and causing micro stutters.
- High FPS, the monitor at its maximum refresh rate and a wired/2.4 GHz mouse (not Bluetooth) matter more than any
  software tweak.
- The app does not apply "miracle" registry tweaks (MouseDataQueueSize, bcdedit etc.): the gain is doubtful and
  sometimes things get worse.

## Development

```
src/                 C# code (WinForms, .NET Framework 4.8)
tests/               automated tests (xUnit)
installer/           installer script (NSIS)
build.sh             builds dist/MouseModes.exe and dist/MouseModes-Setup.exe
VERSION              app and installer version
.github/workflows/   CI: build on every push and a Release on every tag
```

**Official build** (what goes to the Release), on Linux/WSL:

```sh
sudo apt-get install mono-devel nsis
./build.sh          # app + installer
./build.sh app      # app only
```

It compiles against the exact .NET 4.8 reference assemblies (`/usr/lib/mono/4.8-api`). Compiling against Mono's
standard library can produce calls that do not exist on Windows.

**On Windows** (Visual Studio, Rider or a terminal):

```powershell
dotnet build src/MouseModes.csproj -c Release
# exe at src\bin\Release\net48\MouseModes.exe
dotnet test tests/MouseModes.Tests/MouseModes.Tests.csproj -c Release
```

The tests only read Windows (mouse, registry): none of them applies mouse, power plan or USB settings. The files they
create and the log go to a temporary folder. The power plan goes through a fake applier (`ModeManager.PowerApplier`);
a new test that touches the queue must check that before queueing requests.

Texts on screen live in the code with both versions side by side: `L.T("Ajustes", "Settings")` (see `src/L.cs`).

**Publishing a version**:

1. Update the `VERSION` file (e.g. `1.2.0`) and write the `## 1.2.0` section with what changed in `CHANGELOG.md` and,
   in English, in `CHANGELOG.en.md`. Both become the Release text, and CI refuses the tag if either is missing. Commit.
2. Create and push the tag: `git tag v1.2.0 && git push origin v1.2.0`.
3. GitHub Actions builds, runs the tests, checks that the tag matches `VERSION` and attaches `MouseModes-Setup.exe`
   and `MouseModes-Setup.exe.sha256` to the Release (the SHA-256 also goes in the Release text).
   The README download button always points to the installer of the latest Release.

The CI actions are pinned by commit, with the version in a comment. Once a month Dependabot
(`.github/dependabot.yml`) checks for new versions and, if there is one, opens a PR replacing the SHA and the comment.
To update by hand, replace the SHA with the new version's (`git ls-remote --tags https://github.com/<owner>/<action>`).

Executable arguments: `--tray` (starts in the tray), `--quit` (closes the running copy, restoring everything) and
`--uninstall-cleanup` (goes back to Daily and removes "Start with Windows"), used by the installer; and
`--performance`, `--daily` and `--toggle` (switch the mode). With another copy running, the command goes to it and
the exe exits with code 0, or 1 if the running copy is from a version without these commands. When updating by
itself, the app runs the installer with `/S /RELAUNCH`, and the installer opens the new version with `--tray --updated`.

## Code signing policy

The MouseModes installer is not digitally signed yet; code signing is planned. The project applied for the SignPath
Foundation's free code signing for open source, but it has not been granted yet: the foundation asks for more public
use of the project first, and the application will be made again later. Meanwhile, Windows may show the "Windows
protected your PC" warning, and the SHA-256 published with each Release lets you check that the installer is the one
this repository's CI build produced.

Team and roles:

- Committers and reviewers: [gabrielolvv](https://github.com/gabrielolvv)
- Approvers (each signing request): [gabrielolvv](https://github.com/gabrielolvv)

Once there is signing, only what comes out of this repository's automated build, from the public source code, will
be signed, with manual approval for every version.

## Privacy

MouseModes has no account, no telemetry and no ads. It only talks to other systems in these cases, and you can turn
each of them off in Settings → Privacy:

- **New versions**: once a day, a request to the GitHub releases page (api.github.com). When updating, the installer
  is downloaded from the GitHub Release. Only your IP address and the program name go along, as in any website visit.
- **Network X-ray**: during a match (Performance with a listed game open), one ping per second to your router and to
  1.1.1.1 (Cloudflare), with no content.

Settings and the log of what the app does stay on this PC, in `%APPDATA%\MouseModes`. The installer shows this
notice before installing.

## License

[MIT](LICENSE): use, modify and share it freely, keeping the copyright notice. The app comes with no warranty.
