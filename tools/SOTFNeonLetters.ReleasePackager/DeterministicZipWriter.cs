using System.IO.Compression;
using System.Text;

namespace SOTFNeonLetters.ReleasePackaging;

public static class DeterministicZipWriter
{
    private static readonly string[] ExpectedEntryNames =
    {
        "Mods/SOTFNeonLetters.dll",
        "Mods/SOTFNeonLetters/manifest.json",
        "Mods/SOTFNeonLetters/sotfneonletters"
    };
    private static readonly DateTimeOffset EntryTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const int RegularFileMode0644 = unchecked((int)0x81A40000);

    public static void Create(string sourceDirectory, string destinationZip)
    {
        string sourceRoot = Path.GetFullPath(sourceDirectory);
        string destinationPath = Path.GetFullPath(destinationZip);
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"Release package source directory does not exist: {sourceRoot}");
        }

        string sourcePrefix = sourceRoot.TrimEnd(Path.DirectorySeparatorChar) +
                              Path.DirectorySeparatorChar;
        if (destinationPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Release ZIP destination must be outside its source directory.");
        }

        ValidateSourceLayout(sourceRoot);

        string? destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Delete(destinationPath);
        using var output = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: false,
            entryNameEncoding: Encoding.UTF8);

        foreach (string entryName in ExpectedEntryNames)
        {
            string fullPath = Path.Combine(
                sourceRoot,
                entryName.Replace('/', Path.DirectorySeparatorChar));
            ZipArchiveEntry entry = archive.CreateEntry(
                entryName,
                CompressionLevel.Optimal);
            entry.LastWriteTime = EntryTimestamp;
            entry.ExternalAttributes = RegularFileMode0644;

            using FileStream source = File.OpenRead(fullPath);
            using Stream destination = entry.Open();
            source.CopyTo(destination);
        }
    }

    private static void ValidateSourceLayout(string sourceRoot)
    {
        ValidateRegularDirectory(sourceRoot, "release package source directory");

        string modsDirectory = Path.Combine(sourceRoot, "Mods");
        string modAssetsDirectory = Path.Combine(modsDirectory, "SOTFNeonLetters");

        ValidateExactChildren(sourceRoot, "Mods");
        ValidateRegularDirectory(modsDirectory, "Mods directory");
        ValidateExactChildren(
            modsDirectory,
            "SOTFNeonLetters",
            "SOTFNeonLetters.dll");
        ValidateRegularDirectory(modAssetsDirectory, "mod asset directory");
        ValidateExactChildren(
            modAssetsDirectory,
            "manifest.json",
            "sotfneonletters");

        foreach (string entryName in ExpectedEntryNames)
        {
            string fullPath = Path.Combine(
                sourceRoot,
                entryName.Replace('/', Path.DirectorySeparatorChar));
            ValidateRegularFile(fullPath, entryName);
        }
    }

    private static void ValidateExactChildren(
        string directory,
        params string[] expectedNames)
    {
        string[] actualNames = Directory
            .EnumerateFileSystemEntries(directory)
            .Select(path => Path.GetFileName(path) ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] expected = expectedNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (!actualNames.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Release package source has unexpected entries under {directory}.");
        }
    }

    private static void ValidateRegularDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Missing {description}: {path}");
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Release package source cannot use a symbolic link for {description}: {path}");
        }
    }

    private static void ValidateRegularFile(string path, string entryName)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Missing release package file: {entryName}",
                path);
        }

        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidOperationException(
                $"Release package entry must be a regular file: {entryName}");
        }
    }
}
