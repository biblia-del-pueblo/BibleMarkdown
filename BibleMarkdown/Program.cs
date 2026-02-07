using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics.Tracing;
using System.Security.Cryptography;
using System.Data.SqlTypes;
using System.Xml;
using System.Xml.Linq;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace BibleMarkdown;

partial class Program
{

    public static DateTime bibmarktime;
    public static bool LowercaseFirstWords = false;
    public static bool ImportStrongs = false;
    public static bool EachVerseOnNewLine = true;
    public static bool FromSource = false;
    public static bool Imported = false;
    public static bool Help = false;
    public static bool ParagraphVerses = true;
    public static Func<string, string> Preprocess = s => s;
    public static Func<string, string> PreprocessImportUSFM = s => s;
    static string language;
    public static string Language
    {
        get { return language; }
        set
        {
            if (value != language)
            {
                language = value;
                Log($"Language set to {language}");
            }
        }
    }
    public static string RightLanguage
    {
        get { return rightlanguage; }
        set
        {
            if (value != rightlanguage)
            {
                rightlanguage = value;
                Log($"RightLanguage set to {rightlanguage}");
            }
        }
    }
    public static string LeftLanguage
    {
        get { return leftlanguage; }
        set
        {
            if (value != leftlanguage)
            {
                leftlanguage = value;
                Log($"LeftLanguage set to {leftlanguage}");
            }
        }
    }

    static string leftlanguage;
    static string rightlanguage;
    public static string? Replace = null;
    public static bool TwoLanguage = false;

    public struct Footnote
    {
        public int Index;
        public int FIndex;
        public string Value;

        public Footnote(int Index, int FIndex, string Value)
        {
            this.Index = Index;
            this.FIndex = FIndex;
            this.Value = Value;
        }
    }

    public static void LogFile(string file)
    {
        LogFile(file, "Created");
    }
    public static void LogFile(string file, string label)
    {
        var current = Directory.GetCurrentDirectory();
        if (file.StartsWith(current))
        {
            file = file.Substring(current.Length);
        }
        Log($"{label} {file}");
    }

    static StringBuilder log = new StringBuilder();
    public static void Log(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            lock (log)
            {
                log.AppendLine(text);
                Console.WriteLine(text);
            }
        }
    }
    public static bool IsNewer(string file, string srcfile)
    {
        var srctime = DateTime.MaxValue;
        if (File.Exists(srcfile)) srctime = File.GetLastWriteTimeUtc(srcfile);
        var filetime = DateTime.MinValue;
        if (File.Exists(file)) filetime = File.GetLastWriteTimeUtc(file);
        return filetime > srctime && filetime > bibmarktime;
    }

    static string Label(int i)
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

    public static Outline? OutlineForCreate { get; set; }

    public static void ReadOutlineForCreate(string path)
    {
        OutlineForCreate = ReadOutlines(path);
    }

    static Task ProcessFileAsync(string file)
    {
        var path = Path.GetDirectoryName(file);
        var md = Path.Combine(path, "out", "pandoc");
        var mdtex = Path.Combine(md, "tex");
        var mdepub = Path.Combine(md, "epub");
        var mdusfm = Path.Combine(md, "usfm");
        var tex = Path.Combine(path, "out", "tex");
        var html = Path.Combine(path, "out", "html");
        var usfm = Path.Combine(path, "out", "usfm");
        if (!Directory.Exists(md)) Directory.CreateDirectory(md);
        if (!Directory.Exists(mdtex)) Directory.CreateDirectory(mdtex);
        if (!Directory.Exists(mdepub)) Directory.CreateDirectory(mdepub);
        if (!Directory.Exists(mdusfm)) Directory.CreateDirectory(mdusfm);
        if (!Directory.Exists(tex)) Directory.CreateDirectory(tex);
        if (!Directory.Exists(html)) Directory.CreateDirectory(html);
        if (!Directory.Exists(usfm)) Directory.CreateDirectory(usfm);
        var mdfile = Path.Combine(md, Path.GetFileName(file));
        var texfile = Path.Combine(tex, Path.GetFileNameWithoutExtension(file) + ".tex");
        var htmlfile = Path.Combine(html, Path.GetFileNameWithoutExtension(file) + ".html");
        var epubfile = Path.Combine(mdepub, Path.GetFileNameWithoutExtension(file) + ".md");
        var usfmfile = Path.Combine(usfm, Path.GetFileNameWithoutExtension(file) + ".usfm");

        Task TeXTask = Task.CompletedTask, HtmlTask = Task.CompletedTask;

        CreatePandoc(file, mdfile);
        CreateEpub(path, mdfile, epubfile);
        CreateUSFM(mdfile, usfmfile);
        return Task.WhenAll(CreateTeXAsync(mdfile, texfile), CreateHtmlAsync(mdfile, htmlfile));
    }


    static void ProcessPath(string path)
    {
        RunScript(path);
        var srcpath = Path.Combine(path, "src");
        var outpath = Path.Combine(path, "out");
        if (!Directory.Exists(outpath)) Directory.CreateDirectory(outpath);
        if (Directory.Exists(srcpath))
        {
            BookList.Books.Load(path);
            ImportFromZip(path, srcpath);
            ImportFromBibleEdit(srcpath);
            ImportFromUSFM(path, srcpath);
            ImportFromTXT(path, srcpath);
            ImportFromZefania(path, srcpath);
            ImportFromXmlOther(path, srcpath);
            ImportJavascripture(path, srcpath);
            ImportFromBibleMarkdown(path, srcpath);
            ImportParallelVerses(path);
            ImportOutline(path);
        }
        ReadOutlineForCreate(path);
        var files = Directory.EnumerateFiles(path, "*.md")
            .Where(file => Regex.IsMatch(Path.GetFileName(file), @"^[0-9.]+-.*?(?!outline.md$|map.md$)"));
        if (files.Any())
        {
            CreateOutline(path);
            CreateVerseStats(path);
            Log("Convert to Pandoc...");
            Task.WaitAll(files.AsParallel().Select(file => ProcessFileAsync(file)).ToArray());
        }
        File.WriteAllText(Path.Combine(outpath, "bibmark.log"), log.ToString());
        log.Clear();
    }

    static void RunScript(string path)
    {
        var file = Path.Combine(path, "src", "script.cs");
        if (!File.Exists(file)) return;

        var txt = File.ReadAllText(file);
        LogFile(file, "Run script");

        try
        {
            var result = CSharpScript.RunAsync(txt, ScriptOptions.Default
            .WithReferences(typeof(Program).Assembly)
            .WithImports("BibleMarkdown"));
            result.Wait();
        }
        catch (Exception e)
        {
            Log(e.ToString());
        }

    }

    static void ProcessTwoLanguagesPath(string path, string path1, string path2)
    {
        TwoLanguage = true;
        var outpath = Path.Combine(path, "out");
        if (!Directory.Exists(outpath)) Directory.CreateDirectory(outpath);

        // ProcessPath(path1);
        // ProcessPath(path2);
        RunScript(path);
        Books.Load(path);
        CreateTwoLanguage(path, path1, path2);
        var files = Directory.EnumerateFiles(path, "*.md");
        Task.WaitAll(files.AsParallel().Select(file => ProcessFileAsync(file)).ToArray());
        File.WriteAllText(Path.Combine(outpath, "bibmark.log"), log.ToString());
        log.Clear();
    }
    static void InitPandoc()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (arch == Architecture.X86) arch = Architecture.X64;
        var archstr = arch.ToString().ToLower();
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Pandoc.SetPandocPath(@"runtimes\win\pandoc.exe");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) Pandoc.SetPandocPath($"linux-{archstr}/pandoc");
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) Pandoc.SetPandocPath($"osx-{archstr}/pandoc");
        else throw new PlatformNotSupportedException();
	}

    static void ShowHelp()
    {
        Log(@"Bibmark usage:

Bibmark converts Markdown files to various formats.
The Bibles use a specific version of Markdown, BibleMarkdown. BibleMarkdown is
normal pandoc Markdown, with the following extensions:

- For Footnotes you can make them more readable, by placing a marker ^label^
   at the place of the footnote, but specifying the footnote later in the text with
   ordinary ^label^[Footnote] markdown. ""label"" must be a letter or word
   without any digits.
- You can have comments by bracing them with /* and */ or by leading with
   // until the end of the line. /* */ Comments can span multiple lines.
- Verse numbers are denoted with a @ character followed by a number, like
   this: @1 In the beginning was the Word and the Word was with God and the Word was God. @2 This was in the beginning...
- if the text contains the comment //!verse-paragraphs, each verse is rendered
   in a paragraph. For use in Psalms and Proverbs.
- Chapter numbers are denoted with a # markdown title and Chapter headings
   with a ## markdown title
- A special comment //!replace /regularexpression/replacement/regularexpression/replacement/...
   can be placed in the text. All the regular expressions will be replaced. You can
   choose another delimiter char than /, the first character encountered will be used as delimiter.

To edit the Markdown of the Bibles, you can use a normal editor like Typora,
stackedit.io or VisualStudio Code.

Bibmark processes all the .md files in the current directory and converts them to
other formats in the ""out"" subdirectory. The md files in the current directory must
follow a naming schema, of two digits followed by a minus and the name of the
bible book, e.g. like 01-Genesis.md or 02-Exodus.md. Bibmark only processes files
with names adhering to that schema. The md files can be constructed from various
source formats. For this, the source files must be placed in the subdirectory ""src"".
In the ""src"" subdirectory you can place USFM files or zefania xml files, or a BibleEdit
folder. You can also place a script.cs file in the ""src"" folder that will be executed
when running bibmark, that can configure bibmark for certain tasks. Next you can
place a file booknames.xml in the ""src"" subdirectory that contains names of Bible
books in different languages. The names of the books should correspond to the titles
of the books in the USFM files. Then you can also import a Parallel Verses file,
linklist.xml, that contains parallel verses.

If you have the source text of your Bible in USFM markup, you can place
those files in a subfolder src. If you specify the -s argument to bibmark,
bibmark searches this folder for USFM source and creates Bible Markdown
files in the main folder if the source files are newer than the Bible Markdown
files. Instead of USFM you can also place a Zefania XML file or a BibleEdit
or a Javascripture file/folder in the src folder. You can also place a folder 'bibmark'
containing .md files that will be copied to the main folder. That way
you can placce a git submodule in the src/bibmark folder containing BibleMarkdown.
From the Bible Markdown files, bibmark creates Pandoc files in the
out/pandoc folder, LaTeX files in the out/tex folder, HTML files in the
out/html folder and USFM files in the out/usfm folder.

bibmark also creates a file called outline.md & outline.xml in the out folder that
specifies chapter titles, paragraphs and footnotes. If you move this file to the
src folder and it is newer than the Bible Markdown files, bibmark applies the
chapter titles and paragraphs and footnotes found in the outline.md file to the
Bible Markdown files.
In the outline.md file, the Bible Markdown files are specified by a # markdown
title, the chapter numbers by a ## markdown title, and chapter titles by a ###
markdown title.
Verses that end with a paragraph or a footnote are denoted with @n with n
denoting the verse number,  followed by a \ for a paragraph or a ^^ for a
footnote marker, or a ^[Footnote] footnote.
You can also specify mutliple *.outline.md files, that will be merged.
If you put a //!append directive in one of those files, The titles, paragraphs
and footnotes will be added to the .md files. If you ommit //!append, the
titles, paragraphs and footnotes in the outline.md files will replace the titles,
paragraphs and footnotes in the .md files.
You can also put the .outline.md files in the folder with your .md files, in
that case the outlines will be applied to the .md files on output generation.
You can also change the versification of the .outline.md files by putting a
//!map <versification> directive in the .outline.md file or a Map attribute on
the BibleFramework root node in outline.xml.
You then put a verse mapping in the file versification.map.md.
The syntax of the verse mapping md file is as follows:
```
# <book>
<chapter>:<verse>=><tochapter>:<toverse> <chapter2>:<verse2>=><tochapter2>:<toverse2> ...

# <book2>
<mappings as above> ...
...
```
For example the following
```
# Números
12:16=>13:1 13:1=>13:2 13:33=>13:33
```
will point 12:16 to 13:1 and then all verses one up, until verse 13:33.
You can create the mapping files by comparing the verseinfo.md (see below) 
files of the different bibles via the diff tool.

bibmark also creates a file verseinfo.md in the out folder, a file that shows how
many verses each chapter has, so you can compare different Bibles versifications.

You can also place a file linklist.xml in the src folder, to specify parallel verses
included in footnotes, that will be imported and placed in the footnotes.
This file will be exported as ParallelVerses.outline.md &
ParallelVerses.outline.xml in the main directory.

For examples of the various input files like booknames.xml, linklist.xml, outline.md etc.
you can have a look at the Bibles in the BibliaLibre project at
https://github.com/biblia-del-pueblo/BibliaLibre.

Options:
  - -s, -src or -source:
    If you want to import text from the src folder you need to specify this option when
    calling bibmark.
  - -cp:
    (Continuous Paragraph) 
    If you do not want to start each verse on a newline, but the whole paragraph on a
    single line, you can specify this option. Placing each verse on a newline is more git
    friendly, where as placing it on a single line is more readable.
  - -ln language:
    With this option you can specify a language. The language is used to determine
    Book names from booknames.xml.
  - -r or -replace replacementtext
    The first letter of repacementtext is used as token delimiter. replacementtext is
    then split into tokens. Every pair of tokens describes a global replacement directive,
    where the first token is a Regex expression, and the second token is a
    Regex replacement string.
  - -twolanguage path1 path2
    Produces a double column, two language bible, with the single language bibles
    located in path1 and path2.
  - -strongs
    Import Strong number syntax from USFM files.
  ");
    }

    static void Main(string[] args)
    {
        // Install bibmark on Linux & macOS systems
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (Directory.Exists("/usr/bin") && !File.Exists("/usr/bin/bibmark"))
            {
                File.WriteAllText("/usr/bin/bibmark", "#!/bin/bash\ndotnet" + Assembly.GetExecutingAssembly().Location + " \"$@\"\n");
                Process.Start("chmod", "+x /usr/bin/bibmark")?.WaitForExit();
                Console.WriteLine("Installed bibmark to /usr/bin/bibmark");
                return;
            }
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (Directory.Exists("/usr/local/bin") && !File.Exists("/usr/local/bin/bibmark"))
            {
                File.WriteAllText("/usr/local/bin/bibmark", "#!/bin/bash\ndotnet" + Assembly.GetExecutingAssembly().Location + " \"$@\"\n");
                Process.Start("chmod", "+x /usr/local/bin/bibmark")?.WaitForExit();
                Console.WriteLine("Installed bibmark to /usr/local/bin/bibmark");
                return;
            }
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var userenvpath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
			var envpath = new string[] {
                userenvpath,
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "",
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "" };
			var envpaths = envpath.SelectMany(path => path.Split(';'));
            if (!envpaths.Any(p => File.Exists(Path.Combine(p, "bibmark.exe"))))
            {
				var location = Assembly.GetExecutingAssembly().Location;
                userenvpath = string.IsNullOrEmpty(userenvpath) ? location : userenvpath + ";" + location;
				Environment.SetEnvironmentVariable("PATH", userenvpath, EnvironmentVariableTarget.User);
                Console.WriteLine("Added bibmark to PATH environment variable.");
                return;
            }
        }

        // Get the version of the current application.
        var asm = Assembly.GetExecutingAssembly();
        var aname = asm.GetName();
        Log($"{aname.Name}, v{aname.Version.Major}.{aname.Version.Minor}.{aname.Version.Build}.{aname.Version.Revision}");

        if (args.Contains("-h") || args.Contains("-help") || args.Contains("-?"))
        {
            ShowHelp();
            return;
        }

        Init();

        InitPandoc();
        //var process = Process.GetCurrentProcess();
        //var exe = process.MainModule.FileName;
        var exe = Assembly.GetExecutingAssembly().Location;
        bibmarktime = File.GetLastWriteTimeUtc(exe);

        LowercaseFirstWords = args.Contains("-plc");
        EachVerseOnNewLine = !args.Contains("-cp");
        FromSource = args.Contains("-s") || args.Contains("-src") || args.Contains("-source");
        ImportStrongs = args.Contains("-strongs");
        var lnpos = Array.IndexOf(args, "-ln");
        if (lnpos >= 0 && (lnpos + 1 < args.Length)) Language = args[lnpos + 1];
        else Language = "default";

        var replacepos = Array.IndexOf(args, "-replace");
        if (replacepos == -1) replacepos = Array.IndexOf(args, "-r");
        if (replacepos >= 0 && replacepos + 1 < args.Length) Replace = args[replacepos + 1];

        var twolangpos = Array.IndexOf(args, "-twolanguage");
        if (twolangpos >= 0 && twolangpos + 2 < args.Length)
        {
            var left = args[twolangpos + 1];
            var right = args[twolangpos + 2];
            var p = Directory.GetCurrentDirectory();
            ProcessTwoLanguagesPath(p, left, right);
            return;
        }
        var paths = args.ToList();
        for (int i = 0; i < paths.Count; i++)
        {
            if (paths[i] == "-twolanguage")
            {
                paths.RemoveAt(i); paths.RemoveAt(i); paths.RemoveAt(i); i--;
            }
            else if (paths[i] == "-ln" || paths[i] == "-replace" || paths[i] == "-r")
            {
                paths.RemoveAt(i); paths.RemoveAt(i); i--;
            }
            else if (paths[i].StartsWith('-'))
            {
                paths.RemoveAt(i); i--;
            }
        }
        string path;
        if (paths.Count == 0)
        {
            path = Directory.GetCurrentDirectory();
            ProcessPath(path);
        }
        else
        {
            path = paths[0];
            if (Directory.Exists(path))
            {
                ProcessPath(path);
            }
            else if (File.Exists(path)) ProcessFileAsync(path).Wait();
        }

    }
}
