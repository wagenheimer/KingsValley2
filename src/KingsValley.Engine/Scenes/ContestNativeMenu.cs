using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Nez;
using KingsValley.Core.Common;
using KingsValley.Core.SaveSystem;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Graphics;
using KingsValley.Engine.Input;
using KingsValley.Engine.Navigation;
using Wagenheimer.MonoGameHelper.Input;

namespace KingsValley.Engine.Scenes;

/// <summary>Native Contest publication → author → SCREEN SELECT flow, displayed through Gum.</summary>
internal sealed class ContestNativeMenu : Component,IUpdatable
{
    private readonly bool _showTitle;
    public ContestNativeMenu(bool showTitle=true)=>_showTitle=showTitle;
    private int _page,_group,_entry,_theme=1;
    private float _titleTime;
    private string _screen="";
    private Texture2D _texture;
    private global::Gum.GueDeriving.SpriteRuntime _visual;
    public override void OnAddedToEntity()
    {
        base.OnAddedToEntity();
        _visual=new global::Gum.GueDeriving.SpriteRuntime
        {
            Width=256,Height=212,WidthUnits=global::Gum.DataTypes.DimensionUnitType.Absolute,
            HeightUnits=global::Gum.DataTypes.DimensionUnitType.Absolute,TextureWidth=256,TextureHeight=212
        };
        _page=Environment.GetEnvironmentVariable("KINGSVALLEY_CONTEST_MENU") switch {"publication"=>0,"author"=>1,"theme"=>2,_=>_showTitle?-1:0};
        _visual.AddToRoot();Refresh();
    }
    public override void OnRemovedFromEntity()
    {
        _visual?.RemoveFromRoot();_texture?.Dispose();base.OnRemovedFromEntity();
    }
    private void Refresh()
    {
        string name=_page switch
        {
            -1=>"title",0=>$"contest_publication_{_group}",1=>$"contest_author_{_group}_{_entry}",_=>$"contest_theme_{_theme}"
        };
        if(_screen==name)return;
        var next=ScreenBuilder.LoadTexture(Nez.Core.GraphicsDevice,RomContent.ScreenPath(name));
        if(next==null)throw new InvalidDataException($"Missing native Contest menu: {name}. Re-extract the Contest cartridge.");
        _visual.Texture=next;_texture?.Dispose();_texture=next;_screen=name;
        Console.WriteLine($"[ContestMenu] {name}");
    }
    public void Update()
    {
        _visual.Visible=!UI.SystemMenu.IsOpen;
        if(UI.SystemMenu.IsOpen || Nez.Core.Instance is {IsActive:false} || Environment.GetEnvironmentVariable("KINGSVALLEY_IGNORE_INPUT")=="1")return;
        bool up=Nez.Input.IsKeyPressed(Keys.Up),down=Nez.Input.IsKeyPressed(Keys.Down);
        bool left=Nez.Input.IsKeyPressed(Keys.Left),right=Nez.Input.IsKeyPressed(Keys.Right);
        bool confirm=Nez.Input.IsKeyPressed(Keys.Enter)||Nez.Input.IsKeyPressed(Keys.Space);
        bool back=Nez.Input.IsKeyPressed(Keys.Escape);
        foreach(var pad in Nez.Input.GamePads)
        {
            if(!pad.IsConnected())continue;
            up|=pad.IsButtonPressed(Buttons.DPadUp);down|=pad.IsButtonPressed(Buttons.DPadDown);
            left|=pad.IsButtonPressed(Buttons.DPadLeft);right|=pad.IsButtonPressed(Buttons.DPadRight);
            confirm|=pad.IsButtonPressed(Buttons.A);back|=pad.IsButtonPressed(Buttons.B);
        }
        if(_page<0)
        {
            // Native 4647/453D: title counter 0x20, decremented once per ROM logic call.
            _titleTime+=Time.DeltaTime;
            if(confirm||UnifiedPointer.IsJustPressed||_titleTime>=32f*GameConstants.FramesPerTick/60f)_page=0;
            Refresh();return;
        }
        if(back)
        {
            if(_page==0){Nez.Core.Scene=new CampaignSelectionScene();return;}
            _page--;Refresh();return;
        }
        int direction=(down||right?1:0)-(up||left?1:0);
        if(_page==0)_group=(_group+direction+4)%4;
        else if(_page==1)_entry=(_entry+direction+5)%5;
        else _theme=(_theme-1-direction+6)%6+1; // Native UP advances E241; DOWN goes back.
        if(_page<2 && (UnifiedPointer.HasMoved||UnifiedPointer.IsJustPressed))
        {
            var p=UnifiedPointer.Position;
            int top=_page==0?24:32,step=_page==0?40:32,count=_page==0?4:5;
            for(int i=0;i<count;i++)
            {
                if(!new Rectangle(24,top+i*step,216,32).Contains(p))continue;
                if(_page==0)_group=i;else _entry=i;
                confirm|=UnifiedPointer.IsJustPressed;break;
            }
        }
        else if(_page==2 && UnifiedPointer.IsJustPressed)
        {
            var p=UnifiedPointer.Position;
            if(new Rectangle(56,48,144,24).Contains(p))confirm=true;
            else if(new Rectangle(80,80,96,48).Contains(p))_theme=_theme%6+1;
        }
        if(confirm)
        {
            if(_page<2)_page++;
            else
            {
                int hi=ProfileSaveService.Load().ProgressFor(Campaigns.Contest).HiScore;
                GameNavigation.GoToGame(RomContent.NewRun(hi,_group*5+_entry+1) with {ContestPyramid=_theme});return;
            }
        }
        Refresh();
    }
}
