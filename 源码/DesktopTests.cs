using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NaiwaDesktop
{
    internal static class DesktopTests
    {
        private static void Check(bool value,string name,List<string> log)
        {if(!value)throw new Exception("FAIL: "+name);log.Add("PASS: "+name);}
        private static IntPtr PointAt(PetWindow form,int x,int y,Assets assets)
        {return (IntPtr)(((y*form.ClientSize.Height/assets.Height)<<16)|(x*form.ClientSize.Width/assets.Width));}
        private static void Click(PetWindow form,IntPtr point,bool twice)
        {Native.SendMessage(form.Handle,twice?0x203:0x201,(IntPtr)1,point);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,point);}
        internal static void Run(Assets assets,string folder,List<string> log)
        {
            IdleCadence cadence=new IdleCadence();Point pointer=new Point(10,20);
            Check(cadence.Update(0,pointer,false,false)==16&&cadence.Update(.36,pointer,false,false)==80,"settled idle reduces timer wake frequency",log);
            Check(cadence.Update(.4,new Point(11,20),false,false)==16,"pointer movement promptly restores fast updates",log);
            Check(cadence.Update(1,pointer,true,false)==16&&cadence.Update(2,pointer,false,true)==50,"interactions and optional breathing use appropriate cadence",log);
            Check(IdleCadence.Breath(2,0,false,false)==0&&IdleCadence.Breath(2,0,true,true)==0,"breathing is opt-in and suppressed by interaction",log);
            Check(Math.Abs(IdleCadence.Breath(2,0,true,false)-.006)<.000001&&IdleCadence.Breath(5,0,true,false)==0&&IdleCadence.Breath(22,0,true,false)>0,"breathing is subtle with long quiet gaps",log);
            Gesture g=new Gesture();g.Down(pointer,new Size(20,16),new Size(8,8));g.Move(new Point(19,27));
            Check(!g.Dragged,"pointer movement within system drag rectangle remains a click",log);g.Move(new Point(20,28));
            Check(g.Dragged&&!g.Up(pointer,1,1),"crossing system drag rectangle cancels click",log);
            using(PetWindow form=new PetWindow(assets,true))
            {
                form.Show();Application.DoEvents();form.StopLaugh();
                Check(!form.PositionLocked&&!form.ClickThroughEnabled&&!form.BreathingEnabled,"new desktop modes preserve defaults",log);
                form.SetPositionLocked(true);Point original=form.Location;
                Native.SendMessage(form.Handle,0x201,(IntPtr)1,PointAt(form,144,211,assets));
                Native.SendMessage(form.Handle,0x200,(IntPtr)1,PointAt(form,205,250,assets));
                Native.SendMessage(form.Handle,0x202,IntPtr.Zero,PointAt(form,205,250,assets));
                Check(form.Location==original&&!form.Motion.Holding&&!form.Motion.Playing&&!form.Motion.Poking,"position lock prevents movement and drag-release clicks",log);
                Click(form,PointAt(form,130,70,assets),false);
                Check(form.Motion.Patting,"position lock still permits head interaction",log);form.StopLaugh();
                form.SetPositionLocked(false);
                Check(form.SetClickThrough(true)&&(Native.GetWindowLong(form.Handle,-20)&0x20)!=0&&!form.AllowDrop,"click-through sets layered-window style and disables file drops",log);
                Click(form,PointAt(form,130,70,assets),false);
                Check(!form.Motion.Patting,"click-through ignores injected pet clicks",log);
                bool recyclerCalled=false;
                form.RecycleFiles=delegate(string[] paths,IntPtr owner){recyclerCalled=true;return new RecycleResult(paths);};
                string fixture=Path.Combine(folder,"desktop-drop-"+Guid.NewGuid().ToString("N")+".txt");File.WriteAllText(fixture,"Desktop mode test. Must never be recycled.");
                DataObject files=new DataObject(DataFormats.FileDrop,new string[]{fixture});
                form.AcceptFileDrop(new DragEventArgs(files,0,0,0,DragDropEffects.Move,DragDropEffects.None));
                Check(!recyclerCalled&&!form.FeedingBusy&&File.Exists(fixture),"click-through rejects OLE file payloads",log);
                Check(form.SetClickThrough(false)&&(Native.GetWindowLong(form.Handle,-20)&0x20)==0&&form.AllowDrop,"tray mode restores click and drop interaction",log);
                form.Laugh();form.SetManualHidden(true);
                Check(form.HiddenManually&&!Native.IsWindowVisible(form.Handle)&&!form.Motion.Playing&&!form.Laugh(),"manual hide stops playback and keeps the window hidden",log);
                form.ApplyFullscreen(true);form.ApplyFullscreen(false);form.SetAutoHide(false);form.SetSize(110);
                Check(form.HiddenManually&&!Native.IsWindowVisible(form.Handle),"fullscreen exit and settings changes cannot undo manual hide",log);
                form.AcceptFileDrop(new DragEventArgs(files,0,0,0,DragDropEffects.Move,DragDropEffects.None));
                Check(!recyclerCalled&&!form.FeedingBusy,"hidden pet rejects file feeding",log);
                Native.SendMessage(form.Handle,0x312,(IntPtr)0x4e41,IntPtr.Zero);
                Check(!form.HiddenManually&&Native.IsWindowVisible(form.Handle),"hide hotkey handler can restore pet",log);
                form.SetClickThrough(true);form.SetManualHidden(true);form.ReturnHome();
                Check(!form.HiddenManually&&!form.ClickThroughEnabled&&Native.IsWindowVisible(form.Handle),"tray recovery restores visibility and mouse interaction",log);
                foreach(int dpi in new int[]{96,144,192})foreach(int scale in new int[]{65,85,110})
                {
                    form.StopLaugh();form.SetSize(scale);
                    Native.Rect rect=new Native.Rect{Left=form.Left,Top=form.Top,Right=form.Right,Bottom=form.Bottom};
                    IntPtr memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Rect)));
                    try{Marshal.StructureToPtr(rect,memory,false);Native.SendMessage(form.Handle,0x2e0,(IntPtr)((dpi<<16)|dpi),memory);}
                    finally{Marshal.FreeHGlobal(memory);}
                    int effective=DesktopDpi.RenderScale(scale,dpi);
                    Check(form.ClientSize==new Size(assets.Width*effective/100,assets.Height*effective/100),"native DPI change resizes pet at "+dpi+" DPI / "+scale+"%",log);
                    IntPtr head=PointAt(form,130,70,assets);Click(form,head,false);
                    Check(form.Motion.Patting,"head hit follows actual client pixels at "+dpi+" DPI / "+scale+"%",log);
                    form.StopLaugh();IntPtr belly=PointAt(form,144,211,assets);Click(form,belly,false);Click(form,belly,true);
                    Check(form.Motion.Playing,"belly double click follows DPI scaling at "+dpi+" DPI / "+scale+"%",log);
                }
                form.StopLaugh();form.SetManualHidden(true);form.ApplyDpi(96,form.Bounds);
                Check(!Native.IsWindowVisible(form.Handle),"DPI change cannot reveal manually hidden pet",log);
                form.SetManualHidden(false);form.SetSize(85);form.SetBreathing(true);
                Check(form.BreathingEnabled,"optional breathing setting can be enabled",log);
                form.Motion.NextBlink=100;bool breathingSeen=false,blinkSeen=false;
                for(double now=100;now<135;now+=.05)
                {
                    form.Motion.Update(now,false,assets.Duration,assets.BlinkCount/assets.BlinkFps);
                    form.UpdateCadence(now,pointer,false,false);
                    breathingSeen|=form.Motion.BreathAmount>0;blinkSeen|=form.Motion.Blinking;
                }
                Check(breathingSeen&&blinkSeen,"normal blinking does not indefinitely postpone breathing",log);
                form.SetBreathing(false);
                Check(!form.BreathingEnabled&&form.Motion.BreathAmount==0,"breathing can return to original still pose",log);
                form.Close();
            }
        }
    }
}
