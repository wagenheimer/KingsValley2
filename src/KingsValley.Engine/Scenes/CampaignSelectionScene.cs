using Microsoft.Xna.Framework;
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

/// <summary>Application campaign/challenge selection. Native stage data and artwork remain cartridge-specific.</summary>
public sealed class CampaignSelectionScene : SceneBase
{
    private readonly bool _stages;
    private readonly bool _showContestTitle;
    public CampaignSelectionScene(bool stages=false,bool showContestTitle=true){_stages=stages;_showContestTitle=showContestTitle;}
    protected override void OnInitialize()
    {
        var entity=AddEntity(new Entity("campaign-selection"));
        if(_stages)entity.AddComponent(new ContestNativeMenu(_showContestTitle));
        else entity.AddComponent(new Menu(false));
    }
    private sealed class Menu : RenderableComponent,IUpdatable
    {
        private bool _stages;private int _selected;private bool _back;
        private ApplicationMenuCanvas _canvas;
        private global::Gum.GueDeriving.SpriteRuntime _visual;
        private string _renderKey="";
        public override void OnAddedToEntity()
        {
            base.OnAddedToEntity();_canvas=new ApplicationMenuCanvas();
            _visual=new global::Gum.GueDeriving.SpriteRuntime
            {
                Width=256,Height=212,WidthUnits=global::Gum.DataTypes.DimensionUnitType.Absolute,
                HeightUnits=global::Gum.DataTypes.DimensionUnitType.Absolute,
                Texture=_canvas.Texture,TextureWidth=256,TextureHeight=212
            };
            _visual.AddToRoot();
        }
        public override void OnRemovedFromEntity()
        {
            _visual?.RemoveFromRoot();_canvas?.Dispose();base.OnRemovedFromEntity();
        }
        public Menu(bool stages){_stages=stages;_selected=!stages && RomContent.IsContest?1:0;RenderLayer=RenderLayers.SystemMenu;}
        public override RectangleF Bounds=>new(0,0,256,212);
        private Rectangle StageRect(int stage)=>new(16+((stage-1)%5)*46,52+((stage-1)/5)*32,40,20);
        private Rectangle ChoiceRect(int i)=>new(16,62+i*48,224,36);
        private static Rectangle BackRect=>new(16,187,72,16);
        public void Update()
        {
            _visual.Visible=!UI.SystemMenu.IsOpen;
            if(Nez.Core.Instance is {IsActive:false} || Environment.GetEnvironmentVariable("KINGSVALLEY_IGNORE_INPUT")=="1")return;
            if(UI.SystemMenu.IsOpen)return;
            bool up=Nez.Input.IsKeyPressed(Keys.Up),down=Nez.Input.IsKeyPressed(Keys.Down),left=Nez.Input.IsKeyPressed(Keys.Left),right=Nez.Input.IsKeyPressed(Keys.Right);
            bool confirm=Nez.Input.IsKeyPressed(Keys.Enter)||Nez.Input.IsKeyPressed(Keys.Space),cancel=Nez.Input.IsKeyPressed(Keys.Escape);
            foreach(var pad in Nez.Input.GamePads)
            {
                if(!pad.IsConnected())continue;
                up|=pad.IsButtonPressed(Buttons.DPadUp);down|=pad.IsButtonPressed(Buttons.DPadDown);
                left|=pad.IsButtonPressed(Buttons.DPadLeft);right|=pad.IsButtonPressed(Buttons.DPadRight);
                confirm|=pad.IsButtonPressed(Buttons.A);cancel|=pad.IsButtonPressed(Buttons.B);
            }
            if(cancel){Back();return;}
            if(_stages)
            {
                if(_back && up){_back=false;_selected=15;}
                else if(!_back)
                {
                    if(up)_selected=Math.Max(0,_selected-5);
                    if(down){if(_selected>=15)_back=true;else _selected+=5;}
                    if(left)_selected=Math.Max(0,_selected-1);
                    if(right)_selected=Math.Min(19,_selected+1);
                }
            }
            else if(up||down)_selected=1-_selected;
            var point=UnifiedPointer.Position;bool click=UnifiedPointer.IsJustPressed;
            if(UnifiedPointer.HasMoved || click)
            {
                if(BackRect.Contains(point)){_back=true;if(click){Back();return;}}
                else for(int i=0;i<(_stages?20:2);i++)
                {
                    if(!(_stages?StageRect(i+1):ChoiceRect(i)).Contains(point))continue;
                    _back=false;_selected=i;if(click)confirm=true;break;
                }
            }
            if(!confirm)return;
            if(_back){Back();return;}
            if(_stages)
            {
                int hi=ProfileSaveService.Load().ProgressFor(Campaigns.Contest).HiScore;
                GameNavigation.GoToGame(RomContent.NewRun(hi,_selected+1));return;
            }
            string campaign=_selected==0?Campaigns.Original:Campaigns.Contest;
            var candidates=RomContent.Available.Where(s=>s.CampaignId==campaign && RomContent.IsVariantAvailable(s.Variant)).ToArray();
            if(candidates.Length==0){Nez.Core.Scene=new FirstRunScene("Extract a cartridge for this campaign first. Use BROWSE, then EXTRACT SELECTED.");return;}
            string preferred=campaign==Campaigns.Original?ProfileSaveService.Load().OriginalGfxRom:CartridgeVariants.Contest;
            var set=candidates.FirstOrDefault(s=>s.Id==preferred)??candidates[0];GfxVariant.Set(set.Id);
            if(campaign==Campaigns.Contest)Nez.Core.Scene=new CampaignSelectionScene(stages:true);
            else Nez.Core.Scene=new MainMenuScene(startSelect:true);
        }
        private void Back()
        {
            if(_stages){_stages=false;_selected=1;_back=false;return;}
            var original=RomContent.Available.FirstOrDefault(s=>s.CampaignId==Campaigns.Original);
            if(original!=null){GfxVariant.Set(original.Id);GameNavigation.GoTo(GameSceneId.MainMenu);}
            else Nez.Core.Scene=new FirstRunScene();
        }
        private void Box(Rectangle r,bool selected,bool enabled=true)
        {
            _canvas.Fill(r,selected?new Color(40,75,140):new Color(12,24,72));
            Color color=selected?Color.Gold:enabled?new Color(90,120,180):Color.DimGray;
            _canvas.Fill(new(r.X,r.Y,r.Width,1),color);_canvas.Fill(new(r.X,r.Bottom-1,r.Width,1),color);
            _canvas.Fill(new(r.X,r.Y,1,r.Height),color);_canvas.Fill(new(r.Right-1,r.Y,1,r.Height),color);
        }
        public override void Render(Batcher b,Camera camera)
        {
            string key=$"{_stages}/{_selected}/{_back}/{RomContent.IsContestAvailable}/{RomContent.IsMsx1Available}/{RomContent.IsMsx2Available}";
            if(_renderKey==key)return;
            _renderKey=key;
            _canvas.Clear(new Color(0,0,40));
            string title=_stages?"CONTEST STAGES":"SELECT CAMPAIGN";
            _canvas.Text(title,(256-title.Length*12)/2,12,Color.Gold,2);
            _canvas.Mini(_stages?"FOUR GROUPS. FIVE CHALLENGES EACH.":"CHOOSE WHICH CARTRIDGE'S STAGES TO PLAY.",24,32,Color.White);
            Box(BackRect,_back);_canvas.Mini("BACK",40,193,Color.White);
            _canvas.Mini("ARROWS SELECT  ENTER PLAY",112,193,Color.LightBlue);
            if(_stages)
            {
                var done=ProfileSaveService.Load().ProgressFor(Campaigns.Contest).CompletedLevels;
                for(int group=0;group<4;group++)_canvas.Mini($"GROUP {group+1}",16,44+group*32,Color.LightBlue);
                for(int stage=1;stage<=20;stage++)
                {
                    var rect=StageRect(stage);Box(rect,!_back && _selected==stage-1);
                    string number=stage.ToString("00");_canvas.Text(number,rect.X+14,rect.Y+4,Color.White);
                    if(done.Contains(stage))_canvas.Mini("DONE",rect.X+12,rect.Bottom-6,Color.LimeGreen);
                }
            }
            else
            {
                for(int i=0;i<2;i++)
                {
                    bool available=i==0?RomContent.IsMsx1Available||RomContent.IsMsx2Available:RomContent.IsContestAvailable;
                    var r=ChoiceRect(i);Box(r,!_back && _selected==i,available);
                    _canvas.Text(i==0?"ORIGINAL":"CONTEST",r.X+12,r.Y+5,available?Color.White:Color.Gray,2);
                    _canvas.Mini((i==0?"60 ORIGINAL STAGES":"20 CONTEST CHALLENGES")+(available?"  READY":"  EXTRACT ROM FIRST"),r.X+12,r.Y+23,Color.LightBlue);
                }
            }
            _canvas.Commit();
        }
    }
}
