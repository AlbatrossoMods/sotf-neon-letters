#nullable enable

namespace SOTFNeonLetters;

internal sealed class NeonLetterLifecycleCoordinator
{
    private readonly List<Action> _completedStageCleanups = new();

    public void CompleteStage(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        _completedStageCleanups.Add(cleanup);
    }

    public void Cleanup(Action<Exception>? onCleanupError = null)
    {
        for (int index = _completedStageCleanups.Count - 1; index >= 0; index--)
        {
            Action cleanup = _completedStageCleanups[index];
            _completedStageCleanups.RemoveAt(index);
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                ReportCleanupError(onCleanupError, exception);
            }
        }
    }

    private static void ReportCleanupError(
        Action<Exception>? onCleanupError,
        Exception exception)
    {
        try
        {
            onCleanupError?.Invoke(exception);
        }
        catch
        {
            // Cleanup must remain best-effort even when reporting fails.
        }
    }
}

internal sealed class NeonLetterUiDestroyCoordinator<TTarget>
    where TTarget : class
{
    private bool _destroyed = true;

    public void Begin()
    {
        _destroyed = false;
    }

    public void Destroy(
        NeonLetterColorEditorSession<TTarget> editorSession,
        Action<TTarget, NeonRgba> rollbackPreview,
        Action closeUi,
        Action removeUi,
        Action resetUiState,
        Action<Exception>? onTeardownError = null)
    {
        ArgumentNullException.ThrowIfNull(editorSession);
        ArgumentNullException.ThrowIfNull(rollbackPreview);
        ArgumentNullException.ThrowIfNull(closeUi);
        ArgumentNullException.ThrowIfNull(removeUi);
        ArgumentNullException.ThrowIfNull(resetUiState);

        if (_destroyed)
        {
            return;
        }

        _destroyed = true;
        TTarget? target = editorSession.Target;
        NeonLetterColorTargetLoss targetLoss = editorSession.ExitWorld();
        if (targetLoss.ShouldRollback && target != null)
        {
            RunTeardown(
                () => rollbackPreview(target, targetLoss.RollbackColor),
                onTeardownError);
        }

        RunTeardown(closeUi, onTeardownError);
        RunTeardown(removeUi, onTeardownError);
        RunTeardown(resetUiState, onTeardownError);
    }

    private static void RunTeardown(
        Action teardown,
        Action<Exception>? onTeardownError)
    {
        try
        {
            teardown();
        }
        catch (Exception exception)
        {
            try
            {
                onTeardownError?.Invoke(exception);
            }
            catch
            {
                // UI teardown must continue even when reporting fails.
            }
        }
    }
}

internal readonly record struct NeonLetterDismantleIdentity(
    int StructureInstanceId,
    int? SaveId,
    ulong? NetworkIdentity);

internal sealed class NeonLetterDismantleCleanupState
{
    private bool _cleanupStarted;

    public NeonLetterDismantleCleanupState(NeonLetterDismantleIdentity identity)
    {
        Identity = identity;
    }

    public NeonLetterDismantleIdentity Identity { get; }

    public bool TryBeginCleanup(bool originalSucceeded)
    {
        if (_cleanupStarted ||
            !originalSucceeded)
        {
            return false;
        }

        _cleanupStarted = true;
        return true;
    }
}

internal static class NeonLetterDismantleCleanupCoordinator
{
    public static void Cleanup(
        NeonLetterDismantleCleanupState state,
        bool originalSucceeded,
        Action<int> removeInteraction,
        Action<int> removeSessionColor,
        Action<int> removePersistentColor,
        Action<ulong> removeMultiplayerColor,
        Action<int> closeEditor,
        Action spawnRefund,
        Action<Exception>? onCleanupError = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(removeInteraction);
        ArgumentNullException.ThrowIfNull(removeSessionColor);
        ArgumentNullException.ThrowIfNull(removePersistentColor);
        ArgumentNullException.ThrowIfNull(removeMultiplayerColor);
        ArgumentNullException.ThrowIfNull(closeEditor);
        ArgumentNullException.ThrowIfNull(spawnRefund);

        if (!state.TryBeginCleanup(originalSucceeded))
        {
            return;
        }

        NeonLetterDismantleIdentity identity = state.Identity;
        RunCleanup(
            () => removeInteraction(identity.StructureInstanceId),
            onCleanupError);
        RunCleanup(
            () => removeSessionColor(identity.StructureInstanceId),
            onCleanupError);
        if (identity.SaveId.HasValue)
        {
            RunCleanup(
                () => removePersistentColor(identity.SaveId.Value),
                onCleanupError);
        }

        if (identity.NetworkIdentity.HasValue)
        {
            RunCleanup(
                () => removeMultiplayerColor(identity.NetworkIdentity.Value),
                onCleanupError);
        }

        RunCleanup(
            () => closeEditor(identity.StructureInstanceId),
            onCleanupError);
        RunCleanup(spawnRefund, onCleanupError);
    }

    private static void RunCleanup(
        Action cleanup,
        Action<Exception>? onCleanupError)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            try
            {
                onCleanupError?.Invoke(exception);
            }
            catch
            {
                // A dismantle cleanup failure must never escape to the game.
            }
        }
    }
}
