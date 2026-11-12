using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BibleMarkdown;

internal class Program
{
    const bool InstallGlobal = false;
    static bool Help = false;

    static void Log(string message) => Console.WriteLine(message);
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
follow a naming schema, of a real number followed by a minus and the name of the
bible book, e.g. like 01-Genesis.md or 02-Exodus.md. Bibmark only processes files
with names adhering to that schema. The md files can be constructed from various
source formats.
From the Bible Markdown files, bibmark creates Pandoc files in the
out/pandoc folder, LaTeX files in the out/tex folder, HTML files in the
out/html folder and USFM files in the out/usfm folder.

If you have the source text of your Bible in USFM markup, you can place
those files in a subfolder ""src"". If you specify the -s argument to bibmark,
bibmark searches this folder for USFM source and creates Bible Markdown
files in the main folder if the source files are newer than the Bible Markdown
files. Instead of USFM you can also place a Zefania XML file or a BibleEdit
or a Javascripture file/folder in the src folder, or a Bible module of The SWORD Project
(its folders mods.d and modules, or its zip file), which is read directly, without any
SWORD tools. Supported are the zText and RawText modules, with any versification as
long as it is KJV or the module marks its books and chapters (as modules made with osis2mod do).
You can also place a folder ""bibmark""
containing .md files that will be copied to the main folder. That way
you can placce a git submodule in the src/bibmark folder containing BibleMarkdown.
You can also place a script.cs file in the ""src"" folder that will be executed
when running bibmark, that can configure bibmark for certain tasks. Next you can
place a file booknames.xml in the ""src"" subdirectory that contains names of Bible
books in different languages. The names of the books should correspond to the titles
of the books in the USFM files.

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

# <book>
<chapter>:<verse>=><tochapter>:<toverse> <chapter2>:<verse2>=><tochapter2>:<toverse2> ...

# <book2>
<mappings as above> ...
...

For example the following

# Números
12:16=>13:1 13:1=>13:2 13:33=>13:33

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

syntax:

  - bimark [path to bible project folder] {path to import source folder] [output path] [options]
    Invokes bibmark to process all files in the current folder.
  - bibmark import [path to import source folder] [options]
    Imports bibles into bibmark format from the source folder. 
  - bibmark publish [bibles source folder] [www publish folder]
    Zips every Bible in bibles source folder  into www publish folder/books, together with a
    books/index.json that lists the zips and the info.json of each Bible, and copies the
    pdf, md, epub, html, xml and zip files of the out folders to www folder/out/Bible
    (the html, tex and usfm folders as one zip each, the pandoc folder not at all).
    Nothing is converted.
    Use this option to create an online repository of Bibles for CorssBible reader.
  - bibmark twolanguage <path to left column bible> <path to right column bible>
    Produces a double column, two language bible, with the single language bibles
    located in left and right path's.

Options:
  - -p, -project or --project <project path>
    The path of the bible project to process. Defaults to the current direcroty if ommited.
  - -s, -src, -source or --source [source path]
    If you want to import text from the src folder you need to specify this option when
    calling bibmark. You can optionally specify a source path, it defaults to src when 
    ommited. If a relative path is specified, it is relative to the project path or current
    directory.
 - -o, -out or --out <output path>
    With this option you can specify the output path. For a relative path, it is relative
    to the project path or current directory.
  - -cp or --continuous-paragraphs
    (Continuous Paragraphs) 
    If you do not want to start each verse on a newline, but the whole paragraph on a
    single line, you can specify this option. Placing each verse on a newline is more git
    friendly, where as placing it on a single line is more readable.
  - -ln or --language <language>
    With this option you can specify a language. The language is used to determine
    Book names from booknames.xml.
  - -r, -replace or --replace <replacementtext>
    The first letter of repacementtext is used as token delimiter. replacementtext is
    then split into tokens. Every pair of tokens describes a global replacement directive,
    where the first token is a Regex expression, and the second token is a
    Regex replacement string.
  - -strongs or --strongs
    Import Strong number syntax from USFM files and SWORD modules.
  - -v, -version or --version
    Displays the bibmark & pandoc version.
  ");
    }

    static async Task Main(string[] args)
    {
        // Publishing needs neither the installation of bibmark below nor pandoc, so it is the first thing that is looked at
        // (it is what a build server runs, where bibmark cannot be installed in /usr/bin).
        // -publish biblesPath wwwPath: zips the Bibles and lists them in an index.json, then ends (no conversion).
        Transform? Transform;
        string? projectPath = null, sourcePath = null, outputPath = null;
        if (args.FirstOrDefault() == "publish")
        {
            if (args.Length > 3)
            {
                Log("Usage: bibmark publish biblesPath wwwPath");
                return;
            }
            sourcePath = args[1];
            var destPath = args[2];
            Transform = new Transform("", sourcePath, destPath);
            Transform.Publish.Books();
            Transform.Publish.Convers();
            Transform.Publish.DirectoryListings();
            return;
        }

        if (InstallGlobal)
        {
            // Install bibmark on Linux & macOS systems
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    if (Directory.Exists("/usr/bin") && !File.Exists("/usr/bin/bibmark"))
                    {
                        File.WriteAllText("/usr/bin/bibmark", "#!/bin/bash\ndotnet" + Assembly.GetExecutingAssembly().Location + " \"$@\"\n");
                        Process.Start("chmod", "+x /usr/bin/bibmark")?.WaitForExit();
                        Console.WriteLine("Installed bibmark to /usr/bin/bibmark");
                    }
                }
                catch { }
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    if (Directory.Exists("/usr/local/bin") && !File.Exists("/usr/local/bin/bibmark"))
                    {
                        File.WriteAllText("/usr/local/bin/bibmark", "#!/bin/bash\ndotnet" + Assembly.GetExecutingAssembly().Location + " \"$@\"\n");
                        Process.Start("chmod", "+x /usr/local/bin/bibmark")?.WaitForExit();
                        Console.WriteLine("Installed bibmark to /usr/local/bin/bibmark");
                    }
                }
                catch { }
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
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
                    }
                }
                catch { }
            }
        }

        // Get the version of the current application.
        var aname = Assembly.GetExecutingAssembly().GetName();
        Log($"{aname.Name}, v{aname.Version.ToString(3)}");

        if (args.Contains("-h") || args.Contains("-help") || args.Contains("--help") || args.Contains("-?"))
        {
            ShowHelp();
            return;
        }

        if (args.Contains("-version") || args.Contains("--version") || args.Contains("-v"))
        {
            Transform = new Transform("");
            Log($"pandoc version: {Transform.Pandoc.Version.ToString()}");
            return;
        }

        var argslist = args.ToList();
        var command = argslist.FirstOrDefault();
        if (command == "import" || command == "publish" || command == "twolanguage")
            argslist.RemoveAt(0);

        var projectPos = argslist.IndexOf("-p");
        if (projectPos < 0) projectPos = argslist.IndexOf("-project");
        if (projectPos < 0) projectPos = argslist.IndexOf("--project");
        if (projectPos >= 0 && projectPos < argslist.Count - 1)
        {
            argslist.RemoveAt(projectPos);
            projectPath = args[projectPos];
            argslist.RemoveAt(projectPos);
        }
        bool fromSource = false;
        var sourcePos = argslist.IndexOf("-s");
        if (sourcePos < 0) projectPos = argslist.IndexOf("-src");
        if (sourcePos < 0) projectPos = argslist.IndexOf("-source");
        if (sourcePos < 0) projectPos = argslist.IndexOf("--source");
        if (sourcePos >= 0)
        {
            fromSource = true;
            argslist.RemoveAt(sourcePos);
            if (sourcePos < argslist.Count && !argslist[sourcePos].StartsWith("-"))
            {
                sourcePath = argslist[sourcePos];
                argslist.RemoveAt(sourcePos);
            }
        }

        var outPos = argslist.IndexOf("-o");
        if (outPos < 0) outPos = argslist.IndexOf("-out");
        if (outPos < 0) outPos = argslist.IndexOf("--out");
        if (outPos >= 0 && outPos < argslist.Count-1)
        {
            argslist.RemoveAt(outPos);
            outputPath = args[outPos];
            argslist.RemoveAt(outPos);
        }

        string? language;
        var lnpos = argslist.IndexOf("-ln");
        if (lnpos < 0) lnpos = argslist.IndexOf("--language");
        if (lnpos >= 0 && lnpos < argslist.Count - 1) {
            argslist.RemoveAt(lnpos);
            language = argslist[lnpos];
            argslist.RemoveAt(lnpos);
        } else language = "default";

        string? replace = null;
        var replacepos = argslist.IndexOf("-replace");
        if (replacepos < 0) replacepos = argslist.IndexOf("-r");
        if (replacepos < 0) replacepos = argslist.IndexOf("--replace");
        if (replacepos >= 0 && replacepos < argslist.Count - 1) {
            argslist.RemoveAt(replacepos);
            replace = argslist[replacepos];
            argslist.RemoveAt(replacepos);
        }

        string? left = null, right = null;
        bool twolang = command == "twolanguage";
        if (twolang && argslist.Count >= 2)
        {
            left = argslist[0];
            argslist.RemoveAt(0);
            right = argslist[0];
            argslist.RemoveAt(0);
        }

        if (command == "import")
        {
            if (argslist.Count >= 1 && !argslist[0].StartsWith("-"))
            {
                sourcePath = argslist[0];
               argslist.RemoveAt(0);
            }
            fromSource = true;
        }

        var lowercaseFirstWords = argslist.Contains("-plc") || argslist.Contains("--plc") || argslist.Contains("--lowercase-first-words");
        var eachVerseOnNewLine = !argslist.Contains("-cp") && !argslist.Contains("--cp") && !argslist.Contains("--each-verse-on-newline");
        var importStrongs = argslist.Contains("-strongs") || argslist.Contains("--strongs");

        argslist = argslist.Where(arg => !arg.StartsWith("-")).ToList();
        while (argslist.Count > 0)
        {
            if (projectPath == null)
            {
                projectPath = argslist[0];
                argslist.RemoveAt(0);
            }
            else if (sourcePath == null)
            {
                sourcePath = argslist[0];
                argslist.RemoveAt(0);
            }
            else if (outputPath == null)
            {
                outputPath = argslist[0];
                argslist.RemoveAt(0);
            }
            else break;
        }

        projectPath ??= Environment.CurrentDirectory;

        if (Directory.Exists(projectPath))
        {
            Transform = new Transform(projectPath, sourcePath, outputPath);
            Transform.FromSource = fromSource;
            Transform.EachVerseOnNewLine = eachVerseOnNewLine;
            Transform.Language = language;
            Transform.ImportStrongs = importStrongs;
            Transform.LeftLanguage = left;
            Transform.RightLanguage = right;
            Transform.LowercaseFirstWords = lowercaseFirstWords;
            Transform.Replace = replace;
            Transform.TwoLanguage = twolang;
            if (twolang)
            {
                await Transform.TransformTwoLanguagesAsync(left, right);
            } else {
                await Transform.TransformAsync();
            }
        }
        else if (File.Exists(projectPath)) {
            Transform = new Transform(projectPath, sourcePath, outputPath);
            Transform.FromSource = fromSource;
            Transform.EachVerseOnNewLine = eachVerseOnNewLine;
            Transform.Language = language;
            Transform.ImportStrongs = importStrongs;
            Transform.LeftLanguage = left;
            Transform.RightLanguage = right;
            Transform.LowercaseFirstWords = lowercaseFirstWords;
            Transform.Replace = replace;
            Transform.TwoLanguage = twolang;
            await Transform.Create.TransformFileAsync(projectPath);
        }
    }
}
