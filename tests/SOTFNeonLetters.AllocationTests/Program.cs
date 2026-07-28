using SOTFNeonLetters;

internal static class Program
{
    private const int EntriesPerUpdate = 16;
    private const int LeaseCount = 10_000;
    private const int MeasurementUpdates = 10_000;
    private const int WarmupUpdates = 2_048;
    private const long MaximumAllocatedBytes = 256;

    private static readonly Func<TrackedRoot, bool> IsRootAliveCallback =
        IsRootAlive;

    private static int Main()
    {
        var registry =
            new NeonLetterColorInteractionLeaseRegistry<TrackedRoot>();
        for (int index = 0; index < LeaseCount; index++)
        {
            registry.TryAdd(index, new TrackedRoot());
        }

        RunMaintenance(
            registry,
            WarmupUpdates,
            out _,
            out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RunMaintenance(
            registry,
            MeasurementUpdates,
            out int inspectedEntries,
            out bool removed);
        long allocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine(
            $"AllocatedBytes={allocatedBytes}; " +
            $"InspectedEntries={inspectedEntries}; " +
            $"Removed={removed}; LeaseCount={registry.Count}");

        return allocatedBytes <= MaximumAllocatedBytes &&
               inspectedEntries ==
               EntriesPerUpdate * MeasurementUpdates &&
               !removed &&
               registry.Count == LeaseCount
            ? 0
            : 1;
    }

    private static void RunMaintenance(
        NeonLetterColorInteractionLeaseRegistry<TrackedRoot> registry,
        int updateCount,
        out int inspectedEntries,
        out bool removed)
    {
        inspectedEntries = 0;
        removed = false;
        for (int update = 0; update < updateCount; update++)
        {
            removed |= registry.TryTakeNextDead(
                EntriesPerUpdate,
                IsRootAliveCallback,
                out _,
                out int inspectedThisUpdate);
            inspectedEntries += inspectedThisUpdate;
        }
    }

    private static bool IsRootAlive(TrackedRoot root)
    {
        return root.IsAlive;
    }

    private sealed class TrackedRoot
    {
        internal bool IsAlive => true;
    }
}
