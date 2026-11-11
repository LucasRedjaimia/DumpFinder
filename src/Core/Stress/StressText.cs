namespace WinCrashDoctor.Stress
{
    /// <summary>Short labels shared by the stress engines and the page (6 languages).</summary>
    internal static class StressText
    {
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        public static string PlanName(StressPlanKind k)
        {
            switch (k)
            {
                case StressPlanKind.Quick: return L("Test rapide", "Quick test", "Prueba rápida", "Schnelltest", "Test rapido", "Teste rápido");
                case StressPlanKind.Thorough: return L("Test approfondi", "Thorough test", "Prueba a fondo", "Gründlicher Test", "Test approfondito", "Teste completo");
                case StressPlanKind.CpuAllCore: return StageName(StressStageKind.CpuAllCore);
                case StressPlanKind.CpuPerCore: return StageName(StressStageKind.CpuPerCore);
                case StressPlanKind.GpuCompute: return StageName(StressStageKind.GpuCompute);
                case StressPlanKind.Vram: return StageName(StressStageKind.Vram);
                case StressPlanKind.Transient: return StageName(StressStageKind.Transient);
                case StressPlanKind.Ram: return StageName(StressStageKind.Ram);
                default: return StageName(StressStageKind.Disk);
            }
        }

        public static string StageName(StressStageKind k)
        {
            switch (k)
            {
                case StressStageKind.CpuAllCore: return L("Processeur · tous les cœurs", "CPU · all cores", "CPU · todos los núcleos", "CPU · alle Kerne", "CPU · tutti i core", "CPU · todos os núcleos");
                case StressStageKind.CpuPerCore: return L("Processeur · cœur par cœur", "CPU · core by core", "CPU · núcleo a núcleo", "CPU · Kern für Kern", "CPU · core per core", "CPU · núcleo a núcleo");
                case StressStageKind.GpuCompute: return L("Carte graphique · calcul vérifié", "GPU · verified compute", "GPU · cálculo verificado", "GPU · geprüfte Berechnung", "GPU · calcolo verificato", "GPU · cálculo verificado");
                case StressStageKind.Vram: return L("Mémoire vidéo (VRAM)", "Video memory (VRAM)", "Memoria de vídeo (VRAM)", "Grafikspeicher (VRAM)", "Memoria video (VRAM)", "Memória de vídeo (VRAM)");
                case StressStageKind.Transient: return L("Pics de charge CPU + GPU", "CPU + GPU load spikes", "Picos de carga CPU + GPU", "CPU- + GPU-Lastspitzen", "Picchi di carico CPU + GPU", "Picos de carga CPU + GPU");
                case StressStageKind.Ram: return L("Mémoire vive (RAM)", "System memory (RAM)", "Memoria RAM", "Arbeitsspeicher (RAM)", "Memoria RAM", "Memória RAM");
                default: return L("SSD · écriture + relecture", "SSD · write + read-back", "SSD · escritura + relectura", "SSD · Schreiben + Rücklesen", "SSD · scrittura + rilettura", "SSD · gravação + releitura");
            }
        }

        public static string CpuTemp { get { return L("Température CPU", "CPU temperature", "Temperatura CPU", "CPU-Temperatur", "Temperatura CPU", "Temperatura da CPU"); } }

        public static string CpuTempState(Stress.CpuTempState s)
        {
            switch (s)
            {
                case Stress.CpuTempState.Starting: return L("autorisation en attente…", "waiting for permission…", "esperando permiso…", "warte auf Freigabe…", "in attesa di autorizzazione…", "aguardando permissão…");
                case Stress.CpuTempState.NoPawnIo: return L("pilote PawnIO absent", "PawnIO driver missing", "falta el controlador PawnIO", "PawnIO-Treiber fehlt", "driver PawnIO assente", "driver PawnIO ausente");
                case Stress.CpuTempState.Declined: return L("autorisation refusée", "permission declined", "permiso denegado", "Freigabe abgelehnt", "autorizzazione negata", "permissão negada");
                case Stress.CpuTempState.Unavailable: return L("capteur illisible", "sensor unreadable", "sensor ilegible", "Sensor nicht lesbar", "sensore illeggibile", "sensor ilegível");
                default: return L("désactivée", "turned off", "desactivada", "ausgeschaltet", "disattivata", "desativada");
            }
        }

        public static string Calibrating { get { return L("Calibrage…", "Calibrating…", "Calibrando…", "Kalibrierung…", "Calibrazione…", "Calibrando…"); } }
        public static string NotAvailable { get { return L("n/d", "n/a", "n/d", "k. A.", "n/d", "n/d"); } }
        public static string Max { get { return L("Max", "Max", "Máx.", "Max.", "Max", "Máx."); } }
        public static string Errors { get { return L("Erreurs", "Errors", "Errores", "Fehler", "Errori", "Erros"); } }
        public static string Verified { get { return L("Calculs vérifiés", "Verified computations", "Cálculos verificados", "Geprüfte Berechnungen", "Calcoli verificati", "Cálculos verificados"); } }
        public static string NoMismatch { get { return L("aucun écart", "no mismatch", "sin diferencias", "keine Abweichung", "nessuna discrepanza", "nenhuma divergência"); } }
        public static string EffectiveClock { get { return L("Fréquence CPU", "CPU clock", "Frecuencia CPU", "CPU-Takt", "Frequenza CPU", "Frequência CPU"); } }
        public static string CoreClock { get { return L("Fréquence du cœur", "Core clock", "Frecuencia del núcleo", "Kerntakt", "Frequenza del core", "Frequência do núcleo"); } }
        public static string Threads { get { return L("Threads", "Threads", "Hilos", "Threads", "Thread", "Threads"); } }
        public static string Core { get { return L("Cœur testé", "Core under test", "Núcleo probado", "Getesteter Kern", "Core testato", "Núcleo testado"); } }
        public static string GpuClock { get { return L("Fréquence GPU", "GPU clock", "Frecuencia GPU", "GPU-Takt", "Frequenza GPU", "Frequência GPU"); } }
        public static string GpuPower { get { return L("Puissance GPU", "GPU power", "Potencia GPU", "GPU-Leistung", "Potenza GPU", "Potência GPU"); } }
        public static string GpuTemp { get { return L("Température GPU", "GPU temperature", "Temperatura GPU", "GPU-Temperatur", "Temperatura GPU", "Temperatura GPU"); } }
        public static string Throughput { get { return L("Débit", "Throughput", "Velocidad", "Durchsatz", "Velocità", "Taxa"); } }
        public static string Latency { get { return L("Latence max", "Peak latency", "Latencia máx.", "Max. Latenz", "Latenza max", "Latência máx."); } }
        public static string Tested { get { return L("Mémoire testée", "Memory tested", "Memoria probada", "Getesteter Speicher", "Memoria testata", "Memória testada"); } }
        public static string Coverage { get { return L("Couverture", "Coverage", "Cobertura", "Abdeckung", "Copertura", "Cobertura"); } }
        public static string Ready { get { return L("Prêt à démarrer", "Ready to start", "Listo para iniciar", "Startbereit", "Pronto per iniziare", "Pronto para iniciar"); } }
        public static string CpuInconsistent
        {
            get
            {
                return L("le même calcul a donné deux résultats différents dès le calibrage",
                         "the same computation gave two different results during calibration",
                         "el mismo cálculo dio dos resultados distintos en la calibración",
                         "dieselbe Berechnung lieferte bei der Kalibrierung zwei Ergebnisse",
                         "lo stesso calcolo ha dato due risultati diversi in calibrazione",
                         "o mesmo cálculo deu dois resultados diferentes na calibração");
            }
        }

        public static string Gb(double bytes) { return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("0.0") + " " + L("Go", "GB", "GB", "GB", "GB", "GB"); }
        /// <summary>Mo below 1 GB, Go above (for error positions).</summary>
        public static string Size(double bytes) { return bytes < (1L << 30) ? (bytes / (1024.0 * 1024.0)).ToString("N0") + " " + L("Mo", "MB", "MB", "MB", "MB", "MB") : Gb(bytes); }
        public static string MbPerSec(double mb) { return mb.ToString("N0") + " " + L("Mo/s", "MB/s", "MB/s", "MB/s", "MB/s", "MB/s"); }
    }
}
