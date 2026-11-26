namespace KingsValley.Engine.UI;

/// <summary>Bounds variable-length setting values inside the native 256-pixel application menu.</summary>
internal readonly record struct SystemMenuValueLayout(string Text,int X,int Width,bool Compact)
{
    public static SystemMenuValueLayout Fit(string label,string value,int labelX,int right)
    {
        int left=labelX+label.Length*8+12;
        int available=Math.Max(0,right-left);
        bool compact=value.Length*8>available;
        int advance=compact?4:8;
        int capacity=available/advance;
        if(value.Length>capacity)value=capacity>=3?value[..(capacity-3)]+"...":value[..capacity];
        int width=value.Length*advance;
        return new(value,right-width,width,compact);
    }
}
