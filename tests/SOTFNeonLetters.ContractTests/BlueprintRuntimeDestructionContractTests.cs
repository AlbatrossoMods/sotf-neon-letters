using Xunit;

public sealed class BlueprintRuntimeDestructionContractTests
{
    [Fact]
    public void GroundPlacementProviderRetirementAvoidsImmediateRuntimeDestruction()
    {
        string source = File.ReadAllText(FindRepositoryFile("NeonLetterASmallBlueprint.cs"));
        string commitMethod = ExtractMethod(
            source,
            "public void CommitGroundPlacementRemoval()",
            "public void SetInitialRotation(");
        int disableIndex = commitMethod.IndexOf(
            ".enabled = false;",
            StringComparison.Ordinal);
        int deferredRemovalIndex = commitMethod.IndexOf(
            "UnityEngine.Object.Destroy(",
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "DestroyImmediate(",
            commitMethod,
            StringComparison.Ordinal);
        Assert.True(
            disableIndex >= 0,
            "The inherited ground-placement component must be disabled before removal.");
        Assert.True(
            deferredRemovalIndex > disableIndex,
            "The disabled ground-placement component must use Unity's deferred runtime removal.");
    }

    private static string ExtractMethod(
        string source,
        string signature,
        string nextSignature)
    {
        int startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not find method signature '{signature}'.");
        }

        int endIndex = source.IndexOf(
            nextSignature,
            startIndex,
            StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new InvalidOperationException(
                $"Could not find method following '{signature}'.");
        }

        return source[startIndex..endIndex];
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{relativePath}'.");
    }
}
