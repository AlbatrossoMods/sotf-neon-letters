using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using SOTFNeonLetters.ReleasePackaging;

if (args.Length != 5)
{
    Console.Error.WriteLine(
        "Usage: SOTFNeonLetters.ReleaseTests " +
        "<release-dll> <manifest> <readme> <asset-bundle> <release-zip>");
    return 2;
}

string releaseDllPath = Path.GetFullPath(args[0]);
string manifestPath = Path.GetFullPath(args[1]);
string readmePath = Path.GetFullPath(args[2]);
string assetBundlePath = Path.GetFullPath(args[3]);
string releaseZipPath = Path.GetFullPath(args[4]);
string repositoryRoot = Path.GetDirectoryName(manifestPath)!;
string snapshotExtractorPath = Path.Combine(
    repositoryRoot,
    "tools",
    "extract-canonical-unity-assets.py");
var failures = new List<string>();

CheckReleaseAssemblyMetadata();
CheckReleaseAssemblyPaths();
CheckManifest();
CheckReadme();
CheckDeterministicZip();
CheckCanonicalSnapshotExtraction();
CheckReleaseZip();

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Release artifact tests failed: {failures.Count}");
    foreach (string failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    return 1;
}

Console.WriteLine("All SOTFNeonLetters release artifact tests passed.");
return 0;

void CheckReleaseAssemblyMetadata()
{
    Check(File.Exists(releaseDllPath), $"release DLL exists at {releaseDllPath}");
    if (!File.Exists(releaseDllPath))
    {
        return;
    }

    Version? assemblyVersion = AssemblyName.GetAssemblyName(releaseDllPath).Version;
    CheckEqual(new Version(0, 3, 1, 0), assemblyVersion, "assembly version is 0.3.1.0");

    FileVersionInfo fileVersion = FileVersionInfo.GetVersionInfo(releaseDllPath);
    CheckEqual("0.3.1.0", fileVersion.FileVersion, "file version is 0.3.1.0");

    Assembly releaseAssembly = Assembly.LoadFile(releaseDllPath);
    string? informationalVersion = releaseAssembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;
    CheckEqual("0.3.1", informationalVersion, "informational version is 0.3.1");
}

void CheckReleaseAssemblyPaths()
{
    if (!File.Exists(releaseDllPath))
    {
        return;
    }

    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    byte[] dllBytes = File.ReadAllBytes(releaseDllPath);
    CheckDoesNotContain(dllBytes, userProfile, "release DLL omits the local user profile path");
    CheckDoesNotContain(
        dllBytes,
        "GlobalInput",
        "release IL has no global input dependency for color editing");
    CheckDoesNotContain(
        dllBytes,
        "RegisterKey",
        "release IL has no raw key registration");
    CheckDoesNotContain(
        dllBytes,
        "OnUsePerformed",
        "release IL has no raw color-use callback");
    CheckDoesNotContain(
        dllBytes,
        "TryResolveTargetFromView",
        "release IL has no camera target resolver");
    CheckDoesNotContain(
        dllBytes,
        "MainCamTr",
        "release IL has no nearest-camera target dependency");
    CheckDoesNotContain(
        dllBytes,
        "Raycast",
        "release IL has no raycast-based color target selection");
    using var stream = File.OpenRead(releaseDllPath);
    using var peReader = new PEReader(stream);
    foreach (DebugDirectoryEntry entry in peReader.ReadDebugDirectory())
    {
        if (entry.Type != DebugDirectoryEntryType.CodeView)
        {
            continue;
        }

        string pdbPath = peReader.ReadCodeViewDebugDirectoryData(entry).Path;
        Check(
            !Path.IsPathRooted(pdbPath),
            $"release DLL uses a non-machine-local PDB path, but found {pdbPath}");
    }
}

void CheckManifest()
{
    byte[] manifestBytes = File.ReadAllBytes(manifestPath);
    Check(
        Array.IndexOf(manifestBytes, (byte)'\r') < 0,
        "manifest uses LF line endings only");

    using JsonDocument manifest = JsonDocument.Parse(manifestBytes);
    JsonElement root = manifest.RootElement;
    CheckEqual("0.3.1", root.GetProperty("version").GetString(), "manifest version is 0.3.1");
    CheckEqual(
        "Buildable small neon symbols: English A-Z, Cyrillic А-Я (including Ё), digits 0-9, and punctuation.",
        root.GetProperty("description").GetString(),
        "manifest describes the released small symbol catalog");
    CheckEqual(
        "https://github.com/AlbatrossoMods/sotf-neon-letters",
        root.GetProperty("url").GetString(),
        "manifest links to the permanent source repository");
}

void CheckReadme()
{
    string readme = File.ReadAllText(readmePath);
    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    Check(
        string.IsNullOrEmpty(userProfile) ||
        !readme.Contains(userProfile, StringComparison.Ordinal),
        "README omits the local user profile path");
    Check(readme.Contains("80 small neon symbols", StringComparison.Ordinal),
        "README documents the 80-symbol catalog");
    Check(readme.Contains("Neon Symbols", StringComparison.Ordinal),
        "README names the blueprint-book section");
    Check(readme.Contains("English A-Z", StringComparison.Ordinal),
        "README documents the English alphabet");
    Check(readme.Contains("Cyrillic А-Я (including Ё)", StringComparison.Ordinal),
        "README documents the Cyrillic alphabet");
    Check(readme.Contains("digits 0-9", StringComparison.Ordinal),
        "README documents the digits");
    Check(readme.Contains("! # $ & * + , - . = ?", StringComparison.Ordinal),
        "README documents the punctuation symbols");
}

void CheckDeterministicZip()
{
    string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"sotf-neon-release-zip-{Guid.NewGuid():N}");
    string firstSource = Path.Combine(testRoot, "first");
    string secondSource = Path.Combine(testRoot, "second");
    string firstZip = Path.Combine(testRoot, "first.zip");
    string secondZip = Path.Combine(testRoot, "second.zip");
    var fixtureTimestamp = new DateTime(
        2020,
        1,
        2,
        3,
        4,
        6,
        DateTimeKind.Utc);

    try
    {
        CreateZipFixture(
            firstSource,
            fixtureTimestamp,
            reverseCreationOrder: true);
        CreateZipFixture(
            secondSource,
            new DateTime(2030, 6, 7, 8, 9, 10, DateTimeKind.Utc),
            reverseCreationOrder: false);

        DeterministicZipWriter.Create(firstSource, firstZip);
        DeterministicZipWriter.Create(secondSource, secondZip);

        Check(
            File.ReadAllBytes(firstZip).AsSpan().SequenceEqual(File.ReadAllBytes(secondZip)),
            "identical release files produce byte-identical ZIPs despite source metadata changes");

        using ZipArchive archive = ZipFile.OpenRead(firstZip);
        string[] entryNames = archive.Entries.Select(entry => entry.FullName).ToArray();
        CheckEqual(
            "Mods/SOTFNeonLetters.dll\n" +
            "Mods/SOTFNeonLetters/manifest.json\n" +
            "Mods/SOTFNeonLetters/sotfneonletters",
            string.Join('\n', entryNames),
            "release ZIP contains exactly three entries in ordinal order");

        var fixedTimestamp = new DateTime(2000, 1, 1, 0, 0, 0);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            CheckEqual(
                fixedTimestamp,
                entry.LastWriteTime.DateTime,
                $"{entry.FullName} has a fixed timestamp");
            CheckEqual(
                unchecked((int)0x81A40000),
                entry.ExternalAttributes,
                $"{entry.FullName} has fixed regular-file mode 0644");
        }

        string extraSource = Path.Combine(testRoot, "extra");
        CreateZipFixture(extraSource, fixtureTimestamp, reverseCreationOrder: false);
        File.WriteAllText(
            Path.Combine(extraSource, "unexpected.txt"),
            "unexpected\n",
            new UTF8Encoding(false));
        CheckThrows<InvalidOperationException>(
            () => DeterministicZipWriter.Create(
                extraSource,
                Path.Combine(testRoot, "extra.zip")),
            "release ZIP writer rejects unexpected source entries");

        string symlinkSource = Path.Combine(testRoot, "symlink");
        CreateZipFixture(symlinkSource, fixtureTimestamp, reverseCreationOrder: false);
        string externalManifest = Path.Combine(testRoot, "external-manifest.json");
        File.WriteAllText(externalManifest, "{}\n", new UTF8Encoding(false));
        string symlinkManifest = Path.Combine(
            symlinkSource,
            "Mods",
            "SOTFNeonLetters",
            "manifest.json");
        File.Delete(symlinkManifest);
        File.CreateSymbolicLink(symlinkManifest, externalManifest);
        CheckThrows<InvalidOperationException>(
            () => DeterministicZipWriter.Create(
                symlinkSource,
                Path.Combine(testRoot, "symlink.zip")),
            "release ZIP writer rejects symbolic links at expected release paths");
    }
    finally
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }
}

void CheckCanonicalSnapshotExtraction()
{
    Check(
        File.Exists(snapshotExtractorPath),
        $"canonical Unity snapshot extractor exists at {snapshotExtractorPath}");
    if (!File.Exists(snapshotExtractorPath))
    {
        return;
    }

    string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"sotf-neon-snapshot-extraction-{Guid.NewGuid():N}");

    try
    {
        Directory.CreateDirectory(testRoot);
        SnapshotEntry[] validSnapshotEntries =
        {
            new("Assets/Generated.meta"),
            new("Assets/GeneratedSource.meta"),
            new("Assets/Generated/Prefabs/Letter.prefab"),
            new("Assets/GeneratedSource/Model.dae.meta")
        };

        CheckSnapshotExtractionSucceeds(
            Path.Combine(testRoot, "valid.zip"),
            Path.Combine(testRoot, "valid-output"),
            validSnapshotEntries);

        string destinationBoundaryArchive = Path.Combine(
            testRoot,
            "destination-boundary.zip");
        CreateSnapshotFixture(destinationBoundaryArchive, validSnapshotEntries);
        CheckSnapshotExtractionRejectsDestinationRootSymlink(
            testRoot,
            destinationBoundaryArchive);
        CheckSnapshotExtractionRejectsDestinationRootFile(
            testRoot,
            destinationBoundaryArchive);
        CheckSnapshotExtractionRejectsAssetsSymlink(
            testRoot,
            destinationBoundaryArchive);
        CheckSnapshotExtractionRejectsAssetsFile(
            testRoot,
            destinationBoundaryArchive);

        CheckSnapshotExtractionFails(
            Path.Combine(testRoot, "unexpected.zip"),
            Path.Combine(testRoot, "unexpected-output"),
            "canonical Unity snapshot rejects entries outside the generated roots",
            new SnapshotEntry("Assets/Unexpected.asset"));
        CheckSnapshotExtractionFails(
            Path.Combine(testRoot, "traversal.zip"),
            Path.Combine(testRoot, "traversal-output"),
            "canonical Unity snapshot rejects traversal entries",
            new SnapshotEntry("Assets/Generated/../ProjectSettings/escape.asset"));
        CheckSnapshotExtractionFails(
            Path.Combine(testRoot, "duplicate.zip"),
            Path.Combine(testRoot, "duplicate-output"),
            "canonical Unity snapshot rejects duplicate entries",
            new SnapshotEntry("Assets/Generated.meta"),
            new SnapshotEntry("Assets/Generated.meta"));
        CheckSnapshotExtractionFails(
            Path.Combine(testRoot, "symlink.zip"),
            Path.Combine(testRoot, "symlink-output"),
            "canonical Unity snapshot rejects symbolic-link entries",
            new SnapshotEntry(
                "Assets/Generated/linked.asset",
                unchecked((int)0xA1FF0000)));
        CheckSnapshotExtractionFails(
            Path.Combine(testRoot, "directory.zip"),
            Path.Combine(testRoot, "directory-output"),
            "canonical Unity snapshot rejects non-regular entries",
            new SnapshotEntry(
                "Assets/Generated/",
                unchecked((int)0x41ED0000)));
    }
    finally
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }
}

void CheckSnapshotExtractionSucceeds(
    string archivePath,
    string destinationPath,
    params SnapshotEntry[] entries)
{
    CreateSnapshotFixture(archivePath, entries);
    Directory.CreateDirectory(destinationPath);
    ProcessResult result = RunSnapshotExtractor(archivePath, destinationPath);
    CheckEqual(0, result.ExitCode, "canonical Unity snapshot extracts valid generated assets");
    Check(
        File.Exists(Path.Combine(destinationPath, "Assets", "Generated.meta")),
        "canonical Unity snapshot writes allowed generated assets");
}

void CheckSnapshotExtractionRejectsDestinationRootSymlink(
    string testRoot,
    string archivePath)
{
    string externalRoot = Path.Combine(testRoot, "external-root");
    string externalAssets = Path.Combine(externalRoot, "Assets");
    string sentinelPath = Path.Combine(externalAssets, "Generated.meta");
    string destinationPath = Path.Combine(testRoot, "linked-destination");
    Directory.CreateDirectory(externalAssets);
    File.WriteAllText(sentinelPath, "external root sentinel\n", new UTF8Encoding(false));
    Directory.CreateSymbolicLink(destinationPath, externalRoot);

    ProcessResult result = RunSnapshotExtractor(archivePath, destinationPath);
    Check(result.ExitCode != 0, "canonical Unity snapshot rejects a symlink destination root");
    CheckEqual(
        "external root sentinel\n",
        File.ReadAllText(sentinelPath),
        "rejecting a symlink destination root preserves external contents");
}

void CheckSnapshotExtractionRejectsDestinationRootFile(
    string testRoot,
    string archivePath)
{
    string destinationPath = Path.Combine(testRoot, "file-destination");
    File.WriteAllText(destinationPath, "destination root sentinel\n", new UTF8Encoding(false));

    ProcessResult result = RunSnapshotExtractor(archivePath, destinationPath);
    Check(result.ExitCode != 0, "canonical Unity snapshot rejects a non-directory destination root");
    CheckEqual(
        "destination root sentinel\n",
        File.ReadAllText(destinationPath),
        "rejecting a non-directory destination root preserves its contents");
}

void CheckSnapshotExtractionRejectsAssetsSymlink(
    string testRoot,
    string archivePath)
{
    string destinationPath = Path.Combine(testRoot, "linked-assets-destination");
    string externalAssets = Path.Combine(testRoot, "external-assets");
    string sentinelPath = Path.Combine(externalAssets, "Generated.meta");
    Directory.CreateDirectory(destinationPath);
    Directory.CreateDirectory(externalAssets);
    File.WriteAllText(sentinelPath, "external assets sentinel\n", new UTF8Encoding(false));
    Directory.CreateSymbolicLink(
        Path.Combine(destinationPath, "Assets"),
        externalAssets);

    ProcessResult result = RunSnapshotExtractor(archivePath, destinationPath);
    Check(result.ExitCode != 0, "canonical Unity snapshot rejects an Assets symlink");
    CheckEqual(
        "external assets sentinel\n",
        File.ReadAllText(sentinelPath),
        "rejecting an Assets symlink preserves external contents");
}

void CheckSnapshotExtractionRejectsAssetsFile(
    string testRoot,
    string archivePath)
{
    string destinationPath = Path.Combine(testRoot, "file-assets-destination");
    string assetsPath = Path.Combine(destinationPath, "Assets");
    Directory.CreateDirectory(destinationPath);
    File.WriteAllText(assetsPath, "Assets sentinel\n", new UTF8Encoding(false));

    ProcessResult result = RunSnapshotExtractor(archivePath, destinationPath);
    Check(result.ExitCode != 0, "canonical Unity snapshot rejects a non-directory Assets path");
    CheckEqual(
        "Assets sentinel\n",
        File.ReadAllText(assetsPath),
        "rejecting a non-directory Assets path preserves its contents");
}

void CheckSnapshotExtractionFails(
    string archivePath,
    string destinationPath,
    string description,
    params SnapshotEntry[] entries)
{
    CreateSnapshotFixture(archivePath, entries);
    ProcessResult result = RunSnapshotExtractor(archivePath, destinationPath);
    Check(result.ExitCode != 0, description);
    Check(
        !Directory.Exists(destinationPath) ||
        !Directory.EnumerateFileSystemEntries(destinationPath).Any(),
        $"{description} without partially extracting files");
}

ProcessResult RunSnapshotExtractor(string archivePath, string destinationPath)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = snapshotExtractorPath,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add(archivePath);
    startInfo.ArgumentList.Add(destinationPath);

    using Process process = Process.Start(startInfo)!;
    string standardOutput = process.StandardOutput.ReadToEnd();
    string standardError = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return new ProcessResult(process.ExitCode, standardOutput, standardError);
}

void CreateSnapshotFixture(string archivePath, IEnumerable<SnapshotEntry> entries)
{
    using ZipArchive archive = ZipFile.Open(
        archivePath,
        ZipArchiveMode.Create,
        Encoding.UTF8);
    foreach (SnapshotEntry fixtureEntry in entries)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            fixtureEntry.Name,
            CompressionLevel.NoCompression);
        entry.ExternalAttributes = fixtureEntry.ExternalAttributes;
        if (!fixtureEntry.Name.EndsWith("/", StringComparison.Ordinal))
        {
            using var writer = new StreamWriter(
                entry.Open(),
                new UTF8Encoding(false));
            writer.Write("fixture\n");
        }
    }
}

void CheckReleaseZip()
{
    Check(File.Exists(releaseZipPath), $"release ZIP exists at {releaseZipPath}");
    if (!File.Exists(releaseZipPath))
    {
        return;
    }

    using ZipArchive archive = ZipFile.OpenRead(releaseZipPath);
    string[] entryNames = archive.Entries.Select(entry => entry.FullName).ToArray();
    CheckEqual(
        "Mods/SOTFNeonLetters.dll\n" +
        "Mods/SOTFNeonLetters/manifest.json\n" +
        "Mods/SOTFNeonLetters/sotfneonletters",
        string.Join('\n', entryNames),
        "generated release ZIP contains exactly three entries in ordinal order");

    var fixedTimestamp = new DateTime(2000, 1, 1, 0, 0, 0);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
        CheckEqual(
            fixedTimestamp,
            entry.LastWriteTime.DateTime,
            $"generated {entry.FullName} has a fixed timestamp");
        CheckEqual(
            unchecked((int)0x81A40000),
            entry.ExternalAttributes,
            $"generated {entry.FullName} has fixed regular-file mode 0644");
    }

    CheckSequenceEqual(
        File.ReadAllBytes(releaseDllPath),
        ReadZipEntry(archive, "Mods/SOTFNeonLetters.dll"),
        "release ZIP contains the built DLL bytes");
    CheckSequenceEqual(
        File.ReadAllBytes(manifestPath),
        ReadZipEntry(archive, "Mods/SOTFNeonLetters/manifest.json"),
        "release ZIP contains the source manifest bytes");
    CheckSequenceEqual(
        File.ReadAllBytes(assetBundlePath),
        ReadZipEntry(archive, "Mods/SOTFNeonLetters/sotfneonletters"),
        "release ZIP contains the built asset bundle bytes");
}

byte[] ReadZipEntry(ZipArchive archive, string entryName)
{
    ZipArchiveEntry? entry = archive.GetEntry(entryName);
    if (entry == null)
    {
        failures.Add($"release ZIP entry exists: {entryName}");
        return Array.Empty<byte>();
    }

    using Stream source = entry.Open();
    using var destination = new MemoryStream();
    source.CopyTo(destination);
    return destination.ToArray();
}

void CreateZipFixture(
    string sourceRoot,
    DateTime timestamp,
    bool reverseCreationOrder)
{
    string[] relativePaths =
    {
        "Mods/SOTFNeonLetters.dll",
        "Mods/SOTFNeonLetters/manifest.json",
        "Mods/SOTFNeonLetters/sotfneonletters"
    };

    IEnumerable<string> creationOrder = reverseCreationOrder
        ? relativePaths.Reverse()
        : relativePaths;
    foreach (string relativePath in creationOrder)
    {
        string path = Path.Combine(sourceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"fixture:{relativePath}\n", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(path, timestamp);
    }
}

void CheckDoesNotContain(byte[] bytes, string text, string description)
{
    if (string.IsNullOrEmpty(text))
    {
        return;
    }

    byte[] forbidden = Encoding.UTF8.GetBytes(text);
    Check(bytes.AsSpan().IndexOf(forbidden) < 0, description);
}

void Check(bool condition, string description)
{
    if (!condition)
    {
        failures.Add(description);
    }
}

void CheckEqual<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        failures.Add($"{description}: expected {expected}, got {actual}");
    }
}

void CheckSequenceEqual(byte[] expected, byte[] actual, string description)
{
    if (!expected.AsSpan().SequenceEqual(actual))
    {
        failures.Add(description);
    }
}

void CheckThrows<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
        failures.Add(description);
    }
    catch (TException)
    {
    }
}

readonly record struct SnapshotEntry(
    string Name,
    int ExternalAttributes = unchecked((int)0x81A40000));

readonly record struct ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
