using SOTFNeonLetters;
using Xunit;

public sealed class MaterialInitializationLifecycleTests
{
    private static readonly Func<TrackedRoot, bool>
        IsRootAliveCallback = IsRootAlive;
    private static readonly NeonLetterMaterialColorApplyCallback<
        ColorApplicationState> RecordColorCallback = RecordColor;
    private static readonly NeonLetterMaterialColorApplyCallback<
        ColorApplicationState> FailColorCallback = FailColor;

    [Fact]
    public void MissingRootLivenessCheckIsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                null!));
    }

    [Fact]
    public void MissingRootIsRejectedBeforeColorApplication()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);

        Assert.Throws<ArgumentNullException>(
            () => lifecycle.TryApply(
                instanceId: 7,
                root: null!,
                isKnownCompletedStructure: true,
                ref state,
                RecordColorCallback));
    }

    [Fact]
    public void MissingColorApplicationIsRejected()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot();
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);

        Assert.Throws<ArgumentNullException>(
            () => lifecycle.TryApply(
                instanceId: 7,
                root,
                isKnownCompletedStructure: true,
                ref state,
                null!));
    }

    [Fact]
    public void DestroyedKnownRootIsRejectedBeforeColorApplication()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot { IsAlive = false };
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);

        Assert.Throws<InvalidOperationException>(
            () => lifecycle.TryApply(
                instanceId: 7,
                root,
                isKnownCompletedStructure: true,
                ref state,
                RecordColorCallback));
    }

    [Fact]
    public void FirstApplicationForKnownRootUsesDefaultColorInput()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot();
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);

        bool applied = lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        Assert.Equal(
            (true, 1, NeonRgba.ProjectCyan),
            (applied, state.Attempts, state.AppliedColors.Single()));
    }

    [Fact]
    public void FirstApplicationForKnownRootUsesSavedColorInput()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot();
        var savedColor = new NeonRgba(0.2f, 0.4f, 0.6f, 1f);
        var state = new ColorApplicationState(savedColor);

        bool applied = lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        Assert.Equal(
            (true, 1, savedColor),
            (applied, state.Attempts, state.AppliedColors.Single()));
    }

    [Fact]
    public void RepeatedBackfillForTheSameRootAndLifecycleDoesNotApplyAgain()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot();
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);

        bool first = lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);
        bool repeated = lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        Assert.Equal((true, false, 1), (first, repeated, state.Attempts));
    }

    [Fact]
    public void FailedInitialApplicationRemainsRetryable()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot();
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);

        Exception? failure = Record.Exception(
            () => lifecycle.TryApply(
                instanceId: 7,
                root,
                isKnownCompletedStructure: true,
                ref state,
                FailColorCallback));
        bool retried = lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        Assert.Equal(
            (typeof(InvalidOperationException), true, 2, 1),
            (
                failure?.GetType(),
                retried,
                state.Attempts,
                state.AppliedColors.Count));
    }

    [Fact]
    public void UnregisteredThenReusedInstanceIdStartsANewRootLifecycle()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var firstRoot = new TrackedRoot();
        var secondRoot = new TrackedRoot();
        var replacementColor = new NeonRgba(1f, 0f, 1f, 1f);
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);
        lifecycle.TryApply(
            instanceId: 7,
            firstRoot,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        bool removed = lifecycle.Remove(7, firstRoot);
        state.SelectedColor = replacementColor;
        bool replacementApplied = lifecycle.TryApply(
            instanceId: 7,
            secondRoot,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        Assert.Equal(
            (true, true, 2, replacementColor),
            (
                removed,
                replacementApplied,
                state.Attempts,
                state.AppliedColors.Last()));
    }

    [Fact]
    public void UnknownStructureInputDoesNotInvokeApplication()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot();
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);

        bool applied = lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: false,
            ref state,
            RecordColorCallback);

        Assert.Equal((false, 0), (applied, state.Attempts));
    }

    [Fact]
    public void MissingExpectedRootIsRejectedDuringRemoval()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);

        Assert.Throws<ArgumentNullException>(
            () => lifecycle.Remove(7, null!));
    }

    [Fact]
    public void RemovingAnUnknownInstanceDoesNotChangeLifecycleState()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);

        bool removed = lifecycle.Remove(7, new TrackedRoot());

        Assert.False(removed);
    }

    [Fact]
    public void ReusedInstanceIdCannotRemoveADifferentLiveRoot()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var initializedRoot = new TrackedRoot();
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);
        lifecycle.TryApply(
            instanceId: 7,
            initializedRoot,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        bool removed = lifecycle.Remove(7, new TrackedRoot());

        Assert.False(removed);
    }

    [Fact]
    public void ClearingLifecycleAllowsTheSameRootToInitializeAgain()
    {
        var lifecycle =
            new NeonLetterMaterialInitializationLifecycle<TrackedRoot>(
                IsRootAliveCallback);
        var root = new TrackedRoot();
        var state = new ColorApplicationState(NeonRgba.ProjectCyan);
        lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        lifecycle.Clear();
        bool appliedAfterClear = lifecycle.TryApply(
            instanceId: 7,
            root,
            isKnownCompletedStructure: true,
            ref state,
            RecordColorCallback);

        Assert.Equal((true, 2), (appliedAfterClear, state.Attempts));
    }

    private static bool IsRootAlive(TrackedRoot root)
    {
        return root.IsAlive;
    }

    private static void RecordColor(
        ref ColorApplicationState state)
    {
        state.Attempts++;
        state.AppliedColors.Add(state.SelectedColor);
    }

    private static void FailColor(ref ColorApplicationState state)
    {
        state.Attempts++;
        throw new InvalidOperationException("Expected test failure.");
    }

    private sealed class TrackedRoot
    {
        internal bool IsAlive { get; set; } = true;
    }

    private sealed class ColorApplicationState
    {
        internal ColorApplicationState(NeonRgba selectedColor)
        {
            SelectedColor = selectedColor;
        }

        internal int Attempts { get; set; }
        internal NeonRgba SelectedColor { get; set; }
        internal List<NeonRgba> AppliedColors { get; } = new();
    }
}
