using System;
using System.Security.Cryptography;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>
    /// Encrypts secrets (the AI API key) with Windows DPAPI, bound to the current Windows user:
    /// the settings file only ever contains ciphertext that is useless on another account or PC.
    /// </summary>
    public static class SecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DumpFinder.AiApiKey.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            try
            {
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "DPAPI protect");
                return null;
            }
        }

        public static string Unprotect(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return "";
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(cipher), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "DPAPI unprotect");
                return "";
            }
        }
    }
}
