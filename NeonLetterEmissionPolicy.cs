#nullable enable

namespace SOTFNeonLetters;

public interface IEmissionVisualSubtree
{
    string Name { get; }
    IReadOnlyList<IEmissionRenderer> Renderers { get; }
}

public interface IEmissionRenderer
{
    string Name { get; }
    IReadOnlyList<IEmissionMaterial> SharedMaterials { get; }

    IEmissionPropertyBlock ReadPropertyBlock(int materialIndex);
    void WritePropertyBlock(int materialIndex, IEmissionPropertyBlock propertyBlock);
}

public interface IEmissionMaterial
{
    float ReadEmissiveIntensity();
}

public interface IEmissionPropertyBlock
{
    void SetColor(string propertyName, NeonRgba color);
}

public static class NeonLetterEmissionPolicy
{
    public const string EmissiveColorPropertyName = "_EmissiveColor";

    public static void Apply(
        NeonLetterSmallDefinition definition,
        IReadOnlyList<IEmissionVisualSubtree> candidateSubtrees,
        NeonRgba selectedColor)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(candidateSubtrees);

        IEmissionVisualSubtree[] matches = candidateSubtrees
            .Where(subtree =>
                subtree != null &&
                string.Equals(
                    subtree.Name,
                    definition.ColliderVisualChildName,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one visual subtree named " +
                $"'{definition.ColliderVisualChildName}', but found {matches.Length}.");
        }

        IEmissionVisualSubtree selectedSubtree = matches[0];
        IReadOnlyList<IEmissionRenderer> renderers = selectedSubtree.Renderers;
        if (renderers == null || renderers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Visual subtree '{selectedSubtree.Name}' has no renderers.");
        }

        var slots = new List<IEmissionBindingSlot>();
        foreach (IEmissionRenderer renderer in renderers)
        {
            if (renderer == null)
            {
                throw new InvalidOperationException(
                    $"Visual subtree '{selectedSubtree.Name}' has a null renderer.");
            }

            IReadOnlyList<IEmissionMaterial> sharedMaterials = renderer.SharedMaterials;
            if (sharedMaterials == null || sharedMaterials.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Renderer '{renderer.Name}' has no shared materials.");
            }

            for (int materialIndex = 0; materialIndex < sharedMaterials.Count; materialIndex++)
            {
                IEmissionMaterial material = sharedMaterials[materialIndex];
                if (material == null)
                {
                    throw new InvalidOperationException(
                        $"Renderer '{renderer.Name}' has a null shared material at slot " +
                        $"{materialIndex}.");
                }

                slots.Add(
                    new LegacyEmissionBindingSlot(
                        renderer,
                        material,
                        materialIndex));
            }
        }

        new NeonLetterEmissionBinding(slots).Apply(selectedColor);
    }

    private sealed class LegacyEmissionBindingSlot : IEmissionBindingSlot
    {
        private readonly IEmissionRenderer _renderer;
        private readonly IEmissionMaterial _material;

        public LegacyEmissionBindingSlot(
            IEmissionRenderer renderer,
            IEmissionMaterial material,
            int materialIndex)
        {
            _renderer = renderer;
            _material = material;
            MaterialIndex = materialIndex;
        }

        public string RendererName => _renderer.Name;
        public int MaterialIndex { get; }
        public bool IsRendererAlive => true;
        public bool IsMaterialAlive => true;

        public float ReadEmissiveIntensity()
        {
            return _material.ReadEmissiveIntensity();
        }

        public IEmissionPropertyBlock ReadPropertyBlock()
        {
            return _renderer.ReadPropertyBlock(MaterialIndex);
        }

        public void WritePropertyBlock(
            IEmissionPropertyBlock propertyBlock)
        {
            _renderer.WritePropertyBlock(
                MaterialIndex,
                propertyBlock);
        }
    }
}
