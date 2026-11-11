using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BibleMarkdown;

/// <summary>
/// A versification of The SWORD Project: the books of a module with the number of verses of every chapter. A module
/// stores its verses in one long list without keys, so only this structure tells which entry is which verse. The
/// .conf file only names the versification. KJV is built in; for another one the structure is derived from the
/// module itself (<see cref="SwordModule"/>), which works for modules that mark their books and chapters.
/// </summary>
public sealed partial class SwordVersification
{
	/// <param name="Number">1 to 66 for the books of the Protestant canon, else after them in the order of the module.</param>
	/// <param name="NewTestament">Whether the book is in the index of the New Testament (else the one of the Old).</param>
	public sealed record Book(int Number, string OsisId, string Name, int[] Verses, bool NewTestament)
	{
		public Book(int number, string osisId, string name, int[] verses) : this(number, osisId, name, verses, number > 39) { }

		public int Chapters => Verses.Length;
	}

	/// <summary>The number and the English name of a book by its OSIS id: the 66 books of the canon and the usual deuterocanonical ones.</summary>
	public static (int Number, string Name)? KnownBook(string osisId)
	{
		var book = KJV.Books.FirstOrDefault(b => b.OsisId == osisId);
		if (book != null) return (book.Number, book.Name);
		return Deuterocanonical.TryGetValue(osisId, out var name) ? (0, name) : null;
	}

	static readonly Dictionary<string, string> Deuterocanonical = new()
	{
		["Tob"] = "Tobit", ["Jdt"] = "Judith", ["AddEsth"] = "Additions to Esther", ["Wis"] = "Wisdom of Solomon",
		["Sir"] = "Sirach", ["Bar"] = "Baruch", ["EpJer"] = "Letter of Jeremiah", ["PrAzar"] = "Prayer of Azariah",
		["Sus"] = "Susanna", ["Bel"] = "Bel and the Dragon", ["1Macc"] = "1 Maccabees", ["2Macc"] = "2 Maccabees",
		["3Macc"] = "3 Maccabees", ["4Macc"] = "4 Maccabees", ["PrMan"] = "Prayer of Manasseh", ["1Esd"] = "1 Esdras",
		["2Esd"] = "2 Esdras", ["AddPs"] = "Psalm 151", ["Odes"] = "Odes", ["PssSol"] = "Psalms of Solomon",
		["EpLao"] = "Laodiceans", ["1En"] = "1 Enoch", ["Jub"] = "Jubilees", ["AddDan"] = "Additions to Daniel",
	};

	/// <summary>A structure that was derived from a module.</summary>
	public static SwordVersification Derived(IReadOnlyList<Book> books) => new("derived from the module", books);

	public string Name { get; }
	public IReadOnlyList<Book> Books { get; }

	readonly Dictionary<int, int> bookStart = new();       // book number -> index entry of the heading of the book
	readonly Dictionary<int, int[]> chapterStart = new();  // book number -> index entry of the heading of every chapter
	readonly int[] entries = new int[2];                   // number of index entries of the Old and the New Testament

	SwordVersification(string name, IReadOnlyList<Book> books)
	{
		Name = name;
		Books = books;
		// Every testament has its own index: entry 0 is the heading of the module, entry 1 the heading of the
		// testament; then for every book its heading, and for every chapter its heading followed by its verses.
		for (int testament = 0; testament < 2; testament++)
		{
			int index = 2;
			foreach (var book in books.Where(b => b.NewTestament == (testament == 1)))
			{
				bookStart[book.Number] = index++;
				var chapters = new int[book.Chapters];
				for (int chapter = 0; chapter < chapters.Length; chapter++)
				{
					chapters[chapter] = index;
					index += 1 + book.Verses[chapter];
				}
				chapterStart[book.Number] = chapters;
			}
			entries[testament] = index;
		}
	}

	/// <summary>How many entries the index of a testament has in this versification.</summary>
	public int Entries(bool newTestament) => entries[newTestament ? 1 : 0];

	/// <summary>The index entry of a verse. Chapter 0 is the heading of the book, verse 0 the heading of the chapter.</summary>
	public int Index(Book book, int chapter, int verse) =>
		chapter == 0 ? bookStart[book.Number] : chapterStart[book.Number][chapter - 1] + verse;

	/// <summary>The versification with this name, or null if it is not supported. A module without one uses KJV.</summary>
	public static SwordVersification? Find(string? name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Trim().Equals("KJV", StringComparison.OrdinalIgnoreCase)) return KJV;
		return Systems.TryGetValue(name.Trim(), out var system) ? system.Value : null;
	}

	public static string Supported => "KJV, " + string.Join(", ", Systems.Keys);

	/// <summary>
	/// The built-in versification that fits a Bible best. <paramref name="verses"/> maps a book number (1 to 66) to the
	/// highest verse number of every chapter. Every chapter that is missing in the table, has another number of verses,
	/// or is missing in the Bible although its book is present counts as a mismatch; books that the Bible does not
	/// have are ignored. On equal scores the <paramref name="preferred"/> versification wins, then KJV, then the table order.
	/// Returns null if there are no verses.
	/// </summary>
	public static SwordVersification? Detect(IReadOnlyDictionary<int, int[]> verses, string? preferred, out int mismatches)
	{
		mismatches = 0;
		if (verses.Count == 0) return null;

		SwordVersification? best = null;
		int bestScore = int.MaxValue;
		var candidates = new[] { KJV }.Concat(Systems.Values.Select(system => system.Value));
		foreach (var candidate in candidates)
		{
			int score = 0;
			foreach (var (number, counts) in verses)
			{
				var book = candidate.Books.FirstOrDefault(b => b.Number == number);
				if (book == null) { score += counts.Length; continue; }
				for (int chapter = 0; chapter < Math.Max(book.Chapters, counts.Length); chapter++)
				{
					if (chapter >= book.Chapters || chapter >= counts.Length || book.Verses[chapter] != counts[chapter]) score++;
				}
			}
			var preferredWins = score == bestScore && candidate.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase);
			if (score < bestScore || preferredWins)
			{
				best = candidate;
				bestScore = score;
			}
		}
		mismatches = bestScore;
		return best;
	}

	/// <summary>
	/// A versification of the table of SWORD (see SwordVersifications.cs). The books of the canon are numbered 1 to 66
	/// as in KJV, the others from 67 on in the order of the table; the names are the English ones.
	/// </summary>
	static SwordVersification Make(string name, (string OsisId, string Name, bool NewTestament, int[] Verses)[] books)
	{
		int extra = 66;
		var result = new List<Book>();
		foreach (var (osisId, bookName, newTestament, verses) in books)
		{
			var known = KnownBook(osisId);
			int number = known is { Number: > 0 } ? known.Value.Number : ++extra;
			result.Add(new Book(number, osisId, known?.Name ?? bookName, verses, newTestament));
		}
		return new SwordVersification(name, result);
	}

	/// <summary>The 66 books of the Protestant canon with 1189 chapters and 31102 verses.</summary>
	public static readonly SwordVersification KJV = new("KJV", new Book[]
	{
			new(1, "Gen", "Genesis", [31,25,24,26,32,22,24,22,29,32,32,20,18,24,21,16,27,33,38,18,34,24,20,67,34,35,46,22,35,43,55,32,20,31,29,43,36,30,23,23,57,38,34,34,28,34,31,22,33,26]),
			new(2, "Exod", "Exodus", [22,25,22,31,23,30,25,32,35,29,10,51,22,31,27,36,16,27,25,26,36,31,33,18,40,37,21,43,46,38,18,35,23,35,35,38,29,31,43,38]),
			new(3, "Lev", "Leviticus", [17,16,17,35,19,30,38,36,24,20,47,8,59,57,33,34,16,30,37,27,24,33,44,23,55,46,34]),
			new(4, "Num", "Numbers", [54,34,51,49,31,27,89,26,23,36,35,16,33,45,41,50,13,32,22,29,35,41,30,25,18,65,23,31,40,16,54,42,56,29,34,13]),
			new(5, "Deut", "Deuteronomy", [46,37,29,49,33,25,26,20,29,22,32,32,18,29,23,22,20,22,21,20,23,30,25,22,19,19,26,68,29,20,30,52,29,12]),
			new(6, "Josh", "Joshua", [18,24,17,24,15,27,26,35,27,43,23,24,33,15,63,10,18,28,51,9,45,34,16,33]),
			new(7, "Judg", "Judges", [36,23,31,24,31,40,25,35,57,18,40,15,25,20,20,31,13,31,30,48,25]),
			new(8, "Ruth", "Ruth", [22,23,18,22]),
			new(9, "1Sam", "1 Samuel", [28,36,21,22,12,21,17,22,27,27,15,25,23,52,35,23,58,30,24,42,15,23,29,22,44,25,12,25,11,31,13]),
			new(10, "2Sam", "2 Samuel", [27,32,39,12,25,23,29,18,13,19,27,31,39,33,37,23,29,33,43,26,22,51,39,25]),
			new(11, "1Kgs", "1 Kings", [53,46,28,34,18,38,51,66,28,29,43,33,34,31,34,34,24,46,21,43,29,53]),
			new(12, "2Kgs", "2 Kings", [18,25,27,44,27,33,20,29,37,36,21,21,25,29,38,20,41,37,37,21,26,20,37,20,30]),
			new(13, "1Chr", "1 Chronicles", [54,55,24,43,26,81,40,40,44,14,47,40,14,17,29,43,27,17,19,8,30,19,32,31,31,32,34,21,30]),
			new(14, "2Chr", "2 Chronicles", [17,18,17,22,14,42,22,18,31,19,23,16,22,15,19,14,19,34,11,37,20,12,21,27,28,23,9,27,36,27,21,33,25,33,27,23]),
			new(15, "Ezra", "Ezra", [11,70,13,24,17,22,28,36,15,44]),
			new(16, "Neh", "Nehemiah", [11,20,32,23,19,19,73,18,38,39,36,47,31]),
			new(17, "Esth", "Esther", [22,23,15,17,14,14,10,17,32,3]),
			new(18, "Job", "Job", [22,13,26,21,27,30,21,22,35,22,20,25,28,22,35,22,16,21,29,29,34,30,17,25,6,14,23,28,25,31,40,22,33,37,16,33,24,41,30,24,34,17]),
			new(19, "Ps", "Psalms", [6,12,8,8,12,10,17,9,20,18,7,8,6,7,5,11,15,50,14,9,13,31,6,10,22,12,14,9,11,12,24,11,22,22,28,12,40,22,13,17,13,11,5,26,17,11,9,14,20,23,19,9,6,7,23,13,11,11,17,12,8,12,11,10,13,20,7,35,36,5,24,20,28,23,10,12,20,72,13,19,16,8,18,12,13,17,7,18,52,17,16,15,5,23,11,13,12,9,9,5,8,28,22,35,45,48,43,13,31,7,10,10,9,8,18,19,2,29,176,7,8,9,4,8,5,6,5,6,8,8,3,18,3,3,21,26,9,8,24,13,10,7,12,15,21,10,20,14,9,6]),
			new(20, "Prov", "Proverbs", [33,22,35,27,23,35,27,36,18,32,31,28,25,35,33,33,28,24,29,30,31,29,35,34,28,28,27,28,27,33,31]),
			new(21, "Eccl", "Ecclesiastes", [18,26,22,16,20,12,29,17,18,20,10,14]),
			new(22, "Song", "Song of Solomon", [17,17,11,16,16,13,13,14]),
			new(23, "Isa", "Isaiah", [31,22,26,6,30,13,25,22,21,34,16,6,22,32,9,14,14,7,25,6,17,25,18,23,12,21,13,29,24,33,9,20,24,17,10,22,38,22,8,31,29,25,28,28,25,13,15,22,26,11,23,15,12,17,13,12,21,14,21,22,11,12,19,12,25,24]),
			new(24, "Jer", "Jeremiah", [19,37,25,31,31,30,34,22,26,25,23,17,27,22,21,21,27,23,15,18,14,30,40,10,38,24,22,17,32,24,40,44,26,22,19,32,21,28,18,16,18,22,13,30,5,28,7,47,39,46,64,34]),
			new(25, "Lam", "Lamentations", [22,22,66,22,22]),
			new(26, "Ezek", "Ezekiel", [28,10,27,17,17,14,27,18,11,22,25,28,23,23,8,63,24,32,14,49,32,31,49,27,17,21,36,26,21,26,18,32,33,31,15,38,28,23,29,49,26,20,27,31,25,24,23,35]),
			new(27, "Dan", "Daniel", [21,49,30,37,31,28,28,27,27,21,45,13]),
			new(28, "Hos", "Hosea", [11,23,5,19,15,11,16,14,17,15,12,14,16,9]),
			new(29, "Joel", "Joel", [20,32,21]),
			new(30, "Amos", "Amos", [15,16,15,13,27,14,17,14,15]),
			new(31, "Obad", "Obadiah", [21]),
			new(32, "Jonah", "Jonah", [17,10,10,11]),
			new(33, "Mic", "Micah", [16,13,12,13,15,16,20]),
			new(34, "Nah", "Nahum", [15,13,19]),
			new(35, "Hab", "Habakkuk", [17,20,19]),
			new(36, "Zeph", "Zephaniah", [18,15,20]),
			new(37, "Hag", "Haggai", [15,23]),
			new(38, "Zech", "Zechariah", [21,13,10,14,11,15,14,23,17,12,17,14,9,21]),
			new(39, "Mal", "Malachi", [14,17,18,6]),
			new(40, "Matt", "Matthew", [25,23,17,25,48,34,29,34,38,42,30,50,58,36,39,28,27,35,30,34,46,46,39,51,46,75,66,20]),
			new(41, "Mark", "Mark", [45,28,35,41,43,56,37,38,50,52,33,44,37,72,47,20]),
			new(42, "Luke", "Luke", [80,52,38,44,39,49,50,56,62,42,54,59,35,35,32,31,37,43,48,47,38,71,56,53]),
			new(43, "John", "John", [51,25,36,54,47,71,53,59,41,42,57,50,38,31,27,33,26,40,42,31,25]),
			new(44, "Acts", "Acts", [26,47,26,37,42,15,60,40,43,48,30,25,52,28,41,40,34,28,41,38,40,30,35,27,27,32,44,31]),
			new(45, "Rom", "Romans", [32,29,31,25,21,23,25,39,33,21,36,21,14,23,33,27]),
			new(46, "1Cor", "1 Corinthians", [31,16,23,21,13,20,40,13,27,33,34,31,13,40,58,24]),
			new(47, "2Cor", "2 Corinthians", [24,17,18,18,21,18,16,24,15,18,33,21,14]),
			new(48, "Gal", "Galatians", [24,21,29,31,26,18]),
			new(49, "Eph", "Ephesians", [23,22,21,32,33,24]),
			new(50, "Phil", "Philippians", [30,30,21,23]),
			new(51, "Col", "Colossians", [29,23,25,18]),
			new(52, "1Thess", "1 Thessalonians", [10,20,13,18,28]),
			new(53, "2Thess", "2 Thessalonians", [12,17,18]),
			new(54, "1Tim", "1 Timothy", [20,15,16,16,25,21]),
			new(55, "2Tim", "2 Timothy", [18,26,17,22]),
			new(56, "Titus", "Titus", [16,15,15]),
			new(57, "Phlm", "Philemon", [25]),
			new(58, "Heb", "Hebrews", [14,18,19,16,14,20,28,13,28,39,40,29,25]),
			new(59, "Jas", "James", [27,26,18,17,20]),
			new(60, "1Pet", "1 Peter", [25,25,22,19,14]),
			new(61, "2Pet", "2 Peter", [21,22,18]),
			new(62, "1John", "1 John", [10,29,24,21,21]),
			new(63, "2John", "2 John", [13]),
			new(64, "3John", "3 John", [14]),
			new(65, "Jude", "Jude", [25]),
			new(66, "Rev", "Revelation", [20,29,22,11,14,17,17,13,21,11,19,17,18,20,8,21,18,24,21,15,27,21]),
	});
}

/// <summary>
/// The cipher of The SWORD Project for modules that are encrypted (include/sapphire.h and swcipher.cpp): Sapphire, a
/// stream cipher of Michael Paul Johnson, started from the CipherKey of the module. Every compressed block of a
/// zText module and every entry of a RawText module is decrypted by itself, from the state that the key gives.
/// </summary>
internal sealed class SwordCipher
{
	readonly byte[] masterCards = new byte[256];
	readonly byte masterRotor, masterRatchet, masterAvalanche, masterLastPlain, masterLastCipher;

	public SwordCipher(byte[] key)
	{
		byte keySize = (byte)Math.Min(key.Length, 255);
		var cards = masterCards;
		for (int i = 0; i < 256; i++) cards[i] = (byte)i;

		// Swap the card at each position with some other card, as the key tells.
		byte rsum = 0;
		int keyPos = 0;
		for (int i = 255; i >= 0; i--)
		{
			int toSwap = KeyRand(i, key, keySize, ref rsum, ref keyPos, cards);
			(cards[i], cards[toSwap]) = (cards[toSwap], cards[i]);
		}

		masterRotor = cards[1];
		masterRatchet = cards[3];
		masterAvalanche = cards[5];
		masterLastPlain = cards[7];
		masterLastCipher = cards[rsum];
	}

	static int KeyRand(int limit, byte[] key, byte keySize, ref byte rsum, ref int keyPos, byte[] cards)
	{
		if (limit == 0) return 0;
		uint mask = 1;
		while (mask < (uint)limit) mask = (mask << 1) + 1;
		int retries = 0;
		uint u;
		do
		{
			rsum = (byte)(cards[rsum] + key[keyPos++]);
			if (keyPos >= keySize)
			{
				keyPos = 0;                  // recycle the key
				rsum = (byte)(rsum + keySize);
			}
			u = mask & rsum;
			if (++retries > 11) u %= (uint)limit;
		}
		while (u > (uint)limit);
		return (int)u;
	}

	/// <summary>Decrypts the bytes in place.</summary>
	public void Decrypt(Span<byte> data)
	{
		var cards = (byte[])masterCards.Clone();
		byte rotor = masterRotor, ratchet = masterRatchet, avalanche = masterAvalanche;
		byte lastPlain = masterLastPlain, lastCipher = masterLastCipher;
		for (int i = 0; i < data.Length; i++)
		{
			byte b = data[i];
			ratchet = (byte)(ratchet + cards[rotor++]);
			byte swap = cards[lastCipher];
			cards[lastCipher] = cards[ratchet];
			cards[ratchet] = cards[lastPlain];
			cards[lastPlain] = cards[rotor];
			cards[rotor] = swap;
			avalanche = (byte)(avalanche + cards[swap]);
			lastPlain = (byte)(b ^ cards[(cards[ratchet] + cards[rotor]) & 0xFF]
				^ cards[cards[(cards[lastPlain] + cards[lastCipher] + cards[avalanche]) & 0xFF]]);
			lastCipher = b;
			data[i] = lastPlain;
		}
	}
}

/// <summary>
/// A Bible module of The SWORD Project (a .conf file in mods.d and the files of its DataPath), read directly.
/// Supported are the drivers zText, zText4, RawText and RawText4, compressed with ZIP (zlib) or LZSS, in UTF-8 or
/// Latin-1, with any versification; modules that are encrypted with a key in their .conf file are decrypted.
/// </summary>
public sealed class SwordModule
{
	public string Name { get; }
	public string Driver { get; }
	public string SourceType { get; }
	public string Description { get; }
	public SwordVersification Versification { get; }

	readonly bool compressed;  // zText: the verses are in compressed blocks
	readonly bool wide;        // zText4 and RawText4: sizes take 4 bytes instead of 2
	readonly string compression;
	readonly Encoding encoding;
	readonly SwordCipher? cipher; // the module is encrypted with the CipherKey of its .conf file
	readonly Testament?[] testaments = new Testament?[2];

	sealed class Testament
	{
		public required byte[] Index;   // the verse index: where is every verse
		public required byte[] Data;    // the text, or the compressed blocks
		public byte[]? Blocks;          // zText: where is every compressed block
		public int Block = -1;          // the block that is decompressed in BlockData
		public byte[]? BlockData;
	}

	/// <param name="confFile">The .conf file of the module, in the mods.d folder.</param>
	/// <param name="root">The folder with mods.d and modules; the DataPath of the module is relative to it.</param>
	public static SwordModule Open(string confFile, string root) => new(confFile, root);

	SwordModule(string confFile, string root)
	{
		var conf = ReadConf(confFile, out var name);
		Name = name ?? Path.GetFileNameWithoutExtension(confFile);
		Driver = conf.GetValueOrDefault("ModDrv", "");
		SourceType = conf.GetValueOrDefault("SourceType", "");
		Description = conf.GetValueOrDefault("Description", "");

		compressed = Driver.StartsWith("zText", StringComparison.OrdinalIgnoreCase);
		if (!compressed && !Driver.StartsWith("RawText", StringComparison.OrdinalIgnoreCase))
			throw new NotSupportedException($"The driver '{Driver}' of the SWORD module {Name} is not supported (supported: zText, zText4, RawText, RawText4).");
		wide = Driver.EndsWith("4");

		// A module that is encrypted has its key in the .conf file when it may be used freely (CrossWire has the key of such
		// a module as the CipherKey, often with the name of the module); a locked module has it empty, the key is then a
		// secret that has to be bought.
		if (conf.TryGetValue("CipherKey", out var key))
		{
			if (string.IsNullOrWhiteSpace(key))
				throw new NotSupportedException($"The SWORD module {Name} is locked: its .conf file has no CipherKey.");
			cipher = new SwordCipher(Encoding.Latin1.GetBytes(key.Trim()));
		}

		compression = conf.GetValueOrDefault("CompressType", "LZSS").Trim().ToUpperInvariant(); // LZSS is the default of SWORD
		if (compressed && compression != "ZIP" && compression != "LZSS")
			throw new NotSupportedException($"The compression '{compression}' of the SWORD module {Name} is not supported (supported: ZIP, LZSS).");

		var encoding = conf.GetValueOrDefault("Encoding", "Latin-1").Trim();
		this.encoding = encoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase) ? Encoding.UTF8
			: encoding.Equals("Latin-1", StringComparison.OrdinalIgnoreCase) ? Encoding.Latin1
			: throw new NotSupportedException($"The encoding '{encoding}' of the SWORD module {Name} is not supported (supported: UTF-8, Latin-1).");

		var dataPath = conf.GetValueOrDefault("DataPath", "").Trim().Replace('\\', '/');
		while (dataPath.StartsWith("./")) dataPath = dataPath[2..];
		var dir = Path.GetFullPath(Path.Combine(root, dataPath));
		if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"The data of the SWORD module {Name} was not found: {dir}");

		for (int testament = 0; testament < 2; testament++) testaments[testament] = Load(dir, testament == 0 ? "ot" : "nt");

		Versification = ResolveVersification(conf.GetValueOrDefault("Versification"));
	}

	int EntrySize => compressed ? (wide ? 12 : 10) : (wide ? 8 : 6);

	/// <summary>The number of entries in the verse index of a testament.</summary>
	int Entries(int testament) => testaments[testament] == null ? 0 : testaments[testament]!.Index.Length / EntrySize;

	/// <summary>
	/// The structure of the module: the table of the versification if it is a known one and fits the module, else the
	/// structure that the module itself shows. The module is never read with a structure that does not fit the size of its index.
	/// </summary>
	SwordVersification ResolveVersification(string? name)
	{
		var known = SwordVersification.Find(name);
		string? mismatch = null;
		if (known != null)
		{
			for (int testament = 0; testament < 2 && mismatch == null; testament++)
			{
				// A testament that the versification has no books in (Leningrad and MT have the Old Testament only) is
				// not checked: the module may still have an index file for it, with a heading or nothing in it.
				if (testaments[testament] == null || !known.Books.Any(b => b.NewTestament == (testament == 1))) continue;
				if (Entries(testament) != known.Entries(testament == 1))
					mismatch = $"The verse index of the {(testament == 0 ? "Old" : "New")} Testament of the SWORD module {Name} " +
						$"has {Entries(testament)} entries, but the versification {known.Name} has {known.Entries(testament == 1)}.";
			}
			if (mismatch == null) return known;
		}

		// A module that does not fit the table of its versification (some modules differ in a few verses from the table
		// of SWORD) or has a versification that is not known is read with the structure that it shows itself.
		return Derive() ?? throw (known != null
			? new InvalidDataException(mismatch + " Its structure cannot be derived from the module either, because it does not mark its books and chapters (as OSIS modules made with osis2mod do).")
			: new NotSupportedException($"The versification '{name}' of the SWORD module {Name} is not known (known: {SwordVersification.Supported}), " +
				"and its structure cannot be derived from the module, because it does not mark its books and chapters (as OSIS modules made with osis2mod do)."));
	}

	// The headings of a book and of a chapter are entries of the index of their own. Modules made with osis2mod
	// have the start of the book and of the chapter in them, which tells where the books and the chapters begin;
	// every other entry is a verse, numbered in order. The ends of the elements (eID) are in the last verse.
	static readonly Regex BookStartRx = new(@"<div\b(?![^>]*\beID\s*=)(?=[^>]*\btype\s*=\s*""book"")[^>]*\bosisID\s*=\s*""(?<id>[^""]+)""", RegexOptions.Compiled);
	static readonly Regex ChapterStartRx = new(@"<chapter\b(?![^>]*\beID\s*=)[^>]*\bosisID\s*=\s*""(?<book>[^"".]+)\.(?<n>[0-9]+)""", RegexOptions.Compiled);

	/// <summary>The structure from the markers in the module, or null if they are missing or not consistent.</summary>
	SwordVersification? Derive()
	{
		var books = new List<SwordVersification.Book>();
		var ids = new HashSet<string>();
		int extra = 66; // the number of a book that is not one of the 66 of the canon

		for (int testament = 0; testament < 2; testament++)
		{
			if (testaments[testament] == null) continue;
			string? id = null;
			var verses = new List<int>(); // the verses of every chapter of the book

			void Finish()
			{
				if (id == null) return;
				var known = SwordVersification.KnownBook(id);
				int number = known is { Number: > 0 } ? known.Value.Number : ++extra;
				books.Add(new SwordVersification.Book(number, id, known?.Name ?? id, verses.ToArray(), testament == 1));
			}

			int entries = Entries(testament), found = books.Count;
			for (int i = 2; i < entries; i++) // 0 is the heading of the module, 1 the one of the testament
			{
				var text = Read(testament == 1, i);

				var book = BookStartRx.Match(text);
				if (book.Success)
				{
					Finish();
					id = book.Groups["id"].Value;
					verses = new List<int>();
					if (!ids.Add(id)) return null;
					continue;
				}

				var chapter = ChapterStartRx.Match(text);
				if (chapter.Success)
				{
					if (id == null || chapter.Groups["book"].Value != id || int.Parse(chapter.Groups["n"].Value) != verses.Count + 1) return null;
					verses.Add(0);
					continue;
				}

				if (verses.Count == 0) return null; // an entry that is in no chapter: the headings are not marked
				verses[^1]++;
			}
			Finish();
			if (entries > 2 && books.Count == found) return null; // a testament without a single book
		}
		return books.Count == 0 ? null : SwordVersification.Derived(books);
	}

	/// <summary>Whether the module has the Old (false) or the New (true) Testament.</summary>
	public bool Has(bool newTestament) => testaments[newTestament ? 1 : 0] != null;

	internal static Dictionary<string, string> ReadConf(string file, out string? name)
	{
		var conf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		name = null;
		var lines = File.ReadAllLines(file, Encoding.UTF8);
		for (int i = 0; i < lines.Length; i++)
		{
			var line = lines[i].Trim();
			if (line.Length == 0 || line.StartsWith('#')) continue;
			if (name == null && line.StartsWith('[') && line.EndsWith(']')) { name = line[1..^1]; continue; }

			var match = Regex.Match(line, @"^([A-Za-z0-9_.\-]+)\s*=\s*(.*)$");
			if (!match.Success) continue;
			var value = match.Groups[2].Value;
			while (value.EndsWith('\\') && i + 1 < lines.Length) // a value goes on in the next line
				value = value[..^1] + "\n" + lines[++i].Trim();
			conf[match.Groups[1].Value] = value;
		}
		return conf;
	}

	Testament? Load(string dir, string testament)
	{
		if (compressed)
		{
			// The files are named ot.bzv, ot.czv or ot.vzv, after the size of the blocks: book, chapter or verse.
			var index = Directory.EnumerateFiles(dir, $"{testament}.?zv").FirstOrDefault();
			if (index == null) return null;
			var kind = Path.GetFileName(index)[3];
			var blocks = Path.Combine(dir, $"{testament}.{kind}zs");
			var data = Path.Combine(dir, $"{testament}.{kind}zz");
			if (!File.Exists(blocks) || !File.Exists(data))
				throw new FileNotFoundException($"The files {testament}.{kind}zs and {testament}.{kind}zz of the SWORD module {Name} are missing.");
			return new Testament { Index = File.ReadAllBytes(index), Blocks = File.ReadAllBytes(blocks), Data = File.ReadAllBytes(data) };
		}

		var vss = Path.Combine(dir, testament + ".vss");
		var text = Path.Combine(dir, testament);
		if (!File.Exists(vss) || !File.Exists(text)) return null;
		return new Testament { Index = File.ReadAllBytes(vss), Data = File.ReadAllBytes(text) };
	}

	/// <summary>The raw text of an entry of the verse index (see <see cref="SwordVersification.Index"/>), in the markup of the module.</summary>
	public string Read(bool newTestament, int index)
	{
		var t = testaments[newTestament ? 1 : 0];
		if (t == null) return "";
		var bytes = compressed ? ReadCompressed(t, index) : ReadRaw(t, index);
		return bytes == null || bytes.Length == 0 ? "" : encoding.GetString(bytes).Trim('\0', '\r', '\n', ' ');
	}

	byte[]? ReadRaw(Testament t, int index)
	{
		// RawText: the index has an offset and a size for every entry, the text is in one file
		int entry = wide ? 8 : 6, pos = index * entry;
		if (index < 0 || pos + entry > t.Index.Length) return null;
		long offset = BinaryPrimitives.ReadUInt32LittleEndian(t.Index.AsSpan(pos));
		int size = wide ? (int)BinaryPrimitives.ReadUInt32LittleEndian(t.Index.AsSpan(pos + 4)) : BinaryPrimitives.ReadUInt16LittleEndian(t.Index.AsSpan(pos + 4));
		if (size == 0 || offset + size > t.Data.Length) return null;
		var entryBytes = t.Data.AsSpan((int)offset, size).ToArray();
		cipher?.Decrypt(entryBytes); // every entry is encrypted on its own
		return entryBytes;
	}

	byte[]? ReadCompressed(Testament t, int index)
	{
		// zText: the index has a block number, a position in the uncompressed block and a size for every entry
		int entry = wide ? 12 : 10, pos = index * entry;
		if (index < 0 || pos + entry > t.Index.Length) return null;
		uint block = BinaryPrimitives.ReadUInt32LittleEndian(t.Index.AsSpan(pos));
		long start = BinaryPrimitives.ReadUInt32LittleEndian(t.Index.AsSpan(pos + 4));
		int size = wide ? (int)BinaryPrimitives.ReadUInt32LittleEndian(t.Index.AsSpan(pos + 8)) : BinaryPrimitives.ReadUInt16LittleEndian(t.Index.AsSpan(pos + 8));
		if (size == 0) return null;

		if (t.Block != block)
		{
			// Neighbouring verses are in the same block, which is kept decompressed.
			long blockPos = (long)block * 12;
			if (blockPos + 12 > t.Blocks!.Length) return null;
			long offset = BinaryPrimitives.ReadUInt32LittleEndian(t.Blocks.AsSpan((int)blockPos));
			int compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(t.Blocks.AsSpan((int)blockPos + 4));
			int uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(t.Blocks.AsSpan((int)blockPos + 8));
			if (offset + compressedSize > t.Data.Length)
				throw new InvalidDataException($"A block of the SWORD module {Name} is beyond the end of its data.");
			var compressedBytes = t.Data.AsSpan((int)offset, compressedSize).ToArray();
			cipher?.Decrypt(compressedBytes); // the blocks are encrypted after they were compressed, each one on its own
			t.BlockData = Decompress(compressedBytes, uncompressedSize);
			t.Block = (int)block;
		}

		if (start + size > t.BlockData!.Length) return null;
		return t.BlockData.AsSpan((int)start, size).ToArray();
	}

	byte[] Decompress(ReadOnlySpan<byte> data, int size) => compression == "ZIP" ? Zip(data) : Lzss(data, size);

	internal static byte[] Zip(ReadOnlySpan<byte> data)
	{
		using var input = new MemoryStream(data.ToArray());
		using var zlib = new ZLibStream(input, CompressionMode.Decompress);
		using var output = new MemoryStream();
		zlib.CopyTo(output);
		return output.ToArray();
	}

	/// <summary>The LZSS compression of SWORD: a ring buffer of 4096 bytes, matches of 3 to 18 bytes.</summary>
	static byte[] Lzss(ReadOnlySpan<byte> input, int size)
	{
		const int N = 4096, F = 18, Threshold = 2;
		var ring = new byte[N];
		Array.Fill(ring, (byte)' ', 0, N - F);
		int r = N - F, flags = 0, ip = 0, op = 0;
		var output = new byte[size];
		while (op < size && ip < input.Length)
		{
			flags >>= 1;
			if ((flags & 256) == 0)
			{
				flags = input[ip++] | 0xff00;
				if (ip >= input.Length) break;
			}
			if ((flags & 1) != 0)
			{
				byte c = input[ip++];
				output[op++] = c;
				ring[r++] = c; r &= N - 1;
			}
			else
			{
				if (ip + 1 >= input.Length) break;
				int i = input[ip++], j = input[ip++];
				i |= (j & 0xf0) << 4;
				j = (j & 0x0f) + Threshold;
				for (int k = 0; k <= j && op < size; k++)
				{
					byte c = ring[(i + k) & (N - 1)];
					output[op++] = c;
					ring[r++] = c; r &= N - 1;
				}
			}
		}
		return output;
	}
}

/// <summary>A verse of a SWORD module converted to Bible Markdown.</summary>
public sealed class SwordVerse
{
	/// <summary>The headings that start in this verse.</summary>
	public List<string> Titles { get; } = new();
	/// <summary>The title of a psalm, which is the verse 0 of its chapter.</summary>
	public string? Psalm { get; set; }
	/// <summary>A paragraph starts with this verse.</summary>
	public bool Paragraph { get; set; }
	/// <summary>The words of the verse; the footnotes are only a marker ^label^ in it.</summary>
	public string Text { get; set; } = "";
	/// <summary>The footnotes of the verse, to be written as ^label^[text].</summary>
	public List<(string Label, string Text)> Notes { get; } = new();
}

/// <summary>Converts the text of a verse in the markup of a SWORD module (OSIS, GBF, ThML) to Bible Markdown.</summary>
public static class SwordMarkup
{
	/// <param name="raw">The text of the verse as it is in the module.</param>
	/// <param name="sourceType">The SourceType of the module: OSIS, ThML, GBF, or Plain (no markup).</param>
	/// <param name="nextLabel">Gives the label for the next footnote of the chapter.</param>
	/// <param name="strongs">Whether Strong numbers and morphology of the words are kept, like the import of USFM does.</param>
	public static SwordVerse Convert(string raw, string sourceType, Func<string> nextLabel, bool strongs)
	{
		raw = raw.Replace("\r", "");
		sourceType = sourceType.Trim();
		if (sourceType.Equals("Plain", StringComparison.OrdinalIgnoreCase))
			return new SwordVerse { Text = Clean(Escape(WebUtility.HtmlDecode(raw))) };
		if (sourceType.Equals("GBF", StringComparison.OrdinalIgnoreCase)) raw = FromGbf(raw);
		else if (sourceType.Equals("ThML", StringComparison.OrdinalIgnoreCase)) raw = FromThml(raw);
		return FromOsis(raw, nextLabel, strongs);
	}

	// GBF and ThML are turned into the OSIS elements that FromOsis understands.

	static string FromGbf(string text)
	{
		text = text.Replace("<FI>", "<hi type=\"italic\">").Replace("<Fi>", "</hi>")
			.Replace("<FO>", "<hi type=\"italic\">").Replace("<Fo>", "</hi>")
			.Replace("<FB>", "<hi type=\"bold\">").Replace("<Fb>", "</hi>")
			.Replace("<FR>", "<q who=\"Jesus\" marker=\"\">").Replace("<Fr>", "</q>")
			.Replace("<RF>", "<note>").Replace("<Rf>", "</note>")
			.Replace("<RX>", "<note>").Replace("<Rx>", "</note>")
			.Replace("<TS>", "<title>").Replace("<Ts>", "</title>")
			.Replace("<CM>", "<p/>").Replace("<CL>", "<lb/>");
		return Regex.Replace(text, @"<(?:W[HGT][^>]*|[A-Z][a-zA-Z]\d*)>", ""); // Strong numbers and the other tags
	}

	static string FromThml(string text)
	{
		text = Regex.Replace(text, @"<scripRef[^>]*>(.*?)</scripRef>", "$1", RegexOptions.Singleline);
		text = Regex.Replace(text, @"<div\s+class=""(?:sechead|title|heading)[^""]*""[^>]*>(.*?)</div>", "<title>$1</title>", RegexOptions.Singleline);
		text = Regex.Replace(text, @"<(?:sync|verse)\b[^>]*/>", "");
		text = Regex.Replace(text, @"<br\s*/?>", "<lb/>");
		text = Regex.Replace(text, @"<(i|em)>", "<hi type=\"italic\">").Replace("</i>", "</hi>").Replace("</em>", "</hi>");
		text = Regex.Replace(text, @"<(b|strong)>", "<hi type=\"bold\">").Replace("</b>", "</hi>").Replace("</strong>", "</hi>");
		text = Regex.Replace(text, @"<sc>", "<hi type=\"small-caps\">").Replace("</sc>", "</hi>");
		return text.Replace("<added>", "<transChange type=\"added\">").Replace("</added>", "</transChange>");
	}

	static readonly Regex TagRx = new(@"<(?<close>/)?(?<name>[A-Za-z][\w:.-]*)(?<attrs>(?:[^>""']|""[^""]*""|'[^']*')*?)(?<self>/)?>", RegexOptions.Compiled);
	static readonly Regex AttrRx = new(@"(?<key>[\w:.-]+)\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)')", RegexOptions.Compiled);

	sealed class Frame
	{
		public required string Name;
		public required StringBuilder Outer;               // where the content goes when the element is closed
		public Dictionary<string, string> Attrs = new();
		public string Mark = "";                           // q: the quotation mark that was opened
	}

	static SwordVerse FromOsis(string raw, Func<string> nextLabel, bool strongs)
	{
		var verse = new SwordVerse();
		var sb = new StringBuilder();
		var frames = new Stack<Frame>();
		var quotes = new Stack<string>();                  // the quotation marks that are open
		var jesus = new Dictionary<string, (StringBuilder Builder, int Start)>(); // q milestones with the words of Jesus
		bool lineBreak = false, paragraph = false;         // to be written before the next text

		void Raw(string markdown)
		{
			if (markdown.Length == 0) return;
			if (!HasText(markdown))
			{
				if (!lineBreak && !paragraph && sb.Length > 0) sb.Append(' ');
				return;
			}
			if (HasText(sb))
			{
				if (paragraph) sb.Append("\n\n");
				else if (lineBreak) sb.Append("\\\n");
			}
			paragraph = lineBreak = false;
			sb.Append(markdown);
		}

		void Paragraph()
		{
			if (frames.Count > 0) return;
			if (HasText(sb)) paragraph = true;
			else verse.Paragraph = true;
		}

		void Open(string name, Dictionary<string, string> attrs, string mark = "")
		{
			frames.Push(new Frame { Name = name, Outer = sb, Attrs = attrs, Mark = mark });
			sb = new StringBuilder();
		}

		void Close(string name)
		{
			if (!frames.Any(f => f.Name == name)) return;
			while (frames.Peek().Name != name) // elements that were not closed
			{
				var open = frames.Pop();
				var content = sb.ToString();
				sb = open.Outer;
				sb.Append(content);
			}
			var frame = frames.Pop();
			var inner = sb.ToString();
			sb = frame.Outer;
			Finish(frame, inner);
		}

		void Finish(Frame frame, string inner)
		{
			var type = frame.Attrs.GetValueOrDefault("type", "").ToLowerInvariant();
			switch (frame.Name)
			{
				case "note":
				{
					var note = Clean(inner).Replace("\\[", "(").Replace("\\]", ")");
					if (note.Length > 0)
					{
						var label = nextLabel();
						sb.Append('^').Append(label).Append('^');
						verse.Notes.Add((label, note));
					}
					break;
				}
				case "title":
				{
					var title = Clean(inner);
					if (title.Length == 0) break;
					if (type == "psalm") verse.Psalm = verse.Psalm == null ? title : verse.Psalm + " " + title;
					else if (type != "parallel" && type != "x-parallel") verse.Titles.Add(title);
					break;
				}
				case "q":
				{
					var close = CloseMark(frame.Mark);
					var quote = frame.Mark + inner + close;
					Raw(frame.Attrs.GetValueOrDefault("who") == "Jesus" ? "[" + quote + "]{.wj}" : quote);
					break;
				}
				case "w":
					Raw(strongs ? WithStrongs(inner, frame.Attrs) : inner);
					break;
				case "hi":
					Raw(type switch
					{
						"italic" or "emphasis" => Wrap(inner, "*", "*"),
						"bold" => Wrap(inner, "**", "**"),
						"small-caps" or "sc" => Wrap(inner, "[", "]{.smallcaps}"),
						_ => inner,
					});
					break;
				case "transchange":
					Raw(type == "deleted" ? "" : type == "added" || type == "" ? Wrap(inner, "*", "*") : inner);
					break;
				case "divinename":
					Raw(inner.ToUpperInvariant());
					break;
				default:
					Raw(inner);
					break;
			}
		}

		int pos = 0;
		foreach (Match m in TagRx.Matches(raw))
		{
			var before = raw[pos..m.Index];
			pos = m.Index + m.Length;
			if (before.Length > 0) Raw(Escape(WebUtility.HtmlDecode(Regex.Replace(before, @"\s+", " "))));

			var name = m.Groups["name"].Value.ToLowerInvariant();
			bool close = m.Groups["close"].Success, self = m.Groups["self"].Success;
			var attrs = ParseAttrs(m.Groups["attrs"].Value);
			bool milestone = attrs.ContainsKey("sID") || attrs.ContainsKey("eID");

			switch (name)
			{
				case "note": case "title": case "hi": case "transchange": case "divinename":
					if (close) Close(name);
					else if (!self) Open(name, attrs);
					break;
				case "w":
					if (strongs)
					{
						if (close) Close(name);
						else if (!self) Open(name, attrs);
					}
					break;
				case "q" when milestone:
				{
					if (attrs.TryGetValue("sID", out var start))
					{
						var mark = attrs.TryGetValue("marker", out var marker) ? marker : QuoteMark(quotes.Count + 1);
						quotes.Push(mark);
						int at = sb.Length;
						Raw(mark);
						if (attrs.GetValueOrDefault("who") == "Jesus") jesus[start] = (sb, at);
					}
					else
					{
						var opened = quotes.Count > 0 ? quotes.Pop() : "";
						Raw(attrs.TryGetValue("marker", out var marker) ? marker : CloseMark(opened));
						if (jesus.Remove(attrs["eID"], out var j) && ReferenceEquals(j.Builder, sb) && j.Start <= sb.Length)
						{
							sb.Insert(j.Start, "[");
							sb.Append("]{.wj}");
						}
					}
					break;
				}
				case "q":
					if (close)
					{
						Close(name);
						if (quotes.Count > 0) quotes.Pop();
					}
					else if (!self)
					{
						var mark = attrs.TryGetValue("marker", out var marker) ? marker : QuoteMark(quotes.Count + 1);
						quotes.Push(mark);
						Open(name, attrs, mark);
					}
					break;
				case "l":
					if (close || (self && attrs.ContainsKey("eID"))) lineBreak = true;
					break;
				case "lb":
					lineBreak = true;
					break;
				case "p":
					if (!close) Paragraph();
					break;
				case "div":
					if (!close && attrs.GetValueOrDefault("type") == "paragraph") Paragraph();
					break;
				case "milestone":
					if (attrs.GetValueOrDefault("type") == "x-p" || attrs.GetValueOrDefault("marker", "").Contains('¶')) Paragraph();
					break;
				// reference, name, foreign, seg, catchWord, ... : only their text counts
			}
		}
		var rest = raw[pos..];
		if (rest.Length > 0) Raw(Escape(WebUtility.HtmlDecode(Regex.Replace(rest, @"\s+", " "))));

		while (frames.Count > 0) // elements that were not closed
		{
			var open = frames.Pop();
			var content = sb.ToString();
			sb = open.Outer;
			sb.Append(content);
		}

		verse.Text = Clean(sb.ToString());
		return verse;
	}

	static Dictionary<string, string> ParseAttrs(string attrs)
	{
		var result = new Dictionary<string, string>();
		foreach (Match m in AttrRx.Matches(attrs)) result[m.Groups["key"].Value] = WebUtility.HtmlDecode(m.Groups["v"].Value);
		return result;
	}

	/// <summary>The words with their Strong numbers and morphology as the import of USFM writes them: word_&amp;lemma_H1234_%morph.</summary>
	static string WithStrongs(string word, Dictionary<string, string> attrs)
	{
		var tokens = new List<string>(word.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		var lemma = attrs.GetValueOrDefault("lemma", "");
		if (lemma.Length == 0 && !attrs.ContainsKey("morph")) return word;
		foreach (var entry in lemma.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			var colon = entry.IndexOf(':');
			var kind = colon < 0 ? "strong" : entry[..colon].ToLowerInvariant();
			var value = colon < 0 ? entry : entry[(colon + 1)..];
			if (kind == "lemma") tokens.Add("&" + value);
			else if (kind == "strong") tokens.Add(value);
		}
		foreach (var entry in attrs.GetValueOrDefault("morph", "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
			tokens.Add("%" + entry[(entry.IndexOf(':') + 1)..]);
		return string.Join('_', tokens);
	}

	static string QuoteMark(int depth) => depth % 2 == 1 ? "“" : "‘";

	static string CloseMark(string open) => open switch
	{
		"“" => "”", "‘" => "’", "«" => "»", "‹" => "›", "„" => "“", "‚" => "‘", _ => open,
	};

	/// <summary>Puts markers around the text; spaces at its ends stay outside, as Markdown needs it.</summary>
	static string Wrap(string text, string left, string right)
	{
		var trimmed = text.Trim();
		if (trimmed.Length == 0) return text;
		var start = text.Length - text.TrimStart().Length;
		return text[..start] + left + trimmed + right + text[text.TrimEnd().Length..];
	}

	static bool HasText(string s) => s.Any(c => !char.IsWhiteSpace(c));
	static bool HasText(StringBuilder s)
	{
		for (int i = 0; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) return true;
		return false;
	}

	/// <summary>Escapes what has a meaning in Markdown.</summary>
	static string Escape(string text) => text.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]").Replace("*", "\\*").Replace("^", "\\^");

	/// <summary>Collapses the spaces and trims the text.</summary>
	static string Clean(string text) => Regex.Replace(Regex.Replace(text, @"[ \t]+", " "), @" ?\n ?", "\n").Trim();
}
