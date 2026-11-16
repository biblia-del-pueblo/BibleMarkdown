// This file is the one source of InstallableBook for two projects:
//  - BibleMarkdown compiles it as BibleMarkdown.InstallableBook, only the data that is stored in an info.json
//    (see Importer.ReadInfo);
//  - CrossBible.Library links it (CrossBible.Library.csproj) and defines CrossBible: it is then
//    CrossBible.Library.InstallableBook, a Bible with the search and the install methods.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#if CrossBible
namespace CrossBible.Library;
#else
namespace BibleMarkdown;
#endif

public class InstallableBook
#if CrossBible
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

#if !CrossBible
    // Bible has these in CrossBible.
    public string? Name { get; set; }
    public string? EnglishName { get; set; }
    public bool Publish { get; set; }
    public virtual string Origin => GetType().Name;
#endif

    public virtual Classes Class { get; set; }

    public string CountryCode { get; set; }
    public string CountryName { get; set; }
    public virtual string Language { get; set; }
    public virtual string LanguageEnglishName {
        get => field ?? Language;
        set => field = value;
    }
    [JsonIgnore]
    public string? Image { get; set; }

    string? languageCode = null;

    // Looking a language up goes through all the cultures of the system (several hundred): once for a language, not for every one of its (thousand) Bibles.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, string), string?> LanguageCodes = new();

    static CultureInfo[] cultures;
    public static CultureInfo[] Cultures => cultures ??= CultureInfo.GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures);

    public virtual string LanguageCode
    {
        get => languageCode ??=
            Regex.IsMatch(Language, @"^[a-z]{2,3}(?:-[A-Z]{2,3})?$") ? Language :
                LanguageCodes.GetOrAdd((Language, LanguageEnglishName), key =>
                    Cultures
                        .FirstOrDefault(culture =>
                            culture.EnglishName.Contains(key.Item2) ||
                            culture.NativeName.Contains(key.Item1) ||
                            culture.Name.Contains(key.Item1))?.Name)!;
        set => languageCode = value;
    }
    public virtual InstallableBook Upgrade() => this;
    public InstallableBook() { }

    public InstallableBook(InstallableBook source)
    {
        Class = source.Class;
        CountryCode = source.CountryCode;
        CountryName = source.CountryName;
        Name = source.Name;
        EnglishName = source.EnglishName;
        Image = source.Image;
        Language = source.Language;
        languageCode = source.languageCode;
        LanguageEnglishName = source.LanguageEnglishName;
#if CrossBible
        Books = source.Books;
        HasStrongs = source.HasStrongs;
#endif
    }

#if CrossBible
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
            if (!BibleMarkdown.WordIndex.IsCurrent(indexFile)) new BibleMarkdown.Transform(ConfigPath).Index.Create(LanguageCode);
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

    /// <summary>The words of the Bible that start with <paramref name="fragment"/>, with how many times each occurs (see <see cref="BibleMarkdown.WordIndex.WordsContaining"/>).</summary>
    public async Task<List<(string Word, int Count)>> WordsContaining(string fragment, int limit = 50)
    {
        var indexFile = Path.Combine(ConfigPath, BibleMarkdown.WordIndex.FileName);
        return await Task.Run(() =>
        {
            if (!BibleMarkdown.WordIndex.IsCurrent(indexFile)) new BibleMarkdown.Transform(ConfigPath).Index.Create(LanguageCode);
            return BibleMarkdown.WordIndex.WordsContaining(indexFile, fragment, limit);
        });
    }

    public virtual async Task Install() { }
    public virtual async Task Uninstall()
    {
        if (Directory.Exists(ConfigPath)) Directory.Delete(ConfigPath, true);
    }
#endif
}
