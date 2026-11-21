namespace Relego.Core.Annotations;

/// <summary>
/// A stored annotation as the chapter derivation sees it. The builder reads only what a chapter
/// needs, so callers can feed it rows loaded for a single book.
/// </summary>
public sealed record ChapterSourceAnnotation
{
    /// <summary>Stored annotation id; also the identity of a chapter it titles.</summary>
    public required int Id { get; init; }

    /// <summary>Passage text; a heading annotation uses it as the chapter title.</summary>
    public required string Text { get; init; }

    /// <summary>Reader note, which becomes a chapter's section summary.</summary>
    public string? Note { get; init; }

    /// <summary>Heading level (1 to 3) the annotation declares, or <c>null</c>.</summary>
    public int? HeadingLevel { get; init; }

    /// <summary>Raw source position, used both to order the book and as chapter provenance.</summary>
    public string? Position { get; init; }

    /// <summary>Source timestamp, used to order a book that reports no consistent position.</summary>
    public DateTimeOffset? AddedOn { get; init; }
}

/// <summary>
/// Where a single annotation sits in its book's chapter structure. The unknown chapter is
/// represented by a <c>null</c> <see cref="ChapterId"/> and an empty <see cref="ChapterPath"/>.
/// </summary>
public sealed record AnnotationContext
{
    /// <summary>Id of the annotation that titles the chapter, or <c>null</c> for the unknown chapter.</summary>
    public int? ChapterId { get; init; }

    /// <summary>Chapter titles from the root chapter down to the current one; empty for the unknown chapter.</summary>
    public IReadOnlyList<string> ChapterPath { get; init; } = [];
}

/// <summary>A chapter derived from a heading annotation. Never persisted.</summary>
public sealed record Chapter
{
    /// <summary>Id of the annotation that titles the chapter, never the title itself.</summary>
    public required int Id { get; init; }

    /// <summary>Chapter title, taken from the titling annotation's passage text.</summary>
    public required string Title { get; init; }

    /// <summary>Heading level, 1 to 3.</summary>
    public required int Level { get; init; }

    /// <summary>Nearest preceding chapter with a strictly smaller level, or <c>null</c> at the top level.</summary>
    public int? ParentId { get; init; }

    /// <summary>Section summary the titling annotation carries, or <c>null</c>.</summary>
    public string? Note { get; init; }

    /// <summary>Source position the titling annotation carries, or <c>null</c>.</summary>
    public string? Position { get; init; }

    /// <summary>Source timestamp the titling annotation carries, or <c>null</c>.</summary>
    public DateTimeOffset? AddedOn { get; init; }

    /// <summary>Number of annotations in this chapter, including every descendant chapter's.</summary>
    public int AnnotationCount { get; init; }
}

/// <summary>
/// The chapter hierarchy of one book, derived from its heading annotations. The hierarchy is
/// computed per book and never stored, so re-importing earlier content re-derives it.
/// </summary>
public sealed class ChapterTree
{
    private readonly IReadOnlyDictionary<int, IReadOnlyList<int>> _childIdsByParentId;
    private readonly HashSet<int> _chapterIds;

    internal ChapterTree(
        IReadOnlyList<Chapter> chapters,
        IReadOnlyDictionary<int, AnnotationContext> contexts,
        int unknownChapterAnnotationCount)
    {
        Chapters = chapters;
        Contexts = contexts;
        UnknownChapterAnnotationCount = unknownChapterAnnotationCount;
        HasTableOfContents = chapters.Any(chapter => chapter.Level == 1);
        _chapterIds = [.. chapters.Select(chapter => chapter.Id)];
        _childIdsByParentId = chapters
            .Where(chapter => chapter.ParentId is not null)
            .GroupBy(chapter => chapter.ParentId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<int>)[.. group.Select(chapter => chapter.Id)]);
    }

    /// <summary>Chapters in reading order, with their nesting expressed by <see cref="Chapter.ParentId"/>.</summary>
    public IReadOnlyList<Chapter> Chapters { get; }

    /// <summary>Chapter context for every annotation of the book, keyed by annotation id.</summary>
    public IReadOnlyDictionary<int, AnnotationContext> Contexts { get; }

    /// <summary>Number of annotations that precede the first chapter and therefore sit in the unknown chapter.</summary>
    public int UnknownChapterAnnotationCount { get; }

    /// <summary>
    /// Whether a table of contents is offered: at least one level-1 heading exists. Deeper markers
    /// still render as headings, but they alone do not make a navigation.
    /// </summary>
    public bool HasTableOfContents { get; }

    /// <summary>
    /// Returns the given chapter and every chapter nested beneath it, which is the set of
    /// annotations its descendant-inclusive count already covers.
    /// </summary>
    /// <param name="chapterId">Chapter whose section is wanted.</param>
    /// <returns>The chapter id and its descendants; empty when the chapter is unknown.</returns>
    public IReadOnlySet<int> WithDescendants(int chapterId)
    {
        var result = new HashSet<int>();

        if (!_chapterIds.Contains(chapterId))
            return result;

        var pending = new Stack<int>();
        pending.Push(chapterId);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            if (!result.Add(current))
                continue;

            if (_childIdsByParentId.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                    pending.Push(child);
            }
        }

        return result;
    }
}

/// <summary>
/// Turns a book's stored annotations into its chapter hierarchy. Every consumer reads chapters
/// through this builder, so the shared list, the book view and delivery cannot disagree.
/// </summary>
public static class ChapterTreeBuilder
{
    /// <summary>
    /// Derives the chapter hierarchy of a single book from its heading annotations.
    /// </summary>
    /// <param name="annotations">Every annotation of the book, in any arrival order.</param>
    /// <returns>The derived hierarchy and the chapter context of each annotation.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="annotations"/> is <c>null</c>.</exception>
    public static ChapterTree Build(IEnumerable<ChapterSourceAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var ordered = SourceOrder.Apply(
            annotations,
            annotation =>
            {
                var position = SourcePosition.Parse(annotation.Position);
                return new AnnotationOrderKey(position.Kind, position.Start, annotation.AddedOn, annotation.Id);
            });

        var chapters = new List<Chapter>();
        var parentIdByChapterId = new Dictionary<int, int?>();
        var pathByChapterId = new Dictionary<int, IReadOnlyList<string>>();
        var directCountByChapterId = new Dictionary<int, int>();
        var contexts = new Dictionary<int, AnnotationContext>(ordered.Count);

        Chapter? current = null;
        var unknownCount = 0;

        foreach (var annotation in ordered)
        {
            if (IsChapterTitle(annotation))
            {
                var level = annotation.HeadingLevel!.Value;
                // "Nearest preceding chapter with a strictly smaller level" reads backwards from
                // the chapter being added, which is why the list is already in reading order.
                var parent = chapters.LastOrDefault(chapter => chapter.Level < level);
                var path = parent is null
                    ? (IReadOnlyList<string>)[annotation.Text]
                    : [.. pathByChapterId[parent.Id], annotation.Text];

                var chapter = new Chapter
                {
                    Id = annotation.Id,
                    Title = annotation.Text,
                    Level = level,
                    ParentId = parent?.Id,
                    Note = annotation.Note,
                    Position = annotation.Position,
                    AddedOn = annotation.AddedOn,
                };

                chapters.Add(chapter);
                parentIdByChapterId[chapter.Id] = chapter.ParentId;
                pathByChapterId[chapter.Id] = path;
                directCountByChapterId[chapter.Id] = 0;
                current = chapter;
            }

            if (current is null)
            {
                unknownCount++;
                contexts[annotation.Id] = new AnnotationContext();
            }
            else
            {
                directCountByChapterId[current.Id]++;
                contexts[annotation.Id] = new AnnotationContext
                {
                    ChapterId = current.Id,
                    ChapterPath = pathByChapterId[current.Id],
                };
            }
        }

        var childIdsByParentId = parentIdByChapterId
            .Where(pair => pair.Value is not null)
            .GroupBy(pair => pair.Value!.Value)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToList());

        var annotationCountByChapterId = new Dictionary<int, int>();
        foreach (var chapter in chapters)
            CountWithDescendants(chapter.Id, directCountByChapterId, childIdsByParentId, annotationCountByChapterId);

        var result = chapters
            .Select(chapter => chapter with { AnnotationCount = annotationCountByChapterId[chapter.Id] })
            .ToList();

        return new ChapterTree(result, contexts, unknownCount);
    }

    private static bool IsChapterTitle(ChapterSourceAnnotation annotation) =>
        annotation.HeadingLevel is >= 1 and <= 3 && !string.IsNullOrWhiteSpace(annotation.Text);

    private static int CountWithDescendants(
        int chapterId,
        IReadOnlyDictionary<int, int> directCountByChapterId,
        IReadOnlyDictionary<int, List<int>> childIdsByParentId,
        IDictionary<int, int> totals)
    {
        if (totals.TryGetValue(chapterId, out var known))
            return known;

        var total = directCountByChapterId[chapterId];

        if (childIdsByParentId.TryGetValue(chapterId, out var children))
            total += children.Sum(child => CountWithDescendants(child, directCountByChapterId, childIdsByParentId, totals));

        totals[chapterId] = total;
        return total;
    }
}
