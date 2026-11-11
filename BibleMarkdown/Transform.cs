using EstrellasDeEsperanza.AsyncLock;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BibleMarkdown
{
    public class Transform
    {
        static AsyncLocal<Transform> current = new AsyncLocal<Transform>();
        public static Transform Current {
            get => current.Value;
            private set => current.Value = value;
        }
        Importer? importer;
        public Importer Import => importer ??= new Importer(this);
        Creator? creator;
        public Creator Create => creator ??= new Creator(this);
        Publisher? publish;
        public Publisher Publish => publish ??= new Publisher(this);
        Epub? epub;
        public Epub Epub => epub ??= new Epub(this);

        BookList? books;
        public BookList Books {
            get => books ??= new BookList(this);
            set => books = value;
        }
        public string? Language { get; set; } = "default";
        public bool ParagraphVerses { get; set; }
        ISimpleLogger logger;
        public ISimpleLogger Logger {
            get => logger ??= new ConsoleAndFileLogger(this);
            set => logger = value;
        }
        public StringBuilder Output = new StringBuilder();
        public void Clear() => Interlocked.Exchange(ref Output, new StringBuilder());
        Pandoc? pandoc;
        public Pandoc Pandoc => pandoc ??= new Pandoc(this);
        public string ProjectPath { get; set; }
        string? sourcePath, outputPath;
        public string SourcePath {
            get => sourcePath ?? Path.Combine(ProjectPath, "src");
            set => sourcePath = value;
        }
        public string OutputPath {
            get => outputPath ?? Path.Combine(ProjectPath, "out");
            set => outputPath = value;
        }
        public Transform(string projectPath, string? sourcePath = null, string? outputPath = null, ISimpleLogger logger = null)
        {
            if (ProjectPath == "") ProjectPath = Environment.CurrentDirectory;
            ProjectPath = Path.GetFullPath(projectPath);
            this.sourcePath = Path.IsPathRooted(sourcePath) ? sourcePath : Path.GetFullPath(Path.Combine(ProjectPath, sourcePath));
            this.outputPath = Path.IsPathRooted(outputPath) ? outputPath : Path.GetFullPath(Path.Combine(ProjectPath, outputPath));
        }
        public Transform(Transform transform, string projectPath)
        {
            if (ProjectPath == "") ProjectPath = Environment.CurrentDirectory;
            ProjectPath = Path.GetFullPath(projectPath);

            sourcePath = transform.sourcePath;
            outputPath = transform.outputPath;
            EachVerseOnNewLine = transform.EachVerseOnNewLine;
            FromSource = transform.FromSource;
            ImportStrongs = transform.ImportStrongs;
            Language = transform.Language;
            LeftLanguage = null;
            RightLanguage = null;
            LowercaseFirstWords = transform.LowercaseFirstWords;
            ParagraphVerses = transform.ParagraphVerses;
            Replace = transform.Replace;
            SwordVersification = transform.SwordVersification;
            TwoLanguage = false;
        }


        public void Log(string message)
        {
            lock (Output) Output.Append(message + Environment.NewLine);
            Logger.Log(message);
        }
        public void LogFile(string file) => Logger.LogFile(file);
        public void LogFile(string file, string label) => Logger.LogFile(file, label);

        public string FootnoteLabel(int i)
        {
            if (i == 0) return "a";
            StringBuilder label = new StringBuilder();
            while (i > 0)
            {
                var ch = (char)(((int)'a') + i % 26);
                label.Append(ch);
                i = i / 26;
            }
            return label.ToString();
        }
        /// <summary>
        /// An abbreviation of a book name: its number ("1. Mose" has "1") and the first letters of its name, 3 in all
        /// ("1Mo", "Mat"; "Matthäus" is "Mat"). More letters make longer abbreviations for names that start alike.
        /// </summary>
        public string Abbreviate(string name, int length)
        {
            var number = Regex.Match(name, @"^\s*([0-9]+)\.?\s*");
            var prefix = number.Success ? number.Groups[1].Value : "";
            var letters = string.Concat(name[number.Length..].Where(char.IsLetter));
            if (letters.Length == 0) return prefix.Length > 0 ? prefix : name;
            int count = Math.Max(1, length - prefix.Length);
            var abbreviation = prefix + char.ToUpperInvariant(letters[0]) + letters[1..Math.Min(letters.Length, count)].ToLowerInvariant();
            return abbreviation;
        }
        static DateTime? bibmarktime = null;
        public DateTime BibmarkTime
        {
            get
            {
                if (bibmarktime == null)
                {
                    var exe = Assembly.GetExecutingAssembly().Location;
                    bibmarktime = File.GetLastWriteTimeUtc(exe);
                }
                return bibmarktime.Value;
            }
        }
        public bool LowercaseFirstWords
        {
            get => Import.LowercaseFirstWords;
            set => Import.LowercaseFirstWords = value;
        }
        public bool ImportStrongs
        {
            get => Import.ImportStrongs;
            set => Import.ImportStrongs = value;
        }
        public bool EachVerseOnNewLine
        {
            get => Import.EachVerseOnNewLine;
            set => Import.EachVerseOnNewLine = value;
        }
        public bool FromSource
        {
            get => !Import.Disabled;
            set => Import.Disabled = !value;
        }
        public bool Imported => Import.Imported;
        public string SwordVersification
        {
            get => Create.SwordVersification;
            set => Create.SwordVersification = value;
        }
        public string? RightLanguage
        {
            get => Create.RightLanguage;
            set => Create.RightLanguage = value;
        }
        public string? LeftLanguage
        {
            get => Create.LeftLanguage;
            set => Create.LeftLanguage = value;
        }

        public string? Replace
        {
            get => Create.Replace;
            set => Create.Replace = value;
        }
        public bool TwoLanguage
        {
            get => Create.TwoLanguage;
            set => Create.TwoLanguage = value;
        }
        public bool IsNewer(string file, string srcfile)
        {
            var srctime = DateTime.MaxValue;
            if (File.Exists(srcfile)) srctime = File.GetLastWriteTimeUtc(srcfile);
            var filetime = DateTime.MinValue;
            if (File.Exists(file)) filetime = File.GetLastWriteTimeUtc(file);
            return filetime > srctime && filetime > BibmarkTime;
        }

        public bool IsNewer(IEnumerable<string> sources, IEnumerable<string> destinations)
        {
            var tsrc = sources
                .Select(File.GetLastWriteTimeUtc)
                .Max();
            var tdest = destinations
                .Select(File.GetLastWriteTime)
                .Max();
            return tsrc > tdest && tdest > BibmarkTime;
        }

        public async Task TransformAsync(string path = null)
        {
            if (path != null)
            {
                await new Transform(this, path).TransformAsync();
            }
            else
            {
                Clear();
                await RunScriptAsync();
                Directory.CreateDirectory(OutputPath);
                Import.All();
                await Create.ToAllAsync();
            }
        }
        static AsyncLock executeLock = new AsyncLock();
        public async Task RunScriptAsync()
        {
            var file = Path.Combine(SourcePath, "script.cs");
            if (!File.Exists(file)) file = Path.Combine(ProjectPath, "script.cs");
            if (!File.Exists(file)) return;

            //var txt = $"using static Bibmark = BibleMarkdown.Bibmark;{Environment.NewLine}{Environment.NewLine}{await File.ReadAllTextAsync(file)}";
            var txt = await File.ReadAllTextAsync(file);
            LogFile(file, "Run script");

            try
            {
                using (await executeLock.LockAsync())
                {
                    Current = this;
                    var result = await CSharpScript.RunAsync(txt, ScriptOptions.Default
                        .WithReferences(typeof(Program).Assembly)
                        .WithImports("static Bibmark = BibleMarkdown.Bibmark"));
                }
            }
            catch (Exception e)
            {
                Log(e.ToString());
            }

        }

        public async Task TransformTwoLanguagesAsync(string leftpath, string rightpath)
        {
            Clear();
            TwoLanguage = true;
            leftpath = Path.IsPathRooted(leftpath) ? leftpath : Path.GetFullPath(Path.Combine(ProjectPath, leftpath));
            rightpath = Path.IsPathRooted(rightpath) ? rightpath : Path.GetFullPath(Path.Combine(ProjectPath, rightpath));

            if (!Directory.Exists(OutputPath)) Directory.CreateDirectory(OutputPath);

            await Task.WhenAll(
                TransformAsync(leftpath),
                TransformAsync(rightpath));
            await RunScriptAsync();
            Create.ToTwoLanguage(leftpath, rightpath);
            var files = Directory.EnumerateFiles(ProjectPath, "*.md");
            await Task.WhenAll(files.AsParallel().Select(file => Create.TransformFileAsync(file)));
            File.WriteAllText(Path.Combine(OutputPath, "bibmark.log"), Output.ToString());
        }
    }

    public class WithTransform
    {
        public Transform Transform { get; private set; }
        public WithTransform(Transform transform) { Transform = transform; }
        public Importer Import => Transform.Import;
        public Creator Create => Transform.Create;
        public BookList Books
        {
            get => Transform.Books;
            set => Transform.Books = value;
        }
        public string? Language
        {
            get => Transform.Language;
            set => Transform.Language = value;
        }
        public bool ParagraphVerses => Transform.ParagraphVerses;
        public ISimpleLogger Logger => Transform.Logger;
        public Pandoc Pandoc => Transform.Pandoc;
        public string ProjectPath => Transform.ProjectPath;
        public string SourcePath => Transform.SourcePath;
        public string OutputPath => Transform.OutputPath;
        public void Log(string message) => Logger.Log(message);
        public void LogFile(string file) => Logger.LogFile(file);
        public void LogFile(string file, string label) => Logger.LogFile(file, label);
        public string FootnoteLabel(int i) => Transform.FootnoteLabel(i);
        public string Abbreviate(string name, int length) => Transform.Abbreviate(name, length);
    }
}
