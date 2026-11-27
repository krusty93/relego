using Relego.Core.Annotations;
using Relego.Core.Parsing;

namespace Relego.Tests.Parsing;

/// <summary>
/// Table-driven coverage of the reading vocabulary grammar (research.md R1): the leading markers a
/// reader writes before a note, the note prose that survives them, and the cases that must stay
/// literal commentary.
/// </summary>
public sealed class AnnotationMarkerParserTests
{
    [Theory]
    [InlineData(".h1", null, 1, null, "")]
    [InlineData(".H2", null, 2, null, "")]
    [InlineData(".h3", null, 3, null, "")]
    [InlineData(".h1 A summary.", "A summary.", 1, null, "")]
    [InlineData(".h1\nA summary.", "A summary.", 1, null, "")]
    [InlineData(".h2\nline one\nline two", "line one\nline two", 2, null, "")]
    [InlineData(".c1", null, null, 1, "")]
    [InlineData(".C12", null, null, 12, "")]
    [InlineData(".prob", null, null, null, "prob")]
    [InlineData(".prob .review", null, null, null, "prob|review")]
    [InlineData(".prob a real comment", "a real comment", null, null, "prob")]
    [InlineData(".Prob .PROB .review", null, null, null, "Prob|review")]
    [InlineData(".h3 .c2 .prob a summary", "a summary", 3, 2, "prob")]
    [InlineData(".", ".", null, null, "")]
    [InlineData(". ", ".", null, null, "")]
    [InlineData("[my note] a legacy note", "[my note] a legacy note", null, null, "")]
    [InlineData("a plain note", "a plain note", null, null, "")]
    [InlineData(".prob a real comment with .dots", "a real comment with .dots", null, null, "prob")]
    [InlineData(".h0", ".h0", null, null, "")]
    [InlineData(".h4", ".h4", null, null, "")]
    public void Parse_SplitsMarkersFromNote(
        string input,
        string? expectedNote,
        int? expectedHeading,
        int? expectedStitch,
        string expectedTags)
    {
        var result = AnnotationMarkerParser.Parse(input);

        Assert.Equal(expectedNote, result.Note);
        Assert.Equal(expectedHeading, result.HeadingLevel);
        Assert.Equal(expectedStitch, result.StitchLevel);
        Assert.Equal(SplitTags(expectedTags), result.Tags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Parse_WhitespaceOnly_ReportsNoNoteAndNoMarkers(string input)
    {
        var result = AnnotationMarkerParser.Parse(input);

        Assert.Null(result.Note);
        Assert.Null(result.HeadingLevel);
        Assert.Null(result.StitchLevel);
        Assert.Empty(result.Tags);
        Assert.False(result.HasMarkers);
        Assert.Equal(AnnotationMarkers.Empty.Note, result.Note);
    }

    [Fact]
    public void Parse_Null_ReportsNoNoteAndNoMarkers()
    {
        var result = AnnotationMarkerParser.Parse(null);

        Assert.Null(result.Note);
        Assert.False(result.HasMarkers);
    }

    [Fact]
    public void Parse_HeadingShapedTokenThatIsNotALevel_IsNotAHeading()
    {
        // ".hx" is not ".h<1-3>", so it never declares a chapter however it is then classified.
        var result = AnnotationMarkerParser.Parse(".hx");

        Assert.Null(result.HeadingLevel);
    }

    [Fact]
    public void Parse_NoteKeepsItsInternalLineBreaks()
    {
        var result = AnnotationMarkerParser.Parse(".h1\nfirst line\n\nsecond paragraph");

        Assert.Equal("first line\n\nsecond paragraph", result.Note);
    }

    [Fact]
    public void Parse_LegacyPrefix_IsNeverAMarker()
    {
        var result = AnnotationMarkerParser.Parse("[my note] my thought");

        Assert.Equal("[my note] my thought", result.Note);
        Assert.False(result.HasMarkers);
    }

    private static IReadOnlyList<string> SplitTags(string tags)
        => tags.Length == 0 ? [] : tags.Split('|');
}
