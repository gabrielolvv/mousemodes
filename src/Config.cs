using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace MouseModes
{
    public enum Mode
    {
        DiaADia,
        Performance
    }

    public enum PowerPlanOption
    {
        NaoAlterar,
        Equilibrado,
        AltoDesempenho,
        DesempenhoMaximo // "Ultimate Performance" do Windows
    }

    /// <summary>Tudo o que um modo ajusta no sistema.</summary>
    public class MouseProfile
    {
        /// <summary>Velocidade do ponteiro do Windows (1–20). 10 = 1:1, sem multiplicador.</summary>
        public int PointerSpeed { get; set; }

        /// <summary>"Aprimorar precisão do ponteiro" = aceleração do mouse do Windows.</summary>
        public bool EnhancePointerPrecision { get; set; }

        public int ScrollLines { get; set; }

        public PowerPlanOption PowerPlan { get; set; }

        /// <summary>Impede o Windows de "adormecer" portas USB para economizar energia.</summary>
        public bool DisableUsbSelectiveSuspend { get; set; }

        public MouseProfile()
        {
            PointerSpeed = 10;
            ScrollLines = 3;
        }

        public MouseProfile Clone()
        {
            return (MouseProfile)MemberwiseClone();
        }
    }

    public class GameProfile
    {
        public string Process { get; set; }
        public MouseProfile Profile { get; set; }
    }

    public class AppConfig
    {
        public MouseProfile Performance { get; set; }
        public MouseProfile Daily { get; set; }

        /// <summary>Começo do nome do processo (sem .exe). Ex.: "FiveM" pega FiveM_b3095_GTAProcess.</summary>
        public List<string> Games { get; set; }

        public bool AutoSwitch { get; set; }
        public bool RestoreDailyOnExit { get; set; }
        public bool ShowNotifications { get; set; }

        // ---- Extras do modo Performance ----

        /// <summary>Sobe a prioridade do jogo para Alta enquanto o modo Performance estiver ativo.</summary>
        public bool BoostGamePriority { get; set; }

        /// <summary>Fecha os programas de AppsToClose ao entrar no modo Performance.</summary>
        public bool CloseAppsOnPerformance { get; set; }

        /// <summary>Encerra à força quem não fechar sozinho.</summary>
        public bool ForceCloseApps { get; set; }

        public List<string> AppsToClose { get; set; }

        /// <summary>Avisa quando um monitor está abaixo da taxa máxima.</summary>
        public bool MonitorCheck { get; set; }

        // ---- Durante o jogo (só no Performance) ----

        public bool BlockWinKey { get; set; }
        public bool DisableAccessibilityHotkeys { get; set; }
        public bool SilenceNotifications { get; set; }

        /// <summary>Atalho global preferido (índice em HotkeyWindow.Choices).</summary>
        public int Hotkey { get; set; }

        /// <summary>Estado original das notificações enquanto estão silenciadas (para desfazer após travamento).</summary>
        public NotificationBackup NotificationBackup { get; set; }

        /// <summary>Configurações próprias de alguns jogos (os demais usam o perfil Performance).</summary>
        public List<GameProfile> GameProfiles { get; set; }

        /// <summary>
        /// Último modo aplicado ao Windows. Se for Performance ao abrir, o app saiu ou caiu sem voltar ao Dia a dia.
        /// null = desconhecido (config de versão antiga).
        /// </summary>
        public Mode? LastMode { get; set; }

        public AppConfig()
        {
            Games = new List<string>();
            AppsToClose = new List<string>();
            GameProfiles = new List<GameProfile>();
            AutoSwitch = true;
            RestoreDailyOnExit = true;
            ShowNotifications = true;
            BoostGamePriority = true;
            MonitorCheck = true;
            BlockWinKey = true;
            DisableAccessibilityHotkeys = true;
            SilenceNotifications = true;
        }

        /// <summary>Perfil a usar no Performance: o do jogo aberto, se ele tiver um próprio.</summary>
        public MouseProfile PerformanceFor(string runningProcess)
        {
            if (runningProcess != null)
            {
                foreach (GameProfile gp in GameProfiles)
                {
                    string prefix = GameDetector.Normalize(gp.Process);
                    if (prefix.Length > 0 && gp.Profile != null &&
                        runningProcess.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return gp.Profile;
                }
            }
            return Performance;
        }

        public GameProfile FindGameProfile(string name)
        {
            return GameProfiles.Find(g => string.Equals(GameDetector.Normalize(g.Process), name, StringComparison.OrdinalIgnoreCase));
        }

        private static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseModes");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(Folder, "config.xml"); }
        }

        public static AppConfig LoadOrCreate()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var serializer = new XmlSerializer(typeof(AppConfig));
                    AppConfig loaded;
                    using (var stream = File.OpenRead(FilePath))
                    {
                        loaded = (AppConfig)serializer.Deserialize(stream);
                    }

                    if (loaded.Performance == null) loaded.Performance = DefaultPerformance(MouseSettings.Read());
                    if (loaded.Daily == null) loaded.Daily = DefaultDaily(MouseSettings.Read());
                    if (loaded.Games == null) loaded.Games = new List<string>();
                    if (loaded.AppsToClose == null) loaded.AppsToClose = new List<string>();
                    if (loaded.GameProfiles == null) loaded.GameProfiles = new List<GameProfile>();
                    loaded.GameProfiles.RemoveAll(g => g == null || g.Profile == null || string.IsNullOrEmpty(g.Process));
                    return loaded;
                }
                catch
                {
                    // Arquivo corrompido: guarda uma cópia e recomeça do padrão.
                    try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
                }
            }

            var created = CreateDefault();
            created.Save();
            return created;
        }

        public static AppConfig CreateDefault()
        {
            // O modo Dia a dia nasce igual ao que o Windows está usando agora,
            // assim nada muda para quem só quer voltar ao normal.
            var current = MouseSettings.Read();
            var cfg = new AppConfig
            {
                Daily = DefaultDaily(current),
                Performance = DefaultPerformance(current),
                LastMode = Mode.DiaADia // o Windows já está como o Dia a dia: nada a aplicar na primeira abertura
            };
            cfg.Games.AddRange(new[]
            {
                "FiveM",
                "GTA5",
                "cs2",
                "VALORANT-Win64-Shipping",
                "r5apex",
                "FortniteClient-Win64-Shipping",
                "RainbowSix",
                "League of Legends"
            });
            return cfg;
        }

        private static MouseProfile DefaultDaily(MouseProfile current)
        {
            var p = current.Clone();
            p.PowerPlan = PowerPlanOption.Equilibrado;
            p.DisableUsbSelectiveSuspend = false;
            return p;
        }

        private static MouseProfile DefaultPerformance(MouseProfile current)
        {
            return new MouseProfile
            {
                PointerSpeed = 10,
                EnhancePointerPrecision = false,
                ScrollLines = current.ScrollLines,
                PowerPlan = PowerPlanOption.AltoDesempenho,
                DisableUsbSelectiveSuspend = true
            };
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            var serializer = new XmlSerializer(typeof(AppConfig));
            using (var stream = File.Create(tmp))
            {
                serializer.Serialize(stream, this);
            }
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }
    }
}
