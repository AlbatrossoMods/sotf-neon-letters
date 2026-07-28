using System.Diagnostics;
using Xunit;

public sealed class UnityTrackedInputGuardTests
{
    [Fact]
    public void UnchangedPreexistingTrackedEditDoesNotFailUnityBuildGuard()
    {
        UnityGateFixture fixture = CreateFixture();
        File.WriteAllText(fixture.TrackedInputPath, "preexisting edit\n");

        ProcessResult result = RunGate(
            fixture,
            mutateTrackedInput: false);

        Assert.Equal(
            (0, "preexisting edit\n", ""),
            (
                result.ExitCode,
                File.ReadAllText(fixture.TrackedInputPath),
                result.StandardError));
    }

    [Fact]
    public void UnityBuildThatChangesTrackedInputFailsTheGuard()
    {
        UnityGateFixture fixture = CreateFixture();

        ProcessResult result = RunGate(
            fixture,
            mutateTrackedInput: true);

        Assert.Equal(
            (1, true),
            (
                result.ExitCode,
                result.StandardError.Contains(
                    "Unity build modified tracked inputs:\ntracked-input.txt\n",
                    StringComparison.Ordinal)));
    }

    private static UnityGateFixture CreateFixture()
    {
        string fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            $"sotf-neon-unity-input-guard-{Guid.NewGuid():N}");
        string toolsDirectory = Path.Combine(fixtureRoot, "tools");
        string projectDirectory = Path.Combine(
            fixtureRoot,
            "unity",
            "SOTFNeonLetters.Assets");
        string testSourcePath = Path.Combine(
            projectDirectory,
            "Assets",
            "Editor",
            "NeonAlphabetAssetTests.cs");
        string trackedInputPath = Path.Combine(
            fixtureRoot,
            "tracked-input.txt");
        string unityEditorPath = Path.Combine(
            fixtureRoot,
            "unity-editor");
        Directory.CreateDirectory(toolsDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(testSourcePath)!);

        File.Copy(
            FindRepositoryFile("tools/test-unity-assets.sh"),
            Path.Combine(toolsDirectory, "test-unity-assets.sh"));
        File.WriteAllText(testSourcePath, "test entrypoint\n");
        File.WriteAllText(trackedInputPath, "tracked baseline\n");
        WriteExecutable(
            Path.Combine(toolsDirectory, "build-unity-assets.sh"),
            "#!/usr/bin/env bash\n" +
            "set -euo pipefail\n" +
            "\n" +
            "script_dir=\"$(cd \"$(dirname \"${BASH_SOURCE[0]}\")\" && pwd -P)\"\n" +
            "repo_root=\"$(cd \"$script_dir/..\" && pwd -P)\"\n" +
            "bundle_path=\"$repo_root/unity/SOTFNeonLetters.Assets/Build/AssetBundles/Windows/sotfneonletters\"\n" +
            "\n" +
            "mkdir -p \"$(dirname \"$bundle_path\")\"\n" +
            "printf 'fixture bundle\\n' > \"$bundle_path\"\n" +
            "if [[ \"${SOTF_NEON_TEST_MUTATE_TRACKED:-0}\" == \"1\" ]]; then\n" +
            "  printf 'build edit\\n' > \"$repo_root/tracked-input.txt\"\n" +
            "fi\n");
        WriteExecutable(
            unityEditorPath,
            "#!/usr/bin/env bash\n" +
            "exit 0\n");
        RunGit(fixtureRoot, "init", "-q");
        RunGit(fixtureRoot, "add", ".");

        return new UnityGateFixture(
            fixtureRoot,
            trackedInputPath,
            unityEditorPath);
    }

    private static ProcessResult RunGate(
        UnityGateFixture fixture,
        bool mutateTrackedInput)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            WorkingDirectory = fixture.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(
            Path.Combine(
                fixture.Root,
                "tools",
                "test-unity-assets.sh"));
        startInfo.Environment["UNITY_EDITOR_PATH"] =
            fixture.UnityEditorPath;
        startInfo.Environment["SOTF_NEON_TEST_MUTATE_TRACKED"] =
            mutateTrackedInput ? "1" : "0";

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Could not start Unity tracked-input guard fixture.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private static void RunGit(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException(
                "Could not start git for Unity tracked-input guard fixture.");
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git failed for Unity tracked-input guard fixture: {standardError}");
        }
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

    private readonly record struct UnityGateFixture(
        string Root,
        string TrackedInputPath,
        string UnityEditorPath);

    private readonly record struct ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
