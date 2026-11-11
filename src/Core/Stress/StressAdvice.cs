using System;
using System.Collections.Generic;
using System.Linq;

namespace WinCrashDoctor.Stress
{
    /// <summary>Turns what a stage observed into a verdict: what failed, what it most likely means, what to do.</summary>
    internal static class StressAdvice
    {
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        private static StressStageResult R(StageContext ctx, StressOutcome o, string summary, string advice, params string[] fixes)
        {
            return R(ctx.Stage.Kind, o, summary, advice, fixes);
        }

        private static StressStageResult R(StressStageKind kind, StressOutcome o, string summary, string advice, params string[] fixes)
        {
            return new StressStageResult
            {
                Kind = kind,
                Name = StressText.StageName(kind),
                Outcome = o,
                Summary = summary,
                Advice = advice,
                FixIds = fixes != null ? fixes.ToList() : new List<string>()
            };
        }

        // ---------------- Advice blocks
        private static string CpuAdvice
        {
            get
            {
                return L("Causes habituelles : overclock ou undervolt trop agressif (PBO, Curve Optimizer, undervolt Intel), profil mémoire EXPO/XMP ou refroidissement insuffisant. Remets les réglages CPU du BIOS par défaut et refais le test ; s'il passe, réapplique tes réglages un par un.",
                         "Usual causes: overly aggressive overclock or undervolt (PBO, Curve Optimizer, Intel undervolt), EXPO/XMP memory profile or insufficient cooling. Reset CPU settings to BIOS defaults and test again; if it passes, re-apply your tweaks one at a time.",
                         "Causas habituales: overclock o undervolt demasiado agresivo (PBO, Curve Optimizer, undervolt Intel), perfil de memoria EXPO/XMP o refrigeración insuficiente. Restablece los ajustes de CPU de la BIOS y repite la prueba; si pasa, vuelve a aplicar tus ajustes uno a uno.",
                         "Übliche Ursachen: zu aggressives Übertakten oder Undervolting (PBO, Curve Optimizer, Intel-Undervolt), EXPO/XMP-Speicherprofil oder unzureichende Kühlung. CPU-Einstellungen im BIOS auf Standard setzen und erneut testen; wenn es besteht, Anpassungen einzeln wieder aktivieren.",
                         "Cause abituali: overclock o undervolt troppo aggressivo (PBO, Curve Optimizer, undervolt Intel), profilo di memoria EXPO/XMP o raffreddamento insufficiente. Ripristina le impostazioni CPU del BIOS e ripeti il test; se passa, riapplica le modifiche una alla volta.",
                         "Causas habituais: overclock ou undervolt agressivo demais (PBO, Curve Optimizer, undervolt Intel), perfil de memória EXPO/XMP ou refrigeração insuficiente. Restaure as configurações de CPU da BIOS e repita o teste; se passar, reaplique os ajustes um a um.");
            }
        }

        private static string CoreAdvice
        {
            get
            {
                return L("Un cœur qui calcule faux sous charge légère est le symptôme typique d'un Curve Optimizer (AMD) ou d'un undervolt par cœur trop agressif : ce cœur monte plus haut qu'il ne le supporte. Dans le BIOS, rends sa valeur moins négative (par exemple de 3 à 5 crans) puis relance le test cœur par cœur. Sans aucun réglage manuel, mets à jour le BIOS ; si l'erreur persiste, le processeur peut être défectueux (garantie).",
                         "A core that miscalculates under light load is the classic sign of an overly aggressive Curve Optimizer (AMD) or per-core undervolt: that core boosts higher than it can sustain. In the BIOS, make its value less negative (e.g. by 3 to 5 steps) and run the core-by-core test again. With no manual tuning at all, update the BIOS; if the error persists, the CPU may be faulty (warranty).",
                         "Un núcleo que calcula mal con carga ligera es el síntoma típico de un Curve Optimizer (AMD) o undervolt por núcleo demasiado agresivo. En la BIOS, haz su valor menos negativo (de 3 a 5 pasos) y repite la prueba núcleo a núcleo. Sin ajustes manuales, actualiza la BIOS; si persiste, la CPU puede estar defectuosa (garantía).",
                         "Ein Kern, der unter leichter Last falsch rechnet, ist das typische Zeichen eines zu aggressiven Curve Optimizers (AMD) oder Undervolts pro Kern. Im BIOS seinen Wert weniger negativ einstellen (z. B. 3 bis 5 Stufen) und den Kern-für-Kern-Test wiederholen. Ohne manuelle Einstellungen das BIOS aktualisieren; bleibt der Fehler, kann die CPU defekt sein (Garantie).",
                         "Un core che sbaglia i calcoli con carico leggero è il sintomo tipico di un Curve Optimizer (AMD) o undervolt per core troppo aggressivo. Nel BIOS rendi il suo valore meno negativo (da 3 a 5 passi) e ripeti il test core per core. Senza regolazioni manuali, aggiorna il BIOS; se l'errore persiste, la CPU può essere difettosa (garanzia).",
                         "Um núcleo que calcula errado com carga leve é o sintoma típico de um Curve Optimizer (AMD) ou undervolt por núcleo agressivo demais. Na BIOS, torne seu valor menos negativo (3 a 5 passos) e repita o teste núcleo a núcleo. Sem ajustes manuais, atualize a BIOS; se persistir, a CPU pode estar com defeito (garantia).");
            }
        }

        private static string GpuAdvice
        {
            get
            {
                return L("Signe d'instabilité de la carte graphique : overclock ou undervolt (courbe MSI Afterburner, réglages Adrenalin) trop agressif, ou surchauffe. Remets fréquences et tensions d'usine et refais le test. Si l'erreur persiste à l'état d'usine, réinstalle proprement le pilote (DDU) ; ensuite, pense à la garantie.",
                         "A sign of GPU instability: overly aggressive overclock or undervolt (MSI Afterburner curve, Adrenalin tuning) or overheating. Restore factory clocks and voltages and test again. If it still fails at stock, clean-reinstall the driver (DDU); after that, consider the warranty.",
                         "Señal de inestabilidad de la GPU: overclock o undervolt demasiado agresivo (curva de MSI Afterburner, ajustes de Adrenalin) o sobrecalentamiento. Restaura frecuencias y voltajes de fábrica y repite la prueba. Si falla de fábrica, reinstala el controlador con DDU; después, considera la garantía.",
                         "Zeichen für GPU-Instabilität: zu aggressives Übertakten oder Undervolting (MSI-Afterburner-Kurve, Adrenalin) oder Überhitzung. Werkstakte und -spannungen wiederherstellen und erneut testen. Bei Fehlern im Werkszustand den Treiber sauber neu installieren (DDU); danach an die Garantie denken.",
                         "Segno di instabilità della GPU: overclock o undervolt troppo aggressivo (curva MSI Afterburner, impostazioni Adrenalin) o surriscaldamento. Ripristina frequenze e tensioni di fabbrica e ripeti il test. Se fallisce di fabbrica, reinstalla il driver con DDU; poi valuta la garanzia.",
                         "Sinal de instabilidade da GPU: overclock ou undervolt agressivo demais (curva do MSI Afterburner, ajustes do Adrenalin) ou superaquecimento. Restaure frequências e tensões de fábrica e repita o teste. Se falhar de fábrica, reinstale o driver com DDU; depois, considere a garantia.");
            }
        }

        private static string PsuAdvice
        {
            get
            {
                return L("Les pics de charge brusques révèlent une alimentation trop juste ou un câble PCIe / 12V-2x6 mal enfiché : vérifie qu'il est enfoncé à fond, sans adaptateur ni coude serré près du connecteur.",
                         "Sudden load spikes expose a marginal power supply or a badly seated PCIe / 12V-2x6 cable: make sure it is fully inserted, with no adapter and no tight bend near the connector.",
                         "Los picos bruscos de carga revelan una fuente justa o un cable PCIe / 12V-2x6 mal conectado: comprueba que está bien insertado, sin adaptador ni curva cerrada cerca del conector.",
                         "Plötzliche Lastspitzen decken ein knappes Netzteil oder ein schlecht sitzendes PCIe-/12V-2x6-Kabel auf: Kabel ganz einstecken, ohne Adapter und ohne enge Biegung am Stecker.",
                         "I picchi improvvisi rivelano un alimentatore al limite o un cavo PCIe / 12V-2x6 inserito male: verifica che sia inserito a fondo, senza adattatori né curve strette vicino al connettore.",
                         "Picos bruscos de carga revelam uma fonte no limite ou um cabo PCIe / 12V-2x6 mal encaixado: confira se está totalmente inserido, sem adaptador nem curva apertada perto do conector.");
            }
        }

        private static string RamAdvice
        {
            get
            {
                return L("Désactive le profil EXPO/XMP (ou baisse sa fréquence d'un cran / relâche les timings) et refais le test. Si l'erreur persiste aux réglages d'usine, mets à jour le BIOS puis teste les barrettes une par une pour trouver la mauvaise.",
                         "Disable the EXPO/XMP profile (or lower its frequency one step / loosen timings) and test again. If the error persists at stock settings, update the BIOS, then test the sticks one at a time to find the bad one.",
                         "Desactiva el perfil EXPO/XMP (o baja un paso la frecuencia / relaja los tiempos) y repite la prueba. Si persiste de fábrica, actualiza la BIOS y prueba los módulos uno a uno.",
                         "EXPO/XMP-Profil deaktivieren (oder Takt eine Stufe senken / Timings lockern) und erneut testen. Bleibt der Fehler im Werkszustand, BIOS aktualisieren und die Module einzeln testen.",
                         "Disattiva il profilo EXPO/XMP (o abbassa la frequenza di un passo / allenta i timing) e ripeti il test. Se l'errore persiste di fabbrica, aggiorna il BIOS e prova i moduli uno alla volta.",
                         "Desative o perfil EXPO/XMP (ou reduza um passo a frequência / afrouxe as latências) e repita o teste. Se persistir de fábrica, atualize a BIOS e teste os pentes um a um.");
            }
        }

        private static string DiskAdvice
        {
            get
            {
                return L("C'est sérieux : le SSD, son contrôleur ou sa connexion a corrompu des données. Sauvegarde tes fichiers importants maintenant, regarde l'état SMART dans Matériel et mets à jour le firmware du SSD.",
                         "This is serious: the SSD, its controller or its connection corrupted data. Back up your important files now, check SMART health under Hardware and update the SSD firmware.",
                         "Es grave: el SSD, su controlador o su conexión corrompió datos. Haz copia de seguridad ahora, revisa el estado SMART en Hardware y actualiza el firmware del SSD.",
                         "Das ist ernst: SSD, Controller oder Verbindung haben Daten beschädigt. Jetzt wichtige Dateien sichern, SMART-Zustand unter Hardware prüfen und SSD-Firmware aktualisieren.",
                         "È grave: l'SSD, il suo controller o il collegamento ha corrotto dei dati. Fai subito un backup, controlla lo stato SMART in Hardware e aggiorna il firmware dell'SSD.",
                         "É grave: o SSD, seu controlador ou a conexão corrompeu dados. Faça backup agora, confira o SMART em Hardware e atualize o firmware do SSD.");
            }
        }

        private static string PcieAdvice
        {
            get
            {
                return L("Erreur sur un lien PCIe (souvent celui de la carte graphique) : réinsère la carte, évite les rallonges (riser) et, si besoin, force le slot en PCIe Gen4 dans le BIOS.",
                         "Error on a PCIe link (often the graphics card's): reseat the card, avoid riser cables and, if needed, force the slot to PCIe Gen4 in the BIOS.",
                         "Error en un enlace PCIe (a menudo el de la gráfica): vuelve a insertar la tarjeta, evita cables riser y, si hace falta, fuerza PCIe Gen4 en la BIOS.",
                         "Fehler auf einer PCIe-Verbindung (oft der Grafikkarte): Karte neu einsetzen, Riser-Kabel vermeiden und bei Bedarf PCIe Gen4 im BIOS erzwingen.",
                         "Errore su un collegamento PCIe (spesso quello della scheda video): reinserisci la scheda, evita i cavi riser e, se serve, forza PCIe Gen4 nel BIOS.",
                         "Erro em um link PCIe (geralmente o da placa de vídeo): reencaixe a placa, evite cabos riser e, se preciso, force PCIe Gen4 na BIOS.");
            }
        }

        private static string DriverResetAdvice
        {
            get
            {
                return L("Sous charge, le pilote graphique a cessé de répondre et Windows l'a relancé (TDR). Pistes : undervolt ou overclock GPU, alimentation ou câble PCIe / 12V-2x6, pilote à réinstaller proprement (DDU) ou version de pilote connue pour être instable.",
                         "Under load the graphics driver stopped responding and Windows restarted it (TDR). Leads: GPU undervolt or overclock, power supply or PCIe / 12V-2x6 cable, a driver to clean-reinstall (DDU) or a driver version known to be unstable.",
                         "Bajo carga, el controlador gráfico dejó de responder y Windows lo reinició (TDR). Pistas: undervolt u overclock de GPU, fuente o cable PCIe / 12V-2x6, controlador a reinstalar con DDU o versión inestable conocida.",
                         "Unter Last reagierte der Grafiktreiber nicht mehr und Windows startete ihn neu (TDR). Hinweise: GPU-Undervolt/Übertaktung, Netzteil oder PCIe-/12V-2x6-Kabel, Treiber sauber neu installieren (DDU) oder bekannt instabile Treiberversion.",
                         "Sotto carico il driver grafico ha smesso di rispondere e Windows lo ha riavviato (TDR). Piste: undervolt o overclock GPU, alimentatore o cavo PCIe / 12V-2x6, driver da reinstallare con DDU o versione nota come instabile.",
                         "Sob carga, o driver gráfico parou de responder e o Windows o reiniciou (TDR). Pistas: undervolt ou overclock da GPU, fonte ou cabo PCIe / 12V-2x6, driver a reinstalar com DDU ou versão sabidamente instável.");
            }
        }

        private static string AdviceFor(StressStageKind k)
        {
            switch (k)
            {
                case StressStageKind.CpuAllCore: return CpuAdvice;
                case StressStageKind.CpuPerCore: return CoreAdvice;
                case StressStageKind.GpuCompute:
                case StressStageKind.Vram: return GpuAdvice;
                case StressStageKind.Transient: return PsuAdvice;
                case StressStageKind.Ram: return RamAdvice;
                default: return DiskAdvice;
            }
        }

        // ---------------- Verdicts
        public static StressStageResult Passed(StageContext ctx, string summary)
        {
            return R(ctx, ctx.UserStopped ? StressOutcome.Stopped : StressOutcome.Passed, summary, null);
        }

        public static StressStageResult Skipped(StageContext ctx, string reason)
        {
            return R(ctx, StressOutcome.Skipped, reason, null);
        }

        public static StressStageResult CpuFailed(StageContext ctx, int cpu, string work)
        {
            string where = cpu >= 0
                ? string.Format(L(" (processeur logique {0}, charge {1})", " (logical processor {0}, {1} load)", " (procesador lógico {0}, carga {1})", " (logischer Prozessor {0}, Last {1})", " (processore logico {0}, carico {1})", " (processador lógico {0}, carga {1})"), cpu, work)
                : (string.IsNullOrEmpty(work) ? "" : " (" + work + ")");
            return R(ctx, StressOutcome.Failed,
                L("Le processeur a rendu un résultat faux sous charge", "The processor returned a wrong result under load", "La CPU devolvió un resultado erróneo bajo carga", "Die CPU lieferte unter Last ein falsches Ergebnis", "Il processore ha restituito un risultato errato sotto carico", "O processador retornou um resultado errado sob carga") + where + ".",
                CpuAdvice);
        }

        public static StressStageResult CoresFailed(StageContext ctx, List<int> bad, int total)
        {
            return R(ctx, StressOutcome.Failed,
                string.Format(L("Résultat faux sur {0} cœur(s) sur {1} : {2} (numérotés à partir de 0, comme dans Ryzen Master).",
                                "Wrong result on {0} of {1} core(s): {2} (numbered from 0, as in Ryzen Master).",
                                "Resultado erróneo en {0} de {1} núcleo(s): {2} (numerados desde 0, como en Ryzen Master).",
                                "Falsches Ergebnis auf {0} von {1} Kern(en): {2} (ab 0 nummeriert, wie in Ryzen Master).",
                                "Risultato errato su {0} core su {1}: {2} (numerati da 0, come in Ryzen Master).",
                                "Resultado errado em {0} de {1} núcleo(s): {2} (numerados a partir de 0, como no Ryzen Master)."),
                              bad.Count, total, string.Join(", ", bad)),
                CoreAdvice);
        }

        public static StressStageResult GpuUnavailable(StageContext ctx, string reason)
        {
            return R(ctx, StressOutcome.Skipped,
                L("Carte graphique non testée : ", "GPU not tested: ", "GPU no probada: ", "GPU nicht getestet: ", "GPU non testata: ", "GPU não testada: ") + reason, null);
        }

        public static StressStageResult GpuComputeFailed(StageContext ctx, string detail)
        {
            return R(ctx, StressOutcome.Failed,
                L("La carte graphique a rendu un résultat différent pour le même calcul", "The graphics card returned a different result for the same computation", "La gráfica devolvió un resultado distinto para el mismo cálculo", "Die Grafikkarte lieferte für dieselbe Berechnung ein anderes Ergebnis", "La scheda video ha restituito un risultato diverso per lo stesso calcolo", "A placa de vídeo retornou um resultado diferente para o mesmo cálculo") + " (" + detail + ").",
                ctx.Stage.Kind == StressStageKind.Transient ? GpuAdvice + " " + PsuAdvice : GpuAdvice);
        }

        public static StressStageResult GpuDriverReset(StageContext ctx, string detail)
        {
            return R(ctx, StressOutcome.Failed,
                L("Le pilote graphique a décroché pendant le test", "The graphics driver dropped out during the test", "El controlador gráfico falló durante la prueba", "Der Grafiktreiber ist während des Tests ausgefallen", "Il driver grafico è caduto durante il test", "O driver gráfico caiu durante o teste") + " (" + detail + ").",
                ctx.Stage.Kind == StressStageKind.Transient ? DriverResetAdvice + " " + PsuAdvice : DriverResetAdvice, "tdr");
        }

        public static StressStageResult VramFailed(StageContext ctx, double offsetBytes, int bits, long errors)
        {
            return R(ctx, StressOutcome.Failed,
                string.Format(L("La mémoire vidéo a renvoyé des données corrompues : {0} erreur(s), la première à {1} ({2} bit(s) faux).",
                                "Video memory returned corrupted data: {0} error(s), the first at {1} ({2} wrong bit(s)).",
                                "La memoria de vídeo devolvió datos corruptos: {0} error(es), el primero en {1} ({2} bit(s) erróneo(s)).",
                                "Der Grafikspeicher lieferte beschädigte Daten: {0} Fehler, der erste bei {1} ({2} falsche(s) Bit(s)).",
                                "La memoria video ha restituito dati corrotti: {0} errore/i, il primo a {1} ({2} bit errato/i).",
                                "A memória de vídeo retornou dados corrompidos: {0} erro(s), o primeiro em {1} ({2} bit(s) errado(s))."),
                              errors, StressText.Size(offsetBytes), bits),
                L("Retire tout overclock mémoire GPU (Afterburner, Adrenalin) et refais le test. Si l'erreur persiste aux réglages d'usine, la mémoire de la carte est défectueuse : fais jouer la garantie.",
                  "Remove any GPU memory overclock (Afterburner, Adrenalin) and test again. If the error persists at stock settings, the card's memory is faulty: use the warranty.",
                  "Quita cualquier overclock de memoria de la GPU (Afterburner, Adrenalin) y repite la prueba. Si persiste de fábrica, la memoria de la tarjeta está defectuosa: usa la garantía.",
                  "Jede GPU-Speicherübertaktung entfernen (Afterburner, Adrenalin) und erneut testen. Bleibt der Fehler im Werkszustand, ist der Kartenspeicher defekt: Garantie nutzen.",
                  "Rimuovi ogni overclock della memoria GPU (Afterburner, Adrenalin) e ripeti il test. Se l'errore persiste di fabbrica, la memoria della scheda è difettosa: usa la garanzia.",
                  "Remova qualquer overclock de memória da GPU (Afterburner, Adrenalin) e repita o teste. Se persistir de fábrica, a memória da placa está com defeito: acione a garantia."));
        }

        public static StressStageResult RamFailed(StageContext ctx, double offsetBytes, int bits, string pattern)
        {
            return R(ctx, StressOutcome.Failed,
                string.Format(L("Erreur mémoire à {0} : {1} bit(s) différent(s) de la valeur écrite (motif : {2}).",
                                "Memory error at {0}: {1} bit(s) differ from the written value (pattern: {2}).",
                                "Error de memoria en {0}: {1} bit(s) distinto(s) del valor escrito (patrón: {2}).",
                                "Speicherfehler bei {0}: {1} Bit(s) weichen vom geschriebenen Wert ab (Muster: {2}).",
                                "Errore di memoria a {0}: {1} bit diverso/i dal valore scritto (schema: {2}).",
                                "Erro de memória em {0}: {1} bit(s) diferente(s) do valor gravado (padrão: {2})."),
                              StressText.Size(offsetBytes), bits, pattern),
                RamAdvice);
        }

        public static StressStageResult DiskDataFailed(StageContext ctx, double offsetBytes)
        {
            return R(ctx, StressOutcome.Failed,
                string.Format(L("Des données relues ne correspondent pas à celles écrites (à {0} du fichier de test).",
                                "Data read back does not match what was written (at {0} into the test file).",
                                "Los datos releídos no coinciden con los escritos (a {0} del archivo de prueba).",
                                "Rückgelesene Daten stimmen nicht mit den geschriebenen überein (bei {0} der Testdatei).",
                                "I dati riletti non corrispondono a quelli scritti (a {0} del file di test).",
                                "Os dados relidos não correspondem aos gravados (em {0} do arquivo de teste)."),
                              StressText.Size(offsetBytes)),
                DiskAdvice);
        }

        public static StressStageResult DiskIoFailed(StageContext ctx, int win32)
        {
            return R(ctx, StressOutcome.Failed,
                string.Format(L("Le disque a renvoyé une erreur d'entrée/sortie (code Windows {0} : {1}).",
                                "The drive returned an I/O error (Windows code {0}: {1}).",
                                "El disco devolvió un error de E/S (código de Windows {0}: {1}).",
                                "Das Laufwerk meldete einen E/A-Fehler (Windows-Code {0}: {1}).",
                                "Il disco ha restituito un errore di I/O (codice Windows {0}: {1}).",
                                "O disco retornou um erro de E/S (código do Windows {0}: {1})."),
                              win32, new System.ComponentModel.Win32Exception(win32).Message),
                DiskAdvice, "nvme_hmb", "pcie_aspm");
        }

        public static StressStageResult DiskSlow(StageContext ctx, string summary, int spikes)
        {
            return R(ctx, StressOutcome.Warning,
                summary + " " + string.Format(L("Mais {0} blocage(s) de plus de 2 s.", "But {0} stall(s) longer than 2 s.", "Pero {0} bloqueo(s) de más de 2 s.", "Aber {0} Hänger länger als 2 s.", "Ma {0} blocco/i oltre 2 s.", "Mas {0} travamento(s) de mais de 2 s."), spikes),
                L("Des blocages de plusieurs secondes figent Windows (événement stornvme 129). Applique les correctifs NVMe HMB et ASPM, et mets à jour le firmware du SSD.",
                  "Multi-second stalls freeze Windows (stornvme event 129). Apply the NVMe HMB and ASPM fixes and update the SSD firmware.",
                  "Los bloqueos de varios segundos congelan Windows (evento stornvme 129). Aplica las correcciones NVMe HMB y ASPM y actualiza el firmware del SSD.",
                  "Hänger von mehreren Sekunden frieren Windows ein (stornvme-Ereignis 129). NVMe-HMB- und ASPM-Korrekturen anwenden und SSD-Firmware aktualisieren.",
                  "Blocchi di diversi secondi congelano Windows (evento stornvme 129). Applica le correzioni NVMe HMB e ASPM e aggiorna il firmware dell'SSD.",
                  "Travamentos de vários segundos congelam o Windows (evento stornvme 129). Aplique as correções NVMe HMB e ASPM e atualize o firmware do SSD."),
                "nvme_hmb", "pcie_aspm");
        }

        public static StressStageResult InternalError(StageContext ctx, Exception ex)
        {
            return R(ctx, StressOutcome.Warning,
                L("Étape interrompue par une erreur interne : ", "Stage interrupted by an internal error: ", "Etapa interrumpida por un error interno: ", "Schritt durch internen Fehler abgebrochen: ", "Fase interrotta da un errore interno: ", "Etapa interrompida por um erro interno: ") + ex.Message,
                null);
        }

        /// <summary>Errors Windows logged while the stage ran override a clean result.</summary>
        public static StressStageResult MergeEvents(StageContext ctx, StressStageResult r, List<LiveEvent> events)
        {
            if (events == null || events.Count == 0 || r.Outcome == StressOutcome.Failed) return r;
            string list = string.Join(", ", events.Select(e => e.ToString()).Distinct().Take(4));
            var gpu = events.Where(e => e.Kind == LiveEventKind.GpuDriver).ToList();
            var whea = events.Where(e => e.Kind == LiveEventKind.Whea).ToList();
            var disk = events.Where(e => e.Kind == LiveEventKind.Disk).ToList();
            StressStageKind k = ctx.Stage.Kind;
            bool gpuStage = k == StressStageKind.GpuCompute || k == StressStageKind.Vram || k == StressStageKind.Transient;

            if (gpu.Count > 0 && gpuStage)
                return GpuDriverReset(ctx, list);
            if (whea.Count > 0)
            {
                string summary = string.Format(L("Windows a enregistré {0} erreur(s) matérielle(s) WHEA pendant cette étape ({1}).",
                                                 "Windows logged {0} WHEA hardware error(s) during this stage ({1}).",
                                                 "Windows registró {0} error(es) de hardware WHEA durante esta etapa ({1}).",
                                                 "Windows protokollierte {0} WHEA-Hardwarefehler während dieses Schritts ({1}).",
                                                 "Windows ha registrato {0} errore/i hardware WHEA durante questa fase ({1}).",
                                                 "O Windows registrou {0} erro(s) de hardware WHEA durante esta etapa ({1})."), whea.Count, list);
                bool pcieStage = gpuStage || k == StressStageKind.Disk;
                return R(ctx, StressOutcome.Failed, summary, pcieStage ? PcieAdvice : AdviceFor(k), pcieStage ? new[] { "pcie_aspm" } : new string[0]);
            }
            if (disk.Count > 0 && k == StressStageKind.Disk)
                return R(ctx, StressOutcome.Failed,
                    L("Le contrôleur de stockage a signalé des erreurs ou des délais dépassés (", "The storage controller reported errors or timeouts (", "El controlador de almacenamiento informó errores o tiempos agotados (", "Der Speichercontroller meldete Fehler oder Zeitüberschreitungen (", "Il controller di archiviazione ha segnalato errori o timeout (", "O controlador de armazenamento relatou erros ou tempos esgotados (") + list + ").",
                    DiskAdvice, "nvme_hmb", "pcie_aspm");

            // An error unrelated to the stage (e.g. a GPU reset during the RAM test): worth flagging, not a failure of this stage.
            var w = R(ctx, StressOutcome.Warning,
                (r.Summary ?? "") + " " + L("Windows a toutefois signalé : ", "Windows did report: ", "Windows informó: ", "Windows meldete jedoch: ", "Windows ha comunque segnalato: ", "O Windows relatou: ") + list + ".",
                gpu.Count > 0 ? DriverResetAdvice : DiskAdvice);
            return w;
        }

        /// <summary>Verdict for a stage that was running when the PC went down (found at the next launch).</summary>
        public static StressStageResult Interrupted(StressStageKind k)
        {
            return R(k, StressOutcome.Interrupted,
                L("Le PC a planté ou redémarré pendant cette étape : le test n'est pas allé au bout.",
                  "The PC crashed or rebooted during this stage: the test never finished.",
                  "El PC se colgó o reinició durante esta etapa: la prueba no terminó.",
                  "Der PC ist während dieses Schritts abgestürzt oder neu gestartet: Der Test wurde nicht beendet.",
                  "Il PC si è bloccato o riavviato durante questa fase: il test non è terminato.",
                  "O PC travou ou reiniciou durante esta etapa: o teste não terminou."),
                AdviceFor(k) + " " + L("L'écran bleu correspondant est sans doute dans l'onglet Crashs.", "The matching blue screen is probably listed under Crashes.", "La pantalla azul correspondiente probablemente está en Fallos.", "Der passende Bluescreen steht vermutlich unter Abstürze.", "La schermata blu corrispondente è probabilmente in Crash.", "A tela azul correspondente provavelmente está em Falhas."),
                k == StressStageKind.GpuCompute || k == StressStageKind.Vram || k == StressStageKind.Transient ? new[] { "tdr" } : new string[0]);
        }

        public static StressStageResult Abandoned(StressStageKind k)
        {
            return R(k, StressOutcome.Stopped,
                L("Test interrompu avant la fin (PC éteint, session fermée ou application quittée), sans trace de plantage dans le journal Windows.",
                  "Test interrupted before the end (PC shut down, session closed or app quit), with no crash recorded in the Windows log.",
                  "Prueba interrumpida antes del final (PC apagado, sesión cerrada o app cerrada), sin bloqueo registrado en Windows.",
                  "Test vor dem Ende unterbrochen (PC aus, Abmeldung oder App beendet), ohne Absturz im Windows-Protokoll.",
                  "Test interrotto prima della fine (PC spento, sessione chiusa o app chiusa), senza crash nel registro di Windows.",
                  "Teste interrompido antes do fim (PC desligado, sessão encerrada ou app fechado), sem travamento registrado no Windows."), null);
        }

        public static StressStageResult NotRun(StressStageKind k)
        {
            return R(k, StressOutcome.Skipped, L("Non lancée (test arrêté).", "Not run (test stopped).", "No ejecutada (prueba detenida).", "Nicht ausgeführt (Test gestoppt).", "Non eseguita (test interrotto).", "Não executada (teste parado)."), null);
        }

        public static StressStageResult SkippedAfterDriverReset(StressStageKind k)
        {
            return R(k, StressOutcome.Skipped,
                L("Sautée : le pilote graphique a déjà décroché à une étape précédente.", "Skipped: the graphics driver already dropped out in an earlier stage.", "Omitida: el controlador gráfico ya falló en una etapa anterior.", "Übersprungen: Der Grafiktreiber ist bereits zuvor ausgefallen.", "Saltata: il driver grafico è già caduto in una fase precedente.", "Ignorada: o driver gráfico já caiu em uma etapa anterior."), null);
        }
    }
}
