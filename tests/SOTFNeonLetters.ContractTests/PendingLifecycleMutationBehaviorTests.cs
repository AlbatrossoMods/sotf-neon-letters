using SOTFNeonLetters;
using Xunit;

public sealed class PendingLifecycleMutationBehaviorTests
{
    [Fact]
    public void CompleteBatchMayFillExactlyTheAvailablePendingCapacity()
    {
        var state = CreateState(capacity: 2);
        var entries = new[]
        {
            CreateEntry(identity: 1, red: 0.1f),
            CreateEntry(identity: 2, red: 0.2f)
        };

        int appliedCount = ReceiveBatch(
            state,
            entries,
            isReady: _ => false,
            apply: (_, _) => { });

        Assert.Equal((0, 2), (appliedCount, state.PendingCount));
    }

    [Fact]
    public void RejectedBatchDoesNotEvictAnExistingPendingColor()
    {
        var state = CreateState(capacity: 1);
        state.Receive(
            identity: 1,
            Color(red: 0.1f),
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });
        Exception? receiveError = Record.Exception(
            () => ReceiveBatch(
                state,
                new[] { CreateEntry(identity: 2, red: 0.2f) },
                isReady: _ => false,
                apply: (_, _) => { }));
        var appliedIdentities = new List<int>();

        int appliedCount = state.DrainReady(
            nowSeconds: 1d,
            isReady: _ => true,
            apply: (identity, _) => appliedIdentities.Add(identity));

        Assert.Equal(
            (typeof(InvalidOperationException), 1, "1", 0),
            (
                receiveError?.GetType(),
                appliedCount,
                string.Join(",", appliedIdentities),
                state.PendingCount));
    }

    [Fact]
    public void ReadyBatchAppliesAndCommitsEveryColor()
    {
        var state = CreateState(capacity: 2);
        var entries = new[]
        {
            CreateEntry(identity: 1, red: 0.1f),
            CreateEntry(identity: 2, red: 0.2f)
        };
        var appliedIdentities = new List<int>();

        int appliedCount = ReceiveBatch(
            state,
            entries,
            isReady: _ => true,
            apply: (identity, _) => appliedIdentities.Add(identity));

        Assert.Equal(
            (
                2,
                "1,2",
                Color(red: 0.1f),
                Color(red: 0.2f),
                0),
            (
                appliedCount,
                string.Join(",", appliedIdentities),
                state.Resolve(1),
                state.Resolve(2),
                state.PendingCount));
    }

    [Fact]
    public void ExpiredPendingColorFreesCapacityForACompleteBatch()
    {
        var state = new NeonLetterReplicatedColorState<int>(
            pendingCapacity: 1,
            pendingLifetimeSeconds: 1d);
        state.Receive(
            identity: 1,
            Color(red: 0.1f),
            nowSeconds: 0d,
            isReady: _ => false,
            apply: (_, _) => { });

        int appliedCount = ReceiveBatch(
            state,
            new[] { CreateEntry(identity: 2, red: 0.2f) },
            nowSeconds: 1d,
            isReady: _ => false,
            apply: (_, _) => { });

        Assert.Equal((0, 1), (appliedCount, state.PendingCount));
    }

    [Theory]
    [InlineData(0, "entries")]
    [InlineData(1, "getIdentity")]
    [InlineData(2, "getColor")]
    [InlineData(3, "isReady")]
    [InlineData(4, "apply")]
    public void CompleteBatchRequiresEveryInput(
        int invalidInput,
        string expectedParameterName)
    {
        var state = CreateState(capacity: 1);
        IReadOnlyList<BatchEntry> entries =
            invalidInput == 0
                ? null!
                : new[] { CreateEntry(identity: 1, red: 0.1f) };
        Func<BatchEntry, int> getIdentity =
            invalidInput == 1 ? null! : static entry => entry.Identity;
        Func<BatchEntry, NeonRgba> getColor =
            invalidInput == 2 ? null! : static entry => entry.Color;
        Func<int, bool> isReady =
            invalidInput == 3 ? null! : _ => false;
        Action<int, NeonRgba> apply =
            invalidInput == 4 ? null! : (_, _) => { };

        ArgumentNullException exception =
            Assert.Throws<ArgumentNullException>(
                () => state.ReceiveBatch(
                    entries,
                    nowSeconds: 0d,
                    getIdentity,
                    getColor,
                    isReady,
                    apply));

        Assert.Equal(expectedParameterName, exception.ParamName);
    }

    [Fact]
    public void CompleteBatchRejectsNonFiniteTimeBeforeChangingState()
    {
        var state = CreateState(capacity: 1);

        Exception? receiveError = Record.Exception(
            () => ReceiveBatch(
                state,
                new[] { CreateEntry(identity: 1, red: 0.1f) },
                nowSeconds: double.NaN,
                isReady: _ => false,
                apply: (_, _) => { }));

        Assert.Equal(
            (typeof(ArgumentOutOfRangeException), 0),
            (receiveError?.GetType(), state.PendingCount));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MultiplayerSaveRejectsEachNonFiniteRotationComponent(
        int componentIndex)
    {
        NeonQuaternion rotation = componentIndex switch
        {
            0 => new NeonQuaternion(float.NaN, 0f, 0f, 1f),
            1 => new NeonQuaternion(0f, float.NaN, 0f, 1f),
            2 => new NeonQuaternion(0f, 0f, float.NaN, 1f),
            _ => new NeonQuaternion(0f, 0f, 0f, float.NaN)
        };
        NeonLetterMultiplayerSaveEnvelope sanitized =
            NeonLetterMultiplayerPersistencePolicy.Sanitize(
                CreateSaveEnvelope(rotation));

        Assert.Empty(sanitized.Entries);
    }

    [Fact]
    public void MultiplayerSaveAcceptsNormalizedRotationWithFourComponents()
    {
        var rotation = new NeonQuaternion(0.5f, 0.5f, 0.5f, 0.5f);

        NeonLetterMultiplayerSaveEnvelope sanitized =
            NeonLetterMultiplayerPersistencePolicy.Sanitize(
                CreateSaveEnvelope(rotation));

        Assert.Single(sanitized.Entries);
    }

    private static NeonLetterReplicatedColorState<int> CreateState(
        int capacity)
    {
        return new NeonLetterReplicatedColorState<int>(
            capacity,
            pendingLifetimeSeconds: 15d);
    }

    private static int ReceiveBatch(
        NeonLetterReplicatedColorState<int> state,
        IReadOnlyList<BatchEntry> entries,
        Func<int, bool> isReady,
        Action<int, NeonRgba> apply)
    {
        return ReceiveBatch(
            state,
            entries,
            nowSeconds: 0d,
            isReady,
            apply);
    }

    private static int ReceiveBatch(
        NeonLetterReplicatedColorState<int> state,
        IReadOnlyList<BatchEntry> entries,
        double nowSeconds,
        Func<int, bool> isReady,
        Action<int, NeonRgba> apply)
    {
        return state.ReceiveBatch(
            entries,
            nowSeconds,
            static entry => entry.Identity,
            static entry => entry.Color,
            isReady,
            apply);
    }

    private static NeonLetterMultiplayerSaveEnvelope CreateSaveEnvelope(
        NeonQuaternion rotation)
    {
        return new NeonLetterMultiplayerSaveEnvelope
        {
            Entries = new List<NeonLetterMultiplayerSaveEntry>
            {
                new()
                {
                    RecipeId = NeonLetterSmallCatalog.Get('A').RecipeId,
                    NativeSaveId = 1,
                    Position = new NeonVector3(1f, 2f, 3f),
                    Rotation = rotation,
                    PackedColor =
                        NeonLetterNetworkProtocol.Pack(
                            NeonRgba.ProjectCyan)
                }
            }
        };
    }

    private static BatchEntry CreateEntry(int identity, float red)
    {
        return new BatchEntry(identity, Color(red));
    }

    private static NeonRgba Color(float red)
    {
        return NeonLetterNetworkProtocol.Unpack(
            NeonLetterNetworkProtocol.CurrentVersion,
            NeonLetterNetworkProtocol.Pack(
                new NeonRgba(red, 0.2f, 0.3f, 1f)));
    }

    private readonly record struct BatchEntry(
        int Identity,
        NeonRgba Color);
}
