using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    public static class Localizer
    {
        public static AppLang Current = AppLang.FR;

        public static string AppDataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder");
                if (!Directory.Exists(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        // Seamlessly import existing settings from WinCrashDoctor if present
                        string legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCrashDoctor");
                        string legacySettings = Path.Combine(legacyDir, "settings.json");
                        string targetSettings = Path.Combine(dir, "settings.json");
                        if (File.Exists(legacySettings) && !File.Exists(targetSettings))
                            File.Copy(legacySettings, targetSettings);
                    }
                    catch (Exception ex) { Log.Warn(ex); }
                }
                return dir;
            }
        }

        private static string SettingsPath
        {
            get { return Path.Combine(AppDataDir, "settings.json"); }
        }

        public static AppSettings CachedSettings = new AppSettings();

        /// <summary>Set by the --screenshot harness: settings are read but never written.</summary>
        public static bool ReadOnlySettings;

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    var ser = new JavaScriptSerializer();
                    var s = ser.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (string.IsNullOrEmpty(s.OllamaModel)) s.OllamaModel = "llama3.2";
                        s.AiApiKey = SecretStore.Unprotect(s.AiApiKeyProtected);

                        // Migration: settings written by v5/v6.0 held the key in clear text.
                        var raw = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                        object legacy;
                        if (raw != null && raw.TryGetValue("AiApiKey", out legacy) && legacy is string && ((string)legacy).Length > 0)
                        {
                            if (string.IsNullOrEmpty(s.AiApiKey)) s.AiApiKey = (string)legacy;
                            CachedSettings = s;
                            SaveFullSettings(s); // rewrites the file without the clear-text key
                        }
                        CachedSettings = s;
                        return s;
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            CachedSettings = new AppSettings
            {
                Language = (int)DetectSystemLanguage(),
                Theme = (int)AppThemeMode.SystemAuto,
                TraySentinelEnabled = false,
                AiProvider = (int)AiProviderType.Gemini,
                AiApiKey = "",
                OllamaModel = "llama3.2"
            };
            return CachedSettings;
        }

        public static void SaveSettings(AppLang lang, AppThemeMode theme, bool sentinel)
        {
            if (CachedSettings == null) CachedSettings = new AppSettings();
            CachedSettings.Language = (int)lang;
            CachedSettings.Theme = (int)theme;
            CachedSettings.TraySentinelEnabled = sentinel;
            SaveFullSettings(CachedSettings);
        }

        public static void SaveFullSettings(AppSettings settings)
        {
            if (ReadOnlySettings) { CachedSettings = settings; return; }
            try
            {
                CachedSettings = settings;
                settings.AiApiKeyProtected = SecretStore.Protect(settings.AiApiKey);
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(settings);
                File.WriteAllText(SettingsPath, json, Encoding.UTF8);
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        public static AppLang DetectSystemLanguage()
        {
            string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            switch (iso)
            {
                case "fr": return AppLang.FR;
                case "es": return AppLang.ES;
                case "de": return AppLang.DE;
                case "it": return AppLang.IT;
                case "pt": return AppLang.PT;
                default:   return AppLang.EN;
            }
        }

        public static string L(string fr, string en, string es, string de, string it, string pt)
        {
            switch (Current)
            {
                case AppLang.FR: return fr;
                case AppLang.ES: return es;
                case AppLang.DE: return de;
                case AppLang.IT: return it;
                case AppLang.PT: return pt;
                default:         return en;
            }
        }

        /// <summary>
        /// FR/EN engine text. For ES/DE/IT/PT the English text is looked up in the translation table
        /// (I18nTable); dynamic strings that are not in the table stay in English.
        /// </summary>
        public static string T(string fr, string en)
        {
            if (Current == AppLang.FR) return fr ?? en;
            if (Current == AppLang.EN || en == null) return en ?? fr;
            return I18nTable.Translate(en, Current) ?? en;
        }

        /// <summary>Culture matching the app language (dates follow the UI, not the Windows locale).</summary>
        public static CultureInfo Culture
        {
            get
            {
                switch (Current)
                {
                    case AppLang.FR: return CultureInfo.GetCultureInfo("fr-FR");
                    case AppLang.ES: return CultureInfo.GetCultureInfo("es-ES");
                    case AppLang.DE: return CultureInfo.GetCultureInfo("de-DE");
                    case AppLang.IT: return CultureInfo.GetCultureInfo("it-IT");
                    case AppLang.PT: return CultureInfo.GetCultureInfo("pt-BR");
                    default:         return CultureInfo.GetCultureInfo("en-US");
                }
            }
        }

        public static string LanguageDisplayName(AppLang lang)
        {
            switch (lang)
            {
                case AppLang.FR: return "Français";
                case AppLang.EN: return "English";
                case AppLang.ES: return "Español";
                case AppLang.DE: return "Deutsch";
                case AppLang.IT: return "Italiano";
                case AppLang.PT: return "Português";
                default:         return "English";
            }
        }

        public static string CategoryName(CrashCategory cat)
        {
            switch (cat)
            {
                case CrashCategory.BSOD:
                    return L("Écran bleu (BSOD)", "Blue Screen (BSOD)", "Pantalla azul (BSOD)", "Bluescreen (BSOD)", "Schermata blu (BSOD)", "Tela azul (BSOD)");
                case CrashCategory.GpuDriver:
                    return L("Pilote GPU", "GPU Driver", "Controlador GPU", "GPU-Treiber", "Driver GPU", "Driver GPU");
                case CrashCategory.StorageNvme:
                    return L("Stockage NVMe", "NVMe Storage", "Almacenamiento NVMe", "NVMe-Speicher", "Archiviazione NVMe", "Armazenamento NVMe");
                case CrashCategory.HardwareWhea:
                    return L("Matériel (WHEA)", "Hardware (WHEA)", "Hardware (WHEA)", "Hardware (WHEA)", "Hardware (WHEA)", "Hardware (WHEA)");
                case CrashCategory.MemoryExhaustion:
                    return L("Mémoire / RAM", "Memory / RAM", "Memoria / RAM", "Speicher / RAM", "Memoria / RAM", "Memória / RAM");
                case CrashCategory.BrowserGpu:
                    return L("Navigateur GPU", "Browser GPU", "Navegador GPU", "Browser-GPU", "Browser GPU", "Navegador GPU");
                case CrashCategory.AppCrash:
                    return L("Application", "App Crash", "Aplicación", "Anwendung", "Applicazione", "Aplicativo");
                case CrashCategory.UnexpectedReboot:
                    return L("Arrêt imprévu", "Abrupt Reboot", "Reinicio abrupto", "Unerwarteter Neustart", "Riavvio imprevisto", "Reinício inesperado");
                default:
                    return "Info";
            }
        }

        public static string FixGroupName(FixGroup g)
        {
            switch (g)
            {
                case FixGroup.Gpu:
                    return L("Carte graphique", "Graphics (GPU)", "Tarjeta gráfica", "Grafikkarte", "Scheda grafica", "Placa de vídeo");
                case FixGroup.BrowserApps:
                    return L("Navigateurs & Discord", "Browsers & Discord", "Navegadores y Discord", "Browser & Discord", "Browser e Discord", "Navegadores e Discord");
                case FixGroup.StoragePcie:
                    return L("NVMe, PCIe & USB", "NVMe, PCIe & USB", "NVMe, PCIe y USB", "NVMe, PCIe & USB", "NVMe, PCIe e USB", "NVMe, PCIe e USB");
                case FixGroup.SystemMaint:
                    return L("Système & Réparation", "System & Repair", "Sistema y reparación", "System & Reparatur", "Sistema e riparazione", "Sistema e reparo");
                default:
                    return L("Tous", "All", "Todos", "Alle", "Tutti", "Todos");
            }
        }

        public static string StatusBadgeText(FixStatus status)
        {
            switch (status)
            {
                case FixStatus.Applied:
                    return L("Actif", "Active", "Activo", "Aktiv", "Attivo", "Ativo");
                case FixStatus.Recommended:
                    return L("Recommandé", "Recommended", "Recomendado", "Empfohlen", "Consigliato", "Recomendado");
                case FixStatus.Optional:
                    return L("Optionnel", "Optional", "Opcional", "Optional", "Opzionale", "Opcional");
                default:
                    return L("Outil", "Tool", "Herramienta", "Werkzeug", "Strumento", "Ferramenta");
            }
        }

        public static string HealthBadgeText(HealthLevel lvl)
        {
            switch (lvl)
            {
                case HealthLevel.Good:
                    return L("Optimal", "Healthy", "Óptimo", "Optimal", "Ottimale", "Ideal");
                case HealthLevel.Warning:
                    return L("À vérifier", "Check", "Revisar", "Prüfen", "Verifica", "Verificar");
                case HealthLevel.Critical:
                    return L("Critique", "Critical", "Crítico", "Kritisch", "Critico", "Crítico");
                default:
                    return "Info";
            }
        }
    }
}
