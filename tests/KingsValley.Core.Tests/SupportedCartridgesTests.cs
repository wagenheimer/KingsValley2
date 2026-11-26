using System;
using System.Linq;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Extraction;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Scenes;
using Wagenheimer.MsxAudio.Extraction;
using Xunit;

namespace KingsValley.Core.Tests;

public class SupportedCartridgesTests
{
    [Fact]
    public void GuideListsExactlyTheAcceptedProfiles()
    {
        var accepted = CartridgeVariants.All.SelectMany(variant =>
            RomExtractionProfiles.FindByGameVariant("kings-valley-2", variant)!.RomSha1)
            .OrderBy(hash => hash, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(accepted, SupportedCartridges.All.Select(entry => entry.Sha1)
            .OrderBy(hash => hash, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    [Fact]
    public void EveryGuidePageFitsWithoutTruncatingItsHash()
    {
        for (int page = 0; page < SupportedCartridges.GuidePageCount; page++)
        {
            var lines = MsxScreen.MiniTextWrap(SupportedCartridges.GuidePage(page),
                FirstRunLayout.TextWidth(FirstRunLayout.DialogBody));
            Assert.True(lines.Count <= 8, $"Guide page {page}: {lines.Count} lines.");
            if (page > 0) Assert.Contains(SupportedCartridges.All[page - 1].Sha1.ToUpperInvariant(), lines);
        }
    }
}
