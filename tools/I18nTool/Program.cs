// I18nTool: localization helper for Dump Finder's src/Locales (not shipped with the app).
// Run with: dotnet run --project tools/I18nTool -- <mode> ...
//   --check  <srcDir> <localesDir>          keys used in code exist in en.json; every language has every key;
//                                           placeholders and plural forms are valid (exit code 1 on any problem; used by CI)
//   --split  <file.json> <outDir> <size>    cut a locale file into partNN.json chunks for translators
//   --merge  <template.json> <partsDir> <out.json> <label>
//                                           reassemble translated chunks in the template's key order, report gaps
//   --add    <localesDir> <spec.json>       merge { "key": { "lang": "text" } } into the language files
//   --engine <I18nTable.cs> <outDir>        (one-off, already done) export the old engine table to *.engine.json
//   <srcDir> <localesDir> [--dry]           extract inline Localizer.L(fr, en, es, de, it, pt) calls into keys and
//                                           rewrite them to Localizer.S / F (one-off migration, kept for reference)
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class P
{
    public static readonly string[] Langs = { "fr", "en", "es", "de", "it", "pt" };
    public static readonly List<string> KeyOrder = new();
    public static readonly Dictionary<string, string[]> KeyTexts = new();
    public static readonly Dictionary<string, string> TupleToKey = new();
    public static readonly List<string> Skipped = new();
    public static int Rewritten;

    static int Main(string[] args)
    {
        if (args[0] == "--engine") return Engine(args[1], args[2]);
        if (args[0] == "--add") return Add(args[1], args[2]);
        if (args[0] == "--check") return Check(args[1], args[2]);
        if (args[0] == "--split") return Split(args[1], args[2], int.Parse(args[3]));
        if (args[0] == "--merge") return Merge(args[1], args[2], args[3], args[4]);
        string src = args[0], outDir = args[1];
        bool dry = args.Contains("--dry");
        var files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\obj\\") && !f.Contains("\\bin\\"))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        // Incremental run: keep the keys already extracted.
        if (File.Exists(Path.Combine(outDir, "en.json")))
        {
            var loaded = Langs.Select(l => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(outDir, l + ".json")))!).ToArray();
            foreach (var k in loaded[1].Keys)
            {
                var texts = loaded.Select(d => d.TryGetValue(k, out var v) ? v : "").ToArray();
                KeyOrder.Add(k);
                KeyTexts[k] = texts;
                TupleToKey[string.Join("\u0001", texts)] = k;
            }
        }

        foreach (var file in files)
        {
            byte[] raw = File.ReadAllBytes(file);
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            string text = new UTF8Encoding(false).GetString(bom ? raw.Skip(3).ToArray() : raw);
            var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest));
            var rw = new Rewriter(Prefix(file), Path.GetFileName(file), tree);
            var root = rw.Visit(tree.GetRoot());
            if (rw.Changed && !dry)
            {
                string outText = root.ToFullString();
                var enc = new UTF8Encoding(bom);
                File.WriteAllText(file, outText, enc);
            }
        }

        Directory.CreateDirectory(outDir);
        var jsonOpts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        for (int li = 0; li < Langs.Length; li++)
        {
            var dict = new Dictionary<string, string>();
            foreach (var k in KeyOrder) dict[k] = KeyTexts[k][li];
            if (!dry) File.WriteAllText(Path.Combine(outDir, Langs[li] + ".json"), JsonSerializer.Serialize(dict, jsonOpts) + "\n", new UTF8Encoding(false));
        }
        Console.WriteLine($"Rewritten calls: {Rewritten}, unique keys: {KeyOrder.Count}, skipped: {Skipped.Count}");
        foreach (var s in Skipped) Console.WriteLine("SKIP " + s);
        return 0;
    }

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Splits a JSON object into part01.json, part02.json… of at most <paramref name="size"/> entries.</summary>
    static int Split(string file, string outDir, int size)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
        Directory.CreateDirectory(outDir);
        var keys = d.Keys.ToList();
        int n = 0;
        for (int i = 0; i < keys.Count; i += size)
        {
            n++;
            var part = keys.Skip(i).Take(size).ToDictionary(k => k, k => d[k]);
            File.WriteAllText(Path.Combine(outDir, $"part{n:00}.json"), JsonSerializer.Serialize(part, Pretty) + "\n", new UTF8Encoding(false));
        }
        Console.WriteLine($"{keys.Count} entries → {n} parts in {outDir}");
        return 0;
    }

    /// <summary>Assembles translated parts (partsDir/part*.json) in the order of the template file into outFile; reports gaps.</summary>
    static int Merge(string template, string partsDir, string outFile, string label)
    {
        var t = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(template))!;
        var all = new Dictionary<string, string>();
        foreach (var f in Directory.GetFiles(partsDir, "part*.json").OrderBy(x => x))
        {
            try
            {
                foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!) all[kv.Key] = kv.Value;
            }
            catch (Exception ex) { Console.WriteLine($"{label}: INVALID {Path.GetFileName(f)}: {ex.Message}"); }
        }
        var outD = new Dictionary<string, string>();
        int missing = 0;
        foreach (var k in t.Keys)
        {
            if (all.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)) outD[k] = v;
            else { missing++; if (missing <= 20) Console.WriteLine($"{label}: missing {k}"); }
        }
        int extra = all.Keys.Count(k => !t.ContainsKey(k));
        File.WriteAllText(outFile, JsonSerializer.Serialize(outD, Pretty) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"{label}: {outD.Count}/{t.Count} entries, {missing} missing, {extra} unknown keys ignored");
        return missing == 0 ? 0 : 1;
    }

    /// <summary>
    /// Consistency check: keys used in code exist in en.json; every language has every key; placeholders {n}
    /// used by a translation exist in English; plural values ("one: … | other: …") have an "other" form.
    /// </summary>
    static int Check(string src, string localesDir)
    {
        int problems = 0;
        void Report(string s) { problems++; if (problems <= 200) Console.WriteLine(s); }
        var used = new HashSet<string>();
        foreach (var f in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains("\\obj\\")))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"Localizer\.(?:S|F|P)\(\s*""([^""]+)"""))
                used.Add(m.Groups[1].Value);
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(localesDir, "en.json")))!;
        foreach (var k in used.Where(k => !en.ContainsKey(k))) Report("MISSING in en.json: " + k);
        foreach (var k in en.Keys.Where(k => !used.Contains(k))) Console.WriteLine("unused key: " + k);
        var ph = new Regex(@"(?<!\{)\{(\d+)(?::[^}]*)?\}(?!\})");
        foreach (var file in Directory.GetFiles(localesDir, "*.json").Where(f => !f.EndsWith(".engine.json")))
        {
            string lang = Path.GetFileNameWithoutExtension(file);
            Dictionary<string, string> d;
            try { d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!; }
            catch (Exception ex) { Report(lang + ": INVALID JSON " + ex.Message); continue; }
            foreach (var k in en.Keys)
            {
                if (!d.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v)) { Report(lang + ": missing " + k); continue; }
                var enPh = ph.Matches(en[k]).Select(m => m.Groups[1].Value).ToHashSet();
                var vPh = ph.Matches(v).Select(m => m.Groups[1].Value).ToHashSet();
                if (vPh.Any(p => !enPh.Contains(p))) Report(lang + ": unknown placeholder in " + k);
                if (lang != "en" && enPh.Count > 0 && vPh.Count == 0 && !en[k].Contains("one:")) Report(lang + ": all placeholders dropped in " + k);
                if (v.Contains("|") && Regex.IsMatch(v, @"^\s*(zero|one|two|few|many|other)\s*:") && !Regex.IsMatch(v, @"(^|\|)\s*other\s*:")) Report(lang + ": plural without 'other' in " + k);
                try { string.Format(Regex.Replace(v, @"^\s*\w+\s*:", ""), Enumerable.Range(0, 10).Select(i => (object)i).ToArray()); }
                catch (FormatException) { if (!v.Contains("|")) Report(lang + ": bad format string in " + k); }
            }
            foreach (var k in d.Keys.Where(k => !en.ContainsKey(k))) Report(lang + ": extra key " + k);
        }
        Console.WriteLine($"Keys used: {used.Count}, in en.json: {en.Count}, problems: {problems}");
        return problems == 0 ? 0 : 1;
    }

    /// <summary>Merges { key: { lang: text } } into each Locales/&lt;lang&gt;.json (existing order kept, new keys appended).</summary>
    static int Add(string localesDir, string specFile)
    {
        var spec = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(specFile))!;
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var langs = spec.Values.SelectMany(v => v.Keys).Distinct().ToList();
        foreach (var lang in langs)
        {
            string path = Path.Combine(localesDir, lang + ".json");
            var dict = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!
                : new Dictionary<string, string>();
            foreach (var kv in spec)
                if (kv.Value.TryGetValue(lang, out var text)) dict[kv.Key] = text;
            File.WriteAllText(path, JsonSerializer.Serialize(dict, opts) + "\n", new UTF8Encoding(false));
        }
        Console.WriteLine($"Merged {spec.Count} key(s) into {langs.Count} language file(s)");
        return 0;
    }

    /// <summary>I18nTable.cs dictionary initializer { "english", new[] { es, de, it, pt } } → &lt;lang&gt;.engine.json.</summary>
    static int Engine(string tableFile, string outDir)
    {
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(tableFile));
        string[] langs = { "es", "de", "it", "pt" };
        var maps = langs.Select(_ => new Dictionary<string, string>()).ToArray();
        var en = new Dictionary<string, string>();
        foreach (var init in tree.GetRoot().DescendantNodes().OfType<InitializerExpressionSyntax>()
                     .Where(i => i.IsKind(SyntaxKind.ComplexElementInitializerExpression)))
        {
            if (init.Expressions.Count != 2 || init.Expressions[0] is not LiteralExpressionSyntax k) continue;
            var arr = init.Expressions[1].DescendantNodes().OfType<InitializerExpressionSyntax>().FirstOrDefault();
            if (arr == null || arr.Expressions.Count != 4) { Console.WriteLine("SKIP " + k.Token.ValueText); continue; }
            string key = k.Token.ValueText;
            en[key] = key;
            for (int i = 0; i < 4; i++) maps[i][key] = ((LiteralExpressionSyntax)arr.Expressions[i]).Token.ValueText;
        }
        var opts = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        for (int i = 0; i < 4; i++)
            File.WriteAllText(Path.Combine(outDir, langs[i] + ".engine.json"), JsonSerializer.Serialize(maps[i], opts) + "\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "en.engine.json"), JsonSerializer.Serialize(en, opts) + "\n", new UTF8Encoding(false));
        Console.WriteLine("Engine entries: " + en.Count);
        return 0;
    }

    static string Prefix(string file)
    {
        string n = Path.GetFileNameWithoutExtension(file);
        if (n.StartsWith("MainWindow.")) n = n.Substring("MainWindow.".Length);
        if (n.EndsWith("Page") && n.Length > 4) n = n.Substring(0, n.Length - 4);
        n = Regex.Replace(n, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant();
        return n;
    }

    public static string Slug(string en)
    {
        string s = Regex.Replace(en, @"\{\d+\}", " ");
        s = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : ' ');
        }
        var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(6);
        string slug = string.Join("_", words);
        if (slug.Length > 40) slug = slug.Substring(0, 40).TrimEnd('_');
        return slug.Length == 0 ? "text" : slug;
    }

    public static string KeyFor(string prefix, string[] texts)
    {
        string tuple = string.Join("\u0001", texts);
        if (TupleToKey.TryGetValue(tuple, out var existing)) return existing;
        string baseKey = prefix + "." + Slug(texts[1]);
        string key = baseKey;
        for (int i = 2; KeyTexts.ContainsKey(key); i++) key = baseKey + "_" + i;
        TupleToKey[tuple] = key;
        KeyTexts[key] = texts;
        KeyOrder.Add(key);
        return key;
    }
}

/// <summary>One flattened argument: literal text pieces and expression placeholders, in order.</summary>
sealed class Part
{
    public string? Text;            // literal
    public ExpressionSyntax? Expr;  // placeholder
    public string? Format;          // interpolation format clause (":0.0")
}

sealed class Rewriter : CSharpSyntaxRewriter
{
    private readonly string _prefix, _file;
    private readonly SyntaxTree _tree;
    public bool Changed;

    public Rewriter(string prefix, string file, SyntaxTree tree) { _prefix = prefix; _file = file; _tree = tree; }

    private static readonly HashSet<string> Names = new() { "L", "SL", "OL" };

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Bottom-up: nested calls inside the arguments are rewritten first.
        var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        if (!IsTarget(visited)) return visited;
        if (visited.ArgumentList.Arguments.Count != 6) return visited;

        string where = _file + ":" + (_tree.GetLineSpan(node.Span).StartLinePosition.Line + 1);
        var parts = new List<Part>[6];
        for (int i = 0; i < 6; i++)
        {
            var a = visited.ArgumentList.Arguments[i];
            if (a.NameColon != null || a.RefKindKeyword.Kind() != SyntaxKind.None) return Skip(visited, where, "named/ref argument");
            var list = new List<Part>();
            if (!Flatten(a.Expression, list)) return Skip(visited, where, "argument not flattenable");
            parts[i] = list;
        }

        // Wrapper bodies forward their own parameters (fr, en...): nothing to extract.
        if (parts.All(p => p.Count == 1 && p[0].Expr is IdentifierNameSyntax)) return visited;

        // Placeholders: distinct expressions (by text), union over all languages (English order first). A shorter
        // translation that leaves a value out simply does not use that placeholder. Expressions that carry text of
        // their own (a ternary choosing between two phrases) cannot become placeholders: manual work.
        var exprs = new List<string>();
        var exprNodes = new List<ExpressionSyntax>();
        foreach (int li in new[] { 1, 0, 2, 3, 4, 5 })
        {
            foreach (var p in parts[li].Where(x => x.Expr != null))
            {
                if (p.Expr!.DescendantNodesAndSelf().Any(n => n.IsKind(SyntaxKind.StringLiteralExpression) || n is InterpolatedStringExpressionSyntax))
                    return Skip(visited, where, "placeholder expression contains text");
                string t = Norm(p.Expr!);
                if (!exprs.Contains(t)) { exprs.Add(t); exprNodes.Add(p.Expr!); }
            }
        }

        bool hasPlaceholders = exprs.Count > 0;
        var texts = new string[6];
        for (int i = 0; i < 6; i++)
        {
            var sb = new StringBuilder();
            foreach (var p in parts[i])
            {
                if (p.Text != null)
                {
                    if (hasPlaceholders)
                    {
                        if (Regex.IsMatch(p.Text, @"\{\d+(:[^}]*)?\}")) return Skip(visited, where, "literal {n} mixed with concatenation");
                        sb.Append(p.Text.Replace("{", "{{").Replace("}", "}}"));
                    }
                    else sb.Append(p.Text);
                }
                else
                {
                    int idx = exprs.IndexOf(Norm(p.Expr!));
                    sb.Append('{').Append(idx);
                    if (p.Format != null) sb.Append(':').Append(p.Format);
                    sb.Append('}');
                }
            }
            texts[i] = sb.ToString();
        }

        string key = P.KeyFor(_prefix, texts);
        P.Rewritten++;
        Changed = true;

        ExpressionSyntax callee = SyntaxFactory.ParseExpression(hasPlaceholders ? "Localizer.F" : "Localizer.S");
        var args = new List<ArgumentSyntax>
        {
            SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(key)))
        };
        foreach (var e in exprNodes) args.Add(SyntaxFactory.Argument(e.WithoutTrivia()));
        var sep = Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space), Math.Max(0, args.Count - 1));
        var call = SyntaxFactory.InvocationExpression(callee, SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args, sep)));
        return call.WithLeadingTrivia(visited.GetLeadingTrivia()).WithTrailingTrivia(visited.GetTrailingTrivia());
    }

    private static bool IsTarget(InvocationExpressionSyntax inv)
    {
        switch (inv.Expression)
        {
            case IdentifierNameSyntax id: return Names.Contains(id.Identifier.ValueText);
            case MemberAccessExpressionSyntax ma:
                return ma.Name.Identifier.ValueText == "L" && ma.Expression is IdentifierNameSyntax t && t.Identifier.ValueText == "Localizer";
            default: return false;
        }
    }

    private SyntaxNode Skip(InvocationExpressionSyntax node, string where, string why)
    {
        P.Skipped.Add(where + " (" + why + ")");
        return node;
    }

    private static string Norm(ExpressionSyntax e) => Regex.Replace(e.WithoutTrivia().ToString(), @"\s+", " ");

    private static bool Flatten(ExpressionSyntax e, List<Part> into)
    {
        switch (e)
        {
            case ParenthesizedExpressionSyntax p when IsStringy(p.Expression):
                return Flatten(p.Expression, into);
            case LiteralExpressionSyntax lit when lit.IsKind(SyntaxKind.StringLiteralExpression):
                into.Add(new Part { Text = lit.Token.ValueText });
                return true;
            case BinaryExpressionSyntax b when b.IsKind(SyntaxKind.AddExpression) && (IsStringy(b.Left) || IsStringy(b.Right)):
                // Only a left-leaning chain of string concatenation is safe to flatten ("a" + x + "b").
                return FlattenOperand(b.Left, into) && FlattenOperand(b.Right, into);
            case InvocationExpressionSyntax fmt when IsStringFormat(fmt):
            {
                // string.Format("text {0:fmt} text", a) → text / placeholder(a, fmt) / text
                var fargs = fmt.ArgumentList.Arguments;
                string f = ((LiteralExpressionSyntax)fargs[0].Expression).Token.ValueText;
                int pos = 0;
                var lit = new StringBuilder();
                while (pos < f.Length)
                {
                    char c = f[pos];
                    if (c == '{' && pos + 1 < f.Length && f[pos + 1] == '{') { lit.Append('{'); pos += 2; continue; }
                    if (c == '}' && pos + 1 < f.Length && f[pos + 1] == '}') { lit.Append('}'); pos += 2; continue; }
                    if (c == '{')
                    {
                        var m = Regex.Match(f.Substring(pos), @"^\{(\d+)(,[^:}]*)?(?::([^}]*))?\}");
                        if (!m.Success || m.Groups[2].Success) return false;
                        int n = int.Parse(m.Groups[1].Value);
                        if (n + 1 >= fargs.Count) return false;
                        if (lit.Length > 0) { into.Add(new Part { Text = lit.ToString() }); lit.Clear(); }
                        into.Add(new Part { Expr = fargs[n + 1].Expression, Format = m.Groups[3].Success ? m.Groups[3].Value : null });
                        pos += m.Length;
                        continue;
                    }
                    lit.Append(c); pos++;
                }
                if (lit.Length > 0) into.Add(new Part { Text = lit.ToString() });
                return true;
            }
            case InterpolatedStringExpressionSyntax isx:
                foreach (var c in isx.Contents)
                {
                    if (c is InterpolatedStringTextSyntax t) into.Add(new Part { Text = t.TextToken.ValueText });
                    else if (c is InterpolationSyntax ip)
                    {
                        if (ip.AlignmentClause != null) return false;
                        into.Add(new Part { Expr = ip.Expression, Format = ip.FormatClause?.FormatStringToken.ValueText });
                    }
                }
                return true;
            default:
                into.Add(new Part { Expr = e });
                return true;
        }
    }

    private static bool FlattenOperand(ExpressionSyntax e, List<Part> into)
    {
        // A non-string arithmetic sub-expression (a + b with numbers) must stay one placeholder.
        if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression) && !(IsStringy(b.Left) || IsStringy(b.Right)))
        {
            into.Add(new Part { Expr = e });
            return true;
        }
        return Flatten(e, into);
    }

    private static bool IsStringFormat(InvocationExpressionSyntax inv)
    {
        return inv.Expression is MemberAccessExpressionSyntax ma && ma.Name.Identifier.ValueText == "Format"
            && ma.Expression.ToString() is "string" or "String"
            && inv.ArgumentList.Arguments.Count >= 1
            && inv.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax l && l.IsKind(SyntaxKind.StringLiteralExpression);
    }

    private static bool IsStringy(ExpressionSyntax e)
    {
        switch (e)
        {
            case InvocationExpressionSyntax inv when IsStringFormat(inv): return true;
            case LiteralExpressionSyntax lit: return lit.IsKind(SyntaxKind.StringLiteralExpression);
            case InterpolatedStringExpressionSyntax: return true;
            case ParenthesizedExpressionSyntax p: return IsStringy(p.Expression);
            case BinaryExpressionSyntax b: return b.IsKind(SyntaxKind.AddExpression) && (IsStringy(b.Left) || IsStringy(b.Right));
            default: return false;
        }
    }
}
