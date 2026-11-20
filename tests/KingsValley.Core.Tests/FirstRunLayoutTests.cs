#nullable enable
using System.Linq;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Scenes;
using Xunit;

namespace KingsValley.Core.Tests;

public class FirstRunLayoutTests
{
    private static LayoutRect[] Bands => new[]
    {
        FirstRunLayout.Header,
        FirstRunLayout.Notice,
        FirstRunLayout.Status,
        FirstRunLayout.Buttons,
        FirstRunLayout.RomList,
        FirstRunLayout.Footer,
    };

    [Fact]
    public void PanelAndBands_AreInsideDesignArea()
    {
        AssertInside(FirstRunLayout.Panel,
            0, 0, FirstRunLayout.DesignWidth, FirstRunLayout.DesignHeight);

        // 252x208 centered in 256x212 leaves a 2 px frame on every side.
        Assert.Equal(2, FirstRunLayout.Panel.X);
        Assert.Equal(2, FirstRunLayout.Panel.Y);
        Assert.Equal(252, FirstRunLayout.Panel.Width);
        Assert.Equal(208, FirstRunLayout.Panel.Height);

        foreach (LayoutRect band in Bands)
        {
            Assert.True(band.Width > 0 && band.Height > 0);
            AssertInside(band, FirstRunLayout.Panel.X, FirstRunLayout.Panel.Y,
                FirstRunLayout.Panel.Right, FirstRunLayout.Panel.Bottom);
        }
    }

    [Fact]
    public void Buttons_AreStableRects_WithoutOverlap()
    {
        var first = FirstRunLayout.GetButtonRects();
        var second = FirstRunLayout.GetButtonRects();

        Assert.Equal(FirstRunLayout.ButtonCount, first.Count);
        Assert.Equal(4, FirstRunLayout.PlayButtonIndex);
        Assert.Equal(first, second);

        for (int i = 0; i < first.Count; i++)
        {
            LayoutRect r = first[i];
            AssertInside(r, 0, 0, FirstRunLayout.DesignWidth, FirstRunLayout.DesignHeight);
            AssertInside(r, FirstRunLayout.Buttons.X, FirstRunLayout.Buttons.Y,
                FirstRunLayout.Buttons.Right, FirstRunLayout.Buttons.Bottom);

            int labelWidth = MsxScreen.MiniTextWidth(FirstRunLayout.ButtonLabels[i]);
            Assert.True(labelWidth <= r.Width, $"{FirstRunLayout.ButtonLabels[i]} does not fit its button");
            Assert.True(labelWidth + 2 * FirstRunLayout.ButtonPaddingX <= r.Width);
        }

        for (int i = 0; i < first.Count; i++)
        for (int j = i + 1; j < first.Count; j++)
            Assert.False(first[i].Intersects(first[j]), $"buttons {i} and {j} overlap");
    }

    [Fact]
    public void MiniTextWidth_MatchesMicrofontAdvance()
    {
        Assert.Equal(16, MsxScreen.MiniTextWidth("ABCD"));
        Assert.Equal(16, FirstRunLayout.MeasureWidth("ABCD"));
        Assert.Equal(MsxScreen.MiniCharWidth, MsxScreen.MiniTextWidth(" "));
        Assert.Equal(40, MsxScreen.MiniTextWidth("1234567890"));

        foreach (string sample in new[] { "", "A B", "!?", "KING'S VALLEY II - ROM SETUP" })
            Assert.Equal(MsxScreen.MiniTextWidth(sample), FirstRunLayout.MeasureWidth(sample));
    }

    [Theory]
    [InlineData("ONE TWO THREE FOUR FIVE SIX SEVEN EIGHT NINE TEN")]
    [InlineData("SUPERCALIFRAGILISTICEXPIALIDOCIOUS AND SHORT")]
    [InlineData("A  B   C D")]
    [InlineData("EXTRACTION RUNS ONCE - PRESS ESC TO CANCEL")]
    [InlineData("   - MSX1 RC-760 (128 KB): 60 STAGES, AUDIO AND MSX1 ART")]
    public void MiniTextWrap_NeverExceedsWidth_AndKeepsNonSpaceCharacters(string text)
    {
        foreach (int maxWidth in new[] { 7, 16, 40, 232 })
        {
            var lines = MsxScreen.MiniTextWrap(text, maxWidth);
            Assert.NotEmpty(lines);

            foreach (string line in lines)
                Assert.True(MsxScreen.MiniTextWidth(line) <= maxWidth,
                    $"line '{line}' is wider than {maxWidth}");

            string expected = OnlyNonSpace(text);
            string actual = OnlyNonSpace(string.Concat(lines));
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void MiniTextWrap_HandlesEmptyInputAndImpossibleWidth()
    {
        Assert.Empty(MsxScreen.MiniTextWrap(string.Empty, 40));
        Assert.Empty(MsxScreen.MiniTextWrap("ABC", 2));
    }

    [Fact]
    public void StatusColumns_FitLongestExpectedLabelsAndTags()
    {
        AssertInside(FirstRunLayout.StatusLabelColumn, FirstRunLayout.Status.X, FirstRunLayout.Status.Y,
            FirstRunLayout.Status.Right, FirstRunLayout.Status.Bottom);
        AssertInside(FirstRunLayout.StatusTagColumn, FirstRunLayout.Status.X, FirstRunLayout.Status.Y,
            FirstRunLayout.Status.Right, FirstRunLayout.Status.Bottom);
        Assert.False(FirstRunLayout.StatusLabelColumn.Intersects(FirstRunLayout.StatusTagColumn));

        foreach (string label in FirstRunLayout.StatusLabels)
        {
            Assert.True(FirstRunLayout.MeasureWidth(label) <=
                        FirstRunLayout.TextWidth(FirstRunLayout.StatusLabelColumn),
                $"status label '{label}' is too wide");
        }

        foreach (string tag in FirstRunLayout.StatusTags)
        {
            Assert.True(FirstRunLayout.MeasureWidth(tag) <=
                        FirstRunLayout.TextWidth(FirstRunLayout.StatusTagColumn),
                $"status tag '{tag}' is too wide");
        }
    }

    [Fact]
    public void InstructionAndFooterText_FitsBandsWithSidePadding()
    {
        Assert.True(MsxScreen.MiniTextWidth(FirstRunLayout.HeaderTitle) <=
                    FirstRunLayout.TextWidth(FirstRunLayout.Header));
        Assert.True(MsxScreen.MiniTextWidth(FirstRunLayout.DefaultNotice) <=
                    FirstRunLayout.TextWidth(FirstRunLayout.Notice));
        Assert.True(MsxScreen.MiniTextWidth(FirstRunLayout.ExtractionTitle) <=
                    FirstRunLayout.TextWidth(FirstRunLayout.RomList));
        Assert.True(MsxScreen.MiniTextWidth(FirstRunLayout.ExtractionCancelHint) <=
                    FirstRunLayout.TextWidth(FirstRunLayout.RomList));

        foreach (string line in FirstRunLayout.EmptyStateLines)
        {
            var wrapped = MsxScreen.MiniTextWrap(line, FirstRunLayout.TextWidth(FirstRunLayout.RomList));
            Assert.All(wrapped, w => Assert.True(MsxScreen.MiniTextWidth(w) <=
                FirstRunLayout.TextWidth(FirstRunLayout.RomList)));
        }

        foreach (string line in FirstRunLayout.FooterLines)
        {
            Assert.True(MsxScreen.MiniTextWidth(line) <=
                        FirstRunLayout.TextWidth(FirstRunLayout.Footer),
                $"footer line '{line}' is too wide");
        }
    }

    [Fact]
    public void RomItemRects_StayInsideList_AndDoNotOverlap()
    {
        var rects = FirstRunLayout.GetRomItemRects(9); // clamped to VisibleRoms
        Assert.Equal(FirstRunLayout.VisibleRoms, rects.Count);

        for (int i = 0; i < rects.Count; i++)
        {
            AssertInside(rects[i], FirstRunLayout.RomList.X, FirstRunLayout.RomList.Y,
                FirstRunLayout.RomList.Right, FirstRunLayout.RomList.Bottom);
            if (i > 0)
                Assert.False(rects[i - 1].Intersects(rects[i]), "ROM rows overlap");
        }
    }

    [Fact]
    public void FitLine_NeverExceedsRequestedWidth()
    {
        Assert.Equal("ABCD", FirstRunLayout.FitLine("ABCD", 16));
        Assert.True(MsxScreen.MiniTextWidth(FirstRunLayout.FitLine("A VERY LONG NOTICE", 20)) <= 20);
        Assert.True(MsxScreen.MiniTextWidth(FirstRunLayout.FitLine("ABCDE", 4)) <= 4);
    }

    [Fact]
    public void FallbackFont_HasNonZeroPatternsForAllCommonCharacters()
    {
        string testChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!?:;-+=.,/\\|<>()[].'*%&#▲▼◄►•";
        foreach (char c in testChars)
        {
            byte[]? pattern = MsxScreen.GetFallbackGlyph(c);
            Assert.NotNull(pattern);
            Assert.Equal(7, pattern.Length);
            int totalBits = pattern.Sum(b => b);
            Assert.True(totalBits > 0, $"Character '{c}' has no active bits in fallback font!");
        }
    }

    private static string OnlyNonSpace(string text) => new string(text.Where(c => c != ' ').ToArray());

    private static void AssertInside(LayoutRect inner, int x, int y, int right, int bottom)
    {
        Assert.True(inner.X >= x, $"x={inner.X} < {x}");
        Assert.True(inner.Y >= y, $"y={inner.Y} < {y}");
        Assert.True(inner.Right <= right, $"right={inner.Right} > {right}");
        Assert.True(inner.Bottom <= bottom, $"bottom={inner.Bottom} > {bottom}");
    }
}
