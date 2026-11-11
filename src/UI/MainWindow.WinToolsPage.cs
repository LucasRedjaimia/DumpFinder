using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WinCrashDoctor
{
    public partial class MainWindow : Window
    {
        // ==================== DEDICATED WINDOWS DIAGNOSTIC TOOLS PAGE ====================
        private Grid _pageWinTools;
        private TextBlock _lblWinToolsMainTitle;
        private TextBlock _lblWinToolsMainSub;
        private Button _btnQuickSfcAdmin;

        private TextBlock[] _lblWinToolCatHeaders = new TextBlock[4];
        private TextBlock[] _lblWinToolItemTitles = new TextBlock[10];
        private TextBlock[] _lblWinToolItemDescs = new TextBlock[10];
        private Button[] _btnWinToolLaunch = new Button[10];

        private readonly string[] _toolExes = new[]
        {
            "perfmon.exe",   // 0: Reliability Monitor
            "eventvwr.msc",  // 1: Event Viewer
            "devmgmt.msc",   // 2: Device Manager
            "mdsched.exe",   // 3: Windows Memory Diagnostic
            "msinfo32.exe",  // 4: System Information
            "dxdiag.exe",    // 5: DirectX Diagnostic
            "resmon.exe",    // 6: Resource Monitor
            "diskmgmt.msc",  // 7: Disk Management
            "sfc_admin",     // 8: SFC & DISM cmd launcher
            "cleanmgr.exe"   // 9: Disk Cleanup
        };

        private readonly string[] _toolArgs = new[]
        {
            "/rel", "", "", "", "", "", "", "", "", "/lowdisk"
        };

        private void BuildWinToolsPage()
        {
            _pageWinTools = new Grid { Visibility = Visibility.Collapsed };
            _pageWinTools.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            _pageWinTools.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Cards Scroll

            // 1. Page Header with Title + Quick Admin Repair Button
            Grid hdr = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            _lblWinToolsMainTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblWinToolsMainSub = ResFg(new TextBlock { FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }, "TextSecondary");
            left.Children.Add(_lblWinToolsMainTitle);
            left.Children.Add(_lblWinToolsMainSub);
            Grid.SetColumn(left, 0);
            hdr.Children.Add(left);

            _btnQuickSfcAdmin = CreatePrimaryButton("🛡️ SFC /scannow (Admin)", "Accent", "OnAccent", new Thickness(14, 7, 14, 7), 12.2);
            _btnQuickSfcAdmin.Click += (s, e) => LaunchAdminRepairConsole();
            Grid.SetColumn(_btnQuickSfcAdmin, 1);
            hdr.Children.Add(_btnQuickSfcAdmin);

            Grid.SetRow(hdr, 0);
            _pageWinTools.Children.Add(hdr);

            // 2. Scrollable Body with 4 Categorized Cards
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel contentStack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };

            string[] catIcons = new[] { Glyph.Diagnostic, Glyph.Component, Glyph.Lightning, Glyph.Shield };
            int[][] catTools = new int[][]
            {
                new int[] { 0, 1 },       // Cat 0: Crashs & Fiabilité
                new int[] { 2, 3, 4, 5 }, // Cat 1: Matériel & Périphériques
                new int[] { 6, 7 },       // Cat 2: Surveillance & Disques
                new int[] { 8, 9 }        // Cat 3: Maintenance & Nettoyage
            };

            for (int c = 0; c < 4; c++)
            {
                int catIdx = c;
                Border card = ResBorder(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 14, 16, 14),
                    Margin = new Thickness(0, 0, 0, 12)
                }, "BgCard"), "BorderCard");

                StackPanel cardInner = new StackPanel();

                // Category Title Row
                StackPanel catTitleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                TextBlock catIcon = ResFg(Ds.Icon(catIcons[c], 15), "AccentText"); catIcon.Margin = new Thickness(0, 0, 9, 0); catTitleRow.Children.Add(catIcon);
                _lblWinToolCatHeaders[c] = ResFg(new TextBlock { FontSize = 13, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center }, "TextPrimary");
                catTitleRow.Children.Add(_lblWinToolCatHeaders[c]);
                cardInner.Children.Add(catTitleRow);

                // Grid of items in this category (2 columns)
                int[] toolsInThisCat = catTools[c];
                UniformGrid catGrid = new UniformGrid { Columns = 2 };

                for (int t = 0; t < toolsInThisCat.Length; t++)
                {
                    int toolIdx = toolsInThisCat[t];

                    Border itemBd = ResBorder(ResBg(new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(12, 10, 12, 10),
                        Margin = new Thickness((t % 2 == 0) ? 0 : 5, 0, (t % 2 == 0) ? 5 : 0, 8)
                    }, "BgSubtle"), "BorderCard");

                    Grid itemGrid = new Grid();
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    itemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    StackPanel itemText = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                    _lblWinToolItemTitles[toolIdx] = ResFg(new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold }, "TextPrimary");
                    _lblWinToolItemDescs[toolIdx] = ResFg(new TextBlock { FontSize = 11, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }, "TextSecondary");
                    itemText.Children.Add(_lblWinToolItemTitles[toolIdx]);
                    itemText.Children.Add(_lblWinToolItemDescs[toolIdx]);
                    Grid.SetColumn(itemText, 0);
                    itemGrid.Children.Add(itemText);

                    Button btnLaunch = CreateFlatButton("Ouvrir ↗", "BgCard", "AccentText", "BorderStrong", new Thickness(12, 6, 12, 6), 11.5);
                    btnLaunch.FontWeight = FontWeights.SemiBold;
                    btnLaunch.VerticalAlignment = VerticalAlignment.Center;
                    btnLaunch.Click += (s, e) => ExecuteWindowsTool(toolIdx);
                    _btnWinToolLaunch[toolIdx] = btnLaunch;
                    Grid.SetColumn(btnLaunch, 1);
                    itemGrid.Children.Add(btnLaunch);

                    itemBd.Child = itemGrid;
                    catGrid.Children.Add(itemBd);
                }

                cardInner.Children.Add(catGrid);
                card.Child = cardInner;
                contentStack.Children.Add(card);
            }

            sv.Content = contentStack;
            Grid.SetRow(sv, 1);
            _pageWinTools.Children.Add(sv);
        }

        private void ExecuteWindowsTool(int toolIdx)
        {
            if (toolIdx < 0 || toolIdx >= _toolExes.Length) return;

            string exe = _toolExes[toolIdx];
            string arg = _toolArgs[toolIdx];

            if (exe == "sfc_admin")
            {
                LaunchAdminRepairConsole();
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arg,
                    UseShellExecute = true
                };
                Process.Start(psi);
                SetToast(Localizer.L("Outil lancé : ", "Launched tool: ", "Herramienta abierta: ", "Tool gestartet: ", "Strumento avviato: ", "Ferramenta aberta: ") + exe + (string.IsNullOrEmpty(arg) ? "" : (" " + arg)), "AccentText");
            }
            catch (Exception ex)
            {
                SetToast("Error: " + ex.Message, "DangerFg");
            }
        }

        private void LaunchAdminRepairConsole()
        {
            bool started = Elevation.Launch("cmd.exe",
                "/k echo [Dump Finder] Lancement de la verification et reparation systeme... && echo. && sfc /scannow && echo. && echo [Dump Finder] Analyse du magasin de composants DISM... && DISM /Online /Cleanup-Image /RestoreHealth");
            if (started)
                SetToast(Localizer.L("Console de réparation SFC/DISM lancée en Administrateur.", "SFC/DISM repair console launched as Administrator.", "Consola de reparación SFC/DISM iniciada.", "SFC/DISM-Reparaturkonsole gestartet.", "Console di ripristino SFC/DISM avviata.", "Console de reparo SFC/DISM iniciada."), "SuccessFg");
            else
                SetToast(Localizer.L("Élévation annulée.", "Elevation cancelled.", "Elevación cancelada.", "Erhöhung abgebrochen.", "Elevazione annullata.", "Elevação cancelada."), "WarnFg");
        }

        private void RefreshWinToolsPageLabels()
        {
            if (_lblWinToolsMainTitle == null) return;

            _lblWinToolsMainTitle.Text = Localizer.L("Outils de Diagnostic Windows", "Windows Diagnostic Tools", "Herramientas de diagnóstico de Windows", "Windows-Diagnosewerkzeuge", "Strumenti di diagnostica Windows", "Ferramentas de diagnóstico do Windows");
            _lblWinToolsMainSub.Text = Localizer.L(
                "Passerelles directes vers les utilitaires avancés intégrés à Windows (sans console, lancement en 1 clic).",
                "Direct one-click access to advanced Windows built-in diagnostic and maintenance utilities.",
                "Acceso directo en 1 clic a las herramientas de diagnóstico integradas en Windows.",
                "Direkter 1-Klick-Zugriff auf integrierte Windows-Diagnose- und Wartungstools.",
                "Accesso diretto con 1 clic agli strumenti di diagnostica integrati di Windows.",
                "Acesso direto com 1 clique aos utilitários integrados de diagnóstico do Windows.");

            _btnQuickSfcAdmin.Content = Localizer.L("🛡️ Réparation SFC & DISM (Admin)", "🛡️ SFC & DISM Repair (Admin)", "🛡️ Reparación SFC y DISM (Admin)", "🛡️ SFC- & DISM-Reparatur (Admin)", "🛡️ Ripristino SFC e DISM (Admin)", "🛡️ Reparo SFC e DISM (Admin)");

            // Category Headers
            _lblWinToolCatHeaders[0].Text = Localizer.L("1. Analyse des Crashs & Historique de Stabilité", "1. Crash Logs & Reliability History", "1. Registros de fallos e historial de fiabilidad", "1. Absturzberichte & Zuverlässigkeitsverlauf", "1. Log di crash e cronologia affidabilità", "1. Logs de falhas e histórico de confiabilidade");
            _lblWinToolCatHeaders[1].Text = Localizer.L("2. Diagnostic Matériel, BIOS & Pilotes", "2. Hardware Diagnostics, BIOS & Drivers", "2. Diagnóstico de hardware, BIOS y controladores", "2. Hardware-Diagnose, BIOS & Treiber", "2. Diagnostica hardware, BIOS e driver", "2. Diagnóstico de hardware, BIOS e drivers");
            _lblWinToolCatHeaders[2].Text = Localizer.L("3. Surveillance des Ressources & Stockage", "3. Real-Time Resources & Storage Management", "3. Supervisión de recursos y almacenamiento", "3. Echtzeit-Ressourcen & Speicherverwaltung", "3. Monitoraggio risorse e archiviazione", "3. Monitoramento de recursos e armazenamento");
            _lblWinToolCatHeaders[3].Text = Localizer.L("4. Intégrité Système & Nettoyage", "4. System Integrity & Disk Cleanup", "4. Integridad del sistema y limpieza", "4. Systemintegrität & Bereinigung", "4. Integrità del sistema e pulizia", "4. Integridade do sistema e limpeza");

            // Tool Items
            // 0: Reliability Monitor
            _lblWinToolItemTitles[0].Text = Localizer.L("Moniteur de fiabilité (perfmon /rel)", "Reliability Monitor (perfmon /rel)", "Monitor de fiabilidad (perfmon /rel)", "Zuverlässigkeitsverlauf (perfmon /rel)", "Monitoraggio affidabilità (perfmon /rel)", "Monitor de Confiabilidade (perfmon /rel)");
            _lblWinToolItemDescs[0].Text = Localizer.L("Courbe visuelle jour par jour de la stabilité de Windows, indice de 1 à 10 et historique de chaque plantage.", "Visual daily stability curve, 1–10 reliability index, and individual crash timeline.", "Gráfico cronológico de estabilidad, índice de 1 a 10 y detalle de fallos.", "Visueller Zuverlässigkeitsverlauf mit Index von 1 bis 10 und Absturzhistorie.", "Grafico giornaliero di stabilità, indice da 1 a 10 e cronologia dei crash.", "Gráfico diário de estabilidade, índice de 1 a 10 e histórico de falhas.");

            // 1: Event Viewer
            _lblWinToolItemTitles[1].Text = Localizer.L("Observateur d'événements (eventvwr)", "Event Viewer (eventvwr.msc)", "Visor de eventos (eventvwr.msc)", "Ereignisanzeige (eventvwr.msc)", "Visualizzatore eventi (eventvwr.msc)", "Visualizador de Eventos (eventvwr.msc)");
            _lblWinToolItemDescs[1].Text = Localizer.L("Consultation brute des journaux d'erreurs Système, Application, WHEA et BugCheck.", "Detailed query of Windows System, Application, WHEA hardware, and BugCheck logs.", "Consulta detallada de registros de Sistema, Aplicación, WHEA y BugCheck.", "Detaillierte Einsicht in System-, Anwendungs-, WHEA- und BugCheck-Protokolle.", "Consultazione dettagliata dei registri di Sistema, Applicazione, WHEA e BugCheck.", "Consulta detalhada dos logs do Sistema, Aplicativos, WHEA e BugCheck.");

            // 2: Device Manager
            _lblWinToolItemTitles[2].Text = Localizer.L("Gestionnaire de périphériques (devmgmt)", "Device Manager (devmgmt.msc)", "Administrador de dispositivos (devmgmt)", "Geräte-Manager (devmgmt.msc)", "Gestione dispositivi (devmgmt.msc)", "Gerenciador de Dispositivos (devmgmt)");
            _lblWinToolItemDescs[2].Text = Localizer.L("Détection des périphériques en anomalie (Code 43 GPU, conflits de pilotes, points d'exclamation jaunes).", "Inspect hardware devices in error states (Code 43 GPU, driver conflicts, yellow triangles).", "Inspección de dispositivos con problemas (Code 43 GPU, controladores en conflicto).", "Erkennung von Problemgeräten (GPU-Code 43, Treiberkonflikte, gelbe Dreiecke).", "Rilevamento dispositivi in anomalia (Code 43 GPU, conflitti driver, triangoli gialli).", "Detecção de dispositivos com problemas (Código 43 na GPU, conflitos de drivers).");

            // 3: Windows Memory Diagnostic
            _lblWinToolItemTitles[3].Text = Localizer.L("Diagnostic de mémoire Windows (mdsched)", "Windows Memory Diagnostic (mdsched)", "Diagnóstico de memoria de Windows (mdsched)", "Windows-Speicherdiagnose (mdsched)", "Diagnostica memoria Windows (mdsched)", "Diagnóstico de Memória do Windows (mdsched)");
            _lblWinToolItemDescs[3].Text = Localizer.L("Planifie un test matériel approfondi des barrettes RAM au prochain redémarrage.", "Schedules a low-level bare-metal RAM test during the next PC reboot.", "Programa una prueba profunda de memoria RAM en el próximo reinicio.", "Plant einen Tiefentest der RAM-Riegel beim nächsten PC-Neustart.", "Pianifica un test hardware della RAM al prossimo riavvio del PC.", "Agenda um teste profundo da memória RAM na próxima reinicialização.");

            // 4: System Information
            _lblWinToolItemTitles[4].Text = Localizer.L("Informations système complètes (msinfo32)", "System Information (msinfo32)", "Información del sistema (msinfo32)", "Systeminformationen (msinfo32)", "Informazioni di sistema (msinfo32)", "Informações do Sistema (msinfo32)");
            _lblWinToolItemDescs[4].Text = Localizer.L("Fiche technique intégrale : révision BIOS, carte mère, canaux DMA, interruptions IRQ et pilotes noyau.", "Full hardware brief: BIOS revision, motherboard model, DMA channels, and active kernel drivers.", "Informe técnico completo: versión de BIOS, placa base, canales DMA y controladores.", "Vollständiger technischer Bericht: BIOS-Version, Mainboard, DMA-Kanäle und Treiber.", "Scheda tecnica completa: versione BIOS, scheda madre, canali DMA e driver di sistema.", "Ficha técnica completa: versão da BIOS, placa-mãe, canais DMA e drivers de sistema.");

            // 5: DirectX Diagnostic
            _lblWinToolItemTitles[5].Text = Localizer.L("Outil de diagnostic DirectX (dxdiag)", "DirectX Diagnostic Tool (dxdiag)", "Herramienta de diagnóstico DirectX (dxdiag)", "DirectX-Diagnoseprogramm (dxdiag)", "Strumento di diagnostica DirectX (dxdiag)", "Ferramenta de Diagnóstico DirectX (dxdiag)");
            _lblWinToolItemDescs[5].Text = Localizer.L("Contrôle du niveau de fonctionnalités Direct3D, pilote WDDM, DDI et accélération graphique.", "Inspect Direct3D feature levels, WDDM driver version, display DDI, and hardware acceleration.", "Comprobación de niveles de función Direct3D, controlador WDDM y aceleración gráfica.", "Prüfung von Direct3D-Funktionsebenen, WDDM-Treiberversion und Grafikbeschleunigung.", "Controllo dei livelli di funzionalità Direct3D, driver WDDM e accelerazione grafica.", "Verificação de níveis de recursos Direct3D, driver WDDM e aceleração gráfica.");

            // 6: Resource Monitor
            _lblWinToolItemTitles[6].Text = Localizer.L("Moniteur de ressources (resmon)", "Resource Monitor (resmon.exe)", "Monitor de recursos (resmon.exe)", "Ressourcenmonitor (resmon.exe)", "Monitoraggio risorse (resmon.exe)", "Monitor de Recursos (resmon.exe)");
            _lblWinToolItemDescs[6].Text = Localizer.L("Surveillance en temps réel des threads CPU bloqués, files d'attente disque et ports réseau ouverts.", "Real-time inspection of hung CPU threads, memory allocations, disk queues, and open ports.", "Supervisión en tiempo real de hilos CPU bloqueados, memoria, colas de disco y red.", "Echtzeit-Überwachung blockierter CPU-Threads, Speicher, Datenträger- und Netzwerk-Warteschlangen.", "Monitoraggio in tempo reale di thread CPU bloccati, memoria, code disco e rete.", "Monitoramento em tempo real de threads da CPU travadas, memória, filas de disco e rede.");

            // 7: Disk Management
            _lblWinToolItemTitles[7].Text = Localizer.L("Gestion des disques (diskmgmt)", "Disk Management (diskmgmt.msc)", "Administración de discos (diskmgmt)", "Datenträgerverwaltung (diskmgmt.msc)", "Gestione disco (diskmgmt.msc)", "Gerenciamento de Disco (diskmgmt)");
            _lblWinToolItemDescs[7].Text = Localizer.L("Gestion des partitions GPT/MBR, lettres de lecteurs et détection des volumes NVMe/SATA.", "Partition layout (GPT/MBR), drive letters, unallocated space, and NVMe/SATA health.", "Gestión de particiones GPT/MBR, letras de unidad y volúmenes NVMe/SATA.", "Partitionen verwalten (GPT/MBR), Laufwerksbuchstaben und NVMe/SATA-Volumes prüfen.", "Gestione partizioni GPT/MBR, lettere di unità e volumi NVMe/SATA.", "Gerenciamento de partições GPT/MBR, letras de unidade e volumes NVMe/SATA.");

            // 8: SFC & DISM Console
            _lblWinToolItemTitles[8].Text = Localizer.L("Console de réparation SFC / DISM", "SFC / DISM Integrity Console", "Consola de reparación SFC / DISM", "SFC / DISM Systemreparatur-Konsole", "Console di ripristino SFC / DISM", "Console de Reparo SFC / DISM");
            _lblWinToolItemDescs[8].Text = Localizer.L("Ouvre une invite de commandes Administrateur pour réparer les DLL et fichiers système Windows corrompus.", "Opens an elevated console executing 'sfc /scannow' and DISM image repair.", "Abre una consola Administrador para reparar archivos del sistema dañados.", "Öffnet eine Admin-Konsole für 'sfc /scannow' und DISM-Systemreparatur.", "Apre una console Amministratore per riparare file di sistema corrotti.", "Abre um console Administrador para reparar arquivos do sistema corrompidos.");

            // 9: Disk Cleanup
            _lblWinToolItemTitles[9].Text = Localizer.L("Nettoyage de disque Windows (cleanmgr)", "Disk Cleanup (cleanmgr.exe)", "Liberador de espacio en disco (cleanmgr)", "Datenträgerbereinigung (cleanmgr.exe)", "Pulizia disco (cleanmgr.exe)", "Limpeza de Disco (cleanmgr.exe)");
            _lblWinToolItemDescs[9].Text = Localizer.L("Purge les anciens rapports de crash, caches temporaires de mise à jour et résidus d'installation.", "Purges old memory crash dumps, temporary update caches, and residual installation files.", "Elimina antiguos volcados de memoria, cachés temporales y archivos residuales.", "Löscht alte Speicherabbilder, temporäre Update-Caches und Installationsrückstände.", "Elimina vecchi dump di memoria, cache temporanee e file residui.", "Limpa antigos dumps de memória, caches temporários e arquivos residuais.");

            string openBtnText = Localizer.L("Ouvrir ↗", "Launch ↗", "Abrir ↗", "Öffnen ↗", "Apri ↗", "Abrir ↗");
            for (int i = 0; i < 10; i++)
            {
                if (_btnWinToolLaunch[i] != null)
                    _btnWinToolLaunch[i].Content = openBtnText;
            }
        }
    }
}
