using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BibleMarkdown;

public class Epub
{
	public bool CreateChapterLinks = true;
	public bool Links = true;
	public string TableOfContentsPage = "ch001.xhtml";
	public bool OmitTitles = true;
	public bool OmitParagraphs = true;
	public bool OmitFootnotes = true;
	public Func<int, string> Page = book => $"ch{book:d3}.xhtml";

	public Creator Create { get; private set; }
	public Epub(Creator creator) { Create = creator; }

	public bool IsNewer(string file, string srcfile) => Creator.IsNewer(file, srcfile);
    public string Id(string name) =>name.Replace(' ', '-').Replace('.', '-');
	public string Language => Create.Language;
	public bool TwoLanguage => Create.TwoLanguage;
	public void LogFile(string file) => Create.LogFile(file);
	public BookList Books => Create.Books;
	public void ToEpub(string path, string mdfile, string epubfile)
	{
		if (IsNewer(epubfile, mdfile) || TwoLanguage) return;

		string? bookname = Books.Name(mdfile);
		int bookno = Books.Number(mdfile);

		var src = File.ReadAllText(mdfile);

        if (OmitTitles)
		{
			src = Regex.Replace(src, @"(?<=^|\n)##\s+.*?\r?\n", "", RegexOptions.Singleline);
		}

		if (OmitParagraphs)
		{
			src = Regex.Replace(src, @"(?<=\r?\n)[ \t]*\r?\n(?!#)", "", RegexOptions.Singleline);
		}

		if (OmitFootnotes)
		{

		}

		src = Regex.Replace(src, @"(?:^|\n)#[ \t]+(?<chapter>[0-9]+).*?(?=(?:\r?\n#[ \t]+[0-9]+|$))", chapter =>
		{
			return Regex.Replace(chapter.Value, @"\[(?<verse>[0-9]+)\]\{\.bibleverse\}", verse =>
			{
				var vno = verse.Groups["verse"].Value;
				return @$"[{vno}]{{#verse-{Id(bookname)}-{chapter.Groups["chapter"].Value}-{vno} .bibleverse}}";
			}, RegexOptions.Singleline);
		}, RegexOptions.Singleline);

		if (CreateChapterLinks)
		{
			var chapters = Regex.Matches(src, @"(?<=(^|\n)#\s+)[0-9]+", RegexOptions.Singleline);
			var links = new StringBuilder($@"<div id=""chapterlinks-{Id(bookname)}"" class=""chapterlinks"">");
			foreach (Match chapter in chapters)
			{
				links.Append($"[&nbsp;{chapter.Value}&nbsp;](#chapter-{Id(bookname)}-{chapter.Value}) ");
			}
			links.Append("</div>");
			links.AppendLine(); links.AppendLine();
			links.Append(src);
			src = links.ToString();
			// src = Regex.Replace(src, @"(?<=(^|\n)#\s+)([0-9]+)", $@"[$2](#book-{Id(book)}) {{.unnumbered #chapter-{Id(book)}-$2}}", RegexOptions.Singleline);
			src = Regex.Replace(src, @"(?<=(^|\n))(#\s+([0-9]+))", $@"<h2 class=""chaptertitle"">[&nbsp;$3&nbsp;]({Page(bookno)})<span><span id=""chapter-{Id(bookname)}-$3""></span></span></h2>{Environment.NewLine}", RegexOptions.Singleline);
		}


		if (Links)
		{
			var pattern = String.Join('|', Books[Language].Values.Select(b => b.Abbreviation).ToArray());
			src = Regex.Replace(src, @$"(?<book>{pattern})\s+(?<chapter>[0-9]+)([:,](?<verse>[0-9]+)(-(?<upto>[0-9]+))?)", m =>
			{
				var bookabr = Books[Language].Values.FirstOrDefault(b => b.Abbreviation == m.Groups["book"].Value);
				var chapter = m.Groups["chapter"].Value;
				var verse = m.Groups["verse"].Value;
				if (!m.Groups["upto"].Success) return $@"[{m.Groups["book"].Value} {m.Groups["chapter"].Value},{m.Groups["verse"].Value}]({Page(bookabr.Number)}#verse-{Id(bookabr.Name)}-{chapter}-{verse})";
				else return $@"[{m.Groups["book"].Value} {m.Groups["chapter"].Value},{m.Groups["verse"].Value}-{m.Groups["upto"].Value}]({Page(bookabr.Number)}#verse-{Id(bookabr.Name)}-{chapter}-{verse})";
			}, RegexOptions.Singleline);

		}


		src = Regex.Replace(src, @"(?<=\n|^)#", "##", RegexOptions.Singleline);

		src = $@"# [{bookname}]({TableOfContentsPage}) {{#book-{Id(bookname)}}}{Environment.NewLine}{Environment.NewLine}{src}";
		File.WriteAllText(epubfile, src);
		LogFile(epubfile);
	}
}