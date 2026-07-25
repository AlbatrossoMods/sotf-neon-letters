#nullable enable

namespace SOTFNeonLetters;

internal interface IEmissionBindingSlot
{
    string RendererName { get; }
    int MaterialIndex { get; }
    bool IsRendererAlive { get; }
    bool IsMaterialAlive { get; }

    float ReadEmissiveIntensity();
    IEmissionPropertyBlock ReadPropertyBlock();
    void WritePropertyBlock(IEmissionPropertyBlock propertyBlock);
}

internal sealed class NeonLetterEmissionBinding
{
    private readonly IEmissionBindingSlot[] _slots;
    private readonly PreparedWrite[] _preparedWrites;

    public NeonLetterEmissionBinding(
        IReadOnlyList<IEmissionBindingSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new InvalidOperationException(
                "An emission binding requires at least one material slot.");
        }

        _slots = new IEmissionBindingSlot[slots.Count];
        for (int slotIndex = 0;
             slotIndex < slots.Count;
             slotIndex++)
        {
            _slots[slotIndex] = slots[slotIndex];
        }

        _preparedWrites = new PreparedWrite[_slots.Length];
    }

    public void Apply(NeonRgba selectedColor)
    {
        for (int slotIndex = 0;
             slotIndex < _slots.Length;
             slotIndex++)
        {
            IEmissionBindingSlot slot = _slots[slotIndex];
            if (slot == null)
            {
                throw new InvalidOperationException(
                    $"Emission binding material slot {slotIndex} is null.");
            }

            if (!slot.IsRendererAlive)
            {
                throw new InvalidOperationException(
                    $"Renderer '{slot.RendererName}' is no longer live.");
            }

            if (!slot.IsMaterialAlive)
            {
                throw new InvalidOperationException(
                    $"Renderer '{slot.RendererName}' has a destroyed shared " +
                    $"material at slot {slot.MaterialIndex}.");
            }

            float intensity = slot.ReadEmissiveIntensity();
            ValidateIntensity(
                slot.RendererName,
                slot.MaterialIndex,
                intensity);
            NeonRgba emissiveColor = CreateEmissiveColor(
                selectedColor,
                intensity);
            IEmissionPropertyBlock propertyBlock =
                slot.ReadPropertyBlock();
            if (propertyBlock == null)
            {
                throw new InvalidOperationException(
                    $"Renderer '{slot.RendererName}' has no property block for " +
                    $"material slot {slot.MaterialIndex}.");
            }

            _preparedWrites[slotIndex] = new PreparedWrite(
                slot,
                propertyBlock,
                emissiveColor);
        }

        for (int slotIndex = 0;
             slotIndex < _preparedWrites.Length;
             slotIndex++)
        {
            PreparedWrite preparedWrite = _preparedWrites[slotIndex];
            preparedWrite.PropertyBlock.SetColor(
                NeonLetterEmissionPolicy.EmissiveColorPropertyName,
                preparedWrite.Color);
            preparedWrite.Slot.WritePropertyBlock(
                preparedWrite.PropertyBlock);
        }
    }

    private static void ValidateIntensity(
        string rendererName,
        int materialIndex,
        float intensity)
    {
        if (!float.IsFinite(intensity))
        {
            throw new InvalidOperationException(
                $"Renderer '{rendererName}' material slot {materialIndex} " +
                $"requires a finite positive emissive intensity, but got " +
                $"{intensity}.");
        }

        if (intensity <= 0f)
        {
            throw new InvalidOperationException(
                $"Renderer '{rendererName}' material slot {materialIndex} has " +
                $"non-positive emissive intensity {intensity}.");
        }
    }

    private static NeonRgba CreateEmissiveColor(
        NeonRgba selectedColor,
        float intensity)
    {
        return new NeonRgba(
            SrgbToLinear(selectedColor.Red) * intensity,
            SrgbToLinear(selectedColor.Green) * intensity,
            SrgbToLinear(selectedColor.Blue) * intensity,
            selectedColor.Alpha);
    }

    private static float SrgbToLinear(float component)
    {
        return component <= 0.04045f
            ? component / 12.92f
            : MathF.Pow((component + 0.055f) / 1.055f, 2.4f);
    }

    private readonly record struct PreparedWrite(
        IEmissionBindingSlot Slot,
        IEmissionPropertyBlock PropertyBlock,
        NeonRgba Color);
}
