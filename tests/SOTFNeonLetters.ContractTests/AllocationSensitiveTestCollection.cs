using Xunit;

[CollectionDefinition(
    AllocationSensitiveTestCollection.Name,
    DisableParallelization = true)]
public sealed class AllocationSensitiveTestCollection
{
    internal const string Name = "Allocation-sensitive tests";
}
