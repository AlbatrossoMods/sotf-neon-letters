using Xunit;

public sealed class MaterialInitializationRuntimeWiringTests
{
    [Fact]
    public void InitialMaterialApplicationUsesTheColorTargetsCurrentColor()
    {
        string runtime = ReadRepositoryFile("NeonLetterColorRuntime.cs");
        string initialApplication = ExtractSourceSegment(
            runtime,
            "private static void ApplyInitialMaterialColor(",
            "private readonly record struct InitialMaterialColorApplyState(");

        Assert.Equal(
            (CreatesTarget: true, ReadsCurrentColor: true),
            (
                CreatesTarget: initialApplication.Contains(
                    "var target = new NeonLetterColorTarget(",
                    StringComparison.Ordinal),
                ReadsCurrentColor: initialApplication.Contains(
                    "target.CurrentColor",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void InitialMaterialApplicationDoesNotCommitResolvedColor()
    {
        string runtime = ReadRepositoryFile("NeonLetterColorRuntime.cs");
        string initialApplication = ExtractSourceSegment(
            runtime,
            "private static void ApplyInitialMaterialColor(",
            "private readonly record struct InitialMaterialColorApplyState(");

        Assert.Equal(
            (
                CommitsSessionColor: false,
                CommitsPersistentColor: false,
                CommitsThroughTarget: false,
                RequestsMultiplayerColor: false,
                WritesSessionStore: false,
                WritesPersistentStore: false),
            (
                CommitsSessionColor: initialApplication.Contains(
                    "CommitSessionColor(",
                    StringComparison.Ordinal),
                CommitsPersistentColor: initialApplication.Contains(
                    "CommitPersistentColor(",
                    StringComparison.Ordinal),
                CommitsThroughTarget: initialApplication.Contains(
                    "CommitColor(",
                    StringComparison.Ordinal),
                RequestsMultiplayerColor: initialApplication.Contains(
                    "RequestColor(",
                    StringComparison.Ordinal),
                WritesSessionStore: initialApplication.Contains(
                    "SessionColors.Commit(",
                    StringComparison.Ordinal),
                WritesPersistentStore: initialApplication.Contains(
                    "PersistentColors.Upsert(",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void InitialApplicationAndPreviewShareTheEmissionBindingPath()
    {
        string runtime = ReadRepositoryFile("NeonLetterColorRuntime.cs");
        string initialApplication = ExtractSourceSegment(
            runtime,
            "private static void ApplyInitialMaterialColor(",
            "private readonly record struct InitialMaterialColorApplyState(");
        string emissionApplication = ExtractSourceSegment(
            runtime,
            "internal static void ApplyEmission(",
            "internal static void RemoveEmissionBinding(");
        string previewApplication = ExtractSourceSegment(
            runtime,
            "public void PreviewColor(",
            "public void CommitColor(");

        Assert.Equal(
            (
                InitialUsesApplyEmission: true,
                PreviewUsesApplyEmission: true,
                ApplyUsesBindingCache: true,
                ApplyWritesBinding: true),
            (
                InitialUsesApplyEmission: initialApplication.Contains(
                    "ApplyEmission(",
                    StringComparison.Ordinal),
                PreviewUsesApplyEmission: previewApplication.Contains(
                    "NeonLetterColorRuntime.ApplyEmission(",
                    StringComparison.Ordinal),
                ApplyUsesBindingCache: emissionApplication.Contains(
                    "EmissionBindings.GetOrCreate(",
                    StringComparison.Ordinal),
                ApplyWritesBinding: emissionApplication.Contains(
                    "binding.Apply(color);",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void InitialApplicationPrecedesInteractionAndLinkUiRequirements()
    {
        string interactionRuntime = ReadRepositoryFile(
            "NeonLetterColorInteractionRuntime.cs");
        string leaseRuntime = ReadRepositoryFile(
            "NeonLetterColorInteractionLeaseRuntime.cs");
        string registration = ExtractSourceSegment(
            interactionRuntime,
            "internal static void RegisterColorInteraction(",
            "internal static void UnregisterColorInteraction(");
        string leaseCreation = ExtractSourceSegment(
            leaseRuntime,
            "private static ColorInteractionLease CreateColorInteractionLease(",
            "private static void OnInteractionPerformed(");
        int initialApplication = registration.IndexOf(
            "TryApplyInitialMaterialColor(",
            StringComparison.Ordinal);
        int failureGate = registration.IndexOf(
            "InteractionCreationFailures.AllowsAttempt(",
            StringComparison.Ordinal);
        int colliderRequirement = registration.IndexOf(
            "structureRoot.GetComponent<BoxCollider>()",
            StringComparison.Ordinal);
        int leaseRequirement = registration.IndexOf(
            "CreateColorInteractionLease(",
            StringComparison.Ordinal);

        Assert.Equal(
            (
                InitialFound: true,
                BeforeFailureGate: true,
                BeforeCollider: true,
                BeforeLeaseCreation: true,
                LeaseCreatesLinkUi: true),
            (
                InitialFound: initialApplication >= 0,
                BeforeFailureGate:
                    failureGate >= 0 && initialApplication < failureGate,
                BeforeCollider:
                    colliderRequirement >= 0 &&
                    initialApplication < colliderRequirement,
                BeforeLeaseCreation:
                    leaseRequirement >= 0 &&
                    initialApplication < leaseRequirement,
                LeaseCreatesLinkUi: leaseCreation.Contains(
                    "SonsUiTools.CreateLinkUi(",
                    StringComparison.Ordinal)));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(FindRepositoryFile(relativePath))
            .ReplaceLineEndings("\n");
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
            $"Could not locate repository file '{relativePath}'.");
    }

    private static string ExtractSourceSegment(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException(
                $"Could not find source marker '{startMarker}'.");
        }

        int end = source.IndexOf(
            endMarker,
            start + startMarker.Length,
            StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException(
                $"Could not find source marker '{endMarker}'.");
        }

        return source[start..end];
    }
}
