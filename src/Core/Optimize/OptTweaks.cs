using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;

namespace WinCrashDoctor.Optimize
{
    public enum OptGroup { Ads, Search, Ai, Privacy, Gaming }

    /// <summary>One registry value a setting writes. Default = what "Restore" puts back (null = delete the value).</summary>
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
                Title = () => Localizer.S("opt_tweaks.remove_suggestions_and_recommendations_f"),
                Explanation = () => Localizer.S("opt_tweaks.removes_suggested_apps_and_the_tips"),
                Sets = new[] { Cu(Cdm, "SystemPaneSuggestionsEnabled", 0, 1), Cu(Cdm, "SubscribedContent-338388Enabled", 0, 1), Cu(ExplorerAdv, "Start_IrisRecommendations", 0, 1) }
            },
            new Tweak
            {
                Id = "silent_apps", Group = OptGroup.Ads, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.stop_sponsored_apps_from_installing_them"),
                Explanation = () => Localizer.S("opt_tweaks.windows_sometimes_installs_partner_games"),
                Sets = new[] { Cu(Cdm, "SilentInstalledAppsEnabled", 0, 1), Cu(Cdm, "OemPreInstalledAppsEnabled", 0, 1), Cu(Cdm, "PreInstalledAppsEnabled", 0, 1) }
            },
            new Tweak
            {
                Id = "tips", Group = OptGroup.Ads, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.turn_off_tips_and_finish_setting"),
                Explanation = () => Localizer.S("opt_tweaks.no_more_tip_notifications_and_no"),
                Sets = new[] { Cu(Cdm, "SubscribedContent-338389Enabled", 0, 1), Cu(Cdm, "SoftLandingEnabled", 0, 1), Cu(Cdm, "SubscribedContent-310093Enabled", 0, 1),
                               Cu(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, 1) }
            },
            new Tweak
            {
                Id = "settings_ads", Group = OptGroup.Ads, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.remove_suggested_content_in_settings"),
                Explanation = () => Localizer.S("opt_tweaks.removes_the_promotional_panels_shown_in"),
                Sets = new[] { Cu(Cdm, "SubscribedContent-338393Enabled", 0, 1), Cu(Cdm, "SubscribedContent-353694Enabled", 0, 1), Cu(Cdm, "SubscribedContent-353696Enabled", 0, 1) }
            },
            new Tweak
            {
                Id = "lockscreen_ads", Group = OptGroup.Ads, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.remove_fun_facts_and_tips_from"),
                Explanation = () => Localizer.S("opt_tweaks.keeps_your_lock_screen_picture_without"),
                Sets = new[] { Cu(Cdm, "RotatingLockScreenOverlayEnabled", 0, 1), Cu(Cdm, "SubscribedContent-338387Enabled", 0, 1) }
            },
            new Tweak
            {
                Id = "explorer_ads", Group = OptGroup.Ads, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.remove_onedrive_microsoft_365_ads_from"),
                Explanation = () => Localizer.S("opt_tweaks.turns_off_the_sync_provider_notification"),
                Sets = new[] { Cu(ExplorerAdv, "ShowSyncProviderNotifications", 0, 1) }
            },
            new Tweak
            {
                Id = "ad_id", Group = OptGroup.Ads, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.turn_off_the_advertising_id"),
                Explanation = () => Localizer.S("opt_tweaks.apps_can_no_longer_use_this"),
                Sets = new[] { Cu(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1) }
            },

            // ---------------- Search & widgets
            new Tweak
            {
                Id = "bing_search", Group = OptGroup.Search, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.remove_bing_results_from_start_search"),
                Explanation = () => Localizer.S("opt_tweaks.search_only_looks_on_the_pc"),
                Sets = new[] { CuPolicy(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1), CuPolicy(@"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0) }
            },
            new Tweak
            {
                Id = "widgets", Group = OptGroup.Search, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.turn_off_widgets_taskbar_news_and"),
                Explanation = () => Localizer.S("opt_tweaks.removes_the_widgets_board_and_the"),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0) }
            },

            // ---------------- Copilot & AI
            new Tweak
            {
                Id = "copilot", Group = OptGroup.Ai, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.turn_off_windows_built_in_copilot"),
                Explanation = () => Localizer.S("opt_tweaks.turn_off_windows_copilot_policy_the"),
                Sets = new[] { CuPolicy(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1) }
            },
            new Tweak
            {
                Id = "recall", Group = OptGroup.Ai, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.turn_off_recall_and_snapshot_analysis"),
                Explanation = () => Localizer.S("opt_tweaks.no_effect_on_a_pc_without"),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1), Lm(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0) }
            },
            new Tweak
            {
                Id = "click_to_do", Group = OptGroup.Ai, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.turn_off_click_to_do"),
                Explanation = () => Localizer.S("opt_tweaks.removes_the_ai_actions_offered_on"),
                Sets = new[] { CuPolicy(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableClickToDo", 1) }
            },

            // ---------------- Privacy
            new Tweak
            {
                Id = "telemetry", Group = OptGroup.Privacy, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.telemetry_at_the_allowed_minimum_require"),
                Explanation = () => Localizer.S("opt_tweaks.official_allow_diagnostic_data_policy_se"),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1) }
            },
            new Tweak
            {
                Id = "tailored", Group = OptGroup.Privacy, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.turn_off_tailored_experiences"),
                Explanation = () => Localizer.S("opt_tweaks.microsoft_no_longer_uses_your_diagnostic"),
                Sets = new[] { Cu(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1) }
            },
            new Tweak
            {
                Id = "feedback", Group = OptGroup.Privacy, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.stop_feedback_requests"),
                Explanation = () => Localizer.S("opt_tweaks.windows_stops_interrupting_you_to_rate"),
                Sets = new[] { CuPolicy(@"Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0) }
            },
            new Tweak
            {
                Id = "activity", Group = OptGroup.Privacy, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.turn_off_activity_history"),
                Explanation = () => Localizer.S("opt_tweaks.windows_stops_recording_the_apps_files"),
                Sets = new[] { Lm(@"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0), Lm(@"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0) }
            },

            // ---------------- Gaming
            new Tweak
            {
                Id = "game_mode", Group = OptGroup.Gaming, Recommended = true,
                Title = () => Localizer.S("opt_tweaks.keep_game_mode_on"),
                Explanation = () => Localizer.S("opt_tweaks.game_mode_prioritizes_the_game_and"),
                Sets = new[] { Cu(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, 1, 1) }
            },
            new Tweak
            {
                Id = "mouse_accel", Group = OptGroup.Gaming, Recommended = false,
                Title = () => Localizer.S("opt_tweaks.turn_off_mouse_acceleration_enhance_poin"),
                Explanation = () => Localizer.S("opt_tweaks.the_cursor_moves_the_same_distance"),
                Sets = new[] { Cu(@"Control Panel\Mouse", "MouseSpeed", "0", "1"), Cu(@"Control Panel\Mouse", "MouseThreshold1", "0", "6"), Cu(@"Control Panel\Mouse", "MouseThreshold2", "0", "10") }
            }
        };

        public static string GroupName(OptGroup g)
        {
            switch (g)
            {
                case OptGroup.Ads: return Localizer.S("opt_tweaks.ads_and_suggestions");
                case OptGroup.Search: return Localizer.S("opt_tweaks.search_and_widgets");
                case OptGroup.Ai: return Localizer.S("opt_tweaks.copilot_and_ai");
                case OptGroup.Privacy: return Localizer.S("opt_tweaks.privacy");
                default: return Localizer.S("opt_tweaks.gaming");
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
                    VendorTag = t.Admin ? Localizer.S("opt_tweaks.admin") : "",
                    HardwareTarget = "ALL",
                    Title = t.Title(),
                    Explanation = t.Explanation(),
                    RegPathDisplay = string.Join("\n", t.Sets.Select(s => s.Display)),
                    CurrentValue = applied ? Localizer.S("opt_tweaks.applied")
                                           : Localizer.S("opt_tweaks.windows_default"),
                    ApplyBtn = Localizer.S("opt_tweaks.apply"),
                    RestoreBtn = Localizer.S("opt_tweaks.restore"),
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
