using System.Diagnostics;
using Xunit;

public sealed class FullGateMutationIntegrationTests
{
    [Fact]
    public void MutationFailureStopsTheFullGateBeforeReleaseWork()
    {
        string fixtureRoot = CreateGateFixture();
        string tracePath = Path.Combine(fixtureRoot, "gate.trace");
        ProcessResult result = RunGate(
            Path.Combine(fixtureRoot, "tools", "test-all.sh"),
            tracePath);

        Assert.Equal(
            (ExitCode: 37, Trace: "dotnet\nmutation\n"),
            (
                result.ExitCode,
                Trace: File.ReadAllText(tracePath)));
    }

    private static string CreateGateFixture()
    {
        string fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            $"sotf-neon-full-gate-contract-{Guid.NewGuid():N}");
        string toolsDirectory = Path.Combine(fixtureRoot, "tools");
        string dotnetDirectory = Path.Combine(
            fixtureRoot,
            ".tools",
            "dotnet-6");
        Directory.CreateDirectory(toolsDirectory);
        Directory.CreateDirectory(dotnetDirectory);

        File.Copy(
            FindRepositoryFile("tools/test-all.sh"),
            Path.Combine(toolsDirectory, "test-all.sh"));
        WriteExecutable(
            Path.Combine(dotnetDirectory, "dotnet"),
            "#!/usr/bin/env bash\n" +
            "printf 'dotnet\\n' >> \"$SOTF_NEON_GATE_TRACE\"\n" +
            "exit 0\n");
        WriteExecutable(
            Path.Combine(toolsDirectory, "test-mutation.sh"),
            "#!/usr/bin/env bash\n" +
            "printf 'mutation\\n' >> \"$SOTF_NEON_GATE_TRACE\"\n" +
            "exit 37\n");
        WriteExecutable(
            Path.Combine(toolsDirectory, "test-unity-assets.sh"),
            "#!/usr/bin/env bash\n" +
            "printf 'unity\\n' >> \"$SOTF_NEON_GATE_TRACE\"\n" +
            "exit 0\n");
        WriteExecutable(
            Path.Combine(toolsDirectory, "test-clean-release-gate.sh"),
            "#!/usr/bin/env bash\n" +
            "printf 'cold-release\\n' >> \"$SOTF_NEON_GATE_TRACE\"\n" +
            "exit 0\n");
        return fixtureRoot;
    }

    private static ProcessResult RunGate(
        string gatePath,
        string tracePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = Path.GetPathRoot(tracePath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(gatePath);
        startInfo.Environment.Remove("SOTF_NEON_DOTNET");
        startInfo.Environment.Remove("SOTF_NEON_COLD_RELEASE_ACTIVE");
        startInfo.Environment.Remove("SOTF_NEON_GAME_DIR");
        startInfo.Environment.Remove("GameDir");
        startInfo.Environment.Remove("DOTNET_ROOT");
        startInfo.Environment["SOTF_NEON_GATE_TRACE"] = tracePath;

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                $"Could not start full gate fixture '{gatePath}'.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private static void WriteExecutable(string path, string contents)
    {
        File.WriteAllText(path, contents);
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/chmod",
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("700");
        startInfo.ArgumentList.Add(path);
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                $"Could not mark fixture executable '{path}'.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Could not mark fixture executable '{path}'.");
        }
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

    private readonly record struct ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
