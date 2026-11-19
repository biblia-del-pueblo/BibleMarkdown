using System.Text;
using Xunit;

namespace BibleMarkdown.Tests;

/// <summary>
/// The footnotes of the import of SWORD modules (and e-Sword, which writes its chapters the same way): a marker ^a^ stays in the text of the verse, and the bodies
/// ^a^[Footnote] follow the paragraph, as in the import of USFM: the first on a new line, the others after it separated by a space. A paragraph ends with the next
/// paragraph, with a title, and with the chapter.
/// </summary>
public class SwordFootnoteTests
{
    const string NL = "\n";

    static SwordVerse Verse(string text, bool paragraph = false, string? title = null, params (string Label, string Text)[] notes)
    {
        var verse = new SwordVerse { Text = text, Paragraph = paragraph };
        if (title != null) verse.Titles.Add(title);
        verse.Notes.AddRange(notes);
        return verse;
    }

    static string Write(int chapter, SwordVerse? heading, bool eachVerseOnNewLine, params (int Number, SwordVerse Verse)[] verses)
    {
        var text = new StringBuilder();
        SwordChapter.Append(text, NL, chapter, heading, verses, eachVerseOnNewLine);
        return text.ToString();
    }

    [Fact]
    public void NoFootnotes_VersesFollowEachOther()
    {
        var text = Write(1, null, false, (1, Verse("One.")), (2, Verse("Two.")));
        Assert.Equal("# 1\n@1 One. @2 Two.", text);
    }

    [Fact]
    public void Footnote_FollowsTheParagraph_NotItsVerse()
    {
        var text = Write(1, null, false, (1, Verse("One^a^.", notes: ("a", "Foot"))), (2, Verse("Two.")), (3, Verse("Three.")));
        Assert.Equal("# 1\n@1 One^a^. @2 Two. @3 Three.\n^a^[Foot]", text);
    }

    [Fact]
    public void SeveralFootnotes_AreOnOneLineAfterTheParagraph()
    {
        var text = Write(1, null, false,
            (1, Verse("One^a^.", notes: ("a", "A"))),
            (2, Verse("Two^b^ and^c^.", notes: [("b", "B"), ("c", "C")])));
        Assert.Equal("# 1\n@1 One^a^. @2 Two^b^ and^c^.\n^a^[A] ^b^[B] ^c^[C]", text);
    }

    [Fact]
    public void NewParagraph_EndsTheParagraphBefore()
    {
        var text = Write(1, null, false,
            (1, Verse("One^a^.", notes: ("a", "A"))),
            (2, Verse("Two^b^.", paragraph: true, notes: ("b", "B"))));
        Assert.Equal("# 1\n@1 One^a^.\n^a^[A]\n\n@2 Two^b^.\n^b^[B]", text);
    }

    [Fact]
    public void Title_EndsTheParagraphBefore()
    {
        var text = Write(1, null, false,
            (1, Verse("One^a^.", notes: ("a", "A"))),
            (2, Verse("Two.", title: "Heading")));
        Assert.Equal("# 1\n@1 One^a^.\n^a^[A]\n\n## Heading\n@2 Two.", text);
    }

    [Fact]
    public void Chapter_EndsTheParagraph_BeforeTheNextChapter()
    {
        var book = new StringBuilder();
        SwordChapter.Append(book, NL, 1, null, [(1, Verse("One^a^.", notes: ("a", "A")))], false);
        SwordChapter.Append(book, NL, 2, null, [(1, Verse("Uno."))], false);
        Assert.Equal("# 1\n@1 One^a^.\n^a^[A]\n\n# 2\n@1 Uno.", book.ToString());
    }

    [Fact]
    public void ParagraphInsideAVerse_EndsThereForTheFootnotesBeforeIt()
    {
        var text = Write(1, null, false, (1, Verse("First^a^.\n\nSecond^b^.", notes: [("a", "A"), ("b", "B")])));
        Assert.Equal("# 1\n@1 First^a^.\n^a^[A]\n\nSecond^b^.\n^b^[B]", text);
    }

    [Fact]
    public void FootnoteOfAnEarlierVerse_FollowsTheParagraphThatEndsInsideALaterVerse()
    {
        var text = Write(1, null, false,
            (1, Verse("One^a^.", notes: ("a", "A"))),
            (2, Verse("Two.\n\nThree.")));
        Assert.Equal("# 1\n@1 One^a^. @2 Two.\n^a^[A]\n\nThree.", text);
    }

    [Fact]
    public void FootnoteOfTheHeading_FollowsTheFirstParagraph()
    {
        var heading = Verse("Intro^a^", notes: ("a", "Intro note"));
        var text = Write(1, heading, false, (1, Verse("One.")), (2, Verse("Two.", paragraph: true)));
        Assert.Equal("# 1\n*Intro^a^* \\\n@1 One.\n^a^[Intro note]\n\n@2 Two.", text);
    }

    [Fact]
    public void EachVerseOnNewLine_StaysInTheParagraph()
    {
        var text = Write(1, null, true, (1, Verse("One^a^.", notes: ("a", "A"))), (2, Verse("Two.")));
        Assert.Equal("# 1\n@1 One^a^.\n@2 Two.\n^a^[A]", text);
    }

    [Fact]
    public void FromTheMarkup_FootnoteBodyIsAtTheEndOfTheParagraph()
    {
        string label = "a";
        var one = SwordMarkup.Convert("In the beginning<note>Foot</note> God created.", "OSIS", () => label, false);
        var two = SwordMarkup.Convert("And the earth was void.", "OSIS", () => "b", false);

        var text = Write(1, null, false, (1, one), (2, two));

        Assert.StartsWith("# 1\n@1 In the beginning^a^ God created. @2 And the earth was void.", text);
        Assert.EndsWith("\n^a^[Foot]", text);
    }
}
