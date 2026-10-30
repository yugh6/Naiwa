using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NaiwaDesktop
{
    internal sealed partial class PetWindow
    {
        private const int HideHotkey=0x4e41;
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window,int id);
        private readonly IdleCadence cadence=new IdleCadence();
        private readonly RenderWorkspace renderWorkspace=new RenderWorkspace();
        private int windowDpi=96;
        private IntPtr hotkeyWindow;
        private bool adjustingDpi,hotkeyRegistered,saveWarningShown;
        private double breathStart;
        private ToolStripMenuItem lockItem,throughItem,hideItem,breathItem;
        internal bool HiddenManually{get;private set;}
        internal bool IsPetHidden{get{return HiddenManually||HiddenForFullscreen;}}
        internal int RenderScale{get{return DesktopDpi.RenderScale(settings.Scale,windowDpi);}}
        internal int TimerInterval{get{return timer.Interval;}}
        internal bool PositionLocked{get{return settings.LockPosition;}}
        internal bool ClickThroughEnabled{get{return settings.ClickThrough;}}
        internal bool BreathingEnabled{get{return settings.IdleBreathing;}}

        private void InitializeDesktop()
        {
            if(!testing)
            {
                int dpi=DesktopDpi.ForWindow(Handle,windowDpi);
                if(dpi!=windowDpi)ApplyDpi(dpi,Bounds);
                RegisterDesktopHotkey();
                if(!String.IsNullOrEmpty(settings.LoadNotice))Notify(settings.LoadNotice);
            }
            SetClickThrough(settings.ClickThrough);
            breathStart=Now+8;
        }
        private void AddDesktopMenu(ContextMenuStrip target)
        {
            lockItem=new ToolStripMenuItem("固定位置（仍可点击互动）",null,delegate{SetPositionLocked(!settings.LockPosition);});target.Items.Add(lockItem);
            throughItem=new ToolStripMenuItem("鼠标穿透（从托盘恢复）",null,delegate{SetClickThrough(!settings.ClickThrough);});target.Items.Add(throughItem);
            breathItem=new ToolStripMenuItem("轻呼吸（每隔一会儿）",null,delegate{SetBreathing(!settings.IdleBreathing);});target.Items.Add(breathItem);
            hideItem=new ToolStripMenuItem("暂时隐藏奶蛙",null,delegate{SetManualHidden(!HiddenManually);});
            hideItem.ShortcutKeyDisplayString="Ctrl+Alt+N";target.Items.Add(hideItem);
        }
        private void UpdateDesktopMenu()
        {
            if(lockItem==null)return;
            SetToggleMark(lockItem,settings.LockPosition);SetToggleMark(throughItem,settings.ClickThrough);SetToggleMark(breathItem,settings.IdleBreathing);
            hideItem.Text=HiddenManually?"显示奶蛙":"暂时隐藏奶蛙";
            hideItem.Enabled=throughItem.Enabled=!FeedingBusy;
        }
        internal void SetPositionLocked(bool value)
        {
            CancelDesktopGesture();settings.LockPosition=value;UpdateMenu();Save();RenderNow(true);
        }
        internal bool SetClickThrough(bool value)
        {
            if(FeedingBusy)return false;
            if(IsHandleCreated&&!DesktopDpi.SetClickThrough(Handle,value))
            {Notify("暂时无法切换鼠标穿透，请重试。");return false;}
            CancelDesktopGesture();settings.ClickThrough=value;AllowDrop=!value;feedHover=false;
            UpdateMenu();Save();RenderNow(true);return true;
        }
        internal void SetBreathing(bool enabled)
        {settings.IdleBreathing=enabled;breathStart=Now+2;Motion.BreathAmount=0;UpdateMenu();Save();RenderNow(true);}
        internal bool SetManualHidden(bool hidden)
        {
            if(FeedingBusy)return false;
            HiddenManually=hidden;ApplyVisibility();UpdateMenu();return true;
        }
        private void CancelDesktopGesture()
        {Motion.EndDrag(Now);gesture.Cancel();Capture=false;}
        private void ApplyVisibility()
        {
            CancelDesktopGesture();feedHover=false;Motion.Suspend(Now);gaze.Reset();
            if(sound!=null)sound.Stop();
            if(IsPetHidden){Hide();timer.Interval=150;}
            else{timer.Interval=16;breathStart=Now+8;Show();RenderNow(true);}
        }
        internal void ApplyDpi(int dpi,Rectangle suggested)
        {
            if(adjustingDpi)return;
            adjustingDpi=true;
            try
            {
                windowDpi=DesktopDpi.Normalize(dpi);
                Bounds=new Rectangle(suggested.Location,SizeFor(settings.Scale));
                if(gesture.Pressed)
                {dragWindow=Location;lastDragPointer=Cursor.Position;gesture.Origin=lastDragPointer;}
                else KeepVisible();
                lastVisual="";RenderNow(true);Save();
            }
            finally{adjustingDpi=false;}
        }
        internal void UpdateCadence(double now,Point pointer,bool over,bool gazeChanged)
        {
            bool active=Motion.Playing||Motion.Blinking||Motion.Poking||Motion.Patting||Motion.Moving||Motion.Waking||gesture.Pressed||
                gazeChanged||(!settings.FollowCursor&&Math.Abs(Motion.Look-(over?1:0))>.001)||
                (feedSucceeded&&now-feedResultTime<.8);
            // Normal blinking and gaze changes must not keep postponing the next breath.
            bool canBreathe=!Motion.Playing&&!Motion.Resting&&!Motion.Poking&&!Motion.Patting&&!Motion.Moving&&!Motion.Waking&&!gesture.Pressed&&!feedHover&&!FeedingBusy&&!menuOpen;
            if(!canBreathe)breathStart=now+8;
            Motion.BreathAmount=IdleCadence.Breath(now,breathStart,settings.IdleBreathing,!canBreathe);
            timer.Interval=cadence.Update(now,pointer,active,Motion.BreathAmount>0);
        }
        private void RegisterDesktopHotkey()
        {
            if(testing||hotkeyRegistered)return;
            IntPtr window=Handle;
            hotkeyRegistered=RegisterHotKey(window,HideHotkey,0x4003,0x4e);
            if(hotkeyRegistered)hotkeyWindow=window;
            if(!hotkeyRegistered)Notify("Ctrl+Alt+N 快捷键暂不可用，仍可通过托盘菜单隐藏或显示奶蛙。");
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if(ready){RegisterDesktopHotkey();DesktopDpi.SetClickThrough(Handle,settings.ClickThrough);}
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if(hotkeyRegistered){UnregisterHotKey(hotkeyWindow,HideHotkey);hotkeyRegistered=false;hotkeyWindow=IntPtr.Zero;}
            base.OnHandleDestroyed(e);
        }
    }
}
