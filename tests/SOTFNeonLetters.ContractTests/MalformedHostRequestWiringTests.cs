using Xunit;

public sealed class MalformedHostRequestWiringTests
{
    [Fact]
    public void HostRequestReadFailuresRouteThroughMalformedRequestPolicy()
    {
        string wire = File.ReadAllText(
            FindRepositoryFile("NeonLetterMultiplayerWireEvents.cs"));
        string malformedRequestHandler = ExtractSourceSegment(
            wire,
            "private static void HandleMalformedHostRequestReadFailure",
            "private sealed class HandshakeHelloEvent");
        string colorChangeRequest = ExtractSourceSegment(
            wire,
            "private sealed class ColorChangeRequestEvent",
            "private sealed class ColorChangeResultEvent");
        string colorPageRequest = ExtractSourceSegment(
            wire,
            "private sealed class ColorPageRequestEvent",
            "private sealed class ColorPageResponseEvent");
        const string failureRoute =
            "catch (Exception exception)\n" +
            "            {\n" +
            "                HandleMalformedHostRequestReadFailure(\n" +
            "                    Id,\n" +
            "                    fromConnection,\n" +
            "                    exception);\n" +
            "            }";

        Assert.Equal(
            (
                MalformedRequestStatuses: 1,
                ColorRequestFailureCalls: 1,
                PageRequestFailureCalls: 1),
            (
                MalformedRequestStatuses: CountOccurrences(
                    malformedRequestHandler,
                    "NeonLetterHandshakeStatus.MalformedRequest"),
                ColorRequestFailureCalls:
                    CountOccurrences(colorChangeRequest, failureRoute),
                PageRequestFailureCalls:
                    CountOccurrences(colorPageRequest, failureRoute)));
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
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        return source[start..end];
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int start = 0;
        while ((start = source.IndexOf(
                   value,
                   start,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }
}
