using System.Reflection;
using SOTFNeonLetters;
using Xunit;

public sealed class BlueprintCloneActivationRegressionTests
{
    [Fact]
    public void EveryNeonBuiltAndPreviewPrefabUsesCloneSafeActivation()
    {
        MethodInfo shouldReplaceActivation = ResolveActivationPolicy();

        foreach (NeonLetterSmallDefinition definition in NeonLetterSmallCatalog.All)
        {
            Assert.True(
                InvokePolicy(
                    shouldReplaceActivation,
                    $"{definition.PrefabAssetName}(Clone)"),
                $"Built prefab '{definition.PrefabAssetName}' must use clone-safe activation.");
            Assert.True(
                InvokePolicy(
                    shouldReplaceActivation,
                    $"{definition.PrefabAssetName}CraftingNode"),
                $"Placement preview '{definition.PrefabAssetName}' must use clone-safe activation.");
        }
    }

    [Theory]
    [InlineData("CustomChair(Clone)")]
    [InlineData("NeonLetter_A_Small_Icon")]
    [InlineData("")]
    [InlineData(null)]
    public void UnrelatedPrefabsKeepTheDefaultSonsSdkActivationPath(string? prefabName)
    {
        Assert.False(InvokePolicy(ResolveActivationPolicy(), prefabName));
    }

    [Fact]
    public void CloneActivatorFindsComponentsOnEachCloneInsteadOfCarryingIl2CppReferencesFromThePrefab()
    {
        string runtimePath = FindRepositoryFile(
            "NeonLetterPrefabCloneActivationRuntime.cs");
        string runtimeSource = File.ReadAllText(runtimePath);
        string activatorSource = ExtractClass(
            runtimeSource,
            "internal sealed class NeonLetterPrefabCloneActivator",
            "internal static class NeonLetterPrefabActivationHarmony");

        Assert.Contains(
            "GetComponent<StructureCraftingNode>()",
            activatorSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "GetComponent<ScrewStructure>()",
            activatorSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Il2CppReferenceField",
            activatorSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ComponentsToEnable",
            activatorSource,
            StringComparison.Ordinal);
    }

    private static MethodInfo ResolveActivationPolicy()
    {
        Type? policyType = typeof(NeonLetterSmallCatalog).Assembly.GetType(
            "SOTFNeonLetters.NeonLetterPrefabCloneActivationPolicy");
        Assert.NotNull(policyType);

        MethodInfo? method = policyType.GetMethod(
            "ShouldReplaceSonsSdkActivation",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return method;
    }

    private static bool InvokePolicy(MethodInfo method, string? prefabName)
    {
        return (bool)(method.Invoke(null, new object?[] { prefabName })
            ?? throw new InvalidOperationException(
                "Clone activation policy returned no decision."));
    }

    private static string ExtractClass(
        string source,
        string classDeclaration,
        string nextClassDeclaration)
    {
        int startIndex = source.IndexOf(classDeclaration, StringComparison.Ordinal);
        Assert.True(
            startIndex >= 0,
            $"Could not find class declaration '{classDeclaration}'.");
        int endIndex = source.IndexOf(
            nextClassDeclaration,
            startIndex,
            StringComparison.Ordinal);
        Assert.True(
            endIndex > startIndex,
            $"Could not find class following '{classDeclaration}'.");
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
