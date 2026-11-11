using System.Collections.Generic;

namespace WinCrashDoctor
{
    public static class KnowledgeBase
    {
        public static List<KnowledgeEntry> GetEntries()
        {
            return new List<KnowledgeEntry>
            {
                new KnowledgeEntry
                {
                    Tag = "AMD RADEON",
                    BadgeColorHex = "#EF4444",
                    HardwareTarget = "AMD_GPU",
                    Title = Localizer.L(
                        "AMD Driver Timeout (« Expiration du délai d'un pilote » / amdwddmg)",
                        "AMD Driver Timeout ('Driver timeout has occurred' / amdwddmg)",
                        "AMD Driver Timeout ('Tiempo de espera agotado del controlador' / amdwddmg)",
                        "AMD Treiber-Timeout ('Treiber-Timout ist aufgetreten' / amdwddmg)",
                        "AMD Driver Timeout ('Timeout del driver rilevato' / amdwddmg)",
                        "AMD Driver Timeout ('Tempo limite do driver detectado' / amdwddmg)"),
                    Symptoms = Localizer.L(
                        "Écran noir de 3 à 10 secondes, fenêtre de rapport de bogue AMD Adrenalin, jeu ou navigateur qui plante, ou Adrenalin qui refuse de s'ouvrir (« Version incompatible »).",
                        "3–10s black screen, AMD Bug Report popup, game/browser crash, or Adrenalin refusing to launch ('driver version does not match').",
                        "Pantalla negra de 3 a 10 s, aviso de fallo de AMD Adrenalin, cierre de juego o navegador, o Adrenalin que no abre por versión incompatible.",
                        "3–10 Sek. schwarzer Bildschirm, AMD-Fehlerbericht, Spiel-/Browser-Absturz oder Adrenalin startet wegen inkompatibler Version nicht.",
                        "Schermo nero per 3–10s, popup segnalazione bug AMD, chiusura gioco/browser o Adrenalin che non si apre per versione incompatibile.",
                        "Tela preta de 3–10s, aviso de bug do AMD Adrenalin, fechamento do jogo/navegador ou Adrenalin recusando abrir por versão incompatível."),
                    Cause = Localizer.L(
                        "Trois causes principales : (1) Windows Update remplace en silence le pilote Adrenalin par un vieux pilote générique, (2) la veille ULPS coupe trop fort la tension au repos, (3) conflit MPO avec Chrome/Discord.",
                        "Top 3 causes: (1) Windows Update silently overwrites Adrenalin with an older generic driver, (2) ULPS drops idle voltage too low, (3) Windows MPO conflicts with Chrome/Discord.",
                        "Tres causas principales: (1) Windows Update sobrescribe Adrenalin en silencio, (2) ULPS baja demasiado el voltaje en reposo, (3) conflicto de MPO con Chrome/Discord.",
                        "Drei Hauptursachen: (1) Windows Update überschreibt heimlich den Adrenalin-Treiber, (2) ULPS senkt die Ruhespannung zu stark, (3) MPO-Konflikt mit Chrome/Discord.",
                        "Tre cause principali: (1) Windows Update sovrascrive silenziosamente Adrenalin, (2) ULPS abbassa troppo la tensione a riposo, (3) conflitto MPO con Chrome/Discord.",
                        "Três causas principais: (1) Windows Update substitui silenciosamente o Adrenalin, (2) ULPS reduz demais a tensão em repouso, (3) conflito de MPO com Chrome/Discord."),
                    Solution = Localizer.L(
                        "Dans « Correctifs », verrouillez les pilotes face à Windows Update, désactivez ULPS, désactivez MPO et passez le délai TDR à 10s.",
                        "In 'Fixes', lock GPU driver updates from Windows Update, disable ULPS, disable MPO, and raise TDR timeout to 10s.",
                        "En 'Correcciones', bloquee las actualizaciones de controladores de Windows Update, desactive ULPS, desactive MPO y suba TDR a 10s.",
                        "Unter 'Korrekturen': Windows-Update-Treiber sperren, ULPS deaktivieren, MPO deaktivieren und TDR-Timeout auf 10s erhöhen.",
                        "In 'Correzioni', blocca gli aggiornamenti driver di Windows Update, disattiva ULPS, disattiva MPO e imposta TDR a 10s.",
                        "Em 'Correções', bloqueie atualizações de driver do Windows Update, desative ULPS, desative MPO e aumente o TDR para 10s.")
                },
                new KnowledgeEntry
                {
                    Tag = "RADEON RX 7000 • RDNA 3",
                    BadgeColorHex = "#F43F5E",
                    HardwareTarget = "AMD_GPU",
                    Title = Localizer.L(
                        "Écran gris au démarrage & Pop-up Adrenalin en charge (RX 7800 XT / 7900)",
                        "Boot Grey Screen & In-Game Adrenalin Timeout (RX 7800 XT / 7900)",
                        "Pantalla gris al arrancar y fallo Adrenalin en carga (RX 7800 XT / 7900)",
                        "Grauer Startbildschirm & Adrenalin-Timeout unter Last (RX 7800 XT / 7900)",
                        "Schermo grigio all'avvio e Timeout Adrenalin in gioco (RX 7800 XT / 7900)",
                        "Tela cinza na inicialização e Timeout Adrenalin em jogos (RX 7800 XT / 7900)"),
                    Symptoms = Localizer.L(
                        "Écran gris uni à l'arrivée sur Windows au démarrage à froid, suivi en jeu lourd d'un gel puis du pop-up « AMD Adrenalin Driver Timeout ».",
                        "Solid grey screen when booting into Windows, plus freezes and 'AMD Adrenalin Driver Timeout' popups under heavy 3D gaming load.",
                        "Pantalla gris fija al iniciar Windows y cierres en juegos exigentes con ventana de fallo de AMD Adrenalin.",
                        "Einfarbiger grauer Bildschirm beim Windows-Start sowie Spielabstürze mit AMD-Adrenalin-Timeout-Meldung unter Last.",
                        "Schermo grigio fisso all'avvio di Windows e crash nei giochi pesanti con avviso di timeout AMD Adrenalin.",
                        "Tela cinza sólida ao iniciar o Windows e travamentos em jogos pesados com aviso de timeout do AMD Adrenalin."),
                    Cause = Localizer.L(
                        "Au boot : le Démarrage Rapide Windows + ULPS corrompent l'initialisation RDNA 3. En jeu sur RX 7800 XT : câble PCIe dédoublé (pigtail) qui s'écroule lors des pics à 400 W, HAGS actif sous Win11 24H2, ou fréquence Boost d'usine montant trop haut (> 2750 MHz).",
                        "At boot: Windows Fast Startup + ULPS corrupt RDNA 3 display init. In games on RX 7800 XT: a single daisy-chained 8-pin PCIe cable drooping under 400W spikes, Win11 24H2 HAGS, or factory Boost spiking >2750 MHz.",
                        "Al arrancar: Inicio Rápido + ULPS corrompen RDNA 3. En juego (RX 7800 XT): un solo cable PCIe en puente (daisy-chain), HAGS activo o Boost >2750 MHz.",
                        "Beim Start: Schnellstart + ULPS stören RDNA 3. Im Spiel (RX 7800 XT): einzelnes Y-PCIe-Stromkabel bei 400-W-Spitzen, HAGS aktiv oder Werkstakt >2750 MHz.",
                        "All'avvio: Avvio Rapido + ULPS corrompono RDNA 3. In gioco (RX 7800 XT): singolo cavo PCIe sdoppiato sotto picchi da 400 W, HAGS attivo o Boost >2750 MHz.",
                        "No boot: Inicialização Rápida + ULPS corrompem o RDNA 3. Em jogo (RX 7800 XT): cabo PCIe único em Y (daisy-chain), HAGS ativo ou Boost >2750 MHz."),
                    Solution = Localizer.L(
                        "1) Dans « Correctifs », désactivez Démarrage Rapide, ULPS, MPO, HAGS et Issue Detection. 2) Branchez 2 câbles PCIe 8-pins séparés depuis l'alimentation. 3) Dans Adrenalin > Réglages GPU, limitez la Fréquence Max vers 2450–2500 MHz.",
                        "1) In 'Fixes', disable Fast Startup, ULPS, MPO, HAGS, and Issue Detection. 2) Run 2 separate 8-pin PCIe cables from the PSU (no pigtail). 3) In Adrenalin Tuning, cap Max Frequency to ~2450–2500 MHz.",
                        "1) En 'Correcciones', desactive Inicio Rápido, ULPS, MPO, HAGS e Issue Detection. 2) Conecte 2 cables PCIe de 8 pines independientes. 3) En Adrenalin, limite la Frecuencia Máx. a ~2500 MHz.",
                        "1) Unter 'Korrekturen': Schnellstart, ULPS, MPO, HAGS & Issue Detection deaktivieren. 2) 2 separate 8-Pin-PCIe-Kabel vom Netzteil legen. 3) Max-Frequenz in Adrenalin auf ~2500 MHz begrenzen.",
                        "1) In 'Correzioni', disattiva Avvio Rapido, ULPS, MPO, HAGS e Issue Detection. 2) Usa 2 cavi PCIe 8-pin separati dall'alimentatore. 3) In Adrenalin limita la Frequenza Max a ~2500 MHz.",
                        "1) Em 'Correções', desative Inicialização Rápida, ULPS, MPO, HAGS e Issue Detection. 2) Use 2 cabos PCIe 8-pin independentes da fonte. 3) No Adrenalin, limite a Frequência Máx. a ~2500 MHz.")
                },
                new KnowledgeEntry
                {
                    Tag = "NVIDIA • 0x116",
                    BadgeColorHex = "#10B981",
                    HardwareTarget = "NVIDIA_GPU",
                    Title = Localizer.L(
                        "Écran noir + Ventilateurs à 100 % + Reboot (nvlddmkm Event 153 / 0x00000116)",
                        "Black Screen + 100% GPU Fans + Reboot (nvlddmkm Event 153 / 0x00000116)",
                        "Pantalla negra + Ventiladores al 100 % + Reinicio (nvlddmkm 153 / 0x00000116)",
                        "Schwarzer Bildschirm + 100 % GPU-Lüfter + Neustart (nvlddmkm 153 / 0x00000116)",
                        "Schermo nero + Ventole al 100 % + Riavvio (nvlddmkm 153 / 0x00000116)",
                        "Tela preta + Ventoinhas a 100 % + Reinício (nvlddmkm 153 / 0x00000116)"),
                    Symptoms = Localizer.L(
                        "Perte soudaine de signal vidéo (« No Signal »), ventilateurs GPU qui montent à 100 %, son qui continue quelques secondes puis redémarrage.",
                        "Sudden loss of display signal, GPU fans spinning at 100% emergency speed, audio continuing briefly, followed by a reboot.",
                        "Pérdida repentina de señal de vídeo, ventiladores GPU al 100 %, audio durante unos segundos y reinicio automático.",
                        "Plötzlicher Signalverlust, GPU-Lüfter drehen auf 100 %, Ton läuft kurz weiter, danach startet der PC neu.",
                        "Perdita improvvisa del segnale video, ventole GPU al 100 %, audio attivo per pochi secondi e successivo riavvio.",
                        "Perda súbita de sinal de vídeo, ventoinhas da GPU a 100 %, áudio continua por alguns segundos e o PC reinicia."),
                    Cause = Localizer.L(
                        "En lecture vidéo Chrome/YouTube, Skia Graphite + MPO provoque un blocage Direct3D 11 aux basses tensions. Un undervolt (MSI Afterburner) trop haut entre 750 et 850 mV amplifie aussi ce crash hors jeu.",
                        "During Chrome/YouTube video playback, Skia Graphite + MPO stalls Direct3D 11 at low P-states. An MSI Afterburner UV curve shifted too high across 750–850 mV also triggers crashes outside games.",
                        "Al reproducir vídeo en Chrome/YouTube, Skia Graphite + MPO bloquea Direct3D 11 a bajo voltaje. Una curva de undervolt demasiado alta entre 750 y 850 mV también causa fallos fuera de juegos.",
                        "Bei Chrome/YouTube-Videos blockieren Skia Graphite + MPO Direct3D 11 bei niedriger Spannung. Eine zu steile Undervolt-Kurve zwischen 750–850 mV löst ebenfalls Abstürze im Desktop-Betrieb aus.",
                        "Durante i video su Chrome/YouTube, Skia Graphite + MPO blocca Direct3D 11 a bassa tensione. Anche una curva di undervolt troppo alta tra 750 e 850 mV provoca crash fuori dai giochi.",
                        "Em vídeos no Chrome/YouTube, Skia Graphite + MPO trava o Direct3D 11 em baixa tensão. Uma curva de undervolt muito alta entre 750–850 mV também causa travamentos fora de jogos."),
                    Solution = Localizer.L(
                        "Désactivez MPO + Skia Graphite + passez TdrDelay à 10s dans « Correctifs », et gardez la courbe 750–850 mV proche de l'origine dans MSI Afterburner.",
                        "Disable MPO + Skia Graphite + set TdrDelay = 10s in 'Fixes', and keep your 750–850 mV curve points close to stock in MSI Afterburner.",
                        "Desactive MPO + Skia Graphite + ajuste TdrDelay a 10s en 'Correcciones', y mantenga los puntos de 750–850 mV cerca de los valores de fábrica.",
                        "MPO + Skia Graphite deaktivieren, TdrDelay auf 10s setzen und den Bereich 750–850 mV im MSI Afterburner nahe der Standardkurve belassen.",
                        "Disattiva MPO + Skia Graphite + imposta TdrDelay a 10s in 'Correzioni', e mantieni i punti 750–850 mV vicini alla curva originale.",
                        "Desative MPO + Skia Graphite + ajuste TdrDelay para 10s em 'Correções', e mantenha a faixa de 750–850 mV próxima do padrão no Afterburner.")
                },
                new KnowledgeEntry
                {
                    Tag = "SSD NVMe • WIN11",
                    BadgeColorHex = "#3B82F6",
                    HardwareTarget = "ALL",
                    Title = Localizer.L(
                        "Freezes totaux & BSOD SSD NVMe sous Windows 11 (stornvme 129 / 0xEF)",
                        "Total System Freezes & NVMe SSD Dropouts on Windows 11 (stornvme 129 / 0xEF)",
                        "Congelamientos totales y pantallazos NVMe en Windows 11 (stornvme 129 / 0xEF)",
                        "Komplette Freezes & NVMe-SSD-Aussetzer unter Windows 11 (stornvme 129 / 0xEF)",
                        "Freeze totali e BSOD SSD NVMe su Windows 11 (stornvme 129 / 0xEF)",
                        "Congelamentos totais e tela azul de SSD NVMe no Windows 11 (stornvme 129 / 0xEF)"),
                    Symptoms = Localizer.L(
                        "Applications figées pendant 10 à 30 secondes, suivies parfois d'un écran bleu CRITICAL_PROCESS_DIED (0xEF) ou KERNEL_DATA_INPAGE_ERROR (0x7A).",
                        "System hangs for 10–30 seconds, occasionally ending in CRITICAL_PROCESS_DIED (0xEF) or KERNEL_DATA_INPAGE_ERROR (0x7A).",
                        "El sistema se congela de 10 a 30 s, terminando a veces en CRITICAL_PROCESS_DIED (0xEF) o KERNEL_DATA_INPAGE_ERROR (0x7A).",
                        "System friert für 10–30 Sek. ein, teils gefolgt von CRITICAL_PROCESS_DIED (0xEF) oder KERNEL_DATA_INPAGE_ERROR (0x7A).",
                        "Il sistema si blocca per 10–30 secondi, talvolta terminando con CRITICAL_PROCESS_DIED (0xEF) o KERNEL_DATA_INPAGE_ERROR (0x7A).",
                        "O sistema congela por 10–30 segundos, às vezes terminando em CRITICAL_PROCESS_DIED (0xEF) ou KERNEL_DATA_INPAGE_ERROR (0x7A)."),
                    Cause = Localizer.L(
                        "Bug d'allocation du Host Memory Buffer (HMB) sous Windows 11 24H2+ sur les SSD NVMe sans DRAM (WD SN770, SN580, Lexar, Maxio) ou économie d'énergie PCIe ASPM agressive.",
                        "Host Memory Buffer (HMB) allocation bug on Windows 11 24H2+ with DRAM-less NVMe drives (WD SN770, SN580, Lexar, Maxio) or aggressive PCIe ASPM.",
                        "Fallo de asignación de Host Memory Buffer (HMB) en Windows 11 24H2+ con unidades NVMe sin DRAM o ahorro de energía PCIe ASPM agresivo.",
                        "Host-Memory-Buffer-(HMB)-Bug unter Windows 11 24H2+ bei DRAM-losen NVMe-SSDs oder zu aggressivem PCIe-ASPM.",
                        "Bug di allocazione Host Memory Buffer (HMB) su Windows 11 24H2+ con SSD NVMe DRAM-less o risparmio energetico PCIe ASPM aggressivo.",
                        "Bug de alocação do Host Memory Buffer (HMB) no Windows 11 24H2+ em SSDs NVMe sem DRAM ou economia PCIe ASPM agressiva."),
                    Solution = Localizer.L(
                        "Appliquez les correctifs « Stabilisation SSD NVMe HMB » et « Désactiver PCIe ASPM », puis vérifiez les mises à jour de firmware du SSD.",
                        "Apply the 'NVMe HMB Stabilization' and 'Disable PCIe ASPM' fixes, and check for SSD firmware updates.",
                        "Aplique 'Estabilización NVMe HMB' y 'Desactivar PCIe ASPM', y actualice el firmware del SSD.",
                        "Wenden Sie 'NVMe-HMB-Stabilisierung' und 'PCIe-ASPM deaktivieren' an und aktualisieren Sie die SSD-Firmware.",
                        "Applica 'Stabilizzazione NVMe HMB' e 'Disattiva PCIe ASPM', quindi aggiorna il firmware dell'SSD.",
                        "Aplique 'Estabilização NVMe HMB' e 'Desativar PCIe ASPM', e atualize o firmware do SSD.")
                },
                new KnowledgeEntry
                {
                    Tag = "AMD RYZEN • WHEA 18",
                    BadgeColorHex = "#F59E0B",
                    HardwareTarget = "AMD_CPU",
                    Title = Localizer.L(
                        "Redémarrage instantané au repos sur AMD Ryzen (WHEA-Logger Event 18)",
                        "Instant Idle Reboot on AMD Ryzen CPUs (WHEA-Logger Event 18)",
                        "Reinicio instantáneo en reposo en CPU AMD Ryzen (WHEA-Logger 18)",
                        "Sofortiger Neustart im Leerlauf bei AMD Ryzen (WHEA-Logger 18)",
                        "Riavvio istantaneo a riposo su CPU AMD Ryzen (WHEA-Logger 18)",
                        "Reinício instantâneo em repouso em CPUs AMD Ryzen (WHEA-Logger 18)"),
                    Symptoms = Localizer.L(
                        "Le PC redémarre net sans écran bleu ni ventilateurs à 100 %, presque toujours sur le bureau ou au repos léger.",
                        "PC reboots abruptly with no BSOD and no fan spin-up, almost always while idling or browsing lightly.",
                        "El PC se reinicia de golpe sin pantalla azul, casi siempre en el escritorio o con carga ligera.",
                        "Der PC startet ohne Bluescreen sofort neu, fast immer im Leerlauf oder beim leichten Surfen.",
                        "Il PC si riavvia di colpo senza schermata blu, quasi sempre sul desktop o a carico leggero.",
                        "O PC reinicia instantaneamente sem tela azul, quase sempre na área de trabalho ou em uso leve."),
                    Cause = Localizer.L(
                        "Tension CPU insuffisante en sortie de veille C-State à cause d'un Curve Optimizer PBO trop négatif (ex: -30 sur tous les cœurs).",
                        "Insufficient CPU core voltage during idle C-state transitions caused by an overly negative PBO Curve Optimizer (e.g., -30 all-core).",
                        "Voltaje insuficiente del núcleo al salir del reposo por un Curve Optimizer negativo demasiado agresivo (ej. -30).",
                        "Zu niedrige Kernspannung bei C-State-Wechseln durch einen zu aggressiven negativen PBO Curve Optimizer (z. B. -30).",
                        "Tensione core insufficiente in uscita dagli stati C di riposo per un Curve Optimizer PBO troppo negativo (es. -30).",
                        "Tensão insuficiente no núcleo em repouso causada por um Curve Optimizer PBO muito negativo (ex.: -30)."),
                    Solution = Localizer.L(
                        "Dans le BIOS, adoucissez le Curve Optimizer (ex: -15 au lieu de -30 sur le cœur APIC indiqué) et réglez Power Supply Idle Control sur Typical Current Idle.",
                        "In BIOS, relax Curve Optimizer (e.g., -15 instead of -30 on the reported APIC core) and set Power Supply Idle Control to Typical Current Idle.",
                        "En la BIOS, suavice Curve Optimizer (ej. -15 en lugar de -30) y ajuste Power Supply Idle Control en Typical Current Idle.",
                        "Im BIOS den Curve Optimizer entschärfen (z. B. -15 statt -30) und Power Supply Idle Control auf Typical Current Idle stellen.",
                        "Nel BIOS riduci il Curve Optimizer (es. da -30 a -15) e imposta Power Supply Idle Control su Typical Current Idle.",
                        "Na BIOS, suavize o Curve Optimizer (ex.: -15 em vez de -30) e ajuste Power Supply Idle Control para Typical Current Idle.")
                },
                new KnowledgeEntry
                {
                    Tag = "INTEL 13/14 GEN",
                    BadgeColorHex = "#8B5CF6",
                    HardwareTarget = "INTEL_CPU",
                    Title = Localizer.L(
                        "Crash « Out of Video Memory » à la compilation des shaders (Intel 13e / 14e Gen)",
                        "'Out of Video Memory' Shader Compilation Crash (Intel 13th / 14th Gen)",
                        "Fallo 'Out of Video Memory' al compilar shaders (Intel 13.ª / 14.ª Gen)",
                        "'Out of Video Memory'-Absturz bei Shader-Kompilierung (Intel 13./14. Gen)",
                        "Crash 'Out of Video Memory' durante compilazione shader (Intel 13ª / 14ª Gen)",
                        "Erro 'Out of Video Memory' ao compilar shaders (Intel 13ª / 14ª Geração)"),
                    Symptoms = Localizer.L(
                        "Fermeture des jeux Unreal Engine 5 au lancement avec un faux message de mémoire vidéo saturée ou code 0xc0000005.",
                        "Unreal Engine 5 games crash during shader compilation with a misleading 'Out of video memory' popup or 0xc0000005 error.",
                        "Juegos Unreal Engine 5 se cierran al compilar shaders con un falso mensaje de falta de memoria de vídeo o error 0xc0000005.",
                        "Unreal-Engine-5-Spiele stürzen beim Shader-Laden mit falscher VRAM-Meldung oder Fehlercode 0xc0000005 ab.",
                        "I giochi Unreal Engine 5 si chiudono compilando gli shader con un falso errore di memoria video esaurita o 0xc0000005.",
                        "Jogos Unreal Engine 5 fecham ao compilar shaders com falso erro de falta de memória de vídeo ou código 0xc0000005."),
                    Cause = Localizer.L(
                        "Instabilité CPU (Vmin Shift / limites de puissance débridées) lors de la décompression intensive des shaders sur les Core i7/i9 13e et 14e génération.",
                        "CPU instability (Vmin Shift / unlocked motherboard power limits) triggered during heavy AVX shader decompression on 13th/14th Gen i7/i9.",
                        "Inestabilidad de la CPU (Vmin Shift / límites de potencia desbloqueados) durante la descompresión intensiva de shaders en i7/i9.",
                        "CPU-Instabilität (Vmin-Shift / offene Power-Limits) bei intensiver AVX-Shader-Dekomprimierung auf Core i7/i9 der 13./14. Gen.",
                        "Instabilità della CPU (Vmin Shift / limiti di potenza sbloccati) durante la decompressione shader intensiva su i7/i9 13ª/14ª Gen.",
                        "Instabilidade da CPU (Vmin Shift / limites de energia destravados) durante a descompressão pesada de shaders nos i7/i9."),
                    Solution = Localizer.L(
                        "Mettez à jour le BIOS (microcode 0x12F+) et activez le profil « Intel Default Settings » dans le BIOS.",
                        "Update motherboard BIOS (microcode 0x12F+) and enable the 'Intel Default Settings' profile in BIOS.",
                        "Actualice la BIOS (microcódigo 0x12F+) y active el perfil 'Intel Default Settings'.",
                        "Mainboard-BIOS aktualisieren (Microcode 0x12F+) und im BIOS das Profil 'Intel Default Settings' aktivieren.",
                        "Aggiorna il BIOS (microcodice 0x12F+) e attiva il profilo 'Intel Default Settings' nel BIOS.",
                        "Atualize a BIOS (microcódigo 0x12F+) e ative o perfil 'Intel Default Settings' na BIOS.")
                },
                new KnowledgeEntry
                {
                    Tag = "ITX / RISER PCIe",
                    BadgeColorHex = "#EC4899",
                    HardwareTarget = "ALL",
                    Title = Localizer.L(
                        "Saccades, coupures audio USB & erreurs PCIe en boîtier ITX (WHEA 17)",
                        "Stutters, USB Audio Crackles & PCIe Drops in ITX Cases (WHEA 17)",
                        "Tirones, cortes de audio USB y errores PCIe en cajas ITX (WHEA 17)",
                        "Ruckler, USB-Audio-Knacken & PCIe-Fehler in ITX-Gehäusen (WHEA 17)",
                        "Scatti, disturbi audio USB ed errori PCIe nei case ITX (WHEA 17)",
                        "Engasgos, falhas de áudio USB e erros PCIe em gabinetes ITX (WHEA 17)"),
                    Symptoms = Localizer.L(
                        "Nombreux avertissements WHEA Event 17 dans le journal Windows, micro-saccades en jeu ou perte temporaire du GPU.",
                        "Floods of WHEA Event 17 warnings in Event Viewer, FPS micro-stutters, or sudden GPU disconnects.",
                        "Múltiples avisos WHEA 17 en el Visor de eventos, micro-tirones en juegos o desconexiones del GPU.",
                        "Hunderte WHEA-17-Warnungen in der Ereignisanzeige, Mikroruckler in Spielen oder plötzliche GPU-Aussetzer.",
                        "Centinaia di avvisi WHEA 17 nel Registro eventi, micro-scatti nei giochi o disconnessioni improvvise della GPU.",
                        "Centenas de avisos WHEA 17 no Visualizador de Eventos, micro-travamentos em jogos ou perda súbita da GPU."),
                    Cause = Localizer.L(
                        "Perte de signal sur câble Riser PCIe lorsque la carte mère négocie en PCIe Gen 5 sur un riser Gen 4 (ou Gen 4 sur un riser Gen 3).",
                        "Signal loss across a PCIe riser cable when the motherboard negotiates PCIe Gen 5 over a Gen 4 riser (or Gen 4 over Gen 3).",
                        "Pérdida de señal en un cable Riser PCIe cuando la placa negocia PCIe Gen 5 sobre un riser Gen 4 (o Gen 4 sobre Gen 3).",
                        "Signalverlust am PCIe-Riser-Kabel, wenn das Mainboard PCIe Gen 5 über ein Gen-4-Riser-Kabel aushandelt.",
                        "Perdita di segnale sul cavo Riser PCIe quando la scheda madre negozia PCIe Gen 5 su un riser Gen 4.",
                        "Perda de sinal no cabo Riser PCIe quando a placa-mãe negocia PCIe Gen 5 em um riser Gen 4."),
                    Solution = Localizer.L(
                        "Désactivez PCIe ASPM dans « Correctifs » et forcez manuellement le port PCIEX16 sur « Gen 4 » (au lieu de Auto) dans le BIOS.",
                        "Disable PCIe ASPM in 'Fixes' and manually set PCIEX16 speed to 'Gen 4' (instead of Auto) in BIOS.",
                        "Desactive PCIe ASPM en 'Correcciones' y fije manualmente el puerto PCIEX16 en 'Gen 4' en la BIOS.",
                        "PCIe-ASPM unter 'Korrekturen' deaktivieren und PCIEX16 im BIOS manuell von Auto auf 'Gen 4' stellen.",
                        "Disattiva PCIe ASPM in 'Correzioni' e imposta manualmente lo slot PCIEX16 su 'Gen 4' nel BIOS.",
                        "Desative o PCIe ASPM em 'Correções' e defina manualmente o slot PCIEX16 como 'Gen 4' na BIOS.")
                },
                new KnowledgeEntry
                {
                    Tag = "RAM & PAGEFILE",
                    BadgeColorHex = "#06B6D4",
                    HardwareTarget = "ALL",
                    Title = Localizer.L(
                        "Fermetures de jeux aléatoires & BSOD mémoire (0x1A / 0x3B / Event 2004)",
                        "Random Crashes to Desktop & Memory BSODs (0x1A / 0x3B / Event 2004)",
                        "Cierres aleatorios al escritorio y BSOD de memoria (0x1A / 0x3B / Event 2004)",
                        "Zufällige Spielabstürze & Speicher-Bluescreens (0x1A / 0x3B / Event 2004)",
                        "Chiusure improvvise dei giochi e BSOD memoria (0x1A / 0x3B / Event 2004)",
                        "Fechamentos aleatórios de jogos e tela azul de memória (0x1A / 0x3B / Event 2004)"),
                    Symptoms = Localizer.L(
                        "Jeux qui se ferment sans erreur après 20–40 min, onglets navigateur qui plantent, ou alerte Resource-Exhaustion 2004.",
                        "Games closing silently after 20–40 min, browser tab crashes, or Windows Resource-Exhaustion Event 2004.",
                        "Juegos que se cierran sin aviso tras 20–40 min, pestañas caídas o alerta de memoria agotada Event 2004.",
                        "Spiele schließen sich nach 20–40 Min. kommentarlos, Browser-Tabs stürzen ab oder Event 2004 tritt auf.",
                        "Giochi che si chiudono dopo 20–40 min senza errori, schede browser in crash o avviso Event 2004.",
                        "Jogos fechando sozinhos após 20–40 min, abas travando ou alerta de memória esgotada Event 2004."),
                    Cause = Localizer.L(
                        "Fichier d'échange Windows (Pagefile) désactivé manuellement, ou profil DDR5 XMP/EXPO instable lorsque le GPU réchauffe les barrettes au-delà de 50 °C.",
                        "Manually disabled Windows Pagefile, or DDR5 XMP/EXPO instability once GPU heat warms RAM sticks above 50°C.",
                        "Archivo de paginación desactivado manualmente, o perfil XMP/EXPO inestable cuando el calor de la GPU calienta la RAM.",
                        "Manuell deaktivierte Auslagerungsdatei (Pagefile) oder instabiles XMP/EXPO-Profil bei aufgeheizten RAM-Modulen (> 50 °C).",
                        "File di paging Windows disattivato manualmente, o profilo XMP/EXPO instabile quando la GPU scalda la RAM oltre 50 °C.",
                        "Arquivo de paginação desativado manualmente, ou perfil XMP/EXPO instável quando o calor da GPU aquece a RAM acima de 50 °C."),
                    Solution = Localizer.L(
                        "Activez le « Fichier d'échange (Pagefile) automatique » et lancez la réparation « DISM + SFC » dans l'onglet Correctifs.",
                        "Enable 'Automatic Windows Pagefile' and run the 'DISM + SFC System Check' in the Fixes tab.",
                        "Active 'Archivo de paginación automático' y ejecute la reparación 'DISM + SFC' en Correcciones.",
                        "Aktivieren Sie 'Automatische Auslagerungsdatei' und starten Sie die 'DISM + SFC'-Prüfung unter Korrekturen.",
                        "Attiva 'File di paging automatico' ed esegui la verifica 'DISM + SFC' nella scheda Correzioni.",
                        "Ative 'Arquivo de paginação automático' e execute o reparo 'DISM + SFC' na aba Correções.")
                },
                new KnowledgeEntry
                {
                    Tag = "PSU • KERNEL-POWER 41",
                    BadgeColorHex = "#F97316",
                    HardwareTarget = "ALL",
                    Title = Localizer.L(
                        "Coupure électrique sèche en pleine charge 3D (Kernel-Power 41 sans Minidump)",
                        "Instant Power-Off Under Heavy 3D Load (Kernel-Power 41 with Zero Minidump)",
                        "Apagado repentino bajo carga 3D intensa (Kernel-Power 41 sin Minidump)",
                        "Sofortiges Abschalten unter 3D-Last (Kernel-Power 41 ohne Minidump)",
                        "Spegnimento improvviso sotto carico 3D (Kernel-Power 41 senza Minidump)",
                        "Desligamento súbito sob carga 3D pesada (Kernel-Power 41 sem Minidump)"),
                    Symptoms = Localizer.L(
                        "Le PC s'éteint comme une coupure de courant dès qu'une scène 3D lourde démarre, sans aucun rapport dans C:\\Windows\\Minidump.",
                        "PC clicks off like a power outage during heavy 3D gaming transitions, leaving zero files in C:\\Windows\\Minidump.",
                        "El PC se apaga como un corte de luz al iniciar escenas 3D pesadas, sin dejar archivos en C:\\Windows\\Minidump.",
                        "PC schaltet unter hoher 3D-Last wie bei Stromausfall hart ab, ohne Dateien in C:\\Windows\\Minidump.",
                        "Il PC si spegne di netto durante scene 3D pesanti, senza lasciare alcun file in C:\\Windows\\Minidump.",
                        "O PC desliga na hora em cenas 3D pesadas, sem deixar nenhum arquivo em C:\\Windows\\Minidump."),
                    Cause = Localizer.L(
                        "Mise en sécurité (OCP/OPP) de l'alimentation sur un pic transitoire GPU, connecteur 12V-2x6 mal clipsé, ou câble PCIe branché en pont (daisy-chain).",
                        "PSU protection trip (OCP/OPP) from GPU transient spikes, unseated 12V-2x6 plug, or daisy-chained PCIe power cable.",
                        "Protección OCP/OPP de la fuente ante picos transitorios, conector 12V-2x6 mal insertado o cable PCIe en cadena.",
                        "Netzteil-Schutzschaltung (OCP/OPP) durch Lastspitzen, nicht eingerasteter 12V-2x6-Stecker oder Daisy-Chain-PCIe-Kabel.",
                        "Protezione OCP/OPP dell'alimentatore per picchi transitori GPU, connettore 12V-2x6 non inserito a fondo o cavo PCIe a ponte.",
                        "Proteção OCP/OPP da fonte por picos transitórios da GPU, conector 12V-2x6 mal encaixado ou cabo PCIe em daisy-chain."),
                    Solution = Localizer.L(
                        "Vérifiez l'enclenchement complet du câble GPU, utilisez des câbles PCIe dédiés côté alimentation et validez avec l'onglet « Stress Test ».",
                        "Check GPU power plug seating, use separate PSU cables per connector, and validate using the 'Stress Test' tab.",
                        "Verifique el conector de energía GPU, use cables separados desde la fuente y valide con 'Stress Test'.",
                        "GPU-Stromstecker fest einrasten, separate Netzteilkabel nutzen und im Reiter 'Stress Test' prüfen.",
                        "Controlla l'inserimento del cavo GPU, usa cavi separati dall'alimentatore e verifica con 'Stress Test'.",
                        "Verifique o encaixe do cabo da GPU, use cabos separados na fonte e valide na aba 'Stress Test'.")
                },
                new KnowledgeEntry
                {
                    Tag = "INTEL ARC • ReBAR",
                    BadgeColorHex = "#0EA5E9",
                    HardwareTarget = "INTEL_ARC",
                    Title = Localizer.L(
                        "Écrans noirs en veille & lourdes saccades sur GPU Intel Arc (Séries A & B)",
                        "Idle Black Screens & Severe Stutters on Intel Arc GPUs (A & B Series)",
                        "Pantallas negras en reposo y tirones en GPU Intel Arc (Series A y B)",
                        "Schwarze Bildschirme & Ruckler bei Intel Arc GPUs (A- & B-Serie)",
                        "Schermi neri a riposo e scatti su GPU Intel Arc (Serie A e B)",
                        "Telas pretas em repouso e engasgos em GPUs Intel Arc (Séries A e B)"),
                    Symptoms = Localizer.L(
                        "Écran noir au réveil du moniteur (igdkmdnd64.sys), performances divisées par deux ou crashs DirectX 12.",
                        "Black screen when waking displays (igdkmdnd64.sys), halved FPS, or DirectX 12 TDR timeouts.",
                        "Pantalla negra al despertar el monitor (igdkmdnd64.sys) o caídas graves de rendimiento en DX12.",
                        "Schwarzer Bildschirm beim Aufwachen (igdkmdnd64.sys) oder starke FPS-Einbrüche in DX12.",
                        "Schermo nero al risveglio del monitor (igdkmdnd64.sys) o forti cali di FPS in DX12.",
                        "Tela preta ao despertar o monitor (igdkmdnd64.sys) ou quedas severas de FPS em DX12."),
                    Cause = Localizer.L(
                        "L'architecture Intel Arc exige impérativement Resizable BAR actif, et supporte mal certains états profonds PCIe ASPM L1 sur cartes mères tierces.",
                        "Intel Arc hardware strictly requires Resizable BAR, and aggressive PCIe ASPM L1 substates cause wake lockups on many motherboards.",
                        "Intel Arc requiere obligatoriamente Resizable BAR activo y sufre bloqueos con estados profundos PCIe ASPM L1.",
                        "Intel Arc erfordert zwingend Resizable BAR und reagiert empfindlich auf tiefe PCIe-ASPM-L1-Energiesparzustände.",
                        "Intel Arc richiede obbligatoriamente Resizable BAR e soffre di blocchi con stati profondi PCIe ASPM L1.",
                        "A Intel Arc exige obrigatoriamente Resizable BAR ativo e sofre travamentos com estados PCIe ASPM L1."),
                    Solution = Localizer.L(
                        "Activez Above 4G Decoding + Resizable BAR dans le BIOS, désactivez PCIe ASPM + passez TdrDelay à 10s dans « Correctifs ».",
                        "Enable Above 4G Decoding + Resizable BAR in BIOS, and disable PCIe ASPM + set TdrDelay = 10s in 'Fixes'.",
                        "Active Resizable BAR en BIOS y desactive PCIe ASPM + ajuste TdrDelay a 10s en 'Correcciones'.",
                        "Resizable BAR im BIOS aktivieren und PCIe-ASPM deaktivieren + TdrDelay auf 10s setzen.",
                        "Attiva Resizable BAR nel BIOS e disattiva PCIe ASPM + imposta TdrDelay a 10s in 'Correzioni'.",
                        "Ative o Resizable BAR na BIOS e desative PCIe ASPM + ajuste TdrDelay para 10s em 'Correções'.")
                },
                new KnowledgeEntry
                {
                    Tag = "AM5 DDR5 • MCR",
                    BadgeColorHex = "#EAB308",
                    HardwareTarget = "AMD_CPU",
                    Title = Localizer.L(
                        "BSOD MEMORY_MANAGEMENT (0x1A) dès l'arrivée sur le bureau (AMD AM5 DDR5)",
                        "Login BSOD MEMORY_MANAGEMENT (0x1A) on AMD Ryzen AM5 DDR5",
                        "BSOD MEMORY_MANAGEMENT (0x1A) al iniciar sesión en AMD AM5 DDR5",
                        "Login-BSOD MEMORY_MANAGEMENT (0x1A) bei AMD Ryzen AM5 DDR5",
                        "BSOD MEMORY_MANAGEMENT (0x1A) all'avvio di Windows su AMD AM5 DDR5",
                        "BSOD MEMORY_MANAGEMENT (0x1A) ao iniciar o Windows em AMD AM5 DDR5"),
                    Symptoms = Localizer.L(
                        "Écran bleu 0x1A ou 0x0A dans les 60 secondes suivant un démarrage rapide sur Ryzen 7000 / 9000.",
                        "0x1A or 0x0A BSOD within 60 seconds of a fast boot on Ryzen 7000 / 9000 systems.",
                        "Pantalla azul 0x1A o 0x0A en los primeros 60 segundos tras arrancar en Ryzen 7000 / 9000.",
                        "Bluescreen 0x1A oder 0x0A innerhalb von 60 Sekunden nach einem Schnellstart auf Ryzen 7000 / 9000.",
                        "Schermata blu 0x1A o 0x0A entro 60 secondi dall'avvio rapido su Ryzen 7000 / 9000.",
                        "Tela azul 0x1A ou 0x0A nos primeiros 60 segundos após inicializar em Ryzen 7000 / 9000."),
                    Cause = Localizer.L(
                        "Option « Memory Context Restore » activée dans le BIOS alors que « Power Down Enable » est resté sur Disabled ou Auto.",
                        "'Memory Context Restore' is enabled in BIOS while 'Power Down Enable' is left Disabled or Auto, desynchronizing the DDR5 PHY.",
                        "'Memory Context Restore' activado en BIOS con 'Power Down Enable' desactivado.",
                        "'Memory Context Restore' im BIOS aktiviert, während 'Power Down Enable' deaktiviert ist.",
                        "'Memory Context Restore' attivo nel BIOS mentre 'Power Down Enable' è disattivato.",
                        "'Memory Context Restore' ativado na BIOS enquanto 'Power Down Enable' está desativado."),
                    Solution = Localizer.L(
                        "Dans le BIOS, mettez Memory Context Restore ET Power Down Enable tous les deux sur Enabled (ou tous les deux sur Disabled).",
                        "In BIOS, explicitly set BOTH Memory Context Restore AND Power Down Enable to Enabled (or both Disabled).",
                        "En BIOS, ponga TANTO Memory Context Restore COMO Power Down Enable en Enabled.",
                        "Im BIOS sowohl Memory Context Restore als auch Power Down Enable explizit auf Enabled stellen.",
                        "Nel BIOS imposta SIA Memory Context Restore CHE Power Down Enable su Enabled.",
                        "Na BIOS, defina AMBOS Memory Context Restore e Power Down Enable como Enabled.")
                },
                new KnowledgeEntry
                {
                    Tag = "LAN / WIFI • ANTI-CHEAT",
                    BadgeColorHex = "#14B8A6",
                    HardwareTarget = "ALL",
                    Title = Localizer.L(
                        "Freezes Réseau Realtek 2.5GbE / MediaTek RZ616 & Crashs Anti-Cheat (vgk.sys / EAC)",
                        "Realtek 2.5GbE / MediaTek RZ616 Freezes & Anti-Cheat BSODs (vgk.sys / EAC)",
                        "Bloqueos Realtek 2.5GbE / MediaTek RZ616 y BSOD Anti-Cheat (vgk.sys / EAC)",
                        "Realtek 2.5GbE / MediaTek RZ616 Freezes & Anti-Cheat-BSODs (vgk.sys / EAC)",
                        "Freeze Realtek 2.5GbE / MediaTek RZ616 e BSOD Anti-Cheat (vgk.sys / EAC)",
                        "Travamentos Realtek 2.5GbE / MediaTek RZ616 e BSOD Anti-Cheat (vgk.sys / EAC)"),
                    Symptoms = Localizer.L(
                        "Micro-freezes avec coupure Ethernet/Bluetooth (NDIS 10400 / mtkwl6ex.sys), ou BSOD 0x139 au lancement de Valorant / jeux EAC sous Win11 24H2.",
                        "Micro-freezes with Ethernet/Bluetooth dropouts (NDIS 10400 / mtkwl6ex.sys), or 0x139 BSOD launching Valorant / EAC games on Win11 24H2.",
                        "Microcortes Ethernet/Bluetooth (NDIS 10400 / mtkwl6ex.sys) o BSOD 0x139 al abrir Valorant / EAC en Win11 24H2.",
                        "Mikroruckler mit LAN-/Bluetooth-Ausfall oder BSOD 0x139 beim Start von Valorant / EAC unter Win11 24H2.",
                        "Micro-freeze con perdita LAN/Bluetooth o BSOD 0x139 avviando Valorant / EAC su Win11 24H2.",
                        "Micro-travamentos com queda de rede/Bluetooth ou BSOD 0x139 ao abrir Valorant / EAC no Win11 24H2."),
                    Cause = Localizer.L(
                        "Mode EEE / Green Ethernet instable sur Realtek 2.5GbE, veille PCIe sur MediaTek RZ616, ou conflit entre Anti-Cheat Ring-0 et Kernel Shadow Stacks / Auto HDR.",
                        "Buggy EEE / Green Ethernet on Realtek 2.5GbE, PCIe sleep on MediaTek RZ616, or Ring-0 Anti-Cheat colliding with Kernel Shadow Stacks / Auto HDR.",
                        "Modo EEE / Green Ethernet en Realtek 2.5GbE, suspensión en MediaTek RZ616 o conflicto Anti-Cheat con Kernel Shadow Stacks.",
                        "Fehlerhaftes EEE / Green Ethernet bei Realtek 2.5GbE oder Konflikt zwischen Ring-0-Anti-Cheat und Kernel-Stapelschutz.",
                        "Risparmio EEE / Green Ethernet su Realtek 2.5GbE o conflitto tra Anti-Cheat Ring-0 e Protezione stack del kernel.",
                        "Economia EEE / Green Ethernet na Realtek 2.5GbE ou conflito entre Anti-Cheat Ring-0 e Proteção de pilha do kernel."),
                    Solution = Localizer.L(
                        "Appliquez le correctif « Désactiver EEE / Veille Réseau 2.5GbE & Wi-Fi » + « Démarrage Rapide Off », et vérifiez l'onglet Santé & Conflits.",
                        "Apply the 'Disable 2.5GbE EEE & Wi-Fi Power-Save' + 'Fast Startup Off' fixes, and check the Health & Conflicts tab.",
                        "Aplique 'Desactivar ahorro EEE 2.5GbE y Wi-Fi' + 'Inicio rápido Off' en Correcciones.",
                        "Wenden Sie 'EEE & WLAN-Energiesparmodus deaktivieren' + 'Schnellstart Aus' unter Korrekturen an.",
                        "Applica 'Disattiva risparmio EEE 2.5GbE e Wi-Fi' + 'Avvio rapido Off' in Correzioni.",
                        "Aplique 'Desativar economia EEE 2.5GbE e Wi-Fi' + 'Inicialização rápida Off' em Correções.")
                },

                // ---------------- Detection v2 (2025–2026)
                new KnowledgeEntry
                {
                    Tag = "RGB • INPOUTX64",
                    BadgeColorHex = "#F59E0B",
                    HardwareTarget = "ALL",
                    Title = Localizer.L(
                        "Jeux qui gèlent, se ferment ou font redémarrer le PC (pilote inpoutx64 des logiciels RGB, 2026)",
                        "Games freezing, closing or rebooting the PC (inpoutx64 driver from RGB software, 2026)",
                        "Juegos que se congelan, se cierran o reinician el PC (controlador inpoutx64 del software RGB, 2026)",
                        "Spiele frieren ein, schließen sich oder starten den PC neu (inpoutx64-Treiber von RGB-Software, 2026)",
                        "Giochi che si bloccano, si chiudono o riavviano il PC (driver inpoutx64 del software RGB, 2026)",
                        "Jogos que travam, fecham ou reiniciam o PC (driver inpoutx64 de softwares RGB, 2026)"),
                    Symptoms = Localizer.L(
                        "Depuis la mise à jour d'août 2026 (KB5121003) : jeu figé, fermeture sans message ou erreur EXCEPTION_ACCESS_VIOLATION (0xc0000005), parfois redémarrage du PC — notamment ARC Raiders, The Finals ou MARVEL Tōkon.",
                        "Since the August 2026 update (KB5121003): frozen game, silent closure or EXCEPTION_ACCESS_VIOLATION (0xc0000005), sometimes a PC restart — notably ARC Raiders, The Finals or MARVEL Tōkon.",
                        "Desde la actualización de agosto de 2026 (KB5121003): juego congelado, cierre sin aviso o EXCEPTION_ACCESS_VIOLATION (0xc0000005), a veces reinicio del PC.",
                        "Seit dem Update vom August 2026 (KB5121003): eingefrorenes Spiel, Schließen ohne Meldung oder EXCEPTION_ACCESS_VIOLATION (0xc0000005), teils PC-Neustart.",
                        "Dall'aggiornamento di agosto 2026 (KB5121003): gioco bloccato, chiusura senza messaggio o EXCEPTION_ACCESS_VIOLATION (0xc0000005), a volte riavvio del PC.",
                        "Desde a atualização de agosto de 2026 (KB5121003): jogo travado, fechamento sem aviso ou EXCEPTION_ACCESS_VIOLATION (0xc0000005), às vezes reinício do PC."),
                    Cause = Localizer.L(
                        "Microsoft a confirmé que la cause est le pilote inpoutx64.sys installé par des logiciels RGB et de monitoring, pas la mise à jour elle-même. Les mises à jour de septembre 2026 le bloquent automatiquement, sauf sur les PC gérés par une entreprise.",
                        "Microsoft confirmed the cause is the inpoutx64.sys driver installed by RGB and monitoring software, not the update itself. The September 2026 updates block it automatically, except on company-managed PCs.",
                        "Microsoft confirmó que la causa es el controlador inpoutx64.sys del software RGB y de monitorización, no la actualización. Las actualizaciones de septiembre de 2026 lo bloquean salvo en PC gestionados.",
                        "Microsoft bestätigte als Ursache den Treiber inpoutx64.sys von RGB- und Monitoring-Software, nicht das Update. Die Updates vom September 2026 blockieren ihn automatisch, außer auf verwalteten PCs.",
                        "Microsoft ha confermato che la causa è il driver inpoutx64.sys del software RGB e di monitoraggio, non l'aggiornamento. Gli aggiornamenti di settembre 2026 lo bloccano, tranne sui PC gestiti.",
                        "A Microsoft confirmou que a causa é o driver inpoutx64.sys de softwares RGB e de monitoramento, não a atualização. As atualizações de setembro de 2026 o bloqueiam, exceto em PCs gerenciados."),
                    Solution = Localizer.L(
                        "Installe les dernières mises à jour Windows. Si le pilote est encore chargé, désactive-le dans « Correctifs » (Désactiver le pilote inpoutx64.sys) : c'est le contournement officiel. Le contrôle RGB peut cesser de fonctionner jusqu'à une mise à jour du logiciel.",
                        "Install the latest Windows updates. If the driver is still loaded, disable it under 'Fixes' (Disable the inpoutx64.sys driver): that is the official workaround. RGB control may stop working until the software is updated.",
                        "Instala las últimas actualizaciones. Si el controlador sigue cargado, desactívalo en «Correcciones»: es la solución oficial. El control RGB puede dejar de funcionar.",
                        "Neueste Windows-Updates installieren. Ist der Treiber noch geladen, unter „Korrekturen“ deaktivieren: offizielle Umgehung. Die RGB-Steuerung kann ausfallen.",
                        "Installa gli ultimi aggiornamenti. Se il driver è ancora caricato, disattivalo in «Correzioni»: è la soluzione ufficiale. Il controllo RGB può smettere di funzionare.",
                        "Instale as atualizações mais recentes. Se o driver ainda estiver carregado, desative-o em «Correções»: é a solução oficial. O controle RGB pode parar.")
                },
                new KnowledgeEntry
                {
                    Tag = "RTX 50 • BLACKWELL",
                    BadgeColorHex = "#76B900",
                    HardwareTarget = "NVIDIA_GPU",
                    Title = Localizer.L(
                        "RTX 50 : écrans noirs, pilotes retirés et connecteur 12V-2x6",
                        "RTX 50: black screens, pulled drivers and the 12V-2x6 connector",
                        "RTX 50: pantallas negras, controladores retirados y conector 12V-2x6",
                        "RTX 50: schwarze Bildschirme, zurückgezogene Treiber und 12V-2x6-Stecker",
                        "RTX 50: schermi neri, driver ritirati e connettore 12V-2x6",
                        "RTX 50: telas pretas, drivers retirados e conector 12V-2x6"),
                    Symptoms = Localizer.L(
                        "Écran noir avec ventilateurs qui s'emballent, salves d'événements nvlddmkm 153 puis écran bleu 0x116 (VIDEO_TDR_FAILURE), ou extinction nette en pleine charge.",
                        "Black screen with fans ramping up, bursts of nvlddmkm 153 events then a 0x116 blue screen (VIDEO_TDR_FAILURE), or a hard shutdown under load.",
                        "Pantalla negra con ventiladores a tope, ráfagas de eventos nvlddmkm 153 y luego pantalla azul 0x116, o apagado brusco bajo carga.",
                        "Schwarzer Bildschirm mit aufdrehenden Lüftern, Serien von nvlddmkm-153-Ereignissen, dann Bluescreen 0x116, oder hartes Abschalten unter Last.",
                        "Schermo nero con ventole al massimo, raffiche di eventi nvlddmkm 153 e poi schermata blu 0x116, o spegnimento netto sotto carico.",
                        "Tela preta com ventoinhas no máximo, rajadas de eventos nvlddmkm 153 e depois tela azul 0x116, ou desligamento brusco sob carga."),
                    Cause = Localizer.L(
                        "Trois pistes : (1) des versions de pilote défectueuses (la 595.59 a été retirée en février 2026 ; la 621.01 provoquait des gels vidéo corrigés par la 621.12) ; (2) le lien PCIe Gen5 sur certaines cartes mères ; (3) sur RTX 5090/5080, un connecteur 12V-2x6 mal enfiché ou un adaptateur, qui peut chauffer jusqu'à fondre.",
                        "Three leads: (1) faulty driver releases (595.59 was pulled in February 2026; 621.01 caused video freezes fixed by 621.12); (2) the PCIe Gen5 link on some motherboards; (3) on RTX 5090/5080, a badly seated 12V-2x6 connector or an adapter, which can heat up until it melts.",
                        "Tres pistas: (1) versiones de controlador defectuosas (595.59 retirada en febrero de 2026; 621.01 corregida por 621.12); (2) el enlace PCIe Gen5 en algunas placas; (3) en RTX 5090/5080, un conector 12V-2x6 mal insertado o un adaptador.",
                        "Drei Spuren: (1) fehlerhafte Treiberversionen (595.59 im Februar 2026 zurückgezogen; 621.01 durch 621.12 behoben); (2) die PCIe-Gen5-Verbindung mancher Mainboards; (3) bei RTX 5090/5080 ein schlecht sitzender 12V-2x6-Stecker oder Adapter.",
                        "Tre piste: (1) versioni di driver difettose (595.59 ritirata a febbraio 2026; 621.01 corretta dalla 621.12); (2) il collegamento PCIe Gen5 su alcune schede madri; (3) su RTX 5090/5080, un connettore 12V-2x6 inserito male o un adattatore.",
                        "Três pistas: (1) versões de driver com defeito (595.59 retirada em fevereiro de 2026; 621.01 corrigida pela 621.12); (2) o link PCIe Gen5 em algumas placas-mãe; (3) nas RTX 5090/5080, um conector 12V-2x6 mal encaixado ou um adaptador."),
                    Solution = Localizer.L(
                        "Vérifie la version du pilote dans « Matériel › Conflits » et installe un pilote récent (avec DDU en cas de doute). Si les écrans noirs persistent sur une carte mère PCIe 5.0, force le slot en Gen4 dans le BIOS (contournement répandu, pas officiel). PC éteint, vérifie que le câble 12V-2x6 est enfoncé à fond, sans adaptateur ni coude serré près de la carte.",
                        "Check the driver version under 'Hardware › Conflicts' and install a recent driver (with DDU if in doubt). If black screens persist on a PCIe 5.0 motherboard, force the slot to Gen4 in the BIOS (a common, unofficial workaround). With the PC off, make sure the 12V-2x6 cable is fully inserted, with no adapter and no tight bend near the card.",
                        "Revisa la versión en «Hardware › Conflictos» e instala un controlador reciente (con DDU si dudas). Si persisten las pantallas negras en una placa PCIe 5.0, fuerza Gen4 en la BIOS (solución extendida, no oficial). Con el PC apagado, comprueba que el cable 12V-2x6 esté bien insertado, sin adaptador.",
                        "Treiberversion unter „Hardware › Konflikte“ prüfen und aktuellen Treiber installieren (im Zweifel mit DDU). Bleiben schwarze Bildschirme auf PCIe-5.0-Boards, Slot im BIOS auf Gen4 stellen (verbreitete, inoffizielle Umgehung). Bei ausgeschaltetem PC prüfen, dass das 12V-2x6-Kabel ganz steckt, ohne Adapter.",
                        "Controlla la versione in «Hardware › Conflitti» e installa un driver recente (con DDU nel dubbio). Se gli schermi neri persistono su una scheda PCIe 5.0, forza Gen4 nel BIOS (soluzione diffusa, non ufficiale). A PC spento, verifica che il cavo 12V-2x6 sia inserito a fondo, senza adattatori.",
                        "Confira a versão em «Hardware › Conflitos» e instale um driver recente (com DDU na dúvida). Se as telas pretas persistirem numa placa PCIe 5.0, force Gen4 na BIOS (solução comum, não oficial). Com o PC desligado, confira se o cabo 12V-2x6 está totalmente encaixado, sem adaptador.")
                },
                new KnowledgeEntry
                {
                    Tag = "RYZEN 9000 • AM5",
                    BadgeColorHex = "#EF4444",
                    HardwareTarget = "AMD_CPU",
                    Title = Localizer.L(
                        "Ryzen 9000 / 9800X3D : instabilités, EXPO et BIOS de carte mère",
                        "Ryzen 9000 / 9800X3D: instability, EXPO and motherboard BIOS",
                        "Ryzen 9000 / 9800X3D: inestabilidad, EXPO y BIOS de la placa",
                        "Ryzen 9000 / 9800X3D: Instabilität, EXPO und Mainboard-BIOS",
                        "Ryzen 9000 / 9800X3D: instabilità, EXPO e BIOS della scheda madre",
                        "Ryzen 9000 / 9800X3D: instabilidade, EXPO e BIOS da placa-mãe"),
                    Symptoms = Localizer.L(
                        "PC qui ne démarre plus ou redémarre en boucle, écrans bleus mémoire (0x1A, 0x50), erreurs WHEA, ou erreurs de calcul pendant le test de stabilité.",
                        "PC that no longer boots or boot-loops, memory blue screens (0x1A, 0x50), WHEA errors, or miscalculations during the stability test.",
                        "PC que no arranca o se reinicia en bucle, pantallas azules de memoria (0x1A, 0x50), errores WHEA o fallos en la prueba de estabilidad.",
                        "PC startet nicht mehr oder in Schleife, Speicher-Bluescreens (0x1A, 0x50), WHEA-Fehler oder Rechenfehler im Stabilitätstest.",
                        "PC che non si avvia o si riavvia in loop, schermate blu di memoria (0x1A, 0x50), errori WHEA o errori nel test di stabilità.",
                        "PC que não liga ou reinicia em loop, telas azuis de memória (0x1A, 0x50), erros WHEA ou erros no teste de estabilidade."),
                    Cause = Localizer.L(
                        "Profils EXPO trop ambitieux pour le contrôleur mémoire, PBO ou Curve Optimizer trop agressifs et, sur certaines cartes mères (ASRock notamment), des BIOS qui dépassaient les limites électriques d'AMD : des processeurs ont été endommagés jusqu'en 2026.",
                        "EXPO profiles too ambitious for the memory controller, overly aggressive PBO or Curve Optimizer and, on some motherboards (ASRock in particular), BIOSes exceeding AMD's electrical limits: CPUs were damaged as late as 2026.",
                        "Perfiles EXPO demasiado ambiciosos, PBO o Curve Optimizer agresivos y, en algunas placas (sobre todo ASRock), BIOS que superaban los límites eléctricos de AMD: hubo CPU dañadas hasta 2026.",
                        "Zu ehrgeizige EXPO-Profile, aggressives PBO oder Curve Optimizer und auf manchen Boards (vor allem ASRock) BIOS-Versionen über AMDs elektrischen Grenzen: Noch 2026 wurden CPUs beschädigt.",
                        "Profili EXPO troppo ambiziosi, PBO o Curve Optimizer aggressivi e, su alcune schede (soprattutto ASRock), BIOS oltre i limiti elettrici di AMD: CPU danneggiate fino al 2026.",
                        "Perfis EXPO ambiciosos demais, PBO ou Curve Optimizer agressivos e, em algumas placas (sobretudo ASRock), BIOS acima dos limites elétricos da AMD: CPUs danificadas até 2026."),
                    Solution = Localizer.L(
                        "Mets à jour le BIOS de la carte mère. Teste d'abord avec EXPO désactivé, puis réactive-le ; si besoin, baisse sa fréquence d'un cran. Lance le test de stabilité « cœur par cœur » : il désigne les cœurs dont la valeur Curve Optimizer est trop négative.",
                        "Update the motherboard BIOS. Test with EXPO off first, then turn it back on; lower its frequency one step if needed. Run the 'core by core' stability test: it names the cores whose Curve Optimizer value is too negative.",
                        "Actualiza la BIOS. Prueba primero sin EXPO y luego actívalo; si hace falta, baja un paso la frecuencia. Lanza la prueba «núcleo a núcleo»: indica los núcleos con un Curve Optimizer demasiado negativo.",
                        "BIOS aktualisieren. Zuerst ohne EXPO testen, dann wieder einschalten; bei Bedarf Takt eine Stufe senken. Den Test „Kern für Kern“ starten: Er nennt die Kerne mit zu negativem Curve Optimizer.",
                        "Aggiorna il BIOS. Prova prima senza EXPO, poi riattivalo; se serve abbassa la frequenza di un passo. Avvia il test «core per core»: indica i core con un Curve Optimizer troppo negativo.",
                        "Atualize a BIOS. Teste primeiro sem EXPO e depois reative; se preciso, reduza um passo a frequência. Rode o teste «núcleo a núcleo»: ele aponta os núcleos com Curve Optimizer negativo demais.")
                },
                new KnowledgeEntry
                {
                    Tag = "DUMPS",
                    BadgeColorHex = "#6366F1",
                    HardwareTarget = "ALL",
                    Title = Localizer.L(
                        "Écran bleu sans minidump (Kernel-Power 41 seul, volmgr 161)",
                        "Blue screen with no minidump (Kernel-Power 41 only, volmgr 161)",
                        "Pantalla azul sin minivolcado (solo Kernel-Power 41, volmgr 161)",
                        "Bluescreen ohne Minidump (nur Kernel-Power 41, volmgr 161)",
                        "Schermata blu senza minidump (solo Kernel-Power 41, volmgr 161)",
                        "Tela azul sem minidump (só Kernel-Power 41, volmgr 161)"),
                    Symptoms = Localizer.L(
                        "Le PC redémarre ou affiche un écran bleu, mais aucun fichier n'apparaît dans C:\\Windows\\Minidump ; le journal ne montre qu'un Kernel-Power 41, parfois un volmgr 161.",
                        "The PC restarts or shows a blue screen, but no file appears in C:\\Windows\\Minidump; the log only shows a Kernel-Power 41, sometimes a volmgr 161.",
                        "El PC se reinicia o muestra pantalla azul, pero no aparece nada en C:\\Windows\\Minidump; el registro solo muestra un Kernel-Power 41, a veces un volmgr 161.",
                        "Der PC startet neu oder zeigt einen Bluescreen, aber in C:\\Windows\\Minidump erscheint nichts; im Protokoll nur Kernel-Power 41, manchmal volmgr 161.",
                        "Il PC si riavvia o mostra una schermata blu, ma in C:\\Windows\\Minidump non compare nulla; il registro mostra solo un Kernel-Power 41, a volte un volmgr 161.",
                        "O PC reinicia ou mostra tela azul, mas nada aparece em C:\\Windows\\Minidump; o log só mostra um Kernel-Power 41, às vezes um volmgr 161."),
                    Cause = Localizer.L(
                        "Windows écrit le dump via le fichier d'échange : s'il est désactivé, trop petit ou sur un autre disque, ou si l'écriture des dumps est coupée, rien n'est enregistré. Une coupure nette de l'alimentation (protection du bloc d'alimentation) ne laisse pas de dump non plus.",
                        "Windows writes the dump through the page file: if it is disabled, too small or on another drive, or if dump writing is turned off, nothing is saved. A hard power cut (power supply protection tripping) leaves no dump either.",
                        "Windows escribe el volcado a través del archivo de paginación: si está desactivado, es pequeño o está en otro disco, o si los volcados están desactivados, no se guarda nada. Un corte de alimentación tampoco deja volcado.",
                        "Windows schreibt den Dump über die Auslagerungsdatei: Ist sie aus, zu klein oder auf einem anderen Laufwerk, oder sind Dumps abgeschaltet, wird nichts gespeichert. Ein harter Stromausfall (Netzteilschutz) hinterlässt auch keinen Dump.",
                        "Windows scrive il dump tramite il file di paging: se è disattivato, troppo piccolo o su un altro disco, o se i dump sono disattivati, non viene salvato nulla. Anche un'interruzione netta dell'alimentazione non lascia dump.",
                        "O Windows grava o dump pelo arquivo de paginação: se estiver desativado, pequeno ou em outro disco, ou se os dumps estiverem desligados, nada é salvo. Um corte brusco de energia também não deixa dump."),
                    Solution = Localizer.L(
                        "Dans « Matériel › Conflits », regarde « Enregistrement des écrans bleus » et applique le correctif proposé (fichier d'échange automatique ou dumps réactivés). Si tout est en ordre et qu'aucun dump n'apparaît, pense à l'alimentation : une extinction sans écran bleu, c'est souvent sa protection qui se déclenche.",
                        "Under 'Hardware › Conflicts', check 'Blue screen recording' and apply the suggested fix (automatic page file or dumps turned back on). If everything is fine and still no dump appears, suspect the power supply: a shutdown without a blue screen is often its protection tripping.",
                        "En «Hardware › Conflictos», revisa «Registro de pantallas azules» y aplica la corrección propuesta. Si todo está bien y no aparece volcado, sospecha de la fuente de alimentación.",
                        "Unter „Hardware › Konflikte“ „Aufzeichnung von Bluescreens“ prüfen und die vorgeschlagene Korrektur anwenden. Ist alles in Ordnung und erscheint trotzdem kein Dump, das Netzteil verdächtigen.",
                        "In «Hardware › Conflitti» controlla «Registrazione delle schermate blu» e applica la correzione proposta. Se è tutto a posto e il dump non compare, sospetta l'alimentatore.",
                        "Em «Hardware › Conflitos», veja «Registro das telas azuis» e aplique a correção sugerida. Se estiver tudo certo e nenhum dump aparecer, desconfie da fonte.")
                }
            };
        }
    }
}
