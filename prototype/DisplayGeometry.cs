using System.Runtime.InteropServices;
using System.Windows;

namespace WhaleAlive;
public static class DisplayGeometry
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(ShellDesktop.POINT point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);
    internal static Rect WindowWorkArea(Window window,PetWindow pet)
    {
        var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if(handle==0||!window.IsVisible)return ToWindowUnits(window,WorkArea(pet,pet.Anchor),pet);
        var info=new MONITORINFO{Size=Marshal.SizeOf<MONITORINFO>()};
        if(!GetMonitorInfo(MonitorFromWindow(handle,2),ref info))return SystemParameters.WorkArea;
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);
        return new(info.Work.Left/dpi.DpiScaleX,info.Work.Top/dpi.DpiScaleY,(info.Work.Right-info.Work.Left)/dpi.DpiScaleX,(info.Work.Bottom-info.Work.Top)/dpi.DpiScaleY);
    }
    static Rect ToWindowUnits(Window window,Rect work,PetWindow pet)
    {
        var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(window);var a=pet.ToPixels(work.TopLeft);var b=pet.ToPixels(work.BottomRight);
        return new(a.X/dpi.DpiScaleX,a.Y/dpi.DpiScaleY,(b.X-a.X)/dpi.DpiScaleX,(b.Y-a.Y)/dpi.DpiScaleY);
    }
    [DllImport("user32.dll")]static extern nint MonitorFromWindow(nint window,uint flags);
    public static Rect WorkArea(PetWindow pet, Point point)
    {
        var physical = pet.ToPixels(point);
        var monitor = MonitorFromPoint(new((int)physical.X, (int)physical.Y), 2);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        return new Rect(pet.FromPixels(new(info.Work.Left, info.Work.Top)), pet.FromPixels(new(info.Work.Right, info.Work.Bottom)));
    }
}
