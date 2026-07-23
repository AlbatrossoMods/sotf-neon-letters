#nullable enable

namespace SOTFNeonLetters;

internal static class NeonLetterNetworkProtocol
{
    public const byte CurrentVersion = 1;

    public static uint Pack(NeonRgba color)
    {
        EnsureFinite(color);

        uint red = ToByte(color.Red);
        uint green = ToByte(color.Green);
        uint blue = ToByte(color.Blue);
        uint alpha = ToByte(color.Alpha);

        return red | green << 8 | blue << 16 | alpha << 24;
    }

    public static NeonRgba Unpack(byte version, uint packed)
    {
        if (version != CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported neon letter multiplayer protocol version {version}.");
        }

        return new NeonRgba(
            (packed & byte.MaxValue) / (float)byte.MaxValue,
            ((packed >> 8) & byte.MaxValue) / (float)byte.MaxValue,
            ((packed >> 16) & byte.MaxValue) / (float)byte.MaxValue,
            ((packed >> 24) & byte.MaxValue) / (float)byte.MaxValue);
    }

    private static void EnsureFinite(NeonRgba color)
    {
        if (!float.IsFinite(color.Red) ||
            !float.IsFinite(color.Green) ||
            !float.IsFinite(color.Blue) ||
            !float.IsFinite(color.Alpha))
        {
            throw new InvalidOperationException("RGBA components must be finite.");
        }
    }

    private static byte ToByte(float component)
    {
        float clamped = Math.Clamp(component, 0f, 1f);
        return (byte)MathF.Round(
            clamped * byte.MaxValue,
            MidpointRounding.AwayFromZero);
    }
}
