using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace WinCrashDoctor.Optimize
{
    public enum OptGroup { Ads, Search, Ai, Privacy, Gaming }

    /// <summary>One registry value a setting writes. Default = what "Rétablir" puts back (null = delete the value).</summary>
    internal sealed class RegSet
    {
        public RegistryHive Hive;
        public string Sub, Name;
        public RegistryValueKind Kind = RegistryValueKind.DWord;
        public object Value;
        public object Default;
        /// <summary>What Windows behaves like when the value is absent (for the "already applied?" check).</summary>
        public object Missing;

        public string FullKey { get { return (Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + Sub; } }
        public string Display { get { return (Hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") + Sub + "  •  " + Name + " = " + Value; } }
    }

    internal sealed class Tweak
    {
        public string Id;
        public OptGroup Group;
        public Func<string> Title, Explanation;
        public bool Recommended;
        public RegSet[] Sets;
        public bool Admin { get { return Sets.Any(s => s.Hive == RegistryHive.LocalMachine); } }
    }

    /// <summary>
    /// Windows settings for ads, Bing, Copilot/Recall, privacy and gaming — documented settings and policies only,
    /// no services disabled. Every change goes through ChangeRecorder, so the journal can undo it exactly.
    /// </summary>
    public static class OptTweaks
    {
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private const string ExplorerAdv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        private static RegSet Cu(string sub, string name, object value, object def, object missing = null)
        {
            return new RegSet { Hive = RegistryHive.CurrentUser, Sub = sub, Name = name, Value = value, Default = def, Missing = missing ?? def, Kind = value is string ? RegistryValueKind.String : RegistryValueKind.DWord };
        }

        private static RegSet Lm(string sub, string name, object value)
        {
            return new RegSet { Hive = RegistryHive.LocalMachine, Sub = sub, Name = name, Value = value, Default = null, Missing = null };
        }

        private static RegSet CuPolicy(string sub, string name, object value)
        {
            return new RegSet { Hive = RegistryHive.CurrentUser, Sub = sub, Name = name, Value = value, Default = null, Missing = null };
        }

        internal static readonly Tweak[] All =
        {
            // ---------------- Ads & suggestions
            new Tweak
            {
                Id = "start_ads", Group = OptGroup.Ads, Recommended = true,
                Title = () => L("Retirer les suggestions et recommandations du menu Démarrer", "Remove suggestions and recommendations from Start", "Quitar sugerencias y recomendaciones del menú Inicio", "Vorschläge und Empfehlungen aus dem Startmenü entfernen", "Rimuovi suggerimenti e consigli dal menu Start", "Remover sugestões e recomendações do menu Iniciar"),
                Explanation = () => L("Supprime les apps « suggérées » et les recommandations d'astuces, de raccourcis et de nouvelles apps dans le menu Démarrer.", "Removes 'suggested' apps and the tips, shortcuts and new-app recommendations in Start.", "Elimina las apps sugeridas y las recomendaciones del menú Inicio.", "Entfernt „vorgeschlagene“ Apps und Tipps-/App-Empfehlungen im Startmenü.", "Rimuove le app suggerite e i consigli nel menu Start.", "Remove apps sugeridos e recomendações no menu Iniciar."),
                Sets = new[] { Cu(Cdm, "SystemPaneSuggestionsEnabled", 0, 1), Cu(Cdm, "SubscribedContent-338388Enabled", 0, 1), Cu(ExplorerAdv, "Start_IrisRecommendations", 0, 1) }
            },
            new Tweak
            {
                Id = "silent_apps", Group = OptGroup.Ads, Recommended = true,
                Title = () => L("Empêcher l'installation automatique d'apps sponsorisées", "Stop sponsored apps from installing themselves", "Impedir la instalación automática de apps patrocinadas", "Automatische Installation gesponserter Apps verhindern", "Impedisci l'installazione automatica di app sponsorizzate", "Impedir a instalação automática de apps patrocinados"),
                Explanation = () => L("Windows installe parfois tout seul des jeux et apps partenaires (Candy Crush, TikTok…) après une mise à jour. Ce réglage l'en empêche.", "Windows sometimes installs partner games and apps (Candy Crush, TikTok…) on its own after an update. This stops it.", "Windows a veces instala solo juegos y apps de socios tras una actualización. Esto lo impide.", "Windows installiert nach Updates teils selbst Partner-Spiele und -Apps. Das wird verhindert.", "Windows a volte installa da solo giochi e app partner dopo un aggiornamento. Questo lo impedisce.", "O Windows às vezes instala sozinho jogos e apps parceiros após uma atualização. Isto impede."),
                Sets = new[] { Cu(Cdm, "SilentInstalledAppsEnabled", 0, 1), Cu(Cdm, "OemPreInstalledAppsEnabled", 0, 1), Cu(Cdm, "PreInstalledAppsEnabled", 0, 1) }
            },
            new Tweak
            {
                Id = "tips", Group = OptGroup.Ads, Recommended = true,
                Title = () => L("Désactiver les astuces et les écrans « Terminez la configuration »", "Turn off tips and 'Finish setting up' screens", "Desactivar consejos y pantallas «Termina de configurar»", "Tipps und „Einrichtung abschließen“-Bildschirme ausschalten", "Disattiva suggerimenti e schermate «Completa la configurazione»", "Desativar dicas e telas «Conclua a configuração»"),
                Explanation = () => L("Plus de notifications d'astuces, ni d'écran d'accueil après les mises à jour qui pousse Microsoft 365, OneDrive ou Game Pass.", "No more tip notifications, and no post-update welcome screen pushing Microsoft 365, OneDrive or Game Pass.", "Sin notificaciones de consejos ni pantalla de bienvenida tras actualizar que promociona Microsoft 365 u OneDrive.", "Keine Tipp-Benachrichtigungen und kein Willkommensbildschirm nach Updates mit Werbung für Microsoft 365 oder OneDrive.", "Niente notifiche di suggerimenti né schermata di benvenuto dopo gli aggiornamenti che promuove Microsoft 365 o OneDrive.", "Sem notificações de dicas nem tela de boas-vindas após atualizações promovendo Microsoft 365 ou OneDrive."),
                Sets = new[] { Cu(Cdm, "SubscribedContent-338389Enabled", 0, 1), Cu(Cdm, "SoftLandingEnabled", 0, 1), Cu(Cdm, "SubscribedContent-310093Enabled", 0, 1),
                               Cu(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, 1) }
            },
            new Tweak
            {
                Id = "settings_ads", Group = OptGroup.Ads, Recommended = true,
                Title = () => L("Retirer le contenu suggéré dans Paramètres", "Remove suggested content in Settings", "Quitar el contenido sugerido en Configuración", "Vorgeschlagene Inhalte in den Einstellungen entfernen", "Rimuovi i contenuti suggeriti in Impostazioni", "Remover conteúdo sugerido em Configurações"),
                Explanation = () => L("Supprime les encarts promotionnels affichés dans l'application Paramètres.", "Removes the promotional panels shown in the Settings app.", "Elimina los recuadros promocionales de Configuración.", "Entfernt Werbeeinblendungen in der Einstellungen-App.", "Rimuove i riquadri promozionali in Impostazioni.", "Remove os quadros promocionais em Configurações."),
                Sets = new[] { Cu(Cdm, "SubscribedContent-338393Enabled", 0, 1), Cu(Cdm, "SubscribedContent-353694Enabled", 0, 1), Cu(Cdm, "SubscribedContent-353696Enabled", 0, 1) }
            },
            new Tweak
            {
                Id = "lockscreen_ads", Group = OptGroup.Ads, Recommended = false,
                Title = () => L("Retirer les « infos et astuces » de l'écran de verrouillage", "Remove 'fun facts and tips' from the lock screen", "Quitar «datos y consejos» de la pantalla de bloqueo", "„Fakten und Tipps“ vom Sperrbildschirm entfernen", "Rimuovi «curiosità e suggerimenti» dalla schermata di blocco", "Remover «curiosidades e dicas» da tela de bloqueio"),
                Explanation = () => L("Garde ton fond d'écran de verrouillage sans les textes et liens Microsoft par-dessus.", "Keeps your lock screen picture without Microsoft's text and links on top.", "Mantiene el fondo de bloqueo sin textos ni enlaces de Microsoft.", "Behält das Sperrbildschirmbild ohne Microsoft-Texte und -Links.", "Mantiene lo sfondo di blocco senza testi e link di Microsoft.", "Mantém o fundo da tela de bloqueio sem textos e links da Microsoft."),
                Sets = new[] { Cu(Cdm, "RotatingLockScreenOverlayEnabled", 0, 1), Cu(Cdm, "SubscribedContent-338387Enabled", 0, 1) }
            },
            new Tweak
            {
                Id = "explorer_ads", Group = OptGroup.Ads, Recommended = true,
                Title = () => L("Retirer les publicités OneDrive / Microsoft 365 de l'Explorateur", "Remove OneDrive / Microsoft 365 ads from File Explorer", "Quitar anuncios de OneDrive / Microsoft 365 del Explorador", "OneDrive-/Microsoft-365-Werbung aus dem Explorer entfernen", "Rimuovi le pubblicità OneDrive / Microsoft 365 da Esplora file", "Remover anúncios do OneDrive / Microsoft 365 do Explorador"),
                Explanation = () => L("Désactive les bandeaux de « notification du fournisseur de synchronisation » en haut de l'Explorateur.", "Turns off the 'sync provider notification' banners at the top of File Explorer.", "Desactiva los avisos del proveedor de sincronización en el Explorador.", "Schaltet die Hinweise des Synchronisierungsanbieters im Explorer aus.", "Disattiva i banner del provider di sincronizzazione in Esplora file.", "Desativa os avisos do provedor de sincronização no Explorador."),
                Sets = new[] { Cu(ExplorerAdv, "ShowSyncProviderNotifications", 0, 1) }
            },
            new Tweak
            {
                Id = "ad_id", Group = OptGroup.Ads, Recommended = true,
                Title = () => L("Désactiver l'identifiant publicitaire", "Turn off the advertising ID", "Desactivar el identificador de publicidad", "Werbe-ID ausschalten", "Disattiva l'ID pubblicità", "Desativar o ID de publicidade"),
                Explanation = () => L("Les applications ne peuvent plus utiliser cet identifiant pour personnaliser les publicités.", "Apps can no longer use this ID to personalize ads.", "Las apps ya no pueden usar este ID para personalizar anuncios.", "Apps können diese ID nicht mehr für personalisierte Werbung nutzen.", "Le app non possono più usare questo ID per personalizzare gli annunci.", "Os apps não podem mais usar este ID para personalizar anúncios."),
                Sets = new[] { Cu(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1) }
            },

            // ---------------- Search & widgets
            new Tweak
            {
                Id = "bing_search", Group = OptGroup.Search, Recommended = true,
                Title = () => L("Retirer les résultats Bing de la recherche du menu Démarrer", "Remove Bing results from Start search", "Quitar resultados de Bing de la búsqueda de Inicio", "Bing-Ergebnisse aus der Startmenü-Suche entfernen", "Rimuovi i risultati Bing dalla ricerca di Start", "Remover resultados do Bing da pesquisa do Iniciar"),
                Explanation = () => L("La recherche ne cherche plus que sur le PC : plus rapide, et rien de ce que tu tapes n'est envoyé à Bing. Prend effet après redémarrage de l'Explorateur.", "Search only looks on the PC: faster, and nothing you type is sent to Bing. Takes effect after File Explorer restarts.", "La búsqueda solo busca en el PC: más rápida y sin enviar nada a Bing.", "Die Suche durchsucht nur den PC: schneller, nichts geht an Bing.", "La ricerca cerca solo sul PC: più veloce e nulla viene inviato a Bing.", "A pesquisa busca só no PC: mais rápida e nada é enviado ao Bing."),
                Sets = new[] { CuPolicy(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1), CuPolicy(@"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0) }
            },
            new Tweak
            {
                Id = "widgets", Group = OptGroup.Search, Recommended = false,
                Title = () => L("Désactiver les Widgets (actualités et météo de la barre des tâches)", "Turn off Widgets (taskbar news and weather)", "Desactivar los Widgets (noticias y tiempo)", "Widgets ausschalten (News und Wetter in der Taskleiste)", "Disattiva i Widget (notizie e meteo)", "Desativar os Widgets (notícias e clima)"),
                Explanation = () => L("Retire le panneau Widgets et le processus qui l'alimente en arrière-plan (stratégie officielle « Autoriser les widgets »).", "Removes the Widgets board and the background process feeding it (official 'Allow widgets' policy).", "Quita el panel de Widgets y su proceso en segundo plano (directiva oficial).", "Entfernt das Widgets-Board und seinen Hintergrundprozess (offizielle Richtlinie).", "Rimuove il pannello Widget e il processo in background (criterio ufficiale).", "Remove o painel de Widgets e seu processo em segundo plano (política oficial)."),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0) }
            },

            // ---------------- Copilot & AI
            new Tweak
            {
                Id = "copilot", Group = OptGroup.Ai, Recommended = false,
                Title = () => L("Désactiver Copilot intégré à Windows", "Turn off Windows' built-in Copilot", "Desactivar Copilot integrado", "Integrierten Copilot ausschalten", "Disattiva Copilot integrato", "Desativar o Copilot integrado"),
                Explanation = () => L("Stratégie « Désactiver Windows Copilot ». L'application Copilot elle-même se retire dans l'onglet Applications.", "'Turn off Windows Copilot' policy. The Copilot app itself is removed under the Apps tab.", "Directiva «Desactivar Windows Copilot». La app se quita en Aplicaciones.", "Richtlinie „Windows Copilot deaktivieren“. Die App selbst unter Apps entfernen.", "Criterio «Disattiva Windows Copilot». L'app si rimuove in App.", "Política «Desativar o Windows Copilot». O app é removido em Aplicativos."),
                Sets = new[] { CuPolicy(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1) }
            },
            new Tweak
            {
                Id = "recall", Group = OptGroup.Ai, Recommended = false,
                Title = () => L("Désactiver Recall et l'analyse des captures (PC Copilot+)", "Turn off Recall and snapshot analysis (Copilot+ PCs)", "Desactivar Recall (PC Copilot+)", "Recall ausschalten (Copilot+-PCs)", "Disattiva Recall (PC Copilot+)", "Desativar o Recall (PCs Copilot+)"),
                Explanation = () => L("Sans effet sur un PC sans puce IA. Sur un PC Copilot+, empêche Recall d'enregistrer des captures de ton écran.", "No effect on a PC without an AI chip. On a Copilot+ PC, stops Recall from saving screenshots of your screen.", "Sin efecto en un PC sin chip de IA. En un PC Copilot+, impide que Recall guarde capturas.", "Ohne KI-Chip wirkungslos. Auf Copilot+-PCs speichert Recall keine Bildschirmaufnahmen mehr.", "Nessun effetto senza chip IA. Su PC Copilot+, impedisce a Recall di salvare catture.", "Sem efeito sem chip de IA. Em PCs Copilot+, impede o Recall de salvar capturas."),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1), Lm(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0) }
            },
            new Tweak
            {
                Id = "click_to_do", Group = OptGroup.Ai, Recommended = false,
                Title = () => L("Désactiver « Click to Do »", "Turn off Click to Do", "Desactivar Click to Do", "Click to Do ausschalten", "Disattiva Click to Do", "Desativar o Click to Do"),
                Explanation = () => L("Retire les actions IA proposées sur ce qui est affiché à l'écran (Windows + clic).", "Removes the AI actions offered on what is on screen (Windows + click).", "Quita las acciones de IA sobre lo que hay en pantalla.", "Entfernt KI-Aktionen zu Bildschirminhalten.", "Rimuove le azioni IA su ciò che è sullo schermo.", "Remove as ações de IA sobre o que está na tela."),
                Sets = new[] { CuPolicy(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1) }
            },

            // ---------------- Privacy
            new Tweak
            {
                Id = "telemetry", Group = OptGroup.Privacy, Recommended = false,
                Title = () => L("Télémétrie au minimum autorisé (données de diagnostic requises)", "Telemetry at the allowed minimum (required diagnostic data)", "Telemetría al mínimo permitido (datos obligatorios)", "Telemetrie auf das erlaubte Minimum (erforderliche Diagnosedaten)", "Telemetria al minimo consentito (dati obbligatori)", "Telemetria no mínimo permitido (dados obrigatórios)"),
                Explanation = () => L("Stratégie officielle « Autoriser les données de diagnostic » réglée sur « requises » : le niveau le plus bas possible sur Famille et Pro. Paramètres affichera « géré par votre organisation » pour ce réglage.", "Official 'Allow diagnostic data' policy set to 'required': the lowest level possible on Home and Pro. Settings will show 'managed by your organization' for it.", "Directiva oficial en «obligatorios»: el nivel más bajo en Home y Pro. Configuración mostrará «administrado por tu organización».", "Offizielle Richtlinie auf „erforderlich“: niedrigste Stufe bei Home/Pro. Einstellungen zeigt „von Ihrer Organisation verwaltet“.", "Criterio ufficiale su «obbligatori»: il livello minimo su Home e Pro. Impostazioni mostrerà «gestito dall'organizzazione».", "Política oficial em «obrigatórios»: o menor nível no Home e Pro. Configurações mostrará «gerenciado pela organização»."),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1) }
            },
            new Tweak
            {
                Id = "tailored", Group = OptGroup.Privacy, Recommended = true,
                Title = () => L("Désactiver les « expériences personnalisées »", "Turn off 'tailored experiences'", "Desactivar las «experiencias personalizadas»", "„Individuelle Benutzererfahrung“ ausschalten", "Disattiva le «esperienze personalizzate»", "Desativar as «experiências personalizadas»"),
                Explanation = () => L("Microsoft n'utilise plus tes données de diagnostic pour te proposer astuces et offres.", "Microsoft no longer uses your diagnostic data to offer you tips and deals.", "Microsoft ya no usa tus datos de diagnóstico para ofertas.", "Microsoft nutzt Diagnosedaten nicht mehr für Tipps und Angebote.", "Microsoft non usa più i dati di diagnostica per offerte.", "A Microsoft não usa mais seus dados de diagnóstico para ofertas."),
                Sets = new[] { Cu(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1) }
            },
            new Tweak
            {
                Id = "feedback", Group = OptGroup.Privacy, Recommended = true,
                Title = () => L("Ne plus demander de commentaires", "Stop feedback requests", "Dejar de pedir comentarios", "Keine Feedback-Anfragen mehr", "Non chiedere più feedback", "Parar de pedir comentários"),
                Explanation = () => L("Windows ne t'interrompt plus pour noter ton expérience.", "Windows stops interrupting you to rate your experience.", "Windows deja de pedirte que valores tu experiencia.", "Windows unterbricht nicht mehr für Bewertungen.", "Windows non ti interrompe più per valutare l'esperienza.", "O Windows para de interromper pedindo avaliações."),
                Sets = new[] { CuPolicy(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0) }
            },
            new Tweak
            {
                Id = "activity", Group = OptGroup.Privacy, Recommended = false,
                Title = () => L("Désactiver l'historique d'activités", "Turn off activity history", "Desactivar el historial de actividad", "Aktivitätsverlauf ausschalten", "Disattiva la cronologia attività", "Desativar o histórico de atividades"),
                Explanation = () => L("Windows n'enregistre plus la liste des applications, fichiers et sites que tu ouvres (stratégie officielle).", "Windows stops recording the apps, files and sites you open (official policy).", "Windows deja de registrar las apps, archivos y sitios que abres.", "Windows zeichnet geöffnete Apps, Dateien und Seiten nicht mehr auf.", "Windows non registra più app, file e siti aperti.", "O Windows para de registrar apps, arquivos e sites abertos."),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0), Lm(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0) }
            },

            // ---------------- Gaming
            new Tweak
            {
                Id = "game_mode", Group = OptGroup.Gaming, Recommended = true,
                Title = () => L("Laisser le mode Jeu activé", "Keep Game Mode on", "Mantener el Modo Juego activado", "Spielmodus eingeschaltet lassen", "Mantieni la Modalità Gioco attiva", "Manter o Modo de Jogo ativado"),
                Explanation = () => L("Le mode Jeu donne la priorité au jeu et suspend Windows Update pendant la partie. Activé par défaut ; certains guides le coupent à tort.", "Game Mode prioritizes the game and holds Windows Update while you play. On by default; some guides wrongly turn it off.", "El Modo Juego prioriza el juego y pausa Windows Update. Activado por defecto.", "Der Spielmodus priorisiert das Spiel und pausiert Windows Update. Standardmäßig an.", "La Modalità Gioco dà priorità al gioco e sospende Windows Update. Attiva di default.", "O Modo de Jogo prioriza o jogo e pausa o Windows Update. Ativado por padrão."),
                Sets = new[] { Cu(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, 1, 1) }
            },
            new Tweak
            {
                Id = "mouse_accel", Group = OptGroup.Gaming, Recommended = false,
                Title = () => L("Désactiver l'accélération de la souris (« Améliorer la précision »)", "Turn off mouse acceleration ('Enhance pointer precision')", "Desactivar la aceleración del ratón", "Mausbeschleunigung ausschalten („Zeigerbeschleunigung“)", "Disattiva l'accelerazione del mouse", "Desativar a aceleração do mouse"),
                Explanation = () => L("Le curseur bouge de la même distance quelle que soit la vitesse du geste : plus prévisible en jeu. Question de préférence, pas de performance.", "The cursor moves the same distance whatever the speed of the gesture: more predictable in games. A preference, not a performance gain.", "El cursor se mueve igual sea cual sea la velocidad: más predecible en juegos. Es preferencia, no rendimiento.", "Der Zeiger bewegt sich unabhängig vom Tempo gleich weit: berechenbarer im Spiel. Geschmackssache, kein Leistungsgewinn.", "Il cursore si muove uguale a prescindere dalla velocità: più prevedibile nei giochi. Preferenza, non prestazioni.", "O cursor se move igual qualquer que seja a velocidade: mais previsível em jogos. Preferência, não desempenho."),
                Sets = new[] { Cu(@"Control Panel\Mouse", "MouseSpeed", "0", "1"), Cu(@"Control Panel\Mouse", "MouseThreshold1", "0", "6"), Cu(@"Control Panel\Mouse", "MouseThreshold2", "0", "10") }
            }
        };

        public static string GroupName(OptGroup g)
        {
            switch (g)
            {
                case OptGroup.Ads: return L("Publicités et suggestions", "Ads and suggestions", "Anuncios y sugerencias", "Werbung und Vorschläge", "Pubblicità e suggerimenti", "Anúncios e sugestões");
                case OptGroup.Search: return L("Recherche et Widgets", "Search and Widgets", "Búsqueda y Widgets", "Suche und Widgets", "Ricerca e Widget", "Pesquisa e Widgets");
                case OptGroup.Ai: return L("Copilot et IA", "Copilot and AI", "Copilot e IA", "Copilot und KI", "Copilot e IA", "Copilot e IA");
                case OptGroup.Privacy: return L("Confidentialité", "Privacy", "Privacidad", "Datenschutz", "Privacy", "Privacidade");
                default: return L("Jeu", "Gaming", "Juegos", "Spielen", "Gioco", "Jogos");
            }
        }

        // ==================== State ====================
        private static object Read(RegSet s)
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(s.Hive, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(s.Sub))
                    return k != null ? k.GetValue(s.Name) : null;
            }
            catch { return null; }
        }

        private static bool Matches(RegSet s)
        {
            object v = Read(s) ?? s.Missing;
            return v != null && Convert.ToString(v) == Convert.ToString(s.Value);
        }

        internal static bool IsApplied(Tweak t) { return t.Sets.All(Matches); }

        /// <summary>Cards for the Optimiser page (same model as the Fixes page).</summary>
        public static List<RegistryFixItem> Items()
        {
            var list = new List<RegistryFixItem>();
            foreach (Tweak t in All)
            {
                bool applied = IsApplied(t);
                list.Add(new RegistryFixItem
                {
                    Id = Prefix + t.Id,
                    Group = FixGroup.SystemMaint,
                    VendorTag = t.Admin ? L("ADMIN", "ADMIN", "ADMIN", "ADMIN", "ADMIN", "ADMIN") : "",
                    HardwareTarget = "ALL",
                    Title = t.Title(),
                    Explanation = t.Explanation(),
                    RegPathDisplay = string.Join("\n", t.Sets.Select(s => s.Display)),
                    CurrentValue = applied ? L("appliqué", "applied", "aplicado", "angewendet", "applicato", "aplicado")
                                           : L("réglage par défaut de Windows", "Windows default", "valor predeterminado de Windows", "Windows-Standard", "predefinito di Windows", "padrão do Windows"),
                    ApplyBtn = L("Appliquer", "Apply", "Aplicar", "Anwenden", "Applica", "Aplicar"),
                    RestoreBtn = L("Rétablir", "Restore", "Restablecer", "Wiederherstellen", "Ripristina", "Restaurar"),
                    Status = applied ? FixStatus.Applied : (t.Recommended ? FixStatus.Recommended : FixStatus.Optional),
                    RequiresAdmin = t.Admin,
                    CanRestore = applied,
                    RegExportContent = ""
                });
            }
            return list;
        }

        public static OptGroup GroupOf(string itemId)
        {
            Tweak t = All.FirstOrDefault(x => Prefix + x.Id == itemId);
            return t != null ? t.Group : OptGroup.Ads;
        }

        // ==================== Operations ====================
        public const string Prefix = "opt_";

        private static Tweak FromOp(string op, out bool apply)
        {
            apply = op.EndsWith("_apply", StringComparison.Ordinal);
            if (!op.StartsWith(Prefix, StringComparison.Ordinal) || (!apply && !op.EndsWith("_restore", StringComparison.Ordinal))) return null;
            string id = op.Substring(Prefix.Length, op.Length - Prefix.Length - (apply ? 6 : 8));
            return All.FirstOrDefault(t => t.Id == id);
        }

        public static bool IsOp(string op) { bool a; return op != null && FromOp(op, out a) != null; }

        public static bool NeedsAdmin(string op) { bool a; Tweak t = FromOp(op, out a); return t != null && t.Admin; }

        public static string Title(string op) { bool a; Tweak t = FromOp(op, out a); return t != null ? t.Title() : null; }

        public static bool Execute(string op, out string messageFr, out string messageEn)
        {
            bool apply;
            Tweak t = FromOp(op, out apply);
            if (t == null) { messageFr = "Réglage inconnu."; messageEn = "Unknown setting."; return false; }
            foreach (RegSet s in t.Sets)
            {
                ChangeRecorder.Registry(s.Hive, s.Sub, s.Name);
                using (var bk = RegistryKey.OpenBaseKey(s.Hive, RegistryView.Registry64))
                {
                    object target = apply ? s.Value : s.Default;
                    if (target == null)
                    {
                        using (var k = bk.OpenSubKey(s.Sub, true))
                            if (k != null) k.DeleteValue(s.Name, false);
                    }
                    else
                    {
                        using (var k = bk.CreateSubKey(s.Sub))
                            k.SetValue(s.Name, target, s.Kind);
                    }
                }
            }
            messageFr = (apply ? "✓ Appliqué : " : "Rétabli : ") + t.Title();
            messageEn = (apply ? "✓ Applied: " : "Restored: ") + t.Title();
            return true;
        }

        /// <summary>Lets the journal undo changes to exactly the keys these settings write (and nothing else).</summary>
        public static bool IsTweakKey(string keyPath)
        {
            return keyPath != null && All.Any(t => t.Sets.Any(s => string.Equals(s.FullKey, keyPath, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
