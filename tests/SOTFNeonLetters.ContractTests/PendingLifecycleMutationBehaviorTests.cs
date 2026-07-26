using SOTFNeonLetters;
using Xunit;

public sealed class PendingLifecycleMutationBehaviorTests
{
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

}
