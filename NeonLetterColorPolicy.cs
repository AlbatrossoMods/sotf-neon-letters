#nullable enable

namespace SOTFNeonLetters;

public readonly record struct NeonRgba(float Red, float Green, float Blue, float Alpha)
{
    public static NeonRgba ProjectCyan { get; } = new(0f, 1f, 1f, 1f);
}

public readonly record struct NeonLetterColorDecision(NeonRgba Color, bool ShouldPersist);

public sealed class NeonLetterColorEditor
{
    public NeonLetterColorEditor(NeonRgba original)
    {
        Original = original;
        Preview = original;
        Committed = original;
    }

    public NeonRgba Original { get; }
    public NeonRgba Preview { get; private set; }
    public NeonRgba Committed { get; private set; }

    public void SetPreview(NeonRgba color)
    {
        Preview = color;
    }

    public NeonLetterColorDecision Apply()
    {
        Committed = Preview;
        return new NeonLetterColorDecision(Committed, true);
    }

    public NeonLetterColorDecision Cancel()
    {
        Preview = Original;
        Committed = Original;
        return new NeonLetterColorDecision(Original, false);
    }

    public void Reset()
    {
        Preview = NeonRgba.ProjectCyan;
    }
}

public sealed class NeonLetterColorSaveEnvelope
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<NeonLetterColorSaveEntry> Entries { get; set; } = new();
}

public sealed class NeonLetterColorSaveEntry
{
    public NeonLetterColorSaveEntry()
    {
    }

    public NeonLetterColorSaveEntry(int saveId, int recipeId, NeonRgba color)
    {
        SaveId = saveId;
        RecipeId = recipeId;
        Color = color;
    }

    public int SaveId { get; set; }
    public int RecipeId { get; set; }
    public NeonRgba Color { get; set; }
}

public static class NeonLetterColorStore
{
    public static void Upsert(
        NeonLetterColorSaveEnvelope envelope,
        NeonLetterColorSaveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(entry);

        List<NeonLetterColorSaveEntry> entries = envelope.Entries ??= new();
        entries.RemoveAll(savedEntry => savedEntry is null);

        int existingIndex = entries.FindIndex(
            savedEntry => savedEntry.SaveId == entry.SaveId);
        if (existingIndex >= 0)
        {
            entries[existingIndex] = entry;
            return;
        }

        entries.Add(entry);
    }

    public static void Remove(
        NeonLetterColorSaveEnvelope envelope,
        int saveId)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        List<NeonLetterColorSaveEntry> entries = envelope.Entries ??= new();
        entries.RemoveAll(
            savedEntry => savedEntry is null || savedEntry.SaveId == saveId);
    }

    public static NeonRgba? Resolve(
        NeonLetterColorSaveEnvelope envelope,
        int saveId,
        int recipeId)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.Version != NeonLetterColorSaveEnvelope.CurrentVersion ||
            envelope.Entries == null)
        {
            return null;
        }

        NeonLetterColorSaveEntry? entry = envelope.Entries.Find(
            savedEntry => savedEntry is not null && savedEntry.SaveId == saveId);
        if (entry == null || entry.RecipeId != recipeId)
        {
            return null;
        }

        return entry.Color;
    }
}
