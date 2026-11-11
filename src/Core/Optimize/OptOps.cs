namespace WinCrashDoctor.Optimize
{
    /// <summary>Single entry point for Optimiser operations inside the fix pipeline (elevation, journal, undo).</summary>
    public static class OptOps
    {
        public static bool IsOp(string op) { return OptTweaks.IsOp(op) || OptStartup.IsOp(op) || OptCleanup.IsOp(op); }

        public static bool NeedsAdmin(string op) { return OptTweaks.NeedsAdmin(op) || OptStartup.NeedsAdmin(op) || OptCleanup.NeedsAdmin(op); }

        public static bool Execute(string op, out string messageFr, out string messageEn)
        {
            if (OptStartup.IsOp(op)) return OptStartup.Execute(op, out messageFr, out messageEn);
            if (OptCleanup.IsOp(op)) return OptCleanup.Execute(op, out messageFr, out messageEn);
            return OptTweaks.Execute(op, out messageFr, out messageEn);
        }

        public static string Title(string op)
        {
            if (OptStartup.IsOp(op)) return OptStartup.Title(op);
            if (OptCleanup.IsOp(op)) return OptCleanup.Title(op);
            return OptTweaks.Title(op);
        }

        /// <summary>Registry keys the journal may restore for Optimiser changes.</summary>
        public static bool IsAllowedKey(string keyPath) { return OptTweaks.IsTweakKey(keyPath) || OptStartup.IsApprovedKey(keyPath); }
    }
}
