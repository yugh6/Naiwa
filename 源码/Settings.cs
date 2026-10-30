using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Text;

namespace NaiwaDesktop
{
    internal sealed class Settings
    {
        internal int Scale=85,X,Y;
        internal bool Position,Sound,OnTop=true,AutoHide=true,FollowCursor=true,ConfirmFeeding=true;
        internal bool LockPosition,ClickThrough,IdleBreathing;
        internal string LoadNotice="",LastSaveError="";
        internal static string Folder{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NaiwaDesktopPet");}}

        internal static Settings Load(){return Load(Folder);}
        internal static Settings Load(string folder)
        {
            string primary=Path.Combine(folder,"settings.ini"),backup=primary+".bak",error;
            Settings value;
            if(TryRead(primary,out value,out error))return value;
            string primaryError=error;
            if(TryRead(backup,out value,out error))
            {
                value.LoadNotice="设置文件缺失或损坏，已从上一次备份恢复。";
                if(primaryError.Length>0)Log(folder,"读取设置",primaryError);
                return value;
            }
            value=new Settings();
            if(primaryError.Length>0||error.Length>0)
            {
                value.LoadNotice="设置文件无法读取，已使用默认设置。";
                Log(folder,"读取设置",primaryError.Length>0?primaryError:error);
            }
            return value;
        }

        private static bool TryRead(string file,out Settings value,out string error)
        {
            value=null;error="";
            try
            {
                // File.ReadAllLines distinguishes a missing file from a denied read.
                string[] lines=File.ReadAllLines(file);
                if(!TryParse(lines,out value)){error="设置内容不完整或格式无效："+Path.GetFileName(file);return false;}
                return true;
            }
            catch(FileNotFoundException){return false;}
            catch(DirectoryNotFoundException){return false;}
            catch(Exception e){if(!IsFileError(e))throw;error=e.Message;return false;}
        }

        private static bool TryParse(string[] lines,out Settings value)
        {
            value=new Settings();bool haveX=false,haveY=false,versioned=false,complete=false;
            int recognized=0;HashSet<string> keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(string raw in lines)
            {
                string line=raw.Trim();if(line.Length==0||line.StartsWith("#")||line.StartsWith(";"))continue;
                int split=line.IndexOf('=');if(split<1)return false;
                string key=line.Substring(0,split).Trim(),text=line.Substring(split+1).Trim();
                bool known=key=="format"||key=="complete"||key=="scale"||key=="x"||key=="y"||key=="sound"||key=="top"||key=="fullscreen"||key=="gaze"||key=="confirmFeeding"||key=="lockPosition"||key=="clickThrough"||key=="idleBreathing";
                if(!known)continue;
                if(!keys.Add(key))return false;
                recognized++;int number;bool flag;
                if(key=="format"){if(text!="2")return false;versioned=true;}
                else if(key=="complete"){if(text!="2")return false;complete=true;}
                else if(key=="scale"){if(!Int32.TryParse(text,out number))return false;value.Scale=Math.Max(50,Math.Min(150,number));}
                else if(key=="x"){if(!Int32.TryParse(text,out number))return false;value.X=number;haveX=true;}
                else if(key=="y"){if(!Int32.TryParse(text,out number))return false;value.Y=number;haveY=true;}
                else
                {
                    if(!Boolean.TryParse(text,out flag))return false;
                    if(key=="sound")value.Sound=flag;
                    else if(key=="top")value.OnTop=flag;
                    else if(key=="fullscreen")value.AutoHide=flag;
                    else if(key=="gaze")value.FollowCursor=flag;
                    else if(key=="confirmFeeding")value.ConfirmFeeding=flag;
                    else if(key=="lockPosition")value.LockPosition=flag;
                    else if(key=="clickThrough")value.ClickThrough=flag;
                    else if(key=="idleBreathing")value.IdleBreathing=flag;
                }
            }
            if(recognized==0||haveX!=haveY||versioned!=complete)return false;
            // New files always contain every setting and end with a completion marker.
            // Legacy files have neither marker and can omit fields added in newer versions.
            if(versioned&&(keys.Count!=13||lines.Length==0||lines[lines.Length-1].Trim()!="complete=2"))return false;
            value.Position=haveX&&haveY;return true;
        }

        internal bool Save(){return Save(Folder);}
        internal bool Save(string folder)
        {
            string temporary=null;LastSaveError="";
            try
            {
                Directory.CreateDirectory(folder);
                string primary=Path.Combine(folder,"settings.ini"),backup=primary+".bak";
                temporary=primary+"."+Guid.NewGuid().ToString("N")+".tmp";
                using(FileStream stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using(StreamWriter writer=new StreamWriter(stream,new UTF8Encoding(false)))
                {
                    foreach(string line in Serialize())writer.WriteLine(line);
                    writer.Flush();stream.Flush(true);
                }
                if(File.Exists(primary))
                {
                    Settings previous;string ignored;
                    // A recovered configuration must not replace its good backup with the
                    // corrupt primary. File.Replace keeps the old primary intact on failure.
                    bool previousValid=TryRead(primary,out previous,out ignored);
                    File.Replace(temporary,primary,previousValid?backup:null,true);
                }
                else File.Move(temporary,primary);
                temporary=null;return true;
            }
            catch(Exception e)
            {
                if(!IsFileError(e))throw;
                LastSaveError="设置未能保存，下次启动可能恢复为之前的设置。\n"+e.Message;
                Log(folder,"保存设置",e.Message);return false;
            }
            finally
            {
                if(temporary!=null)
                {
                    try{File.Delete(temporary);}
                    catch(Exception e){if(!IsFileError(e))throw;}
                }
            }
        }

        private string[] Serialize()
        {
            return new string[]{"format=2","scale="+Scale,"x="+X,"y="+Y,"sound="+Sound,"top="+OnTop,"fullscreen="+AutoHide,"gaze="+FollowCursor,"confirmFeeding="+ConfirmFeeding,"lockPosition="+LockPosition,"clickThrough="+ClickThrough,"idleBreathing="+IdleBreathing,"complete=2"};
        }
        private static bool IsFileError(Exception e){return e is IOException||e is UnauthorizedAccessException||e is SecurityException||e is ArgumentException||e is NotSupportedException;}
        private static void Log(string folder,string action,string error)
        {
            try{Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"error.log"),DateTime.Now.ToString("s")+" "+action+"："+error+Environment.NewLine,Encoding.UTF8);}
            catch(Exception e){if(!IsFileError(e))throw;}
        }
    }
}
