#nullable enable

using System;

namespace SOTFNeonLetters;

internal static class NeonLetterMalformedHostRequestPolicy
{
    internal static void RejectAcceptedPeer<TPeer>(
        TPeer peer,
        Func<TPeer, bool>? isAccepted,
        Action<TPeer>? reject,
        Action<TPeer>? quarantine,
        Action? logFailure)
        where TPeer : class
    {
        ArgumentNullException.ThrowIfNull(isAccepted);
        ArgumentNullException.ThrowIfNull(reject);
        ArgumentNullException.ThrowIfNull(quarantine);
        ArgumentNullException.ThrowIfNull(logFailure);

        if (peer is null || !isAccepted(peer))
        {
            return;
        }

        reject(peer);
        quarantine(peer);

        try
        {
            logFailure();
        }
        catch
        {
            // Logging failure must not escape the packet callback after quarantine.
        }
    }
}
