#nullable enable
using System.Collections.Generic;

namespace KingsValley.Engine.Extraction;

/// <summary>User-facing identities; exact acceptance remains owned by the extraction profiles.</summary>
public static class SupportedCartridges
{
    public sealed record Entry(string Name, string Variant, string Sha1, string Recommendation, string Purpose, string Note, string? SourceWarning = null);
    public static IReadOnlyList<Entry> All { get; } = new Entry[]
    {
        new("MSX2 RC-761 SCC PATCH", "msx2", "0c7e0b618c72e7ca08bd756c3103b46e5c580c04",
            "ACCEPTED SCC SLOT PATCH", "ORIGINAL CAMPAIGN: 60 STAGES.", "ORIGINAL AND OTHER VERIFIED REVISIONS WORK."),
        new("MSX2 RC-761 ORIGINAL GOODMSX", "msx2", "5ec8811254dc762c6852289a08acf22954404f0d",
            "RECOMMENDED FOR ORIGINAL MSX2", "ORIGINAL CAMPAIGN: 60 STAGES.", "UNMODIFIED ORIGINAL CARTRIDGE REVISION."),
        new("MSX2 RC-761 JOYSNES", "msx2", "9ef68fecc91bf5066b9eb9a796412ab00e5746bf",
            "ACCEPTED MSX2 ALTERNATIVE", "ORIGINAL CAMPAIGN: 60 STAGES.", "SOURCE PATCHES AND ARTWORK ARE PRESERVED."),
        new("MSX2 RC-761 ULVER CRACK", "msx2", "b5acea41aeabb5daa4183953f800d47db0c39fb5",
            "ACCEPTED MSX2 ALTERNATIVE", "ORIGINAL CAMPAIGN: 60 STAGES.", "SOURCE PATCHES AND ARTWORK ARE PRESERVED."),
        new("MSX2 RC-761 ALTERNATE 3164", "msx2", "22704b82d159d963f1192d7a81810aa2be8b6042",
            "ACCEPTED MSX2 ALTERNATIVE", "ORIGINAL CAMPAIGN: 60 STAGES.", "IDENTIFIED BY HASH, NOT BY FILENAME."),
        new("MSX2 RC-761 FRS V1.1", "msx2", "89d96d4b92d9a1c220cae0240cf0c00f310d14aa",
            "ACCEPTED MSX2 ENHANCEMENT", "ORIGINAL CAMPAIGN: 60 STAGES.", "USES ITS OWN PATCHED BOOT AND SOUND DRIVER."),
        new("MSX2 RC-761 SCC SLOT-2 DUMP", "msx2", "f25628e474035e6bf0440b274c5df04f94c49fb8",
            "ACCEPTED WITH SOURCE LIMITATIONS", "ORIGINAL CAMPAIGN: 60 STAGES.", "GOODMSX ORIGINAL IS PREFERRED.",
            "openMSX marks this dump Bad: needs SCC in slot 2 on hardware. Extraction uses the supplied cartridge bytes with an in-process SCC device; it does not repair the ROM."),
        new("MSX2 RC-761 MISSING TEXT", "msx2", "fa750d3f622717fd1dfd0b2740fe69b49ba62ac8",
            "PARTIAL EXTRACTION: SOURCE IS DAMAGED", "ORIGINAL CAMPAIGN: 60 STAGES.", "KNOWN MISSING TEXT; GOODMSX IS PREFERRED.",
            "openMSX marks this dump Bad: Missing Text. The missing text is a source defect and is not reconstructed or borrowed from another ROM."),
        new("MSX1 RC-760 ORIGINAL", "msx1", "cfd872d005b7bd4cdd6e06c4c0162191f0b0415d",
            "RECOMMENDED FOR ORIGINAL MSX1", "ORIGINAL CAMPAIGN: 60 STAGES.", "ALTERNATIVE TO THE MSX2 CARTRIDGE."),
        new("MSX1 RC-760 FRS V1.1", "msx1", "cb612cbfdf79dabe55164b386ca956a3e187bef7",
            "ACCEPTED MSX1 ALTERNATIVE", "ORIGINAL CAMPAIGN: 60 STAGES.", "ALREADY PATCHED; NO AUTO-PATCH FOR MSX1."),
        new("MSX1 RC-760 JOYSNES", "msx1", "7f6c5ec94d35981547a277c308ea9c2d2ab92e0f",
            "ACCEPTED MSX1 ALTERNATIVE", "ORIGINAL CAMPAIGN: 60 STAGES.", "SOURCE PATCHES AND ARTWORK ARE PRESERVED."),
        new("MSX1 RC-760 SCC SLOT-2 DUMP", "msx1", "f3306e6f25d111da21ce66db3404f5f48acb25a1",
            "ACCEPTED WITH SOURCE LIMITATIONS", "ORIGINAL CAMPAIGN: 60 STAGES.", "GOODMSX ORIGINAL IS PREFERRED.",
            "openMSX marks this dump Bad: needs SCC in slot 2 on hardware. Source bytes are preserved; extraction does not repair the ROM."),
        new("MSX2 CONTEST ORIGINAL GOODMSX", "msx2-contest", "2e0436303648097e8e936c5467574aff5e3f1517",
            "RECOMMENDED ORIGINAL CONTEST", "CONTEST CAMPAIGN: 20 STAGES.", "ENGLISH PATCH IS APPLIED OFFLINE TO A COPY."),
        new("MSX2 CONTEST OFFICIAL FRS V1.1", "msx2-contest", "376b4785cab7d60cc083f0c03bd5e8ea2fee8205",
            "ACCEPTED ENGLISH CONTEST", "CONTEST CAMPAIGN: 20 STAGES.", "OFFICIAL FRS OUTPUT; ALREADY TRANSLATED."),
        new("MSX2 EDIT CONTEST JOYSNES", "msx2-contest", ContestEnglishPatch.SourceSha1,
            "RECOMMENDED FOR CONTEST", "CONTEST CAMPAIGN: 20 STAGES.", "ENGLISH PATCH IS APPLIED OFFLINE TO A COPY."),
        new("MSX2 CONTEST JOYSNES + FRS V1.1", "msx2-contest", ContestEnglishPatch.EnglishSha1,
            "ACCEPTED; GENERATED AUTOMATICALLY", "CONTEST CAMPAIGN: 20 STAGES.", "YOU DO NOT NEED TO FIND THIS WORKING COPY."),
    };

    public const string ConvertedIoSha1 = "c4e138d533525d4b9064b2c265c4d2fdc5fa898d";
    public static int GuidePageCount => All.Count + 2;
    public static string GuidePage(int page)
    {
        if (page == 0) return "YOU DO NOT NEED ALL THE ROMS.\nFOR ORIGINAL: CHOOSE MSX2 OR MSX1.\nFOR CONTEST: ADD A VERIFIED CONTEST ROM.\nEACH CARTRIDGE EXTRACTS INDEPENDENTLY.\nMATCH THE FULL SHA-1 ON THE NEXT PAGES.\nFILENAMES DO NOT PROVE COMPATIBILITY.\nROM, BIN OR ZIP; SHA-1 IS FOR THE ROM.\nOTHER REVISIONS ARE NOT ACCEPTED YET.";
        if (page == All.Count + 1) return "KNOWN I/O-MAPPED CONVERSION\nNOT SUPPORTED BY THE CARTRIDGE RECIPES.\nREORDERED BANKS AND CUSTOM I/O PORTS.\nEXACT SHA-1:\n" + ConvertedIoSha1.ToUpperInvariant() + "\nUSE A VERIFIED ORIGINAL IF AVAILABLE.\nUNKNOWN HASHES STILL REQUIRE VALIDATION.\nSEE README AND ROM COMPATIBILITY GUIDE.";
        var entry = All[page - 1];
        return $"{entry.Name}\n{entry.Recommendation}\n{entry.Purpose}\n{entry.Note}\nROM SIZE: 131072 BYTES (128 KIB).\nEXACT ROM SHA-1:\n{entry.Sha1.ToUpperInvariant()}\nKEEP YOUR ORIGINAL ROM; IT IS NEVER OVERWRITTEN.";
    }
}
