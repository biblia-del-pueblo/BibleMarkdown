using Xunit;

namespace BibleMarkdown.Tests;

/// <summary>
/// The paragraphs of the import of SWORD modules. SwordMarkup converts one verse at a time: a paragraph marker before the text of a verse starts a paragraph with
/// it (Paragraph), one in the middle of the text is a blank line in it, and one after the last text (ParagraphAfter) starts a paragraph with the next verse, which
/// SwordChapter hands on.
/// </summary>
public class SwordParagraphTests
{
    static SwordVerse Osis(string raw) => SwordMarkup.Convert(raw, "OSIS", () => "a", false);
    static SwordVerse Gbf(string raw) => SwordMarkup.Convert(raw, "GBF", () => "a", false);
    static SwordVerse Thml(string raw) => SwordMarkup.Convert(raw, "ThML", () => "a", false);

    // ---- the markers in one verse --------------------------------------------------------------------------

    [Fact]
    public void NoMarker_NoParagraph()
    {
        var verse = Osis("In the beginning God created the heaven and the earth.");
        Assert.False(verse.Paragraph);
        Assert.False(verse.ParagraphAfter);
        Assert.Equal("In the beginning God created the heaven and the earth.", verse.Text);
    }

    [Fact]
    public void OsisParagraphBeforeTheText_StartsAParagraphWithTheVerse()
    {
        var verse = Osis("<p>And God said, Let there be light.");
        Assert.True(verse.Paragraph);
        Assert.False(verse.ParagraphAfter);
    }

    [Fact]
    public void OsisParagraphWithEnd_StartsWithTheVerseAndEndsAfterIt()
    {
        var verse = Osis("<p>And God said, Let there be light.</p>");
        Assert.True(verse.Paragraph);
        Assert.True(verse.ParagraphAfter);
    }

    [Fact]
    public void OsisClosingParagraphAtTheEnd_StartsAParagraphWithTheNextVerse()
    {
        var verse = Osis("And there was light.</p>");
        Assert.False(verse.Paragraph);
        Assert.True(verse.ParagraphAfter);
    }

    [Fact]
    public void OsisClosingParagraphAtTheStart_StartsAParagraphWithTheVerse()
    {
        var verse = Osis("</p><p>And God saw the light.");
        Assert.True(verse.Paragraph);
        Assert.False(verse.ParagraphAfter);
    }

    [Fact]
    public void OsisParagraphMilestones_StartAndEnd()
    {
        var verse = Osis("<div type=\"paragraph\" sID=\"p1\"/>And God said.<div type=\"paragraph\" eID=\"p1\"/>");
        Assert.True(verse.Paragraph);
        Assert.True(verse.ParagraphAfter);
    }

    [Fact]
    public void OsisXpDivMilestones_StartAndEnd_AsInTheModulesOfEBible()
    {
        // Genesis 1:3 of the World English Bible of ebible.org, a module made by osis2mod: the paragraph starts in this verse.
        var start = Osis("<div type=\"x-milestone\" subType=\"x-preverse\" sID=\"pv1\"/><div sID=\"gen7\" type=\"x-p\"/> <div type=\"x-milestone\" subType=\"x-preverse\" eID=\"pv1\"/>God said, “Let there be light,” and there was light.");
        Assert.True(start.Paragraph);
        Assert.False(start.ParagraphAfter);
        Assert.Equal("God said, “Let there be light,” and there was light.", start.Text);

        // Matthew 5:2: the paragraph ends after the last text of the verse.
        var end = Osis("He opened his mouth and taught them, saying,  <div eID=\"gen25582\" type=\"x-p\"/>");
        Assert.False(end.Paragraph);
        Assert.True(end.ParagraphAfter);
    }

    [Fact]
    public void OsisXpDivs_GiveTheParagraphsOfAChapter()
    {
        var verses = SwordChapter.KeepVersesWithText(Chapter(Osis,
            "<div sID=\"p1\" type=\"x-p\"/>One.",
            "Two.<div eID=\"p1\" type=\"x-p\"/>",
            "<div sID=\"p2\" type=\"x-p\"/>Three.",
            "Four.<div eID=\"p2\" type=\"x-p\"/>"));

        Assert.Equal([true, false, true, false], verses.Select(v => v.Verse.Paragraph));
    }

    [Fact]
    public void OsisPilcrowMilestone_StartsAParagraph()
    {
        var verse = Osis("<milestone type=\"x-p\" marker=\"&#182;\"/>And God said.");
        Assert.True(verse.Paragraph);
        Assert.False(verse.ParagraphAfter);
    }

    [Fact]
    public void ParagraphInTheMiddle_IsABlankLineInTheText()
    {
        var verse = Osis("First part.<p/>Second part.");
        Assert.False(verse.Paragraph);
        Assert.False(verse.ParagraphAfter);
        Assert.Equal("First part.\n\nSecond part.", verse.Text);
    }

    [Fact]
    public void GbfParagraphMarkerAtTheEnd_StartsAParagraphWithTheNextVerse()
    {
        // GBF writes <CM> after the last verse of a paragraph.
        var verse = Gbf("And God called the light Day.<CM>");
        Assert.False(verse.Paragraph);
        Assert.True(verse.ParagraphAfter);
        Assert.Equal("And God called the light Day.", verse.Text);
    }

    [Fact]
    public void ThmlParagraph_IsKept()
    {
        var verse = Thml("<p>And God said.</p>");
        Assert.True(verse.Paragraph);
        Assert.True(verse.ParagraphAfter);
    }

    [Fact]
    public void ParagraphMarkerInAFootnote_IsNotAParagraph()
    {
        // Inside an element that is open (a note, a quotation) a marker is no paragraph of the text.
        var verse = Osis("And God said.<note><p/>A comment.</note>");
        Assert.False(verse.Paragraph);
        Assert.False(verse.ParagraphAfter);
    }

    // ---- the verses of a chapter ---------------------------------------------------------------------------

    static List<(int Number, SwordVerse Verse)> Chapter(Func<string, SwordVerse> convert, params string[] raws) =>
        raws.Select((raw, index) => (index + 1, convert(raw))).ToList();

    [Fact]
    public void ChapterGbf_ParagraphMarkerAtTheEnd_StartsAParagraphWithTheNextVerse()
    {
        var verses = SwordChapter.KeepVersesWithText(Chapter(Gbf, "One.", "Two.<CM>", "Three.", "Four.<CM>", "Five."));

        Assert.Equal([1, 2, 3, 4, 5], verses.Select(v => v.Number));
        Assert.Equal([false, false, true, false, true], verses.Select(v => v.Verse.Paragraph));
    }

    [Fact]
    public void ChapterOsis_ParagraphAtTheStartOfEachParagraph()
    {
        var verses = SwordChapter.KeepVersesWithText(Chapter(Osis, "<p>One.", "Two.", "<p>Three.", "Four."));

        Assert.Equal([true, false, true, false], verses.Select(v => v.Verse.Paragraph));
    }

    [Fact]
    public void ChapterOsis_BothTheStartAndTheEndOfAParagraph_GiveOneParagraph()
    {
        var verses = SwordChapter.KeepVersesWithText(Chapter(Osis, "<p>One.", "Two.</p>", "<p>Three.", "Four.</p>"));

        Assert.Equal([true, false, true, false], verses.Select(v => v.Verse.Paragraph));
    }

    [Fact]
    public void ChapterEmptyVerses_AreRemoved_AndTheirMarkersStay()
    {
        var verses = SwordChapter.KeepVersesWithText(Chapter(Gbf, "One.<CM>", "", "Three.", "Four."));

        Assert.Equal([1, 3, 4], verses.Select(v => v.Number));
        Assert.Equal([false, true, false], verses.Select(v => v.Verse.Paragraph));
    }

    [Fact]
    public void ChapterEmptyVerseWithAMarker_StartsAParagraphWithTheNextVerse()
    {
        var verses = SwordChapter.KeepVersesWithText(Chapter(Osis, "One.", "<p/>", "Three."));

        Assert.Equal([1, 3], verses.Select(v => v.Number));
        Assert.Equal([false, true], verses.Select(v => v.Verse.Paragraph));
    }

    [Fact]
    public void ChapterWithoutMarkers_HasNoParagraphs()
    {
        var verses = SwordChapter.KeepVersesWithText(Chapter(Osis, "One.", "Two.", "Three."));

        Assert.All(verses, v => Assert.False(v.Verse.Paragraph));
    }
}
