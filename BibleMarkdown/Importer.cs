using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading.Tasks;
using System.Diagnostics.Contracts;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using System.Reflection.Metadata.Ecma335;

namespace BibleMarkdown;

public class Importer: WithTransform
{
    public Importer(Transform transform) : base(transform) { }
    public bool Imported { get; set; } = false;
    public Func<string, string>? PreprocessImportUSFM { get; set; } = null;
    public Func<string, string>? Postprocess { get; set; } = null;
    public bool ImportStrongs { get; set; } = true;
    public bool EachVerseOnNewLine { get; set; } = true;
    public bool LowercaseFirstWords { get; set; } = false;
    public bool Disabled { get; set; } = false;

    public virtual Importer All()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        Books.Load();

        return FromZip()
        .ImportInfo()
        .FromBibleEdit()
        .FromUSFM()
        .FromTXT()
        .FromZefania()
        .FromXmlOther()
        .FromSWORD()
        .FromESword()
        .FromMySword()
        .FromJavascripture()
        .FromBibleMarkdown()
        .ParallelVerses()
        .Outline()
        .PostprocessMarkdown()
        .ImportInfo();
    }

    public Importer PostprocessMarkdown()
    {
        if (Disabled || Postprocess == null) return this;

        var sources = Directory.EnumerateFiles(ProjectPath)
            .Where(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(Path.GetFileName(file), "^[0-9][0-9.]*-"));
        if (sources.Any())
        {
            Log("Import Postprocess");

            foreach (var file in sources)
            {
                var text = File.ReadAllText(file);
                text = Postprocess(text);
                File.WriteAllText(file, text);
            }
        }
        return this;
    }

    /// <summary>The name of the file that stores the <see cref="InstallableBook"/> entry of a Bible, in the folder of its books.</summary>
    public const string InfoFileName = "info.json";

    /// <summary>
    /// Reads the <see cref="InstallableBook"/> entry (name, language, country, class ...) that <c>info.json</c> stores.
    /// </summary>
    /// <param name="path">The folder of the Bible (it has the info.json), or the info.json itself.</param>
    /// <returns>The entry, or null when there is no info.json.</returns>
    /// <exception cref="InvalidDataException">The file is not valid JSON for an entry.</exception>
    public InstallableBook? ReadInfo(string path)
    {
        var file = Directory.Exists(path) ? Path.Combine(path, InfoFileName) : path;
        if (!File.Exists(file)) return null;
        try
        {
            var json = File.ReadAllText(file, Encoding.UTF8);
            // The class may be stored as its name ("Bible") or its number.
            return JsonConvert.DeserializeObject<InstallableBook>(json, new JsonSerializerSettings
            {
                Converters = { new Newtonsoft.Json.Converters.StringEnumConverter() },
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{file} is not a valid {InfoFileName}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Copies the <see cref="InfoFileName"/> of the sources (in <paramref name="srcpath"/>, or its bibmark folder) to the
    /// folder of the books, <paramref name="mdpath"/>, where <see cref="ReadInfo"/> finds it. An info.json that is already
    /// there is replaced.
    /// </summary>
    /// <returns>Whether there was an info.json to copy.</returns>
    public Importer ImportInfo()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var source = (new[] { base.SourcePath, Path.Combine(base.SourcePath, "bibmark") })
            .Select(folder => Path.Combine(folder, InfoFileName))
            .FirstOrDefault(File.Exists);
        if (source == null) return this;

        Directory.CreateDirectory(base.ProjectPath);
        var target = Path.Combine(base.ProjectPath, InfoFileName);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(source, target, overwrite: true);
        LogFile(target);
        return this;
    }
    public Importer ImportBookNames()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        const string BooknamesFile = "booknames.xml";
        var source = Path.Combine(base.SourcePath, BooknamesFile);
        Directory.CreateDirectory(base.ProjectPath);
        if (File.Exists(source)) File.Copy(source, Path.Combine(base.ProjectPath, BooknamesFile), true);
        return this;
    }

    public Importer FromBibleMarkdown()
    {
        if (Disabled || !Directory.Exists(base.SourcePath)) return this;

        var srcpath = base.SourcePath;
        var bmpath = Path.Combine(srcpath, "bibmark");
        if (!Directory.Exists(bmpath)) return this;
        var sources = Directory.EnumerateFiles(bmpath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file => Regex.IsMatch(Path.GetFileName(file), @".md$|.db$|.json$|.xml$|^script.cs$",
                RegexOptions.Singleline | RegexOptions.IgnoreCase));
        if (sources.Any())
        {
            Log("Import from Bible Markdown");

            Imported = true;

            foreach (var source in sources)
            {
                var dest = Path.Combine(base.ProjectPath, Path.GetFileName(source));

                if (source.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                { // binary file
                    var srcbin = File.ReadAllBytes(source);
                    var destbin= File.Exists(dest) ? File.ReadAllBytes(dest) : null;
                    var equal = true;
                    if (destbin == null) equal = false;
                    else if (srcbin.Length != destbin.Length) equal = false;
                    else
                    {
                        for (int i = 0; i < srcbin.Length; i++)
                        {
                            equal &= srcbin[i] == destbin[i];
                        }
                    }
                    if (!equal)
                    {
                        File.Copy(source, dest, true);
                        LogFile(dest);
                    }
                }
                else
                { // text file
                    var srctext = File.ReadAllText(source);
                    var desttext = File.Exists(dest) ? File.ReadAllText(dest) : null;
                    if (srctext != desttext)
                    {
                        File.Copy(source, dest, true);
                        LogFile(dest);
                    }
                }
            }
            // copy script file from source
            var script = Path.Combine(bmpath, "src", "script.cs");
            if (File.Exists(script)) File.Copy(script, Path.Combine(SourcePath, "script.cs"), true);
        }

        return this;
    }

    public Importer FromZip()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var sources = Directory.EnumerateFiles(SourcePath)
            .Where(file => !Path.GetFileName(file).Equals("booknames.xml", StringComparison.InvariantCultureIgnoreCase))
            .ToList();
        if (sources.Count == 1 && sources[0].EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
        {
            var zipfile = sources[0];
            ZipFile.ExtractToDirectory(zipfile, SourcePath, true);
        }
        return this;
    }

    // The usfm book ids in the order of the book numbers 1..66.
    static readonly string[] UsfmBookIds = {
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH", "2CH", "EZR", "NEH", "EST",
        "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO", "OBA", "JON", "MIC", "NAM",
        "HAB", "ZEP", "HAG", "ZEC", "MAL", "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL",
        "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD", "REV" };

    // lower case, umlauts transliterated, only letters and digits: "1. Mose" == "1 MOSE", "Sprüche" == "SPRUECHE"
    string NormalizeBookName(string name) =>
        Regex.Replace(name.ToLower(BookList.Culture(Language)).Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss"), @"[^\p{L}\p{N}]", "");

    public Importer FromUSFM()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var sources = Directory.EnumerateFiles(SourcePath)
            .Where(file => file.EndsWith(".usfm"));

        if (sources.Any())
        {
            Imported = true;

            int bookno = 1;
            string booknostr = "00.1";
            foreach (var source in sources)
            {
                var src = File.ReadAllText(source);

                src = PreprocessImportUSFM?.Invoke(src) ?? src;

                var bookm = Regex.Matches(src, @"(\\h|\\toc1|\\toc2|\\toc3)\s+(.*?)$", RegexOptions.Multiline)
                    .Select(m => m.Groups[2].Value.Trim())
                    .OrderBy(name => name.Where(ch => ch == ' ').Count())
                    .ThenBy(b => b.Count(ch => char.IsUpper(ch)))
                    .ThenBy(b => b.Length)
                    .ToArray();
                var book = bookm
                    .FirstOrDefault();

                var namesfile = Path.Combine(ProjectPath, "src", "booknames.xml");
                var useNames = File.Exists(namesfile);

                XElement[] xmlbooks = new XElement[0];

                var books = Books[Language].Values.ToArray();

                if (useNames)
                {
                    var book2 = books.FirstOrDefault(b => bookm.Any(bm => BookList.SameName(Language, b.Name, bm)));
                    // The names in the usfm are often in capitals or transliterated (1 MOSE, SPRUECHE, ROEMERS), so they do
                    // not equal the names in booknames.xml. Find the book by its usfm \id code (GEN, EXO ...), then by the
                    // normalized name, to get the number and the name of booknames.xml.
                    if (book2 == null)
                    {
                        var idm = Regex.Match(src, @"\\id\s+(?<id>[A-Za-z0-9]{3})");
                        var bookbyid = books.FirstOrDefault(b => b.Number == id);
                        int idno = idm.Success ? Array.IndexOf(UsfmBookIds, idm.Groups["id"].Value.ToUpperInvariant()) + 1 : 0;
                        if (idno > 0) book2 = books.FirstOrDefault(b => b.Number == idno);
                    }
                    if (book2 == null)
                    {
                        var normalized = bookm.Select(NormalizeBookName).ToArray();
                        book2 = books.FirstOrDefault(b => normalized.Contains(NormalizeBookName(b.Name)));
                    }
                    if (book2 != null)
                    {
                        book = book2.Name;
                        bookno = book2.Number;
                        booknostr = $"{bookno:d2}";
                    }
                    else
                    {
                        booknostr = $"00.{bookno:d2}";
                    }
                }
                else
                {
                    var filename = Path.GetFileNameWithoutExtension(source);
                    book = Books.Name(filename);
                    var booknumber = Books.Number(filename);
                    if (booknumber >= 0) bookno = booknumber;
                    booknostr = $"{bookno:d2}";
                }

                src = Regex.Match(src, @"\\c\s+[0-9]+.*", RegexOptions.Singleline).Value; // remove header that is not content of a chapter

                src = src.Replace("\r", "").Replace("\n", ""); // remove newlines

                src = Regex.Replace(src, @"(?<=\\c\s+[0-9]+\s*(\\s[0-9]+\s+[^\\]*?)?)\\p", ""); // remove empty paragraph after chapter

                src = src.Replace("[", "\\[").Replace("]", "\\]"); // escape [ and ]

                src = Regex.Replace(src, @"\\m?s(?<level>[0-9]+)\s*(?<text>[^\\$]+)", m => // section titles
                {
                    int n = 1;
                    int.TryParse(m.Groups["level"].Value, out n);
                    n++;
                    return $"{new String('#', n)} {m.Groups["text"].Value.Trim()}{Environment.NewLine}";
                }, RegexOptions.Singleline);

                src = Regex.Replace(src, @"\\sp\s+(.*?)(?=\s*\\(?!w))", $"\\{Environment.NewLine}*$1* \\{Environment.NewLine}", RegexOptions.Singleline); // speaker headings

                bool firstchapter = true;
                src = Regex.Replace(src, @"\\c\s+([0-9]+\s*)", m => // chapters
                {
                    var res = firstchapter ? $"# {m.Groups[1].Value}{Environment.NewLine}" : $"{Environment.NewLine}{Environment.NewLine}# {m.Groups[1].Value}{Environment.NewLine}";
                    firstchapter = false;
                    return res;
                });

                if (ParagraphVerses) src = Regex.Replace(src, @"\\v\s+([0-9]+)", "@$1"); // verse numbers
                else src = Regex.Replace(src, @"\\v\s+([0-9]+)", "^$1^"); // verse numbers

                // footnotes
                int n = 0;
                bool replaced;
                do
                {
                    replaced = false;
                    src = Regex.Replace(src, @"(?<=(?<dotbefore>[.:;?!¿¡])?)\\(?<type>[fx])\s*[+-?]\s*(?<footnote>.*?)\\\k<type>\*(?=(?<spaceafter>\s)?)(?<body>.*?(?=\s*#|\\p|$))", m =>
                    {
                        var space = n == 0 ? Environment.NewLine : " ";
                        var spacebefore = m.Groups["dotbefore"].Success ? "" : "";
                        var spaceafter = m.Groups["spaceafter"].Success ? "" : " ";
                        replaced = true;
                        var foottxt = m.Groups["footnote"].Value;
                        foottxt = Regex.Replace(foottxt, @"([0-9]+)[:,]\s+([0-9]+)", "$1:$2", RegexOptions.Singleline);
                        return $"{spacebefore}^{FootnoteLabel(n)}^{spaceafter}{m.Groups["body"].Value}{space}^{FootnoteLabel(n)}^[{foottxt}]";
                    }, RegexOptions.Singleline);
                    n++;
                } while (replaced);

                src = Regex.Replace(src, @"\\p[ \t]*", $"{Environment.NewLine}{Environment.NewLine}"); // replace new paragraph with empty line

                if (ImportStrongs)
                {
                    src = Regex.Replace(src, @"\\w(.*?)\\w\*", m =>
                    {
                        var word = m.Groups[1].Value;
                        var param = Regex.Match(word, @"(.*?)\|(.*)");
                        if (param.Success)
                        {
                            word = param.Groups[1].Value;
                            var p = $" {param.Groups[2].Value}";

                            var strongs = Regex.Matches(p, @" strong\s*=\s*""(\s*[A-Z][0-9]+)+\s*""")
                                .SelectMany(m => m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            var morph = Regex.Match(p, @" morph\s*=\s*""([^""]+)""");
                            var lemma = Regex.Match(p, @" lemma\s*=\s*""([^""]+)""");

                            var tokens = new List<string>();

                            tokens.AddRange(word.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            if (lemma.Success) tokens.Add($"&{lemma.Groups[1].Value}");
                            tokens.AddRange(strongs);
                            if (morph.Success) tokens.Add($"%{morph.Groups[1].Value}");

                            return string.Join('_', tokens);
                        }
                        return m.Value;
                    }, RegexOptions.Singleline);
                }

                src = Regex.Replace(src, @"\|([a-zA-Z-]+=""[^""]*""\s*)+", ""); // remove word attributes
                src = Regex.Replace(src, @"\\em\s*(.*?)\s*\\em\*", "*$1*", RegexOptions.Singleline); // italics
                src = Regex.Replace(src, @"\\bd\s*(.*?)\s*\\bd\*", "**$1**", RegexOptions.Singleline); // bold
                src = Regex.Replace(src, @"\\it\s*(.*?)\s*\\it\*", "*$1*", RegexOptions.Singleline); //italics
                src = Regex.Replace(src, @"\\sc\s*(.*?)\s*\\sc\*", "[$1]{.smallcaps}", RegexOptions.Singleline); // smallcaps
                src = Regex.Replace(src, @"\\wj\s*(.*?)\s*\\wj\*", "[$1]{.wj}", RegexOptions.Singleline); // words of jesus
                                                                                                          //src = Regex.Replace(src, @"\\nd\s*(.*?)\s*\\nd\*", "[$1]{.smallcaps}", RegexOptions.Singleline); // name of God
                src = Regex.Replace(src, @"\\nd\s*(.*?)\s*\\nd\*", match =>
                {
                    return match.Value.ToUpper();
                }, RegexOptions.Singleline); // name of God
                src = Regex.Replace(src, @"\\\+?\w+(\*|[ \t]*)?", "", RegexOptions.Singleline); // remove usfm tags
                src = Regex.Replace(src, @" +", " "); // remove multiple spaces
                src = Regex.Replace(src, @"\^\[([0-9]+)[.,:]([0-9]+)", "^[**$1:$2**"); // bold verse references in footnotes
                src = Regex.Replace(src, @"((?<![0-9]+|\s|{)\.(?![0-9]+)|\?|!|;|(?<![0-9]+):(?![0-9]+)|(?<![0-9]+),(?![0-9]+))(\w|“|¿|¡)", "$1 $2"); // Add space after dot
                src = Regex.Replace(src, @"(?<!^|(?:^|\n)[ \t]*\r?\n|(?:^|\n)#+[ \t]+[^\n]*\n)#", $"{Environment.NewLine}#", RegexOptions.Singleline); // add blank line over title
                if (LowercaseFirstWords) // needed for ReinaValera1909, it has uppercase words on every beginning of a chapter
                {
                    src = Regex.Replace(src, @"(\^1\^ \w)(\w*)", m => $"{m.Groups[1].Value}{m.Groups[2].Value.ToLower()}");
                    src = Regex.Replace(src, @"(\^1\^ \w )(\w*)", m => $"{m.Groups[1].Value}{m.Groups[2].Value.ToLower()}");
                }

                src = Regex.Replace(src, @"\^[0-9]+\^(?=\s*(\^[0-9]+\^|#|$|\^[a-zA-Z]\^\[))", "", RegexOptions.Singleline); // remove empty verses
                src = Regex.Replace(src, @"@[0-9]+(?=\s*(@[0-9]+|#|$|\^[a-zA-Z]\^\[))", "", RegexOptions.Singleline); // remove empty verses
                src = Regex.Replace(src, @"(?<!\s|^)(\^[0-9]+\^)", " $1", RegexOptions.Singleline);
                src = Regex.Replace(src, @"(?<=(^|\n)#[ \t]+[0-9]+[ \t]*\r?\n)(##.*?\r?\n)?(?<verse0>.*?)(?=\s*\^1\^)", match => // set italics on verse 0
                {
                    if (match.Value.Length > 0)
                    {
                        return $@"*{match.Value}* \{Environment.NewLine}";
                    }
                    else return match.Value;
                }, RegexOptions.Singleline);
                if (EachVerseOnNewLine)
                {
                    src = Regex.Replace(src, @"(?<!^)(\^[0-9]+\^)", $"{Environment.NewLine}$1", RegexOptions.Multiline);
                    src = Regex.Replace(src, @"(?<!^)(@[0-9]+)", $"{Environment.NewLine}$1", RegexOptions.Multiline);
                }

                var md = Path.Combine(ProjectPath, $"{booknostr}-{book}.md");
                bookno++;
                if (!string.IsNullOrWhiteSpace(src))
                {
                    File.WriteAllText(md, src);
                    LogFile(md);
                }
            }
        }
        return this;
    }

    public Importer FromBibleEdit()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var root = Path.Combine(SourcePath, "bibleedit");
        if (Directory.Exists(root))
        {
            Log("Import from BibleEdit");

            var oldfiles = Directory.EnumerateFiles(SourcePath, "*.usfm");
            foreach (var of in oldfiles) File.Delete(of);

            var folders = Directory.EnumerateDirectories(root).ToArray();
            if (folders.Length == 1) folders = Directory.EnumerateDirectories(Path.Combine(folders[0])).ToArray();

            var namesfile = Path.Combine(SourcePath, "booknames.xml");
            var books = Books[Language].Values;

            int fileno = 1;

            foreach (var folder in folders)
            {
                var chapters = Directory.EnumerateDirectories(folder).ToArray();
                int i = 0;
                chapters = chapters.OrderBy(f =>
                {
                    var name = Path.GetFileName(f);
                    int n;
                    if (!int.TryParse(name, out n)) n = i;
                    i++;
                    return n;
                })
                .ToArray();

                var files = chapters
                    .Select(ch => File.ReadAllText(Path.Combine(ch, "data")))
                    .ToArray();

                var bookm = Regex.Matches(files[0], @"(\\h|\\toc1|\\toc2|\\toc3)\s+(.*?)$", RegexOptions.Multiline)
                    .Select(m => m.Groups[2].Value.Trim())
                    .ToArray();
                var book = books
                    .Where(e => bookm.Any(b => string.Compare(e.Name, b, true) == 0))
                    .FirstOrDefault();
                int index = fileno++;
                string bookname;
                if (book != null)
                {
                    bookname = book.Name;
                    index = book.Number;
                }
                else
                {
                    bookname = bookm.FirstOrDefault();
                }

                var txt = new StringBuilder();
                foreach (string file in files)
                {
                    txt.AppendLine(file);
                }

                var usfmfile = Path.Combine(SourcePath, $"{index:d2}-{bookname}.usfm");
                File.WriteAllText(usfmfile, txt.ToString());
                LogFile(usfmfile);
            }
        }

        return this;
    }
    public Importer FromTXT()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var sources = Directory.EnumerateFiles(SourcePath)
            .Where(file => file.EndsWith(".txt"));

        if (sources.Any())
        {

            //var mdtimes = Directory.EnumerateFiles(mdpath)
            //	.Select(file => File.GetLastWriteTimeUtc(file));
            //var sourcetimes = sources.Select(file => File.GetLastWriteTimeUtc(file));

            //var mdtime = DateTime.MinValue;
            //var sourcetime = DateTime.MinValue;

            //foreach (var time in mdtimes) mdtime = time > mdtime ? time : mdtime;
            //foreach (var time in sourcetimes) sourcetime = time > sourcetime ? time : sourcetime;

            Imported = true;

            int bookno = 1;
            string? book = null;
            string? chapter = null;
            string md;

            var s = new StringBuilder();
            foreach (var source in sources)
            {
                var src = File.ReadAllText(source);
                var matches = Regex.Matches(src, @"(?:^|\n)(.*?)\s*([0-9]+):([0-9]+)(.*?)(?=$|\n[^\n]*?[0-9]+:[0-9]+)", RegexOptions.Singleline);

                foreach (Match m in matches)
                {
                    var bk = m.Groups[1].Value;
                    if (book != bk)
                    {
                        if (book != null)
                        {
                            md = Path.Combine(ProjectPath, $"{bookno++:D2}-{book}.md");
                            File.WriteAllText(md, s.ToString());
                            LogFile(md);
                            s.Clear();
                        }
                        book = bk;
                    }

                    var chap = m.Groups[2].Value;
                    if (chap != chapter)
                    {
                        chapter = chap;
                        if (chapter != "1")
                        {
                            s.AppendLine();
                            s.AppendLine();
                        }
                        s.AppendLine($"# {chapter}");
                    }

                    string verse = m.Groups[3].Value;
                    string text = Regex.Replace(m.Groups[4].Value, @"\r?\n", " ").Trim();
                    if (ParagraphVerses)
                    {
                        s.Append($"{(verse == "1" ? "" : (EachVerseOnNewLine ? $"{Environment.NewLine}" : " "))}@{verse} {text}");
                    }
                    else
                    {
                        s.Append($"{(verse == "1" ? "" : (EachVerseOnNewLine ? $"{Environment.NewLine}" : " "))}^{verse}^ {text}");
                    }
                }
            }
            md = Path.Combine(ProjectPath, $"{bookno++:D2}-{book}.md");
            File.WriteAllText(md, s.ToString());
            LogFile(md);
        }

        return this;
    }

    public Importer FromZefania()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var sources = Directory.EnumerateFiles(SourcePath)
            .Where(file => file.EndsWith(".xml"));

        //var mdtimes = Directory.EnumerateFiles(mdpath)
        //	.Select(file => File.GetLastWriteTimeUtc(file));
        //var sourcetimes = sources.Select(file => File.GetLastWriteTimeUtc(file));

        //var mdtime = DateTime.MinValue;
        //var sourcetime = DateTime.MinValue;

        //foreach (var time in mdtimes) mdtime = time > mdtime ? time : mdtime;
        //foreach (var time in sourcetimes) sourcetime = time > sourcetime ? time : sourcetime;

        foreach (var source in sources)
        {
            using (var stream = File.Open(source, FileMode.Open, FileAccess.Read))
            {
                var root = XElement.Load(stream);

                if (root.Name != "XMLBIBLE") continue;

                Parallel.ForEach(root.Elements("BIBLEBOOK"), book =>
                {
                    Imported = true;

                    StringBuilder text = new StringBuilder();
                    var file = $"{((int)book.Attribute("bnumber")):D2}-{(string)book.Attribute("bname")}.md";
                    var firstchapter = true;

                    foreach (var chapter in book.Elements("CHAPTER"))
                    {
                        if (!firstchapter)
                        {
                            text.AppendLine(""); text.AppendLine();
                        }
                        firstchapter = false;
                        text.Append($"# {((int)chapter.Attribute("cnumber"))}{Environment.NewLine}");
                        var firstverse = true;

                        foreach (var verse in chapter.Elements("VERS"))
                        {
                            if (!firstverse)
                            {
                                if (EachVerseOnNewLine) text.AppendLine();
                                else text.Append(" ");
                            }
                            firstverse = false;
                            if (ParagraphVerses) text.Append($"@{((int)verse.Attribute("vnumber"))} ");
                            else text.Append($"^{((int)verse.Attribute("vnumber"))}^ ");
                            text.Append(verse.Value);
                        }
                    }

                    var md = Path.Combine(ProjectPath, file);
                    File.WriteAllText(md, text.ToString());
                    LogFile(md);
                });
            }
        }

        return this;
    }

    public Importer FromXmlOther()
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var sources = Directory.EnumerateFiles(SourcePath)
            .Where(file => file.EndsWith(".xml"));

        //var mdtimes = Directory.EnumerateFiles(mdpath)
        //	.Select(file => File.GetLastWriteTimeUtc(file));
        //var sourcetimes = sources.Select(file => File.GetLastWriteTimeUtc(file));

        //var mdtime = DateTime.MinValue;
        //var sourcetime = DateTime.MinValue;

        //foreach (var time in mdtimes) mdtime = time > mdtime ? time : mdtime;
        //foreach (var time in sourcetimes) sourcetime = time > sourcetime ? time : sourcetime;

        foreach (var source in sources)
        {
            using (var stream = File.Open(source, FileMode.Open, FileAccess.Read))
            {
                var root = XElement.Load(stream);

                if (root.Name != "bible") continue;

                int booknumber = 1;
                foreach (var book in root.Elements("b"))
                {
                    Imported = true;

                    StringBuilder text = new StringBuilder();
                    var file = $"{booknumber:D2}-{(string)book.Attribute("n")}.md";
                    var firstchapter = true;

                    foreach (var chapter in book.Elements("c"))
                    {
                        if (!firstchapter)
                        {
                            text.AppendLine(""); text.AppendLine();
                        }
                        firstchapter = false;
                        text.Append($"# {((int)chapter.Attribute("n"))}{Environment.NewLine}");
                        var firstverse = true;

                        foreach (var verse in chapter.Elements("v"))
                        {
                            if (!firstverse)
                            {
                                if (EachVerseOnNewLine) text.AppendLine();
                                else text.Append(" ");
                            }
                            firstverse = false;
                            if (ParagraphVerses) text.Append($"@{((int)verse.Attribute("n"))} ");
                            else text.Append($"^{((int)verse.Attribute("n"))}^ ");
                            text.Append(verse.Value);
                        }
                    }

                    var md = Path.Combine(ProjectPath, file);
                    File.WriteAllText(md, text.ToString());
                    LogFile(md);

                    booknumber++;
                }
            }
        }

        return this;
    }

    /// <summary>
    /// Imports a Bible module of The SWORD Project. Place the module in the SourcePath folder, as the folders mods.d (with the
    /// .conf file of the module) and modules (with its data), or as the zip file of the module, which is unpacked first.
    /// The module is read directly, no SWORD tools are needed. The drivers zText, zText4, RawText and RawText4 are
    /// supported (compressed with ZIP or LZSS, in UTF-8 or Latin-1), and the markup OSIS, GBF, ThML or Plain.
    /// Any versification works: KJV is built in, and for every other one the structure of books, chapters and verses
    /// is read from the module itself, which works for OSIS modules made with osis2mod (they mark their books and
    /// chapters). Footnotes, headings, paragraphs, poetry lines, italics and the words of Jesus are kept.
    /// With <paramref name="importBooknames"/>, the names of the books are read from the headings of the books in the
    /// module and written to booknames.xml in the src folder (an existing file is kept), and the books are named by
    /// them. A book whose heading has no usable name keeps its English name.
    /// </summary>
    public Importer FromSWORD(bool importBooknames = false)
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var confpath = Path.Combine(SourcePath, "mods.d");
        var zip = Directory.EnumerateFiles(SourcePath, "*.zip").FirstOrDefault();
        if (zip != null && !Directory.Exists(confpath))
        {
            ZipFile.ExtractToDirectory(zip, SourcePath, true);
        }

        if (!Directory.Exists(confpath)) return this;
        var confs = Directory.EnumerateFiles(confpath, "*.conf").Order().ToList();

        // Strong's dictionaries (and other dictionaries) are written to a binary file each; the Bible is the rest.
        foreach (var dictionary in confs.Where(SwordDictionary.IsDictionary).ToList())
        {
            FromSWORDDictionary(dictionary);
            confs.Remove(dictionary);
        }

        if (confs.Count == 0) return this;
        if (confs.Count > 1) Log($"There are several SWORD modules in {confpath}, only {Path.GetFileName(confs[0])} is imported.");

        try
        {
            var module = SwordModule.Open(confs[0], SourcePath);
            var versification = module.Versification;
            Log($"Import from SWORD module {module.Name} (versification {versification.Name})");

            var localNames = importBooknames ? SwordBookNames(module, versification) : null;
            if (localNames != null) WriteSwordBooknames(module, versification, localNames, SourcePath);

            foreach (var book in versification.Books)
            {
                if (!module.Has(book.NewTestament)) continue;

                var text = new StringBuilder();
                var nl = Environment.NewLine;

                for (int chapter = 1; chapter <= book.Chapters; chapter++)
                {
                    int notes = 0; // the footnotes are lettered a, b, c... in every chapter
                    string NextLabel() => FootnoteLabel(notes++);

                    var verses = new List<(int Number, SwordVerse Verse)>();
                    for (int number = 1; number <= book.Verses[chapter - 1]; number++)
                    {
                        var raw = module.Read(book.NewTestament, versification.Index(book, chapter, number));
                        if (raw.Length == 0) continue;
                        var verse = SwordMarkup.Convert(raw, module.SourceType, NextLabel, ImportStrongs);
                        if (verse.Text.Length > 0) verses.Add((number, verse)); // empty verses are removed, as the import of USFM does
                    }
                    if (verses.Count == 0) continue;

                    // The entry of verse 0 is the heading of the chapter: its titles and an introduction.
                    var heading = SwordMarkup.Convert(module.Read(book.NewTestament, versification.Index(book, chapter, 0)),
                        module.SourceType, NextLabel, ImportStrongs);

                    if (text.Length > 0) text.Append(nl).Append(nl);
                    text.Append($"# {chapter}{nl}");
                    foreach (var title in heading.Titles) text.Append($"## {title}{nl}");
                    if (heading.Text.Length > 0) text.Append($"*{heading.Text}* \\{nl}"); // like the verse 0 of the import of USFM

                    bool first = true;
                    foreach (var (number, verse) in verses)
                    {
                        if (verse.Titles.Count > 0)
                        {
                            if (!first) text.Append(nl).Append(nl);
                            foreach (var title in verse.Titles) text.Append($"## {title}{nl}");
                        }
                        else if (!first)
                        {
                            // a new paragraph is an empty line
                            if (verse.Paragraph) text.Append(nl).Append(nl);
                            else if (EachVerseOnNewLine) text.Append(nl);
                            else text.Append(' ');
                        }
                        if (verse.Psalm != null) text.Append($"*{verse.Psalm}* \\{nl}"); // the title of a psalm is verse 0

                        text.Append($"@{number} "); // the verse style of Bible Markdown (^1^ is legacy)
                        text.Append(verse.Text);
                        foreach (var (label, note) in verse.Notes) text.Append($" ^{label}^[{note}]"); // the footnotes follow their verse
                        first = false;
                    }
                }
                if (text.Length == 0) continue;

                var name = string.Concat((localNames != null && localNames.TryGetValue(book.Number, out var local) ? local.Name : SwordBookName(book)).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                var md = Path.Combine(ProjectPath, $"{book.Number:D2}-{name}.md");
                File.WriteAllText(md, Regex.Replace(text.ToString(), @"\r?\n", nl));
                Imported = true;
                LogFile(md);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException)
        {
            Log($"The SWORD module {Path.GetFileName(confs[0])} cannot be imported: {ex.Message}");
        }
        return this;
    }

    // ---- e-Sword and MySword ----------------------------------------------------------------------------

    /// <summary>
    /// Imports a Bible module of e-Sword (.bblx, .bbli) and a Strong's dictionary or lexicon of e-Sword (.dctx, .dcti, .lexi). Place the
    /// module, or the zip that has it, in the src folder. The files are SQLite databases, which are read directly (no
    /// SQLite library is needed). The books of the canon, 1 to 66, are imported, with their headings, footnotes, the words
    /// of Jesus and, if <see cref="ImportStrongs"/> is set, the Strong's numbers.
    /// </summary>
    public Importer FromESword() =>
        FromSqliteModules("e-Sword", [".bblx", ".bbli"], [".dctx", ".dcti", ".lexi"]);

    /// <summary>
    /// Imports a Bible module of MySword (.bbl.mybible) and a Strong's dictionary of MySword (.dct.mybible), like
    /// <see cref="FromESword"/> does for e-Sword: the two programs have the same format of Bible modules.
    /// </summary>
    public Importer FromMySword() =>
        FromSqliteModules("MySword", [".bbl.mybible"], [".dct.mybible"]);

    Importer FromSqliteModules(string program, string[] bibleExtensions, string[] dictionaryExtensions)
    {
        if (Disabled || !Directory.Exists(SourcePath)) return this;

        var extensions = bibleExtensions.Concat(dictionaryExtensions).ToArray();
        bool Is(string file, string[] of) => of.Any(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        List<string> Modules() => Directory.EnumerateFiles(SourcePath, "*", SearchOption.AllDirectories).Where(f => Is(f, extensions)).Order().ToList();
        var modules = Modules();
        if (modules.Count == 0)
        {
            // a zip that has such modules is unpacked (a zip of a SWORD module is left to FromSWORD)
            foreach (var zip in Directory.EnumerateFiles(SourcePath, "*.zip"))
            {
                using (var archive = ZipFile.OpenRead(zip))
                    if (!archive.Entries.Any(e => Is(e.FullName, extensions))) continue;
                ZipFile.ExtractToDirectory(zip, SourcePath, true);
            }
            modules = Modules();
        }
        if (modules.Count == 0) return this;

        foreach (var dictionary in modules.Where(f => Is(f, dictionaryExtensions)))
            ImportSqliteDictionary(program, dictionary);

        var bibles = modules.Where(f => Is(f, bibleExtensions)).ToList();
        if (bibles.Count > 1) Log($"There are several {program} Bibles in {SourcePath}, only {Path.GetFileName(bibles[0])} is imported.");
        if (bibles.Count > 0) ImportSqliteBible(program, bibles[0]);

        return this;
    }

    void ImportSqliteBible(string program, string file)
    {
        try
        {
            var database = SqliteFile.Open(file);
            Log($"Import from {program} module {Path.GetFileName(file)}{(ESwordModule.Description(database) is { } description ? $" ({description})" : "")}");

            // book -> chapter -> verse -> text
            var books = new SortedDictionary<int, SortedDictionary<int, SortedDictionary<int, string>>>();
            int skipped = 0;
            foreach (var (book, chapter, verse, text) in ESwordModule.Verses(database))
            {
                if (book < 1 || book > 66 || chapter < 1 || verse < 1) { if (book > 66) skipped++; continue; }
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!books.TryGetValue(book, out var chapters)) books[book] = chapters = new();
                if (!chapters.TryGetValue(chapter, out var verses)) chapters[chapter] = verses = new();
                verses[verse] = text;
            }
            if (skipped > 0) Log($"{skipped} verses of books after the 66 of the canon are not imported.");

            var nl = Environment.NewLine;
            foreach (var (number, chapters) in books)
            {
                var text = new StringBuilder();
                foreach (var (chapter, rawVerses) in chapters)
                {
                    int notes = 0; // the footnotes are lettered a, b, c... in every chapter
                    string NextLabel() => FootnoteLabel(notes++);

                    var verses = new List<(int Number, SwordVerse Verse)>();
                    bool paragraph = false;
                    foreach (var (verseNumber, raw) in rawVerses)
                    {
                        var verse = ESwordMarkup.Convert(raw, ImportStrongs, NextLabel, out var paragraphAfter);
                        if (verse.Text.Length == 0) { paragraph |= paragraphAfter; continue; } // empty verses are removed, as in the other imports
                        verse.Paragraph = paragraph;
                        paragraph = paragraphAfter;
                        verses.Add((verseNumber, verse));
                    }
                    if (verses.Count == 0) continue;

                    if (text.Length > 0) text.Append(nl).Append(nl);
                    text.Append($"# {chapter}{nl}");
                    bool first = true;
                    foreach (var (verseNumber, verse) in verses)
                    {
                        if (verse.Titles.Count > 0)
                        {
                            if (!first) text.Append(nl).Append(nl);
                            foreach (var title in verse.Titles) text.Append($"## {title}{nl}");
                        }
                        else if (!first)
                        {
                            if (verse.Paragraph) text.Append(nl).Append(nl); // a new paragraph is an empty line
                            else if (EachVerseOnNewLine) text.Append(nl);
                            else text.Append(' ');
                        }
                        text.Append($"@{verseNumber} ").Append(verse.Text);
                        foreach (var (label, note) in verse.Notes) text.Append($" ^{label}^[{note}]"); // the footnotes follow their verse
                        first = false;
                    }
                }
                if (text.Length == 0) continue;

                var book = SwordVersification.KJV.Books.First(b => b.Number == number);
                var name = string.Concat(SwordBookName(book).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                var mdfile = Path.Combine(ProjectPath, $"{number:D2}-{name}.md");
                File.WriteAllText(mdfile, Regex.Replace(text.ToString(), @"\r?\n", nl));
                Imported = true;
                LogFile(mdfile);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        {
            Log($"The {program} module {Path.GetFileName(file)} cannot be imported: {ex.Message}");
        }
    }

    /// <summary>Imports a Strong's dictionary of e-Sword or MySword to the database strongs.db, like the one of SWORD.</summary>
    void ImportSqliteDictionary(string program, string file)
    {
        try
        {
            var database = SqliteFile.Open(file);
            var description = ESwordModule.Description(database);
            Log($"Import from {program} dictionary {Path.GetFileName(file)}{(description != null ? $" ({description})" : "")}");

            // The keys are G26 or H7225, or plain numbers: then the module tells whether it is the Greek or the Hebrew one.
            var marker = Path.GetFileName(file) + " " + description;
            string? prefix = Regex.IsMatch(marker, @"greek|griego|grec|griech", RegexOptions.IgnoreCase) ? "G"
                : Regex.IsMatch(marker, @"hebrew|hebreo|hébreu|hebr", RegexOptions.IgnoreCase) ? "H" : null;

            var entries = new List<(string Key, string Markdown)>();
            foreach (var (word, definition) in ESwordModule.Entries(database))
            {
                var key = StrongsDatabase.NormalizeKey(word, prefix);
                if (key == null) continue;
                var markdown = StrongsMarkup.ToMarkdown(definition);
                if (markdown.Length > 0) entries.Add((key, markdown));
            }
            if (entries.Count == 0)
            {
                Log($"The {program} dictionary {Path.GetFileName(file)} has no Strong's entries (keys such as G26 or H7225; plain numbers only in a module that is marked as Greek or Hebrew).");
                return;
            }

            var target = Path.Combine(ProjectPath, StrongsDatabase.FileName);
            if (File.Exists(target)) File.Delete(target);
            StrongsDatabase.Write(target, entries);
            Imported = true;
            LogFile(target);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or LiteDB.LiteException)
        {
            Log($"The {program} dictionary {Path.GetFileName(file)} cannot be imported: {ex.Message}");
        }
    }

    /// <summary>
    /// Imports a Strong's dictionary module of The SWORD Project (RawLD, RawLD4 or zLD, e.g. StrongsGreek and
    /// StrongsHebrew) to the LiteDB database <c>strongs.db</c> in <paramref name="mdpath"/>, see <see cref="StrongsDatabase"/>:
    /// every entry has its Strong's number ("G26", "H7225") as id and its definition as markdown. All imported
    /// dictionaries share the database. Entries whose key is not a Strong's number (the introduction of the module)
    /// are left out.
    /// </summary>
    void FromSWORDDictionary(string conf)
    {
        try
        {
            var dictionary = SwordDictionary.Open(conf, SourcePath);
            Log($"Import from SWORD dictionary {dictionary.Name}");
            if (dictionary.StrongsPrefix == null)
                Log($"The SWORD dictionary {dictionary.Name} is not marked as Greek or Hebrew: only entries with keys such as G26 or H7225 are imported.");

            var entries = new List<(string Key, string Markdown)>();
            foreach (var (key, text) in dictionary.Entries())
            {
                var strongs = StrongsDatabase.NormalizeKey(key, dictionary.StrongsPrefix);
                if (strongs == null) continue;
                var markdown = StrongsMarkup.ToMarkdown(text);
                if (markdown.Length > 0) entries.Add((strongs, markdown));
            }
            if (entries.Count == 0)
            {
                Log($"The SWORD dictionary {dictionary.Name} has no Strong's entries.");
                return;
            }

            var file = Path.Combine(ProjectPath, StrongsDatabase.FileName);
            if (File.Exists(file)) File.Delete(file);
            StrongsDatabase.Write(file, entries);
            Imported = true;
            LogFile(file);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException or LiteDB.LiteException)
        {
            Log($"The SWORD dictionary {Path.GetFileName(conf)} cannot be imported: {ex.Message}");
        }
    }

    /// <summary>
    /// The names of the books as the module itself gives them, by book number. The name is the short name of the title
    /// in the heading of the book (its <c>short</c> attribute, "1. Mose"), or the title when there is none; the
    /// description is the title ("Das 1. Buch Mose (Genesis)"). The abbreviation is made from the name, see
    /// <see cref="Abbreviate"/>. Books without a usable title are left out.
    /// </summary>
    Dictionary<int, (string Name, string Abbreviation, string Description)> SwordBookNames(SwordModule module, SwordVersification versification)
    {
        var titles = new List<(int Number, string Name, string Description)>();
        foreach (var book in versification.Books)
        {
            if (!module.Has(book.NewTestament)) continue;
            try
            {
                var raw = module.Read(book.NewTestament, versification.Index(book, 0, 0));
                var heading = SwordMarkup.Convert(raw, module.SourceType, () => "", false);
                // the first book of a testament also has the title of the testament before its own
                var title = heading.Titles.Select(t => Regex.Replace(t, @"\s+", " ").Trim()).LastOrDefault(t => t.Length > 0);
                if (title == null || title.Length > 60) continue;
                // The converted heading has no attributes: the short name is read from the markup (OSIS: <title short="...">).
                var shortName = Regex.Match(raw, @"<title\s[^>]*?short=""([^""]*)""", RegexOptions.IgnoreCase);
                var name = shortName.Success ? System.Net.WebUtility.HtmlDecode(shortName.Groups[1].Value).Trim() : "";
                if (name.Length == 0 || name.Length > 60) name = title;
                if (titles.Any(t => t.Name == name)) name = title; // not unique: the title tells the books apart
                if (titles.Any(t => t.Name == name)) continue;
                titles.Add((book.Number, name, title));
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or IOException or ArgumentException)
            {
                Log($"No book name for {book.Name} in the SWORD module: {ex.Message}");
            }
        }

        // The shortest abbreviation that is unique: 3 letters ("Mat"), longer for names that start alike.
        var names = new Dictionary<int, (string Name, string Abbreviation, string Description)>();
        for (int letters = 4; ; letters++)
        {
            var abbreviations = titles.Select(t => (t.Number, Abbreviation: Abbreviate(t.Name, letters))).ToList();
            var duplicates = abbreviations.GroupBy(a => a.Abbreviation).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            foreach (var t in titles)
            {
                var abbreviation = abbreviations.First(a => a.Number == t.Number).Abbreviation;
                if (!duplicates.Contains(abbreviation) || letters >= 8)
                    names.TryAdd(t.Number, (t.Name, abbreviation, t.Description));
            }
            if (names.Count == titles.Count) break;
        }
        return names;
    }

    /// <summary>
    /// An abbreviation of a book name: its number ("1. Mose" has "1") and the first letters of its name, 3 in all
    /// ("1Mo", "Mat"; "Matthäus" is "Mat"). More letters make longer abbreviations for names that start alike.
    /// </summary>
    public string Abbreviate(string name, int length) => Transform.Abbreviate(name, length);

    /// <summary>
    /// Writes booknames.xml in the src folder: the English names (with the OSIS ids) and the names of the module, which
    /// the reader matches by book number. An existing file is not overwritten.
    /// </summary>
    void WriteSwordBooknames(SwordModule module, SwordVersification versification, Dictionary<int, (string Name, string Abbreviation, string Description)> names, string srcpath)
    {
        var file = Path.Combine(srcpath, "booknames.xml");
        if (File.Exists(file)) { Log("booknames.xml exists already, it is kept."); return; }
        var books = versification.Books.Where(b => module.Has(b.NewTestament)).ToList();
        var language = string.IsNullOrWhiteSpace(module.Name) ? "module" : module.Name;
        var xml = new XElement("BOOKNAMES",
            new XAttribute("default", language),
            new XElement("ID", new XAttribute("descr", "English"),
                books.Select(b => new XElement("BOOK", new XAttribute("bnumber", b.Number), new XAttribute("bshort", b.OsisId), b.Name))),
            new XElement("ID", new XAttribute("descr", language),
                books.Select(b => names.TryGetValue(b.Number, out var local)
                    ? new XElement("BOOK", new XAttribute("bnumber", b.Number), new XAttribute("bshort", local.Abbreviation),
                        new XAttribute("desc", local.Description), local.Name)
                    : new XElement("BOOK", new XAttribute("bnumber", b.Number), new XAttribute("bshort", ""), b.Name))));
        xml.Save(file);
        Log($"Created {file} with {names.Count} book names and abbreviations from the SWORD module.");
    }

    /// <summary>The name of a book: from booknames.xml in the src folder if there is one, else the English name.</summary>
    string SwordBookName(SwordVersification.Book book)
    {
        if (Language != null && Books.TryGetValue(Language, out var names))
        {
            var named = names.Values.FirstOrDefault(b => b.Number == book.Number);
            if (named != null && !string.IsNullOrWhiteSpace(named.Name)) return named.Name;
        }
        return book.Name;
    }


    public Importer ParallelVerses(string path = null)
    {
        if (path == null) path = SourcePath;
        if (Disabled || !Directory.Exists(path)) return this;

        Outline items = new Outline(Transform);

        // import parallel verses
        int lastbook = -1, lastchapter = -11;
        foreach (var parverse in new ParallelVerses(Transform).Load(base.SourcePath))
        {
            StringBuilder footnote = new StringBuilder($"^[**{parverse.Verse.Chapter}:{(parverse.Verse.Verse > 1 ? parverse.Verse.Verse : 1)}** ");
            bool firstpv = true;
            foreach (var pv in parverse.ParallelVerses)
            {
                if (firstpv) firstpv = false;
                else footnote.Append("; ");
                if (pv.Verse <= 0) pv.Verse = 1;
                footnote.Append($"{pv.Book.Abbreviation} {pv.Chapter},{pv.Verse}");
                if (pv.UpToVerse.HasValue && pv.UpToVerse.Value > 0) footnote.Append($"-{pv.UpToVerse}");
            }
            footnote.Append("]");
            if (lastbook != parverse.Verse.Book.Number)
            {
                var file = $"{parverse.Verse.Book.Number:d2}-{parverse.Verse.Book.Name}.md";
                items.Add(new BookItem(parverse.Verse.Book, file));
                lastchapter = -1;
            }
            if (lastchapter != parverse.Verse.Chapter)
            {
                items.Add(new ChapterItem(parverse.Verse.Book, parverse.Verse.Chapter));
            }
            items.Add(new FootnoteItem(parverse.Verse.Book, footnote.ToString(), parverse.Verse.Chapter, parverse.Verse.Verse));
        }

        items.Append = true;
        if (items.Count > 0) items.Save(Path.Combine(path, "ParallelVerses.outline.md"));

        return this;
    }

    public Importer Outline()
    {
        if (!Disabled && Imported)
        {
            var mdfiles = Directory.EnumerateFiles(ProjectPath, "*.md")
                .Where(file => Regex.IsMatch(Path.GetFileName(file), @"^([0-9][0-9])(?!.*?\.outline\.md)"));

            Books.Load(mdfiles);

            Outline items = new Outline(Transform).Read(base.SourcePath);

            if (items.Count > 0)
            {
                Log("Importing Outline...");

                foreach (var srcfile in mdfiles)
                {

                    File.SetLastWriteTimeUtc(srcfile, DateTime.Now);
                    var src = File.ReadAllText(srcfile);

                    var modsrc = items.Apply(src, srcfile);

                    if (src != modsrc)
                    {
                        File.WriteAllText(srcfile, modsrc);
                        LogFile(srcfile);
                    }
                }
            }
        }

        return this;
    }

    public Importer FromJavascripture()
    {
        if (Disabled || !Directory.Exists(base.SourcePath)) return this;

        var json = System.IO.Directory.EnumerateFiles(SourcePath, "*.json")
            .Select(file => File.ReadAllText(file))
            .Where(src => src.Contains("\"version\":") && src.Contains("\"versionName\":") && src.Contains("\"books\":"))
            .Select(src => JObject.Parse(src))
            .FirstOrDefault();
        if (json == null) return this;
        var books = json["books"] as JObject;
        int bookno = 1;
        foreach (var book in books.Properties())
        {
            var name = book.Name;
            var bookInfo = Books[Language]?.FirstOrDefault(b => b.Key == name).Value;

            var sb = new StringBuilder();
            var phrase = new StringBuilder();
            int chapterno = 1;
            int verseno;
            foreach (var chapter in book.Value as JArray)
            {
                verseno = 1;
                sb.AppendLine($"# {chapterno++}");
                foreach (var versetoken in chapter as JArray)
                {
                    string verse;
                    if (versetoken is JArray versearr)
                    {
                        var words = versearr.Select(x => (string)(((JArray)x)[0]));
                        phrase.Clear();
                        foreach (var word in words)
                        {
                            if (phrase.Length > 0 && word != "." && word != ":" && word != "," && word != ";" && word != "?" && word != "!" && word != "¿" && word != "¡")
                            {
                                phrase.Append(" ");
                            }
                            phrase.Append(word);
                        }
                        verse = phrase.ToString();
                    }
                    else
                    {
                        verse = (string)versetoken;
                    }
                    verse = Regex.Replace(verse, "<[0-9A-Z]+>", "");
                    if (EachVerseOnNewLine) sb.AppendLine($"@{verseno++} {verse}");
                    else
                    {
                        if (verseno > 1) sb.Append(" ");
                        sb.Append($"@{verseno++} {verse}");
                    }
                }
                sb.AppendLine();
            }

            string mdfile;
            if (bookInfo != null) mdfile = Path.Combine(ProjectPath, $"{bookInfo.Number}-{bookInfo.Name}.md");
            else mdfile = $"{bookno++}-{name}.md";
            File.WriteAllText(mdfile, sb.ToString());
            LogFile(mdfile);
        }

        return this;
    }
}
