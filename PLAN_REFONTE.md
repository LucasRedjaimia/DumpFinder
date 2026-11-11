# Dump Finder 6.0 — plan de refonte

> À valider avant de commencer. Les décisions à prendre sont rassemblées dans la [section 7](#7-décisions-à-valider).
> Supports visuels :
> - `branding/logo-concepts.html` : pistes de logo et palette
> - `design/home-mockup.html` : maquette du nouvel écran d'accueil

---

## 1. État des lieux (v5.2)

**Ce qui marche bien, et qu'on garde**
- Un exe unique d'environ 700 Ko, sans installation : WPF .NET Framework 4, compilé par `csc` dans `build.ps1`.
- Une vraie valeur métier :
  - lecture des journaux Windows (BSOD, TDR, WHEA, NVMe, Kernel-Power…) ;
  - 17 correctifs ciblés et filtrés selon le matériel ;
  - stress test réel (OpenCL, CPU, RAM, NVMe) ;
  - sentinelle en arrière-plan, serveur MCP, conseiller IA, 6 langues.
- Un thème clair/sombre déjà en place, avec une base de tokens de couleur.

**Ce qui pèche**

| Domaine | Constat |
|---|---|
| **UI** | Tout est construit en C#, sans aucun XAML. On a 2 664 lignes dans `MainWindow.cs` et aucun style partagé. Les tailles de police sont arbitraires (12.2, 12.3, 12.5, 11.2…). Les icônes sont des caractères Unicode (◉ ❖ ✦). La palette est un bleu Tailwind générique, d'où l'impression « template ». |
| **UX** | 7 onglets au même niveau. Les réglages (langue, thème, sentinelle) encombrent la sidebar. La visite guidée fait 23 étapes. Le feedback passe par une ligne de texte en pied de page. Pas d'écran de chargement ni d'état vide soigné. |
| **Architecture de l'info** | Un crash est éclaté entre « Diagnostic » (l'événement) et « Santé › Minidumps » (le fichier .dmp), sans lien entre les deux. L'IA est une page à part au lieu d'être disponible sur chaque crash. |
| **Analyse des dumps** | Ce n'est pas un vrai parseur. Pour les dumps noyau, seul le code de bugcheck est lu. Le « driver coupable » est le premier `.sys` non-Microsoft trouvé dans les octets. Aucune pile n'est reconstruite, alors que l'UI affiche « Modules détectés dans la pile ». `DecodeBugCheck` ne connaît que 11 codes. |
| **Réactivité** | Plusieurs appels bloquent l'UI : WMI au démarrage, application de correctifs avec `WaitForExit(15000)` sur l'UAC, ouverture de .dmp, changement de langue. |
| **Fiabilité** | 67 `catch { }` vides. « Appliquer tout » déclenche un UAC par correctif et ignore les résultats. Le snapshot de restauration ne couvre pas Chrome/Edge, Discord, powercfg ni la carte réseau. |
| **Données** | Aucun historique : les scans, les tests de stress et les alertes de la sentinelle sont perdus à la fermeture. L'export se limite à du texte brut. |
| **Sécurité** | La clé API IA est stockée en clair dans `settings.json`. L'outil MCP `wincrash_apply_fix` modifie le Registre sans confirmation. |
| **Hygiène** | Pas de dépôt git. 5 anciens exe et zip traînent à la racine. Code mort (`GeneratePlainReport`) et logique dupliquée (relance admin ×5, sous-onglets ×4). |

---

## 2. Identité visuelle

### Logo
L'actuel est une **poubelle** : il comprend « dump » comme *décharge*, alors que l'appli parle de *memory dump*. J'ai préparé 3 pistes, visibles dans `branding/logo-concepts.html` :

- **A · Stack Fault** (recommandé) : trois blocs mémoire empilés, celui du milieu est déchiré et décalé en corail. Il signifie « mémoire + crash », reste lisible à 16 px et ne ressemble à aucune icône existante.
- **B · Hex Lens** : un fichier .dmp et une loupe qui isole la ligne fautive. Plus littéral, mais chargé en petite taille.
- **C · Pulse Chip** : une puce traversée par un électrocardiogramme. Plus « docteur » que « dump ».

✅ **Validé : piste A · Stack Fault.** Livrables déjà produits dans `branding/` :
- `logo.svg` : le SVG maître, utilisé à partir de 32 px ;
- `logo-small.svg` : une variante optique pour 24 px et moins (tuile pleine, barres épaisses, sans éclat ni dentelure) ;
- `logo-mono.svg` : un glyphe monochrome en `currentColor`, pour la zone de notification et les overlays ;
- `make-icon.ps1` : rend le logo avec WPF, sans dépendance, et écrit `out/dumpfinder.ico` (10 tailles de 16 à 256 px, environ 40 Ko) ainsi que les PNG de 16 à 512 px.

En phase 1, `make-icon.ps1` remplacera le dessin GDI+ de la poubelle dans `build.ps1`.

### Palette et typographie
- **Ink** `#0B0B12`, **Surface** `#13121C`.
- **Brand indigo** `#7C68FA` : remplace le bleu générique.
- **Fault** corail `#FF5E62 → #FFA24A` : réservé aux crashs.
- **Warning** `#F5B544`, **Healthy** `#3DD68C`.
- Police : **Segoe UI Variable** (native Windows 11, axes Display et Text), **Cascadia Mono** pour les codes et l'hexa.
- Échelle typographique fixe de 7 tailles : 11 / 12 / 13 / 15 / 18 / 24 / 28 (titres de page en 24, Segoe UI Variable Display).
- Espacements sur une grille de 4 px. 3 rayons d'angle : 6, 10, 16.
- Icônes : la police **Segoe Fluent Icons**, livrée avec Windows 11 (aucune dépendance). Elle remplace les glyphes Unicode.

---

## 3. Refonte UX/UI

### 3.1 Fenêtre et coque
- Barre de titre personnalisée avec fond **Mica** (DWM, Windows 11), repli sur un aplat sous Windows 10. Le titre et la recherche y sont intégrés.
- Sidebar compacte et repliable à 64 px (icônes seules).
- La langue, le thème et la sentinelle partent dans **Paramètres**. En bas de la sidebar ne restent qu'une puce matériel (CPU et GPU) et la roue des réglages.
- Transitions de page discrètes : fondu et glissement de 8 px, 160 ms.
- **Toasts** empilés en bas à droite, avec action « Annuler » quand c'est pertinent. Ils remplacent le pied de page.

### 3.2 Nouvelle architecture : 5 sections au lieu de 7

| Nouvelle section | Reprend | Changement clé |
|---|---|---|
| **Accueil** | Diagnostic | Score de stabilité 0–100, verdict en une phrase, frise des crashs sur 30 jours, 3 actions recommandées en 1 clic. |
| **Crashs** | Diagnostic (liste) + Santé › Minidumps + Guide | Un crash = l'événement, son .dmp associé, l'explication et les correctifs liés, réunis dans un **panneau de détail** latéral. Bouton « Demander à l'IA » sur chaque crash. |
| **Matériel** | Santé & Conflits | Cartes BIOS, RAM, PCIe, SMART et conflits avec un état visuel. Le stress test devient un sous-onglet « Tester la stabilité ». |
| **Correctifs** | Correctifs | Aperçu avant/après de chaque valeur, niveau de risque, badge « réversible », journal des modifications avec annulation par élément. |
| **Outils** | Outils Windows + Rapport | Lanceurs Windows, export de rapport, intégrations (MCP). |
| *Paramètres* (roue) | Sidebar + IA | Langue, thème, sentinelle, fournisseur IA, intégrations MCP. |

### 3.3 Composants (design system XAML)
Une bibliothèque `ResourceDictionary` partagée remplace les `new Border { … }` répétés partout :
- boutons : Primary, Secondary, Ghost, Danger ;
- `Card`, `StatTile`, `Badge` et `Chip` (par état), `SegmentedControl` (remplace les 4 implémentations de sous-onglets) ;
- `Drawer` (panneau de détail), `Dialog`, `Toast` ;
- `EmptyState` (illustrations simples), `Skeleton` (chargement), `ProgressRing` ;
- `ScoreRing` pour le score, `Sparkline` et `TimelineChart` (Canvas maison, sans dépendance).

### 3.4 Onboarding
- On remplace la visite de 23 étapes par un **accueil en 3 écrans** : ce que fait l'appli, l'analyse qui se lance, le résultat.
- Ensuite, des **astuces contextuelles** apparaissent une seule fois, la première fois qu'on ouvre une section.
- Les **états vides** expliquent eux-mêmes ce qu'il faut faire.

### 3.5 Accessibilité et confort
- Navigation complète au clavier, anneau de focus visible, `AutomationProperties` pour les lecteurs d'écran.
- Contraste AA vérifié dans les deux thèmes, respect du paramètre Windows « réduire les animations ».
- Raccourcis : `Ctrl+R` relance l'analyse, `Ctrl+K` ouvre une palette de commandes (rechercher un crash, un code ou un correctif), `Ctrl+E` exporte.

---

## 4. Fonctionnalités

### P1 — forte valeur
1. **Vraie analyse des minidumps**
   - Lecture complète de l'en-tête des dumps noyau (code et **4 paramètres** du bugcheck) et de la liste des drivers chargés quand elle est présente.
   - **Si WinDbg ou `cdb.exe` est installé** : `!analyze -v` est lancé automatiquement et on en extrait `MODULE_NAME`, `IMAGE_NAME`, `FAILURE_BUCKET_ID` et la pile. Sinon l'UI propose de l'installer via winget.
   - L'étiquette trompeuse « pile » est corrigée.
2. **Catalogue complet des bugchecks** : environ 250 codes, avec nom, explication en langage courant, causes fréquentes et correctifs liés (au lieu de 11).
3. **Score et frise de stabilité** : basés sur `Win32_ReliabilityStabilityMetrics`, l'indice de fiabilité de Windows lu via WMI, plus nos propres événements.
4. **Historique local** : chaque scan, crash, test de stress et alerte est enregistré en JSON Lines dans `%LOCALAPPDATA%\DumpFinder\history`. Permet les tendances et le « avant/après correctif ».
5. **Correctifs fiabilisés**
   - **Un seul UAC** pour un lot : un processus élevé exécute la liste et renvoie le résultat de chaque correctif.
   - Snapshot **avant chaque correctif**, toutes catégories confondues : Registre, flags navigateur, Discord, powercfg, carte réseau.
   - Journal des changements avec annulation ciblée.
6. **Rapport exportable**
   - un **HTML autonome** soigné, à partager ou joindre à un ticket de support ;
   - un **JSON** pour l'outillage ;
   - un **Markdown « pour forum »** prêt à coller sur Reddit, LTT ou les forums NVIDIA/AMD.
7. **Inventaire des pilotes** : drivers tiers avec version, date et éditeur, signalement des pilotes anciens, lien vers le site du fabricant.

### P2 — confort
8. **Sentinelle 2.0**
   - Démarrage avec Windows (optionnel).
   - Notifications toast natives Windows 10/11 au lieu des bulles.
   - Au démarrage qui suit un crash : « Votre PC a planté hier à 22 h 14 — voir l'analyse ».
9. **IA contextuelle**
   - Lancée depuis un crash précis.
   - Réponse en streaming.
   - Modèles à jour : Claude Sonnet 5.5 / Haiku 5.5 au lieu de `claude-sonnet-4-20250514`. On retire aussi le modèle Gemini fictif « gemini-3.8-flash ».
   - Clé chiffrée avec **DPAPI**.
10. **MCP renforcé**
    - `apply_fix` passe en mode « dry-run » par défaut, avec confirmation dans l'app.
    - Nouveaux outils `get_crash_detail` et `export_report`.
    - Enregistrement en 1 clic pour Claude Desktop, Claude Code et Antigravity.
11. **Localisation complète** : les messages des moteurs sont traduits dans les 6 langues (aujourd'hui seulement FR et EN), avec des chaînes externalisées dans des tables plutôt que 584 appels en ligne.

### P3 — bonus
12. Vérification des mises à jour, via les releases GitHub si le projet y est publié.
13. Comparaison de deux scans (diff).
14. Mode « portable » : les réglages restent à côté de l'exe.

---

## 5. Fondations techniques (préalable)

1. **`git init`** avec un `.gitignore` (exe, zip, `DumpFinder_old_*`) pour pouvoir revenir en arrière à chaque étape. La racine est nettoyée.
2. **Projet SDK-style `.csproj`** ciblant **.NET Framework 4.8** : on garde l'exe unique et léger, sans runtime à installer. `build.ps1` est conservé comme raccourci (`dotnet build` / `msbuild`).
3. **Passage au XAML et à un MVVM léger** (un `ObservableObject` maison, zéro dépendance). Découpage :
   - `Core/` : moteurs, inchangés au départ ;
   - `UI/Views/`, `UI/Components/`, `UI/Themes/` ;
   - `Resources/` : chaînes et icônes.
4. **Tout en asynchrone** : WMI, correctifs, ouverture de .dmp et changement de langue sortent du thread UI.
5. **Journalisation** : les 67 `catch { }` vides écrivent dans `%LOCALAPPDATA%\DumpFinder\logs`, avec un message utilisateur quand l'erreur compte.
6. **Factorisation** : un seul helper d'élévation, un seul système de sous-onglets, suppression du code mort.
7. Le mode `--screenshot` est conservé et étendu pour capturer automatiquement chaque écran. C'est notre test de non-régression visuelle.

---

## 6. Phasage proposé

| Phase | Contenu | Résultat visible |
|---|---|---|
| **0 · Socle** ✅ | Section 5 : git, csproj, XAML, async, logs | Même appli, plus de gels, base saine |
| **1 · Identité** ✅ | Logo, palette, design system, coque (Mica, sidebar, toasts) | L'appli « change de visage » |
| **2 · Navigation** ✅ | 5 sections, Accueil avec score et frise, Crashs avec panneau de détail, Paramètres, nouvel onboarding | Nouvelle expérience complète |
| **3 · Analyse** ✅ | P1 n° 1, 2, 3, 7 (dumps, bugchecks, fiabilité, pilotes) | Diagnostics nettement plus précis |
| **4 · Actions & données** ✅ | P1 n° 4, 5, 6 (historique, correctifs fiabilisés, rapports) | Fiabilité et partage |
| **5 · Confort** ✅ | P2 (sentinelle, IA, MCP, i18n) | Finitions |
| **6a · Stress test v2** ✅ | Tests vérifiés CPU (tous cœurs + cœur par cœur, AVX2), GPU, VRAM, pics CPU+GPU, RAM (~85 % de la mémoire libre), SSD (écriture + relecture non bufferisée) ; formules Rapide / Approfondi ; verdict + correctifs ; détection d'un plantage pendant le test | Le test trouve les erreurs au lieu de seulement chauffer |
| **6b · Détection v2** ✅ | Pilotes à risque (inpoutx64…), pilotes NVIDIA retirés, LiveKernelReports, volmgr 161, support de Windows par build, base de connaissances 2025–2026 | Les pannes connues de 2025–2026 sont reconnues |
| **7 · Optimiser** ✅ | Nouvelle section : réglages Windows officiels annulables (pubs, Bing, Copilot/Recall, confidentialité, jeu), apps préinstallées (liste prudente, réinstallables), programmes au démarrage, nettoyage des caches, section Mythes | Debloat sans rien casser |

Chaque phase se termine par une build fonctionnelle, des captures d'écran et ta validation avant de passer à la suivante.

---

## 7. Décisions à valider

1. ~~**Logo**~~ : ✅ A · Stack Fault.
2. ✅ **Stack** : .NET Framework 4.8, exe unique sans installation, interface en XAML.
3. ✅ **Navigation** : 5 sections (Accueil, Crashs, Matériel avec le stress test, Correctifs, Outils) plus Paramètres. L'IA est accessible depuis chaque crash.
4. ✅ **Périmètre** : premier lot = phases 0 → 2 (socle, identité, navigation). Les phases 3 → 5 viendront dans un second lot.
5. ✅ **Nom** : « Dump Finder » est conservé, `WinCrashDoctor` disparaît des binaires.

**Plan validé, prêt à démarrer par la phase 0.**
