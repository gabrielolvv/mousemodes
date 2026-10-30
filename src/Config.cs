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

        /// <summary>Resolução do monitor principal com o jogo aberto ("1280x960"); null = não muda. Só nos perfis de jogo.</summary>
        public string Resolution { get; set; }

        /// <summary>Limite de FPS gravado no driver da placa (0 = sem limite; -1 = taxa do monitor menos 3). Só nos perfis de jogo.</summary>
        public int FpsCap { get; set; }

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

    /// <summary>Cópia de um plano do Windows criada pelo app (ex.: Desempenho máximo).</summary>
    public class PlanCopy
    {
        public string Template { get; set; }
        public string Guid { get; set; }
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

        /// <summary>
        /// No Performance, o mouse do jogo só vale com um jogo da lista em primeiro plano.
        /// Fora dele (alt-tab, desktop) usa velocidade, aceleração e rolagem do Dia a dia.
        /// </summary>
        public bool MouseOnlyWhenGameFocused { get; set; }

        // ---- Durante o jogo (só no Performance) ----

        public bool BlockWinKey { get; set; }
        public bool DisableAccessibilityHotkeys { get; set; }
        public bool SilenceNotifications { get; set; }

        /// <summary>Segundo plano calmo: prioridade abaixo do normal e modo de eficiência para os outros programas.</summary>
        public bool CalmBackground { get; set; }

        /// <summary>Atalho global preferido (índice em HotkeyWindow.Choices).</summary>
        public int Hotkey { get; set; }

        /// <summary>
        /// Atalho escolhido livremente ("Ctrl+Alt+K"), ou null para usar Hotkey. Se outro programa já usa,
        /// o app cai nas opções fixas a partir de Hotkey.
        /// </summary>
        public string CustomHotkey { get; set; }

        /// <summary>DPI que a pessoa informou no teste de mira (0 = não informado).</summary>
        public int AimTestDpi { get; set; }

        /// <summary>DPI real medido com a régua (0 = nunca medido). Vale enquanto o mouse estiver no MeasuredDpiFor.</summary>
        public int MeasuredDpi { get; set; }

        /// <summary>O DPI do programa do mouse quando o real foi medido.</summary>
        public int MeasuredDpiFor { get; set; }

        // ---- Conversor de sensibilidade (a última conta feita) ----

        public int ConverterGame { get; set; }
        public string ConverterSensitivity { get; set; }
        public string ConverterYaw { get; set; }

        /// <summary>Idioma da interface: "pt", "en" ou null para seguir o Windows (vale ao abrir o app).</summary>
        public string Language { get; set; }

        /// <summary>Uma vez por dia, procura versão nova no GitHub e avisa (fora do modo Performance).</summary>
        public bool CheckForUpdates { get; set; }

        /// <summary>Versão nova: baixa do GitHub, confere e instala sozinho, fora do jogo.</summary>
        public bool AutoUpdate { get; set; }

        /// <summary>Quando procurou versão nova pela última vez (só deste PC).</summary>
        public DateTime? LastUpdateCheck { get; set; }

        /// <summary>Última versão já avisada, para não repetir o aviso todo dia (só deste PC).</summary>
        public string NotifiedVersion { get; set; }

        /// <summary>Estado original das notificações enquanto estão silenciadas (para desfazer após travamento).</summary>
        public NotificationBackup NotificationBackup { get; set; }

        /// <summary>Processos acalmados agora (para desfazer após travamento). Só deste PC.</summary>
        public List<CalmedProcess> CalmBackup { get; set; }

        /// <summary>Raio-X da rede: um ping por segundo ao roteador e ao 1.1.1.1 durante a partida. Desligado, não sai ping.</summary>
        public bool MeasureNetwork { get; set; }

        /// <summary>
        /// A permissão do Caça-travadinhas foi pedida neste PC (a conta entrou no grupo "Performance Log Users"): até sair e
        /// entrar de novo na conta, a tela avisa que falta isso. Só deste PC.
        /// </summary>
        public bool FramePermissionAsked { get; set; }

        /// <summary>A resolução de um jogo está na tela agora (para desfazer após travamento). Só deste PC.</summary>
        public bool ResolutionChanged { get; set; }

        /// <summary>
        /// Modo de energia do Windows (na tomada) antes do Performance pôr em Melhor desempenho; null = o app não trocou.
        /// Volta ao sair do Performance, ou ao abrir de novo se o app cair. Só deste PC.
        /// </summary>
        public string PowerModeBackup { get; set; }

        /// <summary>
        /// Limites de FPS que o app gravou no driver deste PC e confirmou ("cs2.exe=141"): para saber o que mudar ou tirar
        /// quando a tela muda. Só deste PC.
        /// </summary>
        public List<string> FpsCapsApplied { get; set; }

        /// <summary>Configurações próprias de alguns jogos (os demais usam o perfil Performance).</summary>
        public List<GameProfile> GameProfiles { get; set; }

        /// <summary>
        /// Último modo aplicado ao Windows. Se for Performance ao abrir, o app saiu ou caiu sem voltar ao Dia a dia.
        /// null = desconhecido (config de versão antiga).
        /// </summary>
        public Mode? LastMode { get; set; }

        /// <summary>
        /// Plano de energia ativo ao entrar no Performance, para o Dia a dia com "ANTERIOR" voltar a ele.
        /// Guardado no config para valer também depois de um travamento.
        /// </summary>
        public string DailyPlanGuid { get; set; }

        /// <summary>
        /// Cópias de planos que o app criou. Em alguns PCs o "powercfg /list" esconde esses planos; sem guardar
        /// o GUID, o app criaria uma cópia nova a cada vez. Mexida pela thread do plano de energia: use os métodos.
        /// </summary>
        public List<PlanCopy> PlanCopies { get; set; }

        // Protege PlanCopies (thread do plano de energia) enquanto a tela grava o config.
        private readonly object planCopiesGate = new object();

        /// <summary>GUID guardado da cópia deste modelo, ou null.</summary>
        public string FindPlanCopy(string template)
        {
            lock (planCopiesGate)
            {
                PlanCopy copy = PlanCopies.Find(c => c != null && string.Equals(c.Template, template, StringComparison.OrdinalIgnoreCase));
                Guid parsed;
                return copy != null && System.Guid.TryParse(copy.Guid, out parsed) ? parsed.ToString() : null;
            }
        }

        // ---- Exportar / importar ----

        /// <summary>
        /// O que só vale neste PC e não vai para outro: o modo aplicado agora, os planos de energia daqui e o
        /// backup das notificações (importar o de outro PC gravaria os valores dele no registro deste).
        /// </summary>
        internal static readonly string[] MachineOnly =
        {
            "LastMode", "DailyPlanGuid", "PlanCopies", "NotificationBackup", "CalmBackup", "LastUpdateCheck", "NotifiedVersion",
            "ResolutionChanged", "FramePermissionAsked", "FpsCapsApplied", "PowerModeBackup"
        };

        /// <summary>Cópia para exportar: todas as configurações, sem o que só vale neste PC.</summary>
        public AppConfig ForExport()
        {
            var serializer = new XmlSerializer(typeof(AppConfig));
            var buffer = new MemoryStream();
            lock (planCopiesGate) serializer.Serialize(buffer, this);
            buffer.Position = 0;
            var copy = (AppConfig)serializer.Deserialize(buffer);
            copy.LastMode = null;
            copy.DailyPlanGuid = null;
            copy.PlanCopies = new List<PlanCopy>();
            copy.NotificationBackup = null;
            copy.CalmBackup = new List<CalmedProcess>();
            copy.LastUpdateCheck = null;
            copy.NotifiedVersion = null;
            copy.ResolutionChanged = false;
            copy.FramePermissionAsked = false;
            copy.FpsCapsApplied = new List<string>();
            copy.PowerModeBackup = null;
            return copy;
        }

        /// <summary>Lê um arquivo exportado. Lança exceção se ele não for uma configuração do MouseModes.</summary>
        public static AppConfig Import(string path)
        {
            return Load(path);
        }

        /// <summary>
        /// Passa para esta configuração tudo o que veio de outra, menos o que só vale neste PC (MachineOnly).
        /// Copia propriedade por propriedade: uma configuração nova entra sozinha, sem precisar lembrar daqui.
        /// </summary>
        public void CopySettingsFrom(AppConfig other)
        {
            var instance = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            foreach (System.Reflection.PropertyInfo prop in typeof(AppConfig).GetProperties(instance))
            {
                if (!prop.CanRead || !prop.CanWrite || Array.IndexOf(MachineOnly, prop.Name) >= 0) continue;
                prop.SetValue(this, prop.GetValue(other, null), null);
            }
            if (Hotkey < 0 || Hotkey >= HotkeyWindow.Choices.Length) Hotkey = 0;
        }

        public void RememberPlanCopy(string template, string guid)
        {
            lock (planCopiesGate)
            {
                PlanCopies.RemoveAll(c => c == null || string.Equals(c.Template, template, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(guid)) PlanCopies.Add(new PlanCopy { Template = template, Guid = guid });
            }
        }

        public AppConfig()
        {
            Games = new List<string>();
            AppsToClose = new List<string>();
            GameProfiles = new List<GameProfile>();
            PlanCopies = new List<PlanCopy>();
            CalmBackup = new List<CalmedProcess>();
            FpsCapsApplied = new List<string>();
            AutoSwitch = true;
            RestoreDailyOnExit = true;
            ShowNotifications = true;
            BoostGamePriority = true;
            MonitorCheck = true;
            BlockWinKey = true;
            DisableAccessibilityHotkeys = true;
            SilenceNotifications = true;
            CheckForUpdates = true;
            AutoUpdate = true;
            MeasureNetwork = true;
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
            return LoadOrCreate(FilePath);
        }

        internal static AppConfig LoadOrCreate(string path)
        {
            string tmp = path + ".tmp";
            if (File.Exists(path))
            {
                try
                {
                    return Load(path);
                }
                catch (Exception ex)
                {
                    // Arquivo corrompido: guarda uma cópia e recomeça do padrão.
                    Log.Error(L.T("Configuração ilegível; cópia guardada em ", "Unreadable settings; copy saved as ") + Path.GetFileName(path) + L.T(".bak e padrão recriado", ".bak and defaults recreated"), ex);
                    try { File.Copy(path, path + ".bak", true); } catch { }
                }
            }
            else if (File.Exists(tmp))
            {
                // Versões antigas apagavam o config antes de mover o .tmp: se caíram no meio, o .tmp é o config.
                try
                {
                    AppConfig recovered = Load(tmp);
                    recovered.SaveTo(path);
                    Log.Warn(L.T("Configuração recuperada de ", "Settings recovered from ") + Path.GetFileName(tmp));
                    return recovered;
                }
                catch (Exception ex)
                {
                    Log.Error(L.T("Recuperar ", "Recover ") + Path.GetFileName(tmp), ex);
                }
            }

            var created = CreateDefault();
            created.SaveTo(path);
            return created;
        }

        private static AppConfig Load(string path)
        {
            var serializer = new XmlSerializer(typeof(AppConfig));
            AppConfig loaded;
            using (var stream = File.OpenRead(path))
            {
                loaded = (AppConfig)serializer.Deserialize(stream);
            }

            if (loaded.Performance == null) loaded.Performance = DefaultPerformance(MouseSettings.Read());
            if (loaded.Daily == null) loaded.Daily = DefaultDaily(MouseSettings.Read());
            if (loaded.Games == null) loaded.Games = new List<string>();
            if (loaded.AppsToClose == null) loaded.AppsToClose = new List<string>();
            if (loaded.GameProfiles == null) loaded.GameProfiles = new List<GameProfile>();
            if (loaded.PlanCopies == null) loaded.PlanCopies = new List<PlanCopy>();
            loaded.GameProfiles.RemoveAll(g => g == null || g.Profile == null || string.IsNullOrEmpty(g.Process));
            return loaded;
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
            p.PowerPlan = PowerPlanOption.NaoAlterar; // "ANTERIOR": volta ao plano que a pessoa já usava
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
            SaveTo(FilePath);
        }

        /// <summary>
        /// Grava num .tmp (já no disco) e troca pelo arquivo de uma vez: se o PC desligar no meio,
        /// fica o config antigo ou o novo, nunca nenhum.
        /// </summary>
        internal void SaveTo(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            var serializer = new XmlSerializer(typeof(AppConfig));
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                lock (planCopiesGate) serializer.Serialize(stream, this);
                stream.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            try
            {
                File.Replace(tmp, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                // Sistema de arquivos sem troca atômica (ex.: alguns drives de rede): do jeito antigo.
                File.Delete(path);
                File.Move(tmp, path);
            }
        }
    }
}
