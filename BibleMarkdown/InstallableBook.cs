// This file is the one source of InstallableBook for two projects:
//  - BibleMarkdown compiles it as BibleMarkdown.InstallableBook, only the data that is stored in an info.json
//    (see Importer.ReadInfo);
//  - BibleReader.Library links it (BibleReader.Library.csproj) and defines BibleReader: it is then
//    BibleReader.Library.InstallableBook, a Bible with the search and the install methods.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

#if BibleReader
namespace BibleReader.Library;
#else
namespace BibleMarkdown;
#endif

public class InstallableBook
#if BibleReader
    : BookText
#endif
{
    public enum Classes
    {
        All,
        Bible,
        Comment,
        Dictionary,
        Book,
        Map,
        AddOn
    }

#if !BibleReader
    // Bible has these in BibleReader.
    public string? Name { get; set; }
    public string? EnglishName { get; set; }
    public virtual string Origin => GetType().Name;
#endif

    public virtual Classes Class { get; set; }

    public string CountryCode { get; set; }
    public string CountryName { get; set; }
    public string Language { get; set; }
    public string LanguageEnglishName {
        get => field ?? Language;
        set => field = value;
    }
#if BibleReader
    /// <summary>A verse that matches a search.</summary>
    /// <param name="Position">Where the verse is: its book, chapter and verse.</param>
    /// <param name="Score">The match score: the number of different searched words in the verse, plus half a point for every two of them that stand next to each other as in the search phrase.</param>
    /// <param name="Markdown">The markdown of the verse, the searched words in bold.</param>
    public sealed record SearchResult(BookText.VersePosition Position, double Score, string Markdown);

    /// <summary>
    /// Searches the Bible for the words of <paramref name="phrase"/>: every word is looked up in the index of the
    /// words (made first, with <see cref="BibleMarkdown.Importer.CreateIndex"/>, if there is none yet). The verses
    /// that have any of the words are returned, the best match first (see <see cref="SearchResult.Score"/>) and in the
    /// order of the Bible when equal, with the searched words in bold.
    /// </summary>
    /// <param name="limit">The most verses to return (0 for all).</param>
    public async Task<List<SearchResult>> Search(string phrase, int limit = 100)
    {
        var terms = BibleMarkdown.WordIndex.Terms(phrase ?? "");
        if (terms.Count == 0) return new List<SearchResult>();
        var indexFile = Path.Combine(ConfigPath, BibleMarkdown.WordIndex.FileName);

        return await Task.Run(() =>
        {
            if (!BibleMarkdown.WordIndex.IsCurrent(indexFile)) new BibleMarkdown.Importer().CreateIndex(ConfigPath);
            var hits = BibleMarkdown.WordIndex.Search(indexFile, terms, limit);

            // the books are those of the folder; their verses are read once for every book that has a hit
            if (Books.Count == 0)
            {
                var books = OpenFrom(ConfigPath).Books;
                foreach (var book in books) book.BookText = this;
                Books = books;
            }
            var verses = new Dictionary<int, Dictionary<(int Chapter, int Verse), string>>();

            var results = new List<SearchResult>();
            foreach (var hit in hits)
            {
                var book = Books.FirstOrDefault(b => b.Index == hit.Book);
                if (book == null) continue;
                if (!verses.TryGetValue(hit.Book, out var byVerse))
                {
                    byVerse = new Dictionary<(int, int), string>();
                    foreach (var (chapter, verse, text) in BibleMarkdown.WordIndex.Verses(book.Markdown)) byVerse.TryAdd((chapter, verse), text);
                    verses[hit.Book] = byVerse;
                }
                var markdown = byVerse.TryGetValue((hit.Chapter, hit.Verse), out var found) ? BibleMarkdown.WordIndex.Highlight(found, terms) : "";
                results.Add(new SearchResult(new VersePosition { Book = book, Chapter = hit.Chapter, Verse = hit.Verse }, hit.Score, markdown));
            }
            return results;
        });
    }

    public virtual async Task Install() { }
    public virtual async Task Uninstall()
    {
        if (Directory.Exists(ConfigPath)) Directory.Delete(ConfigPath);
    }
#endif
}
