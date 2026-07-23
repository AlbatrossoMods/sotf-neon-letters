#nullable enable

using System;
using System.Collections.Generic;

namespace SOTFNeonLetters
{

public enum NeonSymbolSource
{
    LegacyDae,
    ExtensionGlb
}

public sealed class NeonSymbolManifestEntry
{
    public NeonSymbolManifestEntry(
        char symbol,
        string unicodeCode,
        string assetKey,
        string sourceNodeName,
        NeonSymbolSource source)
    {
        Symbol = symbol;
        UnicodeCode = unicodeCode;
        AssetKey = assetKey;
        SourceNodeName = sourceNodeName;
        Source = source;
    }

    public char Symbol { get; }
    public string UnicodeCode { get; }
    public string AssetKey { get; }
    public string SourceNodeName { get; }
    public NeonSymbolSource Source { get; }
}

public static class NeonSymbolManifest
{
    private static readonly IReadOnlyList<NeonSymbolManifestEntry> Entries =
        Array.AsReadOnly(CreateEntries());

    public static IReadOnlyList<NeonSymbolManifestEntry> All => Entries;

    private static NeonSymbolManifestEntry[] CreateEntries()
    {
        var entries = new NeonSymbolManifestEntry[80];
        for (int index = 0; index < 26; index++)
        {
            char letter = (char)('A' + index);
            string sourceNodeName = letter == 'C'
                ? "g C Letters"
                : $"g Letters {letter}";
            entries[index] = new NeonSymbolManifestEntry(
                letter,
                $"U{(int)letter:X4}",
                letter.ToString(),
                sourceNodeName,
                NeonSymbolSource.LegacyDae);
        }

        NeonSymbolManifestEntry[] extensionEntries = CreateExtensionEntries();
        Array.Copy(extensionEntries, 0, entries, 26, extensionEntries.Length);
        return entries;
    }

    private static NeonSymbolManifestEntry[] CreateExtensionEntries()
    {
        return new[]
        {
            Extension('А', "U0410", "CYR_U0410", "glyph_CYR_U0410.013"),
            Extension('Б', "U0411", "CYR_U0411", "glyph_CYR_U0411.013"),
            Extension('В', "U0412", "CYR_U0412", "glyph_CYR_U0412.013"),
            Extension('Г', "U0413", "CYR_U0413", "glyph_CYR_U0413.013"),
            Extension('Д', "U0414", "CYR_U0414", "glyph_CYR_U0414.013"),
            Extension('Е', "U0415", "CYR_U0415", "glyph_CYR_U0415.013"),
            Extension('Ё', "U0401", "CYR_U0401", "glyph_CYR_U0401.013"),
            Extension('Ж', "U0416", "CYR_U0416", "glyph_CYR_U0416.013"),
            Extension('З', "U0417", "CYR_U0417", "glyph_CYR_U0417.013"),
            Extension('И', "U0418", "CYR_U0418", "glyph_CYR_U0418.013"),
            Extension('Й', "U0419", "CYR_U0419", "glyph_CYR_U0419.013"),
            Extension('К', "U041A", "CYR_U041A", "glyph_CYR_U041A.013"),
            Extension('Л', "U041B", "CYR_U041B", "glyph_CYR_U041B.013"),
            Extension('М', "U041C", "CYR_U041C", "glyph_CYR_U041C.013"),
            Extension('Н', "U041D", "CYR_U041D", "glyph_CYR_U041D.013"),
            Extension('О', "U041E", "CYR_U041E", "glyph_CYR_U041E.013"),
            Extension('П', "U041F", "CYR_U041F", "glyph_CYR_U041F.013"),
            Extension('Р', "U0420", "CYR_U0420", "glyph_CYR_U0420.013"),
            Extension('С', "U0421", "CYR_U0421", "glyph_CYR_U0421.013"),
            Extension('Т', "U0422", "CYR_U0422", "glyph_CYR_U0422.013"),
            Extension('У', "U0423", "CYR_U0423", "glyph_CYR_U0423.013"),
            Extension('Ф', "U0424", "CYR_U0424", "glyph_CYR_U0424.013"),
            Extension('Х', "U0425", "CYR_U0425", "glyph_CYR_U0425.013"),
            Extension('Ц', "U0426", "CYR_U0426", "glyph_CYR_U0426.013"),
            Extension('Ч', "U0427", "CYR_U0427", "glyph_CYR_U0427.013"),
            Extension('Ш', "U0428", "CYR_U0428", "glyph_CYR_U0428.013"),
            Extension('Щ', "U0429", "CYR_U0429", "glyph_CYR_U0429.013"),
            Extension('Ъ', "U042A", "CYR_U042A", "glyph_CYR_U042A.013"),
            Extension('Ы', "U042B", "CYR_U042B", "glyph_CYR_U042B.013"),
            Extension('Ь', "U042C", "CYR_U042C", "glyph_CYR_U042C.013"),
            Extension('Э', "U042D", "CYR_U042D", "glyph_CYR_U042D.013"),
            Extension('Ю', "U042E", "CYR_U042E", "glyph_CYR_U042E.013"),
            Extension('Я', "U042F", "CYR_U042F", "glyph_CYR_U042F.013"),
            Extension('0', "U0030", "DIG_U0030", "glyph_DIG_U0030.013"),
            Extension('1', "U0031", "DIG_U0031", "glyph_DIG_U0031.013"),
            Extension('2', "U0032", "DIG_U0032", "glyph_DIG_U0032.013"),
            Extension('3', "U0033", "DIG_U0033", "glyph_DIG_U0033.013"),
            Extension('4', "U0034", "DIG_U0034", "glyph_DIG_U0034.013"),
            Extension('5', "U0035", "DIG_U0035", "glyph_DIG_U0035.013"),
            Extension('6', "U0036", "DIG_U0036", "glyph_DIG_U0036.013"),
            Extension('7', "U0037", "DIG_U0037", "glyph_DIG_U0037.013"),
            Extension('8', "U0038", "DIG_U0038", "glyph_DIG_U0038.013"),
            Extension('9', "U0039", "DIG_U0039", "glyph_DIG_U0039.013"),
            Extension('!', "U0021", "PUNC_U0021_EXCLAMATION", "glyph_PUNC_U0021.013"),
            Extension('#', "U0023", "PUNC_U0023_NUMBER_SIGN", "glyph_PUNC_U0023.013"),
            Extension('$', "U0024", "PUNC_U0024_DOLLAR_SIGN", "glyph_PUNC_U0024.013"),
            Extension('&', "U0026", "PUNC_U0026_AMPERSAND", "glyph_PUNC_U0026.013"),
            Extension('*', "U002A", "PUNC_U002A_ASTERISK", "glyph_PUNC_U002A.013"),
            Extension('+', "U002B", "PUNC_U002B_PLUS_SIGN", "glyph_PUNC_U002B.013"),
            Extension(',', "U002C", "PUNC_U002C_COMMA", "glyph_PUNC_U002C.013"),
            Extension('-', "U002D", "PUNC_U002D_HYPHEN_MINUS", "glyph_PUNC_U002D.013"),
            Extension('.', "U002E", "PUNC_U002E_FULL_STOP", "glyph_PUNC_U002E.013"),
            Extension('=', "U003D", "PUNC_U003D_EQUALS_SIGN", "glyph_PUNC_U003D.013"),
            Extension('?', "U003F", "PUNC_U003F_QUESTION_MARK", "glyph_PUNC_U003F.013")
        };
    }

    private static NeonSymbolManifestEntry Extension(
        char symbol,
        string unicodeCode,
        string assetKey,
        string sourceNodeName)
    {
        return new NeonSymbolManifestEntry(
            symbol,
            unicodeCode,
            assetKey,
            sourceNodeName,
            NeonSymbolSource.ExtensionGlb);
    }
}
}
