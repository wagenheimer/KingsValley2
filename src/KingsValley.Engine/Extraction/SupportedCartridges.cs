using System.Collections.Generic;

namespace KingsValley.Engine.Extraction;

/// <summary>User-facing identities; exact acceptance remains owned by the extraction profiles.</summary>
public static class SupportedCartridges
{
    public sealed record Entry(string Name, string Variant, string Sha1, string Recommendation, string Purpose, string Note);
    public static IReadOnlyList<Entry> All { get; } = new Entry[]
    {
        new("MSX2 RC-761 SCC PATCH", "msx2", "0c7e0b618c72e7ca08bd756c3103b46e5c580c04",
            "RECOMMENDED FOR ORIGINAL MSX2", "ORIGINAL CAMPAIGN: 60 STAGES.", "THIS EXACT SCC PATCH REVISION IS REQUIRED."),
        new("MSX1 RC-760 ORIGINAL", "msx1", "cfd872d005b7bd4cdd6e06c4c0162191f0b0415d",
            "RECOMMENDED FOR ORIGINAL MSX1", "ORIGINAL CAMPAIGN: 60 STAGES.", "ALTERNATIVE TO THE MSX2 CARTRIDGE."),
        new("MSX1 RC-760 FRS V1.1", "msx1", "cb612cbfdf79dabe55164b386ca956a3e187bef7",
            "ACCEPTED MSX1 ALTERNATIVE", "ORIGINAL CAMPAIGN: 60 STAGES.", "ALREADY PATCHED; NO AUTO-PATCH FOR MSX1."),
        new("MSX2 EDIT CONTEST JOYSNES", "msx2-contest", ContestEnglishPatch.SourceSha1,
            "RECOMMENDED FOR CONTEST", "CONTEST CAMPAIGN: 20 STAGES.", "ENGLISH PATCH IS APPLIED OFFLINE TO A COPY."),
        new("MSX2 CONTEST JOYSNES + FRS V1.1", "msx2-contest", ContestEnglishPatch.EnglishSha1,
            "ACCEPTED; GENERATED AUTOMATICALLY", "CONTEST CAMPAIGN: 20 STAGES.", "YOU DO NOT NEED TO FIND THIS WORKING COPY."),
    };

    public static int GuidePageCount => All.Count + 1;
    public static string GuidePage(int page)
    {
        if (page == 0) return "YOU DO NOT NEED ALL THE ROMS.\nFOR ORIGINAL: CHOOSE MSX2 OR MSX1.\nFOR CONTEST: ADD THE JOYSNES EDITION.\nEACH CARTRIDGE EXTRACTS INDEPENDENTLY.\nMATCH THE FULL SHA-1 ON THE NEXT PAGES.\nFILENAMES DO NOT PROVE COMPATIBILITY.\nROM, BIN OR ZIP; SHA-1 IS FOR THE ROM.\nOTHER REVISIONS ARE NOT ACCEPTED YET.";
        var entry = All[page - 1];
        return $"{entry.Name}\n{entry.Recommendation}\n{entry.Purpose}\n{entry.Note}\nROM SIZE: 131072 BYTES (128 KIB).\nEXACT ROM SHA-1:\n{entry.Sha1.ToUpperInvariant()}\nKEEP YOUR ORIGINAL ROM; IT IS NEVER OVERWRITTEN.";
    }
}
