namespace BibleMarkdown
{
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
		public bool MapVerses;
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

}