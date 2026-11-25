#nullable enable
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.Screens;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Navigation;

namespace KingsValley.Engine.Scenes;

/// <summary>
/// Reveals the native full-frame Konami logo, then enters the interactive title directly.
/// The captured title already includes the complete menu box; do not add a static 1.9-second
/// imitation of the original box animation without its cursor or input handling.
/// </summary>
public class SplashScene : SceneBase
{
    private static (Texture2D? Texture,Color Backdrop) LoadLogo()
    {
        string path=RomContent.ScreenPath("konami");
        if(!System.IO.File.Exists(path))return (null,Color.Black);
        var data=ScreenAssembler.DecodeFile(path);var color=data.Palette[data.Indices[0]];
        return (ScreenBuilder.BuildTexture(Nez.Core.GraphicsDevice,data),new Color(color.R,color.G,color.B,color.A));
    }
    protected override void OnInitialize()
    {
        var logo=LoadLogo();
        bool fast=System.Environment.GetEnvironmentVariable("KINGSVALLEY_FAST_BOOT")=="1";
        AddEntity(new Entity("splash")).AddComponent(new SplashRenderer(fast,logo.Texture,logo.Backdrop));
    }
    public override void OnGfxVariantChanged()=>FindEntity("splash")?.GetComponent<SplashRenderer>()?.ReloadGraphics();

    private sealed class SplashRenderer : RenderableComponent,IUpdatable
    {
        private readonly bool _fast;
        private Texture2D? _logo;
        private Color _backdrop;
        private float _time;
        public SplashRenderer(bool fast,Texture2D? logo,Color backdrop){_fast=fast;_logo=logo;_backdrop=backdrop;}
        public void ReloadGraphics(){_logo?.Dispose();var logo=LoadLogo();_logo=logo.Texture;_backdrop=logo.Backdrop;}
        public override RectangleF Bounds=>new(0,0,GameConstants.DesignWidth,GameConstants.DesignHeight);
        private OpeningLogoFrame Frame=>OpeningPresentation.At(_time,_logo?.Height??(RomContent.Flavor==ArtFlavor.Msx1?192:212),_fast);
        public void Update()
        {
            _time+=Time.DeltaTime;
            bool skip=Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Enter)
                || Nez.Input.IsKeyPressed(Microsoft.Xna.Framework.Input.Keys.Space)
                || KingsValley.Engine.Input.UnifiedPointer.IsJustPressed;
            foreach(var pad in Nez.Input.GamePads)
                if(pad.IsConnected() && (pad.IsButtonPressed(Microsoft.Xna.Framework.Input.Buttons.A)
                    || pad.IsButtonPressed(Microsoft.Xna.Framework.Input.Buttons.Start)))skip=true;
            if(skip || Frame.EnterMenu){GameNavigation.GoTo(GameSceneId.MainMenu);Entity?.Destroy();}
        }
        public override void Render(Batcher batcher,Camera camera)
        {
            MsxScreen.Fill(batcher,new Rectangle(0,0,GameConstants.DesignWidth,GameConstants.DesignHeight),Color.Black);
            if(_logo==null){MsxScreen.Text(batcher,"KONAMI",104,100,Color.White);return;}
            var frame=Frame;
            // The native bitmap already contains its own X/Y artwork placement. Resolve only
            // the MSX1 letterbox and clear to the backdrop sampled from that same ROM screen.
            MsxScreen.Fill(batcher,new Rectangle(0,frame.OffsetY,_logo.Width,_logo.Height),_backdrop);
            var source=new Rectangle(0,0,_logo.Width,frame.SourceHeight);
            batcher.Draw(_logo,new Vector2(0,frame.OffsetY),source,Color.White,0f,Vector2.Zero,Vector2.One,SpriteEffects.None,0f);
        }
    }
}
