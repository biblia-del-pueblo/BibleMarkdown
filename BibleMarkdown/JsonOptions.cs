using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BibleMarkdown;

/// <summary>
/// How JSON is read and written (System.Text.Json): the info.json of a Bible, the index of the Bibles, and the settings of the reader. The reading is as
/// tolerant as the files that people write by hand need: names of properties in any case, comments and a comma after the last entry, and numbers in quotes.
/// The types are read and written by source generation (a JsonSerializerContext of each project), not by reflection, so that the code can be compiled ahead of
/// time (native AOT); a kind of Bible is written as its name by the converter on its enum (InstallableBook.Classes).
/// </summary>
public static class JsonOptions
{
    /// <summary>
    /// A new set of options (a context takes the set that it is made with, and a set belongs to one context).
    /// </summary>
    /// <param name="indented">For the files that are meant to be read by people.</param>
    public static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = indented,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // The metadata of a polymorphic type ("$type") may be anywhere in its object, as with the files that were written by hand.
        AllowOutOfOrderMetadataProperties = true,
        // Letters are written as letters ("Español"), not as \u00F1: the files are read by people.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
