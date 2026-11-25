using System;

namespace KingsValley.Core.Screens;

public readonly record struct OpeningLogoFrame(int OffsetY,int SourceHeight,bool EnterMenu);

/// <summary>Presentation of full native logo frames; the interactive menu owns the title.</summary>
public static class OpeningPresentation
{
    public const double Duration=48*2.0/60+258.0/60;
    public static OpeningLogoFrame At(double seconds,int nativeHeight,bool fast=false)
    {
        if(nativeHeight is not (192 or 212))throw new ArgumentOutOfRangeException(nameof(nativeHeight));
        int rows=fast?48:Math.Clamp((int)(Math.Max(0,seconds)*30),0,48);
        return new((212-nativeHeight)/2,64+rows,seconds>=(fast?0.15:Duration));
    }
}
