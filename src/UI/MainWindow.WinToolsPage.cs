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
                SetToast(Localizer.S("win_tools.launched_tool") + exe + (string.IsNullOrEmpty(arg) ? "" : (" " + arg)), "AccentText");
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
                SetToast(Localizer.S("win_tools.sfc_dism_repair_console_launched_as"), "SuccessFg");
            else
                SetToast(Localizer.S("main_window.elevation_cancelled"), "WarnFg");
        }

        private void RefreshWinToolsPageLabels()
        {
            if (_lblWinToolsMainTitle == null) return;

            _lblWinToolsMainTitle.Text = Localizer.S("win_tools.windows_diagnostic_tools");
            _lblWinToolsMainSub.Text = Localizer.S("win_tools.direct_one_click_access_to_advanced");

            _btnQuickSfcAdmin.Content = Localizer.S("win_tools.sfc_dism_repair_admin");

            // Category Headers
            _lblWinToolCatHeaders[0].Text = Localizer.S("win_tools.1_crash_logs_reliability_history");
            _lblWinToolCatHeaders[1].Text = Localizer.S("win_tools.2_hardware_diagnostics_bios_drivers");
            _lblWinToolCatHeaders[2].Text = Localizer.S("win_tools.3_real_time_resources_storage_management");
            _lblWinToolCatHeaders[3].Text = Localizer.S("win_tools.4_system_integrity_disk_cleanup");

            // Tool Items
            // 0: Reliability Monitor
            _lblWinToolItemTitles[0].Text = Localizer.S("win_tools.reliability_monitor_perfmon_rel");
            _lblWinToolItemDescs[0].Text = Localizer.S("win_tools.visual_daily_stability_curve_1_10");

            // 1: Event Viewer
            _lblWinToolItemTitles[1].Text = Localizer.S("win_tools.event_viewer_eventvwr_msc");
            _lblWinToolItemDescs[1].Text = Localizer.S("win_tools.detailed_query_of_windows_system_applica");

            // 2: Device Manager
            _lblWinToolItemTitles[2].Text = Localizer.S("win_tools.device_manager_devmgmt_msc");
            _lblWinToolItemDescs[2].Text = Localizer.S("win_tools.inspect_hardware_devices_in_error_states");

            // 3: Windows Memory Diagnostic
            _lblWinToolItemTitles[3].Text = Localizer.S("win_tools.windows_memory_diagnostic_mdsched");
            _lblWinToolItemDescs[3].Text = Localizer.S("win_tools.schedules_a_low_level_bare_metal");

            // 4: System Information
            _lblWinToolItemTitles[4].Text = Localizer.S("win_tools.system_information_msinfo32");
            _lblWinToolItemDescs[4].Text = Localizer.S("win_tools.full_hardware_brief_bios_revision_mother");

            // 5: DirectX Diagnostic
            _lblWinToolItemTitles[5].Text = Localizer.S("win_tools.directx_diagnostic_tool_dxdiag");
            _lblWinToolItemDescs[5].Text = Localizer.S("win_tools.inspect_direct3d_feature_levels_wddm_dri");

            // 6: Resource Monitor
            _lblWinToolItemTitles[6].Text = Localizer.S("win_tools.resource_monitor_resmon_exe");
            _lblWinToolItemDescs[6].Text = Localizer.S("win_tools.real_time_inspection_of_hung_cpu");

            // 7: Disk Management
            _lblWinToolItemTitles[7].Text = Localizer.S("win_tools.disk_management_diskmgmt_msc");
            _lblWinToolItemDescs[7].Text = Localizer.S("win_tools.partition_layout_gpt_mbr_drive_letters");

            // 8: SFC & DISM Console
            _lblWinToolItemTitles[8].Text = Localizer.S("win_tools.sfc_dism_integrity_console");
            _lblWinToolItemDescs[8].Text = Localizer.S("win_tools.opens_an_elevated_console_executing_sfc");

            // 9: Disk Cleanup
            _lblWinToolItemTitles[9].Text = Localizer.S("win_tools.disk_cleanup_cleanmgr_exe");
            _lblWinToolItemDescs[9].Text = Localizer.S("win_tools.purges_old_memory_crash_dumps_temporary");

            string openBtnText = Localizer.S("win_tools.launch");
            for (int i = 0; i < 10; i++)
            {
                if (_btnWinToolLaunch[i] != null)
                    _btnWinToolLaunch[i].Content = openBtnText;
            }
        }
    }
}
