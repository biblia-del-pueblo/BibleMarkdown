using LiteDB;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BibleMarkdown;

/// <summary>
/// Where a word is: its book (the number the file of the book starts with: 01-Genesis.md is 1), chapter and verse, and
/// its position in the verse (0 is the first word, 1 the second, ...).
/// </summary>
public readonly record struct WordPlace(int Book, int Chapter, int Verse, int Position)
{
    // The places of a word are stored as bytes, in the order of the Bible, every one relative to the one before: a
    // number that is the same as before is 0, and a number is written with 7 bits to a byte (the 8th bit says that
    // more follow). A place is mostly 2 or 3 bytes. What comes after a number that changed starts again from nothing:
    // a new chapter starts at its verse number, a new verse at its position, as there is nothing to compare with.

    /// <summary>The places as bytes, see <see cref="Decode"/>. They are sorted first, by their place in the Bible.</summary>
    public static byte[] Encode(IEnumerable<WordPlace> places)
    {
        var bytes = new List<byte>();
        WordPlace last = default;
        foreach (var place in places.Order(Comparer<WordPlace>.Create((a, b) =>
            (a.Book, a.Chapter, a.Verse, a.Position).CompareTo((b.Book, b.Chapter, b.Verse, b.Position)))))
        {
            bool newBook = place.Book != last.Book, newChapter = newBook || place.Chapter != last.Chapter,
                newVerse = newChapter || place.Verse != last.Verse;
            Write(bytes, place.Book - last.Book);
            Write(bytes, newBook ? place.Chapter : place.Chapter - last.Chapter);
            Write(bytes, newChapter ? place.Verse : place.Verse - last.Verse);
            Write(bytes, newVerse ? place.Position : place.Position - last.Position);
            last = place;
        }
        return bytes.ToArray();
    }

    /// <summary>The places that <see cref="Encode"/> made bytes of, in the order of the Bible.</summary>
    public static IEnumerable<WordPlace> Decode(byte[] bytes)
    {
        int at = 0;
        WordPlace last = default;
        while (at < bytes.Length)
        {
            int book = last.Book + Read(bytes, ref at);
            bool newBook = book != last.Book;
            int chapter = Read(bytes, ref at);
            if (!newBook) chapter += last.Chapter;
            bool newChapter = newBook || chapter != last.Chapter;
            int verse = Read(bytes, ref at);
            if (!newChapter) verse += last.Verse;
            bool newVerse = newChapter || verse != last.Verse;
            int position = Read(bytes, ref at);
            if (!newVerse) position += last.Position;
            last = new WordPlace(book, chapter, verse, position);
            yield return last;
        }
    }

    static void Write(List<byte> bytes, int number)
    {
        uint value = (uint)number; // never negative: the places are sorted
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
    }

    static int Read(byte[] bytes, ref int at)
    {
        uint value = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte b = bytes[at++];
            value |= (uint)(b & 0x7F) << shift;
            if (b < 0x80) return (int)value;
        }
    }
}

/// <summary>A word of a Bible, with all the places where it is.</summary>
public sealed class WordEntry
{
    /// <summary>The word as <see cref="WordIndex.Normalize"/> makes it: in lower case and without accents. It is the id of the entry.</summary>
    [BsonId]
    public string Word { get; set; } = "";

    /// <summary>The places of the word (book, chapter, verse and position in the verse) as bytes; <see cref="WordPlace.Decode"/> reads them.</summary>
    public byte[] Places { get; set; } = Array.Empty<byte>();
}

/// <summary>A verse that has some of the searched words, with its match score (higher is better).</summary>
public sealed record WordHit(int Book, int Chapter, int Verse, double Score);

/// <summary>
/// The index of all the words of a Bible, in a LiteDB database (collection <c>wordbytes</c>): one entry for every
/// different word, with the places of the word (book, chapter, verse and position in the verse) as a byte array. The word
/// is the id of the entry, so it is found at once. Made by <see cref="Importer.CreateIndex"/> from the markdown books,
/// and searched with <see cref="Search"/>.
/// </summary>
public static class WordIndex
{
    public const string FileName = "index.db";
    const string Collection = "wordbytes"; // "words" and "wordplaces" were older formats: their files are made again

    // The verses are found like the reader does it (see Bible.ParseBook): chapters are "# 1", verses start with "@1".
    static readonly Regex Chapters = new(@"(^|\n)#\s+(?<chno>[0-9]+)\s*\r?\n(?<chbody>.*?)(?=\n#\s+[0-9]+\s*\r?\n|$)", RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex VerseRx = new(@"@(?<verseno>[0-9]+)(?=\s)(?<versebody>.*?)(?=@[0-9]+\s+|\r?\n#|$)", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>The verses of a book in Bible Markdown: chapter, verse and the markdown of the verse (without its number).</summary>
    public static IEnumerable<(int Chapter, int Verse, string Text)> Verses(string markdown)
    {
        markdown = Regex.Replace(markdown, @"/\*.*?\*/", "", RegexOptions.Singleline); // comments
        markdown = Regex.Replace(markdown, @"(?<!:)//.*?\r?\n", "", RegexOptions.Singleline); // single line comments
        foreach (Match chapter in Chapters.Matches(markdown))
        {
            int chapterNumber = int.Parse(chapter.Groups["chno"].Value);
            foreach (Match verse in VerseRx.Matches(chapter.Groups["chbody"].Value))
                yield return (chapterNumber, int.Parse(verse.Groups["verseno"].Value), verse.Groups["versebody"].Value.Trim());
        }
    }

    // ---- Words ---------------------------------------------------------------------------------------

    // What is in the markdown of a verse that is not a word: footnotes and their markers, the "{.wj}" of a style, the
    // address of a link. A word with Strong's attributes ("beginning_H7225_&lemma") is one token with its attributes.
    const string Balanced = @"(?>\[(?<c>)|[^\[\]]+|\](?<-c>))*(?(c)(?!))";
    static readonly Regex TokenRx = new(
        @"\^\[" + Balanced + @"\]" +                                   // a footnote: not part of the verse
        @"|\^[a-zA-Z]+\^" +                                            // the marker of a footnote
        @"|\{\.[a-zA-Z]+\}" +                                          // {.wj}, {.smallcaps}
        @"|(?<=\])\([a-zA-Z]+://[^)\s]*\)" +                           // the address of a link
        @"|(?<token>[\p{L}\p{M}\p{Nd}]+(?:_[%&\p{Nd}\p{L}\p{M}-]+)*)", // a word, or a word with attributes
        RegexOptions.Compiled);

    static readonly Regex StrongsAttribute = new(@"^(?:[GH][0-9]+|&.+|%[0-9a-zA-Z-]+)$", RegexOptions.Compiled);

    /// <summary>A word of a text and where it is: for a word with Strong's attributes the whole token (a phrase has several words).</summary>
    public readonly record struct Token(int Start, int Length, string[] Words);

    /// <summary>The words of a text in markdown, in order. A word with Strong's attributes is one token, so it can be made bold as a whole.</summary>
    public static List<Token> Tokenize(string markdown)
    {
        var tokens = new List<Token>();
        foreach (Match m in TokenRx.Matches(markdown))
        {
            var token = m.Groups["token"];
            if (!token.Success) continue;
            var words = token.Value.Split('_')
                .Where((part, i) => i == 0 || !StrongsAttribute.IsMatch(part)) // the first part is always a word
                .SelectMany(part => Regex.Matches(part, @"[\p{L}\p{M}\p{Nd}]+").Select(w => Normalize(w.Value)))
                .Where(w => w.Length > 0)
                .ToArray();
            if (words.Length > 0) tokens.Add(new Token(token.Index, token.Length, words));
        }
        return tokens;
    }

    /// <summary>A word as it is indexed and searched: in lower case, without accents or vowel points (Greek, Hebrew), the final sigma as sigma.</summary>
    public static string Normalize(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var ch in word.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch == 'ς' || ch == 'Σ' ? 'σ' : char.ToLowerInvariant(ch));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The markdown of a verse without its footnotes and their markers (they are not part of the verse): <c>^[note]</c>,
    /// <c>^a^[note]</c> (a marker followed by its note) and a lone marker <c>^a^</c> that starts the text of a note.
    /// </summary>
    public static string WithoutFootnotes(string markdown) =>
        Regex.Replace(markdown, @"\^(?:[a-zA-Z]+\^)?\[" + Balanced + @"\]|\^[a-zA-Z]+\^", "");

    /// <summary>The different words of a search phrase, in the order they first appear, as they are looked up.</summary>
    public static List<string> Terms(string phrase) =>
        Tokenize(phrase).SelectMany(t => t.Words).Distinct().ToList();

    // ---- Creating the index ------------------------------------------------------------------------------

    /// <summary>
    /// Creates the index of the markdown books (the files such as 01-Genesis.md) in <paramref name="mdpath"/> as the
    /// database <see cref="FileName"/> in the same folder; an existing one is replaced.
    /// </summary>
    /// <returns>The number of words in the index.</returns>
    public static int Create(string mdpath)
    {
        var file = Path.Combine(mdpath, FileName);
        if (File.Exists(file)) File.Delete(file);

        using var database = new LiteDatabase(new ConnectionString { Filename = file, Connection = ConnectionType.Direct });
        var words = database.GetCollection<WordEntry>(Collection);
        int count = 0;

        // The places of every word are collected over all the books, then written: one entry for every word.
        var places = new Dictionary<string, List<WordPlace>>(StringComparer.Ordinal);

        var books = Directory.EnumerateFiles(mdpath, "*.md")
            .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^[0-9]+[0-9.]*-"))
            .Order(StringComparer.Ordinal);
        foreach (var bookFile in books)
        {
            int book = int.Parse(Regex.Match(Path.GetFileName(bookFile), "^[0-9]+").Value);
            foreach (var (chapter, verse, text) in Verses(File.ReadAllText(bookFile)))
            {
                int position = 0;
                foreach (var word in Tokenize(text).SelectMany(t => t.Words))
                {
                    if (!places.TryGetValue(word, out var list)) places[word] = list = new List<WordPlace>();
                    list.Add(new WordPlace(book, chapter, verse, position++));
                    count++;
                }
            }
        }

        words.InsertBulk(places.Select(p => new WordEntry { Word = p.Key, Places = WordPlace.Encode(p.Value) }), 2000);
        return count;
    }

    /// <summary>Whether the file is an index in the format that <see cref="Search"/> reads (an index of an older format is made again).</summary>
    public static bool IsCurrent(string indexFile)
    {
        if (!File.Exists(indexFile)) return false;
        try
        {
            using var database = new LiteDatabase(new ConnectionString { Filename = indexFile, Connection = ConnectionType.Direct, ReadOnly = true });
            return database.CollectionExists(Collection);
        }
        catch (Exception ex) when (ex is LiteException or IOException) { return false; }
    }

    // ---- Searching ---------------------------------------------------------------------------------------

    /// <summary>
    /// The verses that have some of the <paramref name="terms"/>, best match first. The score of a verse is the number
    /// of different searched words in it, and half a point more for every two searched words that stand next to each
    /// other in the order of the phrase. Verses with the same score are in the order of the Bible.
    /// </summary>
    /// <param name="limit">The most verses to return (0 for all).</param>
    public static List<WordHit> Search(string indexFile, IReadOnlyList<string> terms, int limit = 100)
    {
        if (terms.Count == 0 || !File.Exists(indexFile)) return new List<WordHit>();

        using var database = new LiteDatabase(new ConnectionString { Filename = indexFile, Connection = ConnectionType.Direct, ReadOnly = true });
        var words = database.GetCollection<WordEntry>(Collection);

        // for every verse: where the searched words are in it (the index of the word in the phrase and its place in the verse)
        var verses = new Dictionary<(int Book, int Chapter, int Verse), List<(int Term, int Position)>>();
        for (int term = 0; term < terms.Count; term++)
        {
            if (words.FindById(terms[term]) is not { } entry) continue; // the word is not in the Bible
            foreach (var place in WordPlace.Decode(entry.Places))
            {
                var key = (place.Book, place.Chapter, place.Verse);
                if (!verses.TryGetValue(key, out var found)) verses[key] = found = new List<(int, int)>();
                found.Add((term, place.Position));
            }
        }

        var hits = verses.Select(v =>
        {
            var places = v.Value;
            double score = places.Select(p => p.Term).Distinct().Count();
            var positions = places.ToHashSet();
            for (int term = 0; term + 1 < terms.Count; term++)
                if (places.Any(p => p.Term == term && positions.Contains((term + 1, p.Position + 1)))) score += 0.5;
            return new WordHit(v.Key.Book, v.Key.Chapter, v.Key.Verse, score);
        })
        .OrderByDescending(h => h.Score).ThenBy(h => h.Book).ThenBy(h => h.Chapter).ThenBy(h => h.Verse);
        return (limit > 0 ? hits.Take(limit) : hits).ToList();
    }

    /// <summary>The markdown of a verse with the searched words (normalized, see <see cref="Terms"/>) in bold.</summary>
    public static string Highlight(string verse, IReadOnlyCollection<string> terms)
    {
        var set = terms.ToHashSet();
        var sb = new StringBuilder(verse);
        foreach (var token in Tokenize(verse).Where(t => t.Words.Any(set.Contains)).Reverse()) // from the end: the places stay valid
        {
            sb.Insert(token.Start + token.Length, "**");
            sb.Insert(token.Start, "**");
        }
        return sb.ToString();
    }
}
