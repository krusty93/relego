using Relego.Core.Annotations;

namespace Relego.Tests.Annotations;

public sealed class StitchedPassageBuilderTests
{
    private const int BookId = 7;
    private const int OtherBookId = 8;

    private static readonly DateTimeOffset AddedOn = new(2026, 3, 4, 9, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Build_ChainedMarkersInOneBook_JoinTheirTextsInReadingOrder()
    {
        var opening = Fragment(5, BookId, "The opening fragment.", 1, "c1", position: "Location 120");
        var continuation = Fragment(2, BookId, "A later continuation.", 2, "c2", position: "Location 480");

        // Arrival order is reversed and the ids disagree with reading order: position must win.
        var index = StitchedPassageBuilder.Build([continuation, opening]);

        var passage = Assert.Single(index.Passages);
        Assert.Equal(opening.Id, passage.Id);
        Assert.Equal<IEnumerable<int>>([opening.Id, continuation.Id], passage.Fragments.Select(fragment => fragment.Id));
        Assert.Equal("The opening fragment. … A later continuation.", passage.Text);

        var openingMembership = index.Memberships[opening.Id];
        Assert.Equal(passage.Id, openingMembership.GroupId);
        Assert.Equal(0, openingMembership.Index);
        Assert.Equal(passage.Text, openingMembership.StitchedText);

        var continuationMembership = index.Memberships[continuation.Id];
        Assert.Equal(passage.Id, continuationMembership.GroupId);
        Assert.Equal(1, continuationMembership.Index);
        Assert.Null(continuationMembership.StitchedText);

        // A lone fragment keeps its text untouched: no separator, no trailing ellipsis.
        var lone = Fragment(9, OtherBookId, "A lone fragment.", 1, "c1");
        var lonePassage = Assert.Single(StitchedPassageBuilder.Build([lone]).Passages);
        Assert.Equal("A lone fragment.", lonePassage.Text);
        Assert.DoesNotContain("…", lonePassage.Text);
    }

    [Fact]
    public void Build_MarkerWithoutPrecedingOpeningMarker_IsNotStitchedAndSurfacesAsATag()
    {
        var orphanContinuation = Fragment(1, BookId, "A stray continuation.", 2, "C2");
        var orphanDeeper = Fragment(2, BookId, "Another stray continuation.", 3);
        var opening = Fragment(3, BookId, "The opening fragment.", 1, "c1");
        var continuation = Fragment(4, BookId, "The real continuation.", 2, "c2");

        var index = StitchedPassageBuilder.Build([orphanDeeper, continuation, orphanContinuation, opening]);

        Assert.Equal(2, index.OrphanMarkerTags.Count);
        Assert.Equal("C2", index.OrphanMarkerTags[orphanContinuation.Id]);
        Assert.Equal("c3", index.OrphanMarkerTags[orphanDeeper.Id]);
        Assert.False(index.Memberships.ContainsKey(orphanContinuation.Id));
        Assert.False(index.Memberships.ContainsKey(orphanDeeper.Id));

        var passage = Assert.Single(index.Passages);
        Assert.Equal(opening.Id, passage.Id);
        Assert.Equal<IEnumerable<int>>([opening.Id, continuation.Id], passage.Fragments.Select(fragment => fragment.Id));
        Assert.Equal("The opening fragment. … The real continuation.", passage.Text);
    }

    [Fact]
    public void Build_TwoRunsOfOpeningMarkers_ProduceTwoPassages()
    {
        var firstOpening = Fragment(1, BookId, "First run opening.", 1, "c1");
        var firstContinuation = Fragment(2, BookId, "First run continuation.", 2, "c2");
        var secondOpening = Fragment(3, BookId, "Second run opening.", 1, "c1");
        var secondContinuation = Fragment(4, BookId, "Second run continuation.", 2, "c2");

        var index = StitchedPassageBuilder.Build(
            [firstOpening, firstContinuation, secondOpening, secondContinuation]);

        Assert.Equal(2, index.Passages.Count);
        Assert.Equal<IEnumerable<int>>([firstOpening.Id, secondOpening.Id], index.Passages.Select(passage => passage.Id));

        var firstPassage = index.Passages[0];
        Assert.Equal<IEnumerable<int>>(
            [firstOpening.Id, firstContinuation.Id],
            firstPassage.Fragments.Select(fragment => fragment.Id));
        Assert.Equal("First run opening. … First run continuation.", firstPassage.Text);

        var secondPassage = index.Passages[1];
        Assert.Equal<IEnumerable<int>>(
            [secondOpening.Id, secondContinuation.Id],
            secondPassage.Fragments.Select(fragment => fragment.Id));
        Assert.Equal("Second run opening. … Second run continuation.", secondPassage.Text);

        var firstMembership = index.Memberships[firstOpening.Id];
        Assert.Equal(firstPassage.Id, firstMembership.GroupId);
        Assert.Equal(0, firstMembership.Index);
        Assert.Equal(firstPassage.Text, firstMembership.StitchedText);
        Assert.Null(index.Memberships[firstContinuation.Id].StitchedText);

        var secondMembership = index.Memberships[secondOpening.Id];
        Assert.Equal(secondPassage.Id, secondMembership.GroupId);
        Assert.Equal(0, secondMembership.Index);
        Assert.Equal(secondPassage.Text, secondMembership.StitchedText);
        Assert.Null(index.Memberships[secondContinuation.Id].StitchedText);
    }

    [Fact]
    public void Build_RemovingAContinuationMarker_ReflectsTheChangeWithoutLosingOtherFragments()
    {
        var firstOpening = Fragment(1, BookId, "First run opening.", 1, "c1");
        var firstContinuation = Fragment(2, BookId, "First run continuation.", 2, "c2");
        var secondOpening = Fragment(3, BookId, "Second run opening.", 1, "c1");
        var secondContinuation = Fragment(4, BookId, "Second run continuation.", 2, "c2");
        var annotations = new[] { firstOpening, firstContinuation, secondOpening, secondContinuation };

        var before = StitchedPassageBuilder.Build(annotations);
        Assert.Equal(2, before.Passages[0].Fragments.Count);

        // The reader drops the .c2 marker, so the annotation is re-derived as unmarked.
        var withoutMarker = annotations.Select(annotation =>
            annotation.Id == firstContinuation.Id ? annotation with { StitchLevel = null } : annotation);
        var after = StitchedPassageBuilder.Build(withoutMarker);

        Assert.Equal(2, after.Passages.Count);
        var firstPassage = after.Passages[0];
        var onlyFragment = Assert.Single(firstPassage.Fragments);
        Assert.Equal(firstOpening.Id, onlyFragment.Id);
        Assert.Equal(firstOpening.Text, firstPassage.Text);
        Assert.False(after.Memberships.ContainsKey(firstContinuation.Id));

        // Only the fragment that lost its marker leaves the derived output; the others keep their place.
        var remaining = after.Passages
            .SelectMany(passage => passage.Fragments)
            .Select(fragment => fragment.Id)
            .Concat(after.Memberships.Keys)
            .Distinct()
            .OrderBy(id => id);
        Assert.Equal<IEnumerable<int>>([firstOpening.Id, secondOpening.Id, secondContinuation.Id], remaining);
    }

    [Fact]
    public void Build_ExcludedFragment_IsOmittedFromTheJoinedTextButStaysAMember()
    {
        var opening = Fragment(1, BookId, "The opening fragment.", 1, "c1");
        var excluded = Fragment(2, BookId, "An excluded fragment.", 2, "c2", excluded: true);
        var closing = Fragment(3, BookId, "The closing fragment.", 3, "c3");

        var index = StitchedPassageBuilder.Build([opening, excluded, closing]);

        var passage = Assert.Single(index.Passages);
        Assert.Equal("The opening fragment. … The closing fragment.", passage.Text);
        Assert.Equal(3, passage.Fragments.Count);
        Assert.Equal(excluded.Id, passage.Fragments[1].Id);
        Assert.True(passage.Fragments[1].Excluded);

        var membership = index.Memberships[excluded.Id];
        Assert.Equal(opening.Id, membership.GroupId);
        Assert.Equal(1, membership.Index);
        Assert.Null(membership.StitchedText);
    }

    [Fact]
    public void Build_ContinuationMarkerInAnotherBook_IsNeverJoinedToTheOpeningPassage()
    {
        var opening = Fragment(1, BookId, "The opening fragment.", 1, "c1");
        var otherBookContinuation = Fragment(2, OtherBookId, "A continuation in the other book.", 2, "c2");
        var otherBookOpening = Fragment(3, OtherBookId, "The other book's opening.", 1, "c1");

        var index = StitchedPassageBuilder.Build([opening, otherBookContinuation, otherBookOpening]);

        Assert.Equal(2, index.Passages.Count);

        var bookPassage = index.Passages[0];
        Assert.Equal(opening.Id, bookPassage.Id);
        Assert.Equal("The opening fragment.", bookPassage.Text);

        var otherBookPassage = index.Passages[1];
        Assert.Equal(otherBookOpening.Id, otherBookPassage.Id);
        Assert.Equal("The other book's opening.", otherBookPassage.Text);

        Assert.Equal("c2", index.OrphanMarkerTags[otherBookContinuation.Id]);
        Assert.False(index.Memberships.ContainsKey(otherBookContinuation.Id));
    }

    private static StitchSourceAnnotation Fragment(
        int id,
        int bookId,
        string text,
        int? level = null,
        string? marker = null,
        bool excluded = false,
        string? position = null) =>
        new()
        {
            Id = id,
            BookId = bookId,
            Text = text,
            StitchLevel = level,
            StitchMarker = marker,
            Excluded = excluded,
            Position = position,
            AddedOn = AddedOn.AddMinutes(id),
        };
}
