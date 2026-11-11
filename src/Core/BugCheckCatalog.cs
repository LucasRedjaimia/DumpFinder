using System;
using System.Collections.Generic;

namespace WinCrashDoctor
{
    public class BugCheckInfo
    {
        public uint Code { get; set; }
        public string Name { get; set; }
        /// <summary>Plain-language explanation (null for codes without a curated text).</summary>
        public string ExplanationFr { get; set; }
        public string ExplanationEn { get; set; }
        /// <summary>1-based index of the parameter holding a code address inside the faulting module (0 = none).</summary>
        public int AddressParam { get; set; }
        /// <summary>True when the code usually points at hardware rather than software.</summary>
        public bool LikelyHardware { get; set; }
        /// <summary>Dump Finder fix ids that commonly help, best first.</summary>
        public string[] FixIds { get; set; }

        public string Hex { get { return string.Format("0x{0:X8}", Code); } }
    }

    /// <summary>
    /// Windows stop codes: official names for ~290 codes, curated explanations for the common ones.
    /// </summary>
    public static class BugCheckCatalog
    {
        private static readonly Dictionary<uint, BugCheckInfo> Map = new Dictionary<uint, BugCheckInfo>();

        public static BugCheckInfo Get(uint code)
        {
            BugCheckInfo info;
            if (Map.TryGetValue(code, out info)) return info;
            // 0x1000007E-style codes are the same stop with "parameters in a different order" flag.
            if ((code & 0x10000000) != 0 && Map.TryGetValue(code & ~0x10000000u, out info)) return info;
            return null;
        }

        public static string NameOf(uint code)
        {
            var i = Get(code);
            return i != null ? i.Name : string.Format("0x{0:X8}", code);
        }

        /// <summary>Plain explanation for a stop code, or a generic sentence naming it.</summary>
        public static string Explain(uint code)
        {
            var i = Get(code);
            if (i != null && i.ExplanationFr != null) return Localizer.T(i.ExplanationFr, i.ExplanationEn);
            string name = i != null ? i.Name : string.Format("0x{0:X8}", code);
            return Localizer.T(
                "Arrêt " + name + " : Windows s'est arrêté pour éviter d'endommager vos données. Le pilote indiqué dans l'analyse du dump est la meilleure piste.",
                "Stop " + name + ": Windows halted to avoid damaging your data. The driver named in the dump analysis is the best lead.");
        }

        static BugCheckCatalog()
        {
            // ---- Curated (most frequent on desktop PCs)
            Add(0x0A, "IRQL_NOT_LESS_OR_EQUAL", 4, false, new[] { "sfc_repair" },
                "Un pilote a lu une adresse mémoire interdite à un moment où il n'en avait pas le droit. Presque toujours un pilote tiers (réseau, audio, RGB, antivirus) ; plus rarement une RAM instable.",
                "A driver touched a memory address it was not allowed to at that moment. Almost always a third-party driver (network, audio, RGB, antivirus); less often unstable RAM.");
            Add(0x19, "BAD_POOL_HEADER", 0, false, new[] { "sfc_repair" },
                "Une zone de mémoire du noyau a été corrompue. Cause typique : pilote tiers défectueux (antivirus, VPN, RGB) ou RAM instable (profil XMP/EXPO).",
                "A kernel memory block was corrupted. Typical causes: a faulty third-party driver (antivirus, VPN, RGB) or unstable RAM (XMP/EXPO profile).");
            Add(0x1A, "MEMORY_MANAGEMENT", 0, true, new[] { "pagefile_auto", "sfc_repair" },
                "Le gestionnaire de mémoire de Windows a trouvé une incohérence grave. Très souvent la RAM (profil XMP/EXPO trop agressif, barrette défaillante), parfois un pilote.",
                "Windows' memory manager found a serious inconsistency. Very often RAM (aggressive XMP/EXPO profile, failing stick), sometimes a driver.");
            Add(0x1E, "KMODE_EXCEPTION_NOT_HANDLED", 2, false, new[] { "sfc_repair" },
                "Un programme du noyau (pilote) a provoqué une erreur qu'il n'a pas su gérer. Le paramètre 2 pointe directement dans le pilote fautif.",
                "A kernel component (driver) raised an error it did not handle. Parameter 2 points straight into the faulting driver.");
            Add(0x24, "NTFS_FILE_SYSTEM", 0, true, new[] { "sfc_repair" },
                "Le système de fichiers NTFS a rencontré une erreur : disque en fin de vie, câble SATA défaillant ou corruption du volume. Lancez chkdsk et vérifiez le SMART.",
                "The NTFS file system hit an error: failing disk, faulty SATA cable or volume corruption. Run chkdsk and check SMART.");
            Add(0x3B, "SYSTEM_SERVICE_EXCEPTION", 2, false, new[] { "sfc_repair" },
                "Une erreur s'est produite pendant un appel système. Le paramètre 2 donne l'adresse de l'instruction fautive, souvent dans un pilote graphique, un anti-cheat ou un antivirus.",
                "An error occurred during a system call. Parameter 2 is the faulting instruction, often in a graphics driver, anti-cheat or antivirus.");
            Add(0x4E, "PFN_LIST_CORRUPT", 0, true, new[] { "sfc_repair" },
                "La table des pages mémoire est corrompue. Cause la plus fréquente : RAM instable ou défaillante ; plus rarement un pilote qui libère deux fois la même mémoire.",
                "The page frame database is corrupted. Most often unstable or failing RAM; more rarely a driver freeing the same memory twice.");
            Add(0x50, "PAGE_FAULT_IN_NONPAGED_AREA", 3, false, new[] { "pagefile_auto", "sfc_repair" },
                "Le noyau a accédé à une mémoire qui n'existe pas. Le paramètre 3 pointe vers l'instruction fautive. Causes : pilote bogué, antivirus, ou RAM instable.",
                "The kernel accessed memory that does not exist. Parameter 3 points to the faulting instruction. Causes: buggy driver, antivirus, or unstable RAM.");
            Add(0x7A, "KERNEL_DATA_INPAGE_ERROR", 0, true, new[] { "nvme_hmb", "pcie_aspm" },
                "Windows n'a pas pu relire depuis le disque une page de mémoire du noyau. Signe d'un SSD/disque défaillant, d'un câble ou d'un contrôleur instable.",
                "Windows could not read back a kernel memory page from disk. Sign of a failing SSD/disk, cable or unstable controller.");
            Add(0x7B, "INACCESSIBLE_BOOT_DEVICE", 0, true, null,
                "Windows ne trouve plus son disque de démarrage : mode SATA/RAID changé dans le BIOS, mise à jour du pilote de stockage ratée ou disque défaillant.",
                "Windows cannot reach its boot disk: SATA/RAID mode changed in BIOS, failed storage driver update or failing disk.");
            Add(0x7E, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", 2, false, new[] { "sfc_repair" },
                "Un thread système a planté sans être rattrapé. Le paramètre 2 pointe dans le pilote responsable (souvent graphique, Wi-Fi ou un pilote récemment installé).",
                "A system thread crashed and was not caught. Parameter 2 points into the responsible driver (often graphics, Wi-Fi or a recently installed driver).");
            Add(0x7F, "UNEXPECTED_KERNEL_MODE_TRAP", 0, true, null,
                "Le processeur a signalé une erreur que le noyau ne pouvait pas gérer (souvent un débordement de pile). Pistes : overclocking/undervolt CPU, RAM, pilote très bas niveau.",
                "The CPU raised a trap the kernel could not handle (often a stack overflow). Leads: CPU overclock/undervolt, RAM, very low-level driver.");
            Add(0x8E, "KERNEL_MODE_EXCEPTION_NOT_HANDLED", 2, false, new[] { "sfc_repair" },
                "Une exception non gérée dans le noyau. Le paramètre 2 indique l'adresse fautive. Pilote tiers ou RAM instable.",
                "An unhandled exception in kernel mode. Parameter 2 is the faulting address. Third-party driver or unstable RAM.");
            Add(0x9C, "MACHINE_CHECK_EXCEPTION", 0, true, null,
                "Le processeur a détecté une erreur matérielle fatale (ancien équivalent de WHEA). Overclocking, tension, température ou carte mère.",
                "The CPU detected a fatal hardware error (older equivalent of WHEA). Overclock, voltage, temperature or motherboard.");
            Add(0x9F, "DRIVER_POWER_STATE_FAILURE", 0, false, new[] { "fast_startup", "usb_suspend", "pcie_aspm", "nic_powersave" },
                "Un pilote n'a pas répondu lors d'une mise en veille ou d'un réveil. Très fréquent avec les cartes Wi-Fi, USB et les pilotes graphiques ; le Démarrage rapide aggrave souvent le problème.",
                "A driver did not respond during sleep or wake. Very common with Wi-Fi, USB and graphics drivers; Fast Startup often makes it worse.");
            Add(0xA0, "INTERNAL_POWER_ERROR", 0, false, new[] { "fast_startup" },
                "Erreur interne de gestion de l'alimentation, souvent liée à l'hibernation ou au Démarrage rapide.",
                "Internal power-management error, often tied to hibernation or Fast Startup.");
            Add(0xBE, "ATTEMPTED_WRITE_TO_READONLY_MEMORY", 0, false, null,
                "Un pilote a tenté d'écrire dans une mémoire protégée en lecture seule : pilote bogué ou malveillant.",
                "A driver tried to write to read-only memory: buggy or malicious driver.");
            Add(0xC2, "BAD_POOL_CALLER", 0, false, null,
                "Un pilote a mal utilisé la mémoire du noyau (libération en double, mauvaise taille). Presque toujours logiciel.",
                "A driver misused kernel memory (double free, bad size). Almost always software.");
            Add(0xC4, "DRIVER_VERIFIER_DETECTED_VIOLATION", 0, false, null,
                "Le Vérificateur de pilotes de Windows a pris un pilote en faute. Si vous l'avez activé, désactivez-le (verifier /reset) après avoir noté le pilote incriminé.",
                "Windows Driver Verifier caught a driver misbehaving. If you enabled it, turn it off (verifier /reset) after noting the culprit.");
            Add(0xC5, "DRIVER_CORRUPTED_EXPOOL", 4, false, null,
                "Un pilote a corrompu la mémoire du noyau. Le paramètre 4 pointe dans le pilote qui l'a touchée.",
                "A driver corrupted kernel memory. Parameter 4 points into the driver that touched it.");
            Add(0xD1, "DRIVER_IRQL_NOT_LESS_OR_EQUAL", 4, false, new[] { "nic_powersave", "sfc_repair" },
                "Un pilote a accédé à une mémoire invalide à un moment critique. Le paramètre 4 pointe dans ce pilote : réseau (Realtek, Killer, Intel), audio ou USB le plus souvent.",
                "A driver accessed invalid memory at a critical moment. Parameter 4 points into that driver: network (Realtek, Killer, Intel), audio or USB most often.");
            Add(0xEA, "THREAD_STUCK_IN_DEVICE_DRIVER", 0, false, new[] { "tdr", "mpo" },
                "Le pilote graphique est resté bloqué en attendant la carte. Ancêtre du TDR : pilote GPU, alimentation ou surchauffe.",
                "The graphics driver got stuck waiting for the card. Predecessor of TDR: GPU driver, power or overheating.");
            Add(0xEF, "CRITICAL_PROCESS_DIED", 0, false, new[] { "sfc_repair", "nvme_hmb" },
                "Un processus indispensable à Windows s'est arrêté. Fichiers système corrompus, SSD qui décroche (NVMe) ou antivirus trop agressif.",
                "A process Windows cannot live without stopped. Corrupted system files, SSD dropping out (NVMe) or overzealous antivirus.");
            Add(0xF4, "CRITICAL_OBJECT_TERMINATION", 0, true, new[] { "nvme_hmb", "pcie_aspm" },
                "Un processus critique a été tué, souvent parce que le disque système a cessé de répondre (SSD, câble, contrôleur).",
                "A critical process was terminated, often because the system disk stopped responding (SSD, cable, controller).");
            Add(0xFC, "ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", 0, false, null,
                "Un pilote a tenté d'exécuter du code depuis une mémoire non exécutable : pilote bogué ou corruption mémoire.",
                "A driver tried to execute code from non-executable memory: buggy driver or memory corruption.");
            Add(0xFE, "BUGCODE_USB_DRIVER", 0, false, new[] { "usb_suspend" },
                "Erreur fatale dans la pile USB : périphérique USB défaillant, hub, ou pilote de contrôleur USB.",
                "Fatal error in the USB stack: faulty USB device, hub or USB controller driver.");
            Add(0x101, "CLOCK_WATCHDOG_TIMEOUT", 0, true, null,
                "Un cœur du processeur a cessé de répondre. Presque toujours matériel : overclocking/undervolt CPU, tension, BIOS ancien, CPU défaillant.",
                "A CPU core stopped responding. Almost always hardware: CPU overclock/undervolt, voltage, old BIOS, failing CPU.");
            Add(0x109, "CRITICAL_STRUCTURE_CORRUPTION", 0, false, null,
                "Le noyau a détecté une modification interdite de ses structures (PatchGuard) : pilote de triche, de monitoring bas niveau, ou RAM défaillante.",
                "The kernel detected forbidden changes to its structures (PatchGuard): cheat or low-level monitoring driver, or failing RAM.");
            Add(0x10E, "VIDEO_MEMORY_MANAGEMENT_INTERNAL", 0, false, new[] { "tdr", "hags", "shader_cache" },
                "Erreur interne de la gestion de la mémoire vidéo : pilote graphique, VRAM instable (overclock mémoire GPU) ou HAGS.",
                "Internal video memory management error: graphics driver, unstable VRAM (GPU memory overclock) or HAGS.");
            Add(0x113, "VIDEO_DXGKRNL_FATAL_ERROR", 0, false, new[] { "mpo", "wu_driver_lock" },
                "Le noyau graphique de Windows (DirectX) a rencontré une erreur fatale, souvent après une mise à jour ratée du pilote GPU.",
                "Windows' graphics kernel (DirectX) hit a fatal error, often after a botched GPU driver update.");
            Add(0x116, "VIDEO_TDR_FAILURE", 2, false, new[] { "mpo", "tdr", "wu_driver_lock", "hags", "amd_ulps" },
                "La carte graphique a cessé de répondre et Windows n'a pas réussi à relancer son pilote. Pilote GPU, MPO, undervolt/overclock trop agressif, alimentation ou câble PCIe. Le paramètre 2 pointe dans le pilote graphique.",
                "The graphics card stopped responding and Windows could not restart its driver. GPU driver, MPO, aggressive undervolt/overclock, power supply or PCIe cable. Parameter 2 points into the graphics driver.");
            Add(0x117, "VIDEO_TDR_TIMEOUT_DETECTED", 2, false, new[] { "tdr", "mpo" },
                "La carte graphique n'a pas répondu à temps ; Windows a relancé le pilote avec succès cette fois. Mêmes causes qu'un 0x116, en moins grave.",
                "The graphics card did not respond in time; Windows restarted the driver successfully this time. Same causes as 0x116, less severe.");
            Add(0x119, "VIDEO_SCHEDULER_INTERNAL_ERROR", 0, false, new[] { "hags", "mpo", "tdr" },
                "Le planificateur graphique a détecté une incohérence : pilote GPU, HAGS ou overclock de la carte.",
                "The graphics scheduler detected an inconsistency: GPU driver, HAGS or card overclock.");
            Add(0x124, "WHEA_UNCORRECTABLE_ERROR", 0, true, new[] { "pcie_aspm" },
                "Le matériel a signalé une erreur impossible à corriger : processeur (overclock, undervolt, Curve Optimizer), RAM, lien PCIe ou alimentation. Ce n'est pas un problème de pilote.",
                "The hardware reported an uncorrectable error: CPU (overclock, undervolt, Curve Optimizer), RAM, PCIe link or power supply. Not a driver problem.");
            Add(0x12B, "FAULTY_HARDWARE_CORRUPTED_PAGE", 0, true, null,
                "Une page mémoire a été corrompue par le matériel : RAM défaillante ou instable.",
                "A memory page was corrupted by hardware: failing or unstable RAM.");
            Add(0x133, "DPC_WATCHDOG_VIOLATION", 0, false, new[] { "nvme_hmb", "pcie_aspm", "sfc_repair" },
                "Un pilote a monopolisé le processeur trop longtemps. Classiques : firmware de SSD NVMe ancien, pilote de stockage/SATA, pilote audio ou réseau.",
                "A driver hogged the CPU for too long. Classics: old NVMe SSD firmware, storage/SATA driver, audio or network driver.");
            Add(0x139, "KERNEL_SECURITY_CHECK_FAILURE", 0, false, new[] { "sfc_repair" },
                "Le noyau a détecté une corruption de ses structures de données (débordement de tampon). Pilote tiers, anti-cheat ou RAM instable.",
                "The kernel detected corruption of its data structures (buffer overrun). Third-party driver, anti-cheat or unstable RAM.");
            Add(0x13A, "KERNEL_MODE_HEAP_CORRUPTION", 0, false, null,
                "Le tas mémoire du noyau a été corrompu par un pilote. Souvent pilote graphique, audio ou de périphérique RGB.",
                "The kernel heap was corrupted by a driver. Often a graphics, audio or RGB device driver.");
            Add(0x144, "BUGCODE_USB3_DRIVER", 0, false, new[] { "usb_suspend" },
                "Erreur fatale du pilote USB 3 : périphérique, hub, câble ou économie d'énergie USB.",
                "Fatal USB 3 driver error: device, hub, cable or USB power saving.");
            Add(0x154, "UNEXPECTED_STORE_EXCEPTION", 0, true, new[] { "nvme_hmb", "fast_startup" },
                "Le magasin de mémoire compressée a rencontré une erreur, souvent à cause du disque (SSD qui décroche, secteurs défectueux) ou d'un antivirus.",
                "The compressed memory store hit an error, often because of the disk (SSD dropping out, bad sectors) or an antivirus.");
            Add(0x1C8, "MANUALLY_INITIATED_POWER_BUTTON_HOLD", 0, false, null,
                "Le bouton d'alimentation a été maintenu enfoncé : Windows a créé ce dump pour diagnostiquer un PC figé.",
                "The power button was held down: Windows wrote this dump to diagnose a frozen PC.");
            Add(0xE2, "MANUALLY_INITIATED_CRASH", 0, false, null,
                "Crash déclenché volontairement (clavier ou outil de diagnostic).",
                "Intentionally triggered crash (keyboard shortcut or diagnostic tool).");

            Add(0x18B, "SECURE_KERNEL_ERROR", 0, false, null,
                "Le noyau sécurisé (sécurité basée sur la virtualisation, intégrité de la mémoire) a détecté une erreur. Déjà provoqué par une mise à jour de Windows (avril 2025) et par des pilotes incompatibles avec l'intégrité de la mémoire : installe les dernières mises à jour et mets à jour les pilotes signalés dans Sécurité Windows.",
                "The secure kernel (virtualization-based security, Memory Integrity) detected an error. Already caused by a Windows update (April 2025) and by drivers incompatible with Memory Integrity: install the latest updates and update the drivers flagged in Windows Security.");

            // ---- Names only (official stop code names)
            Names(
                0x01, "APC_INDEX_MISMATCH", 0x02, "DEVICE_QUEUE_NOT_BUSY", 0x03, "INVALID_AFFINITY_SET", 0x04, "INVALID_DATA_ACCESS_TRAP",
                0x05, "INVALID_PROCESS_ATTACH_ATTEMPT", 0x06, "INVALID_PROCESS_DETACH_ATTEMPT", 0x07, "INVALID_SOFTWARE_INTERRUPT",
                0x08, "IRQL_NOT_DISPATCH_LEVEL", 0x09, "IRQL_NOT_GREATER_OR_EQUAL", 0x0B, "NO_EXCEPTION_HANDLING_SUPPORT",
                0x0C, "MAXIMUM_WAIT_OBJECTS_EXCEEDED", 0x0D, "MUTEX_LEVEL_NUMBER_VIOLATION", 0x0E, "NO_USER_MODE_CONTEXT",
                0x0F, "SPIN_LOCK_ALREADY_OWNED", 0x10, "SPIN_LOCK_NOT_OWNED", 0x11, "THREAD_NOT_MUTEX_OWNER", 0x12, "TRAP_CAUSE_UNKNOWN",
                0x13, "EMPTY_THREAD_REAPER_LIST", 0x14, "CREATE_DELETE_LOCK_NOT_LOCKED", 0x15, "LAST_CHANCE_CALLED_FROM_KMODE",
                0x16, "CID_HANDLE_CREATION", 0x17, "CID_HANDLE_DELETION", 0x18, "REFERENCE_BY_POINTER", 0x1B, "PFN_SHARE_COUNT",
                0x1C, "PFN_REFERENCE_COUNT", 0x1D, "NO_SPIN_LOCK_AVAILABLE", 0x1F, "SHARED_RESOURCE_CONV_ERROR",
                0x20, "KERNEL_APC_PENDING_DURING_EXIT", 0x21, "QUOTA_UNDERFLOW", 0x22, "FILE_SYSTEM", 0x23, "FAT_FILE_SYSTEM",
                0x25, "NPFS_FILE_SYSTEM", 0x26, "CDFS_FILE_SYSTEM", 0x27, "RDR_FILE_SYSTEM", 0x28, "CORRUPT_ACCESS_TOKEN",
                0x29, "SECURITY_SYSTEM", 0x2A, "INCONSISTENT_IRP", 0x2B, "PANIC_STACK_SWITCH", 0x2C, "PORT_DRIVER_INTERNAL",
                0x2D, "SCSI_DISK_DRIVER_INTERNAL", 0x2E, "DATA_BUS_ERROR", 0x2F, "INSTRUCTION_BUS_ERROR", 0x30, "SET_OF_INVALID_CONTEXT",
                0x31, "PHASE0_INITIALIZATION_FAILED", 0x32, "PHASE1_INITIALIZATION_FAILED", 0x33, "UNEXPECTED_INITIALIZATION_CALL",
                0x34, "CACHE_MANAGER", 0x35, "NO_MORE_IRP_STACK_LOCATIONS", 0x36, "DEVICE_REFERENCE_COUNT_NOT_ZERO",
                0x37, "FLOPPY_INTERNAL_ERROR", 0x38, "SERIAL_DRIVER_INTERNAL", 0x39, "SYSTEM_EXIT_OWNED_MUTEX",
                0x3A, "SYSTEM_UNWIND_PREVIOUS_USER", 0x3C, "INTERRUPT_UNWIND_ATTEMPTED", 0x3D, "INTERRUPT_EXCEPTION_NOT_HANDLED",
                0x3E, "MULTIPROCESSOR_CONFIGURATION_NOT_SUPPORTED", 0x3F, "NO_MORE_SYSTEM_PTES", 0x40, "TARGET_MDL_TOO_SMALL",
                0x41, "MUST_SUCCEED_POOL_EMPTY", 0x42, "ATDISK_DRIVER_INTERNAL", 0x43, "NO_SUCH_PARTITION",
                0x44, "MULTIPLE_IRP_COMPLETE_REQUESTS", 0x45, "INSUFFICIENT_SYSTEM_MAP_REGS", 0x46, "DEREF_UNKNOWN_LOGON_SESSION",
                0x47, "REF_UNKNOWN_LOGON_SESSION", 0x48, "CANCEL_STATE_IN_COMPLETED_IRP", 0x49, "PAGE_FAULT_WITH_INTERRUPTS_OFF",
                0x4A, "IRQL_GT_ZERO_AT_SYSTEM_SERVICE", 0x4B, "STREAMS_INTERNAL_ERROR", 0x4C, "FATAL_UNHANDLED_HARD_ERROR",
                0x4D, "NO_PAGES_AVAILABLE", 0x4F, "NDIS_INTERNAL_ERROR", 0x51, "REGISTRY_ERROR", 0x52, "MAILSLOT_FILE_SYSTEM",
                0x53, "NO_BOOT_DEVICE", 0x54, "LM_SERVER_INTERNAL_ERROR", 0x55, "DATA_COHERENCY_EXCEPTION",
                0x56, "INSTRUCTION_COHERENCY_EXCEPTION", 0x57, "XNS_INTERNAL_ERROR", 0x58, "FTDISK_INTERNAL_ERROR",
                0x59, "PINBALL_FILE_SYSTEM", 0x5A, "CRITICAL_SERVICE_FAILED", 0x5B, "SET_ENV_VAR_FAILED", 0x5C, "HAL_INITIALIZATION_FAILED",
                0x5D, "UNSUPPORTED_PROCESSOR", 0x5E, "OBJECT_INITIALIZATION_FAILED", 0x5F, "SECURITY_INITIALIZATION_FAILED",
                0x60, "PROCESS_INITIALIZATION_FAILED", 0x61, "HAL1_INITIALIZATION_FAILED", 0x62, "OBJECT1_INITIALIZATION_FAILED",
                0x63, "SECURITY1_INITIALIZATION_FAILED", 0x64, "SYMBOLIC_INITIALIZATION_FAILED", 0x65, "MEMORY1_INITIALIZATION_FAILED",
                0x66, "CACHE_INITIALIZATION_FAILED", 0x67, "CONFIG_INITIALIZATION_FAILED", 0x68, "FILE_INITIALIZATION_FAILED",
                0x69, "IO1_INITIALIZATION_FAILED", 0x6A, "LPC_INITIALIZATION_FAILED", 0x6B, "PROCESS1_INITIALIZATION_FAILED",
                0x6C, "REFMON_INITIALIZATION_FAILED", 0x6D, "SESSION1_INITIALIZATION_FAILED", 0x6E, "SESSION2_INITIALIZATION_FAILED",
                0x6F, "SESSION3_INITIALIZATION_FAILED", 0x70, "SESSION4_INITIALIZATION_FAILED", 0x71, "SESSION5_INITIALIZATION_FAILED",
                0x72, "ASSIGN_DRIVE_LETTERS_FAILED", 0x73, "CONFIG_LIST_FAILED", 0x74, "BAD_SYSTEM_CONFIG_INFO",
                0x75, "CANNOT_WRITE_CONFIGURATION", 0x76, "PROCESS_HAS_LOCKED_PAGES", 0x77, "KERNEL_STACK_INPAGE_ERROR",
                0x78, "PHASE0_EXCEPTION", 0x79, "MISMATCHED_HAL", 0x7C, "BUGCODE_NDIS_DRIVER", 0x7D, "INSTALL_MORE_MEMORY",
                0x80, "NMI_HARDWARE_FAILURE", 0x81, "SPIN_LOCK_INIT_FAILURE", 0x82, "DFS_FILE_SYSTEM", 0x85, "SETUP_FAILURE",
                0x8B, "MBR_CHECKSUM_MISMATCH", 0x8F, "PP0_INITIALIZATION_FAILED", 0x90, "PP1_INITIALIZATION_FAILED",
                0x92, "UP_DRIVER_ON_MP_SYSTEM", 0x93, "INVALID_KERNEL_HANDLE", 0x94, "KERNEL_STACK_LOCKED_AT_EXIT",
                0x96, "INVALID_WORK_QUEUE_ITEM", 0x97, "BOUND_IMAGE_UNSUPPORTED", 0x98, "END_OF_NT_EVALUATION_PERIOD",
                0x99, "INVALID_REGION_OR_SEGMENT", 0x9A, "SYSTEM_LICENSE_VIOLATION", 0x9B, "UDFS_FILE_SYSTEM",
                0x9E, "USER_MODE_HEALTH_MONITOR", 0xA1, "PCI_BUS_DRIVER_INTERNAL", 0xA2, "MEMORY_IMAGE_CORRUPT",
                0xA3, "ACPI_DRIVER_INTERNAL", 0xA4, "CNSS_FILE_SYSTEM_FILTER", 0xA5, "ACPI_BIOS_ERROR", 0xA7, "BAD_EXHANDLE",
                0xAB, "SESSION_HAS_VALID_POOL_ON_EXIT", 0xAC, "HAL_MEMORY_ALLOCATION", 0xAD, "VIDEO_DRIVER_DEBUG_REPORT_REQUEST",
                0xB1, "BGI_DETECTED_VIOLATION", 0xB4, "VIDEO_DRIVER_INIT_FAILURE", 0xB8, "ATTEMPTED_SWITCH_FROM_DPC",
                0xB9, "CHIPSET_DETECTED_ERROR", 0xBA, "SESSION_HAS_VALID_VIEWS_ON_EXIT", 0xBB, "NETWORK_BOOT_INITIALIZATION_FAILED",
                0xBC, "NETWORK_BOOT_DUPLICATE_ADDRESS", 0xBD, "INVALID_HIBERNATED_STATE", 0xBF, "MUTEX_ALREADY_OWNED",
                0xC1, "SPECIAL_POOL_DETECTED_MEMORY_CORRUPTION", 0xC6, "DRIVER_CAUGHT_MODIFYING_FREED_POOL",
                0xC7, "TIMER_OR_DPC_INVALID", 0xC8, "IRQL_UNEXPECTED_VALUE", 0xC9, "DRIVER_VERIFIER_IOMANAGER_VIOLATION",
                0xCA, "PNP_DETECTED_FATAL_ERROR", 0xCB, "DRIVER_LEFT_LOCKED_PAGES_IN_PROCESS", 0xCC, "PAGE_FAULT_IN_FREED_SPECIAL_POOL",
                0xCD, "PAGE_FAULT_BEYOND_END_OF_ALLOCATION", 0xCE, "DRIVER_UNLOADED_WITHOUT_CANCELLING_PENDING_OPERATIONS",
                0xCF, "TERMINAL_SERVER_DRIVER_MADE_INCORRECT_MEMORY_REFERENCE", 0xD0, "DRIVER_CORRUPTED_MMPOOL",
                0xD2, "BUGCODE_ID_DRIVER", 0xD3, "DRIVER_PORTION_MUST_BE_NONPAGED",
                0xD4, "SYSTEM_SCAN_AT_RAISED_IRQL_CAUGHT_IMPROPER_DRIVER_UNLOAD", 0xD5, "DRIVER_PAGE_FAULT_IN_FREED_SPECIAL_POOL",
                0xD6, "DRIVER_PAGE_FAULT_BEYOND_END_OF_ALLOCATION", 0xD7, "DRIVER_UNMAPPING_INVALID_VIEW",
                0xD8, "DRIVER_USED_EXCESSIVE_PTES", 0xD9, "LOCKED_PAGES_TRACKER_CORRUPTION", 0xDA, "SYSTEM_PTE_MISUSE",
                0xDB, "DRIVER_CORRUPTED_SYSPTES", 0xDC, "DRIVER_INVALID_STACK_ACCESS", 0xDE, "POOL_CORRUPTION_IN_FILE_AREA",
                0xDF, "IMPERSONATING_WORKER_THREAD", 0xE0, "ACPI_BIOS_FATAL_ERROR", 0xE1, "WORKER_THREAD_RETURNED_AT_BAD_IRQL",
                0xE3, "RESOURCE_NOT_OWNED", 0xE4, "WORKER_INVALID", 0xE6, "DRIVER_VERIFIER_DMA_VIOLATION",
                0xE7, "INVALID_FLOATING_POINT_STATE", 0xE8, "INVALID_CANCEL_OF_FILE_OPEN", 0xE9, "ACTIVE_EX_WORKER_THREAD_TERMINATION",
                0xEB, "DIRTY_MAPPED_PAGES_CONGESTION", 0xEC, "SESSION_HAS_VALID_SPECIAL_POOL_ON_EXIT", 0xED, "UNMOUNTABLE_BOOT_VOLUME",
                0xF0, "STORAGE_MINIPORT_ERROR", 0xF1, "SCSI_VERIFIER_DETECTED_VIOLATION", 0xF2, "HARDWARE_INTERRUPT_STORM",
                0xF3, "DISORDERLY_SHUTDOWN", 0xF5, "FLTMGR_FILE_SYSTEM", 0xF6, "PCI_VERIFIER_DETECTED_VIOLATION",
                0xF7, "DRIVER_OVERRAN_STACK_BUFFER", 0xF8, "RAMDISK_BOOT_INITIALIZATION_FAILED",
                0xF9, "DRIVER_RETURNED_STATUS_REPARSE_FOR_VOLUME_OPEN", 0xFA, "HTTP_DRIVER_CORRUPTED",
                0xFD, "DIRTY_NOWRITE_PAGES_CONGESTION", 0xFF, "RESERVE_QUEUE_OVERFLOW", 0x100, "LOADER_BLOCK_MISMATCH",
                0x102, "DPC_WATCHDOG_TIMEOUT", 0x103, "MUP_FILE_SYSTEM", 0x104, "AGP_INVALID_ACCESS", 0x105, "AGP_GART_CORRUPTION",
                0x106, "AGP_ILLEGALLY_REPROGRAMMED", 0x108, "THIRD_PARTY_FILE_SYSTEM_FAILURE", 0x10A, "APP_TAGGING_INITIALIZATION_FAILED",
                0x10C, "FSRTL_EXTRA_CREATE_PARAMETER_VIOLATION", 0x10D, "WDF_VIOLATION", 0x10F, "RESOURCE_MANAGER_EXCEPTION_NOT_HANDLED",
                0x111, "RECURSIVE_NMI", 0x112, "MSRPC_STATE_VIOLATION", 0x114, "VIDEO_SHADOW_DRIVER_FATAL_ERROR", 0x115, "AGP_INTERNAL",
                0x11A, "EM_INITIALIZATION_FAILURE", 0x11B, "DRIVER_RETURNED_HOLDING_CANCEL_LOCK",
                0x11C, "ATTEMPTED_WRITE_TO_CM_PROTECTED_STORAGE", 0x11D, "EVENT_TRACING_FATAL_ERROR", 0x11E, "TOO_MANY_RECURSIVE_FAULTS",
                0x11F, "INVALID_DRIVER_HANDLE", 0x120, "BITLOCKER_FATAL_ERROR", 0x121, "DRIVER_VIOLATION", 0x122, "WHEA_INTERNAL_ERROR",
                0x123, "CRYPTO_SELF_TEST_FAILURE", 0x125, "NMR_INVALID_STATE", 0x126, "NETIO_INVALID_POOL_CALLER", 0x127, "PAGE_NOT_ZERO",
                0x128, "WORKER_THREAD_RETURNED_WITH_BAD_IO_PRIORITY", 0x129, "WORKER_THREAD_RETURNED_WITH_BAD_PAGING_IO_PRIORITY",
                0x12A, "MUI_NO_VALID_SYSTEM_LANGUAGE", 0x12C, "EXFAT_FILE_SYSTEM", 0x12D, "VOLSNAP_OVERLAPPED_TABLE_ACCESS",
                0x12E, "INVALID_MDL_RANGE", 0x12F, "VHD_BOOT_INITIALIZATION_FAILED", 0x130, "DYNAMIC_ADD_PROCESSOR_MISMATCH",
                0x131, "INVALID_EXTENDED_PROCESSOR_STATE", 0x132, "RESOURCE_OWNER_POINTER_INVALID", 0x134, "DRIVE_EXTENDER",
                0x135, "REGISTRY_FILTER_DRIVER_EXCEPTION", 0x136, "VHD_BOOT_HOST_VOLUME_NOT_ENOUGH_SPACE", 0x137, "WIN32K_HANDLE_MANAGER",
                0x138, "GPIO_CONTROLLER_DRIVER_ERROR", 0x13B, "PASSIVE_INTERRUPT_ERROR", 0x13C, "INVALID_IO_BOOST_STATE",
                0x13D, "CRITICAL_INITIALIZATION_FAILURE", 0x140, "STORAGE_DEVICE_ABNORMALITY_DETECTED", 0x143, "PROCESSOR_DRIVER_INTERNAL",
                0x145, "SECURE_BOOT_VIOLATION", 0x147, "ABNORMAL_RESET_DETECTED", 0x149, "REFS_FILE_SYSTEM", 0x14A, "KERNEL_WMI_INTERNAL",
                0x14B, "SOC_SUBSYSTEM_FAILURE", 0x14C, "FATAL_ABNORMAL_RESET_ERROR", 0x14D, "EXCEPTION_SCOPE_INVALID",
                0x14E, "SOC_CRITICAL_DEVICE_REMOVED", 0x14F, "PDC_WATCHDOG_TIMEOUT", 0x150, "TCPIP_AOAC_NIC_ACTIVE_REFERENCE_LEAK",
                0x151, "UNSUPPORTED_INSTRUCTION_MODE", 0x152, "INVALID_PUSH_LOCK_FLAGS", 0x153, "KERNEL_LOCK_ENTRY_LEAKED_ON_THREAD_TERMINATION",
                0x155, "OS_DATA_TAMPERING", 0x156, "WINSOCK_DETECTED_HUNG_CLOSESOCKET_LIVEDUMP", 0x157, "KERNEL_THREAD_PRIORITY_FLOOR_VIOLATION",
                0x158, "ILLEGAL_IOMMU_PAGE_FAULT", 0x159, "HAL_ILLEGAL_IOMMU_PAGE_FAULT", 0x15A, "SDBUS_INTERNAL_ERROR",
                0x15B, "WORKER_THREAD_RETURNED_WITH_SYSTEM_PAGE_PRIORITY_ACTIVE", 0x15C, "PDC_WATCHDOG_TIMEOUT_LIVEDUMP",
                0x15F, "CONNECTED_STANDBY_WATCHDOG_TIMEOUT_LIVEDUMP", 0x160, "WIN32K_ATOMIC_CHECK_FAILURE", 0x161, "LIVE_SYSTEM_DUMP",
                0x162, "KERNEL_AUTO_BOOST_INVALID_LOCK_RELEASE", 0x163, "WORKER_THREAD_TEST_CONDITION", 0x164, "WIN32K_CRITICAL_FAILURE",
                0x16C, "INVALID_RUNDOWN_PROTECTION_FLAGS", 0x16D, "INVALID_SLOT_ALLOCATOR_FLAGS", 0x16E, "ERESOURCE_INVALID_RELEASE",
                0x170, "CLUSTER_CSV_CLUSSVC_DISCONNECT_WATCHDOG", 0x171, "CRYPTO_LIBRARY_INTERNAL_ERROR", 0x173, "COREMSGCALL_INTERNAL_ERROR",
                0x174, "COREMSG_INTERNAL_ERROR", 0x178, "ELAM_DRIVER_DETECTED_FATAL_ERROR", 0x17B, "PROFILER_CONFIGURATION_ILLEGAL",
                0x17E, "MICROCODE_REVISION_MISMATCH", 0x187, "VIDEO_DWMINIT_TIMEOUT_FALLBACK_BDD", 0x189, "BAD_OBJECT_HEADER",
                0x18B, "SECURE_KERNEL_ERROR", 0x18C, "HYPERGUARD_VIOLATION", 0x18D, "SECURE_FAULT_UNHANDLED",
                0x18E, "KERNEL_PARTITION_REFERENCE_VIOLATION", 0x190, "WIN32K_CRITICAL_FAILURE_LIVEDUMP", 0x191, "PF_DETECTED_CORRUPTION",
                0x192, "KERNEL_AUTO_BOOST_LOCK_ACQUISITION_WITH_RAISED_IRQL", 0x193, "VIDEO_DXGKRNL_LIVEDUMP", 0x195, "SMB_SERVER_LIVEDUMP",
                0x196, "LOADER_ROLLBACK_DETECTED", 0x197, "WIN32K_SECURITY_FAILURE", 0x198, "UFX_LIVEDUMP", 0x199, "KERNEL_STORAGE_SLOT_IN_USE",
                0x19A, "WORKER_THREAD_RETURNED_WHILE_ATTACHED_TO_SILO", 0x19B, "TTM_FATAL_ERROR", 0x19C, "WIN32K_POWER_WATCHDOG_TIMEOUT",
                0x1A0, "TTM_WATCHDOG_TIMEOUT", 0x1A1, "WIN32K_CALLOUT_WATCHDOG_LIVEDUMP", 0x1A2, "WIN32K_CALLOUT_WATCHDOG_BUGCHECK",
                0x1A3, "CALL_HAS_NOT_RETURNED_WATCHDOG_TIMEOUT_LIVEDUMP", 0x1A4, "DRIPS_SW_HW_DIVERGENCE_LIVEDUMP",
                0x1A5, "USB_DRIPS_BLOCKER_SURPRISE_REMOVAL_LIVEDUMP", 0x1A6, "BLUETOOTH_ERROR_RECOVERY_LIVEDUMP",
                0x1A7, "SMB_REDIRECTOR_LIVEDUMP", 0x1A8, "VIDEO_DXGKRNL_BLACK_SCREEN_LIVEDUMP", 0x1B0, "VIDEO_MINIPORT_FAILED_LIVEDUMP",
                0x1B8, "VIDEO_MINIPORT_BLACK_SCREEN_LIVEDUMP", 0x1C4, "DRIVER_VERIFIER_DETECTED_VIOLATION_LIVEDUMP",
                0x1C5, "IO_THREADPOOL_DEADLOCK_LIVEDUMP", 0x1C6, "FAST_ERESOURCE_PRECONDITION_VIOLATION",
                0x1C7, "STORE_DATA_STRUCTURE_CORRUPTION", 0x1C9, "USER_MODE_HEALTH_MONITOR_LIVEDUMP", 0x1CA, "SYNTHETIC_WATCHDOG_TIMEOUT",
                0x1CB, "INVALID_SILO_DETACH", 0x1CC, "EXRESOURCE_TIMEOUT_LIVEDUMP", 0x1CD, "INVALID_CALLBACK_STACK_ADDRESS",
                0x1CE, "INVALID_KERNEL_STACK_ADDRESS", 0x1CF, "HARDWARE_WATCHDOG_TIMEOUT", 0x1D0, "ACPI_FIRMWARE_WATCHDOG_TIMEOUT",
                0x1D2, "WORKER_THREAD_INVALID_STATE", 0x1D3, "WFP_INVALID_OPERATION", 0x1D5, "DRIVER_PNP_WATCHDOG",
                0x1D6, "WORKER_THREAD_RETURNED_WITH_NON_DEFAULT_WORKLOAD_CLASS", 0x1D7, "EFS_FATAL_ERROR", 0x1D8, "UCMUCSI_FAILURE",
                0x1D9, "HAL_IOMMU_INTERNAL_ERROR", 0x1DA, "HAL_BLOCKED_PROCESSOR_INTERNAL_ERROR", 0x1DB, "IPI_WATCHDOG_TIMEOUT",
                0x1DC, "DMA_COMMON_BUFFER_VECTOR_ERROR", 0x1DD, "BUGCODE_MBBADAPTER_DRIVER", 0x1DE, "BUGCODE_WIFIADAPTER_DRIVER",
                0x1DF, "PROCESSOR_START_TIMEOUT", 0x1E4, "VIDEO_DXGKRNL_SYSMM_FATAL_ERROR", 0x1E9, "ILLEGAL_ATS_INITIALIZATION",
                0x1EA, "SECURE_PCI_CONFIG_SPACE_ACCESS_VIOLATION", 0x1EB, "DAM_WATCHDOG_TIMEOUT", 0x1ED, "HANDLE_ERROR_ON_CRITICAL_THREAD",
                0xC000021A, "WINLOGON_FATAL_ERROR", 0xC0000221, "STATUS_IMAGE_CHECKSUM_MISMATCH", 0xDEADDEAD, "MANUALLY_INITIATED_CRASH1");
        }

        public static int Count { get { return Map.Count; } }

        private static void Add(uint code, string name, int addressParam, bool hardware, string[] fixes, string fr, string en)
        {
            Map[code] = new BugCheckInfo
            {
                Code = code,
                Name = name,
                AddressParam = addressParam,
                LikelyHardware = hardware,
                FixIds = fixes ?? new string[0],
                ExplanationFr = fr,
                ExplanationEn = en
            };
        }

        private static void Names(params object[] pairs)
        {
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                uint code = Convert.ToUInt32(pairs[i]);
                if (Map.ContainsKey(code)) continue;
                Map[code] = new BugCheckInfo { Code = code, Name = (string)pairs[i + 1], FixIds = new string[0] };
            }
        }
    }
}
