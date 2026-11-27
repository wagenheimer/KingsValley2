#nullable enable
using Perf = KingsValley.Engine.Diagnostics.EditorPerformanceLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using KingsValley.Core.Stages;
using KingsValley.Engine.Assets;
using KingsValley.Engine.Navigation;
using KingsValley.Engine.Platform;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace KingsValley.Engine.Scenes;

public partial class EditorScene
{
    private sealed partial class EditorRenderer
    {
        private sealed partial class Workspace
        {
            private sealed record ClipboardRegion(string Json, int Room, Rectangle Cells);

            private void Guard(Action action)
            {
                FinishStroke();
                if (!Dirty) { action(); return; }
                _pending = action; _modal = "confirm"; _rebuild = true;
            }
            private void DeleteRecovery() { if (File.Exists(RecoveryPath)) File.Delete(RecoveryPath); }
            private void RestoreJson(string json)
            {
                var stage = StageLoader.Parse(json);
                _editor.ApplyLoadedStage(stage, stage.Number, Math.Min(_editor.RoomNumber, stage.Rooms.Count));
                _selection = null; InvalidateWorld(); _rebuild = true;
            }
            private void Undo() { FinishStroke(); RestoreJson(_state.History.Undo(_editor.BuildJson())); _status = "Undo"; }
            private void Redo() { FinishStroke(); RestoreJson(_state.History.Redo(_editor.BuildJson())); _status = "Redo"; }
            private void SetPyramid(int pyramid)
            {
                _editor._editorPyramid = pyramid; _editor._stage = _editor._stage.WithPyramid(pyramid);
                _editor.ReloadGraphics(); _status = $"Theme: Pyramid {pyramid}";
            }
            private void OpenExternal()
            {
                string? file = LevelFileDialog.Pick(false); _ignoreFrames = 3;
                if (file == null) return;
                LoadDocument(LevelDocument.Load(file, RomContent.Variant), file, saved: true);
            }
            private bool SaveExternal(bool saveAs)
            {
                FinishStroke();
                string? file = !saveAs ? _state.Path : null;
                if (file == null)
                {
                    file = LevelFileDialog.Pick(true, _state.Name + LevelDocument.Extension); _ignoreFrames = 3;
                    if (file == null) return false;
                    if (string.IsNullOrEmpty(Path.GetExtension(file))) file += LevelDocument.Extension;
                }
                string name = Path.GetFileNameWithoutExtension(file);
                var document = new LevelDocument(name, RomContent.Variant, _editor.BuildJson());
                document.Save(file, ProtectedRoots);
                _state.Name = name; _state.Path = Path.GetFullPath(file); _state.SavedJson = _editor.BuildJson();
                DeleteRecovery(); _status = "Saved: " + Path.GetFileName(file); _rebuild = true;
                return true;
            }
            private void LoadDocument(LevelDocument document, string? file, bool saved)
            {
                using var timing=Perf.Measure("document.open",$"variant={document.SourceVariant}");
                var source = EditorTemplateCatalog.Available().FirstOrDefault(s => s.Variant == document.SourceVariant);
                if (source == null) throw new InvalidOperationException("This level needs " + document.SourceVariant.ToUpperInvariant() + " graphics. Extract that cartridge first.");
                var stage = document.Stage;
                var set = RomContent.Available.First(s => s.Variant == source.Variant && RomContent.IsVariantAvailable(s.Variant));
                var scene = (EditorScene)_editor.Entity.Scene;
                scene._loadingTemplate = true;
                try { GfxVariant.Set(set.Id); } finally { scene._loadingTemplate = false; }
                _editor._bgBuilder?.Reset();
                foreach (var texture in _editor._roomTextures.Values) texture.Dispose();
                _editor._roomTextures.Clear();
                _editor.ApplyLoadedStage(stage, stage.Number, stage.PlayerStart.Room);
                _state.Path = file; _state.Name = document.Name; _state.History.Reset();
                _state.SavedJson = saved ? _editor.BuildJson() : "";
                _modal = null; _selection = null; _pending = null; InvalidateWorld(); _worldFitPending=true; _rebuild = true;
                _status = saved ? "Opened: " + document.Name : "Game template copied. Use Save As to keep your own level.";
            }
            private void LoadTemplate(EditorTemplateSource source, int number)
            {
                string json = File.ReadAllText(Path.Combine(source.Directory, $"stage{number:00}.json"));
                LoadDocument(LevelDocument.Parse(json, source.Variant) with { Name = (source.Variant == "msx2-contest" ? "Contest " : "Stage ") + number.ToString("00") + " copy" }, null, false);
            }
            private void NewDocument()
            {
                var root = JsonNode.Parse(_editor.BuildJson())!;
                root["stage"] = 1;
                var grid = new JsonArray();
                for (int row = 0; row < 8; row++) grid.Add(new JsonArray(Enumerable.Range(0,8).Select(col => (JsonNode?)JsonValue.Create(row == 3 && col == 3 ? 1 : 0)).ToArray()));
                root["roomGrid"] = grid;
                var rows = new JsonArray();
                for (int y = 0; y < 24; y++) rows.Add(new string(Enumerable.Range(0,32).Select(x => x < 2 || x >= 30 || y < 2 || y >= 22 ? '#' : '.').ToArray()));
                root["rooms"] = new JsonArray(new JsonObject { ["index"] = 1, ["rows"] = rows });
                foreach (string key in new[] { "items", "coffins", "mechanisms", "soulStones", "ladders", "goldenLadders" }) root[key] = new JsonArray();
                root["playerStart"] = new JsonObject { ["room"] = 1, ["x"] = 32, ["y"] = 160 };
                root["exitDoor"] = new JsonObject { ["room"] = 1, ["x"] = 208, ["y"] = 144, ["w"] = 4, ["h"] = 4 };
                LoadDocument(new LevelDocument("Untitled", RomContent.Variant, root.ToJsonString()), null, false);
            }
            private void Test()
            {
                Perf.Input("Test",$"stage={_editor._currentStageNumber} room={_editor.RoomNumber}");
                using var timing=Perf.Measure("test.launch");
                FinishStroke(); LevelDocument.Validate(_editor.BuildJson());
                _status = "Test mode: press Escape or F5 to return to the editor.";
                _editor.PlayTest();
            }

            private void UpdateCanvasInput()
            {
                if (Control)
                {
                    if (Pressed(Keys.S)) { SaveExternal(Shift); return; }
                    if (Pressed(Keys.O)) { Guard(OpenExternal); return; }
                    if (Pressed(Keys.N)) { Guard(NewDocument); return; }
                    if (Pressed(Keys.Z)) { if (Shift) Redo(); else Undo(); return; }
                    if (Pressed(Keys.Y)) { Redo(); return; }
                    if (Pressed(Keys.C)) { CopySelection(); return; }
                    if (Pressed(Keys.V)) { Change(Paste); return; }
                }
                if (Pressed(Keys.Escape)) { Guard(()=>{DeleteRecovery();GameNavigation.GoTo(GameSceneId.MainMenu);}); return; }
                if (Pressed(Keys.F5) || Pressed(Keys.P)) { Test(); return; }
                if (Pressed(Keys.B)) { _mode=1;_rebuild=true; }
                if (Pressed(Keys.F)) { _mode=2;_rebuild=true; }
                if (Pressed(Keys.V)) { _mode=0;_rebuild=true; }
                if (Pressed(Keys.E)) { _editor._isEraser=true;_mode=1;_rebuild=true; }
                if (Pressed(Keys.G)) { _editor._showGrid=!_editor._showGrid;InvalidateWorld();_rebuild=true; }
                bool leftDown = _mouse.LeftButton == ButtonState.Pressed, rightDown = _mouse.RightButton == ButtonState.Pressed;
                bool justDown = leftDown && _previousMouse.LeftButton != ButtonState.Pressed || rightDown && _previousMouse.RightButton != ButtonState.Pressed;
                if (justDown && _hoverCell is { } start)
                {
                    _stroke = _editor.BuildJson(); _painted.Clear();_worldStrokeLast=null;
                    if (_mode == 0 && !rightDown) { _selectionRoom=_editor.RoomNumber;_selectionStart=start;_selection=new(start.X,start.Y,1,1); }
                }
                if ((leftDown || rightDown) && _hoverCell is { } cell)
                {
                    if(_overview&&_selectionStart!=null&&_selectionRoom!=_editor.RoomNumber)return;
                    _pasteAt = cell;
                    if (_mode == 0 && !rightDown && _selectionStart is { } anchor)
                        _selection = new Rectangle(Math.Min(anchor.X,cell.X),Math.Min(anchor.Y,cell.Y),Math.Abs(anchor.X-cell.X)+1,Math.Abs(anchor.Y-cell.Y)+1);
                    else if(_overview)ApplyWorldStrokeCell(cell,rightDown||_editor._isEraser);
                    else ApplyStrokeCell(cell,rightDown||_editor._isEraser);
                }
                if (!leftDown && !rightDown)
                {
                    if(_selectionStart is {} selected && _selection is {Width:1,Height:1}) PickObject(selected);
                    FinishStroke(); _selectionStart=null;
                }
                if (Pressed(Keys.Delete) && _selection is { } selection) Change(()=>
                {
                    for (int y=selection.Top;y<selection.Bottom;y++) for(int x=selection.Left;x<selection.Right;x++) _editor.EraseAt(x,y);
                });
            }
            private void FinishStroke()
            {
                if (_stroke == null) return;
                string after = _editor.BuildJson(); _state.History.Record(_stroke, after);
                _dirty=_state.SavedJson!=after;
                if (_stroke != after) _rebuild=true;
                _stroke=null;_painted.Clear();_worldStrokeLast=null;
            }
            private void ApplyStrokeCell(Point cell,bool erase)
            {
                if(_stroke==null||!_painted.Add((_editor.RoomNumber,cell)))return;
                if(_mode==2&&_painted.Count==1)Fill(cell,erase);
                else if(_mode!=2)Paint(cell,erase);
            }
            private void PickObject(Point cell)
            {
                int room=_editor.RoomNumber,x=cell.X*8,y=cell.Y*8;
                var coffin=_editor._coffins.LastOrDefault(c=>c.Position.Room==room&&new Rectangle(c.Position.X,c.Position.Y-8,16,24).Contains(x,y));
                var item=_editor._items.LastOrDefault(i=>i.Position.Room==room&&new Rectangle(i.Position.X,i.Position.Y,16,16).Contains(x,y));
                var mechanism=_editor._mechs.LastOrDefault(m=>m.Position.Room==room&&new Rectangle(m.Position.X,m.Position.Y,Math.Max(1,m.WidthCells)*8,Math.Max(1,m.HeightCells)*8).Contains(x,y));
                if(coffin!=null){_category=2;_editor._tool=4;_editor._sub[4]=(int)coffin.Kind-1;_enemyLevel=coffin.Level;}
                else if(item!=null){_category=3;_editor._tool=6;_editor._sub[6]=(int)item.Kind-1;}
                else if(mechanism!=null)
                {
                    _category=4;_editor._tool=5;_mechanismHeight=Math.Max(1,mechanism.HeightCells);
                    _editor._sub[5]=mechanism.Kind==MechanismKind.RotatingDoor?(mechanism.InitialPhase==3?1:0):mechanism.Kind==MechanismKind.Unknown2?(mechanism.Direction==1?3:2):mechanism.Kind==MechanismKind.FallingWall?4:mechanism.Kind==MechanismKind.Unknown4?5:6;
                }
                else if(_editor._stones.Any(s=>s.Room==room&&new Rectangle(s.X,s.Y,16,16).Contains(x,y))){_category=1;_editor._tool=7;}
                else if(_editor._startRoom==room&&new Rectangle(_editor._startX,_editor._startY-8,16,32).Contains(x,y)){_category=1;_editor._tool=3;}
                else if(_editor._exitRoom==room&&new Rectangle(_editor._exitX,_editor._exitY,32,32).Contains(x,y)){_category=1;_editor._tool=8;}
                else {_category=0;_editor._tool=_editor.Cells[cell.Y*32+cell.X] switch{TerrainKind.Soft=>0,TerrainKind.Ladder=>2,_=>1};}
                _editor._isEraser=false;_rebuild=true;
            }
            private void Paint(Point cell, bool erase)
            {
                _editor._cx=cell.X;_editor._cy=cell.Y;
                if (erase)
                {
                    for(int y=cell.Y;y<Math.Min(24,cell.Y+_brush);y++) for(int x=cell.X;x<Math.Min(32,cell.X+_brush);x++) _editor.EraseAt(x,y);
                }
                else if (_editor._tool is 0 or 1 or 2)
                {
                    TerrainKind kind = _editor._tool == 0 ? TerrainKind.Soft : _editor._tool == 1 ? TerrainKind.Wall : TerrainKind.Ladder;
                    int width = _editor._tool == 2 ? 2 : _brush;
                    for(int y=cell.Y;y<Math.Min(24,cell.Y+_brush);y++) for(int x=cell.X;x<Math.Min(32,cell.X+width);x++) _editor.Cells[y*32+x]=kind;
                    _editor.UpdateRoomTexture(_editor.RoomNumber);
                }
                else
                {
                    int x = Math.Min(cell.X,30)*8, y = Math.Min(cell.Y,22)*8, room = _editor.RoomNumber;
                    switch (_editor._tool)
                    {
                        case 3: _editor._startRoom=room;_editor._startX=x;_editor._startY=Math.Clamp(y,8,168);break;
                        case 8: _editor._exitRoom=room;_editor._exitX=Math.Min(x,224);_editor._exitY=Math.Min(y,160);break;
                        case 7:
                            if (_editor._stones.Count >= 16) { _status="A level can contain at most 16 soul stones.";break; }
                            _editor._stones.RemoveAll(p=>p.Room==room&&p.X==x&&p.Y==y);_editor._stones.Add(new(room,x,y));break;
                        case 6:
                            _editor._items.RemoveAll(p=>p.Position.Room==room&&p.Position.X==x&&p.Position.Y==y);
                            _editor._items.Add(new((ItemKind)(_editor._sub[6]+1),new(room,x,y)));break;
                        case 4:
                            y=Math.Clamp(y,8,168);
                            _editor._coffins.RemoveAll(p=>p.Position.Room==room&&p.Position.X==x&&p.Position.Y==y);
                            _editor._coffins.Add(new(new(room,x,y),new[]{EnemyKind.Mummy,EnemyKind.GoldMummy,EnemyKind.Guard,EnemyKind.Rock}[_editor._sub[4]],_enemyLevel));break;
                        case 5: PlaceMechanism(cell);break;
                    }
                    _editor.UpdateRoomTexture(room);
                }
                _status = $"Room {_editor.RoomNumber:00}  /  Cell {cell.X},{cell.Y}  /  Drag to paint, right-click to erase";
            }
            private void Fill(Point cell, bool erase)
            {
                if (!erase && _editor._tool > 2) { _status="Fill is available for terrain. Choose a terrain tile.";_rebuild=true;return; }
                TerrainKind from=_editor.Cells[cell.Y*32+cell.X], to=erase?TerrainKind.Empty:_editor._tool==0?TerrainKind.Soft:_editor._tool==1?TerrainKind.Wall:TerrainKind.Ladder;
                if(from==to)return;
                var pending=new Queue<Point>();pending.Enqueue(cell);
                while(pending.TryDequeue(out var current))
                {
                    if(current.X<0||current.X>=32||current.Y<0||current.Y>=24||_editor.Cells[current.Y*32+current.X]!=from)continue;
                    _editor.Cells[current.Y*32+current.X]=to;
                    pending.Enqueue(new(current.X-1,current.Y));pending.Enqueue(new(current.X+1,current.Y));pending.Enqueue(new(current.X,current.Y-1));pending.Enqueue(new(current.X,current.Y+1));
                }
                _editor.UpdateRoomTexture(_editor.RoomNumber);_status="Filled connected terrain.";
            }
            private void PlaceMechanism(Point cell)
            {
                int sub=_editor._sub[5],room=_editor.RoomNumber;
                if(sub==7)
                {
                    int col=Math.Min(cell.X,30),row=Math.Min(cell.Y,24-_mechanismHeight);
                    for(int r=0;r<_mechanismHeight;r++)for(int c=0;c<2;c++)_editor.Cells[(row+r)*32+col+c]=TerrainKind.Ladder;
                    var json=JsonNode.Parse(_editor.BuildJson())!;
                    var existing=json["goldenLadders"]!.AsArray();
                    for(int i=existing.Count-1;i>=0;i--)if(existing[i]!["room"]!.GetValue<int>()==room&&existing[i]!["col"]!.GetValue<int>()==col&&existing[i]!["row"]!.GetValue<int>()==row)existing.RemoveAt(i);
                    json["goldenLadders"]!.AsArray().Add(new JsonObject{["room"]=room,["col"]=col,["row"]=row,["length"]=_mechanismHeight});
                    _editor.ApplyLoadedStage(StageLoader.Parse(json.ToJsonString()),_editor._currentStageNumber,room);return;
                }
                var kind=sub<2?MechanismKind.RotatingDoor:sub<4?MechanismKind.Unknown2:sub==4?MechanismKind.FallingWall:sub==5?MechanismKind.Unknown4:MechanismKind.MovableBlock;
                int width=kind==MechanismKind.Unknown4?4:kind==MechanismKind.FallingWall?1:2;
                int height=kind==MechanismKind.FallingWall?_mechanismHeight:kind is MechanismKind.Unknown2 or MechanismKind.MovableBlock?2:1;
                int x=Math.Min(cell.X,32-width)*8,y=Math.Min(cell.Y,24-height)*8;
                _editor._mechs.RemoveAll(m=>m.Position.Room==room&&m.Position.X==x&&m.Position.Y==y);
                _editor._mechs.Add(new(kind,new(room,x,y),width,height,sub==1?3:0,sub==3?1:0));
            }
            private void CopySelection()
            {
                FinishStroke();if(_selection is not {} selection)return;
                _clipboard=new(_editor.BuildJson(),_editor.RoomNumber,selection);_status="Selection copied. Point to a destination and press Ctrl+V.";_rebuild=true;
            }
            private void Paste()
            {
                if(_clipboard is not {} clip)return;
                var source=StageLoader.Parse(clip.Json);var dest=JsonNode.Parse(_editor.BuildJson())!;
                int dx=_pasteAt.X-clip.Cells.X,dy=_pasteAt.Y-clip.Cells.Y;
                var rows=dest["rooms"]![_editor.RoomNumber-1]!["rows"]!.AsArray();
                for(int y=0;y<clip.Cells.Height&&_pasteAt.Y+y<24;y++)
                {
                    char[] line=rows[_pasteAt.Y+y]!.GetValue<string>().ToCharArray();
                    for(int x=0;x<clip.Cells.Width&&_pasteAt.X+x<32;x++)line[_pasteAt.X+x]=RowChar(source.Rooms[clip.Room-1].Cells[(clip.Cells.Y+y)*32+clip.Cells.X+x]);
                    rows[_pasteAt.Y+y]=new string(line);
                }
                var src=JsonNode.Parse(clip.Json)!;
                foreach(string key in new[]{"items","coffins","mechanisms","soulStones"})
                foreach(var node in src[key]!.AsArray())
                {
                    int x=node!["x"]!.GetValue<int>(),y=node["y"]!.GetValue<int>();
                    if(node["room"]!.GetValue<int>()!=clip.Room||!clip.Cells.Contains(x/8,y/8))continue;
                    int px=x+dx*8,py=y+dy*8;if(px<0||px>240||py<0||py>176)continue;
                    var target=dest[key]!.AsArray();
                    for(int i=target.Count-1;i>=0;i--)if(target[i]!["room"]!.GetValue<int>()==_editor.RoomNumber&&target[i]!["x"]!.GetValue<int>()==px&&target[i]!["y"]!.GetValue<int>()==py)target.RemoveAt(i);
                    if(key=="soulStones"&&target.Count>=16)continue;
                    var copy=node.DeepClone();copy["room"]=_editor.RoomNumber;copy["x"]=px;copy["y"]=py;target.Add(copy);
                }
                foreach(var golden in source.GoldenLadders.Where(g=>g.Room==clip.Room&&clip.Cells.Contains(g.Column,g.Row)))
                {
                    int col=golden.Column+dx,row=golden.Row+dy;
                    if(col<0||col>30||row<0||row+golden.Length>24)continue;
                    dest["goldenLadders"]!.AsArray().Add(new JsonObject{["room"]=_editor.RoomNumber,["col"]=col,["row"]=row,["length"]=golden.Length});
                }
                foreach(string key in new[]{"playerStart","exitDoor"})
                {
                    var node=src[key]!;int x=node["x"]!.GetValue<int>(),y=node["y"]!.GetValue<int>();
                    if(node["room"]!.GetValue<int>()!=clip.Room||!clip.Cells.Contains(x/8,y/8))continue;
                    int px=x+dx*8,py=y+dy*8;if(px<0||px>224||py<0||py>160)continue;
                    var copy=node.DeepClone();copy["room"]=_editor.RoomNumber;copy["x"]=px;copy["y"]=py;dest[key]=copy;
                }
                RestoreJson(dest.ToJsonString());_status="Selection pasted.";
            }
        }
    }
}
