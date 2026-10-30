using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security;
using System.Threading;
using System.Windows.Forms;

namespace NaiwaDesktop
{
    // Optional help/audio failures must not enter the fatal application handler.
    internal static class OptionalActions
    {
        internal static bool Try(Action action,out string error)
        {
            try{action();error=null;return true;}
            catch(Exception ex)
            {
                if(!(ex is IOException)&&!(ex is UnauthorizedAccessException)&&!(ex is SecurityException)&&
                   !(ex is Win32Exception)&&!(ex is InvalidOperationException)&&!(ex is TimeoutException))throw;
                error=ex.Message;return false;
            }
        }
        internal static void OpenHelp(string path,Action<string> open)
        {
            if(!File.Exists(path))throw new FileNotFoundException("没有找到使用说明.txt，请将它放在程序旁边。",path);
            open(path);
        }
    }

    internal sealed partial class PetWindow
    {
        private bool feedHover,feedBusy,feedConfirming,feedCanAccept;
        internal Func<string[],IntPtr,RecycleResult> RecycleFiles=Recycling.Recycle;
        private string feedMessage="";
        private double feedResultTime=-100;
        private bool feedSucceeded;
        internal bool FeedingBusy{get{return feedBusy||feedConfirming;}}
        private string FeedingVisualKey
        {
            get
            {
                if(feedHover||feedBusy)return "/F"+feedMessage;
                double age=Now-feedResultTime;
                if(age>=3)return "";
                return "/F"+feedMessage+(feedSucceeded&&age<.8?"/"+(int)(age*30):"");
            }
        }
        private void InitializeFeeding(){AllowDrop=!settings.ClickThrough;}
        private void Notify(string message)
        {
            if(testing)return;
            if(tray!=null){tray.BalloonTipTitle="奶蛙桌宠";tray.BalloonTipText=message;tray.ShowBalloonTip(5000);}
            else MessageBox.Show(this,message,"奶蛙桌宠",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        private void OpenHelp()
        {
            string error;
            if(!OptionalActions.Try(delegate{OptionalActions.OpenHelp(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"使用说明.txt"),
                delegate(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true});});},out error))Notify(error);
        }
        internal static string[] FileDropPaths(IDataObject data)
        {
            if(data==null||!data.GetDataPresent(DataFormats.FileDrop,false))return null;
            return data.GetData(DataFormats.FileDrop,false) as string[];
        }
        private string[] CheckDrop(DragEventArgs e,out string error)
        {
            string[] paths=null;
            try{paths=FileDropPaths(e.Data);error=Recycling.Validate(paths);}
            catch(Exception ex)
            {
                if(!(ex is System.Runtime.InteropServices.ExternalException)&&!(ex is IOException)&&!(ex is InvalidOperationException))throw;
                error="无法读取拖入的文件，请从文件资源管理器重新拖入。";
            }
            if(paths==null||paths.Length==0){error="请拖入本地普通文件。";return null;}
            return paths;
        }
        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);e.Effect=DragDropEffects.None;
            if(FeedingBusy||IsPetHidden||settings.ClickThrough)return;
            string error;string[] paths=CheckDrop(e,out error);
            feedHover=true;feedSucceeded=false;
            feedResultTime=-100;
            feedCanAccept=error==null;
            feedMessage=error==null?"松开投喂 "+paths.Length+" 个文件":"暂不支持这类文件";
            if(error!=null)Notify(error);
            if(error==null&&(e.AllowedEffect&DragDropEffects.Move)!=0)e.Effect=DragDropEffects.Move;
            RenderNow(true);
        }
        protected override void OnDragOver(DragEventArgs e)
        {
            base.OnDragOver(e);
            // Reuse the validated hover state; filesystem work is done on enter and drop.
            e.Effect=feedHover&&!FeedingBusy&&!IsPetHidden&&!settings.ClickThrough&&feedCanAccept&&(e.AllowedEffect&DragDropEffects.Move)!=0?
                DragDropEffects.Move:DragDropEffects.None;
        }
        protected override void OnDragLeave(EventArgs e)
        {base.OnDragLeave(e);feedHover=false;RenderNow(true);}
        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);AcceptFileDrop(e);
        }
        internal void AcceptFileDrop(DragEventArgs e)
        {
            e.Effect=DragDropEffects.None;feedHover=false;
            if(FeedingBusy||IsPetHidden||settings.ClickThrough){RenderNow(true);return;}
            string error;string[] paths=CheckDrop(e,out error);
            if(error!=null){FeedFailed(error);return;}
            if((e.AllowedEffect&DragDropEffects.Move)==0){FeedFailed("来源不允许移动此文件。");return;}
            StopLaugh();gesture.Cancel();Capture=false;
            if(settings.ConfirmFeeding&&!testing)
            {
                feedConfirming=true;
                string names="";for(int i=0;i<Math.Min(paths.Length,5);i++)names+="\n• "+Path.GetFileName(paths[i]);
                if(paths.Length>5)names+="\n……";
                DialogResult answer;
                try{answer=MessageBox.Show(this,"将这 "+paths.Length+" 个文件送入系统回收站？"+names+"\n\n可以在回收站中还原。",
                    "喂给奶蛙",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2);}
                finally{feedConfirming=false;}
                if(answer!=DialogResult.OK){feedMessage="已取消投喂";feedSucceeded=false;feedResultTime=Now;RenderNow(true);return;}
            }
            feedBusy=true;feedMessage="正在送入回收站…";RenderNow(true);
            IntPtr owner=Handle;
            Thread worker=new Thread(delegate()
            {
                RecycleResult result=null;string failure=null;
                try{result=RecycleFiles(paths,owner);}
                catch(Exception ex){failure="文件投喂未完成："+ex.Message;}
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        feedBusy=false;
                        if(failure!=null){FeedFailed(failure);return;}
                        feedSucceeded=result.RecycledCount>0;feedResultTime=Now;
                        feedMessage=feedSucceeded?"已回收 "+result.RecycledCount+" 个文件":result.Cancelled?"已取消投喂":"文件没有被吃掉";
                        if(feedSucceeded)Motion.Poke(Now,144,211);
                        if(result.FailedCount>0||result.Cancelled)Notify(result.Summary);
                        RenderNow(true);
                    });
                }
                catch(InvalidOperationException){} // Window can disappear during Windows shutdown.
            });
            worker.IsBackground=true;worker.SetApartmentState(ApartmentState.STA);worker.Start();
            // The shell operation owns the move. Do not advertise a completed OLE move
            // before its asynchronous result: some sources delete on a Move result.
            e.Effect=DragDropEffects.None;
        }
        private void FeedFailed(string message)
        {feedBusy=feedHover=false;feedSucceeded=false;feedResultTime=Now;feedMessage="文件没有被吃掉";Notify(message);RenderNow(true);}
        private void DrawFeeding(Bitmap frame)
        {
            double age=Now-feedResultTime;
            if(!feedHover&&!feedBusy&&age>=3)return;
            using(Graphics g=Graphics.FromImage(frame))
            {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.ScaleTransform(frame.Width/(float)assets.Width,frame.Height/(float)assets.Height);
                using(Brush background=new SolidBrush(Color.FromArgb(225,37,47,35)))
                using(Brush foreground=new SolidBrush(Color.White))
                using(Font font=new Font("Microsoft YaHei UI",10f,FontStyle.Regular,GraphicsUnit.Pixel))
                using(StringFormat format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                {
                    RectangleF box=new RectangleF(36,8,216,26);
                    g.FillRectangle(background,box);g.DrawString(feedMessage,font,foreground,box,format);
                }
                // A small paper icon travels into the original mouth after confirmed
                // recycling. The supplied character pixels are never replaced.
                if(feedSucceeded&&!feedHover&&!feedBusy&&age<.8)
                {
                    float t=(float)Math.Max(0,age/.8),ease=1-(1-t)*(1-t),size=18*(1-t);
                    float x=204+(122-204)*ease,y=60+(83-60)*ease;
                    using(Brush paper=new SolidBrush(Color.FromArgb((int)(255*(1-t)),255,252,222)))
                    using(Pen line=new Pen(Color.FromArgb((int)(255*(1-t)),105,114,82),1))
                    {g.FillRectangle(paper,x-size/2,y-size/2,size,size*1.2f);g.DrawRectangle(line,x-size/2,y-size/2,size,size*1.2f);}
                }
            }
        }
    }
}
