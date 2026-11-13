using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BibleMarkdown;

public class Book
{
	public string? Language;
	public string Name = "";
	public string Abbreviation = "";
	public int Number;
}

public class BookList : SortedList<string?, SortedList<string?, Book>>
{
	/// <summary>The culture of a language of booknames.xml ("german", "spanish2" ...), invariant if there is none.</summary>
	public static CultureInfo Culture(string? language)
	{
		var name = Regex.Replace(language ?? "", "[0-9]", "");
		if (name == "" || name == "default") return CultureInfo.InvariantCulture;
		return CultureInfo.GetCultures(CultureTypes.NeutralCultures)
			.FirstOrDefault(c => c.EnglishName.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
				c.NativeName.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
				c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? CultureInfo.InvariantCulture;
	}

	/// <summary>
	/// Compares book names of a language case independent, by the rules of the culture of the language (the Turkish
	/// dotless i, the German umlauts ...): the book names in usfm files are often in capitals.
	/// </summary>
	public static StringComparer NameComparer(string? language)
	{
		var culture = Culture(language);
		return culture == CultureInfo.InvariantCulture ? StringComparer.InvariantCultureIgnoreCase : StringComparer.Create(culture, ignoreCase: true);
	}

	/// <summary>Whether two book names of a language are the same, ignoring case.</summary>
	public static bool SameName(string? language, string? a, string? b) => NameComparer(language).Equals(a, b);

	Transform Transform { get; set; }
	public BookList(Transform transform): base(StringComparer.InvariantCultureIgnoreCase) {
		Transform = transform;
		Add("default", new SortedList<string?, Book>(NameComparer("default")));
    }
	string Language
	{
		get => Transform.Language;
		set => Transform.Language = value;
	}
	void Log(string message) => Transform.Log(message); 

    public Book this[string? language, int number]
	{
		get
		{
			return base[language]
				.FirstOrDefault(b => b.Value.Number == number)
				.Value;
		}
	}
	public Book this[string? language, string bookname]
	{
		get
		{
			if (base[language].ContainsKey(bookname)) return base[language][bookname];
			else return new Book() { Number = 0, Abbreviation = "", Language = language, Name = bookname };
		}
	}

	public Book this[int number] => this[Language, number];

	public string Name(string file)
	{
		return Regex.Replace(file.EndsWith(".md") ? Path.GetFileNameWithoutExtension(file) : file, "^[0-9.]+-", "", RegexOptions.Singleline).Trim();
	}
	public int Number(string file)
	{
		var match = Regex.Match(file.EndsWith(".md") ? Path.GetFileNameWithoutExtension(file) : file, "^[0-9]+", RegexOptions.Singleline);
		if (match.Success) return int.Parse(match.Value);
		return -1;
	}

	public BookList Load()
	{
		if (Count > 1) return this;

		var path = Transform.ProjectPath;
		var namesfile = Path.Combine(path, "booknames.xml");

		if (!File.Exists(namesfile))
		{
            path = Transform.SourcePath;
            namesfile = Path.Combine(path, "booknames.xml");
			if (!File.Exists(namesfile)) return this;
		}

		XElement xml;
		using (var file = File.Open(namesfile, FileMode.Open, FileAccess.Read))
		{
			xml = XElement.Load(file);
		}

		Log($"Importing booknames.xml.");

		Language = (string?)xml.Attribute("default") ?? Language;

		var languages = xml
			.Elements("ID")
			.Select(langset => new
			{
				Language = (string)langset.Attribute("descr"),
				Books = langset.Elements("BOOK")
					.Select(xml => new Book
					{
						Language = Language,
						Name = xml.Value,
						Abbreviation = (string)xml.Attribute("bshort"),
						Number = (int)xml.Attribute("bnumber")
					})
			});

		foreach (var lang in languages)
		{
			var books = new SortedList<string?, Book>(NameComparer(lang.Language));
			Add(lang.Language, books);
			foreach (var book in lang.Books)
			{
				books.Add(book.Name, book);
			}
		}

		return this;
	}

	public BookList Load(IEnumerable<string> mdfiles)
	{
		if (!ContainsKey("default")) Add("default", new SortedList<string?, Book>(NameComparer("default")));
		
		var books = this["default"];

		if (books.Count > 0) return this;

		foreach (var file in mdfiles)
		{
			var book = new Book() { Language = "default", Abbreviation = "", Name = Name(file), Number = Number(file) };
			if (!books.ContainsKey(book.Name)) books.Add(book.Name, book);
		}
		return this;
	}

    public IEnumerable<Book> All => Values.SelectMany(lang => lang.Values);

	public BookList Books
	{
		get => Transform.Books;
		//set => Transform.Books = value;
	}
	public bool HasBookList => Books != null && Books.Count > 0;
}

public class ParallelVerse
{
	public Location Verse;
	public Location[] ParallelVerses;
}

public class ParallelVerses : List<ParallelVerse>
{
	public Transform Transform { get; private set; }
	public string Language => Transform.Language;
	public BookList Books => Transform.Books;
	public ParallelVerses(Transform tranform): base() { Transform = tranform; }
	public void Log(string message) => Transform.Log(message);

	public ParallelVerses Load(string path)
	{
		if (Count != 0) return this;

		var linklistfile = Path.Combine(path, "linklist.xml");

		if (!File.Exists(linklistfile)) return this;

		Log("Importing linklist.xml");

		Books.Load();

		XElement xml;
		using (var file = File.Open(linklistfile, FileMode.Open, FileAccess.Read))
		{
			xml = XElement.Load(file);
		}
        VerseMaps? Map = null;
        if (xml.Attribute("Map") != null)
        {
            var map = (string)xml.Attribute("Map");
            if (map == "") map = Path.ChangeExtension(linklistfile, ".map.md");
            var mapfile = $"{map}.map.md";
            if (Regex.IsMatch(mapfile, @"^(?!/|[A-Z]:\\)")) mapfile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(mapfile), mapfile));
            if (File.Exists(mapfile))
            {
				Map = new VerseMaps(Transform).Import(mapfile);
            }
        }

		var mapfn = (Location loc) => Map != null ? Map.Map(loc) : loc;
        var verses = xml
			.Descendants("verse")
			.Select(x =>
			{
				var book = Books[Language, (int)x.Attribute("bn")];
                return new ParallelVerse
				{
					Verse = mapfn(new Location
					{
                        Book = book,
                        Chapter = (int)x.Attribute("cn"),
                        Verse = (int)x.Attribute("vn"),
                        UpToVerse = null
                    }),
					ParallelVerses = x.Elements("link")
						.Select(link =>
						{
							var linkbook = Books[(int)link.Attribute("bn")];
							var verse = mapfn(new Location
							{
								Book = linkbook,
								Chapter = (int)link.Attribute("cn1"),
								Verse = Math.Abs((int)link.Attribute("vn1")),
								UpToVerse = link.Attribute("vn2") != null ? (int)link.Attribute("vn2") : null
							});
							verse.Verse = verse.Verse < 1 ? 1 : verse.Verse;
							return verse;
						})
						.ToArray()
				};
			})
			.OrderBy(v => v.Verse.Book.Number)
			.ThenBy(v => v.Verse.Chapter)
			.ThenBy(v => v.Verse.Verse);

		AddRange(verses);

		return this;
	}
}
public class VerseMaps : Dictionary<string, SortedList<Location, Location>>
{
	Transform Transform { get; set; }
	BookList Books => Transform.Books;
	string Language => Transform.Language;
	public VerseMaps(Transform transform) { Transform = transform; }
	public void Log(string message) => Transform.Log(message);
	public Location Map(Location verse)
	{
		if (verse.Verse < 0) verse.Verse = 1;
		if (verse.Verse == 0)
		{

		}
		SortedList<Location, Location> book;
		if (!TryGetValue(verse.Book.Name, out book)) return verse;
		int i = 0, j = book.Count-1, m = 0;
		m = (i + j + 1) / 2;
		Location key = book.Keys[m];
		Location dest;
		while (j>i)
		{

			if (key.Chapter > verse.Chapter || key.Chapter == verse.Chapter && key.Verse > verse.Verse)
			{
				j = m-1;
			}
			else if (key.Chapter == verse.Chapter && key.Verse == verse.Verse)
			{
				i = j = m;
			}
			else
			{ 
				i = m;
			}
			m = (i + j + 1) / 2;
			key = book.Keys[m];
		}
		if (key != null && (key.Chapter > verse.Chapter || key.Chapter == verse.Chapter && key.Verse > verse.Verse)) {
			key = null;
		}
		if (key == null) return verse;
		dest = book.Values[m];
		if ((key.Chapter <= verse.Chapter || key.Chapter == verse.Chapter && key.Verse <= verse.Verse))
		{
			var loc = new Location
			{
				Book = verse.Book,
				Chapter = verse.Chapter - key.Chapter + dest.Chapter,
				Verse = verse.Verse - key.Verse + dest.Verse,
			};
			if (loc.Chapter != verse.Chapter || loc.Verse != verse.Verse)
			{
				Log($"Verse mapped from {verse.Book.Name} {verse.Chapter}:{verse.Verse} to {loc.Chapter}:{loc.Verse}.");
			}
			if (verse.UpToVerse.HasValue)
			{
				var upto = new Location
				{
					Book = verse.Book,
					Chapter = verse.Chapter,
					Verse = verse.UpToVerse ?? 0,
					UpToVerse = null
				};
				upto = Map(upto);
				if (upto.Chapter != loc.Chapter || upto.Verse < loc.Verse)
				{
					loc.UpToVerse = null;
				}
				else
				{
					loc.UpToVerse = upto.Verse;
				}
			}
			return loc;
		}
		else
		{
			return verse;
		}
	}

	public VerseMaps? Import(string? mapfile)
	{
		if (mapfile == null || !File.Exists(mapfile)) return null;

		Clear();

		var src = File.ReadAllText(mapfile);
		var books = Regex.Matches(src, @"(?<=^|\n)#\s+(?<book>.*?)[ \t]*\r?\n(?<map>.*?)(?=\r?\n#\s)", RegexOptions.Singleline)
			.Select(m => new
			{
				Book = m.Groups["book"].Value,
				Map = m.Groups["map"].Value
			});
		foreach (var book in books)
		{
			var bookfromlist = Books[Language].Values.FirstOrDefault(b => b.Name == book.Book);

			if (bookfromlist == null)
			{
				Log($"Error: No book called {book.Book} booknames.xml");
				continue;
			}
			var map = Regex.Matches(book.Map, @"([0-9]+):([0-9]+)=>([0-9]+):([0-9]+)", RegexOptions.Singleline)
				.Select(m => new
				{
					From = new Location()
					{
						Book = bookfromlist,
						Chapter = int.Parse(m.Groups[1].Value),
						Verse = int.Parse(m.Groups[2].Value)
					},
					To = new Location()
					{
						Book = bookfromlist,
						Chapter = int.Parse(m.Groups[3].Value),
						Verse = int.Parse(m.Groups[4].Value)
					}
				})
				.ToArray();

			if (map.Any())
			{
				var list = new SortedList<Location, Location>();
				Add(book.Book, list);
				foreach (var node in map)
				{
					list.Add(node.From, node.To);
				}
			}
		}
		return this;
	}
}

public class Location: IComparable<Location>
{
	public Book Book;
	public int Chapter;
	public int Verse;
	public int? UpToVerse;

	public static Location Zero => new Location()
	{
		Book = new Book() { Name = "", Number = 0, Abbreviation = "", Language = "default" },
		Chapter = 0,
		Verse = -1
	};
	public static int Compare(Location a, Location b)
	{
		if (a.Book.Number < b.Book.Number) return -1;
		if (a.Book.Number > b.Book.Number) return 1;
		if (a.Chapter < b.Chapter) return -1;
		if (a.Chapter > b.Chapter) return 1;
		if (a.Verse < b.Verse) return -1;
		if (a.Verse > b.Verse) return 1;
		return 0;
	}
	int IComparable<Location>.CompareTo(Location? other) => Compare(this, other);
}