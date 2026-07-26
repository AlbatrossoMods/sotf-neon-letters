#nullable enable

namespace SOTFNeonLetters;

public static class NeonLetterColorInteractionPolicy
{
    private static readonly IReadOnlySet<int> KnownRecipeIds =
        NeonLetterSmallCatalog.All
            .Select(definition => definition.RecipeId)
            .ToHashSet();

    public static bool IsEditable(
        bool hasCompletedStructure,
        int recipeId)
    {
        return hasCompletedStructure &&
               KnownRecipeIds.Contains(recipeId);
    }

    public static bool CanOpenEditor(
        bool isPlayerControllable,
        bool hasFocusedLetter)
    {
        return isPlayerControllable && hasFocusedLetter;
    }

    internal static bool ShouldCreateLease(
        bool isDedicatedOrHeadless,
        bool hasCompletedStructure,
        int recipeId,
        bool hasPromptTemplate)
    {
        return !isDedicatedOrHeadless &&
               hasPromptTemplate &&
               IsEditable(hasCompletedStructure, recipeId);
    }

    internal static bool CanOpenEditor(
        NeonLetterColorInteractionValidation validation)
    {
        return CanOpenEditor(
                   validation.IsPlayerControllable,
                   validation.RootAlive &&
                   validation.IsCurrentLease) &&
               validation.IsKnownCompletedStructure &&
               !validation.IsEditorOpen &&
               !validation.IsDismantlingOrBlocked;
    }
}

internal readonly record struct NeonLetterColorInteractionValidation(
    bool RootAlive,
    bool IsCurrentLease,
    bool IsKnownCompletedStructure,
    bool IsPlayerControllable,
    bool IsEditorOpen,
    bool IsDismantlingOrBlocked);

internal readonly record struct NeonLetterColorInteractionActivationState(
    bool HolderInactive,
    bool ActionConfigured,
    bool PromptConfigured,
    bool CallbackRegistered,
    bool GeometryConfigured);

internal static class NeonLetterColorInteractionActivationPolicy
{
    internal static bool CanActivate(
        NeonLetterColorInteractionActivationState state)
    {
        return state.HolderInactive &&
               state.ActionConfigured &&
               state.PromptConfigured &&
               state.CallbackRegistered &&
               state.GeometryConfigured;
    }
}

internal readonly record struct NeonLetterColorInteractionBounds(
    float CenterX,
    float CenterY,
    float CenterZ,
    float SizeX,
    float SizeY,
    float SizeZ);

internal readonly record struct NeonLetterColorInteractionGeometry(
    float CenterX,
    float CenterY,
    float CenterZ,
    float Radius);

internal static class NeonLetterColorInteractionGeometryPolicy
{
    internal const float MinimumProxyRadius = 0.45f;
    internal const float MaximumProxyRadius = 1.25f;
    internal const float ProxyRadiusPadding = 0.15f;

    internal static NeonLetterColorInteractionGeometry Resolve(
        NeonLetterColorInteractionBounds bounds)
    {
        ValidateFinite(bounds.CenterX, nameof(bounds.CenterX));
        ValidateFinite(bounds.CenterY, nameof(bounds.CenterY));
        ValidateFinite(bounds.CenterZ, nameof(bounds.CenterZ));
        ValidateSize(bounds.SizeX, nameof(bounds.SizeX));
        ValidateSize(bounds.SizeY, nameof(bounds.SizeY));
        ValidateSize(bounds.SizeZ, nameof(bounds.SizeZ));

        float largestSize = MathF.Max(
            bounds.SizeX,
            MathF.Max(bounds.SizeY, bounds.SizeZ));
        float radius = Math.Clamp(
            largestSize * 0.5f + ProxyRadiusPadding,
            MinimumProxyRadius,
            MaximumProxyRadius);
        return new NeonLetterColorInteractionGeometry(
            bounds.CenterX,
            bounds.CenterY,
            bounds.CenterZ,
            radius);
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Interaction geometry must be finite.");
        }
    }

    private static void ValidateSize(float value, string parameterName)
    {
        ValidateFinite(value, parameterName);
        if (value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Interaction bounds cannot have a negative size.");
        }
    }
}

internal sealed class NeonLetterColorInteractionLeaseRegistry<TLease>
    where TLease : class
{
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly LinkedList<int> _sweepOrder = new();

    internal int Count => _entries.Count;

    internal bool Contains(int structureInstanceId)
    {
        return _entries.ContainsKey(structureInstanceId);
    }

    internal bool TryAdd(
        int structureInstanceId,
        TLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (_entries.ContainsKey(structureInstanceId))
        {
            return false;
        }

        LinkedListNode<int> sweepNode =
            _sweepOrder.AddLast(structureInstanceId);
        _entries.Add(
            structureInstanceId,
            new Entry(lease, sweepNode));
        return true;
    }

    internal bool IsCurrent(
        int structureInstanceId,
        TLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return _entries.TryGetValue(
                   structureInstanceId,
                   out Entry? entry) &&
               ReferenceEquals(entry.Lease, lease);
    }

    internal bool TryGet(
        int structureInstanceId,
        out TLease? lease)
    {
        if (!_entries.TryGetValue(
                structureInstanceId,
                out Entry? entry))
        {
            lease = null;
            return false;
        }

        lease = entry.Lease;
        return true;
    }

    internal bool TryRemove(
        int structureInstanceId,
        out TLease? lease)
    {
        if (!_entries.TryGetValue(
                structureInstanceId,
                out Entry? entry))
        {
            lease = null;
            return false;
        }

        Remove(structureInstanceId, entry);
        lease = entry.Lease;
        return true;
    }

    internal bool TryTakeNextDead(
        int maxEntries,
        Func<TLease, bool> isAlive,
        out TLease? deadLease,
        out int inspectedEntries)
    {
        if (maxEntries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        ArgumentNullException.ThrowIfNull(isAlive);
        deadLease = null;
        inspectedEntries = 0;
        int entriesToInspect = Math.Min(maxEntries, _entries.Count);
        while (inspectedEntries < entriesToInspect)
        {
            LinkedListNode<int> sweepNode = _sweepOrder.First!;
            _sweepOrder.RemoveFirst();
            int structureInstanceId = sweepNode.Value;
            Entry entry = _entries[structureInstanceId];
            inspectedEntries++;
            if (isAlive(entry.Lease))
            {
                _sweepOrder.AddLast(sweepNode);
                continue;
            }

            _entries.Remove(structureInstanceId);
            deadLease = entry.Lease;
            return true;
        }

        return false;
    }

    internal bool TryTakeFirst(out TLease? lease)
    {
        LinkedListNode<int>? sweepNode = _sweepOrder.First;
        if (sweepNode == null)
        {
            lease = null;
            return false;
        }

        int structureInstanceId = sweepNode.Value;
        Entry entry = _entries[structureInstanceId];
        _sweepOrder.RemoveFirst();
        _entries.Remove(structureInstanceId);
        lease = entry.Lease;
        return true;
    }

    private void Remove(int structureInstanceId, Entry entry)
    {
        _entries.Remove(structureInstanceId);
        _sweepOrder.Remove(entry.SweepNode);
    }

    private sealed record Entry(
        TLease Lease,
        LinkedListNode<int> SweepNode);
}

internal sealed class NeonLetterColorInteractionFailureGate
{
    private bool _promptFailureReported;

    internal bool TryBeginPromptFailureReport()
    {
        if (_promptFailureReported)
        {
            return false;
        }

        _promptFailureReported = true;
        return true;
    }

    internal void ResetPromptFailureReport()
    {
        _promptFailureReported = false;
    }
}

internal sealed class
    NeonLetterColorInteractionCreationFailures<TFingerprint>
    where TFingerprint : notnull
{
    internal const long InitialRetryDelayUpdates = 120;
    internal const long MaximumRetryDelayUpdates = 7_680;

    private readonly Dictionary<int, FailureState> _failures = new();
    private readonly EqualityComparer<TFingerprint> _fingerprints =
        EqualityComparer<TFingerprint>.Default;

    internal int Count => _failures.Count;

    internal bool AllowsAttempt(
        int structureInstanceId,
        long updateTick)
    {
        ValidateUpdateTick(updateTick);
        return !_failures.TryGetValue(
                   structureInstanceId,
                   out FailureState failure) ||
               (!failure.IsTerminal &&
                updateTick >= failure.NextAttemptTick);
    }

    internal bool RecordTerminalFailure(
        int structureInstanceId,
        TFingerprint fingerprint)
    {
        bool reportFailure =
            !_failures.TryGetValue(
                structureInstanceId,
                out FailureState current) ||
            !_fingerprints.Equals(
                current.Fingerprint,
                fingerprint);
        _failures[structureInstanceId] = new FailureState(
            IsTerminal: true,
            NextAttemptTick: long.MaxValue,
            RetryDelayUpdates: MaximumRetryDelayUpdates,
            fingerprint);
        return reportFailure;
    }

    internal bool RecordTransientFailure(
        int structureInstanceId,
        long updateTick,
        TFingerprint fingerprint)
    {
        ValidateUpdateTick(updateTick);
        bool hasCurrent = _failures.TryGetValue(
            structureInstanceId,
            out FailureState current);
        bool changedFingerprint =
            !hasCurrent ||
            !_fingerprints.Equals(
                current.Fingerprint,
                fingerprint);
        long retryDelay =
            changedFingerprint
                ? InitialRetryDelayUpdates
                : current.RetryDelayUpdates;
        long nextAttemptTick =
            updateTick > long.MaxValue - retryDelay
                ? long.MaxValue
                : updateTick + retryDelay;
        long nextRetryDelay = Math.Min(
            retryDelay * 2,
            MaximumRetryDelayUpdates);
        _failures[structureInstanceId] = new FailureState(
            IsTerminal: false,
            nextAttemptTick,
            nextRetryDelay,
            fingerprint);
        return changedFingerprint;
    }

    internal void RecordSuccess(int structureInstanceId)
    {
        _failures.Remove(structureInstanceId);
    }

    internal void Remove(int structureInstanceId)
    {
        _failures.Remove(structureInstanceId);
    }

    internal void Clear()
    {
        _failures.Clear();
    }

    private static void ValidateUpdateTick(long updateTick)
    {
        if (updateTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(updateTick));
        }
    }

    private readonly record struct FailureState(
        bool IsTerminal,
        long NextAttemptTick,
        long RetryDelayUpdates,
        TFingerprint Fingerprint);
}

internal readonly record struct
    NeonLetterColorInteractionPromptCandidate<TTemplate>(
        bool IsOwnedColorInteraction,
        bool UsesNativeUseAction,
        bool HasInteractionGui,
        bool HasDynamicInputIcon,
        TTemplate Template)
    where TTemplate : class;

internal enum NeonLetterColorInteractionPromptObservationResult
{
    Ignored,
    Unchanged,
    Accepted
}

internal sealed class
    NeonLetterColorInteractionPromptLifecycle<TTemplate>
    where TTemplate : class
{
    private readonly Func<TTemplate, bool> _isAlive;
    private readonly NeonLetterColorInteractionBackfillCursor
        _backfill = new();
    private WeakReference<TTemplate>? _template;

    internal NeonLetterColorInteractionPromptLifecycle(
        Func<TTemplate, bool> isAlive)
    {
        ArgumentNullException.ThrowIfNull(isAlive);
        _isAlive = isAlive;
    }

    internal ulong Generation { get; private set; }
    internal bool IsBackfillPending => _backfill.IsActive;

    internal int RetainedTemplateCount =>
        TryGetTemplate(out _) ? 1 : 0;

    internal NeonLetterColorInteractionPromptObservationResult Observe(
        NeonLetterColorInteractionPromptCandidate<TTemplate> candidate)
    {
        if (candidate.IsOwnedColorInteraction ||
            !candidate.UsesNativeUseAction ||
            !candidate.HasInteractionGui ||
            !candidate.HasDynamicInputIcon ||
            candidate.Template == null)
        {
            return NeonLetterColorInteractionPromptObservationResult
                .Ignored;
        }

        if (TryGetTemplate(out _))
        {
            return NeonLetterColorInteractionPromptObservationResult
                .Unchanged;
        }

        _template =
            new WeakReference<TTemplate>(candidate.Template);
        if (Generation < ulong.MaxValue)
        {
            Generation++;
        }

        _backfill.StartCycle();
        return NeonLetterColorInteractionPromptObservationResult.Accepted;
    }

    internal bool TryGetTemplate(out TTemplate? template)
    {
        if (_template != null &&
            _template.TryGetTarget(out template) &&
            template != null &&
            _isAlive(template))
        {
            return true;
        }

        _template = null;
        _backfill.Reset();
        template = null;
        return false;
    }

    internal void StartBackfillIfTemplateAvailable()
    {
        if (TryGetTemplate(out _))
        {
            _backfill.StartCycle();
        }
    }

    internal NeonLetterColorInteractionBackfillWindow TakeBackfillWindow(
        int itemCount,
        int maximumItems)
    {
        return _backfill.TakeWindow(itemCount, maximumItems);
    }

    internal void ReportBackfillUnavailable()
    {
        _backfill.ReportUnavailable();
    }

    internal void Reset()
    {
        _template = null;
        Generation = 0;
        _backfill.Reset();
    }
}

internal sealed class NeonLetterColorInteractionBackfillSchedule
{
    internal const long RetryUpdateDelay = 120;
    private long _nextAttemptTick;

    internal bool TryBeginAttempt(long updateTick)
    {
        if (updateTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(updateTick));
        }

        if (updateTick < _nextAttemptTick)
        {
            return false;
        }

        _nextAttemptTick =
            updateTick > long.MaxValue - RetryUpdateDelay
                ? long.MaxValue
                : updateTick + RetryUpdateDelay;
        return true;
    }

    internal void Reset()
    {
        _nextAttemptTick = 0;
    }
}

internal readonly record struct NeonLetterColorInteractionBackfillWindow(
    int StartOffset,
    int Count);

internal sealed class NeonLetterColorInteractionBackfillCursor
{
    private int _nextOffset;

    internal bool IsActive { get; private set; }

    internal void StartCycle()
    {
        IsActive = true;
        _nextOffset = 0;
    }

    internal NeonLetterColorInteractionBackfillWindow TakeWindow(
        int itemCount,
        int maximumItems)
    {
        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount));
        }

        if (maximumItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        if (!IsActive || itemCount == 0)
        {
            IsActive = false;
            _nextOffset = 0;
            return new NeonLetterColorInteractionBackfillWindow(0, 0);
        }

        int startOffset = Math.Min(_nextOffset, itemCount);
        int count = Math.Min(
            maximumItems,
            itemCount - startOffset);
        _nextOffset = startOffset + count;
        if (_nextOffset >= itemCount)
        {
            IsActive = false;
            _nextOffset = 0;
        }

        return new NeonLetterColorInteractionBackfillWindow(
            startOffset,
            count);
    }

    internal void ReportUnavailable()
    {
        // Preserve the active cycle so the next in-world update retries.
    }

    internal void Reset()
    {
        IsActive = false;
        _nextOffset = 0;
    }
}

public enum NeonLetterColorCommitRoute
{
    Unavailable,
    SinglePlayer,
    MultiplayerHost,
    MultiplayerClient
}

public enum NeonLetterColorTargetMode
{
    SinglePlayer,
    Multiplayer
}

public static class NeonLetterColorCommitRoutingPolicy
{
    public static NeonLetterColorCommitRoute Resolve(
        NeonLetterColorTargetMode targetMode,
        bool isServer,
        bool isClient)
    {
        if (targetMode == NeonLetterColorTargetMode.SinglePlayer)
        {
            return NeonLetterColorCommitRoute.SinglePlayer;
        }

        if (targetMode != NeonLetterColorTargetMode.Multiplayer)
        {
            return NeonLetterColorCommitRoute.Unavailable;
        }

        if (isServer)
        {
            return NeonLetterColorCommitRoute.MultiplayerHost;
        }

        return isClient
            ? NeonLetterColorCommitRoute.MultiplayerClient
            : NeonLetterColorCommitRoute.Unavailable;
    }
}

public readonly record struct NeonLetterColorRoutedCommit(
    bool Succeeded,
    NeonLetterColorCommitRoute Route);

public static class NeonLetterColorCommitRoutingCoordinator
{
    public static NeonLetterColorRoutedCommit TryCommit(
        NeonLetterColorTargetMode targetMode,
        bool isServer,
        bool isClient,
        NeonRgba color,
        Action<NeonRgba> commitSinglePlayer,
        Func<NeonRgba, bool> requestMultiplayer)
    {
        ArgumentNullException.ThrowIfNull(commitSinglePlayer);
        ArgumentNullException.ThrowIfNull(requestMultiplayer);

        NeonLetterColorCommitRoute route = NeonLetterColorCommitRoutingPolicy.Resolve(
            targetMode,
            isServer,
            isClient);
        switch (route)
        {
            case NeonLetterColorCommitRoute.SinglePlayer:
                commitSinglePlayer(color);
                return new NeonLetterColorRoutedCommit(true, route);
            case NeonLetterColorCommitRoute.MultiplayerHost:
            case NeonLetterColorCommitRoute.MultiplayerClient:
                return new NeonLetterColorRoutedCommit(
                    requestMultiplayer(color),
                    route);
            default:
                return new NeonLetterColorRoutedCommit(false, route);
        }
    }
}

public sealed class NeonLetterColorFocus<TTarget>
    where TTarget : class
{
    public TTarget? Current { get; private set; }

    public void Enter(TTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Current = target;
    }

    public void Exit(TTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(Current, target))
        {
            Current = null;
        }
    }

    public void Clear()
    {
        Current = null;
    }
}

public readonly record struct NeonLetterColorTargetLoss(
    bool ShouldRollback,
    NeonRgba RollbackColor);

public sealed class NeonLetterColorEditorSession<TTarget>
    where TTarget : class
{
    public TTarget? Target { get; private set; }
    public NeonLetterColorEditor? Editor { get; private set; }

    public void Open(TTarget target, NeonRgba originalColor)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
        Editor = new NeonLetterColorEditor(originalColor);
    }

    public NeonLetterColorTargetLoss LoseTarget(TTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!ReferenceEquals(Target, target))
        {
            return default;
        }

        return EndWithRollback();
    }

    public NeonLetterColorTargetLoss ExitWorld()
    {
        return EndWithRollback();
    }

    public void Close()
    {
        Target = null;
        Editor = null;
    }

    private NeonLetterColorTargetLoss EndWithRollback()
    {
        if (Target == null || Editor == null)
        {
            Close();
            return default;
        }

        NeonRgba originalColor = Editor.Original;
        Close();
        return new NeonLetterColorTargetLoss(true, originalColor);
    }
}

public sealed class NeonLetterSessionColors<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, NeonRgba> _colors = new();

    public NeonRgba Resolve(TKey key)
    {
        return _colors.TryGetValue(key, out NeonRgba color)
            ? color
            : NeonRgba.ProjectCyan;
    }

    public void Commit(TKey key, NeonRgba color)
    {
        _colors[key] = color;
    }

    public void Remove(TKey key)
    {
        _colors.Remove(key);
    }

    public void Clear()
    {
        _colors.Clear();
    }
}

public static class NeonLetterColorFormatting
{
    public static string ToHex(NeonRgba color)
    {
        return $"#{ToByte(color.Red):X2}{ToByte(color.Green):X2}{ToByte(color.Blue):X2}";
    }

    private static byte ToByte(float component)
    {
        float clamped = Math.Clamp(component, 0f, 1f);
        return (byte)MathF.Round(clamped * byte.MaxValue, MidpointRounding.AwayFromZero);
    }
}
