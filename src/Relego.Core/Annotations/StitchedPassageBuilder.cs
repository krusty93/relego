namespace Relego.Core.Annotations;

/// <summary>
/// A stored annotation as the stitched-passage derivation sees it. Every field travels to the API
/// unchanged, so a fragment keeps its own text, note, tags, provenance and delivery state.
/// </summary>
public sealed record StitchSourceAnnotation
{
    /// <summary>Stored annotation id.</summary>
    public required int Id { get; init; }

    /// <summary>Owning book, which bounds a passage: stitching never crosses books.</summary>
    public required int BookId { get; init; }

    /// <summary>Passage text; empty for a note that carries no highlighted text.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Reader note with its markers stripped, or <c>null</c>.</summary>
    public string? Note { get; init; }

    /// <summary>Stitch level (1 or greater) from <see cref="AnnotationMarkers.StitchLevel"/>, or <c>null</c>.</summary>
    public int? StitchLevel { get; init; }

    /// <summary>
    /// Stitch marker as the reader wrote it, without the leading dot, or <c>null</c>. Used to
    /// surface an orphaned marker as a tag in the reader's own spelling.
    /// </summary>
    public string? StitchMarker { get; init; }

    /// <summary>Tags written in the note, in the reader's spelling; never <c>null</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Raw source position, or <c>null</c>.</summary>
    public string? Position { get; init; }

    /// <summary>Source timestamp, or <c>null</c>.</summary>
    public DateTimeOffset? AddedOn { get; init; }

    /// <summary>Recap weight of the annotation.</summary>
    public int Weight { get; init; }

    /// <summary>Whether the annotation is excluded from delivery.</summary>
    public bool Excluded { get; init; }

    /// <summary>How many recaps already delivered the annotation.</summary>
    public int DeliveryCount { get; init; }
}

/// <summary>One annotation inside a stitched passage, with its own text and provenance intact.</summary>
public sealed record StitchedFragment
{
    /// <summary>Stored annotation id.</summary>
    public required int Id { get; init; }

    /// <summary>Zero-based position of the fragment within its passage.</summary>
    public required int Index { get; init; }

    /// <summary>Passage text of this fragment alone.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Reader note of this fragment alone, or <c>null</c>.</summary>
    public string? Note { get; init; }

    /// <summary>Tags of this fragment alone; never <c>null</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Raw source position of this fragment, or <c>null</c>.</summary>
    public string? Position { get; init; }

    /// <summary>Source timestamp of this fragment, or <c>null</c>.</summary>
    public DateTimeOffset? AddedOn { get; init; }

    /// <summary>Recap weight of this fragment.</summary>
    public int Weight { get; init; }

    /// <summary>Whether this fragment is excluded from delivery.</summary>
    public bool Excluded { get; init; }

    /// <summary>How many recaps already delivered this fragment.</summary>
    public int DeliveryCount { get; init; }
}

/// <summary>
/// A passage derived from one <c>.c1</c> marker and the <c>.c2</c>, <c>.c3</c> … markers that
/// follow it in the same book. Never persisted.
/// </summary>
public sealed record StitchedPassage
{
    /// <summary>Id of the group's first fragment, which is also the group's identity in the API.</summary>
    public required int Id { get; init; }

    /// <summary>Fragments in reading order; never empty.</summary>
    public required IReadOnlyList<StitchedFragment> Fragments { get; init; }

    /// <summary>
    /// Text of the fragments joined with <c>" … "</c>, leaving out excluded and empty ones; one
    /// remaining fragment is returned without an ellipsis, and <c>null</c> when nothing is left.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>Note of the first fragment, or <c>null</c>.</summary>
    public string? Note { get; init; }

    /// <summary>Union of the fragments' tags in first-seen order; never <c>null</c>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>Where one annotation sits inside its stitched passage.</summary>
public sealed record StitchMembership
{
    /// <summary>Id of the group's first fragment.</summary>
    public required int GroupId { get; init; }

    /// <summary>Zero-based position of the annotation within the group.</summary>
    public required int Index { get; init; }

    /// <summary>Joined passage text, set on the group's first fragment only; <c>null</c> otherwise.</summary>
    public string? StitchedText { get; init; }
}

/// <summary>
/// The stitched passages of the given books plus the per-annotation lookups the API needs.
/// </summary>
public sealed class StitchIndex
{
    internal StitchIndex(
        IReadOnlyList<StitchedPassage> passages,
        IReadOnlyDictionary<int, StitchMembership> memberships,
        IReadOnlyDictionary<int, string> orphanMarkerTags)
    {
        Passages = passages;
        Memberships = memberships;
        OrphanMarkerTags = orphanMarkerTags;
    }

    /// <summary>Derived passages, ordered by book and then reading order.</summary>
    public IReadOnlyList<StitchedPassage> Passages { get; }

    /// <summary>Membership of every stitched annotation, keyed by annotation id.</summary>
    public IReadOnlyDictionary<int, StitchMembership> Memberships { get; }

    /// <summary>
    /// Tags surfaced by an orphaned <c>.cN</c> marker — one with no preceding <c>.c1</c> in the
    /// same book — keyed by annotation id (Story 6 AS-2).
    /// </summary>
    public IReadOnlyDictionary<int, string> OrphanMarkerTags { get; }
}

/// <summary>
/// Turns a book's stored annotations into stitched passages. Every consumer reads passages through
/// this builder, so the list and delivery cannot disagree.
/// </summary>
public static class StitchedPassageBuilder
{
    private const string FragmentSeparator = " … ";

    /// <summary>
    /// Derives the stitched passages of the given books. A <c>.c1</c> marker starts a passage, a
    /// later <c>.c2</c> or deeper marker joins the open passage, and an unmarked annotation leaves
    /// the open passage alone. An orphaned <c>.cN</c> is not stitched: its marker surfaces as a tag.
    /// </summary>
    /// <param name="annotations">Annotations of any number of books, in any arrival order.</param>
    /// <returns>The derived passages and the per-annotation lookups.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="annotations"/> is <c>null</c>.</exception>
    public static StitchIndex Build(IEnumerable<StitchSourceAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var passages = new List<StitchedPassage>();
        var memberships = new Dictionary<int, StitchMembership>();
        var orphanMarkerTags = new Dictionary<int, string>();

        foreach (var book in annotations.GroupBy(annotation => annotation.BookId).OrderBy(group => group.Key))
        {
            var ordered = SourceOrder.Apply(
                book,
                annotation =>
                {
                    var position = SourcePosition.Parse(annotation.Position);
                    return new AnnotationOrderKey(position.Kind, position.Start, annotation.AddedOn, annotation.Id);
                });

            var groups = new List<List<StitchSourceAnnotation>>();
            List<StitchSourceAnnotation>? open = null;

            foreach (var annotation in ordered)
            {
                var level = annotation.StitchLevel;

                if (level == 1)
                {
                    open = [annotation];
                    groups.Add(open);
                    continue;
                }

                if (level is null or < 2)
                    continue;

                if (open is null)
                {
                    orphanMarkerTags[annotation.Id] = annotation.StitchMarker ?? $"c{level.Value}";
                    continue;
                }

                open.Add(annotation);
            }

            foreach (var group in groups)
            {
                var fragments = group
                    .Select((annotation, index) => ToFragment(annotation, index))
                    .ToList();
                var passage = new StitchedPassage
                {
                    Id = fragments[0].Id,
                    Fragments = fragments,
                    Text = JoinText(fragments),
                    Note = fragments[0].Note,
                    Tags = UnionTags(fragments),
                };

                passages.Add(passage);

                for (var index = 0; index < fragments.Count; index++)
                {
                    memberships[fragments[index].Id] = new StitchMembership
                    {
                        GroupId = passage.Id,
                        Index = index,
                        StitchedText = index == 0 ? passage.Text : null,
                    };
                }
            }
        }

        return new StitchIndex(passages, memberships, orphanMarkerTags);
    }

    private static StitchedFragment ToFragment(StitchSourceAnnotation annotation, int index) => new()
    {
        Id = annotation.Id,
        Index = index,
        Text = annotation.Text,
        Note = annotation.Note,
        Tags = annotation.Tags,
        Position = annotation.Position,
        AddedOn = annotation.AddedOn,
        Weight = annotation.Weight,
        Excluded = annotation.Excluded,
        DeliveryCount = annotation.DeliveryCount,
    };

    private static string? JoinText(IReadOnlyList<StitchedFragment> fragments)
    {
        var texts = fragments
            .Where(fragment => !fragment.Excluded && !string.IsNullOrWhiteSpace(fragment.Text))
            .Select(fragment => fragment.Text);

        var joined = string.Join(FragmentSeparator, texts);
        return joined.Length == 0 ? null : joined;
    }

    private static IReadOnlyList<string> UnionTags(IReadOnlyList<StitchedFragment> fragments)
    {
        var tags = new List<string>();

        foreach (var tag in fragments.SelectMany(fragment => fragment.Tags))
        {
            if (!tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
                tags.Add(tag);
        }

        return tags;
    }
}
