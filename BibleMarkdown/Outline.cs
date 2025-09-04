using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using static BibleMarkdown.Program;
using static System.Reflection.Metadata.BlobBuilder;

namespace BibleMarkdown;

public class  Outline: List<OutlineItem>
{
    public BookList Books => Program.Books;
    public string Language => Program.Language;
    public void LogFile(string filename) => Program.LogFile(filename);

    public bool Append { get; set; } = false;

    public Outline() { }
    public Outline(IEnumerable<OutlineItem> items): this()
    {
        Clear();
        AddRange(items);
    }

    public void AddMdOutline(string filename)
    {
        var frame = File.ReadAllText(filename);

        frame = Regex.Replace(frame, "/\\*(?!!).*?\\*/", "", RegexOptions.Singleline); // remove /* */ comments
        frame = Regex.Replace(frame, "//(?!!).*?\\r?\\n", "", RegexOptions.Singleline); // remove // comments

        // Load versification map
        VerseMaps? Map = null;
        var mapVerses = Regex.Match(frame, @"(//|/\*)!map\s*(?<map>\.*?)($|\*/|\r?\n)", RegexOptions.Singleline);
        var map = mapVerses.Success ?
            mapVerses.Groups["map"].Success ? 
                mapVerses.Groups["map"].Value.Trim() : 
                "" :
                null;
        if (map == "") map = Path.ChangeExtension(filename, ".map.md");
         var mapfile = $"{map}.map.md";

        Append |= Regex.IsMatch(frame, @"(//|/\*)!append\.*?($|\*/|\r?\n)", RegexOptions.Singleline);

        if (Regex.IsMatch(mapfile, @"^(?!/|[A-Z]:\\)")) mapfile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(mapfile), mapfile));
        if (File.Exists(mapfile))
        {
            Map = new VerseMaps().Import(mapfile);
        }

        var books = Regex.Matches(frame, @"(^|\n)#\s+(?<book>.*?)[ \t]*\r?\n(?<bookbody>.*?)(?=\r?\n#\s|$)", RegexOptions.Singleline)
            .Select(match => new
            {
                Name = Books.Name(match.Groups["book"].Value),
                Body = match.Groups["bookbody"].Value,
                Book = Books[Language, Books.Name(match.Groups["book"].Value)]
            });
        var existingBooks = this.OfType<BookItem>().ToDictionary(book => book.Name);

        foreach (var book in books)
        {
            var bookItem = new BookItem(book.Book, $"{book.Book.Number:d2}-{book.Name}.md")
            {
                VerseParagraphs = Regex.IsMatch(book.Body, @"(//|/\*)!verse-paragraphs.*?($|\r?\n|\*/)", RegexOptions.Singleline)
            };
            if (existingBooks.ContainsKey(bookItem.Name))
            {
                // Update existing book item
                var existingBookItem = existingBooks[bookItem.Name];
                existingBookItem.File = bookItem.File;
                existingBookItem.VerseParagraphs |= bookItem.VerseParagraphs;
                bookItem = existingBookItem;
            }
            else Add(bookItem);

            var chapters = Regex.Matches(book.Body, @"(^|\n)##\s+(?<chapter>[0-9]+).*?\r?\n(?<chapterbody>.*?)(?=\r?\n##\s|$)", RegexOptions.Singleline)
                .Select(match => new
                {
                    Chapter = int.Parse(match.Groups["chapter"].Value),
                    Body = match.Groups["chapterbody"].Value
                });
            var existingChapters = bookItem.Items.OfType<ChapterItem>().ToDictionary(ch => ch.Chapter);
            foreach (var chapter in chapters)
            {
                ChapterItem chapterItem;
                if (existingChapters.ContainsKey(chapter.Chapter))
                {
                    chapterItem = existingChapters[chapter.Chapter];
                }
                else
                {
                    chapterItem = new ChapterItem(book.Book, chapter.Chapter);
                    MapItem(Map, chapterItem);
                    Add(chapterItem);
                    bookItem.Items.Add(chapterItem);
                }

                int verse = -1;
                var tokens = Regex.Matches(chapter.Body, @"\^(?<verse>-?[0-9]+)\^|@(?<verse2>-?[0-9]+)|(?<paragraph>\\)|(?<footnote>\^\[(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))\])|(?<=\n)###\s+(?<title>.*?)(\r?\n|$)", RegexOptions.Singleline)
                    .Select(match =>
                    {

                        if (match.Groups["verse"].Success) verse = int.Parse(match.Groups["verse"].Value);
                        else if (match.Groups["verse2"].Success) verse = int.Parse(match.Groups["verse2"].Value);

                        return new
                        {
                            Verse = verse,
                            Class = match.Groups["paragraph"].Success ?
                                    OutlineItemClass.Paragraph : match.Groups["footnotes"].Success ?
                                        OutlineItemClass.Footnote : match.Groups["title"].Success ?
                                        OutlineItemClass.Title : OutlineItemClass.Verse,
                            Footnote = match.Groups["footnotes"].Success ? match.Groups["footnotes"].Value : "",
                            Title = match.Groups["title"].Success ? match.Groups["title"].Value : ""
                        };
                    });

                foreach (var token in tokens)
                {
                    switch (token.Class)
                    {
                        case OutlineItemClass.Title:
                            var titleItem = new TitleItem(book.Book, token.Title, chapterItem.Chapter, token.Verse);
                            MapItem(Map, titleItem);
                            Add(titleItem);
                            bookItem.Items.Add(titleItem);
                            break;
                        case OutlineItemClass.Footnote:
                            var footnoteItem = new FootnoteItem(book.Book, token.Footnote, chapterItem.Chapter, token.Verse);
                            MapItem(Map, footnoteItem);
                            Add(footnoteItem);
                            bookItem.Items.Add(footnoteItem);
                            break;
                        case OutlineItemClass.Paragraph:
                            var paragraphItem = new ParagraphItem(book.Book, chapterItem.Chapter, token.Verse);
                            MapItem(Map, paragraphItem);
                            Add(paragraphItem);
                            bookItem.Items.Add(paragraphItem);
                            break;
                    }
                }
            }
        }
    }

    public void AddXmlOutline(string filename)
    {
        XElement frame;
        using (var file = File.Open(filename, FileMode.Open, FileAccess.Read))
        {
            frame = XElement.Load(file);
        }
        VerseMaps? Map = null;
        if (frame.Attribute("Map") != null)
        {
            var map = (string)frame.Attribute("Map");
            if (map == "") map = Path.ChangeExtension(filename, ".map.md");
            var mapfile = $"{map}.map.md";
            if (Regex.IsMatch(mapfile, @"^(?!/|[A-Z]:\\)")) mapfile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(mapfile), mapfile));
            if (File.Exists(mapfile))
            {
                Map = new VerseMaps().Import(mapfile);
            }
        }

        Append |= frame.Attributes("Append").Any() && (bool)frame.Attribute("Append");

        var existingBooks = this.OfType<BookItem>().ToDictionary(book => book.Name);
        foreach (XElement file in frame.Elements("Book"))
        {
            var bookname = (string)file.Attribute("Name");
            var book = Books[Language, bookname];

            BookItem bookItem = new BookItem(book, (string)file.Attribute("File"));
            bookItem.VerseParagraphs = file.Attributes("VerseParagraphs").Any() && (bool)file.Attribute("VerseParagraph");
            if (existingBooks.ContainsKey(bookItem.Name))
            {
                // Update existing book item
                var existingBookItem = existingBooks[bookItem.Name];
                existingBookItem.File = bookItem.File;
                existingBookItem.VerseParagraphs |= bookItem.VerseParagraphs;
                bookItem = existingBookItem;
            }
            else Add(bookItem);

            var existingChapters = bookItem.Items.OfType<ChapterItem>().ToDictionary(ch => ch.Chapter);
            foreach (XElement chapter in file.Elements("Chapter"))
            {
                var chapterno = (int)chapter.Attribute("Number");

                ChapterItem chapterItem;
                if (existingChapters.ContainsKey(chapterno))
                {
                    chapterItem = existingChapters[chapterno];
                }
                else
                {
                    chapterItem = new ChapterItem(book, chapterno);
                    MapItem(Map, chapterItem);
                    Add(chapterItem);
                    bookItem.Items.Add(chapterItem);
                }

                foreach (XElement x in chapter.Elements())
                {
                    if (x.Name == "Title")
                    {
                        var titleItem = new TitleItem(book, x.Value, chapterItem.Chapter, (int)x.Attribute("Verse"));
                        MapItem(Map, titleItem);
                        Add(titleItem);
                        bookItem.Items.Add(titleItem);
                    }
                    else if (x.Name == "Footnote")
                    {
                        var footnoteItem = new FootnoteItem(book, x.Value, chapterItem.Chapter, (int)x.Attribute("Verse"));
                        MapItem(Map, footnoteItem);
                        Add(footnoteItem);
                        bookItem.Items.Add(footnoteItem);
                    }
                    else if (x.Name == "Paragraph")
                    {
                        var paragraphItem = new ParagraphItem(book, chapterItem.Chapter, (int)x.Attribute("Verse"));
                        MapItem(Map, paragraphItem);
                        Add(paragraphItem);
                        bookItem.Items.Add(paragraphItem);
                    }
                }
            }
        }
    }

    public OutlineItem MapItem(VerseMaps map, OutlineItem item)
    {
        if (map != null && (item is TitleItem || item is ParagraphItem || item is FootnoteItem))
        {
            item.Location = map.Map(item.Location);
            if (item is FootnoteItem footnote)
            {
                // map footnote references
                var books = Books[Language]
                    .Select(book => Regex.Escape(book.Value.Abbreviation))
                    .ToArray();
                var bookspattern = String.Join('|', books);
                footnote.Footnote = Regex.Replace(footnote.Footnote, $@"\s(?<book>{bookspattern})\s+(?<chapter[0-9]+)(?<separator>[:,])(?<verse>[0-9]+)(?:-(?<upto>[0-9]+))?", match =>
                {
                    var bookabbrevation = match.Groups["book"].Value;
                    var chapter = int.Parse(match.Groups["chapter"].Value);
                    var verse = int.Parse(match.Groups["verse"].Value);
                    int upto = -1;
                    if (match.Groups["upto"].Success) upto = int.Parse(match.Groups["upto"].Value);
                    Location? location = null, uptolocation = null;
                    location = new Location()
                    {
                        Book = Books[Language].Values.FirstOrDefault(book => book.Abbreviation == bookabbrevation),
                        Chapter = chapter,
                        Verse = verse
                    };
                    location = VerseMaps.Footnotes.Map(location);
                    if (upto != -1)
                    {
                        uptolocation = new Location()
                        {
                            Book = Books[Language].Values.FirstOrDefault(book => book.Abbreviation == bookabbrevation),
                            Chapter = chapter,
                            Verse = upto
                        };
                        uptolocation = VerseMaps.Footnotes.Map(uptolocation);
                        if (uptolocation.Chapter != location.Chapter || uptolocation.Verse <= location.Verse) uptolocation = null;
                    }

                    var uptostring = uptolocation != null ? $"-{uptolocation.Verse}" : "";
                    return $" {bookabbrevation} {location.Chapter}{match.Groups["separator"].Value}{location.Verse}{uptostring}";
                });
            }
        }
        return item;
    }
    public new void Sort()
    {
        // Sort mapped items
        base.Sort();
        foreach (var book in this.OfType<BookItem>())
        {
            book.Items.Sort();
        }

        // remove ParagraphItems that are duplicate of a TitleItem and remove duplicate BookItems and ChapterItems
        var temp = this.ToList();
        Clear();
        List<OutlineItem> sameLocation = new List<OutlineItem>(10);
        for (int i = 0; i < temp.Count;)
        {
            int j = i + 1;
            sameLocation.Add(temp[i]);
            while (j < temp.Count && Location.Compare(temp[i].Location, temp[j].Location) == 0)
            {
                sameLocation.Add(temp[j++]);
            }
            i = j;
            IEnumerable<OutlineItem> toAdd;
            if (sameLocation.Any(x => x is TitleItem)) toAdd = sameLocation.Where(x => !(x is ParagraphItem));
            else toAdd = sameLocation;
            if (toAdd.OfType<BookItem>().Count() > 1) toAdd = toAdd
                    .Where(x => !(x is BookItem))
                    .Concat(
                        toAdd.OfType<BookItem>()
                            .Take(1)
                    );
            if (toAdd.OfType<ChapterItem>().Count() > 1) toAdd = toAdd
                    .Where(x => !(x is ChapterItem))
                    .Concat(
                        toAdd.OfType<ChapterItem>()
                            .Take(1)
                    );
            AddRange(toAdd);
            sameLocation.Clear();
        }

        BookItem? bookItem = null;
        // set BookItem items collection
        foreach (var item in this)
        {
            if (item is BookItem)
            {
                bookItem = (BookItem)item;
                bookItem.Items.Clear();
            }
            else if (bookItem != null && item.Location.Book.Number == bookItem.Location.Book.Number) bookItem.Items.Add(item);
        }
    }

    public new void Merge()
    {
        var list = this.ToList();
        foreach (var item in list)
        {
            if (item is BookItem book)
            {
                var existing = this.OfType<BookItem>().FirstOrDefault(b => b.Name == book.Name);
                if (existing != null && existing != book)
                {
                    // Merge items
                    foreach (var subitem in book.Items)
                    {
                        if (!existing.Items.Contains(subitem))
                        {
                            existing.Items.Add(subitem);
                            base.Add(subitem);
                        }
                    }
                    // Remove duplicate book
                    base.Remove(book);
                }
            }
        }
    }

    public void Save(string filename)
    {
        Sort();

        var result = new StringBuilder();
        Location lastlocation = Location.Zero;

        XElement? filexml = null, chapterxml = null;
        XElement root = new XElement("BibleFramework");
        if (Append) {
            root.Add(new XAttribute("Append", true));
            result.AppendLine("//!append");
        }
        foreach (var item in this)
        {
            if (item is BookItem)
            {
                var bookItem = (BookItem)item;
                result.AppendLine($"{Environment.NewLine}# {bookItem.Name}");
                filexml = new XElement("Book");
                filexml.Add(new XAttribute("Name", bookItem.Name));
                filexml.Add(new XAttribute("File", bookItem.File));
                root.Add(filexml);
            }
            else if (item is ChapterItem)
            {
                result.AppendLine($"{Environment.NewLine}## {item.Chapter}");
                chapterxml = new XElement("Chapter");
                chapterxml.Add(new XAttribute("Number", item.Chapter));
                if (filexml == null) Log("Error: No file for outline.");
                else filexml.Add(chapterxml);
            }
            else if (item is TitleItem)
            {
                var titleItem = (TitleItem)item;
                if (Location.Compare(lastlocation, item.Location) != 0) result.AppendLine(Program.ParagraphVerses ? $"@{item.Verse}" : $"^{item.Verse}^");
                var title = new XElement("Title");
                title.Value = titleItem.Title.Trim();
                title.Add(new XAttribute("Verse", item.Verse));
                if (chapterxml != null) chapterxml.Add(title);
                else Log("Error: No chapter for outline.");
                result.AppendLine($"{Environment.NewLine}###{titleItem.Title}");
            }
            else if (item is FootnoteItem)
            {
                var footnoteItem = (FootnoteItem)item;
                if (Location.Compare(lastlocation, item.Location) != 0) result.Append(Program.ParagraphVerses ? $"@{item.Verse} " : $"^{item.Verse}^ ");
                var footnote = new XElement("Footnote");
                footnote.Value = footnoteItem.Footnote;
                footnote.Add(new XAttribute("Verse", item.Verse));
                if (chapterxml != null) chapterxml.Add(footnote);
                else Log("Error: No chapter for outline.");
                result.Append(footnoteItem.Footnote); result.Append(' ');
            }
            else if (item is ParagraphItem)
            {
                if (Location.Compare(lastlocation, item.Location) != 0) result.Append(Program.ParagraphVerses ? $"@{item.Verse} " : $"^{item.Verse}^ ");
                var paragraph = new XElement("Paragraph");
                paragraph.Add(new XAttribute("Verse", item.Verse));
                if (chapterxml != null) chapterxml.Add(paragraph);
                else Log("Error: No chapter for outline.");
                result.Append("\\ ");
            }
            lastlocation = item.Location;
        }

        File.WriteAllText(filename, result.ToString());
        string outlinexml = Path.ChangeExtension(filename, ".xml");
        root.Save(outlinexml);
        LogFile(filename);
        LogFile(outlinexml);
    }
}

public enum OutlineItemClass { Book, Chapter, Title, Footnote, Paragraph, Verse }
public class OutlineItem: IComparable<OutlineItem>
{
	public Location Location;
	public OutlineItemClass Class
	{
		get
		{
			if (this is BookItem) return OutlineItemClass.Book;
			if (this is ChapterItem) return OutlineItemClass.Chapter;
			if (this is TitleItem) return OutlineItemClass.Title;
			if (this is FootnoteItem) return OutlineItemClass.Footnote;
			if (this is ParagraphItem) return OutlineItemClass.Paragraph;
			else throw new NotSupportedException();
		}
	}

	public int Verse { get { return Location.Verse; } set { Location.Verse = value; } }
	public int Chapter { get { return Location.Chapter; } set { Location.Verse = value; } }

	public OutlineItem(Book book, int chapter = 0, int verse = -1)
	{
		Location = new Location()
		{
			Book = book,
			Chapter = chapter,
			Verse = verse
		};
	}

	public int CompareTo(OutlineItem? other)
	{
		var sameloc = Location.Compare(Location, other.Location);
		if (sameloc != 0) return sameloc;
		if (this is ParagraphItem || this is TitleItem)
			if (!(other is ParagraphItem ||other is TitleItem)) return 1;
			else return 0;
		else if (other is ParagraphItem || other is TitleItem) return -1;
		else return 0;
	}
}

public class BookItem : OutlineItem
{
	public string Name;
	public string File;
	public bool VerseParagraphs;
	public Outline Outline;
	public List<OutlineItem> Items = new List<OutlineItem>();
	public BookItem(Book book, string file) : base(book, 0, -1)
	{
		Name = book.Name;
		File = file;
	}
}

public class ChapterItem : OutlineItem
{
	public ChapterItem(Book book, int chapter) : base(book, chapter, -1) { }
}
public class TitleItem : OutlineItem
{
	public string Title;

	public TitleItem(Book book, string title, int chapter, int verse) : base(book, chapter, verse)
	{
		Title = title;
	}
}

public class FootnoteItem : OutlineItem
{
	public string Footnote;
	public FootnoteItem(Book book, string footnote, int chapter, int verse) : base(book, chapter, verse)
	{
		Footnote = footnote;
	}
}

public class ParagraphItem : OutlineItem
{
	public ParagraphItem(Book book, int chapter, int verse) : base(book, chapter, verse) { }
}