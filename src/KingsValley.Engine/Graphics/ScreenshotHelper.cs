#nullable enable
using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using KingsValley.Core.Common;
using Wagenheimer.MonoGameHelper.Layout;

namespace KingsValley.Engine.Graphics;

/// <summary>
/// Captures a crisp 128×106 thumbnail from the gameplay backbuffer for SaveStates.
/// Crops out any pillarbox/letterbox borders from the backbuffer.
/// </summary>
public static class ScreenshotHelper
{
    public const int ThumbWidth = 128;
    public const int ThumbHeight = 106;

    /// <summary>
    /// Captures the active viewport area from the backbuffer and returns PNG bytes.
    /// </summary>
    public static byte[]? CaptureThumbnail(GraphicsDevice? graphicsDevice)
    {
        if (graphicsDevice == null) return null;

        try
        {
            int bbWidth = graphicsDevice.PresentationParameters.BackBufferWidth;
            int bbHeight = graphicsDevice.PresentationParameters.BackBufferHeight;
            if (bbWidth <= 0 || bbHeight <= 0) return null;

            var fullPixels = new Color[bbWidth * bbHeight];
            graphicsDevice.GetBackBufferData(fullPixels);

            float scale = DesignViewport.Scale;
            float offsetX = (bbWidth - GameConstants.DesignWidth * scale) * 0.5f;
            float offsetY = (bbHeight - GameConstants.DesignHeight * scale) * 0.5f;

            int cropX = Math.Clamp((int)offsetX, 0, bbWidth - 1);
            int cropY = Math.Clamp((int)offsetY, 0, bbHeight - 1);
            int cropW = Math.Clamp((int)(GameConstants.DesignWidth * scale), 1, bbWidth - cropX);
            int cropH = Math.Clamp((int)(GameConstants.DesignHeight * scale), 1, bbHeight - cropY);

            var thumbPixels = new Color[ThumbWidth * ThumbHeight];
            for (int y = 0; y < ThumbHeight; y++)
            {
                // In OpenGL DesktopGL, the backbuffer is stored bottom-to-top; flip Y for correct orientation
                int srcY = cropY + ((ThumbHeight - 1 - y) * cropH) / ThumbHeight;
                for (int x = 0; x < ThumbWidth; x++)
                {
                    int srcX = cropX + (x * cropW) / ThumbWidth;
                    thumbPixels[y * ThumbWidth + x] = fullPixels[srcY * bbWidth + srcX];
                }
            }

            using var thumbTex = new Texture2D(graphicsDevice, ThumbWidth, ThumbHeight);
            thumbTex.SetData(thumbPixels);

            using var ms = new MemoryStream();
            thumbTex.SaveAsPng(ms, ThumbWidth, ThumbHeight);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ScreenshotHelper] Failed to capture thumbnail: {ex.Message}");
            return null;
        }
    }
}
