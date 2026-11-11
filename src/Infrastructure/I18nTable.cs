using System.Collections.Generic;

namespace WinCrashDoctor
{
    /// <summary>
    /// ES / DE / IT / PT translations of the engine texts that only exist in FR + EN
    /// (stop-code explanations, fix results, driver explanations, report headings).
    /// Key = the exact English text; value = { es, de, it, pt }.
    /// </summary>
    public static class I18nTable
    {
        public static string Translate(string en, AppLang lang)
        {
            string[] t;
            if (en == null) return null;
            if (Map.TryGetValue(en, out t)) return Pick(t, lang);

            // Composite texts ("<stop-code explanation> <driver explanation>"): translate the known
            // leading sentence block, then the rest (kept in English if unknown).
            foreach (var kv in Map)
            {
                string k = kv.Key;
                if (k.Length < 40 || en.Length <= k.Length + 1 || en[k.Length] != ' ' || !en.StartsWith(k, System.StringComparison.Ordinal))
                    continue;
                string head = Pick(kv.Value, lang);
                if (head == null) return null;
                string rest = en.Substring(k.Length + 1);
                return head + " " + (Translate(rest, lang) ?? rest);
            }
            return null;
        }

        private static string Pick(string[] t, AppLang lang)
        {
            switch (lang)
            {
                case AppLang.ES: return t[0];
                case AppLang.DE: return t[1];
                case AppLang.IT: return t[2];
                case AppLang.PT: return t[3];
                default: return null;
            }
        }

        public static int Count { get { return Map.Count; } }

        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            // ---------------- Stop-code explanations (BugCheckCatalog)
            { "A driver touched a memory address it was not allowed to at that moment. Almost always a third-party driver (network, audio, RGB, antivirus); less often unstable RAM.", new[] {
                "Un controlador accedió a una dirección de memoria que no le estaba permitida en ese momento. Casi siempre un controlador de terceros (red, audio, RGB, antivirus); con menos frecuencia, RAM inestable.",
                "Ein Treiber hat zu diesem Zeitpunkt auf eine unzulässige Speicheradresse zugegriffen. Fast immer ein Fremdtreiber (Netzwerk, Audio, RGB, Antivirus); seltener instabiler RAM.",
                "Un driver ha toccato un indirizzo di memoria non consentito in quel momento. Quasi sempre un driver di terze parti (rete, audio, RGB, antivirus); più raramente RAM instabile.",
                "Um driver acessou um endereço de memória que não lhe era permitido naquele momento. Quase sempre um driver de terceiros (rede, áudio, RGB, antivírus); mais raramente RAM instável." } },
            { "A kernel memory block was corrupted. Typical causes: a faulty third-party driver (antivirus, VPN, RGB) or unstable RAM (XMP/EXPO profile).", new[] {
                "Se corrompió un bloque de memoria del núcleo. Causas típicas: un controlador de terceros defectuoso (antivirus, VPN, RGB) o RAM inestable (perfil XMP/EXPO).",
                "Ein Kernel-Speicherblock wurde beschädigt. Typische Ursachen: fehlerhafter Fremdtreiber (Antivirus, VPN, RGB) oder instabiler RAM (XMP/EXPO-Profil).",
                "Un blocco di memoria del kernel è stato corrotto. Cause tipiche: driver di terze parti difettoso (antivirus, VPN, RGB) o RAM instabile (profilo XMP/EXPO).",
                "Um bloco de memória do kernel foi corrompido. Causas típicas: driver de terceiros defeituoso (antivírus, VPN, RGB) ou RAM instável (perfil XMP/EXPO)." } },
            { "Windows' memory manager found a serious inconsistency. Very often RAM (aggressive XMP/EXPO profile, failing stick), sometimes a driver.", new[] {
                "El administrador de memoria de Windows encontró una incoherencia grave. Muy a menudo la RAM (perfil XMP/EXPO agresivo, módulo defectuoso), a veces un controlador.",
                "Die Speicherverwaltung von Windows hat eine schwere Inkonsistenz gefunden. Sehr oft der RAM (aggressives XMP/EXPO-Profil, defektes Modul), manchmal ein Treiber.",
                "Il gestore della memoria di Windows ha trovato un'incoerenza grave. Molto spesso la RAM (profilo XMP/EXPO aggressivo, modulo difettoso), a volte un driver.",
                "O gerenciador de memória do Windows encontrou uma inconsistência grave. Muitas vezes a RAM (perfil XMP/EXPO agressivo, pente com defeito), às vezes um driver." } },
            { "A kernel component (driver) raised an error it did not handle. Parameter 2 points straight into the faulting driver.", new[] {
                "Un componente del núcleo (controlador) generó un error que no gestionó. El parámetro 2 apunta directamente al controlador culpable.",
                "Eine Kernel-Komponente (Treiber) hat einen Fehler ausgelöst, den sie nicht behandelt hat. Parameter 2 zeigt direkt in den fehlerhaften Treiber.",
                "Un componente del kernel (driver) ha generato un errore che non ha gestito. Il parametro 2 punta direttamente al driver colpevole.",
                "Um componente do kernel (driver) gerou um erro que não tratou. O parâmetro 2 aponta diretamente para o driver culpado." } },
            { "The NTFS file system hit an error: failing disk, faulty SATA cable or volume corruption. Run chkdsk and check SMART.", new[] {
                "El sistema de archivos NTFS encontró un error: disco defectuoso, cable SATA dañado o volumen corrupto. Ejecute chkdsk y revise el SMART.",
                "Das NTFS-Dateisystem hat einen Fehler festgestellt: defekte Festplatte, fehlerhaftes SATA-Kabel oder beschädigtes Volume. chkdsk ausführen und SMART prüfen.",
                "Il file system NTFS ha riscontrato un errore: disco guasto, cavo SATA difettoso o volume corrotto. Esegui chkdsk e controlla lo SMART.",
                "O sistema de arquivos NTFS encontrou um erro: disco com defeito, cabo SATA danificado ou volume corrompido. Execute o chkdsk e verifique o SMART." } },
            { "An error occurred during a system call. Parameter 2 is the faulting instruction, often in a graphics driver, anti-cheat or antivirus.", new[] {
                "Se produjo un error durante una llamada al sistema. El parámetro 2 es la instrucción culpable, a menudo en un controlador gráfico, un antitrampas o un antivirus.",
                "Während eines Systemaufrufs ist ein Fehler aufgetreten. Parameter 2 ist die fehlerhafte Anweisung, oft in einem Grafiktreiber, Anti-Cheat oder Antivirus.",
                "Si è verificato un errore durante una chiamata di sistema. Il parametro 2 è l'istruzione colpevole, spesso in un driver grafico, un anti-cheat o un antivirus.",
                "Ocorreu um erro durante uma chamada de sistema. O parâmetro 2 é a instrução culpada, muitas vezes em um driver gráfico, anti-cheat ou antivírus." } },
            { "The page frame database is corrupted. Most often unstable or failing RAM; more rarely a driver freeing the same memory twice.", new[] {
                "La base de datos de páginas de memoria está corrupta. Lo más habitual: RAM inestable o defectuosa; más raramente un controlador que libera la misma memoria dos veces.",
                "Die Seitenrahmen-Datenbank ist beschädigt. Meist instabiler oder defekter RAM; seltener ein Treiber, der denselben Speicher zweimal freigibt.",
                "Il database delle pagine di memoria è corrotto. Più spesso RAM instabile o guasta; più raramente un driver che libera due volte la stessa memoria.",
                "O banco de dados de páginas de memória está corrompido. Na maioria das vezes RAM instável ou com defeito; mais raramente um driver que libera a mesma memória duas vezes." } },
            { "The kernel accessed memory that does not exist. Parameter 3 points to the faulting instruction. Causes: buggy driver, antivirus, or unstable RAM.", new[] {
                "El núcleo accedió a una memoria que no existe. El parámetro 3 apunta a la instrucción culpable. Causas: controlador defectuoso, antivirus o RAM inestable.",
                "Der Kernel hat auf nicht vorhandenen Speicher zugegriffen. Parameter 3 zeigt auf die fehlerhafte Anweisung. Ursachen: fehlerhafter Treiber, Antivirus oder instabiler RAM.",
                "Il kernel ha acceduto a una memoria inesistente. Il parametro 3 punta all'istruzione colpevole. Cause: driver difettoso, antivirus o RAM instabile.",
                "O kernel acessou uma memória que não existe. O parâmetro 3 aponta para a instrução culpada. Causas: driver com defeito, antivírus ou RAM instável." } },
            { "Windows could not read back a kernel memory page from disk. Sign of a failing SSD/disk, cable or unstable controller.", new[] {
                "Windows no pudo releer desde el disco una página de memoria del núcleo. Señal de un SSD/disco defectuoso, un cable o un controlador inestable.",
                "Windows konnte eine Kernel-Speicherseite nicht von der Festplatte zurücklesen. Zeichen für eine defekte SSD/Festplatte, ein Kabel oder einen instabilen Controller.",
                "Windows non è riuscito a rileggere dal disco una pagina di memoria del kernel. Segno di un SSD/disco guasto, di un cavo o di un controller instabile.",
                "O Windows não conseguiu reler do disco uma página de memória do kernel. Sinal de SSD/disco com defeito, cabo ou controlador instável." } },
            { "Windows cannot reach its boot disk: SATA/RAID mode changed in BIOS, failed storage driver update or failing disk.", new[] {
                "Windows no puede acceder a su disco de arranque: modo SATA/RAID cambiado en la BIOS, actualización fallida del controlador de almacenamiento o disco defectuoso.",
                "Windows erreicht sein Startlaufwerk nicht: SATA/RAID-Modus im BIOS geändert, fehlgeschlagenes Speichertreiber-Update oder defekte Festplatte.",
                "Windows non riesce a raggiungere il disco di avvio: modalità SATA/RAID cambiata nel BIOS, aggiornamento del driver di archiviazione fallito o disco guasto.",
                "O Windows não consegue acessar o disco de inicialização: modo SATA/RAID alterado na BIOS, atualização do driver de armazenamento com falha ou disco com defeito." } },
            { "A system thread crashed and was not caught. Parameter 2 points into the responsible driver (often graphics, Wi-Fi or a recently installed driver).", new[] {
                "Un hilo del sistema falló y no fue capturado. El parámetro 2 apunta al controlador responsable (a menudo gráfico, Wi-Fi o uno instalado recientemente).",
                "Ein System-Thread ist abgestürzt und wurde nicht abgefangen. Parameter 2 zeigt in den verantwortlichen Treiber (oft Grafik, WLAN oder ein kürzlich installierter Treiber).",
                "Un thread di sistema è andato in crash senza essere intercettato. Il parametro 2 punta al driver responsabile (spesso grafico, Wi-Fi o installato di recente).",
                "Uma thread do sistema travou e não foi capturada. O parâmetro 2 aponta para o driver responsável (muitas vezes gráfico, Wi-Fi ou instalado recentemente)." } },
            { "The CPU raised a trap the kernel could not handle (often a stack overflow). Leads: CPU overclock/undervolt, RAM, very low-level driver.", new[] {
                "El procesador generó una excepción que el núcleo no pudo gestionar (a menudo un desbordamiento de pila). Pistas: overclock/undervolt del CPU, RAM, controlador de muy bajo nivel.",
                "Der Prozessor hat eine Ausnahme ausgelöst, die der Kernel nicht behandeln konnte (oft ein Stapelüberlauf). Hinweise: CPU-Übertaktung/Undervolting, RAM, sehr hardwarenaher Treiber.",
                "Il processore ha generato un'eccezione che il kernel non ha potuto gestire (spesso uno stack overflow). Piste: overclock/undervolt della CPU, RAM, driver di bassissimo livello.",
                "O processador gerou uma exceção que o kernel não conseguiu tratar (muitas vezes um estouro de pilha). Pistas: overclock/undervolt da CPU, RAM, driver de nível muito baixo." } },
            { "An unhandled exception in kernel mode. Parameter 2 is the faulting address. Third-party driver or unstable RAM.", new[] {
                "Una excepción no gestionada en modo núcleo. El parámetro 2 es la dirección culpable. Controlador de terceros o RAM inestable.",
                "Eine unbehandelte Ausnahme im Kernelmodus. Parameter 2 ist die fehlerhafte Adresse. Fremdtreiber oder instabiler RAM.",
                "Un'eccezione non gestita in modalità kernel. Il parametro 2 è l'indirizzo colpevole. Driver di terze parti o RAM instabile.",
                "Uma exceção não tratada no modo kernel. O parâmetro 2 é o endereço culpado. Driver de terceiros ou RAM instável." } },
            { "The CPU detected a fatal hardware error (older equivalent of WHEA). Overclock, voltage, temperature or motherboard.", new[] {
                "El procesador detectó un error de hardware fatal (equivalente antiguo de WHEA). Overclock, voltaje, temperatura o placa base.",
                "Der Prozessor hat einen schweren Hardwarefehler erkannt (älteres Gegenstück zu WHEA). Übertaktung, Spannung, Temperatur oder Mainboard.",
                "Il processore ha rilevato un errore hardware fatale (vecchio equivalente di WHEA). Overclock, tensione, temperatura o scheda madre.",
                "O processador detectou um erro de hardware fatal (antigo equivalente do WHEA). Overclock, tensão, temperatura ou placa-mãe." } },
            { "A driver did not respond during sleep or wake. Very common with Wi-Fi, USB and graphics drivers; Fast Startup often makes it worse.", new[] {
                "Un controlador no respondió al entrar o salir de la suspensión. Muy habitual con controladores Wi-Fi, USB y gráficos; el Inicio rápido suele empeorarlo.",
                "Ein Treiber hat beim Wechsel in den oder aus dem Ruhezustand nicht reagiert. Sehr häufig bei WLAN-, USB- und Grafiktreibern; der Schnellstart verschlimmert es oft.",
                "Un driver non ha risposto durante la sospensione o il risveglio. Molto comune con driver Wi-Fi, USB e grafici; l'Avvio rapido spesso peggiora le cose.",
                "Um driver não respondeu ao entrar ou sair da suspensão. Muito comum com drivers Wi-Fi, USB e gráficos; a Inicialização rápida costuma piorar." } },
            { "Internal power-management error, often tied to hibernation or Fast Startup.", new[] {
                "Error interno de la gestión de energía, a menudo relacionado con la hibernación o el Inicio rápido.",
                "Interner Fehler der Energieverwaltung, oft im Zusammenhang mit dem Ruhezustand oder dem Schnellstart.",
                "Errore interno della gestione energetica, spesso legato all'ibernazione o all'Avvio rapido.",
                "Erro interno do gerenciamento de energia, muitas vezes ligado à hibernação ou à Inicialização rápida." } },
            { "A driver tried to write to read-only memory: buggy or malicious driver.", new[] {
                "Un controlador intentó escribir en memoria de solo lectura: controlador defectuoso o malicioso.",
                "Ein Treiber hat versucht, in schreibgeschützten Speicher zu schreiben: fehlerhafter oder bösartiger Treiber.",
                "Un driver ha tentato di scrivere in una memoria di sola lettura: driver difettoso o dannoso.",
                "Um driver tentou gravar em memória somente leitura: driver com defeito ou malicioso." } },
            { "A driver misused kernel memory (double free, bad size). Almost always software.", new[] {
                "Un controlador usó mal la memoria del núcleo (doble liberación, tamaño incorrecto). Casi siempre es software.",
                "Ein Treiber hat Kernel-Speicher falsch verwendet (doppelte Freigabe, falsche Größe). Fast immer Software.",
                "Un driver ha usato male la memoria del kernel (doppia liberazione, dimensione errata). Quasi sempre software.",
                "Um driver usou mal a memória do kernel (liberação dupla, tamanho incorreto). Quase sempre software." } },
            { "Windows Driver Verifier caught a driver misbehaving. If you enabled it, turn it off (verifier /reset) after noting the culprit.", new[] {
                "El Comprobador de controladores de Windows detectó un controlador defectuoso. Si lo activó, desactívelo (verifier /reset) tras anotar el culpable.",
                "Die Windows-Treiberüberprüfung hat einen fehlerhaften Treiber ertappt. Falls Sie sie aktiviert haben, schalten Sie sie nach Notieren des Schuldigen ab (verifier /reset).",
                "Il Verificatore driver di Windows ha colto un driver in errore. Se l'hai attivato, disattivalo (verifier /reset) dopo aver annotato il colpevole.",
                "O Verificador de drivers do Windows flagrou um driver com problema. Se você o ativou, desative-o (verifier /reset) depois de anotar o culpado." } },
            { "A driver corrupted kernel memory. Parameter 4 points into the driver that touched it.", new[] {
                "Un controlador corrompió la memoria del núcleo. El parámetro 4 apunta al controlador que la tocó.",
                "Ein Treiber hat Kernel-Speicher beschädigt. Parameter 4 zeigt in den Treiber, der ihn berührt hat.",
                "Un driver ha corrotto la memoria del kernel. Il parametro 4 punta al driver che l'ha toccata.",
                "Um driver corrompeu a memória do kernel. O parâmetro 4 aponta para o driver que a tocou." } },
            { "A driver accessed invalid memory at a critical moment. Parameter 4 points into that driver: network (Realtek, Killer, Intel), audio or USB most often.", new[] {
                "Un controlador accedió a memoria no válida en un momento crítico. El parámetro 4 apunta a ese controlador: red (Realtek, Killer, Intel), audio o USB con mayor frecuencia.",
                "Ein Treiber hat in einem kritischen Moment auf ungültigen Speicher zugegriffen. Parameter 4 zeigt in diesen Treiber: meist Netzwerk (Realtek, Killer, Intel), Audio oder USB.",
                "Un driver ha acceduto a memoria non valida in un momento critico. Il parametro 4 punta a quel driver: più spesso rete (Realtek, Killer, Intel), audio o USB.",
                "Um driver acessou memória inválida em um momento crítico. O parâmetro 4 aponta para esse driver: na maioria das vezes rede (Realtek, Killer, Intel), áudio ou USB." } },
            { "The graphics driver got stuck waiting for the card. Predecessor of TDR: GPU driver, power or overheating.", new[] {
                "El controlador gráfico se quedó bloqueado esperando a la tarjeta. Antecesor del TDR: controlador de GPU, alimentación o sobrecalentamiento.",
                "Der Grafiktreiber blieb beim Warten auf die Karte hängen. Vorläufer von TDR: GPU-Treiber, Stromversorgung oder Überhitzung.",
                "Il driver grafico è rimasto bloccato in attesa della scheda. Antenato del TDR: driver GPU, alimentazione o surriscaldamento.",
                "O driver gráfico travou esperando a placa. Antecessor do TDR: driver da GPU, alimentação ou superaquecimento." } },
            { "A process Windows cannot live without stopped. Corrupted system files, SSD dropping out (NVMe) or overzealous antivirus.", new[] {
                "Se detuvo un proceso indispensable para Windows. Archivos del sistema dañados, SSD que se desconecta (NVMe) o antivirus demasiado agresivo.",
                "Ein für Windows unverzichtbarer Prozess wurde beendet. Beschädigte Systemdateien, aussetzende SSD (NVMe) oder übereifriges Antivirenprogramm.",
                "Si è arrestato un processo indispensabile per Windows. File di sistema corrotti, SSD che si disconnette (NVMe) o antivirus troppo aggressivo.",
                "Um processo indispensável ao Windows parou. Arquivos de sistema corrompidos, SSD que cai (NVMe) ou antivírus agressivo demais." } },
            { "A critical process was terminated, often because the system disk stopped responding (SSD, cable, controller).", new[] {
                "Se terminó un proceso crítico, a menudo porque el disco del sistema dejó de responder (SSD, cable, controlador).",
                "Ein kritischer Prozess wurde beendet, oft weil das Systemlaufwerk nicht mehr reagierte (SSD, Kabel, Controller).",
                "Un processo critico è stato terminato, spesso perché il disco di sistema ha smesso di rispondere (SSD, cavo, controller).",
                "Um processo crítico foi encerrado, muitas vezes porque o disco do sistema parou de responder (SSD, cabo, controlador)." } },
            { "A driver tried to execute code from non-executable memory: buggy driver or memory corruption.", new[] {
                "Un controlador intentó ejecutar código desde memoria no ejecutable: controlador defectuoso o memoria corrupta.",
                "Ein Treiber hat versucht, Code aus nicht ausführbarem Speicher auszuführen: fehlerhafter Treiber oder Speicherbeschädigung.",
                "Un driver ha tentato di eseguire codice da memoria non eseguibile: driver difettoso o memoria corrotta.",
                "Um driver tentou executar código a partir de memória não executável: driver com defeito ou memória corrompida." } },
            { "Fatal error in the USB stack: faulty USB device, hub or USB controller driver.", new[] {
                "Error fatal en la pila USB: dispositivo USB, concentrador o controlador USB defectuoso.",
                "Schwerer Fehler im USB-Stack: defektes USB-Gerät, Hub oder USB-Controllertreiber.",
                "Errore fatale nello stack USB: dispositivo USB, hub o driver del controller USB difettoso.",
                "Erro fatal na pilha USB: dispositivo USB, hub ou driver do controlador USB com defeito." } },
            { "A CPU core stopped responding. Almost always hardware: CPU overclock/undervolt, voltage, old BIOS, failing CPU.", new[] {
                "Un núcleo del procesador dejó de responder. Casi siempre hardware: overclock/undervolt del CPU, voltaje, BIOS antigua, CPU defectuoso.",
                "Ein Prozessorkern reagiert nicht mehr. Fast immer Hardware: CPU-Übertaktung/Undervolting, Spannung, altes BIOS, defekte CPU.",
                "Un core del processore ha smesso di rispondere. Quasi sempre hardware: overclock/undervolt della CPU, tensione, BIOS vecchio, CPU guasta.",
                "Um núcleo do processador parou de responder. Quase sempre hardware: overclock/undervolt da CPU, tensão, BIOS antiga, CPU com defeito." } },
            { "The kernel detected forbidden changes to its structures (PatchGuard): cheat or low-level monitoring driver, or failing RAM.", new[] {
                "El núcleo detectó cambios prohibidos en sus estructuras (PatchGuard): controlador de trampas o de monitorización de bajo nivel, o RAM defectuosa.",
                "Der Kernel hat verbotene Änderungen an seinen Strukturen erkannt (PatchGuard): Cheat- oder hardwarenaher Überwachungstreiber oder defekter RAM.",
                "Il kernel ha rilevato modifiche vietate alle sue strutture (PatchGuard): driver di cheat o di monitoraggio di basso livello, oppure RAM guasta.",
                "O kernel detectou alterações proibidas em suas estruturas (PatchGuard): driver de trapaça ou de monitoramento de baixo nível, ou RAM com defeito." } },
            { "Internal video memory management error: graphics driver, unstable VRAM (GPU memory overclock) or HAGS.", new[] {
                "Error interno de la gestión de memoria de vídeo: controlador gráfico, VRAM inestable (overclock de memoria de la GPU) o HAGS.",
                "Interner Fehler der Grafikspeicherverwaltung: Grafiktreiber, instabiler VRAM (GPU-Speicherübertaktung) oder HAGS.",
                "Errore interno della gestione della memoria video: driver grafico, VRAM instabile (overclock della memoria GPU) o HAGS.",
                "Erro interno do gerenciamento de memória de vídeo: driver gráfico, VRAM instável (overclock da memória da GPU) ou HAGS." } },
            { "Windows' graphics kernel (DirectX) hit a fatal error, often after a botched GPU driver update.", new[] {
                "El núcleo gráfico de Windows (DirectX) sufrió un error fatal, a menudo tras una actualización fallida del controlador de la GPU.",
                "Der Grafikkern von Windows (DirectX) hatte einen schweren Fehler, oft nach einem missglückten GPU-Treiber-Update.",
                "Il kernel grafico di Windows (DirectX) ha subito un errore fatale, spesso dopo un aggiornamento fallito del driver GPU.",
                "O kernel gráfico do Windows (DirectX) sofreu um erro fatal, muitas vezes após uma atualização malsucedida do driver da GPU." } },
            { "The graphics card stopped responding and Windows could not restart its driver. GPU driver, MPO, aggressive undervolt/overclock, power supply or PCIe cable. Parameter 2 points into the graphics driver.", new[] {
                "La tarjeta gráfica dejó de responder y Windows no pudo reiniciar su controlador. Controlador de GPU, MPO, undervolt/overclock agresivo, fuente de alimentación o cable PCIe. El parámetro 2 apunta al controlador gráfico.",
                "Die Grafikkarte reagierte nicht mehr und Windows konnte ihren Treiber nicht neu starten. GPU-Treiber, MPO, aggressives Undervolting/Übertakten, Netzteil oder PCIe-Kabel. Parameter 2 zeigt in den Grafiktreiber.",
                "La scheda grafica ha smesso di rispondere e Windows non è riuscito a riavviarne il driver. Driver GPU, MPO, undervolt/overclock aggressivo, alimentatore o cavo PCIe. Il parametro 2 punta al driver grafico.",
                "A placa de vídeo parou de responder e o Windows não conseguiu reiniciar o driver. Driver da GPU, MPO, undervolt/overclock agressivo, fonte ou cabo PCIe. O parâmetro 2 aponta para o driver gráfico." } },
            { "The graphics card did not respond in time; Windows restarted the driver successfully this time. Same causes as 0x116, less severe.", new[] {
                "La tarjeta gráfica no respondió a tiempo; esta vez Windows reinició el controlador con éxito. Mismas causas que 0x116, menos grave.",
                "Die Grafikkarte reagierte nicht rechtzeitig; diesmal konnte Windows den Treiber erfolgreich neu starten. Gleiche Ursachen wie 0x116, weniger schwer.",
                "La scheda grafica non ha risposto in tempo; questa volta Windows ha riavviato il driver con successo. Stesse cause dello 0x116, meno grave.",
                "A placa de vídeo não respondeu a tempo; desta vez o Windows reiniciou o driver com sucesso. Mesmas causas do 0x116, menos grave." } },
            { "The graphics scheduler detected an inconsistency: GPU driver, HAGS or card overclock.", new[] {
                "El planificador gráfico detectó una incoherencia: controlador de GPU, HAGS u overclock de la tarjeta.",
                "Der Grafik-Scheduler hat eine Inkonsistenz erkannt: GPU-Treiber, HAGS oder Übertaktung der Karte.",
                "Lo scheduler grafico ha rilevato un'incoerenza: driver GPU, HAGS o overclock della scheda.",
                "O agendador gráfico detectou uma inconsistência: driver da GPU, HAGS ou overclock da placa." } },
            { "The hardware reported an uncorrectable error: CPU (overclock, undervolt, Curve Optimizer), RAM, PCIe link or power supply. Not a driver problem.", new[] {
                "El hardware informó de un error irrecuperable: CPU (overclock, undervolt, Curve Optimizer), RAM, enlace PCIe o fuente de alimentación. No es un problema de controladores.",
                "Die Hardware hat einen nicht korrigierbaren Fehler gemeldet: CPU (Übertaktung, Undervolting, Curve Optimizer), RAM, PCIe-Verbindung oder Netzteil. Kein Treiberproblem.",
                "L'hardware ha segnalato un errore non correggibile: CPU (overclock, undervolt, Curve Optimizer), RAM, collegamento PCIe o alimentatore. Non è un problema di driver.",
                "O hardware relatou um erro incorrigível: CPU (overclock, undervolt, Curve Optimizer), RAM, link PCIe ou fonte. Não é um problema de driver." } },
            { "A memory page was corrupted by hardware: failing or unstable RAM.", new[] {
                "Una página de memoria fue corrompida por el hardware: RAM defectuosa o inestable.",
                "Eine Speicherseite wurde durch Hardware beschädigt: defekter oder instabiler RAM.",
                "Una pagina di memoria è stata corrotta dall'hardware: RAM guasta o instabile.",
                "Uma página de memória foi corrompida pelo hardware: RAM com defeito ou instável." } },
            { "A driver hogged the CPU for too long. Classics: old NVMe SSD firmware, storage/SATA driver, audio or network driver.", new[] {
                "Un controlador acaparó el procesador demasiado tiempo. Clásicos: firmware antiguo del SSD NVMe, controlador de almacenamiento/SATA, audio o red.",
                "Ein Treiber hat die CPU zu lange blockiert. Klassiker: alte NVMe-SSD-Firmware, Speicher-/SATA-Treiber, Audio- oder Netzwerktreiber.",
                "Un driver ha monopolizzato la CPU troppo a lungo. Classici: firmware SSD NVMe vecchio, driver di archiviazione/SATA, audio o rete.",
                "Um driver monopolizou a CPU por tempo demais. Clássicos: firmware antigo do SSD NVMe, driver de armazenamento/SATA, áudio ou rede." } },
            { "The kernel detected corruption of its data structures (buffer overrun). Third-party driver, anti-cheat or unstable RAM.", new[] {
                "El núcleo detectó la corrupción de sus estructuras de datos (desbordamiento de búfer). Controlador de terceros, antitrampas o RAM inestable.",
                "Der Kernel hat eine Beschädigung seiner Datenstrukturen erkannt (Pufferüberlauf). Fremdtreiber, Anti-Cheat oder instabiler RAM.",
                "Il kernel ha rilevato la corruzione delle sue strutture dati (buffer overrun). Driver di terze parti, anti-cheat o RAM instabile.",
                "O kernel detectou corrupção em suas estruturas de dados (estouro de buffer). Driver de terceiros, anti-cheat ou RAM instável." } },
            { "The kernel heap was corrupted by a driver. Often a graphics, audio or RGB device driver.", new[] {
                "Un controlador corrompió el montón del núcleo. A menudo un controlador gráfico, de audio o de un dispositivo RGB.",
                "Der Kernel-Heap wurde von einem Treiber beschädigt. Oft ein Grafik-, Audio- oder RGB-Gerätetreiber.",
                "L'heap del kernel è stato corrotto da un driver. Spesso un driver grafico, audio o di un dispositivo RGB.",
                "O heap do kernel foi corrompido por um driver. Muitas vezes um driver gráfico, de áudio ou de dispositivo RGB." } },
            { "Fatal USB 3 driver error: device, hub, cable or USB power saving.", new[] {
                "Error fatal del controlador USB 3: dispositivo, concentrador, cable o ahorro de energía USB.",
                "Schwerer USB-3-Treiberfehler: Gerät, Hub, Kabel oder USB-Energiesparmodus.",
                "Errore fatale del driver USB 3: dispositivo, hub, cavo o risparmio energetico USB.",
                "Erro fatal do driver USB 3: dispositivo, hub, cabo ou economia de energia USB." } },
            { "The compressed memory store hit an error, often because of the disk (SSD dropping out, bad sectors) or an antivirus.", new[] {
                "El almacén de memoria comprimida encontró un error, a menudo por el disco (SSD que se desconecta, sectores defectuosos) o un antivirus.",
                "Der komprimierte Speicher hatte einen Fehler, oft wegen der Festplatte (aussetzende SSD, defekte Sektoren) oder eines Antivirenprogramms.",
                "L'archivio della memoria compressa ha riscontrato un errore, spesso a causa del disco (SSD che si disconnette, settori danneggiati) o di un antivirus.",
                "O armazenamento de memória compactada encontrou um erro, muitas vezes por causa do disco (SSD que cai, setores defeituosos) ou de um antivírus." } },
            { "The power button was held down: Windows wrote this dump to diagnose a frozen PC.", new[] {
                "Se mantuvo pulsado el botón de encendido: Windows escribió este volcado para diagnosticar un PC bloqueado.",
                "Der Netzschalter wurde gedrückt gehalten: Windows hat diesen Dump geschrieben, um einen eingefrorenen PC zu diagnostizieren.",
                "Il pulsante di accensione è stato tenuto premuto: Windows ha scritto questo dump per diagnosticare un PC bloccato.",
                "O botão de energia foi mantido pressionado: o Windows gravou este dump para diagnosticar um PC travado." } },
            { "Intentionally triggered crash (keyboard shortcut or diagnostic tool).", new[] {
                "Fallo provocado intencionadamente (atajo de teclado o herramienta de diagnóstico).",
                "Absichtlich ausgelöster Absturz (Tastenkombination oder Diagnosewerkzeug).",
                "Crash provocato intenzionalmente (scorciatoia da tastiera o strumento di diagnostica).",
                "Travamento provocado intencionalmente (atalho de teclado ou ferramenta de diagnóstico)." } },
            { "The GPU stopped responding and Windows failed to reset the graphics driver. Video signal cuts immediately (black screen) and GPU fans spin to 100%.\nSolutions: Disable MPO, disable Skia Graphite (Chrome/Edge), disable ULPS on AMD, raise TdrDelay to 10s, and check 750-850 mV points if GPU undervolted.", new[] {
                "La GPU dejó de responder y Windows no pudo reiniciar el controlador gráfico. La señal de vídeo se corta de inmediato (pantalla negra) y los ventiladores de la GPU suben al 100 %.\nSoluciones: desactive MPO, desactive Skia Graphite (Chrome/Edge), desactive ULPS en AMD, suba TdrDelay a 10 s y revise los puntos 750-850 mV si la GPU tiene undervolt.",
                "Die GPU reagierte nicht mehr und Windows konnte den Grafiktreiber nicht zurücksetzen. Das Bildsignal bricht sofort ab (schwarzer Bildschirm) und die GPU-Lüfter drehen auf 100 %.\nLösungen: MPO deaktivieren, Skia Graphite (Chrome/Edge) deaktivieren, ULPS bei AMD deaktivieren, TdrDelay auf 10 s erhöhen und bei Undervolting die Punkte 750-850 mV prüfen.",
                "La GPU ha smesso di rispondere e Windows non è riuscito a reimpostare il driver grafico. Il segnale video si interrompe subito (schermo nero) e le ventole della GPU vanno al 100%.\nSoluzioni: disattiva MPO, disattiva Skia Graphite (Chrome/Edge), disattiva ULPS su AMD, porta TdrDelay a 10 s e controlla i punti 750-850 mV se la GPU è in undervolt.",
                "A GPU parou de responder e o Windows não conseguiu reiniciar o driver gráfico. O sinal de vídeo cai imediatamente (tela preta) e as ventoinhas da GPU vão a 100%.\nSoluções: desative o MPO, desative o Skia Graphite (Chrome/Edge), desative o ULPS na AMD, aumente o TdrDelay para 10 s e verifique os pontos 750-850 mV se a GPU tiver undervolt." } },

            { "The secure kernel (virtualization-based security, Memory Integrity) detected an error. Already caused by a Windows update (April 2025) and by drivers incompatible with Memory Integrity: install the latest updates and update the drivers flagged in Windows Security.", new[] {
                "El núcleo seguro (seguridad basada en virtualización, integridad de memoria) detectó un error. Ya lo causó una actualización de Windows (abril de 2025) y controladores incompatibles con la integridad de memoria: instala las últimas actualizaciones y actualiza los controladores señalados en Seguridad de Windows.",
                "Der sichere Kernel (virtualisierungsbasierte Sicherheit, Speicherintegrität) hat einen Fehler erkannt. Bereits durch ein Windows-Update (April 2025) und durch mit der Speicherintegrität inkompatible Treiber ausgelöst: neueste Updates installieren und die in Windows-Sicherheit gemeldeten Treiber aktualisieren.",
                "Il kernel sicuro (sicurezza basata sulla virtualizzazione, integrità della memoria) ha rilevato un errore. Già causato da un aggiornamento di Windows (aprile 2025) e da driver incompatibili con l'integrità della memoria: installa gli ultimi aggiornamenti e aggiorna i driver segnalati in Sicurezza di Windows.",
                "O kernel seguro (segurança baseada em virtualização, integridade de memória) detectou um erro. Já foi causado por uma atualização do Windows (abril de 2025) e por drivers incompatíveis com a integridade de memória: instale as atualizações mais recentes e atualize os drivers apontados na Segurança do Windows." } },

            // ---------------- Fix results (DiagnosticsEngine)
            { "✓ Windows Registry updated successfully! (A PC reboot is recommended).", new[] { "✓ ¡Registro de Windows actualizado! (Se recomienda reiniciar el PC).", "✓ Windows-Registry erfolgreich aktualisiert! (Ein Neustart wird empfohlen).", "✓ Registro di Windows aggiornato! (Si consiglia di riavviare il PC).", "✓ Registro do Windows atualizado! (Recomenda-se reiniciar o PC)." } },
            { "Operation cancelled at the UAC Administrator prompt.", new[] { "Operación cancelada en el aviso de administrador (UAC).", "Vorgang an der Administrator-Abfrage (UAC) abgebrochen.", "Operazione annullata alla richiesta di amministratore (UAC).", "Operação cancelada no aviso de administrador (UAC)." } },
            { "The Administrator operation did not complete in time.", new[] { "La operación de administrador no terminó a tiempo.", "Der Administratorvorgang wurde nicht rechtzeitig abgeschlossen.", "L'operazione di amministratore non è terminata in tempo.", "A operação de administrador não terminou a tempo." } },
            { "The Administrator operation failed (see log for details).", new[] { "La operación de administrador falló (detalles en el registro).", "Der Administratorvorgang ist fehlgeschlagen (Details im Protokoll).", "L'operazione di amministratore non è riuscita (dettagli nel log).", "A operação de administrador falhou (detalhes no log)." } },
            { "Operation completed successfully.", new[] { "Operación completada correctamente.", "Vorgang erfolgreich abgeschlossen.", "Operazione completata con successo.", "Operação concluída com sucesso." } },
            { "✓ Multiplane Overlay (MPO) disabled (OverlayTestMode = 5).", new[] { "✓ Multiplane Overlay (MPO) desactivado (OverlayTestMode = 5).", "✓ Multiplane Overlay (MPO) deaktiviert (OverlayTestMode = 5).", "✓ Multiplane Overlay (MPO) disattivato (OverlayTestMode = 5).", "✓ Multiplane Overlay (MPO) desativado (OverlayTestMode = 5)." } },
            { "MPO restored to default.", new[] { "MPO restablecido por defecto.", "MPO auf Standard zurückgesetzt.", "MPO ripristinato al valore predefinito.", "MPO restaurado ao padrão." } },
            { "✓ Windows 11 24H2+ Alt+Tab freeze fix applied (OverlayMinFPS = 0).", new[] { "✓ Corrección del bloqueo Alt+Tab de Windows 11 24H2+ aplicada (OverlayMinFPS = 0).", "✓ Fix für Alt+Tab-Einfrieren unter Windows 11 24H2+ angewendet (OverlayMinFPS = 0).", "✓ Correzione del blocco Alt+Tab di Windows 11 24H2+ applicata (OverlayMinFPS = 0).", "✓ Correção do travamento Alt+Tab do Windows 11 24H2+ aplicada (OverlayMinFPS = 0)." } },
            { "OverlayMinFPS restored to default.", new[] { "OverlayMinFPS restablecido por defecto.", "OverlayMinFPS auf Standard zurückgesetzt.", "OverlayMinFPS ripristinato al valore predefinito.", "OverlayMinFPS restaurado ao padrão." } },
            { "✓ Network EEE / Green Ethernet & power-off disabled (reboot PC to apply).", new[] { "✓ EEE / Green Ethernet y apagado de red desactivados (reinicie el PC para aplicar).", "✓ Netzwerk-EEE / Green Ethernet und Abschaltung deaktiviert (Neustart erforderlich).", "✓ EEE / Green Ethernet e spegnimento di rete disattivati (riavvia il PC per applicare).", "✓ EEE / Green Ethernet e desligamento de rede desativados (reinicie o PC para aplicar)." } },
            { "Network power-saving restored to default.", new[] { "Ahorro de energía de red restablecido por defecto.", "Netzwerk-Energiesparen auf Standard zurückgesetzt.", "Risparmio energetico di rete ripristinato.", "Economia de energia de rede restaurada ao padrão." } },
            { "✓ TDR timeout set to 10 seconds (TdrDelay = 10 & TdrDdiDelay = 10).", new[] { "✓ Tiempo de espera TDR fijado en 10 segundos (TdrDelay = 10 y TdrDdiDelay = 10).", "✓ TDR-Zeitlimit auf 10 Sekunden gesetzt (TdrDelay = 10 & TdrDdiDelay = 10).", "✓ Timeout TDR impostato a 10 secondi (TdrDelay = 10 e TdrDdiDelay = 10).", "✓ Tempo limite TDR definido para 10 segundos (TdrDelay = 10 e TdrDdiDelay = 10)." } },
            { "TDR timeout restored to 2s default.", new[] { "Tiempo de espera TDR restablecido a 2 s.", "TDR-Zeitlimit auf Standard (2 s) zurückgesetzt.", "Timeout TDR ripristinato a 2 s.", "Tempo limite TDR restaurado para 2 s." } },
            { "✓ AMD ULPS disabled (EnableUlps = 0). Reboot PC to apply.", new[] { "✓ AMD ULPS desactivado (EnableUlps = 0). Reinicie el PC para aplicar.", "✓ AMD ULPS deaktiviert (EnableUlps = 0). Neustart erforderlich.", "✓ AMD ULPS disattivato (EnableUlps = 0). Riavvia il PC per applicare.", "✓ AMD ULPS desativado (EnableUlps = 0). Reinicie o PC para aplicar." } },
            { "AMD ULPS re-enabled (EnableUlps = 1).", new[] { "AMD ULPS reactivado (EnableUlps = 1).", "AMD ULPS wieder aktiviert (EnableUlps = 1).", "AMD ULPS riattivato (EnableUlps = 1).", "AMD ULPS reativado (EnableUlps = 1)." } },
            { "✓ AMD Issue Detection disabled (EnableIssueDetection = 0). Reboot PC.", new[] { "✓ AMD Issue Detection desactivado (EnableIssueDetection = 0). Reinicie el PC.", "✓ AMD Issue Detection deaktiviert (EnableIssueDetection = 0). PC neu starten.", "✓ AMD Issue Detection disattivato (EnableIssueDetection = 0). Riavvia il PC.", "✓ AMD Issue Detection desativado (EnableIssueDetection = 0). Reinicie o PC." } },
            { "AMD Issue Detection restored to default.", new[] { "AMD Issue Detection restablecido por defecto.", "AMD Issue Detection auf Standard zurückgesetzt.", "AMD Issue Detection ripristinato al valore predefinito.", "AMD Issue Detection restaurado ao padrão." } },
            { "✓ Automatic Windows Update driver overwrites blocked!", new[] { "✓ ¡Bloqueada la sustitución automática de controladores por Windows Update!", "✓ Automatisches Überschreiben von Treibern durch Windows Update blockiert!", "✓ Sovrascrittura automatica dei driver da Windows Update bloccata!", "✓ Substituição automática de drivers pelo Windows Update bloqueada!" } },
            { "Windows Update driver downloads re-enabled.", new[] { "Descargas de controladores por Windows Update reactivadas.", "Treiber-Downloads über Windows Update wieder aktiviert.", "Download dei driver da Windows Update riattivati.", "Downloads de drivers pelo Windows Update reativados." } },
            { "✓ NVMe SSD HMB protection (64MB cap + 60s timeout) enabled! Reboot PC.", new[] { "✓ ¡Protección HMB del SSD NVMe (límite 64 MB + espera 60 s) activada! Reinicie el PC.", "✓ NVMe-SSD-HMB-Schutz (64 MB + 60 s Zeitlimit) aktiviert! PC neu starten.", "✓ Protezione HMB dell'SSD NVMe (limite 64 MB + timeout 60 s) attivata! Riavvia il PC.", "✓ Proteção HMB do SSD NVMe (limite 64 MB + tempo limite 60 s) ativada! Reinicie o PC." } },
            { "NVMe HMB settings restored to default.", new[] { "Ajustes HMB del NVMe restablecidos por defecto.", "NVMe-HMB-Einstellungen auf Standard zurückgesetzt.", "Impostazioni HMB NVMe ripristinate.", "Configurações HMB do NVMe restauradas ao padrão." } },
            { "✓ PCIe Link State Power Management (ASPM) disabled on AC.", new[] { "✓ Administración de energía del enlace PCIe (ASPM) desactivada con corriente.", "✓ PCIe-Verbindungszustand-Energieverwaltung (ASPM) im Netzbetrieb deaktiviert.", "✓ Risparmio energetico del collegamento PCIe (ASPM) disattivato con alimentazione di rete.", "✓ Gerenciamento de energia do link PCIe (ASPM) desativado na tomada." } },
            { "PCIe ASPM restored to Moderate.", new[] { "ASPM PCIe restablecido a Moderado.", "PCIe ASPM auf Mittel zurückgesetzt.", "ASPM PCIe ripristinato su Moderato.", "ASPM PCIe restaurado para Moderado." } },
            { "✓ USB Selective Suspend disabled on AC.", new[] { "✓ Suspensión selectiva USB desactivada con corriente.", "✓ Selektives USB-Energiesparen im Netzbetrieb deaktiviert.", "✓ Sospensione selettiva USB disattivata con alimentazione di rete.", "✓ Suspensão seletiva USB desativada na tomada." } },
            { "USB Selective Suspend re-enabled.", new[] { "Suspensión selectiva USB reactivada.", "Selektives USB-Energiesparen wieder aktiviert.", "Sospensione selettiva USB riattivata.", "Suspensão seletiva USB reativada." } },
            { "✓ Automatic Windows Pagefile restored! Reboot PC.", new[] { "✓ ¡Archivo de paginación automático restablecido! Reinicie el PC.", "✓ Automatische Auslagerungsdatei wiederhergestellt! PC neu starten.", "✓ File di paging automatico ripristinato! Riavvia il PC.", "✓ Arquivo de paginação automático restaurado! Reinicie o PC." } },
            { "✓ Xbox Game DVR capture disabled.", new[] { "✓ Captura de Xbox Game DVR desactivada.", "✓ Xbox Game DVR-Aufnahme deaktiviert.", "✓ Acquisizione Xbox Game DVR disattivata.", "✓ Captura do Xbox Game DVR desativada." } },
            { "Xbox Game DVR capture re-enabled.", new[] { "Captura de Xbox Game DVR reactivada.", "Xbox Game DVR-Aufnahme wieder aktiviert.", "Acquisizione Xbox Game DVR riattivata.", "Captura do Xbox Game DVR reativada." } },
            { "✓ Windows Fast Startup disabled.", new[] { "✓ Inicio rápido de Windows desactivado.", "✓ Windows-Schnellstart deaktiviert.", "✓ Avvio rapido di Windows disattivato.", "✓ Inicialização rápida do Windows desativada." } },
            { "Windows Fast Startup re-enabled.", new[] { "Inicio rápido de Windows reactivado.", "Windows-Schnellstart wieder aktiviert.", "Avvio rapido di Windows riattivato.", "Inicialização rápida do Windows reativada." } },
            { "CrashDumps purged, but the SFC console was refused at the UAC prompt.", new[] { "CrashDumps eliminados, pero la consola SFC fue rechazada en el aviso UAC.", "CrashDumps gelöscht, aber die SFC-Konsole wurde an der UAC-Abfrage abgelehnt.", "CrashDumps eliminati, ma la console SFC è stata rifiutata alla richiesta UAC.", "CrashDumps removidos, mas o console SFC foi recusado no aviso UAC." } },
            { "✓ CrashDumps purged and elevated SFC /scannow console launched!", new[] { "✓ ¡CrashDumps eliminados y consola SFC /scannow de administrador abierta!", "✓ CrashDumps gelöscht und SFC /scannow-Konsole als Administrator gestartet!", "✓ CrashDumps eliminati e console SFC /scannow come amministratore avviata!", "✓ CrashDumps removidos e console SFC /scannow de administrador aberto!" } },
            { "✓ AMD External Events service stopped and disabled.", new[] { "✓ Servicio AMD External Events detenido y desactivado.", "✓ Dienst AMD External Events beendet und deaktiviert.", "✓ Servizio AMD External Events arrestato e disattivato.", "✓ Serviço AMD External Events parado e desativado." } },
            { "✓ Auto HDR disabled successfully.", new[] { "✓ Auto HDR desactivado correctamente.", "✓ Auto HDR erfolgreich deaktiviert.", "✓ Auto HDR disattivato con successo.", "✓ Auto HDR desativado com sucesso." } },
            { "Discord settings.json file not found.", new[] { "No se encontró el archivo settings.json de Discord.", "Discord-Datei settings.json nicht gefunden.", "File settings.json di Discord non trovato.", "Arquivo settings.json do Discord não encontrado." } },
            { "✓ Initial Registry snapshot restored.", new[] { "✓ Copia inicial del Registro restaurada.", "✓ Ursprüngliche Registry-Sicherung wiederhergestellt.", "✓ Backup iniziale del Registro ripristinato.", "✓ Backup inicial do Registro restaurado." } },
            { "Could not restore Registry snapshot.", new[] { "No se pudo restaurar la copia del Registro.", "Registry-Sicherung konnte nicht wiederhergestellt werden.", "Impossibile ripristinare il backup del Registro.", "Não foi possível restaurar o backup do Registro." } },
            { "✓ Windows System Restore Point created successfully.", new[] { "✓ Punto de restauración de Windows creado correctamente.", "✓ Windows-Wiederherstellungspunkt erfolgreich erstellt.", "✓ Punto di ripristino di Windows creato con successo.", "✓ Ponto de restauração do Windows criado com sucesso." } },
            { "System Protection is disabled on C: or daily restore point limit reached.", new[] { "La Protección del sistema está desactivada en C: o se alcanzó el límite diario de puntos de restauración.", "Der Computerschutz ist auf C: deaktiviert oder das tägliche Limit für Wiederherstellungspunkte ist erreicht.", "La Protezione sistema è disattivata su C: o è stato raggiunto il limite giornaliero dei punti di ripristino.", "A Proteção do sistema está desativada em C: ou o limite diário de pontos de restauração foi atingido." } },
            { "Discord GPU acceleration re-enabled (restart Discord).", new[] { "Aceleración por GPU de Discord reactivada (reinicie Discord).", "GPU-Beschleunigung in Discord wieder aktiviert (Discord neu starten).", "Accelerazione GPU di Discord riattivata (riavvia Discord).", "Aceleração por GPU do Discord reativada (reinicie o Discord)." } },
            { "✓ GPU Hardware Acceleration disabled in Discord (restart Discord to apply).", new[] { "✓ Aceleración por hardware desactivada en Discord (reinicie Discord para aplicar).", "✓ Hardwarebeschleunigung in Discord deaktiviert (Discord neu starten).", "✓ Accelerazione hardware disattivata in Discord (riavvia Discord per applicare).", "✓ Aceleração por hardware desativada no Discord (reinicie o Discord para aplicar)." } },

            // ---------------- Driver explanations (MinidumpAnalyzer)
            { "NVIDIA graphics driver (nvlddmkm.sys). Common causes: TDR timeout too short, Windows MPO, Chrome Skia Graphite, or an overly aggressive GPU undervolt (around 750–850 mV).", new[] {
                "Controlador gráfico NVIDIA (nvlddmkm.sys). Causas frecuentes: tiempo de espera TDR demasiado corto, MPO de Windows, Skia Graphite de Chrome o un undervolt de GPU demasiado agresivo (hacia 750–850 mV).",
                "NVIDIA-Grafiktreiber (nvlddmkm.sys). Häufige Ursachen: zu kurzes TDR-Zeitlimit, Windows-MPO, Chrome Skia Graphite oder zu aggressives GPU-Undervolting (um 750–850 mV).",
                "Driver grafico NVIDIA (nvlddmkm.sys). Cause frequenti: timeout TDR troppo breve, MPO di Windows, Skia Graphite di Chrome o undervolt GPU troppo aggressivo (intorno a 750–850 mV).",
                "Driver gráfico NVIDIA (nvlddmkm.sys). Causas frequentes: tempo limite TDR curto demais, MPO do Windows, Skia Graphite do Chrome ou undervolt de GPU agressivo demais (perto de 750–850 mV)." } },
            { "AMD Radeon graphics driver. Common causes: ULPS deep sleep, Windows Update silently replacing Adrenalin, or Windows MPO.", new[] {
                "Controlador gráfico AMD Radeon. Causas frecuentes: reposo profundo ULPS, Windows Update sustituyendo Adrenalin en silencio o MPO de Windows.",
                "AMD-Radeon-Grafiktreiber. Häufige Ursachen: ULPS-Tiefschlaf, Windows Update ersetzt Adrenalin unbemerkt, oder Windows-MPO.",
                "Driver grafico AMD Radeon. Cause frequenti: sonno profondo ULPS, Windows Update che sostituisce Adrenalin in silenzio o MPO di Windows.",
                "Driver gráfico AMD Radeon. Causas frequentes: sono profundo ULPS, Windows Update substituindo o Adrenalin em silêncio ou MPO do Windows." } },
            { "Chromium GPU backend (GraphiteDawnD3D11). Often tied to a Direct3D 11 hang during hardware video decoding: turning off Skia Graphite and MPO in Fixes usually helps.", new[] {
                "Motor GPU de Chromium (GraphiteDawnD3D11). Suele deberse a un bloqueo de Direct3D 11 al decodificar vídeo por hardware: desactivar Skia Graphite y MPO en Correcciones suele ayudar.",
                "Chromium-GPU-Backend (GraphiteDawnD3D11). Oft ein Direct3D-11-Hänger bei der Hardware-Videodekodierung: Skia Graphite und MPO unter Korrekturen abzuschalten hilft meist.",
                "Motore GPU di Chromium (GraphiteDawnD3D11). Spesso legato a un blocco di Direct3D 11 durante la decodifica video hardware: disattivare Skia Graphite e MPO in Correzioni di solito aiuta.",
                "Motor GPU do Chromium (GraphiteDawnD3D11). Geralmente ligado a um travamento do Direct3D 11 na decodificação de vídeo por hardware: desativar o Skia Graphite e o MPO em Correções costuma ajudar." } },
            { "Microsoft NVMe SSD driver (stornvme.sys). Common causes: an SSD controller timeout, or HMB allocation on DRAM-less SSDs under Windows 11; the NVMe HMB fix covers the latter.", new[] {
                "Controlador SSD NVMe de Microsoft (stornvme.sys). Causas frecuentes: tiempo agotado del controlador del SSD o asignación HMB en SSD sin DRAM con Windows 11; la corrección NVMe HMB trata esta última.",
                "Microsoft-NVMe-SSD-Treiber (stornvme.sys). Häufige Ursachen: Zeitüberschreitung des SSD-Controllers oder HMB-Zuweisung bei SSDs ohne DRAM unter Windows 11; die NVMe-HMB-Korrektur behebt Letzteres.",
                "Driver SSD NVMe di Microsoft (stornvme.sys). Cause frequenti: timeout del controller dell'SSD o allocazione HMB su SSD senza DRAM con Windows 11; la correzione NVMe HMB risolve la seconda.",
                "Driver SSD NVMe da Microsoft (stornvme.sys). Causas frequentes: tempo limite do controlador do SSD ou alocação HMB em SSDs sem DRAM no Windows 11; a correção NVMe HMB trata a segunda." } },

            // ---------------- AI / MCP
            { "Request cancelled.", new[] { "Solicitud cancelada.", "Anfrage abgebrochen.", "Richiesta annullata.", "Solicitação cancelada." } },
            { "No API key configured. Add it in Settings › AI assistant, choose Ollama (local, no key), or use \"Copy prompt\".", new[] {
                "Ninguna clave API configurada. Añádala en Ajustes › Asistente IA, elija Ollama (local, sin clave) o use «Copiar prompt».",
                "Kein API-Schlüssel eingerichtet. Unter Einstellungen › KI-Assistent hinzufügen, Ollama (lokal, ohne Schlüssel) wählen oder „Prompt kopieren“ nutzen.",
                "Nessuna chiave API configurata. Aggiungila in Impostazioni › Assistente IA, scegli Ollama (locale, senza chiave) o usa «Copia prompt».",
                "Nenhuma chave de API configurada. Adicione em Configurações › Assistente IA, escolha Ollama (local, sem chave) ou use «Copiar prompt»." } },
            { "Claude declined this request. Rephrase the question or use \"Copy for forum\".", new[] {
                "Claude rechazó esta solicitud. Reformule la pregunta o use «Copiar para foro».",
                "Claude hat diese Anfrage abgelehnt. Formulieren Sie die Frage um oder nutzen Sie „Für Forum kopieren“.",
                "Claude ha rifiutato questa richiesta. Riformula la domanda o usa «Copia per forum».",
                "O Claude recusou esta solicitação. Reformule a pergunta ou use «Copiar para fórum»." } },
            { "An AI agent connected through MCP asks to change your PC:\n\n", new[] { "Un agente de IA conectado por MCP pide modificar su PC:\n\n", "Ein über MCP verbundener KI-Agent möchte Ihren PC ändern:\n\n", "Un agente IA collegato tramite MCP chiede di modificare il tuo PC:\n\n", "Um agente de IA conectado via MCP pede para alterar seu PC:\n\n" } },
            { "Current value: ", new[] { "Valor actual: ", "Aktueller Wert: ", "Valore attuale: ", "Valor atual: " } },
            { "The current value is saved: you can undo it from Dump Finder (Tools > History).\n\nAllow?", new[] {
                "El valor actual se guarda: podrá deshacerlo desde Dump Finder (Herramientas > Historial).\n\n¿Permitir?",
                "Der aktuelle Wert wird gesichert: Sie können ihn in Dump Finder rückgängig machen (Werkzeuge > Verlauf).\n\nZulassen?",
                "Il valore attuale viene salvato: potrai annullare da Dump Finder (Strumenti > Cronologia).\n\nConsentire?",
                "O valor atual é salvo: você poderá desfazer no Dump Finder (Ferramentas > Histórico).\n\nPermitir?" } },

            // ---------------- Reports (ReportBuilder)
            { "Stable", new[] { "Estable", "Stabil", "Stabile", "Estável" } },
            { "Fragile", new[] { "Frágil", "Anfällig", "Fragile", "Frágil" } },
            { "Unstable", new[] { "Inestable", "Instabil", "Instabile", "Instável" } },
            { "Diagnostic report", new[] { "Informe de diagnóstico", "Diagnosebericht", "Rapporto di diagnostica", "Relatório de diagnóstico" } },
            { "stability", new[] { "estabilidad", "Stabilität", "stabilità", "estabilidade" } },
            { "Windows index ", new[] { "Índice de Windows ", "Windows-Index ", "Indice Windows ", "Índice do Windows " } },
            { " crash(es) in 30 days, ", new[] { " fallo(s) en 30 días, ", " Absturz/Abstürze in 30 Tagen, ", " crash in 30 giorni, ", " falha(s) em 30 dias, " } },
            { " severe.", new[] { " grave(s).", " schwer.", " grave/i.", " grave(s)." } },
            { "System", new[] { "Sistema", "System", "Sistema", "Sistema" } },
            { "driver ", new[] { "controlador ", "Treiber ", "driver ", "driver " } },
            { "Motherboard", new[] { "Placa base", "Mainboard", "Scheda madre", "Placa-mãe" } },
            { "Last boot", new[] { "Último arranque", "Letzter Start", "Ultimo avvio", "Última inicialização" } },
            { "Crashes in the last 30 days", new[] { "Fallos de los últimos 30 días", "Abstürze der letzten 30 Tage", "Crash degli ultimi 30 giorni", "Falhas dos últimos 30 dias" } },
            { "No crash recorded.", new[] { "Ningún fallo registrado.", "Keine Abstürze aufgezeichnet.", "Nessun crash registrato.", "Nenhuma falha registrada." } },
            { "Date", new[] { "Fecha", "Datum", "Data", "Data" } },
            { "Type", new[] { "Tipo", "Typ", "Tipo", "Tipo" } },
            { "Description", new[] { "Descripción", "Beschreibung", "Descrizione", "Descrição" } },
            { "Code · module", new[] { "Código · módulo", "Code · Modul", "Codice · modulo", "Código · módulo" } },
            { "Decoded blue screens", new[] { "Pantallas azules decodificadas", "Dekodierte Bluescreens", "Schermate blu decodificate", "Telas azuis decodificadas" } },
            { "Faulting module", new[] { "Módulo culpable", "Fehlerhaftes Modul", "Modulo colpevole", "Módulo culpado" } },
            { "Evidence", new[] { "Prueba", "Beleg", "Prova", "Evidência" } },
            { "Drivers on the stack", new[] { "Controladores en la pila", "Treiber im Stack", "Driver nello stack", "Drivers na pilha" } },
            { "Parameters", new[] { "Parámetros", "Parameter", "Parametri", "Parâmetros" } },
            { "File", new[] { "Archivo", "Datei", "File", "Arquivo" } },
            { "Installed driver", new[] { "Controlador instalado", "Installierter Treiber", "Driver installato", "Driver instalado" } },
            { "Protections", new[] { "Protecciones", "Schutzmaßnahmen", "Protezioni", "Proteções" } },
            { "Status", new[] { "Estado", "Status", "Stato", "Status" } },
            { "Current value", new[] { "Valor actual", "Aktueller Wert", "Valore attuale", "Valor atual" } },
            { "Active", new[] { "Activo", "Aktiv", "Attivo", "Ativo" } },
            { "Recommended", new[] { "Recomendado", "Empfohlen", "Consigliato", "Recomendado" } },
            { "Optional", new[] { "Opcional", "Optional", "Opzionale", "Opcional" } },
            { "Drivers worth a look", new[] { "Controladores a revisar", "Treiber, die einen Blick wert sind", "Driver da controllare", "Drivers que merecem atenção" } },
            { "Device", new[] { "Dispositivo", "Gerät", "Dispositivo", "Dispositivo" } },
            { "Provider · version", new[] { "Proveedor · versión", "Anbieter · Version", "Fornitore · versione", "Fornecedor · versão" } },
            { "Changes made by Dump Finder", new[] { "Cambios realizados por Dump Finder", "Änderungen durch Dump Finder", "Modifiche effettuate da Dump Finder", "Alterações feitas pelo Dump Finder" } },
            { "Failed", new[] { "Fallido", "Fehlgeschlagen", "Non riuscito", "Falhou" } },
            { "Undone", new[] { "Deshecho", "Rückgängig gemacht", "Annullato", "Desfeito" } },
            { "Applied", new[] { "Aplicado", "Angewendet", "Applicato", "Aplicado" } },
            { "Generated locally by Dump Finder. No data was sent anywhere.", new[] { "Generado localmente por Dump Finder. No se envió ningún dato.", "Lokal von Dump Finder erstellt. Es wurden keine Daten gesendet.", "Generato localmente da Dump Finder. Nessun dato è stato inviato.", "Gerado localmente pelo Dump Finder. Nenhum dado foi enviado." } },
            { "My PC diagnosis", new[] { "Diagnóstico de mi PC", "Diagnose meines PCs", "Diagnosi del mio PC", "Diagnóstico do meu PC" } },
            { "Stability score", new[] { "Puntuación de estabilidad", "Stabilitätswert", "Punteggio di stabilità", "Pontuação de estabilidade" } },
            { "Windows index", new[] { "índice de Windows", "Windows-Index", "indice Windows", "índice do Windows" } },
            { "driver", new[] { "controlador", "Treiber", "driver", "driver" } },
            { "Crashes (30 days)", new[] { "Fallos (30 días)", "Abstürze (30 Tage)", "Crash (30 giorni)", "Falhas (30 dias)" } },
            { "application crash(es)", new[] { "fallo(s) de aplicaciones", "Anwendungsabsturz/-abstürze", "crash di applicazioni", "falha(s) de aplicativos" } },
            { "Implicated modules", new[] { "Módulos implicados", "Beteiligte Module", "Moduli coinvolti", "Módulos envolvidos" } },
            { "Generated by Dump Finder", new[] { "Generado por Dump Finder", "Erstellt von Dump Finder", "Generato da Dump Finder", "Gerado pelo Dump Finder" } }
        };
    }
}
