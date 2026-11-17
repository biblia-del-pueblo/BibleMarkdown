using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BibleMarkdown;

/// <summary>The types that BibleMarkdown reads and writes as JSON (source generation: no reflection, see <see cref="JsonOptions"/>).</summary>
[JsonSerializable(typeof(InstallableBook))]
[JsonSerializable(typeof(List<Publisher.IndexEntry>))]
[JsonSerializable(typeof(List<string>))]
internal partial class JsonContext : JsonSerializerContext
{
    /// <summary>One line.</summary>
    public static readonly JsonContext Plain = new(JsonOptions.Create(indented: false));

    /// <summary>Indented, for what people read.</summary>
    public static readonly JsonContext Indented = new(JsonOptions.Create(indented: true));
}
