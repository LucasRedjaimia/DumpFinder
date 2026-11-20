using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            sb.AppendLine("=== Courbe GPU (Afterburner) ===");
            try { GpuCurveChecks(sb, expect); }
            catch (Exception ex) { expect("courbe GPU : " + ex, false); }

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "TOUT EST CONFORME" : bad + " ÉCART(S)");
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return bad;
        }

        // ==================== GPU curve ====================
        /// <summary>
        /// A typical undervolt saved by Afterburner (stock 2610 MHz at 950 mV): stock up to 885 mV, a climb to 2925 MHz
        /// (+315) at 950 mV with two points pushed harder than that (+415 at 915–920 mV), a flat plateau, and one point
        /// left above it at 1000 mV (2940 MHz). Then the same curve as live control memory.
        /// </summary>
        private static void GpuCurveChecks(StringBuilder sb, Action<string, bool> expect)
        {
            var mv = new List<double>();
            for (double v = 700; v <= 1200; v += 5) mv.Add(v);
            Func<double, double> stockAt = v => v < 750 ? 180 : Math.Min(3200, 2610 + (v - 950) * 5.0);
            Func<double, double> userAt = v =>
            {
                double s = stockAt(v);
                if (v < 890) return s;
                if (v == 915 || v == 920) return s + 415;
                if (v < 950) return s + 315 * (v - 885) / 65.0;
                return v == 1000 ? 2940 : 2925;
            };
            string stockHex = SavedCurveHex(mv, stockAt, stockAt);
            string userHex = SavedCurveHex(mv, stockAt, userAt);
            var lines = new[]
            {
                "[Startup]", "Format=2", "CoreClkBoost=0", "VFCurve=" + stockHex, "MemClkBoost=0",
                "[Profile1]", "Format=2", "CoreClkBoost=315000", "VFCurve=" + userHex, "MemClkBoost=2000000",
                "[Profile2]", "VFCurve=", "[Defaults]", "VFCurve=" + stockHex, "[Settings]", "CaptureDefaults=0"
            };

            var parsed = GpuCurve.ParseProfileFile(lines);
            VfProfile p1 = parsed["Profile1"];
            expect("courbe enregistrée décodée (points, tension, décalage, mémoire)",
                p1.Points != null && p1.Points.Count == mv.Count && p1.Points[0].MilliVolts == 700 && p1.Modified
                && Math.Abs(p1.Points.First(p => p.MilliVolts == 1000).MHz - 2940) < 0.5 && p1.MemBoostMHz == 2000 && p1.CoreBoostMHz == 315
                && parsed["Profile2"].Points == null && !parsed["Startup"].Modified);
            expect("valeurs corrompues refusées (vide, hex invalide, mauvais format, tronqué, tensions désordonnées)",
                GpuCurve.ParseSaved("") == null && GpuCurve.ParseSaved("zz" + userHex.Substring(2)) == null
                && GpuCurve.ParseSaved("00000100" + userHex.Substring(8)) == null && GpuCurve.ParseSaved(userHex.Substring(0, 200)) == null
                && GpuCurve.ParseSaved(SavedCurveHex(new List<double> { 900, 850, 950 }, stockAt, stockAt)) == null);

            // Real data (RTX 5080, Afterburner 4.6): 31 points (890–1075 mV) of a saved undervolt that the card, once it
            // was applied, ran flat at 2850 MHz from 950 mV, exactly as Afterburner's editor drew it. Reading the
            // fields in the wrong order once shifted every offset by one point and invented a spike: never again.
            const string realCurve = "000002001F0000000000000000805E4400F013450000204100C05F44001016450000C0400000614400801745000050410080634400B018450000104100C0644400D01A450000A0400000664400A01B450000E0400040674400601D450000004200C0694400B01E450000D84200006B440050204500001D4300406C44009021450000454300806D4400002345000072430000704400F0234500006343004071440050254500004D43008072440040264500003E4300C0734400B02745000027430040764400A02845000018430080774400002A450000024300C0784400F02A450000E64200007A4400502D4500009A4200807C4400B02E4500005C4200C07D4400A02F450000204200007F44009030450000C84100208044008031450000204100608144007032450000A0C000008244006033450000A0C100A082440050344500000CC20040834400403545000048C20080844400303645000082C200208544002037450000A0C200C0854400A037450000B0C200608644000039450000DCC2";
            List<VfPoint> real = GpuCurve.ParseSaved(realCurve);
            bool flat = real != null && real.Count == 31 && real.Where(p => p.MilliVolts >= 950).All(p => Math.Abs(p.MHz - 2850) < 0.5)
                        && Math.Abs(real.First(p => p.MilliVolts == 940).MHz - 2722) < 0.5 && Math.Abs(real.First(p => p.MilliVolts == 950).OffsetMHz - 242) < 0.5;
            CurveAdvice realAdvice = real != null ? GpuCurve.Analyze(new VfProfile { Id = "Startup", Points = real }, null) : null;
            expect("vraie courbe Afterburner : plate à 2850 MHz dès 950 mV comme dans l'éditeur, sans faux pic",
                flat && realAdvice != null && realAdvice.TargetMv == 950 && realAdvice.RunsAtMv == 950 && realAdvice.Tips.All(t => t.Kind != CurveTipKind.Danger));

            // Live control memory with Profile 1 applied: the profile is recognised, base clocks drifted by temperature do not matter.
            // Afterburner applied Profile 1 on a warmer GPU: base clocks one bin lower, offsets recomputed (seen on a real
            // RTX 5080), real clocks within a bin of the saved ones. It must still be recognised.
            byte[] macm = LiveMemory("VEN_10DE&DEV_2C02&SUBSYS_89E01043&REV_A1&BUS_1&DEV_0&FN_0", mv, v => stockAt(v) - 15, v => userAt(v) - (stockAt(v) - 15) - (v % 10 == 0 ? 7 : 0), 2000);
            var live = GpuCurve.ParseLive(macm);
            GpuCurveInfo info = GpuCurve.Build("x", "x.cfg", lines, live.Count == 1 ? live[0].Item2 : null);
            expect("mémoire en direct décodée et profil appliqué reconnu malgré des décalages recalculés (Profil 1)",
                live.Count == 1 && live[0].Item1.StartsWith("VEN_10DE&DEV_2C02", StringComparison.Ordinal) && info.ActiveIds.SequenceEqual(new[] { "Profile1" }) && !info.LiveIsCustom
                && Math.Abs(info.Stock.Points[mv.IndexOf(950)].BaseMHz - 2595) < 0.5);
            // A curve edited and applied without being saved: 40 MHz higher on the plateau. Not Profile 1, and the gap is known.
            byte[] edited = LiveMemory("VEN_10DE", mv, v => stockAt(v), v => userAt(v) - stockAt(v) + (v >= 950 ? 40 : 0), 2000);
            GpuCurveInfo editedInfo = GpuCurve.Build("x", "x.cfg", lines, GpuCurve.ParseLive(edited)[0].Item2);
            expect("courbe appliquée non enregistrée : aucun profil reconnu, écart avec Profil 1 de 40 MHz",
                editedInfo.LiveIsCustom && Math.Abs(editedInfo.GapToLive["Profile1"] - 40) < 0.5);
            byte[] stockLive = LiveMemory("VEN_10DE", mv, v => stockAt(v), v => 0, 0);
            GpuCurveInfo stockInfo = GpuCurve.Build("x", "x.cfg", lines, GpuCurve.ParseLive(stockLive)[0].Item2);
            expect("courbe d'origine appliquée reconnue (et Démarrage, identique)", stockInfo.ActiveIds.Contains("Stock") && stockInfo.ActiveIds.Contains("Startup") && !stockInfo.ActiveIds.Contains("Profile1"));
            byte[] broken = (byte[])macm.Clone();
            broken[0] = 0;
            byte[] truncated = new byte[100];
            Array.Copy(macm, truncated, 100);
            expect("mémoire en direct invalide ignorée (signature, tronquée, vide)", GpuCurve.ParseLive(broken).Count == 0 && GpuCurve.ParseLive(truncated).Count == 0 && GpuCurve.ParseLive(null).Count == 0);

            // Analysis: the spike, the steep climb, the plateau and the memory are reported; the suggestion is safe.
            CurveAdvice a = GpuCurve.Analyze(p1, info.Stock);
            int iV = mv.IndexOf(950);
            expect("analyse : plateau à 950 mV, la carte tourne en fait à 1000 mV",
                a.TargetMv == 950 && Math.Abs(a.PlateauMHz - 2925) < 0.5 && a.RunsAtMv == 1000 && Math.Abs(a.RunsAtMHz - 2940) < 0.5);
            expect("analyse : pic, points trop poussés, plateau et mémoire signalés",
                a.Tips.Count(t => t.Kind == CurveTipKind.Danger) == 1 && a.Tips.Count(t => t.Kind == CurveTipKind.Warning) == 1 && a.Tips.Count == 4);
            bool monotone = true, capped = true, belowUser = true;
            for (int i = 1; i < a.Suggested.Count; i++) monotone &= a.Suggested[i] >= a.Suggested[i - 1];
            for (int i = 0; i < a.Suggested.Count; i++) { capped &= a.Suggested[i] <= 2910; belowUser &= a.Suggested[i] <= Math.Max(p1.Points[i].MHz, stockAt(mv[i])) + 0.5; }
            int i920 = mv.IndexOf(920);
            expect("suggestion : ne descend jamais, plateau à 2910 MHz dès 950 mV, aucun point relevé, points trop poussés ramenés à +300",
                monotone && capped && belowUser && a.Suggested[iV] == 2910 && a.Suggested[mv.Count - 1] == 2910 && a.SuggestedMHz == 2910
                && a.Suggested[i920] <= stockAt(920) + 300 && a.KeyPoints.Any(k => k.Key == 915) && a.KeyPoints.Last().Key == 950);
            CurveAdvice stockAdvice = GpuCurve.Analyze(parsed["Startup"], info.Stock);
            expect("courbe d'origine : rien à suggérer", stockAdvice.Suggested == null && stockAdvice.Tips.Count == 1 && stockAdvice.Tips[0].Kind == CurveTipKind.Info);

            // This PC, read-only.
            GpuCurveInfo mine = GpuCurve.Load();
            if (mine == null) { sb.AppendLine("   Afterburner ou courbe NVIDIA absent sur ce PC (rien à lire)"); return; }
            sb.AppendLine("   " + mine.GpuId + " · " + mine.Profiles.Count + " profil(s) · " + (mine.Live == null ? "Afterburner fermé" : "appliqué : " + (mine.ActiveIds.Count > 0 ? string.Join(", ", mine.ActiveIds) : "courbe non enregistrée")));
            foreach (VfProfile p in mine.Profiles.Concat(mine.Live != null ? new[] { mine.Live } : new VfProfile[0]))
            {
                CurveAdvice pa = GpuCurve.Analyze(p, mine.Stock);
                sb.AppendLine("   " + p.Id.PadRight(9) + (p.Modified ? "modifiée" : "origine ") + " · tourne à " + GpuCurve.Mhz(pa.RunsAtMHz) + " / " + GpuCurve.Mv(pa.RunsAtMv)
                              + (pa.Suggested != null ? " · suggérée " + GpuCurve.Mhz(pa.SuggestedMHz) + " dès " + GpuCurve.Mv(pa.TargetMv) : "") + " · " + pa.Tips.Count + " conseil(s)");
            }
        }

        private static string SavedCurveHex(List<double> mv, Func<double, double> stock, Func<double, double> curve)
        {
            var b = new List<byte>();
            b.AddRange(BitConverter.GetBytes(0x00020000u));
            b.AddRange(BitConverter.GetBytes((uint)mv.Count));
            b.AddRange(BitConverter.GetBytes(0u));
            foreach (double v in mv)
            {
                b.AddRange(BitConverter.GetBytes((float)v));
                b.AddRange(BitConverter.GetBytes((float)stock(v)));
                b.AddRange(BitConverter.GetBytes((float)(curve(v) - stock(v))));
            }
            return string.Concat(b.Select(x => x.ToString("X2")));
        }

        /// <summary>Afterburner's control memory v2 (MACMSharedMemory.h): header, one GPU entry with its curve and GPU id.</summary>
        private static byte[] LiveMemory(string gpuId, List<double> mv, Func<double, double> baseAt, Func<double, double> offsetAt, int memBoostMHz)
        {
            const int header = 36, entry = 3760;
            var m = new byte[header + entry];
            Action<int, int> put = (o, v) => BitConverter.GetBytes(v).CopyTo(m, o);
            put(0, 0x4D41434D);
            put(4, 0x00020003);
            put(8, header);
            put(12, 1);
            put(16, entry);
            put(header + 51 * 4, memBoostMHz * 1000);
            put(header + 69 * 4 + 8, mv.Count);
            for (int k = 0; k < mv.Count; k++)
            {
                int o = header + 69 * 4 + 12 + k * 12;
                put(o, (int)(mv[k] * 1000));
                put(o + 4, (int)(baseAt(mv[k]) * 1000));
                put(o + 8, (int)(offsetAt(mv[k]) * 1000));
            }
            Encoding.ASCII.GetBytes(gpuId).CopyTo(m, header + entry - 260);
            return m;
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
