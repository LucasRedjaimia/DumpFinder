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
                    Title = Localizer.S("knowledge_base.amd_driver_timeout_driver_timeout_has"),
                    Symptoms = Localizer.S("knowledge_base.3_10s_black_screen_amd_bug"),
                    Cause = Localizer.S("knowledge_base.top_3_causes_1_windows_update"),
                    Solution = Localizer.S("knowledge_base.in_fixes_lock_gpu_driver_updates")
                },
                new KnowledgeEntry
                {
                    Tag = "RADEON RX 7000 • RDNA 3",
                    BadgeColorHex = "#F43F5E",
                    HardwareTarget = "AMD_GPU",
                    Title = Localizer.S("knowledge_base.boot_grey_screen_in_game_adrenalin"),
                    Symptoms = Localizer.S("knowledge_base.solid_grey_screen_when_booting_into"),
                    Cause = Localizer.S("knowledge_base.at_boot_windows_fast_startup_ulps"),
                    Solution = Localizer.S("knowledge_base.1_in_fixes_disable_fast_startup")
                },
                new KnowledgeEntry
                {
                    Tag = "NVIDIA • 0x116",
                    BadgeColorHex = "#10B981",
                    HardwareTarget = "NVIDIA_GPU",
                    Title = Localizer.S("knowledge_base.black_screen_100_gpu_fans_reboot"),
                    Symptoms = Localizer.S("knowledge_base.sudden_loss_of_display_signal_gpu"),
                    Cause = Localizer.S("knowledge_base.during_chrome_youtube_video_playback_ski"),
                    Solution = Localizer.S("knowledge_base.disable_mpo_skia_graphite_set_tdrdelay")
                },
                new KnowledgeEntry
                {
                    Tag = "SSD NVMe • WIN11",
                    BadgeColorHex = "#3B82F6",
                    HardwareTarget = "ALL",
                    Title = Localizer.S("knowledge_base.total_system_freezes_nvme_ssd_dropouts"),
                    Symptoms = Localizer.S("knowledge_base.system_hangs_for_10_30_seconds"),
                    Cause = Localizer.S("knowledge_base.host_memory_buffer_hmb_allocation_bug"),
                    Solution = Localizer.S("knowledge_base.apply_the_nvme_hmb_stabilization_and")
                },
                new KnowledgeEntry
                {
                    Tag = "AMD RYZEN • WHEA 18",
                    BadgeColorHex = "#F59E0B",
                    HardwareTarget = "AMD_CPU",
                    Title = Localizer.S("knowledge_base.instant_idle_reboot_on_amd_ryzen"),
                    Symptoms = Localizer.S("knowledge_base.pc_reboots_abruptly_with_no_bsod"),
                    Cause = Localizer.S("knowledge_base.insufficient_cpu_core_voltage_during_idl"),
                    Solution = Localizer.S("knowledge_base.in_bios_relax_curve_optimizer_e")
                },
                new KnowledgeEntry
                {
                    Tag = "INTEL 13/14 GEN",
                    BadgeColorHex = "#8B5CF6",
                    HardwareTarget = "INTEL_CPU",
                    Title = Localizer.S("knowledge_base.out_of_video_memory_shader_compilation"),
                    Symptoms = Localizer.S("knowledge_base.unreal_engine_5_games_crash_during"),
                    Cause = Localizer.S("knowledge_base.cpu_instability_vmin_shift_unlocked_moth"),
                    Solution = Localizer.S("knowledge_base.update_motherboard_bios_microcode_0x12f")
                },
                new KnowledgeEntry
                {
                    Tag = "ITX / RISER PCIe",
                    BadgeColorHex = "#EC4899",
                    HardwareTarget = "ALL",
                    Title = Localizer.S("knowledge_base.stutters_usb_audio_crackles_pcie_drops"),
                    Symptoms = Localizer.S("knowledge_base.floods_of_whea_event_17_warnings"),
                    Cause = Localizer.S("knowledge_base.signal_loss_across_a_pcie_riser"),
                    Solution = Localizer.S("knowledge_base.disable_pcie_aspm_in_fixes_and")
                },
                new KnowledgeEntry
                {
                    Tag = "RAM & PAGEFILE",
                    BadgeColorHex = "#06B6D4",
                    HardwareTarget = "ALL",
                    Title = Localizer.S("knowledge_base.random_crashes_to_desktop_memory_bsods"),
                    Symptoms = Localizer.S("knowledge_base.games_closing_silently_after_20_40"),
                    Cause = Localizer.S("knowledge_base.manually_disabled_windows_pagefile_or_dd"),
                    Solution = Localizer.S("knowledge_base.enable_automatic_windows_pagefile_and_ru")
                },
                new KnowledgeEntry
                {
                    Tag = "PSU • KERNEL-POWER 41",
                    BadgeColorHex = "#F97316",
                    HardwareTarget = "ALL",
                    Title = Localizer.S("knowledge_base.instant_power_off_under_heavy_3d"),
                    Symptoms = Localizer.S("knowledge_base.pc_clicks_off_like_a_power"),
                    Cause = Localizer.S("knowledge_base.psu_protection_trip_ocp_opp_from"),
                    Solution = Localizer.S("knowledge_base.check_gpu_power_plug_seating_use")
                },
                new KnowledgeEntry
                {
                    Tag = "INTEL ARC • ReBAR",
                    BadgeColorHex = "#0EA5E9",
                    HardwareTarget = "INTEL_ARC",
                    Title = Localizer.S("knowledge_base.idle_black_screens_severe_stutters_on"),
                    Symptoms = Localizer.S("knowledge_base.black_screen_when_waking_displays_igdkmd"),
                    Cause = Localizer.S("knowledge_base.intel_arc_hardware_strictly_requires_res"),
                    Solution = Localizer.S("knowledge_base.enable_above_4g_decoding_resizable_bar")
                },
                new KnowledgeEntry
                {
                    Tag = "AM5 DDR5 • MCR",
                    BadgeColorHex = "#EAB308",
                    HardwareTarget = "AMD_CPU",
                    Title = Localizer.S("knowledge_base.login_bsod_memory_management_0x1a_on"),
                    Symptoms = Localizer.S("knowledge_base.0x1a_or_0x0a_bsod_within_60"),
                    Cause = Localizer.S("knowledge_base.memory_context_restore_is_enabled_in"),
                    Solution = Localizer.S("knowledge_base.in_bios_explicitly_set_both_memory")
                },
                new KnowledgeEntry
                {
                    Tag = "LAN / WIFI • ANTI-CHEAT",
                    BadgeColorHex = "#14B8A6",
                    HardwareTarget = "ALL",
                    Title = Localizer.S("knowledge_base.realtek_2_5gbe_mediatek_rz616_freezes"),
                    Symptoms = Localizer.S("knowledge_base.micro_freezes_with_ethernet_bluetooth_dr"),
                    Cause = Localizer.S("knowledge_base.buggy_eee_green_ethernet_on_realtek"),
                    Solution = Localizer.S("knowledge_base.apply_the_disable_2_5gbe_eee")
                },

                // ---------------- Detection v2 (2025–2026)
                new KnowledgeEntry
                {
                    Tag = "RGB • INPOUTX64",
                    BadgeColorHex = "#F59E0B",
                    HardwareTarget = "ALL",
                    Title = Localizer.S("knowledge_base.games_freezing_closing_or_rebooting_the"),
                    Symptoms = Localizer.S("knowledge_base.since_the_august_2026_update_kb5121003"),
                    Cause = Localizer.S("knowledge_base.microsoft_confirmed_the_cause_is_the"),
                    Solution = Localizer.S("knowledge_base.install_the_latest_windows_updates_if")
                },
                new KnowledgeEntry
                {
                    Tag = "RTX 50 • BLACKWELL",
                    BadgeColorHex = "#76B900",
                    HardwareTarget = "NVIDIA_GPU",
                    Title = Localizer.S("knowledge_base.rtx_50_black_screens_pulled_drivers"),
                    Symptoms = Localizer.S("knowledge_base.black_screen_with_fans_ramping_up"),
                    Cause = Localizer.S("knowledge_base.three_leads_1_faulty_driver_releases"),
                    Solution = Localizer.S("knowledge_base.check_the_driver_version_under_hardware")
                },
                new KnowledgeEntry
                {
                    Tag = "RYZEN 9000 • AM5",
                    BadgeColorHex = "#EF4444",
                    HardwareTarget = "AMD_CPU",
                    Title = Localizer.S("knowledge_base.ryzen_9000_9800x3d_instability_expo_and"),
                    Symptoms = Localizer.S("knowledge_base.pc_that_no_longer_boots_or"),
                    Cause = Localizer.S("knowledge_base.expo_profiles_too_ambitious_for_the"),
                    Solution = Localizer.S("knowledge_base.update_the_motherboard_bios_test_with")
                },
                new KnowledgeEntry
                {
                    Tag = "DUMPS",
                    BadgeColorHex = "#6366F1",
                    HardwareTarget = "ALL",
                    Title = Localizer.S("knowledge_base.blue_screen_with_no_minidump_kernel"),
                    Symptoms = Localizer.S("knowledge_base.the_pc_restarts_or_shows_a"),
                    Cause = Localizer.S("knowledge_base.windows_writes_the_dump_through_the"),
                    Solution = Localizer.S("knowledge_base.under_hardware_conflicts_check_blue_scre")
                }
            };
        }
    }
}
