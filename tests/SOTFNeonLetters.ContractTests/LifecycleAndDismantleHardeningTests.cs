using SOTFNeonLetters;
using Xunit;

public sealed class LifecycleAndDismantleHardeningTests
{
    [Fact]
    public void CleanupRunsCompletedLifecycleStagesInReverseOrder()
    {
        var cleanupOrder = new List<string>();
        var lifecycle = new NeonLetterLifecycleCoordinator();
        lifecycle.CompleteStage(() => cleanupOrder.Add("multiplayer"));
        lifecycle.CompleteStage(() => cleanupOrder.Add("ui"));
        lifecycle.CompleteStage(() => cleanupOrder.Add("color"));

        lifecycle.Cleanup();

        Assert.Equal(
            new[] { "color", "ui", "multiplayer" },
            cleanupOrder);
    }

    [Fact]
    public void LifecycleCleanupCanBeRepeatedWithoutRepeatingAStage()
    {
        int cleanupCount = 0;
        var lifecycle = new NeonLetterLifecycleCoordinator();
        lifecycle.CompleteStage(() => cleanupCount++);

        lifecycle.Cleanup();
        lifecycle.Cleanup();

        Assert.Equal(1, cleanupCount);
    }

    [Fact]
    public void LifecycleCleanupContinuesAfterOneStageFails()
    {
        var cleanupResults = new List<string>();
        var lifecycle = new NeonLetterLifecycleCoordinator();
        lifecycle.CompleteStage(() => cleanupResults.Add("first"));
        lifecycle.CompleteStage(() => throw new InvalidOperationException("failure"));
        lifecycle.CompleteStage(() => cleanupResults.Add("third"));

        lifecycle.Cleanup(
            exception => cleanupResults.Add(exception.Message));

        Assert.Equal(
            new[] { "third", "failure", "first" },
            cleanupResults);
    }

    [Fact]
    public void UiDestroyRollsBackActivePreviewOnceBeforeClosing()
    {
        var events = new List<string>();
        var target = new object();
        var session = new NeonLetterColorEditorSession<object>();
        var destroy = new NeonLetterUiDestroyCoordinator<object>();
        var originalColor = new NeonRgba(0.1f, 0.2f, 0.3f, 1f);
        session.Open(target, originalColor);
        session.Editor!.SetPreview(new NeonRgba(1f, 0f, 0f, 1f));
        destroy.Begin();

        destroy.Destroy(
            session,
            (_, color) => events.Add($"rollback:{color}"),
            () => events.Add("close"),
            () => events.Add("remove"),
            () => events.Add("reset"));

        Assert.Equal(
            new[]
            {
                $"rollback:{originalColor}",
                "close",
                "remove",
                "reset"
            },
            events);
    }

    [Fact]
    public void UiDestroyWithoutActiveSessionOnlyClosesUiResources()
    {
        var events = new List<string>();
        var session = new NeonLetterColorEditorSession<object>();
        var destroy = new NeonLetterUiDestroyCoordinator<object>();
        destroy.Begin();

        destroy.Destroy(
            session,
            (_, _) => events.Add("rollback"),
            () => events.Add("close"),
            () => events.Add("remove"),
            () => events.Add("reset"));

        Assert.Equal(
            new[] { "close", "remove", "reset" },
            events);
    }

    [Fact]
    public void UiDestroyContinuesClosingWhenPreviewRollbackFails()
    {
        var events = new List<string>();
        var session = new NeonLetterColorEditorSession<object>();
        var destroy = new NeonLetterUiDestroyCoordinator<object>();
        session.Open(new object(), NeonRgba.ProjectCyan);
        destroy.Begin();

        destroy.Destroy(
            session,
            (_, _) =>
            {
                events.Add("rollback");
                throw new InvalidOperationException("rollback failure");
            },
            () => events.Add("close"),
            () => events.Add("remove"),
            () => events.Add("reset"),
            exception => events.Add(exception.Message));

        Assert.Equal(
            new[]
            {
                "rollback",
                "rollback failure",
                "close",
                "remove",
                "reset"
            },
            events);
    }

    [Fact]
    public void RepeatedUiDestroyDoesNotRepeatRollbackOrTeardown()
    {
        var events = new List<string>();
        var session = new NeonLetterColorEditorSession<object>();
        var destroy = new NeonLetterUiDestroyCoordinator<object>();
        session.Open(new object(), NeonRgba.ProjectCyan);
        destroy.Begin();

        destroy.Destroy(
            session,
            (_, _) => events.Add("rollback"),
            () => events.Add("close"),
            () => events.Add("remove"),
            () => events.Add("reset"));
        destroy.Destroy(
            session,
            (_, _) => events.Add("rollback"),
            () => events.Add("close"),
            () => events.Add("remove"),
            () => events.Add("reset"));

        Assert.Equal(
            new[] { "rollback", "close", "remove", "reset" },
            events);
    }

    [Fact]
    public void FailedOriginalDismantleLeavesCapturedStateUntouched()
    {
        var removedIdentities = new List<string>();
        var state = new NeonLetterDismantleCleanupState(
            new NeonLetterDismantleIdentity(
                StructureInstanceId: 11,
                SaveId: 22,
                NetworkIdentity: 33));

        NeonLetterDismantleCleanupCoordinator.Cleanup(
            state,
            originalSucceeded: false,
            instanceId => removedIdentities.Add($"interaction:{instanceId}"),
            instanceId => removedIdentities.Add($"session:{instanceId}"),
            saveId => removedIdentities.Add($"persistent:{saveId}"),
            networkIdentity => removedIdentities.Add($"network:{networkIdentity}"),
            instanceId => removedIdentities.Add($"editor:{instanceId}"),
            () => removedIdentities.Add("refund"));

        Assert.Empty(removedIdentities);
    }

    [Fact]
    public void SuccessfulNeonDismantleCleansOnlyCapturedIdentityOnce()
    {
        var removedIdentities = new List<string>();
        var state = new NeonLetterDismantleCleanupState(
            new NeonLetterDismantleIdentity(
                StructureInstanceId: 11,
                SaveId: 22,
                NetworkIdentity: 33));

        for (int attempt = 0; attempt < 2; attempt++)
        {
            NeonLetterDismantleCleanupCoordinator.Cleanup(
                state,
                originalSucceeded: true,
                instanceId => removedIdentities.Add($"interaction:{instanceId}"),
                instanceId => removedIdentities.Add($"session:{instanceId}"),
                saveId => removedIdentities.Add($"persistent:{saveId}"),
                networkIdentity => removedIdentities.Add($"network:{networkIdentity}"),
                instanceId => removedIdentities.Add($"editor:{instanceId}"),
                () => removedIdentities.Add("refund"));
        }

        Assert.Equal(
            new[]
            {
                "interaction:11",
                "session:11",
                "persistent:22",
                "network:33",
                "editor:11",
                "refund"
            },
            removedIdentities);
    }

    [Fact]
    public void DismantleCleanupContinuesAfterOneTargetStoreFails()
    {
        var cleanupResults = new List<string>();
        var state = new NeonLetterDismantleCleanupState(
            new NeonLetterDismantleIdentity(
                StructureInstanceId: 11,
                SaveId: 22,
                NetworkIdentity: 33));

        NeonLetterDismantleCleanupCoordinator.Cleanup(
            state,
            originalSucceeded: true,
            instanceId => cleanupResults.Add($"interaction:{instanceId}"),
            _ => throw new InvalidOperationException("session failure"),
            saveId => cleanupResults.Add($"persistent:{saveId}"),
            networkIdentity => cleanupResults.Add($"network:{networkIdentity}"),
            instanceId => cleanupResults.Add($"editor:{instanceId}"),
            () => cleanupResults.Add("refund"),
            exception => cleanupResults.Add(exception.Message));

        Assert.Equal(
            new[]
            {
                "interaction:11",
                "session failure",
                "persistent:22",
                "network:33",
                "editor:11",
                "refund"
            },
            cleanupResults);
    }

    [Fact]
    public void AuthoritativeColorRemovalIsExactAndIdempotent()
    {
        var colors = new NeonLetterAuthoritativeColors<int>();
        var firstColor = new NeonRgba(1f, 0f, 0f, 1f);
        var secondColor = new NeonRgba(0f, 1f, 0f, 1f);
        colors.TryAccept(
            isHost: true,
            identity: 11,
            isLive: true,
            NeonLetterASmallDefinition.RecipeId,
            firstColor);
        colors.TryAccept(
            isHost: true,
            identity: 22,
            isLive: true,
            NeonLetterASmallDefinition.RecipeId,
            secondColor);

        colors.Remove(11);
        colors.Remove(11);

        Assert.Equal(
            new[] { NeonRgba.ProjectCyan, secondColor },
            new[] { colors.Resolve(11), colors.Resolve(22) });
    }

    [Fact]
    public void PendingColorRemovalIsExactAndIdempotent()
    {
        var pending = new NeonLetterPendingColors<int>(
            capacity: 4,
            lifetimeSeconds: 15d);
        pending.Enqueue(11, new NeonRgba(1f, 0f, 0f, 1f), nowSeconds: 0d);
        pending.Enqueue(22, new NeonRgba(0f, 1f, 0f, 1f), nowSeconds: 0d);

        pending.Remove(11);
        pending.Remove(11);
        var appliedIdentities = new List<int>();
        pending.ApplyReady(
            nowSeconds: 1d,
            _ => true,
            (identity, _) => appliedIdentities.Add(identity));

        Assert.Equal(new[] { 22 }, appliedIdentities);
    }

    [Fact]
    public void ReplicatedColorRemovalClearsResolvedAndPendingState()
    {
        var replicated = new NeonLetterReplicatedColorState<int>(
            pendingCapacity: 4,
            pendingLifetimeSeconds: 15d);
        var firstColor = new NeonRgba(1f, 0f, 0f, 1f);
        var secondColor = new NeonRgba(0f, 1f, 0f, 1f);
        replicated.Receive(11, firstColor, 0d, _ => true, (_, _) => { });
        replicated.Receive(11, secondColor, 1d, _ => false, (_, _) => { });
        replicated.Receive(22, secondColor, 1d, _ => false, (_, _) => { });

        replicated.Remove(11);
        replicated.Remove(11);
        var appliedIdentities = new List<int>();
        replicated.DrainReady(
            2d,
            _ => true,
            (identity, _) => appliedIdentities.Add(identity));

        Assert.Equal(
            (NeonRgba.ProjectCyan, "22"),
            (replicated.Resolve(11), string.Join(",", appliedIdentities)));
    }
}
