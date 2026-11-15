using System.Windows;
using System.Windows.Input;

namespace WinCrashDoctor
{
    /// <summary>
    /// Window-wide keyboard shortcuts: Ctrl+1…7 open the sidebar sections, F5 runs a new scan, Esc closes the
    /// crash detail panel. Handled on the bubbling KeyDown, so a control that uses the key first (an open
    /// drop-down list on Esc, a text box) keeps it.
    /// </summary>
    public partial class MainWindow
    {
        private void InstallKeyboardShortcuts()
        {
            KeyDown += OnWindowKeyDown;
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            // Modal overlays (welcome tour, update) handle their own keys.
            if ((_onboardingOverlay != null && _onboardingOverlay.Visibility == Visibility.Visible)
                || (_updateOverlay != null && _updateOverlay.Visibility == Visibility.Visible))
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (HandleShortcut(key, Keyboard.Modifiers)) e.Handled = true;
        }

        /// <summary>Runs the shortcut for this key, if any. True when the key was used.</summary>
        internal bool HandleShortcut(Key key, ModifierKeys mods)
        {
            if (mods == ModifierKeys.Control)
            {
                int tab = -1;
                if (key >= Key.D1 && key <= Key.D7) tab = key - Key.D1;
                else if (key >= Key.NumPad1 && key <= Key.NumPad7) tab = key - Key.NumPad1;
                if (tab >= 0 && tab < NavCount)
                {
                    SwitchTab(tab);
                    // Keyboard users keep a visible focus: on the section they just opened.
                    if (_navButtons[tab] != null) _navButtons[tab].Focus();
                    return true;
                }
                return false;
            }

            if (mods != ModifierKeys.None) return false;
            if (key == Key.F5)
            {
                RunScanAsync();
                return true;
            }
            if (key == Key.Escape && _activeTab == TabCrashes && _crashDrawer != null && _crashDrawer.Visibility == Visibility.Visible)
            {
                CloseCrashDrawer();
                return true;
            }
            return false;
        }

        /// <summary>"Ctrl+3" for the third sidebar entry (shown in its tooltip).</summary>
        private static string NavShortcut(int index)
        {
            return "Ctrl+" + (index + 1);
        }
    }
}
