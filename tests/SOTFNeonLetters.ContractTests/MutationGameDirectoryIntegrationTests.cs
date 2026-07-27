using System.Diagnostics;
using Xunit;

public sealed class MutationGameDirectoryIntegrationTests
{
    [Fact]
    public void ExplicitGameDirectoryIsPropagatedToTheStrykerBuild()
    {
        MutationFixture fixture = CreateMutationFixture();
        string gameDirectory = Path.Combine(
            fixture.Root,
            "game directory");
        Directory.CreateDirectory(gameDirectory);

        ProcessResult result = RunMutationGate(
            fixture,
            gameDirectory);

        Assert.Equal(
            (ExitCode: 0, GameDirectory: gameDirectory),
            (
                result.ExitCode,
                GameDirectory: File.ReadAllText(fixture.TracePath)));
    }

    [Fact]
    public void MissingGameDirectoryFailsBeforeStrykerStarts()
    {
        MutationFixture fixture = CreateMutationFixture();

        ProcessResult result = RunMutationGate(
            fixture,
            gameDirectory: null);

        Assert.Equal(
            (
                ExitCode: 1,
                StandardError:
                    "Error: set SOTF_NEON_GAME_DIR or create " +
                    "SOTFNeonLetters.csproj.user from the provided template.\n",
                TraceExists: false),
            (
                result.ExitCode,
                result.StandardError,
                TraceExists: File.Exists(fixture.TracePath)));
    }

    private static MutationFixture CreateMutationFixture()
    {
        string fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            $"sotf-neon-mutation-game-dir-{Guid.NewGuid():N}");
        string toolsDirectory = Path.Combine(fixtureRoot, "tools");
        string dotnetDirectory = Path.Combine(
            fixtureRoot,
            ".tools",
            "dotnet-6");
        string testProjectDirectory = Path.Combine(
            fixtureRoot,
            "tests",
            "SOTFNeonLetters.ContractTests");
        Directory.CreateDirectory(toolsDirectory);
        Directory.CreateDirectory(dotnetDirectory);
        Directory.CreateDirectory(testProjectDirectory);

        File.Copy(
            FindRepositoryFile("tools/test-mutation.sh"),
            Path.Combine(toolsDirectory, "test-mutation.sh"));
        WriteExecutable(
            Path.Combine(dotnetDirectory, "dotnet"),
            "#!/usr/bin/env bash\n" +
            "if [[ \"$1\" == \"tool\" && \"$2\" == \"restore\" ]]; then\n" +
            "  exit 0\n" +
            "fi\n" +
            "if [[ \"$1\" == \"tool\" && \"$2\" == \"run\" ]]; then\n" +
            "  printf '%s' \"${GameDir:-}\" > \"$SOTF_NEON_MUTATION_TRACE\"\n" +
            "  [[ -n \"${GameDir:-}\" && -d \"$GameDir\" ]] || exit 41\n" +
            "  exit 0\n" +
            "fi\n" +
            "exit 42\n");

        return new MutationFixture(
            fixtureRoot,
            Path.Combine(fixtureRoot, "mutation.trace"));
    }

    private static ProcessResult RunMutationGate(
        MutationFixture fixture,
        string? gameDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = Path.GetPathRoot(fixture.Root)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(
            Path.Combine(fixture.Root, "tools", "test-mutation.sh"));
        startInfo.Environment.Remove("SOTF_NEON_DOTNET");
        startInfo.Environment.Remove("SOTF_NEON_GAME_DIR");
        startInfo.Environment.Remove("GameDir");
        startInfo.Environment.Remove("DOTNET_ROOT");
        startInfo.Environment["SOTF_NEON_MUTATION_TRACE"] =
            fixture.TracePath;
        if (gameDirectory != null)
        {
            startInfo.Environment["SOTF_NEON_GAME_DIR"] =
                gameDirectory;
        }

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Could not start mutation gate fixture.");
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

    private readonly record struct MutationFixture(
        string Root,
        string TracePath);

    private readonly record struct ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
