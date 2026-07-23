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
