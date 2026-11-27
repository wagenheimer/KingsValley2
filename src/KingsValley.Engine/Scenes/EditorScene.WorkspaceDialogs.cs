#nullable enable
using System;
using System.IO;
using System.Linq;
using KingsValley.Core.Stages;
using KingsValley.Engine.Assets;
using Microsoft.Xna.Framework;

namespace KingsValley.Engine.Scenes;

public partial class EditorScene
{
    private sealed partial class EditorRenderer
    {
        private sealed partial class Workspace
        {
            private void DrawModal()
            {
                _buttons.Clear();
                Box(new(0,0,_size.X,_size.Y),new Color(0,0,0,210));
                int width=Math.Min(_size.X-48,_modal=="properties"?460:840);
                int height=Math.Min(_size.Y-60,_modal=="properties"?660:_modal is "templates" or "slots"?490:280);
                var panel=new Rectangle((_size.X-width)/2,(_size.Y-height)/2,width,height);
                Box(panel,Border);Box(new(panel.X+1,panel.Y+1,panel.Width-2,panel.Height-2),Panel);
                int x=panel.X+24,y=panel.Y+20;
                switch(_modal)
                {
                    case "confirm":
                        Label("Save your changes?",new(x,y,width-48,35),25,Gold);
                        Label("This level has unsaved changes. Save it before continuing, or discard this copy.",new(x,y+58,width-48,74),19);
                        Button("Save and Continue",new(x,panel.Bottom-67,185,40),()=>
                        {
                            if(!SaveExternal(false))return;
                            var action=_pending;_pending=null;_modal=null;action?.Invoke();_rebuild=true;
                        });
                        Button("Discard",new(x+199,panel.Bottom-67,100,40),()=>
                        {
                            var action=_pending;_pending=null;_modal=null;DeleteRecovery();action?.Invoke();_rebuild=true;
                        });break;
                    case "replace-slot":
                        Label("Replace saved slot?",new(x,y,width-48,35),25,Gold);
                        Label(_message,new(x,y+58,width-48,75),19);
                        Button("Replace",new(x,panel.Bottom-67,145,40),()=>{var action=_pending;_pending=null;_modal=null;action?.Invoke();_rebuild=true;});break;
                    case "error":
                        Label("Unable to complete the action",new(x,y,width-48,40),24,Gold);
                        Label(_message,new(x,y+59,width-48,112),18);break;
                    case "recovery":
                        Label("Recover your last editing session?",new(x,y,width-48,40),24,Gold);
                        Label("A recovery copy is available. Restore it to continue editing, or start with the current game stage.",new(x,y+58,width-48,84),18);
                        Button("Restore recovery",new(x,panel.Bottom-67,180,40),()=>LoadDocument(LevelDocument.Load(RecoveryPath,RomContent.Variant),null,false));
                        Button("Start fresh",new(x+194,panel.Bottom-67,120,40),()=>{DeleteRecovery();_modal=null;_rebuild=true;});break;
                    case "templates": DrawTemplates(panel);break;
                    case "slots": DrawSlots(panel);break;
                    case "properties": DrawProperties(new(panel.X,panel.Y+2,panel.Width,panel.Height-38));break;
                }
                if(_modal!="recovery")
                    Button(_modal=="confirm"?"Cancel":"Close",new(panel.Right-106,panel.Bottom-58,82,34),()=>{_modal=null;_pending=null;_rebuild=true;});
            }

            private void DrawTemplates(Rectangle panel)
            {
                int x=panel.X+24,y=panel.Y+20,width=panel.Width-48;
                Label("From Game",new(x,y,width,34),25,Gold);
                Label("Open a stage as an editable copy. Game stages remain unchanged.",new(x,y+43,width,30),17,Muted);
                var sources=EditorTemplateCatalog.Available();
                if(sources.Count==0){Label("Extract a supported cartridge first.",new(x,y+100,width,60),20);return;}
                _templateSource=Math.Clamp(_templateSource,0,sources.Count-1);
                for(int i=0;i<sources.Count;i++)
                {
                    int index=i,tabWidth=(width-12)/sources.Count;
                    Button(sources[i].Label,new(x+i*(tabWidth+4),y+89,tabWidth-3,36),()=>{_templateSource=index;_rebuild=true;},i==_templateSource);
                }
                var source=sources[_templateSource];int cellWidth=width/10;
                for(int i=0;i<source.StageCount;i++)
                {
                    int number=i+1;
                    Button(number.ToString("00"),new(x+i%10*cellWidth,y+151+i/10*40,cellWidth-6,34),()=>LoadTemplate(source,number));
                }
                Label($"{source.StageCount} stages available  /  Choose a stage to begin editing",new(x,panel.Bottom-75,width-100,38),16,Muted);
            }
            private void DrawSlots(Rectangle panel)
            {
                int x=panel.X+24,y=panel.Y+20,width=panel.Width-48;
                Label("Local Slots",new(x,y,width,34),25,Gold);
                Button("Load a slot",new(x,y+50,145,34),()=>{_slotSave=false;_rebuild=true;},!_slotSave);
                Button("Save a copy to slot",new(x+157,y+50,190,34),()=>{_slotSave=true;_rebuild=true;},_slotSave);
                for(int i=0;i<10;i++)
                {
                    int slot=i+1;string path=Path.Combine(RomContent.CustomLevelsDirectory,$"custom_stage_{slot:00}.json");bool exists=File.Exists(path);
                    string title=$"{slot:00}  /  {(exists?"Saved level":"Empty")}";
                    Button(title,new(x+i%2*(width/2),y+112+i/2*47,width/2-10,38),()=>
                    {
                        if(_slotSave)
                        {
                            if(exists){_message=$"Replace the level in local slot {slot:00}?";_pending=()=>SaveSlot(slot);_modal="replace-slot";_rebuild=true;}
                            else SaveSlot(slot);
                        }
                        else Guard(()=>LoadDocument(LevelDocument.Load(path,RomContent.Variant) with{Name=$"Slot {slot:00}"},null,true));
                    },enabled:_slotSave||exists);
                }
                Label("External files: use Open or Save As in the main toolbar.",new(x,panel.Bottom-75,width-100,35),16,Muted);
            }
            private void SaveSlot(int slot)
            {
                _editor.SaveToSlot(slot);
                if(_state.Path==null){_state.SavedJson=_editor.BuildJson();DeleteRecovery();}
                _status=$"Saved a copy to local slot {slot:00}.";_modal=null;_rebuild=true;
            }
        }
    }
}
