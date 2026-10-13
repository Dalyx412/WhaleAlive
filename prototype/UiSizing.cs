using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;
namespace WhaleAlive;

internal static class UiSizing
{
    internal static readonly double[] UiLevels={1,1.25,1.5,1.75,2};
    // Anything not one of the offered levels (old/edited files) falls back to 100%.
    internal static double CoerceUi(double value)=>UiLevels.FirstOrDefault(l=>Math.Abs(l-value)<.001,1);
    internal static void Apply(FrameworkElement root,double scale)
    {
        if(root.LayoutTransform is not ScaleTransform current||current.ScaleX!=scale||current.ScaleY!=scale)root.LayoutTransform=new ScaleTransform(scale,scale);
        TextOptions.SetTextFormattingMode(root,TextFormattingMode.Ideal);
    }
    internal static Rect Clamp(Rect rect,Rect work,double margin=8)
    {
        double width=Math.Min(rect.Width,Math.Max(1,work.Width-2*margin)),height=Math.Min(rect.Height,Math.Max(1,work.Height-2*margin));
        return new(Math.Clamp(rect.X,work.Left+margin,work.Right-margin-width),Math.Clamp(rect.Y,work.Top+margin,work.Bottom-margin-height),width,height);
    }
    internal static Rect SettingsBounds(Point origin,Rect work,double scale)=>Clamp(new Rect(origin,new Size(1080*scale,780*scale)),work,20);
    internal static void Settings(Window window,FrameworkElement root,double scale,Rect work,bool resize=true)
    {
        var origin=new Point(double.IsFinite(window.Left)?window.Left:work.Left+20,double.IsFinite(window.Top)?window.Top:work.Top+20);
        // Default size only on first apply or a scale change; otherwise keep the user's size and just clamp.
        var bounds=resize||!double.IsFinite(window.Width)?SettingsBounds(origin,work,scale):Clamp(new Rect(origin,new Size(window.Width,window.Height)),work,20);Apply(root,scale);
        window.MinWidth=Math.Min(960*scale,bounds.Width);window.MinHeight=Math.Min(650*scale,bounds.Height);
        window.Width=bounds.Width;window.Height=bounds.Height;window.Left=bounds.Left;window.Top=bounds.Top;
        if(WindowChrome.GetWindowChrome(window) is {} chrome)chrome.CaptionHeight=50*scale;
    }
    // Speech must clear the character and every todo bubble individually: a union box
    // pushes it far from the head, and ignoring todos lets it cover the top of the arc.
    internal static Rect Beside(Rect preferred,Rect character,IReadOnlyList<Rect> others,Rect work,double gap)
    {
        var blockers=others.Where(r=>r.Width>0&&r.Height>0).ToArray();
        if(blockers.Length==0)return Beside(preferred,character,work,gap);
        double w=preferred.Width,h=preferred.Height,head=character.Top-gap-h;
        bool othersLeft=blockers.Average(r=>r.Left+r.Width/2)<character.Left+character.Width/2;
        double clearX=othersLeft?blockers.Max(r=>r.Right)+gap:blockers.Min(r=>r.Left)-gap-w;
        double aboveAll=Math.Min(character.Top,blockers.Min(r=>r.Top))-gap-h;
        var candidates=new[]{preferred,new Rect(clearX,preferred.Y,w,h),new Rect(preferred.X,head,w,h),new Rect(clearX,head,w,h),new Rect(preferred.X,aboveAll,w,h),
            new Rect(othersLeft?character.Right+gap:character.Left-gap-w,preferred.Y,w,h),new Rect(preferred.X,character.Bottom+gap,w,h)}.Select(r=>Clamp(r,work)).ToArray();
        return candidates.FirstOrDefault(r=>!r.IntersectsWith(character)&&!blockers.Any(b=>b.IntersectsWith(r)),candidates[0]);
    }
    internal static Rect Beside(Rect preferred,Rect character,Rect work,double gap)
    {
        var candidates=new[]{preferred,new Rect(preferred.X,character.Top-gap-preferred.Height,preferred.Width,preferred.Height),
            new Rect(character.Right+gap,preferred.Y,preferred.Width,preferred.Height),new Rect(character.Left-gap-preferred.Width,preferred.Y,preferred.Width,preferred.Height),
            new Rect(preferred.X,character.Bottom+gap,preferred.Width,preferred.Height)}.Select(r=>Clamp(r,work)).ToArray();
        return candidates.FirstOrDefault(r=>!r.IntersectsWith(character),candidates[0]);
    }
}
