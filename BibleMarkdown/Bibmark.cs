using System;
using System.Collections.Generic;
using System.Text;

namespace BibleMarkdown
{
    public class Bibmark
    {
        public static Transform Transform => Transform.Current;
        public static Transform Program => Transform;
        public static Importer Import => Transform.Import;
        public static Creator Create => Transform.Create;
        public static Publisher Publish => Transform.Publish;
        public static BookList Books => Transform.Books;
        public static Epub Epub => Transform.Epub;
        public static string Language
        {
            get => Transform.Language;
            set => Transform.Language = value;
        }
        public static bool ParagraphVerses => Transform.ParagraphVerses;
        public static Pandoc Pandoc => Transform.Pandoc;
        public static ISimpleLogger Logger => Transform.Logger;
        public static void Log(string message) => Logger.Log(message);
        public static void LogFile(string file) => Logger.LogFile(file);
        public static void LogFile(string file, string label) => Logger.LogFile(file, label);

        public static bool LowercaseFirstWords
        {
            get => Import.LowercaseFirstWords;
            set => Import.LowercaseFirstWords = value;
        }
        public static bool ImportStrongs
        {
            get => Import.ImportStrongs;
            set => Import.ImportStrongs = value;
        }
        public static bool EachVerseOnNewLine
        {
            get => Import.EachVerseOnNewLine;
            set => Import.EachVerseOnNewLine = value;
        }
        public static bool FromSource
        {
            get => !Import.Disabled;
            set => Import.Disabled = !value;
        }
        public static bool Imported => Import.Imported;
        public static string SwordVersification
        {
            get => Create.SwordVersification;
            set => Create.SwordVersification = value;
        }
        public static string? RightLanguage
        {
            get => Create.RightLanguage;
            set => Create.RightLanguage = value;
        }
        public static string? LeftLanguage
        {
            get => Create.LeftLanguage;
            set => Create.LeftLanguage = value;
        }

        public static string? Replace
        {
            get => Create.Replace;
            set => Create.Replace = value;
        }
        public static bool TwoLanguage
        {
            get => Create.TwoLanguage;
            set => Create.TwoLanguage = value;
        }
    }
}
