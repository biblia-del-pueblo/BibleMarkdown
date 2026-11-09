using System.Text;
using System.Text.RegularExpressions;

namespace BibleMarkdown;

/// <summary>
/// The modules of e-Sword (.bblx, .dctx, ...) and MySword (.bbl.mybible, .dct.mybible): SQLite databases. A Bible has a
/// table Bible with a row for every verse (Book 1 to 66, Chapter, Verse, Scripture); a dictionary has a word and its
/// definition for every entry. The text of a verse is in the markup of the two programs (<c>&lt;FI&gt;added words&lt;Fi&gt;</c>,
/// <c>&lt;WH430&gt;</c> ...), which <see cref="ESwordMarkup"/> reads.
/// </summary>
internal static class ESwordModule
{
	/// <summary>The verses of a Bible module: book, chapter, verse and the raw text.</summary>
	public static IEnumerable<(int Book, int Chapter, int Verse, string Text)> Verses(SqliteFile database)
	{
		var table = database.Tables.FirstOrDefault(t => t.Equals("Bible", StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidDataException("The module has no table Bible.");
		var columns = database.Columns(table);
		int book = Index(columns, "Book"), chapter = Index(columns, "Chapter"), verse = Index(columns, "Verse"), text = Index(columns, "Scripture");
		foreach (var row in database.Rows(table))
		{
			if (row[book] is not long b || row[chapter] is not long c || row[verse] is not long v) continue;
			var scripture = row[text] switch { string s => s, byte[] bytes => Encoding.UTF8.GetString(bytes), _ => "" };
			yield return ((int)b, (int)c, (int)v, scripture);
		}
	}

	/// <summary>The description of a module (its table Details), or null.</summary>
	public static string? Description(SqliteFile database)
	{
		var table = database.Tables.FirstOrDefault(t => t.Equals("Details", StringComparison.OrdinalIgnoreCase));
		if (table == null) return null;
		var columns = database.Columns(table);
		int description = columns.ToList().FindIndex(c => c.Equals("Description", StringComparison.OrdinalIgnoreCase) || c.Equals("Title", StringComparison.OrdinalIgnoreCase));
		if (description < 0) return null;
		return database.Rows(table).Select(r => r[description] as string).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
	}

	/// <summary>
	/// The entries of a dictionary module: the word and its definition. The table is the one with a column for the word
	/// (word, topic, key) and one for the text (definition, data, text), which are named differently in the two programs.
	/// </summary>
	public static IEnumerable<(string Word, string Definition)> Entries(SqliteFile database)
	{
		foreach (var table in database.Tables)
		{
			var columns = database.Columns(table);
			int word = columns.ToList().FindIndex(c => Regex.IsMatch(c, @"^(word|topic|key|entry)$", RegexOptions.IgnoreCase));
			int definition = columns.ToList().FindIndex(c => Regex.IsMatch(c, @"^(definition|data|text|content|description)$", RegexOptions.IgnoreCase));
			if (word < 0 || definition < 0 || word == definition) continue;
			foreach (var row in database.Rows(table))
			{
				var w = row[word] as string;
				var d = row[definition] switch { string s => s, byte[] bytes => Encoding.UTF8.GetString(bytes), _ => null };
				if (!string.IsNullOrWhiteSpace(w) && !string.IsNullOrWhiteSpace(d)) yield return (w.Trim(), d);
			}
			yield break;
		}
		throw new InvalidDataException("The module has no table of words and definitions.");
	}

	static int Index(IReadOnlyList<string> columns, string name)
	{
		for (int i = 0; i < columns.Count; i++)
			if (columns[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
		throw new InvalidDataException($"The table has no column {name}.");
	}
}

/// <summary>Reads the markup of the verses of e-Sword and MySword and makes Bible Markdown of it.</summary>
internal static class ESwordMarkup
{
	// <FI>, <Fi>, <TS1>, <WH430>, <WTN-NSM> ...: a name of one or two letters (WG, WH and WT for a Strong's number or a
	// morphology), then perhaps an argument (the number, the code, a level).
	static readonly Regex TagRx = new(@"<(?<name>W[GHT]|[A-Za-z]{1,2})(?<arg>[^>]{0,40})>", RegexOptions.Compiled);

	sealed class Frame(string kind)
	{
		public string Kind { get; } = kind;
		public StringBuilder Text { get; } = new();
	}

	/// <param name="raw">The text of a verse as the module stores it.</param>
	/// <param name="strongs">Whether the Strong's numbers (and the morphology) are kept, on their words.</param>
	/// <param name="nextLabel">The label of the next footnote (a, b, c ...).</param>
	/// <param name="paragraphAfter">Whether the verse ends a paragraph (the tag CM): the next verse starts a new one.</param>
	public static SwordVerse Convert(string raw, bool strongs, Func<string> nextLabel, out bool paragraphAfter)
	{
		var verse = new SwordVerse();
		paragraphAfter = false;
		var root = new Frame("");
		var stack = new Stack<Frame>();
		Frame Current() => stack.Count > 0 ? stack.Peek() : root;

		void Close(string kind)
		{
			// the tags of a kind may be closed without ever having been opened: they are ignored
			if (!stack.Any(f => f.Kind == kind)) return;
			Frame frame;
			do
			{
				frame = stack.Pop();
				if (frame.Kind != kind) Current().Text.Append(frame.Text); // an element that was not closed ends with its parent
			}
			while (frame.Kind != kind);

			var content = frame.Text.ToString();
			switch (kind)
			{
				case "FI": Current().Text.Append(Emphasis(content, "*", "*")); break;
				case "FR": Current().Text.Append(Emphasis(content, "[", "]{.wj}")); break;
				case "TS": { var title = Tidy(content); if (title.Length > 0) verse.Titles.Add(title); break; }
				case "RF": { var note = Tidy(content); if (note.Length > 0) verse.Notes.Add((nextLabel(), note)); break; }
				default: Current().Text.Append(content); break; // FO, FU, FB ...: the text without the markup
			}
		}

		int pos = 0;
		foreach (Match tag in TagRx.Matches(raw))
		{
			Current().Text.Append(raw, pos, tag.Index - pos);
			pos = tag.Index + tag.Length;

			var name = tag.Groups["name"].Value;
			var arg = tag.Groups["arg"].Value;
			bool closing = name.Length == 2 && char.IsUpper(name[0]) && char.IsLower(name[1]) && arg.Length == 0;
			if (closing) { Close(name.ToUpperInvariant()); continue; }

			switch (name)
			{
				case "FI" when arg.Length == 0: stack.Push(new Frame("FI")); break;
				case "FR" when arg.Length == 0: stack.Push(new Frame("FR")); break;
				case "RF": stack.Push(new Frame("RF")); break;
				case "TS": stack.Push(new Frame("TS")); break;
				case "FO" or "FU" or "FB" when arg.Length == 0: stack.Push(new Frame(name)); break;
				case "CM" when arg.Length == 0: paragraphAfter = true; break;
				case "WG" or "WH" when arg.Length > 0 && char.IsDigit(arg[0]):
					if (strongs) AddStrong(Current().Text, name[1] + arg.TrimStart('0').Trim(), isMorphology: false);
					break;
				case "WT" when arg.Length > 0:
					if (strongs) AddStrong(Current().Text, arg.Trim().Replace(' ', '-'), isMorphology: true);
					break;
				default: break; // paragraph and indentation marks, cross references ...: not used
			}
		}
		Current().Text.Append(raw, pos, raw.Length - pos);
		while (stack.Count > 0) Close(stack.Peek().Kind);

		verse.Text = Tidy(root.Text.ToString());
		return verse;
	}

	/// <summary>
	/// Puts a Strong's number (G26, H7225) or a morphology code on the word before it, as Bible Markdown has it:
	/// <c>word_H7225_%morph</c>. Only a word of letters is marked: with punctuation or markup in it, it is not.
	/// </summary>
	static void AddStrong(StringBuilder text, string value, bool isMorphology)
	{
		int start = text.Length;
		while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
		var token = text.ToString(start, text.Length - start);
		var match = Regex.Match(token, @"^(?<word>[\p{L}\p{M}'’]+)(?<attrs>(?:_[GH][0-9]+|_%[0-9A-Za-z-]+)*)$");
		if (!match.Success) return;
		if (isMorphology && match.Groups["attrs"].Length == 0) return; // a morphology belongs to a number
		text.Length = start;
		text.Append(token).Append('_').Append(isMorphology ? "%" : "").Append(value);
	}

	/// <summary>Markdown emphasis: spaces at the ends of the content stay outside the marks, as markdown needs.</summary>
	static string Emphasis(string content, string open, string close)
	{
		var trimmed = content.Trim();
		if (trimmed.Length == 0) return content;
		var before = content[..(content.Length - content.TrimStart().Length)];
		var after = content[(content.TrimEnd().Length)..];
		return before + open + trimmed + close + after;
	}

	/// <summary>The text without line breaks and double spaces, with HTML entities decoded.</summary>
	static string Tidy(string text) => Regex.Replace(System.Net.WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
}
