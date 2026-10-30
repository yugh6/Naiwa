using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NaiwaDesktop
{
    internal static class DesktopDpi
    {
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Native.Point point,uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor,int kind,out uint x,out uint y);
        [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index,uint dpi);
        [DllImport("user32.dll",SetLastError=true)] private static extern int SetWindowLong(IntPtr window,int index,int value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);

        internal static int Normalize(int dpi){return Math.Max(96,Math.Min(768,dpi));}
        internal static int RenderScale(int percent,int dpi){return Math.Max(1,(int)Math.Round(percent*Normalize(dpi)/96.0));}
        internal static int AtPoint(Point point)
        {
            try{uint x,y;if(GetDpiForMonitor(MonitorFromPoint(new Native.Point(point.X,point.Y),2),0,out x,out y)==0)return Normalize((int)x);}
            catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
            return 96;
        }
        internal static int ForWindow(IntPtr window,int fallback)
        {
            try{uint dpi=GetDpiForWindow(window);if(dpi!=0)return Normalize((int)dpi);}
            catch(EntryPointNotFoundException){}
            return fallback;
        }
        internal static Size DragSize(int dpi){return Metrics(68,69,SystemInformation.DragSize,dpi);}
        internal static Size DoubleClickSize(int dpi){return Metrics(36,37,SystemInformation.DoubleClickSize,dpi);}
        private static Size Metrics(int x,int y,Size fallback,int dpi)
        {
            try{return new Size(Math.Max(1,GetSystemMetricsForDpi(x,(uint)dpi)),Math.Max(1,GetSystemMetricsForDpi(y,(uint)dpi)));}
            catch(EntryPointNotFoundException){return fallback;}
        }
        internal static PointF ToArtwork(Point point,Size client,int width,int height)
        {return new PointF(point.X*width/(float)Math.Max(1,client.Width),point.Y*height/(float)Math.Max(1,client.Height));}
        internal static bool SetClickThrough(IntPtr window,bool enabled)
        {
            int style=Native.GetWindowLong(window,-20),desired=enabled?style|0x20:style&~0x20;
            if(style!=desired){SetWindowLong(window,-20,desired);SetWindowPos(window,IntPtr.Zero,0,0,0,0,0x37);}
            return ((Native.GetWindowLong(window,-20)&0x20)!=0)==enabled;
        }
    }

    internal sealed class IdleCadence
    {
        private Point previous;
        private bool seen;
        private double lastActivity;
        internal int Update(double now,Point pointer,bool active,bool breathing)
        {
            if(!seen||pointer!=previous||active){lastActivity=now;previous=pointer;seen=true;}
            if(active||now-lastActivity<.35)return 16;
            return breathing?50:80;
        }
        internal static double Breath(double now,double start,bool enabled,bool busy)
        {
            if(!enabled||busy||now<start)return 0;
            double phase=(now-start)%20;
            return phase<4?.006*(1-Math.Cos(phase*Math.PI/2))/2:0;
        }
    }
}
