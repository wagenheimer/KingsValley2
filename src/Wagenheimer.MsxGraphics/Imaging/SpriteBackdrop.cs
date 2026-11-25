namespace Wagenheimer.MsxGraphics.Imaging;

public static class SpriteBackdrop
{
    /// <summary>Creates an optional presentation copy; enclosed black artwork remains opaque.</summary>
    public static byte[] RemoveConnectedBlack(byte[] rgba,int width,int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if(width<=0 || height<=0 || (long)width*height*4!=rgba.Length)
            throw new ArgumentException("The RGBA dimensions do not match the sprite.");
        byte[] result=(byte[])rgba.Clone();var visited=new bool[width*height];var queue=new Queue<int>();
        void Add(int x,int y)
        {
            if(x<0 || x>=width || y<0 || y>=height)return;
            int i=y*width+x,p=i*4;
            if(visited[i] || (result[p+3]!=0 && (result[p]!=0 || result[p+1]!=0 || result[p+2]!=0)))return;
            visited[i]=true;queue.Enqueue(i);
        }
        for(int x=0;x<width;x++){Add(x,0);Add(x,height-1);}
        for(int y=0;y<height;y++){Add(0,y);Add(width-1,y);}
        while(queue.TryDequeue(out int i))
        {
            Array.Clear(result,i*4,4);int x=i%width,y=i/width;
            Add(x-1,y);Add(x+1,y);Add(x,y-1);Add(x,y+1);
        }
        return result;
    }
}
