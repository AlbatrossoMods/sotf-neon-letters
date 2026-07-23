using SOTFNeonLetters.ReleasePackaging;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: SOTFNeonLetters.ReleasePackager <source-directory> <destination-zip>");
    return 2;
}

try
{
    DeterministicZipWriter.Create(args[0], args[1]);
    Console.WriteLine($"Created deterministic release ZIP: {Path.GetFullPath(args[1])}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Release packaging failed: {exception.Message}");
    return 1;
}
