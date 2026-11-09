using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BibleMarkdown;

/// <summary>
/// A dictionary module of The SWORD Project, such as StrongsGreek or StrongsHebrew, read directly (no SWORD tools are
/// needed). Supported are the drivers RawLD and RawLD4 (the entries are in one file) and zLD (the entries are in
/// blocks compressed with ZIP), in UTF-8 or Latin-1. The entries are in the markup of the module (TEI, ThML or plain
/// text), see <see cref="StrongsMarkup"/>.
/// </summary>
public sealed class SwordDictionary
{
    public string Name { get; }
    public string Driver { get; }
    public string SourceType { get; }
    public string Description { get; }

    /// <summary>"G" for a Greek and "H" for a Hebrew dictionary (from its Feature, name or description), else null.</summary>
    public string? StrongsPrefix { get; }

    readonly bool compressed;   // zLD: the entries are in compressed blocks
    readonly bool wide;         // RawLD4: sizes take 4 bytes instead of 2
    readonly Encoding encoding;
    readonly byte[] idx, dat;   // the index of the entries, and the keys (RawLD: also the text)
    readonly byte[]? zdx, zdt;  // zLD: where the blocks are, and the blocks
    int cachedBlock = -1;
    byte[]? cachedData;

    /// <summary>Whether the .conf file is that of a dictionary module (RawLD, RawLD4 or zLD).</summary>
    public static bool IsDictionary(string confFile)
    {
        try
        {
            var driver = SwordModule.ReadConf(confFile, out _).GetValueOrDefault("ModDrv", "");
            return driver.StartsWith("RawLD", StringComparison.OrdinalIgnoreCase) || driver.StartsWith("zLD", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
    }

    /// <param name="confFile">The .conf file of the module, in the mods.d folder.</param>
    /// <param name="root">The folder with mods.d and modules; the DataPath of the module is relative to it.</param>
    public static SwordDictionary Open(string confFile, string root) => new(confFile, root);

    SwordDictionary(string confFile, string root)
    {
        var conf = SwordModule.ReadConf(confFile, out var name);
        Name = name ?? Path.GetFileNameWithoutExtension(confFile);
        Driver = conf.GetValueOrDefault("ModDrv", "");
        SourceType = conf.GetValueOrDefault("SourceType", "");
        Description = conf.GetValueOrDefault("Description", "");

        compressed = Driver.StartsWith("zLD", StringComparison.OrdinalIgnoreCase);
        if (!compressed && !Driver.StartsWith("RawLD", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"The driver '{Driver}' of the SWORD module {Name} is not supported (supported: RawLD, RawLD4, zLD).");
        wide = Driver.EndsWith("4");

        if (conf.ContainsKey("CipherKey"))
            throw new NotSupportedException($"The SWORD module {Name} is encrypted.");
        var compression = conf.GetValueOrDefault("CompressType", "LZSS").Trim().ToUpperInvariant();
        if (compressed && compression != "ZIP")
            throw new NotSupportedException($"The compression '{compression}' of the SWORD module {Name} is not supported (supported: ZIP).");

        var encodingName = conf.GetValueOrDefault("Encoding", "Latin-1").Trim();
        encoding = encodingName.Equals("UTF-8", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8
            : encodingName.Equals("Latin-1", StringComparison.OrdinalIgnoreCase) ? Encoding.Latin1
            : throw new NotSupportedException($"The encoding '{encodingName}' of the SWORD module {Name} is not supported (supported: UTF-8, Latin-1).");

        // The DataPath of a dictionary is the start of the names of its files: dict -> dict.idx, dict.dat ...
        var dataPath = conf.GetValueOrDefault("DataPath", "").Trim().Replace('\\', '/');
        while (dataPath.StartsWith("./")) dataPath = dataPath[2..];
        var files = Path.GetFullPath(Path.Combine(root, dataPath));
        foreach (var extension in compressed ? new[] { "idx", "dat", "zdx", "zdt" } : new[] { "idx", "dat" })
            if (!File.Exists($"{files}.{extension}"))
                throw new FileNotFoundException($"The file {Path.GetFileName(files)}.{extension} of the SWORD module {Name} was not found.");
        idx = File.ReadAllBytes(files + ".idx");
        dat = File.ReadAllBytes(files + ".dat");
        if (compressed)
        {
            zdx = File.ReadAllBytes(files + ".zdx");
            zdt = File.ReadAllBytes(files + ".zdt");
        }

        var kind = $"{conf.GetValueOrDefault("Feature", "")} {Name} {Description}";
        bool greek = kind.Contains("Greek", StringComparison.OrdinalIgnoreCase);
        bool hebrew = kind.Contains("Hebrew", StringComparison.OrdinalIgnoreCase);
        StrongsPrefix = kind.Contains("GreekDef", StringComparison.OrdinalIgnoreCase) ? "G"
            : kind.Contains("HebrewDef", StringComparison.OrdinalIgnoreCase) ? "H"
            : greek && !hebrew ? "G"
            : hebrew && !greek ? "H"
            : null;
    }

    int IndexEntrySize => compressed || wide ? 8 : 6;

    /// <summary>The number of entries in the module (the first, with the introduction of the module, included).</summary>
    public int Count => idx.Length / IndexEntrySize;

    /// <summary>
    /// The entries: the key as it is in the module ("00026") and the text in the markup of the module.
    /// Entries that cannot be read (data beyond the end of a file) are skipped.
    /// </summary>
    public IEnumerable<(string Key, string Text)> Entries()
    {
        for (int i = 0; i < Count; i++)
        {
            var pos = i * IndexEntrySize;
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(idx.AsSpan(pos));
            int size = compressed || wide ? (int)BinaryPrimitives.ReadUInt32LittleEndian(idx.AsSpan(pos + 4))
                : BinaryPrimitives.ReadUInt16LittleEndian(idx.AsSpan(pos + 4));
            if (size <= 0 || offset + size > dat.Length) continue;

            // An entry in the data file is its key, a line break, and then the text (RawLD) or, for zLD, the number of
            // the block and the number of the entry in the block (two 32 bit numbers).
            var entry = dat.AsSpan((int)offset, size);
            int lineEnd = entry.IndexOf((byte)'\n');
            if (lineEnd < 0) continue;
            var key = encoding.GetString(entry[..lineEnd]).Trim('\r', ' ', '\\', '\0');

            string? text;
            if (compressed)
            {
                if (lineEnd + 1 + 8 > entry.Length) continue;
                int block = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[(lineEnd + 1)..]);
                int number = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[(lineEnd + 5)..]);
                text = ReadBlockEntry(block, number);
            }
            else text = encoding.GetString(entry[(lineEnd + 1)..]);

            if (text != null) yield return (key, text.Trim('\0', '\r', '\n', ' '));
        }
    }

    // zLD: a block starts with the number of its entries, then the offset and the size of each entry (32 bit numbers).
    string? ReadBlockEntry(int block, int number)
    {
        if (cachedBlock != block)
        {
            long pos = (long)block * 8;
            if (block < 0 || pos + 8 > zdx!.Length) return null;
            long offset = BinaryPrimitives.ReadUInt32LittleEndian(zdx.AsSpan((int)pos));
            int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(zdx.AsSpan((int)pos + 4));
            if (offset + size > zdt!.Length)
                throw new InvalidDataException($"A block of the SWORD module {Name} is beyond the end of its data.");
            cachedData = SwordModule.Zip(zdt.AsSpan((int)offset, size));
            cachedBlock = block;
        }

        var data = cachedData!;
        if (data.Length < 4) return null;
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (number < 0 || number >= count || 4 + (number + 1) * 8 > data.Length) return null;
        long entryOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 + number * 8));
        int entrySize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4 + number * 8 + 4));
        if (entryOffset + entrySize > data.Length) return null;
        return encoding.GetString(data, (int)entryOffset, entrySize);
    }
}

/// <summary>Converts the entries of a Strong's dictionary to Bible Markdown.</summary>
public static class StrongsMarkup
{
    /// <summary>
    /// The entry as markdown: the word in bold, its transliteration and its pronunciation in italics, and then the
    /// definition as paragraphs. Entries in TEI (as StrongsGreek) are read by their elements; plain text (as
    /// StrongsHebrew) is unwrapped and divided at its empty lines.
    /// </summary>
    public static string ToMarkdown(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return "";
        return text.StartsWith('<') ? FromXml(text) : FromPlainText(text);
    }

    static string FromXml(string text)
    {
        try
        {
            var root = XElement.Parse("<root>" + text + "</root>");
            var sb = new StringBuilder();
            foreach (var node in root.Nodes()) Append(sb, node, inDefinition: false);
            return Tidy(sb.ToString());
        }
        catch (XmlException)
        {
            // Not well-formed (HTML-like markup, as in the lexicons of e-Sword): bold and italics are kept, the other tags go.
            text = Regex.Replace(text, @"<(b|strong)\b[^>]*>(.*?)</\1>", m => Emphasis(m.Groups[2].Value, "**"), RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<(i|em)\b[^>]*>(.*?)</\1>", m => Emphasis(m.Groups[2].Value, "*"), RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<(lb|br|p)\b[^>]*/?>", "\n\n", RegexOptions.IgnoreCase);
            return Tidy(System.Net.WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", "")));
        }
    }

    static void Append(StringBuilder sb, XNode node, bool inDefinition)
    {
        if (node is XText t)
        {
            sb.Append(Regex.Replace(System.Net.WebUtility.HtmlDecode(t.Value), @"\s+", " "));
            return;
        }
        if (node is not XElement e) return;

        string Inner(bool definition)
        {
            var inner = new StringBuilder();
            foreach (var child in e.Nodes()) Append(inner, child, definition);
            return inner.ToString().Trim();
        }

        var name = e.Name.LocalName;
        var rend = (string?)e.Attribute("rend");
        switch (name)
        {
            case "lb":
            case "br":
            case "p":
                sb.Append(inDefinition ? "\n\n" : " ");
                break;
            case "orth":
                // the headword in the original script is bold; the same word in ASCII ("writing") is left out
                var type = (string?)e.Attribute("type");
                if (type == "writing") break;
                var orth = Inner(inDefinition);
                if (orth.Length > 0) sb.Append(type == null ? $" **{orth}** " : $" {orth} ");
                break;
            case "pron":
                var pron = Inner(inDefinition);
                if (pron.Length > 0) sb.Append($" *{pron}* ");
                break;
            case "def":
                sb.Append("\n\n").Append(Inner(true)).Append("\n\n");
                break;
            case "hi" or "emph" or "i" or "b":
                var styled = Inner(inDefinition);
                if (styled.Length == 0) break;
                if (rend == "bold" || name == "b") sb.Append($"**{styled}**");
                else sb.Append($"*{styled}*");
                break;
            default:
                foreach (var child in e.Nodes()) Append(sb, child, inDefinition);
                break;
        }
    }

    static string FromPlainText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).ToList();
        var sb = new StringBuilder();

        // The first line has the number, the transliteration and the pronunciation: " 1  'ab  awb".
        int start = 0;
        while (start < lines.Count && lines[start].Length == 0) start++;
        if (start < lines.Count)
        {
            var header = Regex.Match(lines[start], @"^\d+\s+(?<trans>\S.*?)\s{2,}(?<pron>\S.*)$");
            if (header.Success)
            {
                sb.Append($"**{header.Groups["trans"].Value}** *{header.Groups["pron"].Value}*");
                start++;
            }
        }

        // The lines are wrapped at a fixed width; the paragraphs are divided by empty lines, and "see HEBREW for 01"
        // is a paragraph of its own.
        var paragraph = new List<string>();
        void Flush()
        {
            if (paragraph.Count > 0) sb.Append("\n\n").Append(string.Join(' ', paragraph));
            paragraph.Clear();
        }
        for (int i = start; i < lines.Count; i++)
        {
            if (lines[i].Length == 0) { Flush(); continue; }
            if (Regex.IsMatch(lines[i], @"^see (HEBREW|GREEK) for\b", RegexOptions.IgnoreCase)) Flush();
            paragraph.Add(lines[i]);
        }
        Flush();
        return Tidy(sb.ToString());
    }

    /// <summary>Single spaces, no spaces around line breaks, one empty line between paragraphs, no outer white space.</summary>
    /// <summary>Markdown emphasis; the spaces at the ends of the content stay outside the marks, and nothing is marked that is empty.</summary>
    static string Emphasis(string content, string mark)
    {
        var trimmed = Regex.Replace(content, "<[^>]+>", "").Trim();
        if (trimmed.Length == 0) return content;
        return (content.Length > 0 && char.IsWhiteSpace(content[0]) ? " " : "") + mark + trimmed + mark + (content.Length > 0 && char.IsWhiteSpace(content[^1]) ? " " : "");
    }

    static string Tidy(string text)
    {
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" ?\n ?", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}

/// <summary>An entry of the Strong's database: the Strong's number ("G26", "H7225") and its definition as markdown.</summary>
public sealed class StrongsEntry
{
    /// <summary>The Strong's number: "G" (Greek) or "H" (Hebrew) and the number without leading zeros.</summary>
    public string Id { get; set; } = "";
    public string Markdown { get; set; } = "";
}

/// <summary>
/// The Strong's dictionaries of a Bible, in a LiteDB database (one file, collection <c>strongs</c>, the Strong's number
/// as the id of every entry). Greek and Hebrew entries are in the same database, so a number such as "G26" is looked up
/// without knowing which dictionary it is in; importing a dictionary again replaces its entries.
/// </summary>
public sealed class StrongsDatabase : IDisposable
{
    public const string FileName = "strongs.db";
    const string Collection = "strongs";

    readonly LiteDB.LiteDatabase database;
    readonly LiteDB.ILiteCollection<StrongsEntry> entries;

    StrongsDatabase(string path, bool readOnly)
    {
        database = new LiteDB.LiteDatabase(new LiteDB.ConnectionString
        {
            Filename = path,
            Connection = LiteDB.ConnectionType.Direct,
            ReadOnly = readOnly,
        });
        entries = database.GetCollection<StrongsEntry>(Collection);
    }

    /// <summary>Opens the database to read from it (it must exist).</summary>
    public static StrongsDatabase Open(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The Strong's database was not found.", path);
        return new StrongsDatabase(path, readOnly: true);
    }

    /// <summary>Adds the entries (a Strong's number and its markdown) to the database, which is created if it is missing; existing entries are replaced.</summary>
    public static void Write(string path, IEnumerable<(string Key, string Markdown)> entries)
    {
        using var database = new StrongsDatabase(path, readOnly: false);
        database.entries.Upsert(entries.Select(e => new StrongsEntry { Id = e.Key, Markdown = e.Markdown }));
    }

    /// <summary>The number of entries.</summary>
    public int Count => entries.Count();

    /// <summary>The markdown of an entry ("G26"), or null if there is none.</summary>
    public string? Get(string key) => entries.FindById(key)?.Markdown;

    public bool Contains(string key) => entries.Exists(e => e.Id == key);

    /// <summary>The key of a Strong's number: "G26" or "H7225"; the number may be padded with zeros ("00026" with prefix "G").</summary>
    public static string? NormalizeKey(string key, string? defaultPrefix)
    {
        var m = Regex.Match(key.Trim(), @"^(?<prefix>[GHgh]?)0*(?<number>[0-9]+)$");
        if (!m.Success) return null;
        var prefix = m.Groups["prefix"].Length > 0 ? m.Groups["prefix"].Value.ToUpperInvariant() : defaultPrefix;
        if (prefix == null || !int.TryParse(m.Groups["number"].Value, out var number) || number == 0) return null;
        return prefix + number;
    }

    public void Dispose() => database.Dispose();
}
