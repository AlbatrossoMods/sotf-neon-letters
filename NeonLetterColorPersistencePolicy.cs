#nullable enable

namespace SOTFNeonLetters;

public interface INeonLetterColorRestoreTarget
{
    int RecipeId { get; }

    void Apply(NeonRgba color);
}

public static class NeonLetterColorPersistenceEligibility
{
    public static bool CanPersist(
        bool hasSaveIdentity,
        bool isTrackedCurrentStructure)
    {
        return hasSaveIdentity && isTrackedCurrentStructure;
    }
}

public sealed class NeonLetterColorSaveState
{
    private NeonLetterColorSaveEnvelope _envelope = new();

    public NeonLetterColorSaveEnvelope Save()
    {
        return CreateSnapshot(_envelope);
    }

    public void Load(NeonLetterColorSaveEnvelope? envelope)
    {
        _envelope = CreateSnapshot(envelope);
    }

    public void Upsert(NeonLetterColorSaveEntry entry)
    {
        NeonLetterColorStore.Upsert(_envelope, entry);
    }

    public void Remove(int saveId)
    {
        NeonLetterColorStore.Remove(_envelope, saveId);
    }

    public NeonRgba? Resolve(int saveId, int recipeId)
    {
        return NeonLetterColorStore.Resolve(_envelope, saveId, recipeId);
    }

    public void Clear()
    {
        _envelope = new NeonLetterColorSaveEnvelope();
    }

    private static NeonLetterColorSaveEnvelope CreateSnapshot(
        NeonLetterColorSaveEnvelope? source)
    {
        var snapshot = new NeonLetterColorSaveEnvelope();
        if (source == null ||
            source.Version != NeonLetterColorSaveEnvelope.CurrentVersion ||
            source.Entries == null)
        {
            return snapshot;
        }

        foreach (NeonLetterColorSaveEntry? entry in source.Entries)
        {
            if (entry == null || !IsFinite(entry.Color))
            {
                continue;
            }

            NeonLetterColorStore.Upsert(
                snapshot,
                new NeonLetterColorSaveEntry(
                    entry.SaveId,
                    entry.RecipeId,
                    entry.Color));
        }

        return snapshot;
    }

    private static bool IsFinite(NeonRgba color)
    {
        return float.IsFinite(color.Red) &&
               float.IsFinite(color.Green) &&
               float.IsFinite(color.Blue) &&
               float.IsFinite(color.Alpha);
    }
}

public static class NeonLetterColorRestoreCoordinator
{
    private static readonly IReadOnlySet<int> KnownRecipeIds =
        NeonLetterSmallCatalog.All
            .Select(definition => definition.RecipeId)
            .ToHashSet();

    public static int Restore(
        NeonLetterColorSaveEnvelope? envelope,
        Func<int, INeonLetterColorRestoreTarget?> resolveTarget,
        Action<Exception>? onEntryError = null)
    {
        ArgumentNullException.ThrowIfNull(resolveTarget);

        if (envelope == null ||
            envelope.Version != NeonLetterColorSaveEnvelope.CurrentVersion ||
            envelope.Entries == null)
        {
            return 0;
        }

        int restoredCount = 0;
        foreach (NeonLetterColorSaveEntry? entry in envelope.Entries)
        {
            if (entry == null || !KnownRecipeIds.Contains(entry.RecipeId))
            {
                continue;
            }

            try
            {
                INeonLetterColorRestoreTarget? target = resolveTarget(entry.SaveId);
                if (target == null || target.RecipeId != entry.RecipeId)
                {
                    continue;
                }

                target.Apply(entry.Color);
                restoredCount++;
            }
            catch (Exception exception)
            {
                onEntryError?.Invoke(exception);
            }
        }

        return restoredCount;
    }
}

public static class NeonLetterColorCommitCoordinator
{
    public static void Commit(
        NeonRgba color,
        Action<NeonRgba> applyEmission,
        Action<NeonRgba> persist)
    {
        ArgumentNullException.ThrowIfNull(applyEmission);
        ArgumentNullException.ThrowIfNull(persist);

        applyEmission(color);
        persist(color);
    }
}
