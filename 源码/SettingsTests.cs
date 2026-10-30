using System;
using System.Collections.Generic;
using System.IO;

namespace NaiwaDesktop
{
    internal static class SettingsTests
    {
        internal static void Run(string folder,List<string> log)
        {
            string root=Path.Combine(Path.GetFullPath(folder),"settings-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string legacy=Case(root,"legacy");
            File.WriteAllLines(Path.Combine(legacy,"settings.ini"),new string[]{"scale=110","x=-420","y=123","sound=True","top=False","fullscreen=False"});
            Settings value=Settings.Load(legacy);
            Check(value.Scale==110&&value.Position&&value.X==-420&&value.Y==123&&value.Sound&&!value.OnTop&&!value.AutoHide,"legacy settings and negative-screen coordinates load",log);
            Check(value.FollowCursor&&value.ConfirmFeeding&&!value.LockPosition&&!value.ClickThrough&&!value.IdleBreathing,"new settings retain safe defaults for legacy files",log);

            string roundtrip=Case(root,"roundtrip");
            Settings saved=new Settings{Scale=65,X=-70,Y=250,Sound=true,OnTop=false,AutoHide=false,FollowCursor=false,ConfirmFeeding=false,LockPosition=true,ClickThrough=true,IdleBreathing=true};
            Check(saved.Save(roundtrip),"first settings save succeeds",log);
            value=Settings.Load(roundtrip);
            Check(value.Scale==65&&value.Position&&value.X==-70&&value.Y==250&&value.Sound&&!value.OnTop&&!value.AutoHide&&!value.FollowCursor&&!value.ConfirmFeeding&&value.LockPosition&&value.ClickThrough&&value.IdleBreathing,"all settings survive atomic roundtrip",log);
            saved.Scale=110;Check(saved.Save(roundtrip),"second settings save atomically replaces primary",log);
            string primary=Path.Combine(roundtrip,"settings.ini"),backup=primary+".bak";
            Check(File.Exists(backup)&&File.ReadAllText(backup).Contains("scale=65"),"replacement retains previous valid settings backup",log);
            File.WriteAllText(primary,"format=2\nscale=150\nx=900\n");
            value=Settings.Load(roundtrip);
            Check(value.Scale==65&&value.Position&&value.X==-70&&value.LoadNotice.Length>0,"truncated primary recovers complete backup",log);
            Check(value.Save(roundtrip)&&File.ReadAllText(backup).Contains("scale=65")&&!File.ReadAllText(backup).Contains("x=900"),"saving recovered settings preserves good backup",log);
            File.WriteAllText(primary,"scale=110\nx=0\ny=0\nsound=not-a-bool\n");
            Check(Settings.Load(roundtrip).Scale==65,"malformed primary value recovers backup",log);

            string half=Case(root,"half-position");
            File.WriteAllText(Path.Combine(half,"settings.ini"),"scale=110\nx=5\n");
            value=Settings.Load(half);
            Check(!value.Position&&value.Scale==85&&value.LoadNotice.Length>0,"incomplete coordinate pair cannot move pet to a fabricated position",log);
            File.WriteAllText(Path.Combine(half,"settings.ini"),"scale=110\ncustomFutureSetting=hello\n");
            value=Settings.Load(half);
            Check(value.Scale==110&&!value.Position&&value.LoadNotice.Length==0,"legacy partial settings allow unknown future fields",log);

            string locked=Case(root,"locked");
            Settings protectedValue=new Settings{Scale=85,X=12,Y=34};
            Check(protectedValue.Save(locked),"locked-save fixture is created",log);
            string lockedFile=Path.Combine(locked,"settings.ini"),before=File.ReadAllText(lockedFile);
            using(FileStream held=new FileStream(lockedFile,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                protectedValue.Scale=110;
                Check(!protectedValue.Save(locked)&&protectedValue.LastSaveError.Length>0,"sharing violation is reported without exiting",log);
                Check(File.ReadAllText(lockedFile)==before,"failed replacement preserves original settings bytes",log);
            }
            Check(Directory.GetFiles(locked,"*.tmp").Length==0,"failed save removes only its temporary settings file",log);
            Check(File.Exists(Path.Combine(locked,"error.log")),"save failure records a diagnostic",log);
            Check(protectedValue.Save(locked)&&protectedValue.LastSaveError.Length==0&&Settings.Load(locked).Scale==110,"saving recovers after the file becomes writable",log);

            string missing=Path.Combine(root,"missing");
            value=Settings.Load(missing);
            Check(value.Scale==85&&!value.Position&&value.LoadNotice.Length==0&&!Directory.Exists(missing),"first run uses defaults without touching profile or disk",log);
            string occupied=Path.Combine(root,"not-a-folder");File.WriteAllText(occupied,"test fixture");
            Check(!value.Save(occupied)&&File.ReadAllText(occupied)=="test fixture","unwritable settings destination is reported without changing existing data",log);
        }
        private static string Case(string root,string name){string folder=Path.Combine(root,name);Directory.CreateDirectory(folder);return folder;}
        private static void Check(bool success,string name,List<string> log){if(!success)throw new Exception("FAIL: "+name);log.Add("PASS: "+name);}
    }
}
