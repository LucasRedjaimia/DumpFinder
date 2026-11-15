using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>
    /// --parse-selftest &lt;out.txt&gt;: robustness of the dump parsers on malformed input (truncated, mutated,
    /// out-of-range offsets). Crash dumps come from crashing machines: a corrupt file must give "unknown", never an
    /// exception or a hang. Uses synthetic files in a temp folder only; reads no real dump. Exit code = failures.
    /// </summary>
    internal static class ParseSelfTest
    {
        private const int Mutations = 400;

        public static int Run(string outPath)
        {
            Localizer.ReadOnlySettings = true;
            Localizer.Current = AppLang.EN;
            var sb = new StringBuilder();
            int bad = 0;
            Action<string, bool> expect = (label, ok) => { if (!ok) bad++; sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + label); };

            string dir = Path.Combine(Path.GetTempPath(), "dumpfinder_parse_selftest_" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(dir);
            try
            {
                // ---- Valid headers are still understood
                byte[] k32 = Kernel32Header(0x116);
                MinidumpReportItem r32 = Analyze(dir, "k32.dmp", k32);
                expect("PAGEDUMP 32 bits : code d'arrêt lu à 0x28 (0x116)", r32 != null && r32.BugCheckCode == 0x116);

                byte[] k64 = Kernel64Header(0xEF, 1024 * 16);
                MinidumpReportItem r64 = Analyze(dir, "k64.dmp", k64);
                expect("PAGEDU64 : code d'arrêt lu (0xEF)", r64 != null && r64.BugCheckCode == 0xEF);

                byte[] user = UserMdmp(0xC0000005);
                MinidumpReportItem ru = Analyze(dir, "user.dmp", user);
                expect("MDMP : rapport produit pour un dump applicatif minimal", ru != null);

                // ---- Hostile offsets (uint wrap-around used to pass the bounds checks)
                byte[] wrap = UserMdmp(0xC0000005);
                PutU32(wrap, 8, 10);           // stream count
                PutU32(wrap, 12, 0xFFFFFFF8);  // directory RVA near uint.MaxValue
                expect("MDMP : RVA de répertoire 0xFFFFFFF8 sans exception", NoThrow(() => Analyze(dir, "wrap.dmp", wrap)));

                byte[] badStream = UserMdmp(0xC0000005);
                PutU32(badStream, 0x20 + 8, 0xFFFFFFF0); // exception stream RVA
                PutU32(badStream, 0x2C + 8, 0xFFFFFFFC); // module list RVA
                expect("MDMP : RVA de flux hors fichier sans exception", NoThrow(() => Analyze(dir, "stream.dmp", badStream)));

                byte[] hugeMods = UserMdmp(0xC0000005);
                AddModuleList(hugeMods, uint.MaxValue, 0xFFFFFFF0);
                expect("MDMP : liste de modules énorme / nom hors fichier sans exception", NoThrow(() => Analyze(dir, "mods.dmp", hugeMods)));

                byte[] triage = Kernel64Header(0x50, 0x2000 + 0x400);
                PutU32(triage, 0xF98, 4);                 // triage dump
                PutU32(triage, 0x2000 + 0x30, 0x2100);     // driver list offset
                PutU32(triage, 0x2000 + 0x34, 4096);       // driver count (max accepted)
                PutU32(triage, 0x2000 + 0x38, 0xFFFFFFF0); // string pool offset beyond file
                expect("PAGEDU64 triage : tables hors fichier sans exception", NoThrow(() => KernelDumpParser.Parse(triage)));

                // ---- Truncated files at every interesting length
                int truncFails = 0;
                foreach (byte[] src in new[] { k32, k64, user })
                {
                    foreach (int len in new[] { 0, 1, 7, 8, 63, 64, 65, 0x40, 0x80, 0x100, 0x1000, 0x2000, 0x2080 })
                    {
                        if (len > src.Length) continue;
                        byte[] t = new byte[len];
                        Array.Copy(src, t, len);
                        if (!NoThrow(() => { if (len > 0) Analyze(dir, "trunc.dmp", t); KernelDumpParser.Parse(t); })) truncFails++;
                    }
                }
                expect("fichiers tronqués (39 longueurs) sans exception", truncFails == 0);

                // ---- Random mutations (fixed seed: reproducible)
                var rnd = new Random(20261115);
                int mutFails = 0;
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < Mutations; i++)
                {
                    byte[] baseBuf = i % 3 == 0 ? k32 : i % 3 == 1 ? k64 : user;
                    byte[] m = (byte[])baseBuf.Clone();
                    int edits = 1 + rnd.Next(24);
                    for (int e = 0; e < edits; e++)
                    {
                        int pos = rnd.Next(m.Length);
                        if (rnd.Next(3) == 0 && pos + 4 <= m.Length)
                            PutU32(m, pos, rnd.Next(2) == 0 ? 0xFFFFFFFF - (uint)rnd.Next(64) : (uint)rnd.Next());
                        else
                            m[pos] = (byte)rnd.Next(256);
                    }
                    if (!NoThrow(() => { Analyze(dir, "mut.dmp", m); KernelDumpParser.Parse(m); }))
                    {
                        mutFails++;
                        if (mutFails <= 3) sb.AppendLine("       mutation #" + i + " a levé une exception");
                    }
                }
                expect(Mutations + " mutations aléatoires sans exception (" + sw.ElapsedMilliseconds + " ms)", mutFails == 0);
                expect("analyse rapide : moins de 30 s pour toutes les mutations", sw.Elapsed.TotalSeconds < 30);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception ex) { Log.Warn(ex, dir); }
            }

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "TOUT EST CONFORME" : bad + " ÉCART(S)");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return bad;
        }

        private static MinidumpReportItem Analyze(string dir, string name, byte[] buf)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, buf);
            return MinidumpAnalyzer.AnalyzeBuffer(new FileInfo(path), buf);
        }

        private static bool NoThrow(Action a)
        {
            try { a(); return true; }
            catch (Exception ex) { Log.Warn(ex, "parse selftest"); return false; }
        }

        // ---------- Synthetic files

        /// <summary>DUMP_HEADER32 ("PAGEDUMP"), BugCheckCode at 0x28, padded to 4 KB.</summary>
        private static byte[] Kernel32Header(uint bugCheck)
        {
            var b = new byte[0x1000];
            Encoding.ASCII.GetBytes("PAGEDUMP").CopyTo(b, 0);
            PutU32(b, 0x08, 15);     // major
            PutU32(b, 0x0C, 19041);  // minor = build
            PutU32(b, 0x24, 8);      // NumberProcessors
            PutU32(b, 0x28, bugCheck);
            return b;
        }

        /// <summary>DUMP_HEADER64 ("PAGEDU64"), BugCheckCode at 0x38, header 0x2000.</summary>
        private static byte[] Kernel64Header(uint bugCheck, int size)
        {
            var b = new byte[Math.Max(size, 0x2000 + 0x100)];
            Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(b, 0);
            PutU32(b, 0x08, 15);
            PutU32(b, 0x0C, 26100);
            PutU32(b, 0x38, bugCheck);
            PutU32(b, 0xF98, 1); // full dump type: no triage parsing
            return b;
        }

        /// <summary>Minimal user-mode minidump: header + 2 directory entries (exception stream, empty module list).</summary>
        private static byte[] UserMdmp(uint exceptionCode)
        {
            var b = new byte[0x400];
            PutU32(b, 0, 0x504D444D); // 'MDMP'
            PutU32(b, 4, 0xA793);     // version
            PutU32(b, 8, 2);          // streams
            PutU32(b, 12, 0x20);      // directory RVA
            PutU32(b, 20, 1763200000); // timestamp (2025)
            // directory entry 0: ExceptionStream (6) at 0x100
            PutU32(b, 0x20, 6); PutU32(b, 0x24, 0xA8); PutU32(b, 0x28, 0x100);
            // directory entry 1: ModuleListStream (4) at 0x200
            PutU32(b, 0x2C, 4); PutU32(b, 0x30, 4); PutU32(b, 0x34, 0x200);
            PutU32(b, 0x100 + 8, exceptionCode);
            PutU32(b, 0x200, 0); // 0 modules
            return b;
        }

        private static void AddModuleList(byte[] b, uint count, uint nameRva)
        {
            PutU32(b, 0x200, count);
            // first MINIDUMP_MODULE (108 bytes) at 0x204, ModuleNameRva at +20
            PutU32(b, 0x204 + 20, nameRva);
        }

        private static void PutU32(byte[] b, int off, uint v)
        {
            if (off < 0 || off + 4 > b.Length) return;
            BitConverter.GetBytes(v).CopyTo(b, off);
        }
    }
}
