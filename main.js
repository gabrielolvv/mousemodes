// Idioma (pt/en), estrelas e versão atual. A página funciona em português sem JavaScript.
(function () {
  "use strict";

  var en = {
    "nav.features": "Features",
    "nav.xray": "X-ray",
    "nav.delay": "Real latency",
    "nav.download": "Download",
    "nav.star": "Star",
    "hero.eyebrow": "Free · open source · Windows 10 and 11",
    "hero.title": "One click and your PC is in <span class=\"grad-perf\">game mode</span>.",
    "hero.lead": "MouseModes switches your mouse, power and Windows between Performance and Daily, by itself when a game opens, and undoes it all when the game closes. No admin rights, no telemetry, no miracle tweaks.",
    "cta.download": "Download for Windows",
    "cta.star": "Star it on GitHub",
    "hero.fine": "~280 KB installer · for your user only · updates by itself",
    "trust.1t": "No admin rights",
    "trust.1d": "Installs and runs for your user only.",
    "trust.2t": "Undoes everything",
    "trust.2d": "When leaving Performance, closing the app or shutting down.",
    "trust.3t": "No telemetry",
    "trust.3d": "It only talks to GitHub, to check versions, and measures ping during a match.",
    "trust.4t": "Open source",
    "trust.4d": "Every version comes from GitHub with its SHA-256 published.",
    "modes.title": "Two modes. Switch in one click, by hotkey or automatically.",
    "modes.perfSub": "For gaming",
    "modes.perf1": "1:1 pointer and acceleration off",
    "modes.perf2": "High performance power plan",
    "modes.perf3": "USB without suspend: the mouse never \"falls asleep\"",
    "modes.perf4": "Turbo: high priority, Windows key blocked, notifications silenced",
    "modes.dailyTitle": "Daily",
    "modes.dailySub": "For work and browsing",
    "modes.daily1": "Brings back the speed and acceleration you used",
    "modes.daily2": "Brings back your previous power plan",
    "modes.daily3": "USB back to the Windows default",
    "modes.daily4": "Everything Turbo changed goes back to normal",
    "modes.note": "Automatic switching recognizes the game by its process name. You can also switch with Ctrl+Alt+M, even inside the game, or from a Stream Deck button.",
    "feat.title": "Everything that comes with it",
    "feat.calm.t": "Calm background",
    "feat.calm.d": "Instead of closing them, keeps the browser, Steam and OneDrive at low priority and in efficiency mode. On hybrid Intel CPUs, it moves them to the E-cores and leaves the P-cores for the game.",
    "feat.xray.t": "Match X-ray",
    "feat.xray.d": "When the game closes, it shows what weighed the most and how the network was, and fixes it in one click: auto-close or calm it.",
    "feat.aim.t": "Aim and real DPI",
    "feat.aim.d": "20 seconds of targets suggest a DPI. With a ruler, the app measures the DPI your sensor really delivers.",
    "feat.sens.t": "Sensitivity converter",
    "feat.sens.d": "The same aim (cm/360) in CS2, Apex, Valorant, Overwatch 2 and Call of Duty, or at another DPI.",
    "feat.poll.t": "Polling rate and clicks",
    "feat.poll.d": "Measures how many reports per second your mouse sends, the jitter, and whether the switch double-clicks.",
    "feat.hz.t": "Monitor refresh rate",
    "feat.hz.d": "Warns when your 144 or 165 Hz monitor is running at 60 Hz and fixes it in one click.",
    "feat.games.t": "Per-game profiles",
    "feat.games.d": "Speed, acceleration and power plan for each game. The search finds your Steam and Epic games.",
    "feat.update.t": "Updates by itself",
    "feat.update.d": "Downloads the new version from GitHub, checks the SHA-256 and installs it outside games, keeping your settings.",
    "spot1.k": "Match X-ray",
    "spot1.t": "Find out what steals your PC and your network during a game.",
    "spot1.d": "While the game is open, the app notes every 3 seconds how much CPU and data each background program used, and measures ping to the router and to the internet every second. At the end, a notice shows the culprits (chrome peaking at 61%, OneDrive with 2.1 GB), the ping spikes and whether they came from the Wi-Fi or the internet.",
    "spot1.n": "It only reads, without admin rights. It measures what weighed and the network, not FPS.",
    "spot2.k": "Turbo",
    "spot2.t": "Only in Performance, and everything comes back.",
    "spot2.d": "High priority for the game, Windows key blocked with the game in front, no Sticky Keys prompt, silenced notifications and calm background. If the app crashes midway, it undoes it when it opens again.",
    "spot3.k": "Sensitivity converter",
    "spot3.t": "Switch games without relearning your aim.",
    "spot3.d": "Type the sensitivity and DPI you use and see how far the mouse travels for a full turn, and the value that gives the same aim in each game. Changed the DPI? The app recalculates everything.",
    "delay.title": "What really cuts latency (and what does not)",
    "delay.lead": "No magic FPS promises. What really matters:",
    "delay.1t": "Polling rate",
    "delay.1d": "It matters the most, and you set it in your mouse software. 1000 Hz is one report per ms; 125 Hz, one every 8 ms. The app's meter checks it.",
    "delay.2t": "Acceleration off",
    "delay.2d": "With speed 10, movement is predictable in menus, on the desktop and in games without raw input.",
    "delay.3t": "Power and USB awake",
    "delay.3d": "They keep the CPU and USB port from \"sleeping\" and causing micro stutters.",
    "delay.4t": "Miracle tweaks",
    "delay.4d": "The app does not touch MouseDataQueueSize, bcdedit and the like: the gain is doubtful and sometimes worse.",
    "dl.title": "Download and play in 1 minute",
    "dl.1": "Click <strong>Download for Windows</strong>.",
    "dl.2": "Open <code>MouseModes-Setup.exe</code> and click <strong>Next</strong> until the end.",
    "dl.3": "If Windows says it protected your PC (the app is not signed), click <strong>More info → Run anyway</strong>.",
    "dl.notes": "Release notes and SHA-256",
    "dl.uninstall": "To uninstall: Settings → Installed apps → MouseModes. The mouse and power go back to Daily.",
    "star.title": "Like it? Leave a star.",
    "star.lead": "It is free, takes a second and helps more people find MouseModes. Ideas and bugs are welcome in the issues.",
    "star.issues": "Suggest an idea",
    "foot.releases": "Releases",
    "foot.changelog": "What's new",
    "foot.issues": "Issues and ideas"
  };

  var pt = {}; // o texto em português é o que já está no HTML
  var meta = {
    pt: { title: document.title, lang: "pt-BR", button: "EN" },
    en: { title: "MouseModes · game mode for your Windows PC", lang: "en", button: "PT" }
  };

  function remember(lang) {
    try { localStorage.setItem("mm-lang", lang); } catch (e) { /* sem armazenamento: só não lembra */ }
  }

  function saved() {
    try { return localStorage.getItem("mm-lang"); } catch (e) { return null; }
  }

  // Guarda o português de cada elemento antes da primeira troca.
  document.querySelectorAll("[data-i18n]").forEach(function (el) { pt[el.dataset.i18n] = el.textContent; });
  document.querySelectorAll("[data-i18n-html]").forEach(function (el) { pt[el.dataset.i18nHtml] = el.innerHTML; });
  document.querySelectorAll("img[data-alt-en]").forEach(function (el) { el.dataset.altPt = el.alt; });
  document.querySelectorAll("[data-href-en]").forEach(function (el) { el.dataset.hrefPt = el.getAttribute("href"); });

  function apply(lang) {
    var dict = lang === "en" ? en : pt;
    document.querySelectorAll("[data-i18n]").forEach(function (el) {
      var text = dict[el.dataset.i18n];
      if (text != null) el.textContent = text;
    });
    document.querySelectorAll("[data-i18n-html]").forEach(function (el) {
      var html = dict[el.dataset.i18nHtml];
      if (html != null) el.innerHTML = html;
    });
    document.querySelectorAll("img[data-src-" + lang + "]").forEach(function (el) {
      el.src = el.getAttribute("data-src-" + lang);
    });
    document.querySelectorAll("img[data-alt-en]").forEach(function (el) {
      el.alt = lang === "en" ? el.dataset.altEn : el.dataset.altPt;
    });
    document.querySelectorAll("[data-href-en]").forEach(function (el) {
      el.setAttribute("href", lang === "en" ? el.dataset.hrefEn : el.dataset.hrefPt);
    });
    document.documentElement.lang = meta[lang].lang;
    document.title = meta[lang].title;
    document.getElementById("lang").textContent = meta[lang].button;
    current = lang;
  }

  var current = "pt";
  var start = saved() || ((navigator.language || "pt").toLowerCase().indexOf("pt") === 0 ? "pt" : "en");
  if (start === "en") apply("en");
  document.getElementById("lang").addEventListener("click", function () {
    var next = current === "pt" ? "en" : "pt";
    apply(next);
    remember(next);
  });

  // Estrelas e versão vêm da API pública do GitHub; se falhar, a página segue sem elas.
  function get(url, done) {
    if (!window.fetch) return;
    fetch(url, { headers: { Accept: "application/vnd.github+json" } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (data) { if (data) done(data); })
      .catch(function () {});
  }

  get("https://api.github.com/repos/gabrielolvv/mousemodes", function (repo) {
    // "0" afasta mais do que convida: o número só aparece depois da primeira estrela.
    if (typeof repo.stargazers_count !== "number" || repo.stargazers_count < 1) return;
    var el = document.getElementById("stars");
    el.textContent = repo.stargazers_count.toLocaleString();
    el.hidden = false;
  });

  get("https://api.github.com/repos/gabrielolvv/mousemodes/releases/latest", function (release) {
    if (typeof release.tag_name === "string" && /^v\d+\.\d+\.\d+$/.test(release.tag_name))
      document.getElementById("version").textContent = release.tag_name;
  });
})();
