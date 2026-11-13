using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace BibleMarkdown;


/// <summary>
/// Publishes Bibles on a web server: every Bible (a folder of books, see <see cref="Importer"/>) becomes a zip, and an
/// index.json lists the zips with the <see cref="Importer.InfoFileName"/> of each, so a reader can show what there is to
/// install without downloading it. What the Bibles were converted to (their <c>out</c> folder: PDF, EPUB, HTML ...) is
/// published too, for people to download.
/// </summary>
public class Publisher : WithTransform
{
    public Publisher(Transform transform) : base(transform) { }

    /// <summary>The name of the file that lists the published Bibles, next to their zips.</summary>
    public const string IndexFileName = "index.json";

    /// <summary>The folder of the web root that has the zips of the Bibles and the index.json.</summary>
    public const string BooksFolder = "books";

    /// <summary>The folder of the web root that has the files that were made of the Bibles.</summary>
    public const string OutFolder = "out";

    /// <summary>The files of an out folder that are published; the others (logs, tex, aux, usfm files ...) are not.</summary>
    public static readonly string[] OutExtensions = { ".pdf", ".md", ".epub", ".html", ".xml", ".zip" };

    /// <summary>The folders of an out folder that are published as one zip each (their many small files are not copied).</summary>
    public static readonly string[] ZippedOutFolders = { "html", "tex", "usfm" };

    /// <summary>The folders of an out folder that are not published at all.</summary>
    public static readonly string[] SkippedOutFolders = { "pandoc" };

    /// <summary>
    /// Publishes the Bibles, the folders in SourcePath, into OutputPath:
    /// <list type="bullet">
    /// <item><c>books/Folder.zip</c>: the files that are directly in the folder of the Bible, not those of its subfolders
    /// (the sources in <c>src</c> are not published), and <c>books/index.json</c>, an array with an entry for every zip:
    /// <c>{ "File": "Folder.zip", "Info": { ... } }</c>, where Info is the info.json of the folder as it is, or null
    /// when there is none.</item>
    /// <item><c>out/Folder/...</c>: the files of the <c>out</c> folder of the Bible, with its subfolders, but only
    /// *.pdf, *.md, *.epub, *.html, *.xml and *.zip. The <c>out/html</c>, <c>out/tex</c> and <c>out/usfm</c> folders are
    /// not copied but zipped: the zip <c>out/Folder/usfm/Folder.usfm.zip</c> (likewise for html and tex) is all there is
    /// of such a folder. The <c>out/pandoc</c> folder is left out.</item>
    /// </list>
    /// A folder without books (files such as 01-Genesis.md) is skipped. What was published before for a Bible is replaced.
    /// Every folder of OutputPath, <c>books</c> and <c>out</c> also gets an index.html that lists its content.
    /// </summary>
    /// <returns>The names of the zips of the Bibles, in the order of the index.</returns>
    public List<string> Books(bool verbose = false)
    {
        if (!Directory.Exists(SourcePath)) throw new DirectoryNotFoundException($"Directory not found: {SourcePath}");
        var booksPath = Path.Combine(OutputPath, BooksFolder);
        var outPath = Path.Combine(OutputPath, OutFolder);
        Directory.CreateDirectory(booksPath);

        var index = new List<(string File, InstallableBook Info)>();
        var names = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(SourcePath)
            .Where(folder => !folder.StartsWith("."))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var info = ReadInfo(files);
            if (info == null || !info.Publish) continue;
            
            // Only a folder with books is a Bible the reader can read (01-Genesis.md ...); a folder with just a scan
            // or a PDF is not published.
            if (!files.Any(f => IsBook(f)))
            {
                Log($"Skipped {Path.GetFileName(folder)}: it has no books");
                continue;
            }

            var name = Path.GetFileName(folder) + ".zip";
            float prevratio = 0;
            Action<float> progress = null;
            if (verbose)
            {
                progress = (float ratio) =>
                {
                    if (ratio - prevratio > 0.1)
                    {
                        prevratio = ratio;
                        Log($"{(int)(ratio * 100)}%");
                    }
                    else Log(".");
                };
            }
            var zipfile = Path.Combine(booksPath, Uri.EscapeDataString(name));
            ZipFolder(folder, zipfile, SearchOption.AllDirectories,
                file => !file.Contains('/') || file == "src/script.cs" ? "bibmark/" + file : null, progress);
            Log($"Created {BooksFolder}/{name}");

            names.Add(name);
            index.Add((File: name, Info: info));

            PublishOut(Path.Combine(folder, "out"), Path.Combine(outPath, Path.GetFileName(folder)), Path.GetFileName(folder));
        }

        File.WriteAllText(Path.Combine(booksPath, IndexFileName),
            JsonConvert.SerializeObject(index, new JsonSerializerSettings()
            {
                Converters = [new Newtonsoft.Json.Converters.VersionConverter(), new StringEnumConverter()],
                Formatting = Formatting.Indented
            }));

        Log($"Created {BooksFolder}/{IndexFileName}, {names.Count} Bibles");

        return names;
    }

    public const string CoverDateFormat = "yyyy-MM-dd_HH-mm-ss";
    public static readonly Regex CoverZipName = new(@"^covers\.upto\.(?<time>\d{4}-\d\d-\d\d_\d\d-\d\d-\d\d)\.zip$");

    public static string CoverZipFile(DateTime to) => $"covers.upto.{to.ToString(CoverDateFormat, CultureInfo.InvariantCulture)}.zip";

    public static DateTime ParseCoverDate(string text) =>
        DateTime.ParseExact(text, CoverDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>
    /// The date of a file: that of the last git commit that changed it (the files of a fresh checkout all have the date of the
    /// checkout, so their last write time says nothing), or its last write time when git cannot tell, rounded down to the second.
    /// </summary>
    private static DateTime FileDate(FileInfo file)
    {
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("git")
            {
                WorkingDirectory = file.DirectoryName!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var arg in new[] { "log", "-1", "--format=%ct", "--", file.Name }) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0 && long.TryParse(output, out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        catch (Exception) { } // no git: the last write time is all there is
        var time = file.LastWriteTimeUtc;
        return new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    /// <summary>
    /// Publishes the book covers (Source/.BookCovers) as <c>book-covers/covers.upto.DATE.zip</c> files (DATE is UTC,
    /// yyyy-MM-dd_HH-mm-ss), each with the covers that changed after the DATE of the zip before it, up to and including its own
    /// (the first zip has all that changed up to its DATE), and lists them in <c>book-covers/index.json</c>. The names in the
    /// index are all that is kept between publishes: the latest DATE is the last publish, and a zip that is missing is made
    /// again from the files in its range.
    /// </summary>
    public void BookCovers()
    {
        const string imagesFolder = ".BookCovers";

        var imagesSourcePath = Path.Combine(SourcePath, imagesFolder);
        var coversPath = Path.Combine(OutputPath, "book-covers");
        Directory.CreateDirectory(coversPath);
        var indexFile = Path.Combine(coversPath, IndexFileName);

        // The dates of the earlier publishes. Names of another form (covers.since.*) are dropped: the covers are published again.
        var dates = new List<DateTime>();
        if (File.Exists(indexFile))
            foreach (var name in JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(indexFile)) ?? [])
                if (CoverZipName.Match(name) is { Success: true } match)
                    dates.Add(ParseCoverDate(match.Groups["time"].Value));
        dates = dates.Distinct().Order().ToList();
        var lastpublish = dates.Count > 0 ? dates[^1] : DateTime.MinValue;

        var images = Directory.Exists(imagesSourcePath)
            ? new DirectoryInfo(imagesSourcePath).EnumerateFiles("*.*", SearchOption.AllDirectories)
                .Select(file => (File: file, Date: FileDate(file))).ToList()
            : [];

        // The files changed since the last publish are the newest zip, which ends with the date of their latest one.
        var news = images.Where(image => image.Date > lastpublish).ToList();
        if (news.Count > 0) dates.Add(news.Max(image => image.Date));

        var names = new List<string>();
        var from = DateTime.MinValue;
        foreach (var to in dates)
        {
            var name = CoverZipFile(to);
            names.Add(name);
            var zipfile = Path.Combine(coversPath, name);
            var before = from;
            from = to;
            if (File.Exists(zipfile)) continue;
            var inRange = images.Where(image => image.Date > before && image.Date <= to).ToList();
            Zip(inRange.Select(image => (File: image.File.FullName,
                Entry: Path.GetRelativePath(imagesSourcePath, image.File.FullName).Replace(Path.DirectorySeparatorChar, '/'))), zipfile);
            LogFile(zipfile, "Published book covers");
        }

        // What the index does not list (zips of an older form too) is not published any more.
        foreach (var zip in new DirectoryInfo(coversPath).EnumerateFiles("*.zip").Where(zip => !names.Contains(zip.Name)))
            zip.Delete();
        File.WriteAllText(indexFile, JsonConvert.SerializeObject(names));
    }
    public void DirectoryListings() => WriteListings(OutputPath);

    /// <summary>Marks an index.html as one made here, so that a page that somebody wrote is never replaced.</summary>
    private const string ListingMarker = "<!-- bibmark directory listing -->";

    /// <summary>
    /// Writes an index.html that lists the folders and files into <paramref name="wwwPath"/> and into every folder of its
    /// <c>books</c> and <c>out</c> trees: a web server such as GitHub Pages does not list a folder, so this is how one
    /// browses to the files. An index.html that was not made here is left as it is.
    /// </summary>
    private void WriteListings(string wwwPath)
    {
        int count = 0;
        void Walk(string folder, bool recurse)
        {
            if (WriteListing(wwwPath, folder)) count++;
            if (!recurse) return;
            foreach (var sub in Directory.EnumerateDirectories(folder))
                Walk(sub, true);
        }

        Walk(wwwPath, false);
        foreach (var name in new[] { BooksFolder, OutFolder })
            if (Directory.Exists(Path.Combine(wwwPath, name))) Walk(Path.Combine(wwwPath, name), true);
        Log($"Created {count} directory listings ({ListingFileName})");
    }

    private const string ListingFileName = "index.html";

    private bool WriteListing(string wwwPath, string folder)
    {
        var file = Path.Combine(folder, ListingFileName);
        if (File.Exists(file) && !File.ReadAllText(file, Encoding.UTF8).Contains(ListingMarker))
        {
            Log($"Kept {Path.GetRelativePath(wwwPath, file)}: it was not made by bibmark");
            return false;
        }

        var relative = Path.GetRelativePath(wwwPath, folder).Replace('\\', '/');
        var title = "/" + (relative == "." ? "" : relative + "/");
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine(ListingMarker);
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine($"<title>Index of {WebUtility.HtmlEncode(title)}</title>");
        html.AppendLine("<style>");
        html.AppendLine(":root { color-scheme: light dark; }");
        html.AppendLine("body { font-family: system-ui, sans-serif; margin: 2rem auto; max-width: 52rem; padding: 0 1rem; }");
        html.AppendLine("table { border-collapse: collapse; width: 100%; }");
        html.AppendLine("td { padding: .25rem .5rem; border-bottom: 1px solid color-mix(in srgb, currentColor 15%, transparent); }");
        html.AppendLine("td.size { text-align: right; white-space: nowrap; opacity: .7; }");
        html.AppendLine("a { text-decoration: none; } a:hover { text-decoration: underline; }");
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine($"<h1>Index of {WebUtility.HtmlEncode(title)}</h1>");
        html.AppendLine("<table>");
        if (relative != ".") html.AppendLine("<tr><td><a href=\"../\">../</a></td><td class=\"size\"></td></tr>");

        var comparer = StringComparer.OrdinalIgnoreCase;
        foreach (var sub in Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OrderBy(n => n, comparer))
            html.AppendLine($"<tr><td><a href=\"{Href(sub!)}/\">{WebUtility.HtmlEncode(sub)}/</a></td><td class=\"size\"></td></tr>");
        foreach (var name in Directory.EnumerateFiles(folder).Select(Path.GetFileName).OrderBy(n => n, comparer))
        {
            if (string.Equals(name, ListingFileName, StringComparison.OrdinalIgnoreCase) || name!.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
            html.AppendLine($"<tr><td><a href=\"{Href(name!)}\">{WebUtility.HtmlEncode(name)}</a></td><td class=\"size\">{Size(new FileInfo(Path.Combine(folder, name)).Length)}</td></tr>");
        }
        html.AppendLine("</table>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        File.WriteAllText(file, html.ToString(), new UTF8Encoding(false));
        return true;
    }

    private static string Href(string name) => Uri.EscapeDataString(name);

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.#} GB",
    };

    /// <summary>Zips the files (without folders) into <paramref name="zip"/>.</summary>
    private static void ZipFiles(IEnumerable<string> files, string zip, Func<string, string?>? filter = null, Action<float> progress = null) =>
        Zip(files.Select(file => (File: file, Entry: filter?.Invoke(Path.GetFileName(file))))
            .Where(file => file.Entry != null)
            .OfType<(string File, string Entry)>(), zip, progress);

    /// <summary>Zips a folder with its subfolders into <paramref name="zip"/>.</summary>
    private static void ZipFolder(string folder, string zip, SearchOption searchOption = SearchOption.AllDirectories, Func<string, string?>? filter = null, Action<float> progress = null) =>
        Zip(Directory.EnumerateFiles(folder, "*", searchOption)
            .Select(file => (File: file, Entry: filter?.Invoke(Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'))))
            .Where(file => file.Entry != null)
            .OfType<(string File, string Entry)>(), zip, progress);

    // Every entry has the same date and the entries are sorted: the same files give the same zip, byte for byte, so
    // that publishing again does not change a zip (and a git repository that holds the site does not grow with every run).
    private static readonly DateTimeOffset ZipDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Zips the files under the names given; a zip that fails half way does not replace a good one.</summary>
    private static void Zip(IEnumerable<(string File, string Entry)> files, string zip, Action<float> progress = null)
    {
        var temporary = zip + ".tmp";
        if (File.Exists(temporary)) File.Delete(temporary);
        var fileslist = files.ToList();
        var n = fileslist.Count;
        int i = 0;
        using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (var (file, name) in fileslist.OrderBy(f => f.Entry, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = ZipDate;
                using var input = File.OpenRead(file);
                using var output = entry.Open();
                input.CopyTo(output);
                progress?.Invoke(((float)i++) / ((float)n));
            }
        }
        File.Move(temporary, zip, overwrite: true);
    }

    /// <summary>
    /// Copies the files of the out folder of a Bible (see <see cref="OutExtensions"/>) to <paramref name="target"/>, and
    /// zips its html, tex and usfm folders (<see cref="ZippedOutFolders"/>).
    /// </summary>
    private void PublishOut(string source, string target, string bible)
    {
        if (!Directory.Exists(source)) return;
        if (Directory.Exists(target)) Directory.Delete(target, true); // what an earlier publish left must not stay when it is gone
        int count = CopyOut(source, target, bible, top: true);
        if (count > 0) Log($"Created {OutFolder}/{bible}, {count} files");
    }

    private int CopyOut(string source, string target, string bible, bool top)
    {
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(source).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!OutExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(target);
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            count++;
        }
        foreach (var folder in Directory.EnumerateDirectories(source).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(folder);
            var subtarget = Path.Combine(target, name);
            if (top && SkippedOutFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (top && ZippedOutFolders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                // many small files: a zip of the folder is published instead of them
                if (!Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any()) continue;
                Directory.CreateDirectory(subtarget);
                var zip = Path.Combine(subtarget, $"{bible}.{name.ToLowerInvariant()}.zip");
                ZipFolder(folder, zip, SearchOption.TopDirectoryOnly);
                count++;
            }
            else count += CopyOut(folder, subtarget, bible, top: false);
        }
        return count;
    }

    private static bool IsBook(string file) => Regex.IsMatch(Path.GetFileName(file), "^[0-9.]+-.*?.md$|.db$|.info$|.xml$");

    /// <summary>The content of the info.json among the files, or null when there is none or it is not valid JSON.</summary>
    private InstallableBook? ReadInfo(IEnumerable<string> files)
    {
        var info = files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), Importer.InfoFileName, StringComparison.OrdinalIgnoreCase));
        if (info == null) return null;
        try { return JsonConvert.DeserializeObject<InstallableBook>(File.ReadAllText(info, Encoding.UTF8)); }
        catch (JsonException ex)
        {
            Log($"{info} is not valid JSON and is left out of the index: {ex.Message}");
            return null;
        }
    }
}
