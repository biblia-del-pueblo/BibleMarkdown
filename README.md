# BibleMarkdown
BibleMarkdown or bibmark.exe is an application that transforms source markup like USFM, BibleEdit or Zefania XML to Bible Markdown and then to LaTeX, HTML, Epub, Pandoc Markdown & USFM.

# Installation
You can install bibmark on Windows, Linux and MacOS. You need to have [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet) installed. Then you can execute the following commands in a shell:
```
dotnet tool install -g BibleMarkdown
```

After this, bibmark is added to your PATH and you can execute it from any folder:
```
bibmark
```

To show a help page execute `bibmark -?`.

Alernatively, on Windows, you can also install BibleMarkdown with the MSI Installer.

# Documentation
Bible Markdown is normal pandoc Markdown with the following extensions:
- For Footnotes you can make them more readable, by placing a marker ^label^ at the place of the footnote, but specifying the footnote later in the text with ordinary ^label^[Footnote] markdown. "label" must be a letter or word without any digits.
- You can have comments by bracing them with /\* and \*/ or by leading with // until the end of the line. /\* \*/ Comments can span multiple lines.
- Verse numbers are denoted with a @ character followed by a number, like this: @1 In the beginning was the Word and the Word was with God and the Word was God. @2 This was in the beginning...
- if the text contains the comment //!verse-paragraphs, each verse is rendered in a paragraph. For use in Psalms and Proverbs.
- Chapter numbers are denoted with a # markdown title and Chapter headings with a ## markdown title
- A special comment //!replace /regularexpression/replacement/regularexpression/replacement/... can be placed in the text. All the regular expressions will be replaced. You can choose another delimiter char than /, the first character encountered will be used as delimiter.

If you have the source text of your Bible in USFM markup, you can place those files in a subfolder src. If you specify the -s argument to bibmark, bibmark searches this folder for USFM source and creates Bible Markdown files in the main folder if the source files are newer than the Bible Markdown files. Instead of USFM you can also place a Zefania XML file or a BibleEdit or a Javascripture file/folder in the src folder. You can also place a folder `bibmark` containing .md files that will be copied to the main folder. That way you can placce a git submodule in the bibmark folder containing BibleMarkdown.
From the Bible Markdown files, bibmark creates Pandoc files in the out/pandoc folder, LaTeX files in the out/tex folder, HTML files in the out/html folder and USFM files in the out/usfm folder.

bibmark also creates a file called outline.md & outline.xml in the out folder that specifies chapter titles, paragraphs and footnotes. If you move this file to the src folder and it is newer than the Bible Markdown files, bibmark applies the chapter titles and paragraphs and footnotes found in the outline.md file to the Bible Markdown files.
In the outline.md file, the Bible Markdown files are specified by a # markdown title, the chapter numbers by a ## markdown title, and chapter titles by a ### markdown title.
Verses that contain a paragraph or a footnote are denoted with superscript markdown notation followed by a \ for a paragraph or a ^^ for a footnote marker, or a ^[Footnote]
footnote.
You can also specify mutliple *.outline.md files, that will be merged.
If you put a //!append directive in one of those files, The titles, paragraphs and footnotes will be added to the .md files. If you ommit //!append, the titles, paragraphs and footnotes in the outline.md files will replace the titles, paragraphs and footnotes in the .md files.
You can also put the .outline.md files in the folder with your .md files, in that case the outlines will be applied to the .md files on output generation.
You can also change the versification of the .outline.md files by putting a //!map *&lt;versification&gt;* directive in the .outline.md file or a Map attribute on the BibleFramework root node in outline.xml.
You then put a verse mapping in the file versification.map.md.
The syntax of the verse mapping md file is as follows:
```
# <book>
<chapter>:<verse>=><tochapter>:<toverse> <chapter2>:<verse2>=><tochapter2>:<toverse2> ...

# <book2>
...
```
For example the following
```
# Números
12:16=>13:1 13:1=>13:2 13:33=>13:33
```
will point 12:16 to 13:1 and then all verses one up, until verse 13:33.
You can create the mapping files by comparing the verseinfo.md (see below) files of the different bibles via the diff tool.

bibmark also creates a file verseinfo.md in the out folder, a file that shows how many verses each chapter has, so you can compare different Bibles versifications.

You can also place a file linklist.xml in the src folder, to specify parallel verses included in footnotes, that will be imported and placed in the footnotes. This file will be exported as ParallelVerses.outline.md & ParallelVerses.outline.xml in the main directory.

For examples of the various input files like booknames.xml, linklist.xml, outline.md etc. you can have a look at the Bibles in the BibliaLibre project at
https://github.com/biblia-del-pueblo/BibliaLibre.