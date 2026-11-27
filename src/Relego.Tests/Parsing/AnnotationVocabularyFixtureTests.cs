using Relego.Core.Parsing;
using Relego.Tests.Sources.Support;

namespace Relego.Tests.Parsing;

/// <summary>
/// Guards the markers of <c>Fixtures/annotation-vocabulary.txt</c>, the file the browser journey in
/// <c>relego.web/tests/annotations.spec.ts</c> imports. That journey asserts the rendered page
/// against the API payload, so it only proves anything while the fixture still declares a chapter,
/// a section, two tags and a two-fragment passage.
/// </summary>
public sealed class AnnotationVocabularyFixtureTests
{
    [Fact]
    public async Task TheVocabularyFixture_DeclaresHeadingsTagsAndATwoFragmentPassage()
    {
        var result = await ClippingsParser.ParseAsync(
            TestFixtures.AnnotationVocabularyFixturePath());

        var book = Assert.Single(result.Books);
        Assert.Equal("Relego Annotation Vocabulary", book.Title);
        Assert.Equal(6, book.Highlights.Count);

        var headings = book.Highlights
            .Select(highlight => AnnotationMarkerParser.Parse(highlight.Note).HeadingLevel)
            .ToList();
        Assert.Equal([1, 2, null, null, null, 3], headings);

        var passages = book.Highlights
            .Where(highlight => AnnotationMarkerParser.Parse(highlight.Note).StitchLevel is not null)
            .ToList();
        Assert.Equal(
            ["A stitched passage begins here", "and continues with a second fragment"],
            passages.Select(passage => passage.Text));
        Assert.Equal([1, 2], passages.Select(passage =>
            AnnotationMarkerParser.Parse(passage.Note).StitchLevel));

        var tagged = book.Highlights
            .Select(highlight => AnnotationMarkerParser.Parse(highlight.Note).Tags)
            .Where(tags => tags.Count > 0)
            .ToList();
        Assert.Equal(2, tagged.Count);
        Assert.Equal(["prob", "review"], tagged[0]);
        Assert.Equal(["prob"], tagged[1]);

        // The prose the reader wrote after the markers is the note, and the passage is untouched.
        var annotated = book.Highlights[2];
        Assert.Equal("Simplicity is prerequisite for reliability.", annotated.Text);
        Assert.Equal(
            "the whole design principle in one line",
            AnnotationMarkerParser.Parse(annotated.Note).Note);
    }
}
