using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace WinCrashDoctor.Optimize
{
    /// <summary>A program launched at sign-in (Run keys or Startup folders).</summary>
    public sealed class StartupEntry
    {
        public string Scope;      // cu, lm, lm32, fu, fc
        public string Name;       // Run value name, or shortcut file name
        public string Command;
        public string FilePath;
        public string Publisher;
        public bool Enabled;
        public bool Admin { get { return Scope != "cu" && Scope != "fu"; } }

        public string SourceLabel
        {
            get
            {
                switch (Scope)
                {
                    case "cu": return Localizer.L("Registre (utilisateur)", "Registry (user)", "Registro (usuario)", "Registry (Benutzer)", "Registro (utente)", "Registro (usuário)");
                    case "lm": case "lm32": return Localizer.L("Registre (tous les utilisateurs)", "Registry (all users)", "Registro (todos)", "Registry (alle Benutzer)", "Registro (tutti)", "Registro (todos)");
                    case "fu": return Localizer.L("Dossier Démarrage", "Startup folder", "Carpeta Inicio", "Autostart-Ordner", "Cartella Esecuzione automatica", "Pasta Inicializar");
                    default: return Localizer.L("Dossier Démarrage commun", "Common Startup folder", "Carpeta Inicio común", "Gemeinsamer Autostart-Ordner", "Cartella Esecuzione comune", "Pasta Inicializar comum");
                }
            }
        }

        /// <summary>optst_&lt;scope&gt;_&lt;base64url(name)&gt;_apply (disable) / _restore (enable).</summary>
        public string Op(bool disable)
        {
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Name)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return OptStartup.Prefix + Scope + "_" + b64 + (disable ? "_apply" : "_restore");
        }
    }

    /// <summary>
    /// Startup programs, toggled the way Task Manager does it: an entry under Explorer\StartupApproved marks it
    /// disabled. Nothing is deleted, and ChangeRecorder lets the journal put the exact previous value back.
    /// </summary>
    public static class OptStartup
    {
        public const string Prefix = "optst_";
        private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        private sealed class Source
        {
            public string Scope;
            public RegistryHive Hive;
            public string RunKey;          // null for folders
            public Environment.SpecialFolder Folder;
            public string ApprovedSub;
        }

        private static readonly Source[] Sources =
        {
            new Source { Scope = "cu", Hive = RegistryHive.CurrentUser, RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ApprovedSub = Approved + "Run" },
            new Source { Scope = "lm", Hive = RegistryHive.LocalMachine, RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", ApprovedSub = Approved + "Run" },
            new Source { Scope = "lm32", Hive = RegistryHive.LocalMachine, RunKey = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", ApprovedSub = Approved + "Run32" },
            new Source { Scope = "fu", Hive = RegistryHive.CurrentUser, Folder = Environment.SpecialFolder.Startup, ApprovedSub = Approved + "StartupFolder" },
            new Source { Scope = "fc", Hive = RegistryHive.LocalMachine, Folder = Environment.SpecialFolder.CommonStartup, ApprovedSub = Approved + "StartupFolder" }
        };

        public static List<StartupEntry> Scan()
        {
            var list = new List<StartupEntry>();
            foreach (Source src in Sources)
            {
                try
                {
                    if (src.RunKey != null)
                    {
                        using (var bk = RegistryKey.OpenBaseKey(src.Hive, RegistryView.Registry64))
                        using (var k = bk.OpenSubKey(src.RunKey))
                        {
                            if (k == null) continue;
                            foreach (string name in k.GetValueNames())
                            {
                                if (string.IsNullOrEmpty(name)) continue;
                                string cmd = Convert.ToString(k.GetValue(name) ?? "");
                                list.Add(Make(src, name, cmd));
                            }
                        }
                    }
                    else
                    {
                        string dir = Environment.GetFolderPath(src.Folder);
                        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                        foreach (string f in Directory.GetFiles(dir))
                        {
                            string fn = Path.GetFileName(f);
                            if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                            string target = fn.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(f) : f;
                            list.Add(Make(src, fn, target ?? f));
                        }
                    }
                }
                catch (Exception ex) { Log.Warn(ex, src.Scope); }
            }
            return list.OrderBy(e => !e.Enabled).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static StartupEntry Make(Source src, string name, string cmd)
        {
            var e = new StartupEntry { Scope = src.Scope, Name = name, Command = cmd, FilePath = ExtractPath(cmd), Enabled = IsEnabled(src, name) };
            try
            {
                if (e.FilePath != null && File.Exists(e.FilePath))
                {
                    var vi = FileVersionInfo.GetVersionInfo(e.FilePath);
                    e.Publisher = !string.IsNullOrWhiteSpace(vi.CompanyName) ? vi.CompanyName.Trim() : null;
                }
            }
            catch { }
            return e;
        }

        /// <summary>Executable part of a command line ("C:\x\a.exe" -min, C:\x\a.exe /b, rundll32 …).</summary>
        private static string ExtractPath(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\"")) { int end = cmd.IndexOf('"', 1); return end > 1 ? cmd.Substring(1, end - 1) : null; }
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? cmd.Substring(0, exe + 4) : cmd.Split(' ')[0];
        }

        private static string ShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                string target = (string)sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                return string.IsNullOrEmpty(target) ? null : target;
            }
            catch { return null; }
        }

        /// <summary>No StartupApproved value, or an even first byte (02 / 06) = enabled; odd (03) = disabled.</summary>
        private static bool IsEnabled(Source src, string name)
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(src.Hive, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(src.ApprovedSub))
                {
                    var data = k != null ? k.GetValue(name) as byte[] : null;
                    return data == null || data.Length == 0 || (data[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        // ==================== Operations ====================
        private static StartupEntry FromOp(string op, out bool disable)
        {
            disable = op != null && op.EndsWith("_apply", StringComparison.Ordinal);
            if (op == null || !op.StartsWith(Prefix, StringComparison.Ordinal) || (!disable && !op.EndsWith("_restore", StringComparison.Ordinal))) return null;
            string body = op.Substring(Prefix.Length, op.Length - Prefix.Length - (disable ? 6 : 8));
            int sep = body.IndexOf('_');
            if (sep <= 0) return null;
            string scope = body.Substring(0, sep);
            string b64 = body.Substring(sep + 1).Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            string name;
            try { name = Encoding.UTF8.GetString(Convert.FromBase64String(b64)); } catch { return null; }
            // Only an entry that really exists right now can be toggled.
            return Scan().FirstOrDefault(e => e.Scope == scope && e.Name == name);
        }

        public static bool IsOp(string op) { return op != null && op.StartsWith(Prefix, StringComparison.Ordinal); }

        public static bool NeedsAdmin(string op)
        {
            if (!IsOp(op)) return false;
            string body = op.Substring(Prefix.Length);
            return !(body.StartsWith("cu_") || body.StartsWith("fu_"));
        }

        public static string Title(string op)
        {
            bool disable;
            StartupEntry e = FromOp(op, out disable);
            if (e == null) return null;
            return (disable ? Localizer.L("Démarrage automatique désactivé : ", "Startup disabled: ", "Inicio automático desactivado: ", "Autostart deaktiviert: ", "Avvio automatico disattivato: ", "Inicialização desativada: ")
                            : Localizer.L("Démarrage automatique réactivé : ", "Startup re-enabled: ", "Inicio automático reactivado: ", "Autostart aktiviert: ", "Avvio automatico riattivato: ", "Inicialização reativada: ")) + e.Name;
        }

        public static bool Execute(string op, out string messageFr, out string messageEn)
        {
            bool disable;
            StartupEntry e = FromOp(op, out disable);
            if (e == null) { messageFr = "Programme de démarrage introuvable."; messageEn = "Startup entry not found."; return false; }
            Source src = Sources.First(s => s.Scope == e.Scope);
            ChangeRecorder.Registry(src.Hive, src.ApprovedSub, e.Name);
            var data = new byte[12];
            data[0] = (byte)(disable ? 0x03 : 0x02);
            if (disable) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            using (var bk = RegistryKey.OpenBaseKey(src.Hive, RegistryView.Registry64))
            using (var k = bk.CreateSubKey(src.ApprovedSub))
                k.SetValue(e.Name, data, RegistryValueKind.Binary);
            messageFr = (disable ? "✓ Ne se lancera plus au démarrage : " : "Se relancera au démarrage : ") + e.Name;
            messageEn = (disable ? "✓ Will no longer start with Windows: " : "Will start with Windows again: ") + e.Name;
            return true;
        }

        public static bool IsApprovedKey(string keyPath)
        {
            if (keyPath == null) return false;
            foreach (string hive in new[] { "HKEY_CURRENT_USER\\", "HKEY_LOCAL_MACHINE\\" })
                foreach (string leaf in new[] { "Run", "Run32", "StartupFolder" })
                    if (string.Equals(keyPath, hive + Approved + leaf, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
