using Relego.Core.Annotations;

namespace Relego.Tests.Annotations;

/// <summary>
/// Covers the chapter hierarchy derived from heading markers (FR-019 to FR-028, Story 2): nesting by
/// marker level in source order, the unknown chapter for annotations before the first marker and for
/// books without markers, chapters needing a title, and counts that include descendant chapters.
/// </summary>
public sealed class ChapterTreeBuilderTests
{
    private static readonly DateTimeOffset AddedOn = new(2026, 3, 4, 9, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void Build_ShallowerMarkerBeforeDeeperOne_NestsTheDeeperChapterAndJoinsBothTitlesInThePath()
    {
        var foundations = Heading(1, 1, "Foundations", "Location 100");
        var openingPassage = Passage(2, "The first highlight.", "Location 150");
        var fallacies = Heading(3, 2, "The Eight Fallacies", "Location 200");
        var sectionPassage = Passage(4, "A passage in the section.", "Location 250");

        var tree = ChapterTreeBuilder.Build([foundations, openingPassage, fallacies, sectionPassage]);

        var root = Assert.Single(tree.Chapters, chapter => chapter.Level == 1);
        var child = Assert.Single(tree.Chapters, chapter => chapter.Level == 2);
        Assert.Equal(foundations.Id, root.Id);
        Assert.Null(root.ParentId);
        Assert.Equal(fallacies.Id, child.Id);
        Assert.Equal(foundations.Id, child.ParentId);
        Assert.Equal<IEnumerable<string>>(["Foundations"], tree.Contexts[openingPassage.Id].ChapterPath);
        Assert.Equal<IEnumerable<string>>(
            ["Foundations", "The Eight Fallacies"],
            tree.Contexts[sectionPassage.Id].ChapterPath);
        Assert.Equal<IEnumerable<string>>(
            ["Foundations", "The Eight Fallacies"],
            tree.Contexts[fallacies.Id].ChapterPath);
        Assert.Equal(0, tree.UnknownChapterAnnotationCount);
    }

    [Fact]
    public void Build_DeeperMarkerBeforeAnyShallowerOne_IsTopLevelAndKeepsNestingTheNextDeeperMarker()
    {
        var beforeAnyChapter = Passage(1, "Read before the first marker.", "Location 50");
        var warmUp = Heading(2, 2, "Warm-up", "Location 100");
        var warmUpPassage = Passage(3, "A warm-up passage.", "Location 150");
        var details = Heading(4, 3, "Details", "Location 200");
        var mainArgument = Heading(5, 1, "The Main Argument", "Location 300");

        var tree = ChapterTreeBuilder.Build([beforeAnyChapter, warmUp, warmUpPassage, details, mainArgument]);

        Assert.True(tree.HasTableOfContents);
        Assert.Equal(3, tree.Chapters.Count);
        var byId = tree.Chapters.ToDictionary(chapter => chapter.Id);
        Assert.Null(byId[warmUp.Id].ParentId);
        Assert.Null(byId[mainArgument.Id].ParentId);
        Assert.Equal(warmUp.Id, byId[details.Id].ParentId);
        Assert.Equal<IEnumerable<string>>(["Warm-up", "Details"], tree.Contexts[details.Id].ChapterPath);
        Assert.Equal(warmUp.Id, tree.Contexts[warmUpPassage.Id].ChapterId);
        Assert.Equal(1, tree.UnknownChapterAnnotationCount);
    }

    [Fact]
    public void Build_RaisingAMarkerLevel_ReparentsItsDescendantsUnderTheirNewTopLevelChapter()
    {
        var partOne = Heading(1, 1, "Part One", "Location 100");
        var section = Heading(2, 2, "The Section", "Location 200");
        var subsection = Heading(3, 3, "The Subsection", "Location 300");

        var nested = ChapterTreeBuilder.Build([partOne, section, subsection]);
        var nestedById = nested.Chapters.ToDictionary(chapter => chapter.Id);
        Assert.Equal(section.Id, nestedById[subsection.Id].ParentId);

        var promoted = ChapterTreeBuilder.Build([partOne, section with { HeadingLevel = 1 }, subsection]);
        var promotedById = promoted.Chapters.ToDictionary(chapter => chapter.Id);
        Assert.Null(promotedById[section.Id].ParentId);
        Assert.Equal(section.Id, promotedById[subsection.Id].ParentId);
    }

    [Fact]
    public void Build_HeadingMarkerWithoutPassageText_CreatesNoChapterButKeepsTheAnnotation()
    {
        var untitledMarker = new ChapterSourceAnnotation
        {
            Id = 1,
            Text = string.Empty,
            Note = "A section summary with no title to show.",
            HeadingLevel = 1,
            Position = "Location 100",
        };
        var passage = Passage(2, "An unrelated highlight.", "Location 150");

        var tree = ChapterTreeBuilder.Build([untitledMarker, passage]);

        Assert.Empty(tree.Chapters);
        Assert.False(tree.HasTableOfContents);
        Assert.Equal(2, tree.UnknownChapterAnnotationCount);
        var markerContext = tree.Contexts[untitledMarker.Id];
        Assert.Null(markerContext.ChapterId);
        Assert.Empty(markerContext.ChapterPath);
        var passageContext = tree.Contexts[passage.Id];
        Assert.Null(passageContext.ChapterId);
        Assert.Empty(passageContext.ChapterPath);
    }

    [Fact]
    public void Build_HeadingMarkerWithNote_BecomesTheChapterNoteWithItsProvenance()
    {
        var title = new ChapterSourceAnnotation
        {
            Id = 1,
            Text = "The Rediscovery of the Commons",
            Note = "written in 1993 and still true",
            HeadingLevel = 1,
            Position = "Location 42-58",
            AddedOn = AddedOn,
        };

        var tree = ChapterTreeBuilder.Build([title]);

        var chapter = Assert.Single(tree.Chapters);
        Assert.Equal("written in 1993 and still true", chapter.Note);
        Assert.Equal("Location 42-58", chapter.Position);
        Assert.Equal<DateTimeOffset?>(AddedOn, chapter.AddedOn);
    }

    [Fact]
    public void Build_BookWithoutHeadingMarkers_ReportsEveryAnnotationInTheUnknownChapter()
    {
        var first = Passage(1, "The first highlight.", "Location 10");
        var second = Passage(2, "The second highlight.", "Location 20");
        var third = Passage(3, "The third highlight.", "Location 30");

        var tree = ChapterTreeBuilder.Build([first, second, third]);

        Assert.Empty(tree.Chapters);
        Assert.False(tree.HasTableOfContents);
        Assert.Equal(3, tree.UnknownChapterAnnotationCount);
        Assert.Equal<IEnumerable<int>>([1, 2, 3], tree.Contexts.Keys.OrderBy(id => id));
        foreach (var context in tree.Contexts.Values)
        {
            Assert.Null(context.ChapterId);
            Assert.Empty(context.ChapterPath);
        }
    }

    [Fact]
    public void Build_AnnotationsArrivingOutOfOrder_AreAssignedInReadingOrder()
    {
        var third = Heading(1, 1, "Third", "Location 300");
        var first = Heading(2, 1, "First", "Location 100");
        var second = Heading(3, 1, "Second", "Location 200");
        var betweenFirstTwo = Passage(4, "Read between the first two chapters.", "Location 150");

        var tree = ChapterTreeBuilder.Build([third, first, second, betweenFirstTwo]);

        Assert.Equal<IEnumerable<string>>(
            ["First", "Second", "Third"],
            tree.Chapters.Select(chapter => chapter.Title));
        Assert.Equal(first.Id, tree.Contexts[first.Id].ChapterId);
        Assert.Equal(second.Id, tree.Contexts[second.Id].ChapterId);
        Assert.Equal(third.Id, tree.Contexts[third.Id].ChapterId);
        Assert.Equal(first.Id, tree.Contexts[betweenFirstTwo.Id].ChapterId);
        Assert.Equal(0, tree.UnknownChapterAnnotationCount);
    }

    [Fact]
    public void Build_Counts_IncludeTheTitlingMarkerAndEveryDescendantChaptersAnnotations()
    {
        var beforeAnyChapter = Passage(1, "An introduction highlighted first.", "Location 50");
        var chapterTitle = Heading(2, 1, "The Chapter", "Location 100");
        var chapterPassage = Passage(3, "A passage in the chapter.", "Location 150");
        var sectionTitle = Heading(4, 2, "The Section", "Location 200");
        var firstSectionPassage = Passage(5, "First section passage.", "Location 250");
        var secondSectionPassage = Passage(6, "Second section passage.", "Location 300");

        var tree = ChapterTreeBuilder.Build(
        [
            beforeAnyChapter,
            chapterTitle,
            chapterPassage,
            sectionTitle,
            firstSectionPassage,
            secondSectionPassage,
        ]);

        var byId = tree.Chapters.ToDictionary(chapter => chapter.Id);
        Assert.Equal(3, byId[sectionTitle.Id].AnnotationCount);
        Assert.Equal(5, byId[chapterTitle.Id].AnnotationCount);
        Assert.Equal(1, tree.UnknownChapterAnnotationCount);
        Assert.Equal(chapterTitle.Id, tree.Contexts[chapterPassage.Id].ChapterId);
        Assert.Equal(sectionTitle.Id, tree.Contexts[secondSectionPassage.Id].ChapterId);
        Assert.Equal<IEnumerable<int>>(
            [chapterTitle.Id, sectionTitle.Id],
            tree.WithDescendants(chapterTitle.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_TwoChaptersSharingATitle_StayDistinctBecauseIdentityIsTheMarkerId()
    {
        var firstNotes = Heading(1, 2, "Notes", "Location 100");
        var secondNotes = Heading(2, 2, "Notes", "Location 200");

        var tree = ChapterTreeBuilder.Build([firstNotes, secondNotes]);

        Assert.Equal(2, tree.Chapters.Count);
        Assert.Equal<IEnumerable<int>>([firstNotes.Id, secondNotes.Id], tree.Chapters.Select(chapter => chapter.Id));
        Assert.Equal<IEnumerable<string>>(["Notes", "Notes"], tree.Chapters.Select(chapter => chapter.Title));
        Assert.NotEqual(tree.Chapters[0].Id, tree.Chapters[1].Id);
        Assert.Equal(firstNotes.Id, tree.Contexts[firstNotes.Id].ChapterId);
        Assert.Equal(secondNotes.Id, tree.Contexts[secondNotes.Id].ChapterId);
        Assert.False(tree.HasTableOfContents);
    }

    private static ChapterSourceAnnotation Heading(int id, int level, string title, string position) =>
        new()
        {
            Id = id,
            Text = title,
            HeadingLevel = level,
            Position = position,
        };

    private static ChapterSourceAnnotation Passage(int id, string text, string position) =>
        new()
        {
            Id = id,
            Text = text,
            Position = position,
        };
}
