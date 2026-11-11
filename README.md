# Dump Finder

Dump Finder aide à comprendre pourquoi un PC Windows plante (écrans bleus, timeouts GPU, redémarrages brutaux) et propose des correctifs.

**[Télécharger la dernière version](https://github.com/LucasRedjaimia/DumpFinder/releases/latest)** : un seul fichier `DumpFinder.exe`, sans installation (Windows 10 / 11, 64 bits).

## Ce que fait l'app

- **Crashs** : lit les journaux Windows et les fichiers de vidage (minidumps), identifie le pilote ou le composant en cause et explique les causes fréquentes.
- **Matériel** : BIOS, mémoire (EXPO/XMP), GPU, SSD (SMART), pilotes à risque, version de Windows.
- **Stress test** : CPU (cœur par cœur), mémoire, GPU (OpenCL), disque, avec résultats vérifiés et température du CPU.
- **Correctifs** : réglages Windows connus pour stabiliser un PC (TDR, MPO, HMB des SSD NVMe…), tous annulables.
- **Optimiser** : réglages Windows officiels, applications préinstallées, programmes au démarrage, nettoyage de fichiers temporaires.
- **Assistant IA** (optionnel, avec votre propre clé) et serveur MCP.
- Interface en français, anglais, espagnol, allemand, italien et portugais.

Au lancement, l'app vérifie sur GitHub si une nouvelle version existe et affiche ses nouveautés avec un lien de téléchargement. Rien n'est téléchargé ni installé automatiquement ; la vérification peut être désactivée dans **Paramètres › Mises à jour**.

## Compiler

Prérequis : [SDK .NET 8](https://dotnet.microsoft.com/download) (le projet cible .NET Framework 4.8).

```powershell
.\build.ps1
```

L'exécutable est produit à la racine : `DumpFinder.exe`.

## Publier une nouvelle version

Prérequis : [GitHub CLI](https://cli.github.com/) connecté (`gh auth login`).

1. Lancer `.\release.ps1 -Version 6.1.0`. La première fois, le script crée `release-notes\v6.1.0.md` et s'arrête.
2. Écrire dans ce fichier les nouveautés et corrections (Markdown).
3. Relancer `.\release.ps1 -Version 6.1.0`. Le script met à jour le numéro de version, compile, commit, crée le tag, pousse et publie la release avec `DumpFinder.exe`.

Les utilisateurs voient la nouvelle version au prochain lancement de l'app.

## Composants tiers

- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) : lecture de la température du CPU, via le pilote [PawnIO](https://pawnio.eu/).
- [Anthropic SDK for C#](https://github.com/anthropics/anthropic-sdk-csharp) (MIT).
- [Costura.Fody](https://github.com/Fody/Costura) (MIT) : regroupe les dépendances dans un seul exécutable.
