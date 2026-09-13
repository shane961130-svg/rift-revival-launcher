using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Threading;

namespace RiftRevival.Launcher
{
    public sealed class LauncherRelease
    {
        public string Version, Notes, DownloadUrl, Digest;
    }
    public static class LauncherUpdates
    {
        // Stable release source for launcher self-updates.
        public const string Repository = "shane961130-svg/rift-revival-launcher";
        public const string InstanceMutex = @"Local\RiftRevivalRecipeLauncher";
        public const string AssetName = "RiftRevivalLauncher.zip";
        public static readonly string[] Files = { "RiftRevivalLauncher.exe", "Mono.Cecil.dll", "README.txt", "THIRD-PARTY-LICENSES.txt" };
        public static string CurrentVersion { get { return typeof(LauncherUpdates).Assembly.GetName().Version.ToString(); } }
        public static Version Normalize(string value) { Version v; if(!Version.TryParse(value,out v)) throw new InvalidDataException("Invalid launcher version."); return new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision)); }
        public static string Hash(string path) { using(var h=SHA256.Create()) using(var s=File.OpenRead(path)) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        private static string Text(Dictionary<string,object> d,string key) { object v; return d.TryGetValue(key,out v)?Convert.ToString(v):""; }
        public static LauncherRelease ParseRelease(string json, string current)
        {
            var d=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
            if(d==null||Text(d,"draft").Equals("True",StringComparison.OrdinalIgnoreCase)||Text(d,"prerelease").Equals("True",StringComparison.OrdinalIgnoreCase)) return null;
            string tag=Text(d,"tag_name");
            if(!tag.StartsWith("launcher-v",StringComparison.Ordinal)) return null;
            Version version; string number=tag.Substring(10);
            if(!System.Version.TryParse(number,out version)||Normalize(number)<=Normalize(current)) return null;
            version=Normalize(number);
            object raw; if(!d.TryGetValue("assets",out raw)) return null;
            foreach(var item in (System.Collections.IEnumerable)raw)
            {
                var a=(Dictionary<string,object>)item;
                if(Text(a,"name")!=AssetName)continue;
                string url=Text(a,"browser_download_url"), digest=Text(a,"digest");
                Uri uri;
                if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/"+Repository+"/releases/download/",StringComparison.Ordinal)) throw new InvalidDataException("Unexpected update download location.");
                if(!digest.StartsWith("sha256:")||digest.Length!=71||!digest.Substring(7).All(Uri.IsHexDigit))throw new InvalidDataException("This release has no usable SHA-256 download digest.");
                return new LauncherRelease { Version=version.ToString(), Notes=Text(d,"body"), DownloadUrl=url, Digest=digest.Substring(7).ToLowerInvariant() };
            }
            return null;
        }
        public static LauncherRelease Check(CancellationToken token)
        {
            if(String.IsNullOrEmpty(Repository))throw new InvalidOperationException("Launcher updates are waiting for the GitHub release repository to be configured.");
            string json=GetText("https://api.github.com/repos/"+Repository+"/releases?per_page=100",token);
            return SelectRelease(json,CurrentVersion);
        }
        public static LauncherRelease SelectRelease(string json,string current)
        {
            var serializer=new JavaScriptSerializer();
            var releases=serializer.DeserializeObject(json) as object[];
            if(releases==null)throw new InvalidDataException("GitHub returned an unexpected release response.");
            LauncherRelease best=null;
            foreach(var item in releases){var candidate=ParseRelease(serializer.Serialize(item),current);if(candidate!=null&&(best==null||Normalize(candidate.Version)>Normalize(best.Version)))best=candidate;}
            return best;
        }
        private static HttpWebRequest Request(string url)
        {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            var r=(HttpWebRequest)WebRequest.Create(url);r.UserAgent="RiftRevivalLauncher/"+CurrentVersion;r.Accept="application/vnd.github+json";r.Timeout=20000;r.ReadWriteTimeout=20000;r.AllowAutoRedirect=false;return r;
        }
        private static void Download(string url,Stream output,long limit,CancellationToken token)
        {
            for(int redirects=0;redirects<=5;redirects++) {
                token.ThrowIfCancellationRequested();
                Uri uri=new Uri(url);
                if(uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||!(uri.Host=="api.github.com"||uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com"))throw new InvalidDataException("Unexpected update redirect.");
                var request=Request(url);
                using(token.Register(request.Abort)) {
                    try { using(var response=(HttpWebResponse)request.GetResponse()) {
                        int status=(int)response.StatusCode;
                        if(status==301||status==302||status==303||status==307||status==308){url=new Uri(uri,response.Headers["Location"]).AbsoluteUri;continue;}
                        if(status!=200)throw new IOException("GitHub returned HTTP "+status+".");
                        if(response.ContentLength>limit)throw new InvalidDataException("Update exceeds the allowed size.");
                        using(var stream=response.GetResponseStream())CopyLimited(stream,output,limit,token);
                        return;
                    }} catch(WebException e) { token.ThrowIfCancellationRequested(); if(e.Response!=null)e.Response.Dispose();throw new IOException("Could not download from GitHub. Check your connection and try again.",e); }
                }
            }
            throw new IOException("Too many update redirects.");
        }
        private static string GetText(string url,CancellationToken token)
        {
            using(var memory=new MemoryStream()){Download(url,memory,4*1024*1024,token);return System.Text.Encoding.UTF8.GetString(memory.ToArray());}
        }
        private static void CopyLimited(Stream source,Stream target,long limit,CancellationToken token)
        {
            byte[] b=new byte[65536];long total=0;int n;while((n=source.Read(b,0,b.Length))>0){token.ThrowIfCancellationRequested();total+=n;if(total>limit)throw new InvalidDataException("Update exceeds the allowed size.");target.Write(b,0,n);}token.ThrowIfCancellationRequested();
        }
        public static string Prepare(LauncherRelease release,string root,CancellationToken token)
        {
            root=Path.GetFullPath(root); CheckDirectory(root);
            string updates=Path.Combine(root,"Updates");Directory.CreateDirectory(updates);CheckDirectory(updates);
            string stage=Path.Combine(updates,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            string zip=Path.Combine(stage,"download.zip");
            try {
            using(var output=File.Create(zip))Download(release.DownloadUrl,output,100*1024*1024,token);
            if(Hash(zip)!=release.Digest)throw new InvalidDataException("The update download failed its integrity check. Nothing was installed.");
            Extract(zip,stage,token);
            var version=FileVersionInfo.GetVersionInfo(Path.Combine(stage,Files[0])).FileVersion;
            if(Normalize(version)!=Normalize(release.Version))throw new InvalidDataException("Downloaded launcher version differs from the release.");
            var hashes=Files.ToDictionary(n=>n,n=>Hash(Path.Combine(stage,n)));
            File.WriteAllText(Path.Combine(stage,"verified.json"),new JavaScriptSerializer().Serialize(hashes));
            return stage;
            } catch { DeleteStage(stage);throw; }
        }
        public static void DeleteStage(string stage)
        {
            CheckDirectory(stage);
            // Only flat, known staging files; never recursively traverse a supplied tree.
            foreach(string n in Files.Concat(new[]{"download.zip","verified.json","UpdateRunner.exe"})) {string p=Path.Combine(stage,n);if(File.Exists(p)){CheckFile(p);File.Delete(p);}}
            if(!Directory.EnumerateFileSystemEntries(stage).Any())Directory.Delete(stage);
        }
        public static void Extract(string zip,string stage,CancellationToken token)
        {
            CheckDirectory(stage);CheckFile(zip);
            using(var archive=ZipFile.OpenRead(zip)){
                if(archive.Entries.Count!=Files.Length||archive.Entries.Select(e=>e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Files.Length||archive.Entries.Any(e=>!Files.Contains(e.FullName,StringComparer.Ordinal)))throw new InvalidDataException("Update contains unexpected files.");
                long total=0;foreach(var entry in archive.Entries){total+=entry.Length;if(total>150*1024*1024)throw new InvalidDataException("Expanded update is too large.");}
                foreach(var entry in archive.Entries)using(var input=entry.Open())using(var output=new FileStream(Path.Combine(stage,entry.FullName),FileMode.CreateNew))CopyLimited(input,output,100*1024*1024,token);
            }
        }
        private static void CheckDirectory(string path)
        {
            var d=new DirectoryInfo(path);while(d!=null){if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Updates are not supported through linked folders.");d=d.Parent;}
        }
        private static void CheckFile(string path) { CheckDirectory(Path.GetDirectoryName(path));if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked update files are not supported."); }
        public static string Apply(string root,string stage,bool awaitStartup=false)
        {
            root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);stage=Path.GetFullPath(stage);
            if(!stage.StartsWith(Path.Combine(root,"Updates")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Update staging folder is outside the launcher.");
            CheckDirectory(root);CheckDirectory(stage);
            CheckFile(Path.Combine(stage,"verified.json"));
            var hashes=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(stage,"verified.json")));
            foreach(string n in Files)CheckFile(Path.Combine(stage,n));
            if(hashes==null||hashes.Count!=Files.Length||Files.Any(n=>!hashes.ContainsKey(n)||Hash(Path.Combine(stage,n))!=hashes[n]))throw new InvalidDataException("Staged update changed after verification.");
            foreach(string n in Files){string target=Path.Combine(root,n);if(File.Exists(target)&&(File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked launcher files cannot be updated.");}
            string backup=Path.Combine(stage,"previous");if(Directory.Exists(backup))throw new IOException("This update was already attempted. Check its result before retrying.");Directory.CreateDirectory(backup);
            var existed=new HashSet<string>();
            foreach(string n in Files){string target=Path.Combine(root,n);if(!File.Exists(target))throw new IOException("Launcher package is incomplete; restore the four launcher files before updating.");using(var locked=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}existed.Add(n);}
            var changed=new List<string>();
            try{
                // Same-volume renames never overwrite an executing image or leave half-written executable bytes.
                File.WriteAllText(Path.Combine(stage,"pending.txt"),root);
                foreach(string n in Files){string target=Path.Combine(root,n);File.Move(target,Path.Combine(backup,n));changed.Add(n);File.Move(Path.Combine(stage,n),target);if(Hash(target)!=hashes[n])throw new IOException("Installed update failed verification.");}
                if(!awaitStartup)File.Delete(Path.Combine(stage,"pending.txt"));
            }catch{
                var errors=new List<Exception>();
                foreach(string n in changed.AsEnumerable().Reverse()){try{string target=Path.Combine(root,n);if(File.Exists(target))File.Delete(target);File.Move(Path.Combine(backup,n),target);}catch(Exception e){errors.Add(e);}}
                if(errors.Count>0)throw new AggregateException("Update rollback could not finish. Keep the Updates folder and restore from its previous folder.",errors);
                File.Delete(Path.Combine(stage,"pending.txt"));throw;
            }
            return backup;
        }
        public static void Recover(string root,string stage)
        {
            root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);stage=Path.GetFullPath(stage);
            if(!stage.StartsWith(Path.Combine(root,"Updates")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Recovery folder is outside the launcher.");
            CheckDirectory(root);CheckDirectory(stage);
            string pending=Path.Combine(stage,"pending.txt");CheckFile(pending);
            if(!String.Equals(File.ReadAllText(pending),root,StringComparison.OrdinalIgnoreCase))throw new IOException("Recovery belongs to another installation.");
            string backup=Path.Combine(stage,"previous");CheckDirectory(backup);
            foreach(string n in Files) {
                string old=Path.Combine(backup,n),target=Path.Combine(root,n);
                if(!File.Exists(old))continue;
                CheckFile(old);
                if(File.Exists(target)){CheckFile(target);using(var locked=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }
            }
            foreach(string n in Files) {
                string old=Path.Combine(backup,n),target=Path.Combine(root,n);
                if(!File.Exists(old))continue;
                if(File.Exists(target))File.Delete(target);File.Move(old,target);
            }
            File.Delete(pending);
        }
        public static void StartHelper(string stage)
        {
            CheckDirectory(stage);
            string helper=Path.Combine(stage,"UpdateRunner.exe");File.Copy(typeof(LauncherUpdates).Assembly.Location,helper,false);
            string root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            using(var process=Process.Start(new ProcessStartInfo(helper,"--apply-update \""+root+"\" \""+stage+"\" "+Process.GetCurrentProcess().Id+" "+Hash(Path.Combine(stage,"download.zip"))){WorkingDirectory=stage,UseShellExecute=false})) {
                string ready=Path.Combine(stage,"helper-ready.txt");
                if(!SpinWait.SpinUntil(delegate{return process.HasExited||File.Exists(ready);},10000)||process.HasExited)throw new IOException("Update helper could not start. The launcher will stay open.");
            }
        }
        public static void RunHelper(string root,string stage,int pid,string digest)
        {
            root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);stage=Path.GetFullPath(stage);
            if(!stage.StartsWith(Path.Combine(root,"Updates")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Update staging folder is outside the launcher.");
            CheckDirectory(root);
            CheckDirectory(stage);
            File.WriteAllText(Path.Combine(stage,"helper-ready.txt"),"ready");
            try{using(var parent=Process.GetProcessById(pid)){if(!parent.WaitForExit(60000))throw new IOException("Close the launcher before updating.");}}catch(ArgumentException){}
            try{using(var mutex=new Mutex(false,InstanceMutex)) {
                bool owned=false;
                try {
                    try{owned=mutex.WaitOne(10000);}catch(AbandonedMutexException){owned=true;}
                    if(!owned)throw new IOException("Another launcher is open. Close it before updating.");
                    // Re-extract the exact verified download, ignoring mutable extracted files and their manifest.
                    string zip=Path.Combine(stage,"download.zip");CheckFile(zip);
                    if(Hash(zip)!=digest)throw new InvalidDataException("The verified download changed before installation.");
                    foreach(string n in Files.Concat(new[]{"verified.json"})){string p=Path.Combine(stage,n);CheckFile(p);File.Delete(p);}
                    Extract(zip,stage,CancellationToken.None);
                    File.WriteAllText(Path.Combine(stage,"verified.json"),new JavaScriptSerializer().Serialize(Files.ToDictionary(n=>n,n=>Hash(Path.Combine(stage,n)))));
                    Apply(root,stage,true);
                } finally{if(owned)mutex.ReleaseMutex();}
            }}
            catch(Exception e){File.WriteAllText(Path.Combine(stage,"result.txt"),"Update failed: "+e.Message);throw;}
            string acknowledgement=Guid.NewGuid().ToString("N");
            try { using(var restarted=Process.Start(new ProcessStartInfo(Path.Combine(root,Files[0]),"--updated \""+stage+"\" "+acknowledgement){WorkingDirectory=root,UseShellExecute=false})) {
                string marker=Path.Combine(stage,"started-"+acknowledgement+".txt");
                bool ready=SpinWait.SpinUntil(delegate{return File.Exists(marker)||restarted.HasExited;},30000)&&File.Exists(marker);
                if(ready){File.Delete(Path.Combine(stage,"pending.txt"));File.WriteAllText(Path.Combine(stage,"result.txt"),"Launcher updated and startup confirmed. Previous files are retained in previous.");return;}
                if(!restarted.HasExited){File.WriteAllText(Path.Combine(stage,"result.txt"),"Startup was not confirmed. Close the launcher and use update recovery; previous files have been retained.");return;}
            }} catch(System.ComponentModel.Win32Exception) { /* Invalid/missing executable: restore below. */ }
            using(var mutex=new Mutex(false,InstanceMutex)) {
                bool owned;try{owned=mutex.WaitOne(10000);}catch(AbandonedMutexException){owned=true;}
                if(!owned)throw new IOException("Startup failed; close the other launcher before recovering this update.");
                try{
                    // Windows can briefly retain the exited image mapping. Recovery is idempotent.
                    for(int attempt=0;;attempt++){try{Recover(root,stage);break;}catch(IOException){if(attempt>=49)throw;Thread.Sleep(100);}}
                    File.WriteAllText(Path.Combine(stage,"result.txt"),"New launcher failed to start. The previous launcher was restored.");
                }catch(Exception e){File.WriteAllText(Path.Combine(stage,"result.txt"),"Startup recovery could not finish: "+e.Message);throw;}finally{mutex.ReleaseMutex();}
            }
            using(var previous=Process.Start(new ProcessStartInfo(Path.Combine(root,Files[0])){WorkingDirectory=root,UseShellExecute=true})){}
        }
    }
}
