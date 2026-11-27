#nullable enable
using System;
using System.Collections.Generic;
using Gum.GueDeriving;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using KingsValley.Engine.Graphics;
using KingsValley.Core.Stages;
using Perf = KingsValley.Engine.Diagnostics.EditorPerformanceLog;

namespace KingsValley.Engine.Scenes;

public partial class EditorScene
{
    private sealed partial class EditorRenderer
    {
        private sealed partial class Workspace
        {
            private readonly EditorWorldCamera _worldCamera=new();
            private readonly HashSet<int> _worldPreviewValid=new();
            private RenderTarget2D? _worldCanvas;
            private bool _panning;
            private bool _worldFitPending=true;
            private Point _panPointer;
            private int _hoverRoom, _selectionRoom;
            private Point? _worldStrokeLast;
            private readonly bool _worldEditProbe=Environment.GetEnvironmentVariable("KINGSVALLEY_EDITOR_WORLD_EDIT_PROBE")=="1";
            private int _worldEditProbeUpdates;
            private static bool _probeTestLaunched;
            private static float _probeExpectedScale;

            private void ValidateWorldProbeReturn()
            {
                if(!_worldEditProbe||!_probeTestLaunched)return;
                if(!_overview||!_worldCamera.Initialized||_worldFitPending||Math.Abs(_worldCamera.Scale-_probeExpectedScale)>.001f)
                    throw new InvalidOperationException("Returning from Test did not preserve the full-stage camera.");
                Perf.Event("probe","full_stage_return_pass",$"full=true scale={_worldCamera.Scale}");
            }

            private void RunWorldEditProbe()
            {
                if(!_worldEditProbe||_probeTestLaunched||++_worldEditProbeUpdates!=120)return;
                if(!_overview||_editor._rooms.Length<2)throw new InvalidOperationException("The full-stage edit probe requires full view and at least two rooms.");
                string before=_editor.BuildJson();int roomBefore=_editor.RoomNumber,tool=_editor._tool,mode=_mode,brush=_brush;
                bool eraser=_editor._isEraser;var history=_state.History;string? savedBefore=_state.SavedJson;
                try
                {
                    int index=-1;
                    for(int y=2;y<22&&index<0;y++)for(int x=2;x<30;x++)
                        if(_editor._rooms[0][y*32+x]==TerrainKind.Empty&&_editor._rooms[1][y*32+x]==TerrainKind.Empty){index=y*32+x;break;}
                    if(index<0)throw new InvalidOperationException("The probe needs a common empty cell in rooms 1 and 2.");
                    var cell=new Point(index%32,index/32);
                    _state.SavedJson=before;_dirty=false;
                    _state.History=new EditorHistory();_stroke=before;_painted.Clear();_editor._tool=1;_editor._isEraser=false;_mode=1;_brush=1;
                    _worldCamera.Fit(_layout.Canvas,_editor._stage.RoomGrid);RefreshWorldBounds();
                    foreach(int id in new[]{1,2})
                    {
                        var bounds=_worldBounds[id];
                        var point=new Point(bounds.X+(int)((cell.X*8+4)*_worldCamera.Scale),bounds.Y+(int)((cell.Y*8+4)*_worldCamera.Scale));
                        var hit=_worldCamera.Hit(point,_editor._stage.RoomGrid);
                        if(hit==null||hit.Value.Room!=id||hit.Value.Cell!=cell)throw new InvalidOperationException("Full-stage probe hit testing selected the wrong cell.");
                        SelectWorldRoom(id);ApplyWorldStrokeCell(hit.Value.Cell,false);
                    }
                    bool dispatched=false;Guard(()=>dispatched=true);
                    if(dispatched || _modal!="confirm")throw new InvalidOperationException("An active stroke bypassed the unsaved-changes guard.");
                    _modal=null;_pending=null;
                    Perf.Event("probe","active_stroke_guard_pass","unsaved_changes_confirmed");
                    if(_editor._rooms[0][index]!=TerrainKind.Wall||_editor._rooms[1][index]!=TerrainKind.Wall)throw new InvalidOperationException("A stroke failed to edit both rooms.");
                    string after=_editor.BuildJson();Undo();
                    if(_editor.BuildJson()!=before||_state.History.CanUndo)throw new InvalidOperationException("Cross-room undo did not restore both rooms in one operation.");
                    Redo();if(_editor.BuildJson()!=after)throw new InvalidOperationException("Cross-room redo did not restore both edits.");
                    Perf.Event("probe","full_stage_edit_pass",$"rooms=1,2 cell={cell} undo=one_step redo=passed");
                    var anchor=_layout.Canvas.Center.ToVector2();var nativeAnchor=_worldCamera.ScreenToWorld(anchor);
                    _worldCamera.Zoom(true,anchor);_worldCamera.Zoom(true,anchor);
                    if(Vector2.Distance(nativeAnchor,_worldCamera.ScreenToWorld(anchor))>.01f)throw new InvalidOperationException("Zoom moved the native point under its anchor.");
                    _worldCamera.Pan(new(56,24));RefreshWorldBounds();
                    Perf.Event("probe","full_stage_camera_pass","zoom_anchor=passed pan=applied render_clipped_to_canvas");
                }
                finally
                {
                    _stroke=null;_painted.Clear();RestoreJson(before);_state.History=history;_state.SavedJson=savedBefore;
                    _editor._tool=tool;_editor._isEraser=eraser;_mode=mode;_brush=brush;SelectWorldRoom(roomBefore);_selection=null;_rebuild=true;
                }
                _probeTestLaunched=true;_probeExpectedScale=_worldCamera.Scale;
                Test();
                Nez.Core.Schedule(2f,_=>
                {
                    Perf.Event("probe","full_stage_test_return_requested",Nez.Core.Scene?.GetType().Name);
                    KingsValley.Engine.Navigation.GameNavigation.StageOverride=null;
                    KingsValley.Engine.Navigation.GameNavigation.GoTo(KingsValley.Engine.Navigation.GameSceneId.Editor);
                });
            }

            public void InvalidateWorldRoom(int room)
            { _worldPreviewValid.Remove(room);_worldDirty=true; }
            private void InvalidateWorld()
            { _worldPreviewValid.Clear();_worldDirty=true; }
            public void OnGraphicsReloaded()
            {InvalidateWorld();_rebuild=true;}
            private void RefreshWorldBounds()
            {
                _worldBounds.Clear();
                foreach(var entry in _worldCamera.VisibleRooms(_editor._stage.RoomGrid))_worldBounds[entry.Key]=entry.Value;
                _worldDirty=true;
            }
            private void FitWorld()
            {
                FinishStroke();_worldCamera.Fit(_layout.Canvas,_editor._stage.RoomGrid);_worldFitPending=false;
                RefreshWorldBounds();_rebuild=true;
            }
            private void ToggleWorld()
            {
                FinishStroke();_selection=null;_overview=!_overview;
                if(_overview)FitWorld();else _rebuild=true;
            }
            private void Zoom(bool inward)
            {
                FinishStroke();
                if(_overview)
                {
                    var anchor=_layout.Canvas.Contains(Pointer)?Pointer:_layout.Canvas.Center;
                    _worldCamera.Zoom(inward,anchor.ToVector2());RefreshWorldBounds();
                }
                else if(!inward&&_layout.Zoom==1){_overview=true;FitWorld();}
                else _zoom=Math.Max(1,_layout.Zoom+(inward?1:-1));
                _rebuild=true;
            }
            private bool UpdateWorldPan()
            {
                bool held=_mouse.MiddleButton==ButtonState.Pressed || (_keys.IsKeyDown(Keys.Space)&&_mouse.LeftButton==ButtonState.Pressed);
                if(!held){_panning=false;return false;}
                if(!_panning)
                {
                    if(!_layout.Canvas.Contains(Pointer))return false;
                    FinishStroke();_selectionStart=null;_panning=true;_panPointer=Pointer;_worldDirty=true;
                }
                var delta=Pointer-_panPointer;_panPointer=Pointer;
                if(delta!=Point.Zero){_worldCamera.Pan(delta);RefreshWorldBounds();}
                _hoverCell=null;return true;
            }
            private void SelectWorldRoom(int room,bool center=false)
            {
                if(_editor.RoomNumber!=room)
                {
                    _editor._room=room-1;
                    if(_selectionRoom!=room)_selection=null;
                    _rebuild=true;_worldDirty=true;
                }
                if(center&&_overview){_worldCamera.CenterRoom(room,_editor._stage.RoomGrid);RefreshWorldBounds();}
            }
            private void UpdateWorldInput()
            {
                if(UpdateWorldPan())return;
                if(_hoverCell==null)_worldStrokeLast=null;
                bool drawing=_mouse.LeftButton==ButtonState.Pressed||_mouse.RightButton==ButtonState.Pressed;
                bool paste=Control&&Pressed(Keys.V);
                if(_hoverCell!=null && _hoverRoom>0 && (drawing||paste))
                {
                    if(_mode==0&&_selectionStart!=null&&_selectionRoom!=_hoverRoom)_hoverCell=null;
                    else SelectWorldRoom(_hoverRoom);
                    if(_hoverCell is {} cell)_pasteAt=cell;
                }
                UpdateCanvasInput();
            }
            private void ApplyWorldStrokeCell(Point cell,bool erase)
            {
                if(_mode==2){ApplyStrokeCell(cell,erase);return;}
                int destination=_editor.RoomNumber;
                var point=EditorWorldCamera.StageCell(destination,cell,_editor._stage.RoomGrid);
                try
                {
                    foreach(var stageCell in EditorWorldCamera.StrokeCells(_worldStrokeLast??point,point))
                    {
                        var hit=EditorWorldCamera.HitStageCell(stageCell,_editor._stage.RoomGrid);if(hit==null)continue;
                        _editor._room=hit.Value.Room-1;ApplyStrokeCell(hit.Value.Cell,erase);
                    }
                }
                finally{_editor._room=destination-1;}
                _worldStrokeLast=point;
            }
            private void DrawWorld()
            {
                if(!_worldCamera.Initialized||_worldFitPending){_worldCamera.Fit(_layout.Canvas,_editor._stage.RoomGrid);_worldFitPending=false;}
                else if(_worldCamera.Viewport!=_layout.Canvas)_worldCamera.Resize(_layout.Canvas);
                RefreshWorldBounds();
                int width=_layout.Canvas.Width*_uiScale,height=_layout.Canvas.Height*_uiScale;
                if(_worldCanvas==null||_worldCanvas.Width!=width||_worldCanvas.Height!=height)
                {
                    _worldCanvas?.Dispose();_worldCanvas=new RenderTarget2D(Nez.Core.GraphicsDevice,width,height);
                }
                var visual=new SpriteRuntime{Texture=_worldCanvas,TextureWidth=width,TextureHeight=height};
                Position(visual,_layout.Canvas);_root!.AddChild(visual);
                _status="FULL: paint directly / Wheel: zoom / Space + drag or middle button: pan / Click zoom value: fit";
            }
            private Rectangle CanvasPixels(Rectangle bounds) => new(
                (bounds.X-_layout.Canvas.X)*_uiScale,(bounds.Y-_layout.Canvas.Y)*_uiScale,bounds.Width*_uiScale,bounds.Height*_uiScale);
            private Rectangle WorldCellPixels(int room,Rectangle cells)
            {
                var bounds=_worldBounds[room];float unit=8*_worldCamera.Scale;
                return new((int)MathF.Round((bounds.X-_layout.Canvas.X+cells.X*unit)*_uiScale),
                    (int)MathF.Round((bounds.Y-_layout.Canvas.Y+cells.Y*unit)*_uiScale),
                    Math.Max(1,(int)MathF.Round(cells.Width*unit*_uiScale)),Math.Max(1,(int)MathF.Round(cells.Height*unit*_uiScale)));
            }
            private void ComposeWorld()
            {
                var device=Nez.Core.GraphicsDevice;device.SetRenderTarget(_worldCanvas);device.Clear(new Color(7,12,21));
                _batcher.Begin(BlendState.AlphaBlend,SamplerState.PointClamp,DepthStencilState.None,RasterizerState.CullNone);
                foreach(var entry in _worldBounds)
                {
                    if(!_worldPreviews.TryGetValue(entry.Key,out var texture))continue;
                    var rect=CanvasPixels(entry.Value);_batcher.Draw(texture,rect,Color.White);
                    bool active=entry.Key==_editor.RoomNumber;
                    Outline(_batcher,rect,active?Gold:Border);
                    var badge=new Rectangle(rect.X,rect.Y,24*_uiScale,12*_uiScale);
                    MsxScreen.Fill(_batcher,badge,active?Gold:Panel);
                    string id=entry.Key.ToString("00");
                    for(int i=0;i<id.Length;i++)MsxScreen.Draw(_batcher,"digit_"+id[i],rect.X+(3+i*8)*_uiScale,rect.Y+2*_uiScale,color:active?Background:TextColor,scale:_uiScale);
                }
                if(_selection is {} selection && _worldBounds.ContainsKey(_selectionRoom))Outline(_batcher,WorldCellPixels(_selectionRoom,selection),Gold);
                if(_hoverCell is {} cell && _worldBounds.ContainsKey(_hoverRoom)&&_modal==null&&!_panning)
                    Outline(_batcher,WorldCellPixels(_hoverRoom,new(cell.X,cell.Y,1,1)),Color.White);
                _batcher.End();
            }
        }
    }
}
