using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace NaiwaDesktop
{
    internal static class FeatureTests
    {
        internal static void Run(Assets assets,string folder,List<string> log)
        {
            GazeTests.Run(assets.Eye(0),folder,log);
            HeadPatTests.Run(assets.Eye(0),folder,log);
            Recycling.RunValidationTests(folder,log);
            Motion pat=new Motion();
            Check(pat.PatHead(0,130,70)&&pat.Patting&&!pat.Playing,"single head click starts pat without laughter",log);
            pat.Update(HeadPat.Duration+.01,false,assets.Duration,assets.BlinkCount/assets.BlinkFps);
            Check(!pat.Patting,"head pat returns to idle",log);
            Check(!pat.PatHead(1,144,211),"belly cannot trigger head pat",log);
            pat.PatHead(1,130,70);pat.Laugh(1.1);
            Check(!pat.Patting&&pat.Playing,"laugh cleanly interrupts head pat",log);
            pat.Stop();pat.PatHead(2,130,70);pat.BeginDrag(2.1);
            Check(!pat.Patting&&pat.Holding,"drag cleanly interrupts head pat",log);
            Gesture zones=new Gesture();System.Drawing.Point click=new System.Drawing.Point(100,100);
            zones.Down(click);zones.Up(click,1,1);zones.Down(click);
            Check(!zones.Up(click,1.1,2),"different body regions cannot form a double click",log);
            string error;bool launched=false;
            string missing=Path.Combine(folder,"missing-help-"+Guid.NewGuid().ToString("N")+".txt");
            Check(!OptionalActions.Try(delegate{OptionalActions.OpenHelp(missing,delegate{launched=true;});},out error)&&!launched&&!String.IsNullOrEmpty(error),"missing help is recoverable without launching",log);
            Check(!OptionalActions.Try(delegate{throw new InvalidOperationException("invalid WAV");},out error)&&error=="invalid WAV","optional audio error is recoverable",log);
            bool fatalPropagated=false;
            try{OptionalActions.Try(delegate{throw new OutOfMemoryException();},out error);}catch(OutOfMemoryException){fatalPropagated=true;}
            Check(fatalPropagated,"fatal errors are not silently swallowed",log);
            DataObject text=new DataObject(DataFormats.Text,"file:///C:/example.txt");
            Check(PetWindow.FileDropPaths(text)==null,"text and URLs cannot initiate recycling",log);

            string fixture=Path.Combine(Path.GetFullPath(folder),"naiwa-ui-fixture-"+Guid.NewGuid().ToString("N")+".txt");
            File.WriteAllText(fixture,"Generated UI fixture. A mock recycler leaves this file untouched.");
            DataObject files=new DataObject(DataFormats.FileDrop,new string[]{fixture});
            Check(PetWindow.FileDropPaths(files)[0]==fixture,"Explorer file payload is recognized",log);
            using(PetWindow form=new PetWindow(assets,true))
            using(ManualResetEvent release=new ManualResetEvent(false))
            {
                form.Show();Application.DoEvents();bool workerSta=false;
                foreach(int scale in new int[]{65,85,110})
                {
                    form.SetSize(scale);form.StopLaugh();
                    IntPtr head=(IntPtr)(((70*scale/100)<<16)|(130*scale/100));
                    Native.SendMessage(form.Handle,0x201,(IntPtr)1,head);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,head);
                    Check(form.Motion.Patting&&!form.Motion.Playing,"native head click starts pat at size "+scale,log);
                    Native.SendMessage(form.Handle,0x203,(IntPtr)1,head);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,head);
                    Check(form.Motion.Patting&&!form.Motion.Playing,"head double click never laughs at size "+scale,log);
                    form.StopLaugh();
                    IntPtr foot=(IntPtr)(((330*scale/100)<<16)|(150*scale/100));
                    Native.SendMessage(form.Handle,0x201,(IntPtr)1,foot);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,foot);
                    Native.SendMessage(form.Handle,0x203,(IntPtr)1,foot);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,foot);
                    Check(!form.Motion.Playing&&!form.Motion.Patting,"feet double click never laughs at size "+scale,log);
                    IntPtr belly=(IntPtr)(((211*scale/100)<<16)|(144*scale/100));
                    Native.SendMessage(form.Handle,0x201,(IntPtr)1,belly);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,belly);
                    Check(form.Motion.Poking&&!form.Motion.Playing,"single belly click still pokes at size "+scale,log);
                    Native.SendMessage(form.Handle,0x203,(IntPtr)1,belly);Native.SendMessage(form.Handle,0x202,IntPtr.Zero,belly);
                    Check(form.Motion.Playing&&!form.Motion.Patting,"belly double click laughs at size "+scale,log);
                }
                form.SetSize(85);form.StopLaugh();
                form.RecycleFiles=delegate(string[] paths,IntPtr owner)
                {
                    workerSta=Thread.CurrentThread.GetApartmentState()==ApartmentState.STA;
                    release.WaitOne(5000);
                    RecycleResult result=new RecycleResult(paths);
                    result.Items[0].Error="fixture is intentionally not recycled";
                    return result;
                };
                DragEventArgs drop=new DragEventArgs(files,0,0,0,DragDropEffects.Move,DragDropEffects.None);
                form.AcceptFileDrop(drop);
                Check(form.FeedingBusy&&!form.Motion.Poking,"feeding waits for operation before feedback",log);
                Check(drop.Effect==DragDropEffects.None,"async drop does not ask the source to delete again",log);
                Check(!form.Laugh(),"busy recycling cannot overlap laughter",log);
                Check(!form.SetManualHidden(true)&&!form.HiddenManually,"recycle operation keeps its owner window available",log);
                Check(!form.SetClickThrough(true)&&!form.ClickThroughEnabled,"recycle operation cannot enable click-through mid-drop",log);
                release.Set();PumpUntilIdle(form);
                Check(workerSta&&!form.FeedingBusy&&!form.Motion.Poking,"failed STA recycle completes without success animation",log);
                form.RecycleFiles=delegate(string[] paths,IntPtr owner)
                {
                    RecycleResult result=new RecycleResult(paths);result.Items[0].Success=true;return result;
                };
                form.AcceptFileDrop(new DragEventArgs(files,0,0,0,DragDropEffects.Move,DragDropEffects.None));PumpUntilIdle(form);
                Check(form.Motion.Poking,"confirmed recycle success triggers belly feedback",log);
                form.StopLaugh();bool called=false;
                form.RecycleFiles=delegate(string[] paths,IntPtr owner){called=true;return new RecycleResult(paths);};
                form.AcceptFileDrop(new DragEventArgs(text,0,0,0,DragDropEffects.Move,DragDropEffects.None));
                Check(!called&&!form.FeedingBusy,"invalid drop never calls the recycler",log);
                form.Close();
            }
            Check(File.Exists(fixture),"UI tests never delete their fixture",log);
        }
        private static void PumpUntilIdle(PetWindow form)
        {
            Stopwatch timeout=Stopwatch.StartNew();
            while(form.FeedingBusy&&timeout.ElapsedMilliseconds<6000){Application.DoEvents();Thread.Sleep(5);}
            if(form.FeedingBusy)throw new Exception("FAIL: recycle callback did not complete");
        }
        private static void Check(bool value,string name,List<string> log)
        {if(!value)throw new Exception("FAIL: "+name);log.Add("PASS: "+name);}
    }
}
