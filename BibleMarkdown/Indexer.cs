using System;
using System.Collections.Generic;
using System.Text;

namespace BibleMarkdown
{
    public class Indexer : WithTransform
    {
        public Indexer(Transform transform) : base(transform) { }

        /// <summary>
        /// Creates the index of all the words of the books in <paramref name="mdpath"/>: a LiteDB database
        /// (<see cref="WordIndex.FileName"/>, in the same folder) with an entry for every word of every verse, its book,
        /// chapter and verse, and its place in the verse. See <see cref="WordIndex"/>; the index is searched with
        /// <see cref="WordIndex.Search"/>.
        /// </summary>
        /// <returns>The number of words in the index.</returns>
        public int Create()
        {
            Log("Create the word index");
            var indexSize = WordIndex.Create(ProjectPath);
            LogFile(Path.Combine(ProjectPath, WordIndex.FileName), $"Created, {indexSize} words,");
            return indexSize;
        }
        public List<WordHit> Search(IReadOnlyList<string> terms, int limit)
        {
            return WordIndex.Search(Path.Combine(ProjectPath, WordIndex.FileName), terms, limit);
        }
    }
}
