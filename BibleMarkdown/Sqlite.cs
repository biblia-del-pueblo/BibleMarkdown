using System.Text;
using System.Text.RegularExpressions;

namespace BibleMarkdown;

/// <summary>
/// A reader for SQLite database files, only as far as modules of e-Sword and MySword need: ordinary tables (with a
/// rowid) are read row by row. It is managed code with no native library, so it also runs where SQLite does not (Android,
/// the browser). Not supported are writing, indexes, WITHOUT ROWID tables and encrypted files.
/// </summary>
internal sealed class SqliteFile
{
	readonly byte[] data;
	readonly int pageSize;
	readonly int usable;     // the part of a page that is not reserved
	readonly Encoding encoding;
	readonly Dictionary<string, (int Root, string Sql)> tables = new(StringComparer.OrdinalIgnoreCase);

	public static SqliteFile Open(string path) => new(File.ReadAllBytes(path), path);

	SqliteFile(byte[] data, string path)
	{
		this.data = data;
		if (data.Length < 100 || Encoding.ASCII.GetString(data, 0, 15) != "SQLite format 3")
			throw new InvalidDataException($"{Path.GetFileName(path)} is not a SQLite database.");
		pageSize = (data[16] << 8) | data[17];
		if (pageSize == 1) pageSize = 65536;
		usable = pageSize - data[20];
		encoding = ReadInt32(56) switch { 2 => Encoding.Unicode, 3 => Encoding.BigEndianUnicode, _ => Encoding.UTF8 };
		if (data.Length % pageSize != 0 && data.Length < pageSize) throw new InvalidDataException($"{Path.GetFileName(path)} is not a valid SQLite database.");

		// sqlite_master: type, name, tbl_name, rootpage, sql
		foreach (var row in ReadTable(1))
			if (row.Count >= 5 && row[0] as string == "table" && row[1] is string name && row[3] is long root)
				tables[name] = ((int)root, row[4] as string ?? "");
	}

	public IEnumerable<string> Tables => tables.Keys;

	public bool HasTable(string table) => tables.ContainsKey(table);

	/// <summary>The names of the columns of a table, as its CREATE TABLE statement declares them.</summary>
	public IReadOnlyList<string> Columns(string table) => ParseColumns(tables[table].Sql).Select(c => c.Name).ToList();

	/// <summary>The rows of a table, a value per column: null, a long, a double, a string or a byte array.</summary>
	public IEnumerable<object?[]> Rows(string table)
	{
		if (!tables.TryGetValue(table, out var info)) throw new InvalidDataException($"The table {table} is not in the database.");
		var columns = ParseColumns(info.Sql);
		int rowidColumn = columns.FindIndex(c => c.IsRowid);
		foreach (var record in ReadTable(info.Root, withRowid: true))
		{
			var row = new object?[columns.Count];
			for (int i = 0; i < row.Length && i < record.Count - 1; i++) row[i] = record[i];
			if (rowidColumn >= 0 && row[rowidColumn] == null) row[rowidColumn] = record[^1]; // INTEGER PRIMARY KEY is the rowid
			yield return row;
		}
	}

	// ---- the CREATE TABLE statement -----------------------------------------------------------------------

	static List<(string Name, bool IsRowid)> ParseColumns(string sql)
	{
		var result = new List<(string, bool)>();
		int open = sql.IndexOf('('), close = sql.LastIndexOf(')');
		if (open < 0 || close < open) return result;
		var body = sql[(open + 1)..close];

		var parts = new List<string>();
		int depth = 0, start = 0;
		char quote = '\0';
		for (int i = 0; i < body.Length; i++)
		{
			char c = body[i];
			if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
			if (c is '"' or '\'' or '`') quote = c;
			else if (c == '[') quote = ']';
			else if (c == '(') depth++;
			else if (c == ')') depth--;
			else if (c == ',' && depth == 0) { parts.Add(body[start..i]); start = i + 1; }
		}
		parts.Add(body[start..]);

		foreach (var part in parts.Select(p => p.Trim()).Where(p => p.Length > 0))
		{
			if (Regex.IsMatch(part, @"^(PRIMARY|UNIQUE|FOREIGN|CHECK|CONSTRAINT)\b", RegexOptions.IgnoreCase)) continue;
			var match = Regex.Match(part, @"^(?:""(?<n>[^""]+)""|`(?<n>[^`]+)`|\[(?<n>[^\]]+)\]|'(?<n>[^']+)'|(?<n>[^\s]+))");
			if (!match.Success) continue;
			result.Add((match.Groups["n"].Value, Regex.IsMatch(part, @"\bINTEGER\s+PRIMARY\s+KEY\b", RegexOptions.IgnoreCase)));
		}
		return result;
	}

	// ---- b-trees --------------------------------------------------------------------------------------------

	int ReadInt32(int pos) => (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
	int ReadInt16(int pos) => (data[pos] << 8) | data[pos + 1];

	static long ReadVarint(byte[] buffer, ref int pos)
	{
		long value = 0;
		for (int i = 0; i < 9; i++)
		{
			byte b = buffer[pos++];
			if (i == 8) return (value << 8) | b;
			value = (value << 7) | (uint)(b & 0x7F);
			if ((b & 0x80) == 0) return value;
		}
		return value;
	}

	/// <summary>The records of the table whose root page this is; the last value of every record is its rowid.</summary>
	List<List<object?>> ReadTable(int rootPage, bool withRowid = false)
	{
		var rows = new List<List<object?>>();
		Walk(rootPage, rows, 0);
		return rows;
	}

	void Walk(int pageNumber, List<List<object?>> rows, int depth)
	{
		if (depth > 40) throw new InvalidDataException("The SQLite database has a b-tree that is too deep.");
		long pageStart = (long)(pageNumber - 1) * pageSize;
		if (pageNumber < 1 || pageStart + pageSize > data.Length) throw new InvalidDataException("A page of the SQLite database is beyond the end of the file.");
		int page = (int)pageStart;
		int header = pageNumber == 1 ? page + 100 : page;
		byte type = data[header];
		int cells = ReadInt16(header + 3);

		if (type == 0x05) // interior table page: child pages, in order of the rowids
		{
			int pointers = header + 12;
			for (int i = 0; i < cells; i++)
			{
				int cell = page + ReadInt16(pointers + 2 * i);
				Walk(ReadInt32(cell), rows, depth + 1);
			}
			Walk(ReadInt32(header + 8), rows, depth + 1);
			return;
		}
		if (type != 0x0D) throw new InvalidDataException("The SQLite database has a page that is not a table page (an index or a table without rowid).");

		int cellPointers = header + 8;
		for (int i = 0; i < cells; i++)
		{
			int pos = page + ReadInt16(cellPointers + 2 * i);
			long payloadSize = ReadVarint(data, ref pos);
			long rowid = ReadVarint(data, ref pos);
			var payload = ReadPayload(pos, payloadSize);
			var record = ParseRecord(payload);
			record.Add(rowid);
			rows.Add(record);
		}
	}

	/// <summary>The payload of a cell: the part on the page, and the rest in the chain of overflow pages.</summary>
	byte[] ReadPayload(int pos, long size)
	{
		long maxLocal = usable - 35;
		if (size <= maxLocal) return data.AsSpan(pos, (int)size).ToArray();

		long minLocal = ((usable - 12) * 32 / 255) - 23;
		long local = minLocal + ((size - minLocal) % (usable - 4));
		if (local > maxLocal) local = minLocal;

		var payload = new byte[size];
		Array.Copy(data, pos, payload, 0, local);
		int overflow = ReadInt32(pos + (int)local);
		long copied = local;
		while (copied < size)
		{
			if (overflow == 0) throw new InvalidDataException("An overflow page of the SQLite database is missing.");
			int page = (overflow - 1) * pageSize;
			if (page < 0 || page + pageSize > data.Length) throw new InvalidDataException("An overflow page of the SQLite database is beyond the end of the file.");
			int next = ReadInt32(page);
			int count = (int)Math.Min(usable - 4, size - copied);
			Array.Copy(data, page + 4, payload, copied, count);
			copied += count;
			overflow = next;
		}
		return payload;
	}

	List<object?> ParseRecord(byte[] payload)
	{
		int pos = 0;
		long headerSize = ReadVarint(payload, ref pos);
		var types = new List<long>();
		while (pos < headerSize) types.Add(ReadVarint(payload, ref pos));

		var values = new List<object?>(types.Count + 1);
		pos = (int)headerSize;
		foreach (var type in types)
		{
			switch (type)
			{
				case 0: values.Add(null); break;
				case 1: values.Add((long)(sbyte)payload[pos]); pos += 1; break;
				case 2: values.Add((long)(short)((payload[pos] << 8) | payload[pos + 1])); pos += 2; break;
				case 3: values.Add((long)((((payload[pos] << 24) | (payload[pos + 1] << 16) | (payload[pos + 2] << 8)) >> 8))); pos += 3; break;
				case 4: values.Add((long)ReadInt(payload, pos, 4)); pos += 4; break;
				case 5: values.Add(ReadInt(payload, pos, 6)); pos += 6; break;
				case 6: values.Add(ReadInt(payload, pos, 8)); pos += 8; break;
				case 7: values.Add(BitConverter.Int64BitsToDouble(ReadInt(payload, pos, 8))); pos += 8; break;
				case 8: values.Add(0L); break;
				case 9: values.Add(1L); break;
				default:
					if (type >= 12)
					{
						int length = (int)((type - (type % 2 == 0 ? 12 : 13)) / 2);
						values.Add(type % 2 == 0 ? payload.AsSpan(pos, length).ToArray() : encoding.GetString(payload, pos, length));
						pos += length;
					}
					else throw new InvalidDataException("The SQLite database has a value of a type that is not known.");
					break;
			}
		}
		return values;
	}

	// a signed big-endian integer of 4, 6 or 8 bytes
	static long ReadInt(byte[] buffer, int pos, int bytes)
	{
		long value = (sbyte)buffer[pos];
		for (int i = 1; i < bytes; i++) value = (value << 8) | buffer[pos + i];
		return value;
	}
}
