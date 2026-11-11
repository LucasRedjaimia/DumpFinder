using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WinCrashDoctor.Detection
{
    /// <summary>Detection v2 checks shown under Hardware › Conflicts (Windows support, GPU driver version, dump capture, live dumps).</summary>
    public static class DetectionChecks
    {
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        public static IEnumerable<ConflictCheckItem> All(HardwareSummary hw)
        {
            var list = new List<ConflictCheckItem>();
            Try(list, () => WindowsSupport());
            Try(list, () => GpuDriverVersion(hw));
            Try(list, () => DumpCapture());
            Try(list, () => LiveKernelReports());
            return list;
        }

        private static void Try(List<ConflictCheckItem> list, Func<ConflictCheckItem> make)
        {
            try { var c = make(); if (c != null) list.Add(c); }
            catch (Exception ex) { Log.Warn(ex); }
        }

        // ==================== Windows release & support ====================
        public static ConflictCheckItem WindowsSupport()
        {
            WindowsRelease w = WindowsRelease.Current;
            DateTime? end = w.EndOfServicing;
            DateTime today = DateTime.Today;
            bool expired = end.HasValue && today > end.Value;
            bool soon = end.HasValue && !expired && (end.Value - today).TotalDays <= 90;

            string explanation;
            if (expired)
                explanation = string.Format(L(
                    "Cette version ne reçoit plus de mises à jour depuis le {0} : plus de correctifs de sécurité ni de stabilité, et les nouveaux pilotes ne sont plus testés dessus. Installe la dernière version de Windows via Windows Update.",
                    "This release has not received updates since {0}: no more security or stability fixes, and new drivers are no longer tested on it. Install the latest Windows release from Windows Update.",
                    "Esta versión no recibe actualizaciones desde el {0}: sin parches de seguridad ni de estabilidad, y los controladores nuevos ya no se prueban en ella. Instala la última versión desde Windows Update.",
                    "Diese Version erhält seit dem {0} keine Updates mehr: keine Sicherheits- oder Stabilitätskorrekturen, neue Treiber werden darauf nicht mehr getestet. Neueste Version über Windows Update installieren.",
                    "Questa versione non riceve aggiornamenti dal {0}: niente più correzioni di sicurezza o stabilità, e i nuovi driver non vengono più testati. Installa l'ultima versione da Windows Update.",
                    "Esta versão não recebe atualizações desde {0}: sem correções de segurança ou estabilidade, e novos drivers não são mais testados nela. Instale a versão mais recente pelo Windows Update."), WindowsRelease.Date(end.Value));
            else if (soon)
                explanation = string.Format(L("Cette version ne recevra plus de mises à jour après le {0}. Prévois la mise à niveau via Windows Update.",
                                              "This release stops getting updates after {0}. Plan the upgrade through Windows Update.",
                                              "Esta versión dejará de recibir actualizaciones después del {0}. Planifica la actualización.",
                                              "Diese Version erhält nach dem {0} keine Updates mehr. Upgrade über Windows Update einplanen.",
                                              "Questa versione non riceverà aggiornamenti dopo il {0}. Pianifica l'aggiornamento.",
                                              "Esta versão deixará de receber atualizações após {0}. Planeje a atualização."), WindowsRelease.Date(end.Value));
            else
                explanation = L("Version prise en charge : les correctifs de Microsoft (y compris ceux qui corrigent des plantages) continuent d'arriver.",
                                "Supported release: Microsoft fixes (including crash fixes) keep coming.",
                                "Versión compatible: siguen llegando las correcciones de Microsoft (incluidas las de bloqueos).",
                                "Unterstützte Version: Microsoft-Korrekturen (auch gegen Abstürze) kommen weiterhin.",
                                "Versione supportata: le correzioni di Microsoft (anche per i crash) continuano ad arrivare.",
                                "Versão suportada: as correções da Microsoft (inclusive de travamentos) continuam chegando.");

            return new ConflictCheckItem
            {
                Tag = "WINDOWS",
                Title = L("Version de Windows et support", "Windows version and support", "Versión de Windows y soporte", "Windows-Version und Support", "Versione di Windows e supporto", "Versão do Windows e suporte"),
                StatusText = w.Label + (expired ? L("  •  plus mise à jour", "  •  out of support", "  •  sin soporte", "  •  ohne Support", "  •  non supportata", "  •  sem suporte") : ""),
                Explanation = explanation,
                Level = expired ? HealthLevel.Warning : (soon ? HealthLevel.Info : HealthLevel.Good),
                QuickActionLabel = L("Windows Update", "Windows Update", "Windows Update", "Windows Update", "Windows Update", "Windows Update"),
                QuickActionCommand = "ms-settings:windowsupdate"
            };
        }

        // ==================== Graphics driver version ====================
        /// <summary>"32.0.15.9559" → "595.59" (the last five digits of the Windows version are NVIDIA's).</summary>
        public static string NvidiaVersion(string wmi)
        {
            if (string.IsNullOrEmpty(wmi)) return null;
            string[] p = wmi.Split('.');
            if (p.Length < 4) return null;
            string tail = p[2] + p[3].PadLeft(4, '0');
            if (tail.Length < 5) return null;
            tail = tail.Substring(tail.Length - 5);
            return tail.Substring(0, 3) + "." + tail.Substring(3);
        }

        private static double Ver(string v)
        {
            double d;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        /// <summary>NVIDIA releases with a documented problem, and what to do instead.</summary>
        private static string NvidiaProblem(string version, string gpuName)
        {
            switch (version)
            {
                case "595.59":
                    return L("Version retirée par NVIDIA en février 2026 (ventilateurs, écrans noirs, VIDEO_TDR_FAILURE). NVIDIA conseille de revenir à la 591.86 ou d'installer une version plus récente.",
                             "Release pulled by NVIDIA in February 2026 (fans, black screens, VIDEO_TDR_FAILURE). NVIDIA advises rolling back to 591.86 or installing a newer release.",
                             "Versión retirada por NVIDIA en febrero de 2026 (ventiladores, pantallas negras, VIDEO_TDR_FAILURE). NVIDIA aconseja volver a la 591.86 o instalar una más reciente.",
                             "Von NVIDIA im Februar 2026 zurückgezogene Version (Lüfter, schwarze Bildschirme, VIDEO_TDR_FAILURE). NVIDIA rät zu 591.86 oder einer neueren Version.",
                             "Versione ritirata da NVIDIA a febbraio 2026 (ventole, schermi neri, VIDEO_TDR_FAILURE). NVIDIA consiglia di tornare alla 591.86 o installarne una più recente.",
                             "Versão retirada pela NVIDIA em fevereiro de 2026 (ventoinhas, telas pretas, VIDEO_TDR_FAILURE). A NVIDIA recomenda voltar à 591.86 ou instalar uma mais recente.");
                case "621.01":
                    return L("Version qui provoque des gels vidéo et des écrans noirs sur les RTX 50, corrigés par le hotfix 621.12. Installe la 621.12 ou une version plus récente.",
                             "Release causing video freezes and black screens on RTX 50 cards, fixed by hotfix 621.12. Install 621.12 or newer.",
                             "Versión que causa congelaciones de vídeo y pantallas negras en RTX 50, corregidas por el hotfix 621.12. Instala la 621.12 o una más reciente.",
                             "Version mit Video-Hängern und schwarzen Bildschirmen auf RTX-50-Karten, behoben durch Hotfix 621.12. 621.12 oder neuer installieren.",
                             "Versione che causa blocchi video e schermi neri sulle RTX 50, corretti dall'hotfix 621.12. Installa la 621.12 o successiva.",
                             "Versão que causa travamentos de vídeo e telas pretas nas RTX 50, corrigidos pelo hotfix 621.12. Instale a 621.12 ou mais recente.");
            }
            bool rtx50 = gpuName != null && System.Text.RegularExpressions.Regex.IsMatch(gpuName, @"RTX\s*50\d0");
            double v = Ver(version);
            if (rtx50 && v > 0 && v < 572.83)
                return L("Pilote antérieur aux correctifs des écrans noirs des RTX 50 (versions 572.75 / 572.83 de mars 2025). Installe un pilote récent.",
                         "Driver older than the RTX 50 black-screen fixes (releases 572.75 / 572.83, March 2025). Install a recent driver.",
                         "Controlador anterior a las correcciones de pantallas negras de las RTX 50 (572.75 / 572.83, marzo de 2025). Instala uno reciente.",
                         "Treiber älter als die Korrekturen für schwarze Bildschirme der RTX 50 (572.75 / 572.83, März 2025). Aktuellen Treiber installieren.",
                         "Driver precedente alle correzioni degli schermi neri delle RTX 50 (572.75 / 572.83, marzo 2025). Installa un driver recente.",
                         "Driver anterior às correções de tela preta das RTX 50 (572.75 / 572.83, março de 2025). Instale um driver recente.");
            return null;
        }

        public static ConflictCheckItem GpuDriverVersion(HardwareSummary hw)
        {
            if (hw == null || string.IsNullOrEmpty(hw.GpuDriver)) return null;
            if (!hw.HasNvidiaGpu)
            {
                return new ConflictCheckItem
                {
                    Tag = L("PILOTE GRAPHIQUE", "GRAPHICS DRIVER", "CONTROLADOR GRÁFICO", "GRAFIKTREIBER", "DRIVER GRAFICO", "DRIVER GRÁFICO"),
                    Title = L("Version du pilote graphique", "Graphics driver version", "Versión del controlador gráfico", "Grafiktreiber-Version", "Versione del driver grafico", "Versão do driver gráfico"),
                    StatusText = hw.GpuName + " · " + hw.GpuDriver,
                    Explanation = L("Aucune liste de versions problématiques n'est suivie pour ce fabricant : garde simplement un pilote récent, installé depuis le site du fabricant.",
                                    "No list of problem releases is tracked for this vendor: just keep a recent driver from the vendor's site.",
                                    "No se sigue una lista de versiones problemáticas para este fabricante: mantén un controlador reciente de su web.",
                                    "Für diesen Hersteller wird keine Liste problematischer Versionen geführt: einfach einen aktuellen Treiber von der Herstellerseite nutzen.",
                                    "Per questo produttore non si segue un elenco di versioni problematiche: tieni un driver recente dal sito del produttore.",
                                    "Não há lista de versões problemáticas para este fabricante: mantenha um driver recente do site do fabricante."),
                    Level = HealthLevel.Info
                };
            }

            string nv = NvidiaVersion(hw.GpuDriver);
            string problem = nv != null ? NvidiaProblem(nv, hw.GpuName) : null;
            return new ConflictCheckItem
            {
                Tag = L("PILOTE GRAPHIQUE", "GRAPHICS DRIVER", "CONTROLADOR GRÁFICO", "GRAFIKTREIBER", "DRIVER GRAFICO", "DRIVER GRÁFICO"),
                Title = L("Version du pilote graphique", "Graphics driver version", "Versión del controlador gráfico", "Grafiktreiber-Version", "Versione del driver grafico", "Versão do driver gráfico"),
                StatusText = "NVIDIA " + (nv ?? "?") + "  (" + hw.GpuDriver + ")",
                Explanation = problem ?? L("Cette version ne fait pas partie des pilotes NVIDIA connus pour être problématiques.",
                                           "This release is not one of the NVIDIA drivers known to be problematic.",
                                           "Esta versión no figura entre los controladores NVIDIA problemáticos conocidos.",
                                           "Diese Version gehört nicht zu den bekannt problematischen NVIDIA-Treibern.",
                                           "Questa versione non è tra i driver NVIDIA noti come problematici.",
                                           "Esta versão não está entre os drivers NVIDIA conhecidos como problemáticos."),
                Level = problem != null ? HealthLevel.Warning : HealthLevel.Good,
                GuideTitle = problem != null ? L("Changer de pilote proprement", "Switching drivers cleanly", "Cambiar de controlador limpiamente", "Treiber sauber wechseln", "Cambiare driver in modo pulito", "Trocar de driver corretamente") : null,
                GuideSteps = problem != null ? L(
                    "1. Télécharge la version conseillée sur nvidia.com (pas via Windows Update).\r\n2. Facultatif mais conseillé : désinstalle l'ancien pilote avec DDU en mode sans échec.\r\n3. Installe le nouveau pilote puis redémarre.\r\n4. Dans Correctifs, verrouille les pilotes face à Windows Update pour qu'il ne soit pas remplacé.",
                    "1. Download the advised release from nvidia.com (not through Windows Update).\r\n2. Optional but advised: remove the old driver with DDU in safe mode.\r\n3. Install the new driver and reboot.\r\n4. In Fixes, lock drivers against Windows Update so it is not replaced.",
                    "1. Descarga la versión aconsejada en nvidia.com.\r\n2. Opcional: elimina el controlador anterior con DDU en modo seguro.\r\n3. Instala y reinicia.\r\n4. En Correcciones, bloquea los controladores frente a Windows Update.",
                    "1. Empfohlene Version von nvidia.com laden.\r\n2. Optional: alten Treiber mit DDU im abgesicherten Modus entfernen.\r\n3. Installieren und neu starten.\r\n4. Unter Korrekturen Treiber gegen Windows Update sperren.",
                    "1. Scarica la versione consigliata da nvidia.com.\r\n2. Facoltativo: rimuovi il vecchio driver con DDU in modalità provvisoria.\r\n3. Installa e riavvia.\r\n4. In Correzioni, blocca i driver da Windows Update.",
                    "1. Baixe a versão recomendada em nvidia.com.\r\n2. Opcional: remova o driver antigo com o DDU no modo de segurança.\r\n3. Instale e reinicie.\r\n4. Em Correções, bloqueie os drivers contra o Windows Update.") : null,
                QuickActionLabel = problem != null ? L("Pilotes NVIDIA", "NVIDIA drivers", "Controladores NVIDIA", "NVIDIA-Treiber", "Driver NVIDIA", "Drivers NVIDIA") : null,
                QuickActionCommand = problem != null ? "https://www.nvidia.com/drivers/" : null
            };
        }

        // ==================== Crash dump capture ====================
        private const string CrashControl = @"SYSTEM\CurrentControlSet\Control\CrashControl";

        private static int? ReadDword(string sub, string name)
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(sub))
                {
                    object v = k != null ? k.GetValue(name) : null;
                    return v is int ? (int?)(int)v : null;
                }
            }
            catch { return null; }
        }

        private static bool HasPagefile()
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
                {
                    var v = k != null ? k.GetValue("PagingFiles") as string[] : null;
                    return v != null && v.Any(s => !string.IsNullOrWhiteSpace(s));
                }
            }
            catch { return true; }
        }

        private static int CountEvents(string provider, int id, int days)
        {
            int n = 0;
            try
            {
                string utc = DateTime.Now.AddDays(-days).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);
                string xpath = "*[System[Provider[@Name='" + provider + "'] and (EventID=" + id + ") and TimeCreated[@SystemTime >= '" + utc + "']]]";
                using (var r = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath)))
                {
                    EventRecord rec;
                    while ((rec = r.ReadEvent()) != null) { rec.Dispose(); n++; }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return n;
        }

        public static ConflictCheckItem DumpCapture()
        {
            int? mode = ReadDword(CrashControl, "CrashDumpEnabled");
            bool off = mode.HasValue && mode.Value == 0;
            bool pagefile = HasPagefile();
            int failed = CountEvents("volmgr", 161, 60);
            int written = CountEvents("volmgr", 162, 60);
            bool bad = off || !pagefile || failed > 0;

            string modeName;
            switch (mode ?? 7)
            {
                case 0: modeName = L("désactivé", "disabled", "desactivado", "deaktiviert", "disattivato", "desativado"); break;
                case 1: modeName = L("complet", "complete", "completo", "vollständig", "completo", "completo"); break;
                case 2: modeName = L("noyau", "kernel", "núcleo", "Kernel", "kernel", "kernel"); break;
                case 3: modeName = L("minidump", "small (minidump)", "minivolcado", "Minidump", "minidump", "minidump"); break;
                default: modeName = L("automatique", "automatic", "automático", "automatisch", "automatico", "automático"); break;
            }

            var reasons = new List<string>();
            if (off) reasons.Add(L("l'écriture des dumps est désactivée", "dump writing is turned off", "la escritura de volcados está desactivada", "das Schreiben von Dumps ist aus", "la scrittura dei dump è disattivata", "a gravação de dumps está desativada"));
            if (!pagefile) reasons.Add(L("il n'y a pas de fichier d'échange (Windows en a besoin pour écrire le dump)", "there is no page file (Windows needs one to write the dump)", "no hay archivo de paginación (Windows lo necesita para el volcado)", "es gibt keine Auslagerungsdatei (Windows braucht sie für den Dump)", "non c'è un file di paging (serve a Windows per il dump)", "não há arquivo de paginação (o Windows precisa dele para o dump)"));
            if (failed > 0) reasons.Add(string.Format(L("{0} écriture(s) de dump ont échoué en 60 jours (volmgr 161)", "{0} dump write(s) failed in 60 days (volmgr 161)", "{0} escritura(s) de volcado fallaron en 60 días (volmgr 161)", "{0} Dump-Schreibvorgang/-vorgänge in 60 Tagen fehlgeschlagen (volmgr 161)", "{0} scrittura/e di dump fallita/e in 60 giorni (volmgr 161)", "{0} gravação(ões) de dump falharam em 60 dias (volmgr 161)"), failed));

            return new ConflictCheckItem
            {
                Tag = L("ÉCRANS BLEUS", "BLUE SCREENS", "PANTALLAS AZULES", "BLUESCREENS", "SCHERMATE BLU", "TELAS AZUIS"),
                Title = L("Enregistrement des écrans bleus (dumps)", "Blue screen recording (dumps)", "Registro de pantallas azules (volcados)", "Aufzeichnung von Bluescreens (Dumps)", "Registrazione delle schermate blu (dump)", "Registro das telas azuis (dumps)"),
                StatusText = string.Format(L("Mode : {0} · {1} dump(s) écrit(s), {2} échec(s) en 60 jours", "Mode: {0} · {1} dump(s) written, {2} failure(s) in 60 days", "Modo: {0} · {1} volcado(s) escrito(s), {2} fallo(s) en 60 días", "Modus: {0} · {1} Dump(s) geschrieben, {2} Fehler in 60 Tagen", "Modalità: {0} · {1} dump scritto/i, {2} errore/i in 60 giorni", "Modo: {0} · {1} dump(s) gravado(s), {2} falha(s) em 60 dias"), modeName, written, failed),
                Explanation = bad
                    ? L("Sans dump, un écran bleu ne laisse qu'un « Kernel-Power 41 » sans coupable identifiable. En cause : ", "Without a dump, a blue screen only leaves a 'Kernel-Power 41' with no identifiable culprit. Because: ", "Sin volcado, una pantalla azul solo deja un «Kernel-Power 41» sin culpable. Motivo: ", "Ohne Dump hinterlässt ein Bluescreen nur ein „Kernel-Power 41“ ohne erkennbaren Verursacher. Grund: ", "Senza dump, una schermata blu lascia solo un «Kernel-Power 41» senza colpevole. Motivo: ", "Sem dump, uma tela azul só deixa um «Kernel-Power 41» sem culpado. Motivo: ") + string.Join(" ; ", reasons) + "."
                    : L("Windows est bien configuré pour enregistrer un dump à chaque écran bleu : c'est ce qui permet à Dump Finder de nommer le pilote fautif.",
                        "Windows is set up to save a dump at every blue screen: that is what lets Dump Finder name the faulty driver.",
                        "Windows está configurado para guardar un volcado en cada pantalla azul: así Dump Finder puede nombrar el controlador culpable.",
                        "Windows speichert bei jedem Bluescreen einen Dump: So kann Dump Finder den fehlerhaften Treiber benennen.",
                        "Windows è configurato per salvare un dump a ogni schermata blu: così Dump Finder può indicare il driver colpevole.",
                        "O Windows está configurado para salvar um dump a cada tela azul: é assim que o Dump Finder identifica o driver culpado."),
                Level = bad ? HealthLevel.Warning : HealthLevel.Good,
                InAppFixOp = off ? "crashdump_apply" : (!pagefile || failed > 0 ? "pagefile_auto_apply" : null),
                InAppFixLabel = off
                    ? L("⚡ Réactiver les dumps", "⚡ Turn dumps back on", "⚡ Reactivar volcados", "⚡ Dumps wieder einschalten", "⚡ Riattiva i dump", "⚡ Reativar dumps")
                    : (!pagefile || failed > 0 ? L("⚡ Fichier d'échange automatique", "⚡ Automatic page file", "⚡ Archivo de paginación automático", "⚡ Automatische Auslagerungsdatei", "⚡ File di paging automatico", "⚡ Arquivo de paginação automático") : null)
            };
        }

        /// <summary>CrashDumpEnabled = 7 (automatic memory dump, the Windows default). Recorded for undo.</summary>
        public static bool EnableCrashDumps(out string messageFr, out string messageEn)
        {
            ChangeRecorder.Registry(RegistryHive.LocalMachine, CrashControl, "CrashDumpEnabled");
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.CreateSubKey(CrashControl))
                k.SetValue("CrashDumpEnabled", 7, RegistryValueKind.DWord);
            messageFr = "✓ Enregistrement des écrans bleus réactivé (vidage mémoire automatique).";
            messageEn = "✓ Blue screen recording turned back on (automatic memory dump).";
            return true;
        }

        // ==================== Live kernel reports ====================
        public static ConflictCheckItem LiveKernelReports()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiveKernelReports");
            if (!Directory.Exists(dir)) return null;

            var title = L("Blocages récupérés par Windows (LiveKernelReports)", "Hangs Windows recovered from (LiveKernelReports)", "Bloqueos recuperados por Windows (LiveKernelReports)", "Von Windows abgefangene Hänger (LiveKernelReports)", "Blocchi recuperati da Windows (LiveKernelReports)", "Travamentos recuperados pelo Windows (LiveKernelReports)");
            var tag = L("DUMPS « LIVE »", "LIVE DUMPS", "VOLCADOS «LIVE»", "LIVE-DUMPS", "DUMP «LIVE»", "DUMPS «LIVE»");
            List<FileInfo> files;
            try
            {
                files = new DirectoryInfo(dir).EnumerateFiles("*.dmp", SearchOption.AllDirectories)
                                             .Where(f => (DateTime.Now - f.LastWriteTime).TotalDays <= 30).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                return new ConflictCheckItem
                {
                    Tag = tag,
                    Title = title,
                    StatusText = L("Lecture réservée au mode administrateur", "Readable in administrator mode only", "Solo legible en modo administrador", "Nur im Administratormodus lesbar", "Leggibile solo in modalità amministratore", "Legível só no modo administrador"),
                    Explanation = L("Quand le pilote graphique (ou USB, réseau…) se bloque sans faire d'écran bleu, Windows le relance et garde un « live dump » dans C:\\Windows\\LiveKernelReports. Relance Dump Finder en mode administrateur pour les compter et les analyser.",
                                    "When the graphics (or USB, network…) driver hangs without a blue screen, Windows restarts it and keeps a 'live dump' in C:\\Windows\\LiveKernelReports. Restart Dump Finder as administrator to count and analyze them.",
                                    "Cuando el controlador gráfico (o USB, red…) se bloquea sin pantalla azul, Windows lo reinicia y guarda un «live dump» en C:\\Windows\\LiveKernelReports. Abre Dump Finder como administrador para analizarlos.",
                                    "Hängt der Grafik- (oder USB-, Netzwerk-)Treiber ohne Bluescreen, startet Windows ihn neu und legt einen „Live-Dump“ in C:\\Windows\\LiveKernelReports ab. Dump Finder als Administrator starten, um sie auszuwerten.",
                                    "Quando il driver grafico (o USB, rete…) si blocca senza schermata blu, Windows lo riavvia e salva un «live dump» in C:\\Windows\\LiveKernelReports. Avvia Dump Finder come amministratore per analizzarli.",
                                    "Quando o driver gráfico (ou USB, rede…) trava sem tela azul, o Windows o reinicia e guarda um «live dump» em C:\\Windows\\LiveKernelReports. Abra o Dump Finder como administrador para analisá-los."),
                    Level = HealthLevel.Info
                };
            }

            if (files.Count == 0)
                return new ConflictCheckItem
                {
                    Tag = tag,
                    Title = title,
                    StatusText = L("Aucun en 30 jours", "None in 30 days", "Ninguno en 30 días", "Keine in 30 Tagen", "Nessuno in 30 giorni", "Nenhum em 30 dias"),
                    Explanation = L("Windows n'a pas eu à relancer de pilote bloqué ces 30 derniers jours.", "Windows did not have to restart a hung driver in the last 30 days.", "Windows no tuvo que reiniciar ningún controlador bloqueado en 30 días.", "Windows musste in den letzten 30 Tagen keinen hängenden Treiber neu starten.", "Windows non ha dovuto riavviare driver bloccati negli ultimi 30 giorni.", "O Windows não precisou reiniciar nenhum driver travado nos últimos 30 dias."),
                    Level = HealthLevel.Good
                };

            // Header (and, for triage-style live dumps, the culprit driver) of each file.
            var codes = new Dictionary<string, int>();
            var culprits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool gpu = false;
            foreach (FileInfo f in files.OrderByDescending(x => x.LastWriteTime).Take(40))
            {
                KernelDumpInfo info = null;
                try
                {
                    int len = (int)Math.Min(f.Length, 16L << 20);
                    var buf = new byte[len];
                    using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        int read = 0;
                        while (read < len) { int n = fs.Read(buf, read, len - read); if (n <= 0) break; read += n; }
                    }
                    info = KernelDumpParser.Parse(buf);
                }
                catch (Exception ex) { Log.Warn(ex, f.FullName); }

                string key = f.Directory.Name + (info != null ? " 0x" + info.BugCheckCode.ToString("X") : "");
                codes[key] = codes.ContainsKey(key) ? codes[key] + 1 : 1;
                if (info != null && (info.BugCheckCode == 0x141 || info.BugCheckCode == 0x117 || info.BugCheckCode == 0x1A8 || info.BugCheckCode == 0x193)) gpu = true;
                if (f.Directory.Name.StartsWith("WATCHDOG", StringComparison.OrdinalIgnoreCase)) gpu = true;
                if (info != null && !string.IsNullOrEmpty(info.Culprit))
                    culprits[info.Culprit] = culprits.ContainsKey(info.Culprit) ? culprits[info.Culprit] + 1 : 1;
            }

            string summary = string.Join(", ", codes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " ×" + kv.Value));
            string culprit = culprits.Count > 0 ? culprits.OrderByDescending(kv => kv.Value).First().Key : null;
            return new ConflictCheckItem
            {
                Tag = tag,
                Title = title,
                StatusText = string.Format(L("{0} en 30 jours : {1}", "{0} in 30 days: {1}", "{0} en 30 días: {1}", "{0} in 30 Tagen: {1}", "{0} in 30 giorni: {1}", "{0} em 30 dias: {1}"), files.Count, summary),
                Explanation = (gpu
                    ? L("Le pilote graphique s'est bloqué et Windows l'a relancé sans écran bleu (écran noir de quelques secondes, jeu qui se ferme). Ce sont les mêmes causes qu'un 0x116, en moins grave : vois les correctifs TDR et MPO, l'undervolt GPU et le pilote.",
                        "The graphics driver hung and Windows restarted it without a blue screen (a few seconds of black screen, game closing). Same causes as a 0x116, less severe: see the TDR and MPO fixes, GPU undervolt and the driver.",
                        "El controlador gráfico se bloqueó y Windows lo reinició sin pantalla azul. Mismas causas que un 0x116, menos grave: revisa TDR, MPO, undervolt y controlador.",
                        "Der Grafiktreiber hing und Windows startete ihn ohne Bluescreen neu. Gleiche Ursachen wie 0x116, weniger schwer: TDR, MPO, Undervolt und Treiber prüfen.",
                        "Il driver grafico si è bloccato e Windows l'ha riavviato senza schermata blu. Stesse cause di uno 0x116, meno grave: vedi TDR, MPO, undervolt e driver.",
                        "O driver gráfico travou e o Windows o reiniciou sem tela azul. Mesmas causas de um 0x116, menos grave: veja TDR, MPO, undervolt e driver.")
                    : L("Un pilote s'est bloqué et Windows l'a relancé sans écran bleu ; le dossier indique le composant (USB, réseau, stockage…).",
                        "A driver hung and Windows restarted it without a blue screen; the folder names the component (USB, network, storage…).",
                        "Un controlador se bloqueó y Windows lo reinició sin pantalla azul; la carpeta indica el componente.",
                        "Ein Treiber hing und Windows startete ihn ohne Bluescreen neu; der Ordner nennt die Komponente.",
                        "Un driver si è bloccato e Windows l'ha riavviato senza schermata blu; la cartella indica il componente.",
                        "Um driver travou e o Windows o reiniciou sem tela azul; a pasta indica o componente."))
                    + (culprit != null ? " " + L("Pilote le plus souvent en cause : ", "Driver most often involved: ", "Controlador más implicado: ", "Am häufigsten beteiligter Treiber: ", "Driver più coinvolto: ", "Driver mais envolvido: ") + culprit + "." : ""),
                Level = HealthLevel.Warning
            };
        }
    }
}
