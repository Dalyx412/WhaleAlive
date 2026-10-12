using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WhaleAlive;

// No HWND, screen capture, shell enumeration or real companion file is used here.
internal static class CharacterScaleVerification
{
    static int assertions,frames;
    static void Check(bool ok,string label){assertions++;if(!ok)throw new Exception("Character scale: "+label);}
    static void Near(Point a,Point b,string label)=>Check((a-b).Length<.000001,label+": "+a+" / "+b);
    static Point Origin(PetWindow pet)=>new(Canvas.GetLeft(pet.SpriteView),Canvas.GetTop(pet.SpriteView));
    static Point Attachment(PetWindow pet)=>new(Canvas.GetLeft(pet.AttachmentView)+pet.AttachmentView.Width/2,Canvas.GetTop(pet.AttachmentView)+pet.AttachmentView.Height/2);
    static readonly Point Anchor=new(750,500);
    static readonly Size Scene=new(1500,900);
    static void Layout(PetWindow pet)=>pet.ArrangeOffscreen(Anchor,Scene);
    static Point LegacyPivot(AnimationClip c,string key,int frame,bool right)=>c.Pivot(key=="sit"?Math.Clamp(frame,c.LoopStart,c.LoopEnd):key=="lie_sleep"?c.LoopStart:frame,right);
    static Vector Correction(CompanionData d,string key,bool right,bool window=false)=>(key is "sit" or "sit_idle" ? window?d.WindowSeatCorrection:d.SeatCorrection : key=="lie_sleep" ? window?d.WindowSleepCorrection:d.SleepCorrection : new PoseCorrection(0,0)).ForDirection(right);
    static void Save(PetWindow pet,string name,string? surface=null)
    {
        Layout(pet);var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(235,241,248)),null,new Rect(Scene));
            if(surface=="icon")dc.DrawRectangle(Brushes.CornflowerBlue,new Pen(Brushes.SteelBlue,1),new Rect(Anchor.X-24,Anchor.Y,48,48));
            if(surface=="window")dc.DrawLine(new Pen(Brushes.SteelBlue,2),new(350,Anchor.Y),new(1150,Anchor.Y));
            dc.DrawRectangle(new VisualBrush((Visual)pet.Content){ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(Scene),Stretch=Stretch.Fill},null,new Rect(Scene));
            dc.DrawEllipse(null,new Pen(Brushes.OrangeRed,1),Anchor,3,3);
        }
        var bitmap=new RenderTargetBitmap((int)Scene.Width,(int)Scene.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(AppContext.BaseDirectory,"char-scale-"+name+".png"));encoder.Save(stream);
    }
    static DrawingImage Icon()
    {
        var image=new DrawingImage(new GeometryDrawing(Brushes.Goldenrod,new Pen(Brushes.DarkGoldenrod,1),new RectangleGeometry(new Rect(.5,.5,47,47))));image.Freeze();return image;
    }
    public static async Task Run()
    {
        assertions=frames=0;
        string directory=Path.Combine(Path.GetTempPath(),"WhaleAlive-character-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,"companion.json");File.WriteAllText(file,"{}");var store=new CompanionStore(file);
        var pet=new PetWindow(offscreen:true);var engine=new Interaction(pet,Path.Combine(directory,"history.json"));
        using var features=new CompanionFeatures(pet,engine,store);
        try
        {
            Check(store.Data.CharacterScale==1,"old JSON default");
            foreach(double value in new[]{1d,2d,0d,-1d,1.5d,3d})
            {
                File.WriteAllText(file,JsonSerializer.Serialize(new{CharacterScale=value}));Check(new CompanionStore(file).Data.CharacterScale==(value==2?2:1),"disk coercion");
                features.SetCharacterScale(value);Check(new CompanionStore(file).Data.CharacterScale==pet.CharacterScale&&pet.CharacterScale==(value==2?2:1),"live roundtrip");
            }
            features.SetCharacterScale(1);Layout(pet);pet.Say("");
            foreach(var (key,c) in pet.Clips)
            foreach(bool right in new[]{false,true})
            foreach(int f in Enumerable.Range(0,c.Count))
            {
                pet.CalibrationFrame=f;pet.Face(right);pet.SetState(key,true);
                var pivot=LegacyPivot(c,key,f,right);var correction=Correction(store.Data,key,right);double size=128*c.DisplayScale;
                var expected=new Point(Anchor.X-pivot.X*size+correction.X,Anchor.Y-pivot.Y*size+correction.Y);
                pet.SetCharacterScale(1);Layout(pet);Near(Origin(pet),expected,$"legacy {key}/{f}/{right}");
                Check(pet.SpriteDisplaySize==size&&pet.ReferenceCharacterHeight==size*c.ReferenceHeight,"legacy status dimensions");
                // Corrections identify the calibrated support in the art: effective local
                // pivot = nominal pivot - canonical correction. It must remain on anchor.
                var localContact=new Vector(pivot.X*size-correction.X,pivot.Y*size-correction.Y);
                Near(Origin(pet)+localContact,Anchor,"1x calibrated support");
                var hand=PetWindow.HandPoint(key,f,c.Count);if(right&&c.Directional)hand.X=1-hand.X;
                Near(Attachment(pet),expected+new Vector(hand.X*size,hand.Y*size),"legacy attachment hand");
                pet.SetCharacterScale(2);Layout(pet);
                Near(Origin(pet)+localContact*2,Anchor,"2x calibrated support");
                Near(Origin(pet),Anchor+(expected-Anchor)*2,"origin scales about support");
                Near(Attachment(pet),Origin(pet)+new Vector(hand.X*size*2,hand.Y*size*2),"2x attachment follows hand");
                Check(pet.SpriteDisplaySize==size*2&&pet.ReferenceCharacterHeight==size*2*c.ReferenceHeight,"2x status dimensions");frames++;
            }
            // Independent window corrections and carry corrections, including right overrides.
            store.Data.PickupCorrection=new(7,-3);store.Data.PutdownCorrection=new(-4,8);
            store.Data.PickupRightCorrection=new(9,2);store.Data.PutdownRightCorrection=new(-6,-5);
            foreach(bool right in new[]{false,true})
            foreach(int scale in new[]{1,2})
            {
                pet.SetCharacterScale(scale);pet.Face(right);
                foreach(string key in new[]{"sit","sit_idle","lie_sleep"})
                {
                    pet.SurfaceOffset=(k,r)=>Correction(store.Data,k,r,true);var c=pet.Clips[key];double size=128*c.DisplayScale;
                    foreach(int f in new[]{0,c.LoopStart,c.LoopEnd,c.Count-1})
                    {
                        pet.CalibrationFrame=f;pet.SetState(key,true);Layout(pet);var pivot=LegacyPivot(c,key,f,right);var correction=Correction(store.Data,key,right,true);
                        Near(Origin(pet)+new Vector(pivot.X*size-correction.X,pivot.Y*size-correction.Y)*scale,Anchor,"window contact");
                        var legacyDelta=(c.Pivot(f==c.Count-1?c.Count-1:0,right)-LegacyPivot(c,key,f==c.Count-1?c.Count-1:0,right))*size+correction;
                        // Offscreen has 96 DPI; use the same desktop conversion as production.
                        Near(Anchor+(pet.ToPixels(pet.SurfaceStandingPosition(Anchor,key,right,f==c.Count-1))-pet.ToPixels(Anchor)),Anchor+legacyDelta*scale,"surface entry/exit delta");
                    }
                }
                pet.SurfaceOffset=null;
                foreach(string key in new[]{"pickup","putdown"})
                {
                    var saved=key=="pickup"?(right?store.Data.PickupRightCorrection!:store.Data.PickupCorrection):(right?store.Data.PutdownRightCorrection!:store.Data.PutdownCorrection);
                    var offset=new Vector(right?-24:24,45)+saved.ForDirection(right);
                    Near(pet.CarryPosition(Anchor,key,right),Anchor+offset*scale,"canonical carry standing offset");
                    var clip=pet.Clips[key];double size=128*clip.DisplayScale;
                    foreach(int frame in new[]{0,key=="pickup"?25:33,clip.Count-1})
                    {
                        pet.CalibrationFrame=frame;pet.SetState(key,true);
                        var standing=pet.CarryPosition(Anchor,key,right);pet.ArrangeOffscreen(standing,Scene);
                        var pivot=clip.Pivot(frame,right);
                        // The saved carry offset calibrates the source-art contact to the
                        // icon target. Unlike the icon, that local contact scales with art.
                        var calibratedHand=new Vector(pivot.X*size,pivot.Y*size)-offset;
                        Near(Origin(pet)+calibratedHand*scale,Anchor,"pickup/putdown calibrated target contact");
                    }
                    // Input is a display-DIP delta; a 2x drag of (10,-6) stores (5,-3).
                    var canonical=PoseCorrection.FromDisplay(saved.ForDirection(right)+new Vector(10,-6)/scale,right);
                    Near(new Point()+canonical.ForDirection(right)*scale,new Point()+saved.ForDirection(right)*scale+new Vector(10,-6),"canonical nudge roundtrip");
                }
                Check(pet.MovementSpeed(false,false)==110*scale&&pet.MovementSpeed(false,true)==90*scale&&pet.MovementSpeed(true,false)==210*scale,"gait speeds");
            }
            foreach(var point in new[]{new Point(150,250),new Point(350,500),new Point(1800,1000)})
            {
                var work=new Rect(0,0,1920,1080);
                bool legacy=point.Y>210&&point.Y<1015&&point.X>100&&point.X<1810;
                Check(DesktopSeats.Fits(work,point,1)==legacy,"legacy seat space requirements");
            }
            Check(!DesktopSeats.Fits(new Rect(0,0,1920,1080),new Point(150,250),2),"2x rejects cramped seat");
            Check(DesktopSeats.Fits(new Rect(0,0,1920,1080),new Point(350,500),2),"2x accepts roomy seat");
            features.SetCharacterScale(2);
            using(var panel=new CompanionPanel(features,engine,()=>[]))
            {
                panel.ShowAppearance();panel.Measure(new Size(760,700));panel.Arrange(new Rect(0,0,760,700));panel.UpdateLayout();
                var toggle=Find<CheckBox>(panel).Single(c=>Equals(c.Content,"角色放大到 200%"));
                foreach(bool enabled in new[]{false,true,false}){toggle.IsChecked=enabled;Check(store.Data.CharacterScale==(enabled?2:1)&&pet.CharacterScale==store.Data.CharacterScale&&new CompanionStore(file).Data.CharacterScale==store.Data.CharacterScale,"actual switch live persistence");}
            }
            await VerifyCalibration(directory);
            pet.Attach(Icon());double attachmentSize=pet.AttachmentView.Width;
            pet.SetState("idle",true);Layout(pet);// settle the first offscreen frame before capturing
            foreach(int scale in new[]{1,2})
            {
                features.SetCharacterScale(scale);pet.Face(false);pet.Say("");pet.ShowTodos([], _=>{});
                foreach(var (name,key,surface) in new[]{("idle","idle",""),("icon-seat","sit_idle","icon"),("window-seat","sit_idle","window"),("window-sleep","lie_sleep","window"),("carry","carry_idle","")})
                {
                    pet.SurfaceOffset=surface=="window"?(k,r)=>Correction(store.Data,k,r,true):null;
                    pet.Attach(name=="carry"?Icon():null);pet.CalibrationFrame=pet.Clips[key].LoopStart;pet.SetState(key,true);Save(pet,$"{scale}x-{name}",surface);
                    if(name=="idle"){var c=pet.Clips[key];var p=c.Pivot(c.LoopStart,pet.FacingRight);Near(Origin(pet)+new Vector(p.X*pet.SpriteDisplaySize,p.Y*pet.SpriteDisplaySize),Anchor,$"{scale}x idle feet on anchor");}
                    Check(pet.AttachmentView.Width==attachmentSize,"attachment source size changed");
                }
            }
            pet.SurfaceOffset=null;pet.Attach(null);pet.CalibrationFrame=0;pet.SetState("idle",true);
            foreach(int character in new[]{1,2})foreach(int ui in new[]{1,2})
            {
                features.SetCharacterScale(character);features.SetUiScale(ui);pet.ShowTodos(Enumerable.Range(1,4).Select(i=>new PetNote(i.ToString(),"待办事项 "+i,DateTime.UtcNow)),_=>{});pet.Say("你好呀，今天也陪着你。\n角色与界面独立缩放");Layout(pet);
                var speech=new Rect(Canvas.GetLeft(pet.SpeechView),Canvas.GetTop(pet.SpeechView),pet.SpeechView.DesiredSize.Width,pet.SpeechView.DesiredSize.Height);
                Check(!speech.IntersectsWith(pet.CharacterBox)&&new Rect(Scene).Contains(speech),"speech collision/bounds");
                var todos=PetWindow.TodoArcLayout(Anchor,new Rect(Scene),4,ui,pet.CharacterBox,character);
                Check(todos.All(r=>new Rect(Scene).Contains(r)&&(character==1&&ui==1||!r.IntersectsWith(pet.CharacterBox))),"enlarged todo collision/bounds (legacy 1x keeps its original arc)");
                Check(todos.All(r=>r.Width==136*ui&&r.Height==46*ui),"todo sizes independent");
                Check(todos.All(r=>!r.IntersectsWith(speech)),$"speech/todo collision ui {ui}x character {character}x");
                Save(pet,$"ui-{ui}x-character-{character}x");
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"char-scale-verification.json"),JsonSerializer.Serialize(new{pass=true,clips=pet.Clips.Count,frames,assertions,offscreen=true,pngs=14},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{engine.Stop();pet.Close();Directory.Delete(directory,true);}
        await Task.CompletedTask;
    }
    sealed class FakeSeats : IDesktopSeatSource
    {
        public IReadOnlyList<DesktopSeat> Available(PetWindow pet)=>[new("fixture","离屏图标",Anchor,0,0)];
        public bool StillThere(DesktopSeat seat)=>true;
    }
    static async Task VerifyCalibration(string directory)
    {
        var pet=new PetWindow(offscreen:true);var seats=new FakeSeats();
        var engine=new Interaction(pet,Path.Combine(directory,"calibration-history.json"),seats);
        var store=new CompanionStore(Path.Combine(directory,"calibration.json"));
        using var calibration=new PoseCalibration(pet,engine,store,seats);
        try
        {
            pet.SetCharacterScale(2);
            foreach(bool putdown in new[]{false,true})
            {
                pet.Face(false);await calibration.StartCarry(putdown);calibration.Nudge(new(10,-6));
                Check(calibration.Offset==new Vector(5,-3),"actual 2x calibration nudge");
                calibration.SaveAndSwitchDirection();calibration.Nudge(new(-4,8));await calibration.Save();
                var saved=new CompanionStore(Path.Combine(directory,"calibration.json")).Data;
                Check((putdown?saved.PutdownCorrection:saved.PickupCorrection)==new PoseCorrection(5,-3),"left canonical persisted");
                Check((putdown?saved.PutdownRightCorrection:saved.PickupRightCorrection)==new PoseCorrection(7,1),"independent right canonical persisted");
                pet.SetCharacterScale(1);Near(pet.CarryPosition(Anchor,putdown?"putdown":"pickup",false),Anchor+new Vector(29,42),"1x after saving at 2x");pet.SetCharacterScale(2);
            }
            foreach(bool sleep in new[]{false,true})
            {
                pet.Face(false);await calibration.Start(sleep);var before=calibration.Offset;calibration.Nudge(new(10,-6));
                Check(calibration.Offset==before+new Vector(5,-3),"actual seat/sleep canonical nudge");await calibration.Save();
                pet.SetCharacterScale(1);Check(calibration.RenderOffset(sleep?"lie_sleep":"sit_idle",false)==before+new Vector(5,-3),"seat/sleep 1x after 2x save");pet.SetCharacterScale(2);
            }
        }
        finally{await calibration.Cancel();engine.Stop();pet.Close();}
    }
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;foreach(var item in Find<T>(child))yield return item;}
    }
}

