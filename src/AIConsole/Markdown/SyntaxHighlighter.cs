using System.Text;
using System.Text.RegularExpressions;
using ColorCode;
using ColorCode.Common;
using ColorCode.Compilation;
using ColorCode.Parsing;

namespace AIConsole.Markdown;

public enum SyntaxKind
{
    Plain, Keyword, String, Comment, Number, Type, Function, Operator, Punctuation, Tag, Attribute, Preprocessor, Constant,
}

public readonly record struct SyntaxToken(SyntaxKind Kind, string Text);

public static class SyntaxHighlighter
{
    private static readonly LanguageParser Parser = new(
        new LanguageCompiler(new Dictionary<string, CompiledLanguage>(), new System.Threading.ReaderWriterLockSlim()),
        new LanguageRepository(new Dictionary<string, ILanguage>()));

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "CSharp", ["csharp"] = "CSharp", ["c#"] = "CSharp", ["c-sharp"] = "CSharp", ["dotnet"] = "CSharp",
        ["js"] = "JavaScript", ["javascript"] = "JavaScript", ["mjs"] = "JavaScript", ["jsx"] = "JavaScript", ["node"] = "JavaScript",
        ["ts"] = "Typescript", ["typescript"] = "Typescript", ["tsx"] = "Typescript",
        ["py"] = "Python", ["python"] = "Python", ["python3"] = "Python",
        ["html"] = "Html", ["htm"] = "Html", ["xhtml"] = "Html",
        ["xml"] = "Xml", ["svg"] = "Xml", ["xaml"] = "Xml", ["axaml"] = "Xml",
        ["css"] = "Css", ["scss"] = "Css", ["sass"] = "Css",
        ["sql"] = "Sql", ["sqlite"] = "Sql", ["mysql"] = "Sql", ["postgres"] = "Sql", ["tsql"] = "Sql",
        ["ps"] = "PowerShell", ["ps1"] = "PowerShell", ["powershell"] = "PowerShell", ["pwsh"] = "PowerShell",
        ["vb"] = "VbDotNet", ["vbnet"] = "VbDotNet", ["vb.net"] = "VbDotNet",
        ["c"] = "Cpp", ["cpp"] = "Cpp", ["c++"] = "Cpp", ["cc"] = "Cpp", ["h"] = "Cpp", ["hpp"] = "Cpp",
        ["java"] = "Java", ["kt"] = "Java", ["kotlin"] = "Java",
        ["php"] = "Php", ["json"] = "Json", ["jsonc"] = "Json", ["json5"] = "Json",
        ["fsharp"] = "FSharp", ["f#"] = "FSharp", ["fs"] = "FSharp",
        ["haskell"] = "Haskell", ["hs"] = "Haskell",
        ["matlab"] = "MATLAB", ["fortran"] = "Fortran", ["php5"] = "Php", ["aspx"] = "Aspx",
    };

    private static readonly HashSet<string> ShellNames = new(StringComparer.OrdinalIgnoreCase)
    { "bash", "sh", "shell", "zsh", "ksh", "console", "terminal", "cmd", "batch", "bat" };

    private static readonly HashSet<string> ShellKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if","then","else","elif","fi","for","while","do","done","case","esac","in","function","return","local","export",
        "readonly","declare","unset","shift","source","alias","cd","echo","printf","read","set","trap","exit","break","continue",
        "true","false","test","let","eval","exec","sudo","apt","npm","npx","dotnet","git","curl","wget","mkdir","rm","cp","mv","ls","cat",
    };

    public static IReadOnlyList<string> SupportedLanguages => new[]
    {
        "csharp","javascript","typescript","python","json","html","xml","css","sql","powershell","vbnet","cpp","java","php","fsharp","haskell","matlab","fortran","bash",
    };

    /// <summary>Tokenize code into per-line token lists (for line numbers). Never throws.</summary>
    public static List<List<SyntaxToken>> Highlight(string code, string? language)
    {
        code ??= "";
        try
        {
            var lang = NormalizeLanguage(language);
            var flat = new List<SyntaxToken>();

            if (lang is not null && ResolveLanguage(lang) is { } ilang)
            {
                Parser.Parse(code, ilang, (chunk, scopes) => Flatten(chunk, scopes, flat));
            }
            else if (lang is not null && ShellNames.Contains(lang))
            {
                flat = ShellTokenize(code);
            }
            else
            {
                flat = GenericTokenize(code);
            }

            return SplitLines(flat);
        }
        catch
        {
            return SplitLines(new List<SyntaxToken> { new(SyntaxKind.Plain, code) });
        }
    }

    public static string? NormalizeLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return null;
        var raw = info.Trim().Trim('.').ToLowerInvariant();
        // strip Markdig quirks like "csharp {linenums}" or attributes
        var firstToken = raw.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstToken is null) return null;
        if (Aliases.TryGetValue(firstToken, out var mapped)) return mapped;
        return firstToken;
    }

    private static ILanguage? ResolveLanguage(string name)
    {
        try
        {
            var id = Aliases.TryGetValue(name, out var m) ? m : name;
            return Languages.FindById(id) ?? Languages.FindById(id.ToLowerInvariant());
        }
        catch { return null; }
    }

    // ── ColorCode scope tree → flat token stream ──

    private static void Flatten(string text, IList<Scope> scopes, List<SyntaxToken> output)
    {
        if (text.Length == 0) return;
        if (scopes.Count == 0) { Emit(output, SyntaxKind.Plain, text); return; }

        var ordered = scopes.OrderBy(s => s.Index).ToList();
        int pos = 0;
        foreach (var s in ordered)
        {
            int start = Math.Clamp(s.Index, 0, text.Length);
            int end = Math.Clamp(s.Index + s.Length, start, text.Length);
            if (start > pos) Emit(output, SyntaxKind.Plain, text[pos..start]);
            if (end > start) Emit(output, MapScope(s.Name), text[start..end]);
            pos = Math.Max(pos, end);
        }
        if (pos < text.Length) Emit(output, SyntaxKind.Plain, text[pos..]);
    }

    private static SyntaxKind MapScope(string name)
    {
        var n = name.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        return n switch
        {
            "keyword" or "controlkeyword" or "pseudokeyword" or "predefined" or "intrinsic" or "builtinvalue" or "languageprefix" => SyntaxKind.Keyword,
            "string" or "stringcsharpverbatim" or "stringescape" or "jsonstring" or "xmlattributevalue" or "xmlattributequotes"
                or "csspropertyvalue" or "htmlattributevalue" or "cdatasection" or "xmlcdatasection" => SyntaxKind.String,
            "comment" or "xmlcomment" or "xmldoccomment" or "xmldoctag" or "htmlcomment" or "xmldoc" => SyntaxKind.Comment,
            "number" or "jsonnumber" => SyntaxKind.Number,
            "jsonconst" or "builtinconstant" => SyntaxKind.Constant,
            "type" or "typevariable" or "classname" or "namespace" or "constructor" or "powershelltype" or "jsonkey" => SyntaxKind.Type,
            "builtinfunction" or "sqlsystemfunction" or "powershellcommand" => SyntaxKind.Function,
            "operator" or "powershelloperator" or "htmloperator" => SyntaxKind.Operator,
            "delimiter" or "xmldelimiter" or "htmltagdelimiter" or "brackets" or "continuation" => SyntaxKind.Punctuation,
            "xmlname" or "htmlname" or "htmlelementname" or "cssselector" => SyntaxKind.Tag,
            "xmlattribute" or "htmlattributename" or "csspropertyname" or "powershellparameter" or "attribute" => SyntaxKind.Attribute,
            "preprocessorkeyword" => SyntaxKind.Preprocessor,
            "powershellattribute" => SyntaxKind.Attribute,
            "powershellvariable" => SyntaxKind.Type,
            "plaintext" or "" => SyntaxKind.Plain,
            _ => SyntaxKind.Plain,
        };
    }

    private static void Emit(List<SyntaxToken> output, SyntaxKind kind, string text)
    {
        if (text.Length == 0) return;
        if (output.Count > 0 && output[^1].Kind == kind)
            output[^1] = new SyntaxToken(kind, output[^1].Text + text);
        else output.Add(new SyntaxToken(kind, text));
    }

    // ── line splitting (tokens can span lines, e.g. block comments) ──

    private static List<List<SyntaxToken>> SplitLines(List<SyntaxToken> flat)
    {
        var lines = new List<List<SyntaxToken>> { new() };
        foreach (var tok in flat)
        {
            var parts = tok.Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) lines.Add(new List<SyntaxToken>());
                if (parts[i].Length > 0)
                {
                    var cur = lines[^1];
                    if (cur.Count > 0 && cur[^1].Kind == tok.Kind)
                        cur[^1] = new SyntaxToken(tok.Kind, cur[^1].Text + parts[i]);
                    else cur.Add(new SyntaxToken(tok.Kind, parts[i]));
                }
            }
        }
        return lines;
    }

    // ── shell (ColorCode has no shell grammar) ──

    private static readonly Regex ShellToken = new(
        @"(?<comment>\#.*)|(?<str>""(?:\\.|[^""])*""|'(?:[^'\\]|\\.)*')|(?<num>\b\d+\b)|(?<word>[\w\-\.\/]+)",
        RegexOptions.Compiled);

    private static List<SyntaxToken> ShellTokenize(string code)
    {
        var output = new List<SyntaxToken>();
        int pos = 0;
        foreach (Match m in ShellToken.Matches(code))
        {
            if (m.Index > pos) Emit(output, SyntaxKind.Plain, code[pos..m.Index]);
            var kind = m.Groups["comment"].Success ? SyntaxKind.Comment
                : m.Groups["str"].Success ? SyntaxKind.String
                : m.Groups["num"].Success ? SyntaxKind.Number
                : ShellKeywords.Contains(m.Value) ? SyntaxKind.Keyword
                : m.Value.StartsWith("-") ? SyntaxKind.Attribute
                : SyntaxKind.Plain;
            Emit(output, kind, m.Value);
            pos = m.Index + m.Length;
        }
        if (pos < code.Length) Emit(output, SyntaxKind.Plain, code[pos..]);
        return output;
    }

    // ── generic fallback (unknown language): strings, comments, numbers ──

    private static readonly Regex GenericToken = new(
        @"(?<block>/\*.*?\*/|<!--.*?-->)|(?<comment>//.*|#.*|--.*)|(?<str>""(?:\\.|[^""\n])*""|'(?:\\.|[^'\n])*')|(?<num>\b0x[0-9a-fA-F]+\b|\b\d+(?:\.\d+)?\b)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static List<SyntaxToken> GenericTokenize(string code)
    {
        var output = new List<SyntaxToken>();
        int pos = 0;
        foreach (Match m in GenericToken.Matches(code))
        {
            if (m.Index > pos) Emit(output, SyntaxKind.Plain, code[pos..m.Index]);
            var kind = m.Groups["block"].Success || m.Groups["comment"].Success ? SyntaxKind.Comment
                : m.Groups["str"].Success ? SyntaxKind.String
                : m.Groups["num"].Success ? SyntaxKind.Number : SyntaxKind.Plain;
            Emit(output, kind, m.Value);
            pos = m.Index + m.Length;
        }
        if (pos < code.Length) Emit(output, SyntaxKind.Plain, code[pos..]);
        return output;
    }
}
