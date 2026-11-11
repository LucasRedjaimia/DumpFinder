using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    public static class SystemHealthScanner
    {
        /// <summary>Health-card op (handled in the UI, no admin, no registry): "this BIOS is already the latest".</summary>
        public const string BiosAckOp = "bios_ack:";

        /// <summary>Self-test only: pretend the BIOS has this release date.</summary>
        internal static DateTime? TestBiosDate;

        public static string BiosAckKey(string board, string biosVersion) { return (board ?? "") + "|" + (biosVersion ?? ""); }

        public static List<HardwareCheckItem> ScanHardwareHealth(HardwareSummary hw, List<DiskSmartInfo> adminSmart)
        {
            List<HardwareCheckItem> list = new List<HardwareCheckItem>();

            // 1. BIOS Version & Date
            try
            {
                string biosVer = "-";
                DateTime biosDate = DateTime.MinValue;
                using (var s = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        biosVer = Convert.ToString(mo["SMBIOSBIOSVersion"]).Trim();
                        string rd = Convert.ToString(mo["ReleaseDate"]);
                        if (!string.IsNullOrEmpty(rd))
                            biosDate = ManagementDateTimeConverter.ToDateTime(rd);
                        break;
                    }
                }

                if (TestBiosDate.HasValue) biosDate = TestBiosDate.Value;
                bool oldBios = (biosDate != DateTime.MinValue && (DateTime.Now - biosDate).TotalDays > 540);
                string dateStr = biosDate != DateTime.MinValue ? biosDate.ToString("dd/MM/yyyy") : "-";
                // The app cannot know whether a newer BIOS exists: the user can confirm this one is the latest.
                string biosAckKey = BiosAckKey(hw.BoardName, biosVer);
                bool biosAcked = oldBios && Localizer.CachedSettings != null && Localizer.CachedSettings.BiosLatestAck == biosAckKey;
                bool biosWarn = oldBios && !biosAcked;

                string biosGuideTitle = Localizer.L(
                    "Guide : Mettre à jour le BIOS de votre carte mère",
                    "Guide: How to Update Motherboard BIOS",
                    "Guía: Actualizar la BIOS de la placa base",
                    "Anleitung: Mainboard-BIOS aktualisieren",
                    "Guida: Come aggiornare il BIOS della scheda madre",
                    "Guia: Como atualizar a BIOS da placa-mãe");

                string biosGuideSteps = Localizer.L(
                    "1. Notez le modèle exact de votre carte mère (" + hw.BoardName + ") et votre version actuelle (" + biosVer + ").\r\n" +
                    "2. Rendez-vous sur la page Support officielle du fabricant (ASUS, MSI, Gigabyte, ASRock) et téléchargez le dernier BIOS stable.\r\n" +
                    "3. Décompressez l'archive et copiez le fichier sur une clé USB formatée en FAT32.\r\n" +
                    "4. Redémarrez le PC et tapotez la touche 'Suppr' ou 'F2' au démarrage pour entrer dans le BIOS UEFI.\r\n" +
                    "5. Lancez l'utilitaire de mise à jour (EZ-Flash, M-Flash ou Q-Flash) et sélectionnez le fichier.\r\n" +
                    "⚠️ Ne jamais éteindre ou débrancher le PC pendant le flashage.",
                    "1. Note your exact motherboard model (" + hw.BoardName + ") and current version (" + biosVer + ").\r\n" +
                    "2. Download the latest stable BIOS from the manufacturer's official support page.\r\n" +
                    "3. Extract and copy the BIOS file onto a FAT32-formatted USB flash drive.\r\n" +
                    "4. Reboot your PC and press 'Del' or 'F2' repeatedly to enter UEFI BIOS.\r\n" +
                    "5. Launch the update utility (EZ-Flash, M-Flash, Q-Flash) and select the file.\r\n" +
                    "⚠️ Never power off or unplug the PC during BIOS flashing.",
                    "1. Anote el modelo de placa base (" + hw.BoardName + ") y versión actual (" + biosVer + ").\r\n2. Descargue la última BIOS estable de la web oficial del fabricante.\r\n3. Descomprima y copie el archivo en una memoria USB en FAT32.\r\n4. Reinicie y pulse 'Supr' o 'F2' para entrar en la BIOS.\r\n5. Use EZ-Flash, M-Flash o Q-Flash para actualizar.",
                    "1. Notieren Sie das Mainboard-Modell (" + hw.BoardName + ") und die Version (" + biosVer + ").\r\n2. Laden Sie das neueste stabile BIOS von der Herstellerseite herunter.\r\n3. Entpacken Sie die Datei auf einen FAT32-formatierten USB-Stick.\r\n4. Starten Sie neu und drücken Sie 'Entf' oder 'F2'.\r\n5. Starten Sie EZ-Flash, M-Flash oder Q-Flash zur Aktualisierung.",
                    "1. Annota il modello della scheda madre (" + hw.BoardName + ") e la versione (" + biosVer + ").\r\n2. Scarica l'ultimo BIOS stabile dal sito del produttore.\r\n3. Estrai e copia il file su una chiavetta USB in FAT32.\r\n4. Riavvia premendo 'Canc' o 'F2' per accedere al BIOS.\r\n5. Avvia EZ-Flash, M-Flash o Q-Flash per procedere.",
                    "1. Anote o modelo da placa-mãe (" + hw.BoardName + ") e a versão atual (" + biosVer + ").\r\n2. Baixe a BIOS estável mais recente no site do fabricante.\r\n3. Copie o arquivo descompactado para um pendrive FAT32.\r\n4. Reinicie pressionando 'Del' ou 'F2' para entrar na BIOS.\r\n5. Use EZ-Flash, M-Flash ou Q-Flash para atualizar.");

                list.Add(new HardwareCheckItem
                {
                    CategoryTag = hw.HasAmdCpu ? "BIOS / AGESA (AMD)" : (hw.HasIntelCpu ? "BIOS / Microcode (Intel)" : "BIOS Carte Mère"),
                    Title = Localizer.L("Version et date du BIOS carte mère", "Motherboard BIOS Version & Date", "Versión y fecha de BIOS", "Mainboard-BIOS-Version & Datum", "Versione e data BIOS scheda madre", "Versão e data da BIOS"),
                    ValueSummary = string.Format("BIOS {0} ({1})", biosVer, dateStr),
                    Detail = biosAcked
                        ? Localizer.L("Tu as indiqué que c'est la dernière version disponible pour " + hw.BoardName + ". L'alerte reviendra seulement si le BIOS change.",
                                      "You confirmed this is the latest version available for " + hw.BoardName + ". The warning only comes back if the BIOS changes.",
                                      "Indicaste que es la última versión disponible para " + hw.BoardName + ". El aviso solo volverá si cambia la BIOS.",
                                      "Du hast bestätigt, dass dies die neueste Version für " + hw.BoardName + " ist. Die Warnung erscheint nur wieder, wenn sich das BIOS ändert.",
                                      "Hai indicato che è l'ultima versione disponibile per " + hw.BoardName + ". L'avviso tornerà solo se il BIOS cambia.",
                                      "Você indicou que é a versão mais recente para " + hw.BoardName + ". O aviso só volta se a BIOS mudar.")
                        : oldBios
                        ? (hw.HasAmdCpu
                            ? Localizer.L("Ce BIOS a plus de 18 mois. Regarde sur la page support de ta carte mère s'il existe une version plus récente : les mises à jour AGESA corrigent souvent la stabilité DDR5 et Ryzen. Si c'est déjà la dernière, indique-le avec le bouton ci-dessous.",
                                          "This BIOS is over 18 months old. Check your motherboard's support page for a newer version: AGESA updates often fix DDR5 and Ryzen stability. If it is already the latest, say so with the button below.",
                                          "Esta BIOS tiene más de 18 meses. Busca en la página de soporte de tu placa una versión más reciente: las actualizaciones AGESA suelen mejorar la estabilidad DDR5 y Ryzen. Si ya es la última, indícalo con el botón de abajo.",
                                          "Dieses BIOS ist älter als 18 Monate. Prüfe auf der Support-Seite deines Mainboards, ob es eine neuere Version gibt: AGESA-Updates verbessern oft die DDR5- und Ryzen-Stabilität. Ist es bereits die neueste, bestätige das mit der Schaltfläche unten.",
                                          "Questo BIOS ha più di 18 mesi. Controlla sulla pagina di supporto della scheda madre se esiste una versione più recente: gli aggiornamenti AGESA migliorano spesso la stabilità DDR5 e Ryzen. Se è già l'ultima, indicalo con il pulsante qui sotto.",
                                          "Esta BIOS tem mais de 18 meses. Veja na página de suporte da sua placa-mãe se há uma versão mais recente: as atualizações AGESA costumam melhorar a estabilidade DDR5 e Ryzen. Se já for a última, indique isso no botão abaixo.")
                            : Localizer.L("Ce BIOS a plus de 18 mois. Regarde sur la page support de ta carte mère s'il existe une version plus récente : les mises à jour du microcode corrigent souvent la stabilité du processeur et de la mémoire. Si c'est déjà la dernière, indique-le avec le bouton ci-dessous.",
                                          "This BIOS is over 18 months old. Check your motherboard's support page for a newer version: microcode updates often fix CPU and memory stability. If it is already the latest, say so with the button below.",
                                          "Esta BIOS tiene más de 18 meses. Busca en la página de soporte de tu placa una versión más reciente: las actualizaciones de microcódigo suelen mejorar la estabilidad. Si ya es la última, indícalo con el botón de abajo.",
                                          "Dieses BIOS ist älter als 18 Monate. Prüfe auf der Support-Seite deines Mainboards, ob es eine neuere Version gibt: Microcode-Updates verbessern oft die Stabilität. Ist es bereits die neueste, bestätige das mit der Schaltfläche unten.",
                                          "Questo BIOS ha più di 18 mesi. Controlla sulla pagina di supporto della scheda madre se esiste una versione più recente: gli aggiornamenti del microcodice migliorano spesso la stabilità. Se è già l'ultima, indicalo con il pulsante qui sotto.",
                                          "Esta BIOS tem mais de 18 meses. Veja na página de suporte da sua placa-mãe se há uma versão mais recente: as atualizações de microcódigo costumam melhorar a estabilidade. Se já for a última, indique isso no botão abaixo."))
                        : Localizer.L("Firmware BIOS récent sur " + hw.BoardName + ".", "Up-to-date BIOS firmware on " + hw.BoardName + ".", "Firmware BIOS reciente en " + hw.BoardName + ".", "Aktuelles BIOS auf " + hw.BoardName + ".", "Firmware BIOS recente su " + hw.BoardName + ".", "Firmware BIOS recente em " + hw.BoardName + "."),
                    Level = biosWarn ? HealthLevel.Warning : HealthLevel.Good,
                    InAppFixOp = biosWarn ? BiosAckOp + biosAckKey : null,
                    InAppFixLabel = biosWarn ? Localizer.L("✓ C'est déjà la dernière version", "✓ It's already the latest version", "✓ Ya es la última versión", "✓ Bereits die neueste Version", "✓ È già l'ultima versione", "✓ Já é a versão mais recente") : null,
                    GuideTitle = biosGuideTitle,
                    GuideSteps = biosGuideSteps,
                    QuickActionLabel = Localizer.L("🔍 Chercher le BIOS officiel", "🔍 Find official BIOS", "🔍 Buscar BIOS oficial", "🔍 Offizielles BIOS suchen", "🔍 Cerca BIOS ufficiale", "🔍 Buscar BIOS oficial"),
                    QuickActionCommand = "web:search",
                    WebSearchQuery = hw.BoardName + " motherboard BIOS update download official"
                });
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 2. RAM Capacity, Speed & EXPO / XMP Check
            try
            {
                long totalBytes = 0;
                int sticks = 0;
                int cfgSpeed = 0;
                string partNum = "";
                string vendor = "";

                using (var s = new ManagementObjectSearcher("SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        sticks++;
                        totalBytes += Convert.ToInt64(mo["Capacity"]);
                        int cs = Convert.ToInt32(mo["ConfiguredClockSpeed"] ?? mo["Speed"] ?? 0);
                        if (cs > cfgSpeed) cfgSpeed = cs;
                        partNum = Convert.ToString(mo["PartNumber"]).Trim();
                        vendor = Convert.ToString(mo["Manufacturer"]).Trim();
                    }
                }

                double gb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0));
                int ratedSpeed = cfgSpeed;
                Match mRated = Regex.Match(partNum, @"(3200|3600|4000|5200|5600|6000|6400|6800|7200|7600|8000)");
                if (mRated.Success) int.TryParse(mRated.Groups[1].Value, out ratedSpeed);

                bool xmpDisabled = (cfgSpeed == 4800 && ratedSpeed > 4800) || (cfgSpeed == 2133 && ratedSpeed > 2133) || (cfgSpeed == 2400 && ratedSpeed > 2400);

                string ramCat = hw.HasAmdCpu ? "RAM • EXPO / DOCP" : (hw.HasIntelCpu ? "RAM • XMP" : "RAM • XMP / EXPO");
                string ramTitle = hw.HasAmdCpu
                    ? Localizer.L("Mémoire vive (RAM) & Profil EXPO / DOCP (AMD)", "System Memory (RAM) & EXPO / DOCP Profile (AMD)", "Memoria RAM y perfil EXPO / DOCP (AMD)", "Arbeitsspeicher (RAM) & EXPO/DOCP (AMD)", "Memoria RAM e profilo EXPO/DOCP (AMD)", "Memória RAM e perfil EXPO/DOCP (AMD)")
                    : (hw.HasIntelCpu
                        ? Localizer.L("Mémoire vive (RAM) & Profil XMP (Intel)", "System Memory (RAM) & XMP Profile (Intel)", "Memoria RAM y perfil XMP (Intel)", "Arbeitsspeicher (RAM) & XMP (Intel)", "Memoria RAM e profilo XMP (Intel)", "Memória RAM e perfil XMP (Intel)")
                        : Localizer.L("Mémoire vive (RAM) & Profil XMP / EXPO", "System Memory (RAM) & XMP / EXPO Profile", "Memoria RAM y perfil XMP / EXPO", "Arbeitsspeicher (RAM) & XMP / EXPO", "Memoria RAM e profilo XMP / EXPO", "Memória RAM e perfil XMP / EXPO"));

                string ramGuideTitle = hw.HasAmdCpu
                    ? Localizer.L("Guide : Activer le profil EXPO / DOCP dans le BIOS", "Guide: Enable AMD EXPO / DOCP in BIOS", "Guía: Activar perfil EXPO en BIOS", "Anleitung: AMD EXPO/DOCP im BIOS aktivieren", "Guida: Abilitare AMD EXPO nel BIOS", "Guia: Ativar AMD EXPO na BIOS")
                    : Localizer.L("Guide : Activer le profil Intel XMP dans le BIOS", "Guide: Enable Intel XMP in BIOS", "Guía: Activar perfil Intel XMP en BIOS", "Anleitung: Intel XMP im BIOS aktivieren", "Guida: Abilitare Intel XMP nel BIOS", "Guia: Ativar Intel XMP na BIOS");

                string ramGuideSteps = Localizer.L(
                    "1. Redémarrez le PC et tapotez la touche 'Suppr' (Del) ou 'F2' dès l'allumage pour entrer dans le BIOS UEFI.\r\n" +
                    "2. Passez en mode Avancé (touche F7 sur la plupart des cartes mères ASUS, MSI et ASRock).\r\n" +
                    "3. " + (hw.HasAmdCpu ? "Sur processeur AMD : cherchez 'EXPO' ou 'D.O.C.P.' dans l'onglet 'Ai Tweaker' ou 'OC'." : "Sur processeur Intel : cherchez 'XMP' (Extreme Memory Profile) dans l'onglet 'Ai Tweaker' ou 'OC'.") + "\r\n" +
                    "4. Sélectionnez 'Profile 1' (fréquence nominale de vos barrettes, ex: " + ratedSpeed + " MT/s).\r\n" +
                    "5. Appuyez sur la touche F10 pour Sauvegarder et Quitter (Save & Exit).\r\n" +
                    "💡 Conseil : Si le premier redémarrage prend 30 à 60 secondes écran noir, c'est l'entraînement mémoire normal (Memory Training).",
                    "1. Reboot your PC and press 'Del' or 'F2' repeatedly to enter UEFI BIOS.\r\n" +
                    "2. Switch to Advanced Mode (F7 on ASUS, MSI, and ASRock).\r\n" +
                    "3. " + (hw.HasAmdCpu ? "On AMD CPU: find 'EXPO' or 'D.O.C.P.' under 'Ai Tweaker' or 'OC'." : "On Intel CPU: find 'XMP' (Extreme Memory Profile) under 'Ai Tweaker' or 'OC'.") + "\r\n" +
                    "4. Select 'Profile 1' (rated frequency of your sticks, e.g. " + ratedSpeed + " MT/s).\r\n" +
                    "5. Press F10 to Save & Exit.\r\n" +
                    "💡 Tip: If first boot takes 30-60s on a black screen, that is standard DDR5 Memory Training.",
                    "1. Reinicie y pulse 'Supr' o 'F2' para entrar en la BIOS.\r\n2. Modo Avanzado (F7).\r\n3. Active el perfil EXPO o XMP 1.\r\n4. Pulse F10 para guardar y salir.",
                    "1. Neu starten und 'Entf' oder 'F2' drücken.\r\n2. Erweiterten Modus öffnen (F7).\r\n3. EXPO- bzw. XMP-Profil 1 aktivieren.\r\n4. Mit F10 speichern und neu starten.",
                    "1. Riavvia premendo 'Canc' o 'F2'.\r\n2. Passa alla modalità avanzata (F7).\r\n3. Abilita profilo EXPO o XMP 1.\r\n4. Salva con F10 e riavvia.",
                    "1. Reinicie e pressione 'Del' ou 'F2'.\r\n2. Modo Avançado (F7).\r\n3. Ative o perfil EXPO ou XMP 1.\r\n4. Salve com F10 e reinicie.");

                list.Add(new HardwareCheckItem
                {
                    CategoryTag = ramCat,
                    Title = ramTitle,
                    ValueSummary = string.Format("{0} GB ({1}x) @ {2} MT/s  •  {3} {4}", gb, sticks, cfgSpeed, vendor, partNum).Trim(),
                    Detail = xmpDisabled
                        ? (hw.HasAmdCpu
                            ? Localizer.L(
                                string.Format("Vos barrettes ({0}) tournent à la vitesse de base ({1} MT/s) au lieu de {2} MT/s. Le profil AMD EXPO / DOCP semble désactivé dans le BIOS.", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Your memory sticks ({0}) are running at base JEDEC speed ({1} MT/s) instead of {2} MT/s. AMD EXPO / DOCP appears disabled in BIOS.", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Sus módulos ({0}) funcionan a {1} MT/s en lugar de {2} MT/s (EXPO desactivado).", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Ihre Module ({0}) laufen mit {1} MT/s statt {2} MT/s (EXPO im BIOS deaktiviert).", partNum, cfgSpeed, ratedSpeed),
                                string.Format("I moduli ({0}) operano a {1} MT/s invece di {2} MT/s (EXPO disattivato).", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Seus módulos ({0}) estão a {1} MT/s em vez de {2} MT/s (EXPO desativado).", partNum, cfgSpeed, ratedSpeed))
                            : Localizer.L(
                                string.Format("Vos barrettes ({0}) tournent à la vitesse de base ({1} MT/s) au lieu de {2} MT/s. Le profil Intel XMP semble désactivé dans le BIOS.", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Your memory sticks ({0}) are running at base JEDEC speed ({1} MT/s) instead of {2} MT/s. Intel XMP appears disabled in BIOS.", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Sus módulos ({0}) funcionan a {1} MT/s en lugar de {2} MT/s (XMP desactivado).", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Ihre Module ({0}) laufen mit {1} MT/s statt {2} MT/s (XMP im BIOS deaktiviert).", partNum, cfgSpeed, ratedSpeed),
                                string.Format("I moduli ({0}) operano a {1} MT/s invece di {2} MT/s (XMP disattivato).", partNum, cfgSpeed, ratedSpeed),
                                string.Format("Seus módulos ({0}) estão a {1} MT/s em vez de {2} MT/s (XMP desativado).", partNum, cfgSpeed, ratedSpeed)))
                        : Localizer.L(
                            "Fréquence mémoire optimale (" + cfgSpeed + " MT/s actif). Utilisez le profil « Test Mémoire RAM » dans Stress Test pour valider sa stabilité à chaud.",
                            "Memory frequency is optimal (" + cfgSpeed + " MT/s active). Use the 'RAM Stability Test' profile in Stress Test to verify thermal stability.",
                            "Frecuencia de memoria óptima (" + cfgSpeed + " MT/s activo).",
                            "Optimale Speicherfrequenz (" + cfgSpeed + " MT/s aktiv).",
                            "Frequenza memoria ottimale (" + cfgSpeed + " MT/s attivo).",
                            "Frequência de memória ideal (" + cfgSpeed + " MT/s ativo)."),
                    Level = xmpDisabled ? HealthLevel.Warning : HealthLevel.Good,
                    GuideTitle = ramGuideTitle,
                    GuideSteps = ramGuideSteps,
                    QuickActionLabel = Localizer.L("🧪 Tester la stabilité RAM", "🧪 Run RAM Stability Test", "🧪 Probar estabilidad RAM", "🧪 RAM-Stabilität testen", "🧪 Testa stabilità RAM", "🧪 Testar estabilidade RAM"),
                    QuickActionCommand = "nav:stress_ram"
                });
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 3. Resizable BAR & GPU Memory Window
            try
            {
                long bar1Mib = DetectGpuBar1Mib();
                bool rebarActive = (bar1Mib >= 512);
                string activeTag = Localizer.L("Actif", "Active", "Activo", "Aktiv", "Attivo", "Ativo");
                string rebarLabel = hw.HasAmdGpu ? "AMD SAM " : (hw.HasNvidiaGpu ? "NVIDIA ReBAR " : "Resizable BAR ");
                string barSummary = bar1Mib >= 1024
                    ? string.Format("{0}{1} ({2:F0} GB BAR1)", rebarLabel, activeTag, bar1Mib / 1024.0)
                    : (rebarActive ? (rebarLabel + activeTag) : "Standard (256 MB BAR)");

                string rebarCat = hw.HasAmdGpu ? "PCIe • AMD SAM" : (hw.HasNvidiaGpu ? "PCIe • NVIDIA ReBAR" : "PCIe • ReBAR");
                string rebarTitle = hw.HasAmdGpu
                    ? Localizer.L("AMD Smart Access Memory (SAM) / Resizable BAR", "AMD Smart Access Memory (SAM) / Resizable BAR", "AMD Smart Access Memory (SAM) / Resizable BAR", "AMD Smart Access Memory (SAM) / Resizable BAR", "AMD Smart Access Memory (SAM) / Resizable BAR", "AMD Smart Access Memory (SAM) / Resizable BAR")
                    : (hw.HasNvidiaGpu
                        ? Localizer.L("NVIDIA Resizable BAR (Accès direct VRAM CPU ↔ GPU)", "NVIDIA Resizable BAR (Direct CPU ↔ GPU VRAM Access)", "NVIDIA Resizable BAR (Acceso directo VRAM)", "NVIDIA Resizable BAR (Direkter VRAM-Zugriff)", "NVIDIA Resizable BAR (Accesso diretto VRAM)", "NVIDIA Resizable BAR (Acesso direto à VRAM)")
                        : Localizer.L("Resizable BAR (Accès direct VRAM CPU ↔ GPU)", "Resizable BAR (Full CPU ↔ GPU VRAM Access)", "Resizable BAR (Acceso directo VRAM)", "Resizable BAR (Direkter VRAM-Zugriff)", "Resizable BAR (Accesso diretto VRAM)", "Resizable BAR (Acesso direto à VRAM)"));

                string rebarGuideTitle = Localizer.L(
                    "Guide : Activer Resizable BAR dans le BIOS",
                    "Guide: Enable Resizable BAR in BIOS",
                    "Guía: Activar Resizable BAR en BIOS",
                    "Anleitung: Resizable BAR im BIOS aktivieren",
                    "Guida: Abilitare Resizable BAR nel BIOS",
                    "Guia: Ativar Resizable BAR na BIOS");

                string rebarGuideSteps = Localizer.L(
                    "1. Redémarrez le PC et appuyez sur 'Suppr' ou 'F2' pour entrer dans le BIOS UEFI.\r\n" +
                    "2. Vérifiez que le mode 'CSM' (Compatibility Support Module) est bien DÉSACTIVÉ dans l'onglet Boot (mode UEFI requis).\r\n" +
                    "3. Rendez-vous dans 'Advanced' > 'PCI Subsystem Settings' (ou 'PCIe Configuration').\r\n" +
                    "4. Activez 'Above 4G Decoding' (Décodage au-dessus de 4G) = Activé (Enabled).\r\n" +
                    "5. Activez 'Re-Size BAR Support' = Auto ou Activé.\r\n" +
                    "6. Appuyez sur F10 pour enregistrer et redémarrer.\r\n" +
                    "💡 Gain : Débloque 5 à 15% de performances en jeu et élimine les micro-saccades de transfert VRAM.",
                    "1. Reboot your PC and press 'Del' or 'F2' to enter UEFI BIOS.\r\n" +
                    "2. Ensure CSM (Compatibility Support Module) is DISABLED under Boot (pure UEFI required).\r\n" +
                    "3. Navigate to 'Advanced' > 'PCI Subsystem Settings'.\r\n" +
                    "4. Set 'Above 4G Decoding' = Enabled.\r\n" +
                    "5. Set 'Re-Size BAR Support' = Auto or Enabled.\r\n" +
                    "6. Press F10 to Save & Exit.\r\n" +
                    "💡 Gain: Unlocks 5-15% gaming performance and prevents VRAM frame pacing bottlenecks.",
                    "1. Reinicie y entre en la BIOS (Supr/F2).\r\n2. Desactive CSM (modo UEFI).\r\n3. Active 'Above 4G Decoding' y 'Re-Size BAR Support'.\r\n4. Guarde con F10.",
                    "1. Neu starten und ins BIOS (Entf/F2).\r\n2. CSM deaktivieren (reines UEFI).\r\n3. 'Above 4G Decoding' und 'Re-Size BAR Support' aktivieren.\r\n4. Mit F10 speichern.",
                    "1. Riavvia ed entra nel BIOS (Canc/F2).\r\n2. Disattiva CSM (modalità UEFI).\r\n3. Attiva 'Above 4G Decoding' e 'Re-Size BAR Support'.\r\n4. Salva con F10.",
                    "1. Reinicie e entre na BIOS (Del/F2).\r\n2. Desative o CSM (modo UEFI).\r\n3. Ative 'Above 4G Decoding' e 'Re-Size BAR Support'.\r\n4. Salve com F10.");

                list.Add(new HardwareCheckItem
                {
                    CategoryTag = rebarCat,
                    Title = rebarTitle,
                    ValueSummary = barSummary,
                    Detail = rebarActive
                        ? Localizer.L("Le processeur accède directement à toute la mémoire vidéo de la carte graphique sans goulot d'étranglement de 256 Mo.", "Your CPU can directly address the full GPU frame buffer without the legacy 256 MB bottleneck.", "La CPU accede directamente a toda la memoria de vídeo sin el límite de 256 MB.", "Die CPU greift ohne das 256-MB-Limit direkt auf den gesamten Grafikspeicher zu.", "La CPU accede direttamente a tutta la VRAM senza il limite di 256 MB.", "A CPU acessa diretamente toda a VRAM sem o limite antigo de 256 MB.")
                        : (hw.HasIntelArcGpu
                            ? Localizer.L("CRITIQUE sur Intel Arc : Resizable BAR est désactivé (256 Mo) ! Activez impérativement Above 4G Decoding + Re-Size BAR dans le BIOS.", "CRITICAL on Intel Arc: Resizable BAR is OFF (256 MB)! Enable Above 4G Decoding + Re-Size BAR in BIOS immediately.", "CRÍTICO en Intel Arc: ¡Active Resizable BAR en la BIOS!", "KRITISCH bei Intel Arc: Aktivieren Sie Resizable BAR im BIOS!", "CRITICO su Intel Arc: Attiva subito Resizable BAR nel BIOS!", "CRÍTICO na Intel Arc: Ative o Resizable BAR na BIOS!")
                            : Localizer.L("Resizable BAR semble limité à 256 Mo. Vous pouvez l'activer (« Above 4G Decoding » + « Re-Size BAR ») dans le BIOS de votre carte mère.", "Resizable BAR appears capped at 256 MB. You can enable 'Above 4G Decoding' + 'Re-Size BAR' in your motherboard BIOS.", "Resizable BAR parece limitado a 256 MB.", "Resizable BAR scheint auf 256 MB begrenzt zu sein.", "Resizable BAR sembra limitato a 256 MB.", "O Resizable BAR parece limitado a 256 MB.")),
                    Level = rebarActive ? HealthLevel.Good : (hw.HasIntelArcGpu ? HealthLevel.Critical : HealthLevel.Info),
                    GuideTitle = rebarGuideTitle,
                    GuideSteps = rebarGuideSteps,
                    QuickActionLabel = Localizer.L("🛠 Gestionnaire de périphériques", "🛠 Device Manager", "🛠 Administrador de dispositivos", "🛠 Geräte-Manager", "🛠 Gestione dispositivi", "🛠 Gerenciador de Dispositivos"),
                    QuickActionCommand = "tool:devmgmt"
                });
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 3b. GPU PCIe Link Width & Signal Integrity
            try
            {
                int genCur, genMax, widthCur, widthMax;
                if (DetectGpuPcieLink(out genCur, out genMax, out widthCur, out widthMax) && widthMax > 0)
                {
                    bool degradedWidth = (widthCur > 0 && widthCur < widthMax && widthMax >= 8);

                    string pcieGuideTitle = Localizer.L(
                        "Guide : Résoudre un lien PCIe dégradé (x8/x1 au lieu de x16)",
                        "Guide: Resolve Degraded PCIe Link Width",
                        "Guía: Resolver enlace PCIe degradado",
                        "Anleitung: Degradierten PCIe-Link beheben",
                        "Guida: Risolvere link PCIe degradato",
                        "Guia: Resolver link PCIe degradado");

                    string pcieGuideSteps = Localizer.L(
                        "1. Vérifiez que la carte graphique est insérée dans le premier port PCIe x16 tout en haut (directement relié au CPU).\r\n" +
                        "2. Si vous utilisez un riser vertical (boîtiers ITX) : entrez dans le BIOS et forcez la vitesse du slot PCIe sur 'Gen 4' ou 'Gen 3' au lieu de 'Auto'.\r\n" +
                        "3. Éteignez le PC, débranchez la prise secteur, retirez la carte et réenclenchez-la fermement jusqu'au clic du loquet.\r\n" +
                        "4. Nettoyez les contacts dorés du port PCIe avec une bombe à air sec.\r\n" +
                        "5. Vérifiez que chaque connecteur 8-pins PCIe est alimenté par un câble dédié direct venant de l'alimentation.",
                        "1. Verify GPU is seated in the topmost PCIe x16 slot (wired directly to CPU lanes).\r\n" +
                        "2. If using a vertical riser cable (ITX cases): enter BIOS and manually force PCIe speed to 'Gen 4' or 'Gen 3' instead of 'Auto'.\r\n" +
                        "3. Shut down, unplug PSU, reseat GPU firmly until the slot latch clicks.\r\n" +
                        "4. Blow dust off PCIe gold fingers with compressed air.\r\n" +
                        "5. Ensure separate 8-pin PCIe power cables are used (avoid daisy-chain pigtails).",
                        "1. Verifique que la GPU esté en la primera ranura PCIe x16 superior.\r\n2. Con riser, fuerce Gen 4 en BIOS.\r\n3. Retire y vuelva a encajar firmemente la GPU.\r\n4. Use cables de alimentación dedicados.",
                        "1. Prüfen Sie, ob die GPU im obersten PCIe-x16-Slot sitzt.\r\n2. Bei Riser-Kabel im BIOS Gen 4 erzwingen.\r\n3. GPU fest neu einsetzen.\r\n4. Separate 8-Pin-Kabel verwenden.",
                        "1. Verifica che la GPU sia nel primo slot PCIe x16.\r\n2. Con cavo riser, forza Gen 4 nel BIOS.\r\n3. Reinserisci saldamente la scheda.\r\n4. Usa cavi PCIe dedicati.",
                        "1. Verifique se a GPU está no primeiro slot PCIe x16.\r\n2. Com riser, force Gen 4 na BIOS.\r\n3. Reencaixe firmemente a placa.\r\n4. Use cabos PCIe separados.");

                    list.Add(new HardwareCheckItem
                    {
                        CategoryTag = Localizer.L("GPU • LIEN PCIe", "GPU • PCIe LINK", "GPU • ENLACE PCIe", "GPU • PCIe-LINK", "GPU • LINK PCIe", "GPU • LINK PCIe"),
                        Title = Localizer.L("Intégrité du lien PCIe Carte Graphique", "Graphics Card PCIe Link Integrity", "Integridad del enlace PCIe GPU", "GPU-PCIe-Verbindungsintegrität", "Integrità collegamento PCIe GPU", "Integridade do link PCIe da GPU"),
                        ValueSummary = Localizer.L(
                            string.Format("PCIe Gen {0} (max GPU Gen {1})  •  Largeur x{2} / x{3}", genCur, genMax, widthCur, widthMax),
                            string.Format("PCIe Gen {0} (GPU max Gen {1})  •  Width x{2} / x{3}", genCur, genMax, widthCur, widthMax),
                            string.Format("PCIe Gen {0} (máx GPU Gen {1})  •  Ancho x{2} / x{3}", genCur, genMax, widthCur, widthMax),
                            string.Format("PCIe Gen {0} (GPU max. Gen {1})  •  Breite x{2} / x{3}", genCur, genMax, widthCur, widthMax),
                            string.Format("PCIe Gen {0} (max GPU Gen {1})  •  Larghezza x{2} / x{3}", genCur, genMax, widthCur, widthMax),
                            string.Format("PCIe Gen {0} (máx GPU Gen {1})  •  Largura x{2} / x{3}", genCur, genMax, widthCur, widthMax)),
                        Detail = degradedWidth
                            ? Localizer.L(
                                string.Format("Attention : votre GPU tourne en x{0} au lieu de x{1} ! Vérifiez l'enclenchement dans le port PCIe (et les câbles d'alimentation), ou forcez PCIe Gen 4 dans le BIOS si vous utilisez un riser.", widthCur, widthMax),
                                string.Format("Warning: GPU is running at x{0} instead of x{1}! Reseat the GPU (and PCIe power cables) firmly, or force PCIe Gen 4 in BIOS if using a riser cable.", widthCur, widthMax),
                                string.Format("Atención: la GPU funciona a x{0} en lugar de x{1}. Revise la ranura PCIe o fuerce Gen 4 en BIOS.", widthCur, widthMax),
                                string.Format("Achtung: GPU läuft mit x{0} statt x{1}! Sitz im PCIe-Slot prüfen oder im BIOS Gen 4 erzwingen.", widthCur, widthMax),
                                string.Format("Attenzione: la GPU opera a x{0} invece di x{1}. Controlla lo slot PCIe o forza Gen 4 nel BIOS.", widthCur, widthMax),
                                string.Format("Atenção: a GPU está em x{0} em vez de x{1}. Verifique o slot PCIe ou force Gen 4 na BIOS.", widthCur, widthMax))
                            : Localizer.L(
                                string.Format("Pleine bande passante x{0} négociée sans perte de lignes sur le port PCIe (Gen {1}).", widthCur, genCur),
                                string.Format("Full x{0} PCIe lane width negotiated cleanly on motherboard slot (Gen {1}).", widthCur, genCur),
                                string.Format("Ancho completo x{0} negociado limpiamente en la ranura PCIe (Gen {1}).", widthCur, genCur),
                                string.Format("Volle x{0}-Anbindung sauber auf dem PCIe-Steckplatz ausgehandelt (Gen {1}).", widthCur, genCur),
                                string.Format("Larghezza piena x{0} negoziata correttamente sullo slot PCIe (Gen {1}).", widthCur, genCur),
                                string.Format("Largura total x{0} negociada corretamente no slot PCIe (Gen {1}).", widthCur, genCur)),
                        Level = degradedWidth ? HealthLevel.Warning : HealthLevel.Good,
                        InAppFixOp = degradedWidth ? "pcie_aspm" : null,
                        InAppFixLabel = degradedWidth ? Localizer.L("⚡ Désactiver veille PCIe ASPM", "⚡ Disable PCIe ASPM", "⚡ Desactivar PCIe ASPM", "⚡ PCIe-ASPM deaktivieren", "⚡ Disattiva PCIe ASPM", "⚡ Desativar PCIe ASPM") : null,
                        GuideTitle = pcieGuideTitle,
                        GuideSteps = pcieGuideSteps,
                        QuickActionLabel = Localizer.L("🛠 Gestionnaire de périphériques", "🛠 Device Manager", "🛠 Administrador de dispositivos", "🛠 Geräte-Manager", "🛠 Gestione dispositivi", "🛠 Gerenciador de Dispositivos"),
                        QuickActionCommand = "tool:devmgmt"
                    });
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 4. NVMe / SSD Disk Health & SMART
            try
            {
                using (var s = new ManagementObjectSearcher(@"\\.\root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string devId = Convert.ToString(mo["DeviceId"]).Trim();
                        string model = Convert.ToString(mo["FriendlyName"]).Trim();
                        ushort health = Convert.ToUInt16(mo["HealthStatus"] ?? 0);
                        ushort bus = Convert.ToUInt16(mo["BusType"] ?? 0);
                        double sizeGb = Math.Round(Convert.ToDouble(mo["Size"] ?? 0) / (1000.0 * 1000.0 * 1000.0));

                        bool isNvme = (bus == 17);
                        // HMB advice only on an NVMe drive under Windows 11 24H2+, where the issue exists.
                        bool isDramLessHmb = isNvme && Detection.NvmeDrives.WindowsHasHmbIssue && Detection.NvmeDrives.IsDramLess(model);

                        DiskSmartInfo smart = (adminSmart != null) ? adminSmart.FirstOrDefault(x => x.DeviceId == devId) : null;
                        if (smart == null) smart = TryReadSingleSmartCounter(devId);

                        string smartStr = (smart != null && (smart.TemperatureC > 0 || smart.PowerOnHours > 0))
                            ? string.Format("  •  {0} °C  •  Usure/Wear: {1}%  •  Erreurs: {2}", smart.TemperatureC, smart.WearPct, smart.UncorrectedErrors)
                            : "";

                        string hStatus = (health == 0)
                            ? Localizer.L("Sain (SMART OK)", "Healthy (SMART OK)", "Sano (SMART OK)", "Gesund (SMART OK)", "Sano (SMART OK)", "Saudável (SMART OK)")
                            : Localizer.L("Alerte SMART (" + health + ")", "SMART Warning (" + health + ")", "Alerta SMART", "SMART-Warnung", "Avviso SMART", "Alerta SMART");

                        bool isHmbApplied = false;
                        try
                        {
                            isHmbApplied = (DiagnosticsEngine.ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy") == 2);
                        }
                        catch (Exception ex) { Log.Warn(ex); }

                        string nvmeGuideTitle = Localizer.L(
                            "Comprendre le bogue de cache HMB sous Windows 11",
                            "Understanding the NVMe HMB Bug in Windows 11",
                            "Comprender el error HMB en Windows 11",
                            "Den NVMe-HMB-Fehler in Windows 11 verstehen",
                            "Capire il bug HMB su Windows 11",
                            "Entendendo o bug HMB no Windows 11");

                        string nvmeGuideSteps = Localizer.L(
                            "1. Les SSD DRAM-less (Kingston NV2/NV3, WD SN770/SN580, Crucial P3, Lexar NM790) empruntent la mémoire vive de votre PC via le protocole HMB (Host Memory Buffer).\r\n" +
                            "2. Sous Windows 11 24H2+, un dysfonctionnement de stornvme.sys alloue jusqu'à 200 Mo de HMB au lieu de 64 Mo, causant des micro-blocages ou des écrans bleus stornvme.\r\n" +
                            "3. Le bouton 'Stabiliser le cache HMB' applique la valeur officielle Microsoft (HmbAllocationPolicy = 2) plafonnant l'allocation à 64 Mo.\r\n" +
                            "4. Vérifiez également le firmware de votre SSD via le logiciel constructeur (WD Dashboard, Crucial Storage Executive, Kingston SSD Manager).",
                            "1. DRAM-less SSDs (Kingston NV2/NV3, WD SN770/SN580, Crucial P3, Lexar NM790) borrow host RAM via the Host Memory Buffer (HMB) protocol.\r\n" +
                            "2. On Windows 11 24H2+, a stornvme.sys bug allocates up to 200MB instead of 64MB, triggering stornvme freezes and BSODs.\r\n" +
                            "3. 'Stabilize NVMe HMB' sets Microsoft's recommended HmbAllocationPolicy = 2 to cap buffer size to 64MB.\r\n" +
                            "4. Check for NVMe firmware updates using the drive maker's official utility.",
                            "1. Los SSD sin DRAM usan la RAM del PC mediante HMB.\r\n2. En Windows 11 24H2+, un error asigna más de 64MB y provoca congelaciones.\r\n3. Esta corrección limita la memoria HMB a 64MB.",
                            "1. DRAM-lose SSDs nutzen den Arbeitsspeicher per HMB.\r\n2. Unter Windows 11 24H2+ führt ein Fehler zu Freezes.\r\n3. Diese Korrektur begrenzt HMB auf 64 MB.",
                            "1. Gli SSD senza DRAM usano la RAM tramite HMB.\r\n2. Su Windows 11 24H2+, un bug provoca blocchi improvvisi.\r\n3. Questa correzione limita l'HMB a 64 MB.",
                            "1. SSDs sem DRAM usam a RAM do PC via HMB.\r\n2. No Windows 11 24H2+, um bug causa travamentos.\r\n3. Esta correção limita o HMB a 64 MB.");

                        list.Add(new HardwareCheckItem
                        {
                            CategoryTag = isNvme ? "SSD NVMe" : "STOCKAGE",
                            Title = model + " (" + sizeGb + " GB)",
                            ValueSummary = hStatus + smartStr,
                            Detail = isDramLessHmb
                                ? Localizer.L(
                                    "Ce modèle NVMe utilise le Host Memory Buffer (HMB). Si vous rencontrez des freezes sous Windows 11 24H2+, activez le correctif « Stabiliser le cache HMB des SSD NVMe ».",
                                    "This NVMe model uses Host Memory Buffer (HMB). If you experience freezes on Windows 11 24H2+, enable 'Stabilize NVMe SSD Host Memory Buffer' in Fixes.",
                                    "Este modelo NVMe usa HMB. Si sufre bloqueos en Windows 11 24H2+, active la corrección NVMe HMB.",
                                    "Dieses NVMe-Modell nutzt HMB. Bei Freezes unter Windows 11 24H2+ aktivieren Sie die NVMe-HMB-Stabilisierung.",
                                    "Questo modello NVMe usa HMB. In caso di freeze su Windows 11 24H2+, attiva la stabilizzazione NVMe HMB.",
                                    "Este modelo NVMe usa HMB. Se tiver congelamentos no Windows 11 24H2+, ative a estabilização NVMe HMB.")
                                : Localizer.L("État matériel du disque vérifié.", "Drive hardware status verified.", "Estado del disco verificado.", "Laufwerksstatus geprüft.", "Stato disco verificato.", "Status do disco verificado."),
                            Level = (health == 0 && (smart == null || smart.UncorrectedErrors == 0)) ? HealthLevel.Good : HealthLevel.Warning,
                            InAppFixOp = (isDramLessHmb && !isHmbApplied) ? "nvme_hmb" : null,
                            InAppFixLabel = (isDramLessHmb && !isHmbApplied) ? Localizer.L("⚡ Stabiliser le cache HMB (64 Mo)", "⚡ Stabilize NVMe HMB (64MB)", "⚡ Estabilizar HMB (64MB)", "⚡ NVMe-HMB stabilisieren (64 MB)", "⚡ Stabilizza HMB (64MB)", "⚡ Estabilizar HMB (64MB)") : null,
                            InAppFixApplied = (isDramLessHmb && isHmbApplied),
                            GuideTitle = isDramLessHmb ? nvmeGuideTitle : null,
                            GuideSteps = isDramLessHmb ? nvmeGuideSteps : null,
                            QuickActionLabel = Localizer.L("🛠 Gestion des disques", "🛠 Disk Management", "🛠 Administración de discos", "🛠 Datenträgerverwaltung", "🛠 Gestione disco", "🛠 Gerenciamento de Disco"),
                            QuickActionCommand = "tool:diskmgmt"
                        });
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            return list;
        }

        public static List<DiskSmartInfo> ReadAllSmartCountersAdmin()
        {
            List<DiskSmartInfo> list = new List<DiskSmartInfo>();
            try
            {
                using (var s = new ManagementObjectSearcher(@"\\.\root\Microsoft\Windows\Storage",
                    "SELECT DeviceId, Temperature, Wear, ReadErrorsUncorrected, WriteErrorsUncorrected, PowerOnHours FROM MSFT_StorageReliabilityCounter"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        long rErr = Convert.ToInt64(mo["ReadErrorsUncorrected"] ?? 0);
                        long wErr = Convert.ToInt64(mo["WriteErrorsUncorrected"] ?? 0);
                        list.Add(new DiskSmartInfo
                        {
                            DeviceId = Convert.ToString(mo["DeviceId"]).Trim(),
                            TemperatureC = Convert.ToInt32(mo["Temperature"] ?? 0),
                            WearPct = Convert.ToInt32(mo["Wear"] ?? 0),
                            UncorrectedErrors = rErr + wErr,
                            PowerOnHours = Convert.ToInt64(mo["PowerOnHours"] ?? 0)
                        });
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        private static DiskSmartInfo TryReadSingleSmartCounter(string devId)
        {
            try
            {
                var all = ReadAllSmartCountersAdmin();
                return all.FirstOrDefault(x => x.DeviceId == devId);
            }
            catch (Exception ex) { Log.Warn(ex); return null; }
        }

        private static long DetectGpuBar1Mib()
        {
            // 1. Try nvidia-smi -q -d MEMORY
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "-q -d MEMORY",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(1500);
                    Match m = Regex.Match(output, @"BAR1 Memory Usage[\s\S]{1,120}?Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
                    if (m.Success)
                        return long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 2. Fallback: Win32_DeviceMemoryAddress range > 512 MB
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT StartingAddress, EndingAddress FROM Win32_DeviceMemoryAddress"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        ulong start = Convert.ToUInt64(mo["StartingAddress"] ?? 0);
                        ulong end = Convert.ToUInt64(mo["EndingAddress"] ?? 0);
                        if (end > start && (end - start) > 536870912UL)
                            return (long)((end - start + 1UL) / (1024UL * 1024UL));
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return 256;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out uint PropertyType, out uint PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        private static bool DetectGpuPcieLink(out int genCur, out int genMax, out int widthCur, out int widthMax)
        {
            genCur = genMax = widthCur = widthMax = 0;

            // 1. Universal Native Windows Kernel PCIe Link Query (works on AMD Radeon, Intel Arc & NVIDIA RTX without admin)
            try
            {
                Guid displayGuid = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
                IntPtr devSet = SetupDiGetClassDevsW(ref displayGuid, null, IntPtr.Zero, 0x02 /* DIGCF_PRESENT */);
                if (devSet != IntPtr.Zero && devSet != new IntPtr(-1))
                {
                    try
                    {
                        Guid pciFmt = new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
                        SP_DEVINFO_DATA dev = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };

                        uint bestMaxW = 0;
                        for (uint i = 0; SetupDiEnumDeviceInfo(devSet, i, ref dev); i++)
                        {
                            StringBuilder sbId = new StringBuilder(260);
                            uint reqId;
                            if (!SetupDiGetDeviceInstanceIdW(devSet, ref dev, sbId, 260, out reqId))
                                continue;
                            string instId = sbId.ToString();
                            if (!instId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                                continue;

                            uint t, reqSz, cSpd = 0, cW = 0, mSpd = 0, mW = 0;
                            DEVPROPKEY kCurSpd = new DEVPROPKEY { fmtid = pciFmt, pid = 9 };  // DEVPKEY_PciDevice_CurrentLinkSpeed
                            DEVPROPKEY kCurW   = new DEVPROPKEY { fmtid = pciFmt, pid = 10 }; // DEVPKEY_PciDevice_CurrentLinkWidth
                            DEVPROPKEY kMaxSpd = new DEVPROPKEY { fmtid = pciFmt, pid = 11 }; // DEVPKEY_PciDevice_MaxLinkSpeed
                            DEVPROPKEY kMaxW   = new DEVPROPKEY { fmtid = pciFmt, pid = 12 }; // DEVPKEY_PciDevice_MaxLinkWidth

                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kCurSpd, out t, out cSpd, 4, out reqSz, 0);
                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kCurW,   out t, out cW,   4, out reqSz, 0);
                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kMaxSpd, out t, out mSpd, 4, out reqSz, 0);
                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kMaxW,   out t, out mW,   4, out reqSz, 0);

                            // Prefer discrete PCIe GPUs (x8 / x16) over CPU-internal iGPUs
                            if (mW >= 4 && mW >= bestMaxW)
                            {
                                bestMaxW = mW;
                                genCur = (int)(cSpd > 0 ? cSpd : mSpd);
                                genMax = (int)(mSpd > 0 ? mSpd : cSpd);
                                widthCur = (int)cW;
                                widthMax = (int)mW;
                            }
                        }

                        if (widthMax > 0)
                            return true;
                    }
                    finally
                    {
                        SetupDiDestroyDeviceInfoList(devSet);
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 2. Fallback: nvidia-smi
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max --format=csv,noheader,nounits",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    string line = (p.StandardOutput.ReadLine() ?? "").Trim();
                    p.WaitForExit(1500);
                    string[] parts = line.Split(',');
                    if (parts.Length >= 4)
                    {
                        int.TryParse(parts[0].Trim(), out genCur);
                        int.TryParse(parts[1].Trim(), out genMax);
                        int.TryParse(parts[2].Trim(), out widthCur);
                        int.TryParse(parts[3].Trim(), out widthMax);
                        return widthMax > 0;
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return false;
        }

        // ==================== FEATURE 3: SOFTWARE CONFLICTS & DRIVER REMNANTS ====================
        public static List<ConflictCheckItem> ScanSoftwareConflicts(HardwareSummary hw)
        {
            List<ConflictCheckItem> list = new List<ConflictCheckItem>();

            // 1. Active Overlay Stack Check
            List<string> activeOverlays = new List<string>();
            if (IsProcActive("Discord")) activeOverlays.Add("Discord");
            if (IsProcActive("GameOverlayUI") || IsProcActive("steamwebhelper")) activeOverlays.Add("Steam Overlay");
            if (IsProcActive("NVIDIA Overlay") || IsProcActive("NVIDIA Share")) activeOverlays.Add("NVIDIA Overlay");
            if (IsProcActive("RadeonSoftware")) activeOverlays.Add("AMD Adrenalin Overlay");
            if (IsProcActive("RTSS")) activeOverlays.Add("RivaTuner (RTSS)");
            if (IsProcActive("Overwolf")) activeOverlays.Add("Overwolf");

            bool manyOverlays = activeOverlays.Count >= 3;
            bool isDiscordRunning = IsProcActive("Discord");

            string ovGuideTitle = Localizer.L(
                "Guide : Désactiver les overlays conflictuels",
                "Guide: Disable Conflicting Overlays",
                "Guía: Desactivar superposiciones",
                "Anleitung: Konfligierende Overlays deaktivieren",
                "Guida: Disattivare overlay in conflitto",
                "Guia: Desativar overlays conflitantes");

            string ovGuideSteps = Localizer.L(
                "1. Discord : Paramètres utilisateur ⚙ > Superposition de jeu > Désactivez 'Activer l'overlay en jeu'.\r\n" +
                "2. Steam : Paramètres Steam > En jeu > Décochez 'Activer l'overlay Steam en jeu'.\r\n" +
                "3. NVIDIA App / GeForce Experience : Paramètres ⚙ > Fonctionnalités > Désactivez la 'Superposition en jeu' (Alt+Z).\r\n" +
                "4. RivaTuner (RTSS) : Réglez le niveau de hook sur 'Low' pour éviter les collisions d'injection DirectX 12.\r\n" +
                "💡 L'empilement de crochets (hooks) DirectX simultanés fait crasher le moteur graphique lors du basculement Alt-Tab.",
                "1. Discord: User Settings ⚙ > Game Overlay > Turn off 'Enable in-game overlay'.\r\n" +
                "2. Steam: Steam Settings > In-Game > Uncheck 'Enable the Steam Overlay while in-game'.\r\n" +
                "3. NVIDIA App / GeForce Experience: Settings ⚙ > Features > Disable 'In-Game Overlay' (Alt+Z).\r\n" +
                "4. RivaTuner (RTSS): Set hook level to 'Low' to prevent DirectX 12 hook collisions.\r\n" +
                "💡 Stacking multiple graphics hooks causes D3D12/Vulkan driver crashes on Alt-Tab.",
                "1. Discord: Ajustes > Superposición > Desactivar.\r\n2. Steam: Ajustes > En la partida > Desactivar overlay.\r\n3. NVIDIA: Desactivar superposición en el juego (Alt+Z).",
                "1. Discord: Einstellungen > Game-Overlay > Deaktivieren.\r\n2. Steam: Einstellungen > Im Spiel > Overlay deaktivieren.\r\n3. NVIDIA: In-Game-Overlay deaktivieren.",
                "1. Discord: Impostazioni > Overlay di gioco > Disattiva.\r\n2. Steam: Impostazioni > In gioco > Disattiva overlay.\r\n3. NVIDIA: Disattiva overlay di gioco.",
                "1. Discord: Configurações > Sobreposição > Desativar.\r\n2. Steam: Configurações > No jogo > Desativar sobreposição.\r\n3. NVIDIA: Desativar sobreposição no jogo.");

            list.Add(new ConflictCheckItem
            {
                Tag = "OVERLAYS",
                Title = Localizer.L("Superpositions graphiques (Overlays) simultanées", "Simultaneous Graphics Overlays", "Superposiciones gráficas simultáneas", "Gleichzeitige Grafik-Overlays", "Overlay grafici simultanei", "Overlays gráficos simultâneos"),
                StatusText = activeOverlays.Count == 0
                    ? Localizer.L("Aucun overlay actif", "No active overlays", "Sin overlays activos", "Keine aktiven Overlays", "Nessun overlay attivo", "Nenhum overlay ativo")
                    : string.Join(", ", activeOverlays),
                Explanation = manyOverlays
                    ? Localizer.L("3 overlays ou plus tournent en même temps. L'empilement de hooks DirectX/Vulkan (Discord + Steam + pilote) est une cause fréquente de stutters et de timeouts TDR.", "3 or more overlays are running simultaneously. Stacking multiple DirectX/Vulkan hooks is a frequent cause of stutters and TDR crashes.", "3 o más overlays activos a la vez pueden causar tirones y fallos TDR.", "3 oder mehr gleichzeitige Overlays können Ruckler und TDR-Abstürze auslösen.", "3 o più overlay attivi contemporaneamente possono causare scatti e timeout TDR.", "3 ou mais overlays ativos ao mesmo tempo podem causar engasgos e falhas TDR.")
                    : Localizer.L("Nombre d'overlays raisonnable — aucun conflit d'injection DirectX détecté.", "Overlay count is clean — no DirectX hook contention detected.", "Número de overlays normal.", "Overlay-Anzahl unbedenklich.", "Numero di overlay normale.", "Número de overlays normal."),
                Level = manyOverlays ? HealthLevel.Warning : HealthLevel.Good,
                InAppFixOp = (manyOverlays && isDiscordRunning) ? "discord_hwaccel" : null,
                InAppFixLabel = (manyOverlays && isDiscordRunning) ? Localizer.L("⚡ Désactiver accélération Discord", "⚡ Disable Discord HwAccel", "⚡ Desactivar aceleración Discord", "⚡ Discord-HwBeschl. aus", "⚡ Disattiva acc. Discord", "⚡ Desativar acel. Discord") : null,
                GuideTitle = ovGuideTitle,
                GuideSteps = ovGuideSteps,
                QuickActionLabel = Localizer.L("⚙ Paramètres Applications", "⚙ App Settings", "⚙ Ajustes de aplicaciones", "⚙ App-Einstellungen", "⚙ Impostazioni app", "⚙ Configurações de Aplicativos"),
                QuickActionCommand = "tool:appsfeatures"
            });

            // 2. Cross-Vendor GPU Driver Service Remnants
            List<string> remnants = new List<string>();
            bool onlyRyzenIgpuHelper = false;
            try
            {
                bool isRyzenCpu = (hw.CpuName ?? "").IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0;
                ServiceController[] services = ServiceController.GetServices();
                foreach (var sc in services)
                {
                    if (sc.Status != ServiceControllerStatus.Running) continue;
                    string sn = sc.ServiceName ?? "";
                    if (!hw.HasAmdGpu && sn.Equals("AMDRSServ", StringComparison.OrdinalIgnoreCase))
                        remnants.Add("AMDRSServ (AMD Adrenalin)");
                    if (!hw.HasAmdGpu && sn.Equals("AMD External Events Utility", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isRyzenCpu) onlyRyzenIgpuHelper = true;
                        else remnants.Add("AMD External Events Utility");
                    }
                    if (!hw.HasNvidiaGpu && sn.IndexOf("NVDisplay.Container", StringComparison.OrdinalIgnoreCase) >= 0)
                        remnants.Add(sn + " (NVIDIA)");
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            string gpuGuideTitle = Localizer.L(
                "Guide : Nettoyage des pilotes & Désactivation de l'iGPU",
                "Guide: GPU Driver Cleanup & iGPU Management",
                "Guía: Limpieza de controladores GPU y gestión iGPU",
                "Leitfaden: GPU-Treiberbereinigung & iGPU-Verwaltung",
                "Guida: Pulizia driver GPU e gestione iGPU",
                "Guia: Limpeza de drivers GPU e gestão de iGPU");

            string gpuGuideSteps = onlyRyzenIgpuHelper
                ? Localizer.L(
                    "1. Le processeur AMD Ryzen intègre un circuit graphique (RDNA 2) qui tourne en parallèle de votre carte graphique dédiée.\r\n" +
                    "2. Si votre écran est branché sur votre carte dédiée (NVIDIA ou Radeon), ce service d'arrière-plan n'est pas indispensable.\r\n" +
                    "3. Cliquez sur « ⚡ Désactiver le service iGPU Ryzen » pour stopper le service Windows sans toucher au BIOS.\r\n" +
                    "4. Pour couper complètement le GPU intégré au niveau matériel : Redémarrez > Touche Suppr > BIOS UEFI > Advanced > NBIO Common Options / GFX Configuration > iGPU Configuration > Disabled.",
                    "1. Your AMD Ryzen CPU includes an integrated graphics block (RDNA 2) active alongside your discrete GPU.\r\n" +
                    "2. If your display is connected to your dedicated graphics card, this background helper service is not required.\r\n" +
                    "3. Click '⚡ Disable Ryzen iGPU Service' to stop and disable the Windows background service in 1 click.\r\n" +
                    "4. To disable the iGPU at the hardware level: Reboot > Press Del/F2 > UEFI BIOS > Advanced > NBIO Configuration > iGPU > Disabled.",
                    "1. El procesador Ryzen incluye gráficos integrados.\r\n2. Puede desactivar el servicio en 1 clic.\r\n3. En BIOS UEFI: Advanced > iGPU > Disabled.",
                    "1. Der Ryzen-Prozessor verfügt über eine integrierte GPU.\r\n2. Deaktivieren Sie den Dienst mit 1 Klick.\r\n3. Im UEFI-BIOS: Advanced > iGPU > Disabled.",
                    "1. Il processore Ryzen ha una GPU integrata.\r\n2. Disattiva il servizio con 1 clic.\r\n3. Nel BIOS UEFI: Advanced > iGPU > Disabled.",
                    "1. O processador Ryzen tem gráficos integrados.\r\n2. Desative o serviço em 1 clique.\r\n3. Na BIOS UEFI: Advanced > iGPU > Disabled.")
                : Localizer.L(
                    "1. Téléchargez la dernière version officielle de Display Driver Uninstaller (DDU) depuis Wagnardsoft.\r\n" +
                    "2. Redémarrez Windows en Mode Sans Échec (Maintenez Maj enfoncée tout en cliquant sur Redémarrer).\r\n" +
                    "3. Lancez DDU, sélectionnez 'GPU' puis la marque de l'ancien pilote à supprimer, et cliquez sur 'Nettoyer et redémarrer'.\r\n" +
                    "4. Au redémarrage normal, installez le dernier pilote propre de votre carte graphique actuelle.",
                    "1. Download the latest official Display Driver Uninstaller (DDU) from Wagnardsoft.\r\n" +
                    "2. Reboot Windows in Safe Mode (hold Shift while clicking Restart).\r\n" +
                    "3. Launch DDU, select 'GPU' and the previous vendor's driver, then click 'Clean and restart'.\r\n" +
                    "4. In normal boot, install the clean official driver for your active GPU.",
                    "1. Descargue DDU desde Wagnardsoft.\r\n2. Reinicie en Modo Seguro.\r\n3. Seleccione la GPU antigua y pulse 'Limpiar y reiniciar'.",
                    "1. DDU von Wagnardsoft herunterladen.\r\n2. Im abgesicherten Modus starten.\r\n3. Vorherigen Treiber bereinigen und neu starten.",
                    "1. Scarica DDU da Wagnardsoft.\r\n2. Riavvia in modalità provvisoria.\r\n3. Pulisci i driver precedenti e riavvia.",
                    "1. Baixe o DDU em Wagnardsoft.\r\n2. Reinicie em Modo de Segurança.\r\n3. Limpe os drivers antigos e reinicie.");

            list.Add(new ConflictCheckItem
            {
                Tag = "PILOTES GPU",
                Title = Localizer.L("Résidus de services pilotes graphiques concurrents", "Conflicting GPU Driver Service Remnants", "Restos de servicios de controladores GPU", "Konfligierende GPU-Treiberdienste", "Residui di servizi driver GPU in conflitto", "Resíduos de serviços de drivers GPU"),
                StatusText = remnants.Count > 0
                    ? string.Join(", ", remnants)
                    : (onlyRyzenIgpuHelper
                        ? Localizer.L("Service iGPU Ryzen actif (AMD External Events)", "Ryzen iGPU helper active (AMD External Events)", "Servicio iGPU Ryzen activo", "Ryzen-iGPU-Dienst aktiv", "Servizio iGPU Ryzen attivo", "Serviço iGPU Ryzen ativo")
                        : Localizer.L("Aucun service concurrent actif", "No conflicting GPU services running", "Sin servicios en conflicto", "Keine konfligierenden Dienste", "Nessun servizio in conflitto", "Nenhum serviço em conflitto")),
                Explanation = remnants.Count > 0
                    ? Localizer.L("Un service pilote d'une autre marque tourne en arrière-plan. Un nettoyage avec Display Driver Uninstaller (DDU) est recommandé.", "A background driver service from another GPU brand is running. Cleaning with DDU is recommended.", "Se detectó un servicio de otra marca de GPU. Se recomienda usar DDU.", "Ein Treiberdienst eines anderen GPU-Herstellers läuft im Hintergrund (DDU empfohlen).", "Rilevato servizio di un altro marchio GPU. Si consiglia DDU.", "Serviço de outra marca de GPU detectado. Recomenda-se usar o DDU.")
                    : (onlyRyzenIgpuHelper
                        ? Localizer.L("Lié au circuit graphique intégré du processeur AMD Ryzen (désactivable si vous utilisez uniquement votre carte dédiée).", "Installed for the AMD Ryzen CPU's integrated graphics block (harmless alongside a dedicated GPU, or removable if iGPU is off).", "Asociado a los gráficos integrados del procesador AMD Ryzen.", "Gehört zur integrierten Grafikeinheit des AMD-Ryzen-Prozessors.", "Associato alla grafica integrata del processore AMD Ryzen.", "Associado aos gráficos integrados do processador AMD Ryzen.")
                        : Localizer.L("Seul le service du pilote de votre carte graphique actuelle tourne en arrière-plan.", "Only your active GPU vendor's driver services are running.", "Solo se ejecutan los servicios de su GPU actual.", "Es laufen nur die Dienste Ihrer aktuellen Grafikkarte.", "Sono attivi solo i servizi della GPU attuale.", "Apenas os serviços da sua GPU atual estão ativos.")),
                Level = remnants.Count > 0 ? HealthLevel.Warning : (onlyRyzenIgpuHelper ? HealthLevel.Info : HealthLevel.Good),
                InAppFixOp = onlyRyzenIgpuHelper ? "disable_ryzen_igpu" : null,
                InAppFixLabel = onlyRyzenIgpuHelper ? Localizer.L("⚡ Désactiver service iGPU Ryzen", "⚡ Disable Ryzen iGPU Service", "⚡ Desactivar servicio iGPU Ryzen", "⚡ Ryzen-iGPU-Dienst aus", "⚡ Disattiva servizio iGPU Ryzen", "⚡ Desativar serviço iGPU Ryzen") : null,
                GuideTitle = gpuGuideTitle,
                GuideSteps = gpuGuideSteps,
                QuickActionLabel = onlyRyzenIgpuHelper
                    ? Localizer.L("🛠 Gestionnaire de périphériques", "🛠 Device Manager", "🛠 Administrador de dispositivos", "🛠 Geräte-Manager", "🛠 Gestione dispositivi", "🛠 Gerenciador de Dispositivos")
                    : (remnants.Count > 0 ? Localizer.L("🔍 Télécharger DDU", "🔍 Download DDU", "🔍 Descargar DDU", "🔍 DDU laden", "🔍 Scarica DDU", "🔍 Baixar DDU") : null),
                QuickActionCommand = onlyRyzenIgpuHelper ? "tool:devmgmt" : null,
                WebSearchQuery = remnants.Count > 0 ? "https://www.wagnardsoft.com/display-driver-uninstaller-ddu-" : null
            });

            // 3. Risky Kernel Drivers & Motherboard Bloatware (ene.sys, WinRing0, Gigabyte Update, cFosSpeed)
            List<string> riskyFound = new List<string>();
            string driversDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
            if (File.Exists(Path.Combine(driversDir, "cfosspeed.sys"))) riskyFound.Add("cfosspeed.sys (cFosSpeed Network Shaper)");
            if (IsProcActive("GbtCloudMatrix") || IsServiceRunning("GigabyteUpdateService")) riskyFound.Add("Gigabyte Update / CloudMatrix");
            if (IsProcActive("PatriotViperRGB") || IsProcActive("AAChal")) riskyFound.Add("Legacy RGB Service (ene.sys)");

            string riskyGuideTitle = Localizer.L(
                "Guide : Élimination des composants OEM et pilotes à risque",
                "Guide: Removing Risky OEM Components & Drivers",
                "Guía: Eliminación de componentes y servicios OEM a riesgo",
                "Leitfaden: Entfernen von riskanten OEM-Komponenten",
                "Guida: Rimozione di componenti OEM e driver a rischio",
                "Guia: Remoção de componentes e serviços OEM de risco");

            string riskyGuideSteps = Localizer.L(
                "1. Cliquez sur « ⚡ Neutraliser ces composants en 1 clic » pour stopper immédiatement les processus et désactiver leurs services Windows.\r\n" +
                "2. Ouvrez « Applications installées » pour désinstaller proprement les suites constructeur (Gigabyte Control Center, App Center, cFosSpeed).\r\n" +
                "3. Si vous avez une carte mère Gigabyte/ASUS : Redémarrez dans le BIOS UEFI et désactivez 'Gigabyte Download Assistant' ou 'Armoury Crate Auto Install' pour empêcher Windows de les réinstaller automatiquement.",
                "1. Click '⚡ Neutralize in 1 Click' to immediately terminate processes and disable their Windows services.\r\n" +
                "2. Open 'Installed Apps' to cleanly uninstall the manufacturer software suites (Gigabyte Control Center, App Center, cFosSpeed).\r\n" +
                "3. On Gigabyte/ASUS motherboards: Reboot to UEFI BIOS and disable 'Gigabyte Download Assistant' or 'Armoury Crate Auto Install' to prevent Windows from re-injecting them.",
                "1. Pulse '⚡ Neutralizar en 1 clic' para detener los procesos.\r\n2. Desinstale las suites del fabricante desde Aplicaciones instaladas.\r\n3. Desactive en la BIOS UEFI el asistente de descarga automática.",
                "1. Klicken Sie auf '⚡ Mit 1 Klick neutralisieren'.\r\n2. Deinstallieren Sie OEM-Tools über Installierte Apps.\r\n3. Deaktivieren Sie im UEFI-BIOS automatische Download-Assistenten.",
                "1. Clicca su '⚡ Neutralizza in 1 clic'.\r\n2. Disinstalla le suite OEM da App installate.\r\n3. Disattiva nel BIOS UEFI l'assistente di download automatico.",
                "1. Clique em '⚡ Neutralizar em 1 clique'.\r\n2. Desinstale os programas OEM em Aplicativos instalados.\r\n3. Desative na BIOS UEFI o assistente de download automático.");

            list.Add(new ConflictCheckItem
            {
                Tag = "NOYAU & SERVICES",
                Title = Localizer.L("Pilotes tiers & Services constructeurs à risque", "Known Unstable Drivers & OEM Bloat Services", "Controladores y servicios OEM conflictivos", "Bekannte instabile Treiber & OEM-Dienste", "Driver instabili noti e servizi OEM", "Drivers instáveis e serviços OEM"),
                StatusText = riskyFound.Count == 0
                    ? Localizer.L("Aucun service ou pilote instable actif", "No unstable OEM services active", "Ningún servicio inestable activo", "Keine instabilen Dienste aktiv", "Nessun servizio instabile attivo", "Nenhum serviço instável ativo")
                    : string.Join(", ", riskyFound),
                Explanation = riskyFound.Count == 0
                    ? Localizer.L("Les services connus pour causer des fuites de mémoire ou des BSOD (GigabyteUpdateService, cFosSpeed, anciens pilotes RGB) sont absents ou désactivés.", "Services notorious for memory leaks or BSODs (GigabyteUpdateService, cFosSpeed, legacy RGB rings) are absent or disabled.", "Los servicios conocidos por causar fugas de memoria o BSOD están desactivados.", "Bekannte Problem-Dienste (GigabyteUpdateService, cFosSpeed, alte RGB-Treiber) sind inaktiv.", "I servizi noti per causare memory leak o BSOD sono assenti o disattivati.", "Serviços conhecidos por causar vazamentos de memória ou BSOD estão desativados.")
                    : Localizer.L("Des composants tiers connus pour provoquer des instabilités ou des crashs en arrière-plan ont été détectés.", "Third-party components known to cause background crashes or DPC latency were detected.", "Se detectaron componentes conocidos por causar inestabilidad.", "Komponenten erkannt, die für Hintergrundabstürze bekannt sind.", "Rilevati componenti noti per causare instabilità.", "Componentes conhecidos por causar instabilidade foram detectados."),
                Level = riskyFound.Count == 0 ? HealthLevel.Good : HealthLevel.Warning,
                InAppFixOp = riskyFound.Count > 0 ? "disable_risky_oem" : null,
                InAppFixLabel = riskyFound.Count > 0 ? Localizer.L("⚡ Neutraliser composants en 1 clic", "⚡ Neutralize Bloat in 1 Click", "⚡ Neutralizar componentes en 1 clic", "⚡ Komponenten mit 1 Klick stoppen", "⚡ Neutralizza componenti in 1 clic", "⚡ Neutralizar componentes em 1 clique") : null,
                GuideTitle = riskyGuideTitle,
                GuideSteps = riskyGuideSteps,
                QuickActionLabel = Localizer.L("⚙ Applications installées", "⚙ Installed Apps", "⚙ Aplicaciones instaladas", "⚙ Installierte Apps", "⚙ App installate", "⚙ Aplicativos instalados"),
                QuickActionCommand = "tool:appsfeatures"
            });

            // 4. Anti-Cheat Ring-0 vs Kernel Shadow Stacks & Auto HDR (Win11 24H2+)
            List<string> antiCheats = new List<string>();
            if (File.Exists(Path.Combine(driversDir, "vgk.sys")) || IsServiceInstalled("vgk")) antiCheats.Add("Riot Vanguard (vgk.sys)");
            if (IsServiceInstalled("EasyAntiCheat") || IsServiceInstalled("EasyAntiCheat_EOS")) antiCheats.Add("Easy Anti-Cheat");
            if (IsServiceInstalled("BEService")) antiCheats.Add("BattlEye");

            bool shadowStackOn = ReadRegDwordSafe(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\KernelShadowStacks", "Enabled") == 1;
            bool autoHdrOn = CheckAutoHdrActive();
            bool acConflict = antiCheats.Count > 0 && (shadowStackOn || autoHdrOn);

            List<string> acCollisionNotes = new List<string>();
            if (shadowStackOn) acCollisionNotes.Add("Kernel Shadow Stacks ON");
            if (autoHdrOn) acCollisionNotes.Add("Auto HDR ON");

            string acGuideTitle = Localizer.L(
                "Guide : Conflits Anti-Cheat Noyau vs Sécurité Windows 11 24H2+",
                "Guide: Kernel Anti-Cheat vs Windows 11 24H2+ Security Conflicts",
                "Guía: Conflictos Anti-Cheat de núcleo y Seguridad de Windows 11",
                "Leitfaden: Kernel-Anti-Cheat vs. Windows 11 24H2+ Sicherheitskonflikte",
                "Guida: Conflitti Anti-Cheat Kernel e Sicurezza Windows 11",
                "Guia: Conflitos de Anti-Cheat de Kernel e Segurança do Windows 11");

            string acGuideSteps = Localizer.L(
                "1. Sous Windows 11 24H2+, les anti-cheats Ring-0 (Vanguard, Easy Anti-Cheat, BattlEye) peuvent entrer en collision avec la 'Protection de la pile en mode noyau' (Kernel Shadow Stacks), provoquant un BSOD 0x139 (KERNEL_SECURITY_CHECK_FAILURE).\r\n" +
                "2. Si vous subissez des BSODs immédiats au lancement d'un jeu protégé : Ouvrez 'Sécurité des appareils' > 'Isolation du noyau' > Désactivez temporairement 'Protection de la pile appliquée par le matériel en mode noyau', puis redémarrez.\r\n" +
                "3. Auto HDR peut également saturer le pipeline D3D11/D3D12 lors de l'injection d'anti-cheat : vous pouvez le désactiver en 1 clic ci-dessous.",
                "1. On Windows 11 24H2+, Ring-0 anti-cheats (Vanguard, EAC, BattlEye) can clash with Hardware-enforced Stack Protection, causing 0x139 BSODs.\r\n" +
                "2. If games crash instantly on boot: Open 'Device Security' > 'Core Isolation' > Temporarily turn off 'Kernel-mode Hardware-enforced Stack Protection', then reboot.\r\n" +
                "3. Windows Auto HDR can also destabilize game hooks: you can toggle it off in 1 click below.",
                "1. En Windows 11 24H2+, los anti-cheats Ring-0 pueden colisionar con la protección de pila del núcleo (BSOD 0x139).\r\n2. Abra 'Seguridad del dispositivo' > 'Aislamiento del núcleo' y desactive la protección de pila.\r\n3. Puede desactivar Auto HDR con 1 clic abajo.",
                "1. Unter Windows 11 24H2+ können Ring-0-Anti-Cheats mit dem Kernel-Stapelschutz kollidieren (BSOD 0x139).\r\n2. Öffnen Sie 'Gerätesicherheit' > 'Kernisolierung' und deaktivieren Sie den Hardware-Stapelschutz.\r\n3. Deaktivieren Sie Auto HDR mit 1 Klick unten.",
                "1. Su Windows 11 24H2+, gli anti-cheat Ring-0 possono entrare in conflitto con la protezione stack del kernel (BSOD 0x139).\r\n2. Apri 'Sicurezza dispositivi' > 'Isolamento core' e disattiva la protezione stack.\r\n3. Disattiva Auto HDR con 1 clic sotto.",
                "1. No Windows 11 24H2+, anti-cheats Ring-0 podem colidir com a proteção de pilha do kernel (BSOD 0x139).\r\n2. Abra 'Segurança do dispositivo' > 'Isolamento de núcleo' e desative a proteção de pilha.\r\n3. Desative o Auto HDR com 1 clique abaixo.");

            list.Add(new ConflictCheckItem
            {
                Tag = "ANTI-CHEAT • RING-0",
                Title = Localizer.L("Anti-Cheats Noyau vs Protection de pile (Shadow Stacks) & Auto HDR", "Kernel Anti-Cheats vs Hardware Stack Protection & Auto HDR", "Anti-Cheats de núcleo vs Protección de pila y Auto HDR", "Kernel-Anti-Cheats vs. Hardware-Stapelschutz & Auto HDR", "Anti-Cheat Kernel vs Protezione stack e Auto HDR", "Anti-Cheats de Kernel vs Proteção de pilha e Auto HDR"),
                StatusText = antiCheats.Count == 0
                    ? Localizer.L("Aucun anti-cheat Ring-0 permanent détecté", "No persistent Ring-0 anti-cheat detected", "Sin anti-cheat Ring-0 detectado", "Kein Ring-0-Anti-Cheat erkannt", "Nessun anti-cheat Ring-0 rilevato", "Nenhum anti-cheat Ring-0 detectado")
                    : (string.Join(", ", antiCheats) + (acCollisionNotes.Count > 0 ? ("  •  " + string.Join(" + ", acCollisionNotes)) : "")),
                Explanation = acConflict
                    ? Localizer.L("Sous Windows 11 24H2+, l'activation simultanée d'un anti-cheat Ring-0 avec la Protection de la pile en mode noyau ou Auto HDR peut provoquer un BSOD 0x139 au lancement des jeux.", "On Windows 11 24H2+, combining Ring-0 anti-cheats with Kernel-mode Hardware-enforced Stack Protection or Auto HDR can trigger 0x139 BSODs on game launch.", "En Windows 11 24H2+, combinar anti-cheats Ring-0 con Protección de pila del kernel o Auto HDR puede causar BSOD 0x139.", "Unter Windows 11 24H2+ kann die Kombination aus Ring-0-Anti-Cheat und Kernel-Stapelschutz oder Auto HDR BSOD 0x139 auslösen.", "Su Windows 11 24H2+, combinare anti-cheat Ring-0 con Protezione stack del kernel o Auto HDR può causare BSOD 0x139.", "No Windows 11 24H2+, combinar anti-cheats Ring-0 com Proteção de pilha do kernel ou Auto HDR pode causar BSOD 0x139.")
                    : Localizer.L("Aucune collision connue entre anti-cheats Ring-0 et l'isolation de pile du noyau Windows.", "No known collision detected between Ring-0 anti-cheats and Windows kernel stack protection.", "Sin colisiones detectadas entre anti-cheats y seguridad del núcleo.", "Kein Konflikt zwischen Anti-Cheat und Kernel-Sicherheit erkannt.", "Nessuna collisione rilevata tra anti-cheat e sicurezza del kernel.", "Nenhum conflito detectado entre anti-cheats e segurança do kernel."),
                Level = acConflict ? HealthLevel.Warning : HealthLevel.Good,
                InAppFixOp = autoHdrOn ? "disable_autohdr" : null,
                InAppFixLabel = autoHdrOn ? Localizer.L("⚡ Désactiver Auto HDR Windows", "⚡ Disable Windows Auto HDR", "⚡ Desactivar Auto HDR", "⚡ Auto HDR ausschalten", "⚡ Disattiva Auto HDR", "⚡ Desativar Auto HDR") : null,
                GuideTitle = acGuideTitle,
                GuideSteps = acGuideSteps,
                QuickActionLabel = Localizer.L("🛡️ Sécurité des appareils", "🛡️ Device Security", "🛡️ Seguridad del dispositivo", "🛡️ Gerätesicherheit", "🛡️ Sicurezza dispositivi", "🛡️ Segurança do dispositivo"),
                QuickActionCommand = "tool:device_security"
            });

            // 5. Realtek 2.5GbE EEE / Green Ethernet & MediaTek RZ616 Wi-Fi Power-Saving Check
            string nicDesc;
            bool nicHasPowerBug = InspectNicPowerSavingRisk(out nicDesc);
            if (!string.IsNullOrEmpty(nicDesc))
            {
                string nicGuideTitle = Localizer.L(
                    "Guide : Stabilisation des contrôleurs Realtek 2.5GbE & Wi-Fi",
                    "Guide: Realtek 2.5GbE & Wi-Fi Stability Guide",
                    "Guía: Estabilización de controladores Realtek 2.5GbE y Wi-Fi",
                    "Leitfaden: Realtek 2.5GbE- & WLAN-Stabilitätsanleitung",
                    "Guida: Stabilizzazione controller Realtek 2.5GbE e Wi-Fi",
                    "Guia: Estabilização de controladores Realtek 2.5GbE e Wi-Fi");

                string nicGuideSteps = Localizer.L(
                    "1. Les contrôleurs Realtek 2.5GbE (RTL8125/RTL8126) et MediaTek Wi-Fi (RZ616/MT7921) possèdent des fonctions d'économie d'énergie (EEE / Green Ethernet / extinction PCIe) qui causent des micro-coupures réseau et des gels système DPC sous Windows 11.\r\n" +
                    "2. Cliquez sur « ⚡ Désactiver l'économie réseau en 1 clic » pour appliquer instantanément les optimisations dans le Registre Windows.\r\n" +
                    "3. Si le problème persiste après redémarrage : Ouvrez 'Connexions réseau' (ncpa.cpl) > Clic droit sur votre carte > Propriétés > Configurer > Avancé > Désactivez 'Energy Efficient Ethernet' et 'Green Ethernet' manuellement.\r\n" +
                    "4. Dans l'onglet 'Gestion de l'alimentation', décochez 'Autoriser l'ordinateur à éteindre ce périphérique pour économiser l'énergie'.",
                    "1. Realtek 2.5GbE (RTL8125/8126) and MediaTek Wi-Fi adapters have aggressive power-saving (EEE / Green Ethernet / PCIe sleep) that causes network drops and DPC latency spikes.\r\n" +
                    "2. Click '⚡ Disable Network Power-Saving in 1 Click' to apply stable registry values immediately.\r\n" +
                    "3. If drops persist after reboot: Open 'Network Connections' (ncpa.cpl) > Right-click adapter > Properties > Configure > Advanced > Disable 'Energy Efficient Ethernet' and 'Green Ethernet'.\r\n" +
                    "4. Under 'Power Management' tab, uncheck 'Allow computer to turn off this device to save power'.",
                    "1. Los controladores Realtek 2.5GbE sufren micro-cortes por el ahorro de energía EEE.\r\n2. Pulse '⚡ Desactivar ahorro de energía en 1 clic'.\r\n3. En ncpa.cpl > Propiedades del adaptador > Desactive Energy Efficient Ethernet.",
                    "1. Realtek 2.5GbE-Controller leiden unter Verbindungsabbrüchen durch EEE-Energiesparmodi.\r\n2. Klicken Sie auf '⚡ Energiesparmodus mit 1 Klick deaktivieren'.\r\n3. In ncpa.cpl > Adaptereigenschaften > Energy Efficient Ethernet ausschalten.",
                    "1. I controller Realtek 2.5GbE soffrono di micro-disconnessioni per il risparmio energetico EEE.\r\n2. Clicca su '⚡ Disattiva risparmio energetico in 1 clic'.\r\n3. In ncpa.cpl > Proprietà scheda > Disattiva Energy Efficient Ethernet.",
                    "1. Controladores Realtek 2.5GbE sofrem micro-quedas devido à economia de energia EEE.\r\n2. Clique em '⚡ Desativar economia de energia em 1 clique'.\r\n3. Em ncpa.cpl > Propriedades do adaptador > Desative o Energy Efficient Ethernet.");

                list.Add(new ConflictCheckItem
                {
                    Tag = "RÉSEAU • 2.5GbE / WIFI",
                    Title = Localizer.L("Contrôleurs Réseau Realtek 2.5GbE / MediaTek & Veille EEE", "Realtek 2.5GbE / MediaTek Network Controllers & EEE Sleep", "Controladores Realtek 2.5GbE / MediaTek y ahorro EEE", "Realtek 2.5GbE / MediaTek-Netzwerk & EEE-Sparmodus", "Controller Realtek 2.5GbE / MediaTek e risparmio EEE", "Controladores Realtek 2.5GbE / MediaTek e economia EEE"),
                    StatusText = nicDesc,
                    Explanation = nicHasPowerBug
                        ? Localizer.L("L'économie d'énergie (Green Ethernet / EEE / mise en veille PCIe) est active sur cette puce réseau : cause fréquente de micro-freezes NDIS et de pertes Wi-Fi/Bluetooth. Désactivez-la en 1 clic dans « Correctifs ».", "Power-saving (Green Ethernet / EEE / PCIe sleep) is active on this controller — a frequent cause of NDIS micro-freezes and Wi-Fi/Bluetooth dropouts. Disable it in 1 click under 'Fixes'.", "El ahorro de energía (Green Ethernet / EEE) está activo en este controlador. Desactívelo en 'Correcciones'.", "Energiesparmodus (Green Ethernet / EEE) ist auf diesem Controller aktiv. Unter 'Korrekturen' mit 1 Klick deaktivierbar.", "Il risparmio energetico (Green Ethernet / EEE) è attivo su questo controller. Disattivalo in 'Correzioni'.", "A economia de energia (Green Ethernet / EEE) está ativa neste controlador. Desative em 'Correções'.")
                        : Localizer.L("Les modes d'économie d'énergie instables (EEE / Green Ethernet / coupure veille) sont désactivés sur vos contrôleurs réseau.", "Unstable power-saving states (EEE / Green Ethernet / device sleep) are cleanly disabled on your network controllers.", "Los modos de ahorro inestables están desactivados en su tarjeta de red.", "Instabile Energiesparmodi sind auf Ihren Netzwerkadaptern deaktiviert.", "I risparmi energetici instabili sono disattivati sulla scheda di rete.", "Os modos de economia instáveis estão desativados nos adaptadores de rede."),
                    Level = nicHasPowerBug ? HealthLevel.Warning : HealthLevel.Good,
                    InAppFixOp = nicHasPowerBug ? "nic_powersave_apply" : null,
                    InAppFixLabel = nicHasPowerBug ? Localizer.L("⚡ Désactiver l'économie réseau en 1 clic", "⚡ Disable Network Power-Saving in 1 Click", "⚡ Desactivar ahorro de energía en 1 clic", "⚡ Netzwerk-Energiesparen aus", "⚡ Disattiva risparmio rete in 1 clic", "⚡ Desativar economia de rede em 1 clique") : null,
                    InAppFixApplied = !nicHasPowerBug,
                    GuideTitle = nicGuideTitle,
                    GuideSteps = nicGuideSteps,
                    QuickActionLabel = Localizer.L("🌐 Connexions réseau (ncpa.cpl)", "🌐 Network Connections (ncpa.cpl)", "🌐 Conexiones de red", "🌐 Netzwerkverbindungen", "🌐 Connessioni di rete", "🌐 Conexões de rede"),
                    QuickActionCommand = "tool:ncpa"
                });
            }

            list.AddRange(Detection.DetectionChecks.All(hw));

            return list;
        }

        public static bool InspectNicPowerSavingRisk(out string summary)
        {
            summary = "";
            List<string> names = new List<string>();
            bool risky = false;
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey ck = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", false))
                {
                    if (ck == null) return false;
                    foreach (string sub in ck.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                        using (RegistryKey sk = ck.OpenSubKey(sub, false))
                        {
                            if (sk == null) continue;
                            string desc = Convert.ToString(sk.GetValue("DriverDesc") ?? "").Trim();
                            if (!Regex.IsMatch(desc, @"(Realtek.*2\.5GbE|Realtek.*Gaming|MediaTek.*Wi-Fi|RZ616|MT792|Realtek.*8852)", RegexOptions.IgnoreCase))
                                continue;

                            names.Add(desc);
                            string eee = Convert.ToString(sk.GetValue("*EEE") ?? "0");
                            string green = Convert.ToString(sk.GetValue("EnableGreenEthernet") ?? "0");
                            string ps = Convert.ToString(sk.GetValue("PowerSavingMode") ?? "0");
                            int pnp = Convert.ToInt32(sk.GetValue("PnPCapabilities") ?? 0);

                            if (eee != "0" || green != "0" || ps != "0" || (pnp & 0x18) != 0x18)
                                risky = true;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            if (names.Count > 0)
                summary = string.Join(" + ", names.Distinct());
            return risky;
        }

        private static bool IsServiceInstalled(string serviceName)
        {
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey sk = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    return sk != null;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static int ReadRegDwordSafe(RegistryHive hive, string subKey, string valName)
        {
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey sk = bk.OpenSubKey(subKey, false))
                {
                    if (sk == null) return 0;
                    object v = sk.GetValue(valName);
                    return v != null ? Convert.ToInt32(v) : 0;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return 0; }
        }

        private static bool CheckAutoHdrActive()
        {
            try
            {
                using (RegistryKey uk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", false))
                {
                    if (uk == null) return false;
                    string def = Convert.ToString(uk.GetValue("DirectXUserGlobalSettings") ?? "");
                    return def.IndexOf("AutoHDREnable=1", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static bool IsProcActive(string name)
        {
            try { return Process.GetProcessesByName(name).Length > 0; }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static bool IsServiceRunning(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }
    }
}
