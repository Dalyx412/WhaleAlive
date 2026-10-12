using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
namespace WhaleAlive;

internal static class UiScaleVerification
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern nint WindowFromPoint(ShellDesktop.POINT point);
    static void Check(bool value,string message){if(!value)throw new Exception("UI scale: "+message);}
    static Rect[] LegacyArc(Point anchor,Rect work,int count)
    {
        double width=136+(count>1?44:0),height=count*46+(count-1)*10;
        double x=anchor.X-45-width,y=anchor.Y-65-height/2;
        bool right=x<work.Left+8&&anchor.X+45+width<=work.Right-8;if(right)x=anchor.X+45;
        x=Math.Clamp(x,work.Left+8,Math.Max(work.Left+8,work.Right-width-8));y=Math.Clamp(y,work.Top+8,Math.Max(work.Top+8,work.Bottom-height-8));
        return Enumerable.Range(0,count).Select(i=>{double t=count==1?0:2.0*i/(count-1)-1,bend=count==1?0:44*t*t;return new Rect(x+(right?width-136-bend:bend),y+i*56,136,46);}).ToArray();
    }
    static Size Save(FrameworkElement view,string name,double scale=1)
    {
        var size=new Size(Math.Ceiling(view.ActualWidth*scale),Math.Ceiling(view.ActualHeight*scale));
        Check(size.Width>0&&size.Height>0,"Empty render: "+name);
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawRectangle(new VisualBrush(view){Stretch=Stretch.Fill},null,new Rect(size));
        var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,name+".png")))png.Save(output);return size;
    }
    static void Double(Size a,Size b,string name)=>Check(Math.Abs(b.Width-a.Width*2)<=2&&Math.Abs(b.Height-a.Height*2)<=2,$"{name} did not double: {a} -> {b}");
    public static async Task Run()
    {
        var evidence=new List<object>();
        foreach(var work in new[]{new Rect(0,0,2560,1392),new Rect(-1080,-480,1080,1872),new Rect(2560,-200,1080,1872)})
        foreach(var anchor in new[]{work.TopLeft+new Vector(65,140),work.BottomRight-new Vector(65,10),new Point(work.Left+work.Width/2,work.Top+work.Height/2)})
        for(int count=1;count<=4;count++)
        {
            Check(PetWindow.TodoArcLayout(anchor,work,count,1).SequenceEqual(LegacyArc(anchor,work,count)),"1x arc changed");
            var box=new Rect(anchor.X-64,anchor.Y-128,128,128);var arc=PetWindow.TodoArcLayout(anchor,work,count,2);
            Check(arc.All(r=>r.Width==272&&r.Height==92&&work.Contains(r)&&!r.IntersectsWith(box)),"2x arc bounds/character collision");
            for(int i=0;i<count;i++)for(int j=i+1;j<count;j++)Check(!arc[i].IntersectsWith(arc[j]),"2x todos overlap");
            var speech=UiSizing.Beside(new Rect(anchor.X-210,anchor.Y-183,420,240),box,work,24);
            Check(work.Contains(speech)&&!speech.IntersectsWith(box),"Speech/progress covers character or escapes monitor");
            var settings=UiSizing.SettingsBounds(anchor,work,2);Check(work.Contains(settings)&&settings.Width<=work.Width-40&&settings.Height<=work.Height-40,"Settings clamp");
        }
        evidence.Add(new{test="legacy-arc-equality-double-size-no-overlap-and-three-monitor-clamps",pass=true});
        string directory=Path.Combine(Path.GetTempPath(),"WhaleAlive-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        string file=Path.Combine(directory,"companion.json");File.WriteAllText(file,"{}");var store=new CompanionStore(file);double original=store.Data.UiScale;
        var pet=new PetWindow();pet.Show();var engine=new Interaction(pet,Path.Combine(directory,"history.json"));
        using var features=new CompanionFeatures(pet,engine,store);using var menu=new PetQuickMenu(pet,engine,features,()=>{},()=>{});
        try
        {
            Check(original==1,"Legacy default");
            foreach(double value in new[]{1d,2d,0d,-1d,1.5d,3d})
            {
                File.WriteAllText(file,JsonSerializer.Serialize(new{UiScale=value}));Check(new CompanionStore(file).Data.UiScale==(value==2?2:1),"Invalid disk value not coerced");
                store.Data.UiScale=value;store.Save();Check(new CompanionStore(file).Data.UiScale==(value==2?2:1),"Round trip failed");
            }
            evidence.Add(new{test="legacy-default-valid-roundtrip-invalid-disk-coercion",pass=true});
            for(int i=0;i<4;i++)store.Data.Todos.Add(new("ui-"+i,"界面缩放测试待办 "+(i+1),DateTime.UtcNow));
            pet.SetAnchor(new(1100,700));pet.SetState("idle");pet.CalibrationFrame=0;pet.UpdateLayout();double spriteSize=pet.SpriteDisplaySize;var origin=pet.RenderedSpriteOrigin;
            var sizes=new Dictionary<string,Size>();
            using var panel=new CompanionPanel(features,engine,()=>[]);panel.ShowAppearance();
            var host=new Border{Child=panel,Width=760,Height=700,Background=GlassAppearance.Surface};var outer=new Grid();outer.Children.Add(host);
            foreach(int scale in new[]{1,2})
            {
                features.SetUiScale(scale);Check(pet.UiScale==scale,"Scale not applied immediately");pet.UpdateLayout();
                Check(pet.SpriteDisplaySize==spriteSize&&pet.RenderedSpriteOrigin==origin,"Character geometry changed");
                pet.ShowTaskProgress("");pet.Say("你好呀，今天也陪着你。\n界面大小测试");pet.UpdateLayout();
                var speech=Save(pet.SpeechView,$"ui-scale-{scale}x-speech",scale);
                var desired=pet.SpeechView.DesiredSize;Check(Math.Abs(desired.Width-210*scale)<1,"Speech layout did not grow");
                pet.ShowTaskProgress("Claude · 正在执行\n正在核对任务进度");pet.UpdateLayout();var progress=Save(pet.SpeechView,$"ui-scale-{scale}x-progress",scale);
                var button=(Button)((Canvas)pet.TodoView).Children[0];var point=button.TranslatePoint(new Point(button.ActualWidth/2,button.ActualHeight/2),pet);
                var hit=pet.InputHitTest(point) as DependencyObject;while(hit!=null&&hit!=button)hit=VisualTreeHelper.GetParent(hit);Check(hit==button,"Scaled todo hit test failed");
                Check(Math.Abs(button.DesiredSize.Width-136*scale)<1&&Math.Abs(button.DesiredSize.Height-46*scale)<1,"Todo layout did not double");
                var todo=Save(pet.TodoView,$"ui-scale-{scale}x-todos");
                menu.Open();await Task.Delay(100);menu.Card.UpdateLayout();var card=Save(menu.Card,$"ui-scale-{scale}x-menu",scale);
                Check(menu.Card.ActualHeight*scale<=DisplayGeometry.WorkArea(pet,pet.FromPixels(menu.Card.PointToScreen(new Point()))).Height-16,"Quick menu exceeds work area");var menuWork=DisplayGeometry.WorkArea(pet,pet.FromPixels(menu.Card.PointToScreen(new Point())));
                var menuBounds=new Rect(pet.FromPixels(menu.Card.PointToScreen(new Point())),pet.FromPixels(menu.Card.PointToScreen(new Point(menu.Card.ActualWidth,menu.Card.ActualHeight))));
                Check(menuWork.Contains(menuBounds),"Menu placement outside work area");menu.Close();
                UiSizing.Apply(host,scale);outer.Measure(new Size(760*scale,700*scale));outer.Arrange(new Rect(0,0,760*scale,700*scale));outer.UpdateLayout();
                Check(host.DesiredSize==new Size(760*scale,700*scale),"Settings panel layout did not double");var settings=Save(outer,$"ui-scale-{scale}x-settings");
                foreach(var (name,size) in new[]{("speech",speech),("progress",progress),("todos",todo),("menu",card),("settings",settings)})
                {
                    if(scale==1)sizes[name]=size;else if(name!="menu")Double(sizes[name],size,name);
                    else{Check(Math.Abs(size.Width-sizes[name].Width*2)<=2&&size.Height<=sizes[name].Height*2+2,"Menu scale/clamp");}
                    evidence.Add(new{test=$"render-{scale}x-{name}",pass=true,width=size.Width,height=size.Height});
                }
                var empty=pet.ToPixels(pet.Anchor+new Vector(300,200));
                Check(WindowFromPoint(new((int)empty.X,(int)empty.Y))!=new System.Windows.Interop.WindowInteropHelper(pet).Handle,"Empty canvas intercepts input");
            }
            menu.Open();menu.LimitToWorkArea(new Rect(0,0,1080,900));menu.Card.UpdateLayout();
            var scroller=Find<ScrollViewer>(menu.Card).First();Check(menu.Card.ActualHeight*2<=870&&scroller.ScrollableHeight>0,"Menu did not scroll when capped");menu.Close();
            evidence.Add(new{test="quick-menu-monitor-placement-and-height-cap-scroll",pass=true});
            // Check actual 2x controls, including routed actions through transformed roots.
            int completed=0;pet.ShowTodos(store.Data.Todos,_=>completed++);pet.UpdateLayout();((Button)((Canvas)pet.TodoView).Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(completed==1,"2x completion action");
            pet.ShowTaskProgress("");pet.SetMusicBubble("♪ 音乐控制测试",true,true,true,true);
            var music=(StackPanel)((StackPanel)((Border)pet.SpeechView).Child).Children[1];music.Visibility=Visibility.Visible;pet.UpdateLayout();
            string? command=null;pet.MusicCommand+=value=>command=value;var play=(Button)music.Children[1];var playPoint=play.TranslatePoint(new Point(play.ActualWidth/2,play.ActualHeight/2),pet);
            var musicHit=pet.InputHitTest(playPoint) as DependencyObject;while(musicHit!=null&&musicHit!=play)musicHit=VisualTreeHelper.GetParent(musicHit);Check(musicHit==play,"2x music hit test");
            play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(command=="toggle","2x music command");Save(pet.SpeechView,"ui-scale-2x-music",2);
            evidence.Add(new{test="live-scale-fixed-sprite-transparent-hit-test-todo-and-music-buttons",pass=true});
            var window=new MainWindow(desktopMode:true,store:store);
            try
            {
                window.ShowSettings();window.SettingsPanel.ShowAppearance();await Task.Delay(100);window.UpdateLayout();
                var work=DisplayGeometry.WindowWorkArea(window,pet);Check(work.Contains(new Rect(window.Left,window.Top,window.Width,window.Height)),"Actual settings outside work area");
                Check(WindowChrome.GetWindowChrome(window).CaptionHeight==100&&window.MinHeight<=window.Height&&window.MinWidth<=window.Width,"Caption/min size");
                Save(window.SettingsView,"ui-scale-2x-settings-clamped",2);
                var toggle=Find<CheckBox>(window).Single(c=>Equals(c.Content,"界面放大到 200%"));toggle.IsChecked=false;window.UpdateLayout();
                Check(store.Data.UiScale==1&&new CompanionStore(file).Data.UiScale==1&&WindowChrome.GetWindowChrome(window).CaptionHeight==50,"Switch did not save/apply 1x");
                toggle.IsChecked=true;window.UpdateLayout();Check(store.Data.UiScale==2&&WindowChrome.GetWindowChrome(window).CaptionHeight==100,"Live 2x switch failed");
                Check(work.Contains(new Rect(window.Left,window.Top,window.Width,window.Height)),"Live switch lost window bounds");
                evidence.Add(new{test="actual-settings-clamp-titlebar-minimums-and-live-switch-persistence",pass=true});
            }
            finally{window.ExitApplication();}
        }
        finally{features.SetUiScale(original);engine.Stop();pet.Close();Directory.Delete(directory,true);}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ui-scale-verification.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var item in Find<T>(child))yield return item;}
    }
}
