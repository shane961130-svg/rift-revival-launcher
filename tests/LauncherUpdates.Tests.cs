using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Web.Script.Serialization;
using RiftRevival.Launcher;

class LauncherUpdateTests
{
    static int checks;
    static string fixtures;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS: "+name);checks++;}
    static void Reject(Action work,string name){try{work();}catch(Exception){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static string Json(string tag,bool preview=false,string digest=null,string url=null){return new JavaScriptSerializer().Serialize(new {tag_name=tag,prerelease=preview,draft=false,body="Notes",assets=new[]{new{name=LauncherUpdates.AssetName,digest=digest??("sha256:"+new string('a',64)),browser_download_url=url??("https://github.com/"+LauncherUpdates.Repository+"/releases/download/"+tag+"/"+LauncherUpdates.AssetName)}}});}
    static string Folder(){string p=Path.Combine(fixtures,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p;}
    static string Stage(string root){string p=Path.Combine(root,"Updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);foreach(string n in LauncherUpdates.Files)File.WriteAllText(Path.Combine(p,n),"new-"+n);File.WriteAllText(Path.Combine(p,"verified.json"),new JavaScriptSerializer().Serialize(LauncherUpdates.Files.ToDictionary(n=>n,n=>LauncherUpdates.Hash(Path.Combine(p,n)))));return p;}
    static string Root(){string p=Folder();foreach(string n in LauncherUpdates.Files)File.WriteAllText(Path.Combine(p,n),"old-"+n);Directory.CreateDirectory(Path.Combine(p,"Library"));File.WriteAllText(Path.Combine(p,"Library","mod-sets.json"),"recipe+solar");File.WriteAllText(Path.Combine(p,"Library","IR.exe"),"game");return p;}
    static bool Old(string root){return LauncherUpdates.Files.All(n=>File.ReadAllText(Path.Combine(root,n))=="old-"+n);}
    static string Archive(string[] names,int size=8){string zip=Path.Combine(Folder(),"fixture.zip");using(var a=ZipFile.Open(zip,ZipArchiveMode.Create)){foreach(string n in names){using(var s=a.CreateEntry(n).Open()){byte[] data=new byte[65536];int remaining=size;while(remaining>0){int count=Math.Min(remaining,data.Length);s.Write(data,0,count);remaining-=count;}}}}return zip;}
    static int Main(string[] args){try{
        fixtures=Path.GetFullPath(args[0]);Directory.CreateDirectory(fixtures);
        Check(LauncherUpdates.Normalize("0.3.0")==LauncherUpdates.Normalize("0.3.0.0"),"three/four part versions compare equally");
        Check(LauncherUpdates.ParseRelease(Json("launcher-v0.3.0"),"0.3.0.0")==null,"same version does not update");
        Check(LauncherUpdates.ParseRelease(Json("launcher-v0.2.9"),"0.3.0")==null,"older version ignored");
        Check(LauncherUpdates.ParseRelease(Json("launcher-v0.4.0",true),"0.3.0")==null,"preview release ignored on stable channel");
        Check(LauncherUpdates.ParseRelease(Json("launcher-v0.4.0-preview"),"0.3.0")==null,"prerelease suffix never stripped into stable version");
        Check(LauncherUpdates.ParseRelease(Json("game-v9.0.0"),"0.3.0")==null,"unrelated tags ignored");
        Check(LauncherUpdates.ParseRelease(Json("launcher-v0.4.0"),"0.3.0").Version=="0.4.0.0","new release selected");
        Check(LauncherUpdates.SelectRelease("[]","0.3.0")==null,"empty release feed handled");
        Check(LauncherUpdates.SelectRelease("["+Json("launcher-v0.5.0")+","+Json("launcher-v0.4.0")+"]","0.3.0").Version=="0.5.0.0","highest compatible release chosen");
        Reject(()=>LauncherUpdates.ParseRelease(Json("launcher-v0.4.0",false,"sha256:bad"),"0.3.0"),"missing usable digest rejected");
        Reject(()=>LauncherUpdates.ParseRelease(Json("launcher-v0.4.0",false,null,"https://example.com/a.zip"),"0.3.0"),"foreign asset URL rejected");
        Reject(()=>LauncherUpdates.SelectRelease("invalid","0.3.0"),"malformed API response rejected");
        string zip=Archive(LauncherUpdates.Files),outDir=Folder();LauncherUpdates.Extract(zip,outDir,CancellationToken.None);Check(Directory.GetFiles(outDir).Length==4,"four-file archive extracted");
        foreach(string bad in new[]{"../escape.txt","Library/IR.exe","/absolute.txt","README.txt:stream"}) {string[] names=(string[])LauncherUpdates.Files.Clone();names[0]=bad;string z=Archive(names);Reject(()=>LauncherUpdates.Extract(z,Folder(),CancellationToken.None),"archive rejects "+bad);}
        string dup=Archive(new[]{"README.txt","README.txt","Mono.Cecil.dll","RiftRevivalLauncher.exe"});Reject(()=>LauncherUpdates.Extract(dup,Folder(),CancellationToken.None),"duplicate archive entries rejected");
        string corrupt=Path.Combine(Folder(),"bad.zip");File.WriteAllText(corrupt,"bad");Reject(()=>LauncherUpdates.Extract(corrupt,Folder(),CancellationToken.None),"corrupt archive rejected");
        string huge=Archive(LauncherUpdates.Files,40*1024*1024);Reject(()=>LauncherUpdates.Extract(huge,Folder(),CancellationToken.None),"oversized expanded archive rejected");
        var cancelled=new CancellationToken(true);Reject(()=>LauncherUpdates.Extract(zip,Folder(),cancelled),"cancelled extraction rejected");
        string root=Root(),stage=Stage(root);LauncherUpdates.Apply(root,stage);Check(LauncherUpdates.Files.All(n=>File.ReadAllText(Path.Combine(root,n))=="new-"+n),"four launcher files replaced");
        Check(File.ReadAllText(Path.Combine(root,"Library","IR.exe"))=="game"&&File.ReadAllText(Path.Combine(root,"Library","mod-sets.json"))=="recipe+solar","Library/game/mod selections preserved");
        Check(LauncherUpdates.Files.All(n=>File.ReadAllText(Path.Combine(stage,"previous",n))=="old-"+n),"all original files retained in backup");
        root=Root();stage=Stage(root);File.WriteAllText(Path.Combine(stage,"README.txt"),"tampered");Reject(()=>LauncherUpdates.Apply(root,stage),"staged tampering rejected");Check(Old(root),"tampering leaves launcher untouched");
        root=Root();stage=Stage(root);using(var locked=new FileStream(Path.Combine(root,"Mono.Cecil.dll"),FileMode.Open,FileAccess.Read,FileShare.None)){Reject(()=>LauncherUpdates.Apply(root,stage),"locked installed file rejected");}Check(Old(root),"locked-file failure preserves all files");
        root=Root();stage=Stage(root);Directory.CreateDirectory(Path.Combine(stage,"previous"));Reject(()=>LauncherUpdates.Apply(root,stage),"used staging directory rejected");Check(Old(root),"used stage leaves originals intact");
        root=Root();stage=Stage(root);using(var locked=new FileStream(Path.Combine(stage,"Mono.Cecil.dll"),FileMode.Open,FileAccess.Read,FileShare.Read)){Reject(()=>LauncherUpdates.Apply(root,stage),"mid-transaction rename failure rejected");}Check(Old(root),"partial replacement rolls back all files");
        root=Root();stage=Stage(root);string backup=Path.Combine(stage,"previous");Directory.CreateDirectory(backup);File.WriteAllText(Path.Combine(stage,"pending.txt"),root);File.Move(Path.Combine(root,LauncherUpdates.Files[0]),Path.Combine(backup,LauncherUpdates.Files[0]));File.Move(Path.Combine(stage,LauncherUpdates.Files[0]),Path.Combine(root,LauncherUpdates.Files[0]));LauncherUpdates.Recover(root,stage);Check(Old(root),"interrupted transaction recovered");
        root=Root();stage=Stage(root);Reject(()=>LauncherUpdates.Apply(root,Folder()),"external staging directory rejected");
        LauncherUpdates.DeleteStage(stage);Check(!Directory.Exists(stage),"cancelled stage cleaned without recursive deletion");
        Console.WriteLine("PASS: "+checks+" updater checks; fixture files only.");return 0;
    }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
