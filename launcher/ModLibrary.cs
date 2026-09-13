using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace RiftRevival.Launcher
{
    public sealed class InstalledFile
    {
        public string Path { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
    }

    public sealed class InstallManifest
    {
        public string Format { get; set; }
        public string ModId { get; set; }
        public string Version { get; set; }
        public string Profile { get; set; }
        public string Source { get; set; }
        public string InstalledUtc { get; set; }
        public List<InstalledFile> Files { get; set; }
        public bool? RecipeEnabled { get; set; }
        public int? SelectionMask { get; set; }
    }

    public sealed class WorkProgress
    {
        public int Percent;
        public string Message;
        public WorkProgress(int percent, string message) { Percent = percent; Message = message; }
    }

    public static class SafePaths
    {
        internal static void CommitPreparedFile(string prepared, string target)
        {
            NoLinks(prepared); NoLinks(target);
            if (System.IO.Path.GetDirectoryName(Full(prepared)) != System.IO.Path.GetDirectoryName(Full(target))) throw new IOException("Prepared settings must be in the same folder.");
            if (!File.Exists(target)) { File.Move(prepared, target); return; }
            // File.Replace is denied in some packaged Windows environments. Same-folder
            // renames retain a recoverable previous record until the new one is committed.
            string previous = target + ".previous-" + Guid.NewGuid().ToString("N");
            File.Move(target, previous);
            try { File.Move(prepared, target); }
            catch { if (!File.Exists(target)) File.Move(previous, target); throw; }
            try { File.Delete(previous); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public static string Full(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new IOException("Choose a local folder first.");
            string full = System.IO.Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException("Use a local drive for this preview.");
            string root = System.IO.Path.GetPathRoot(full);
            return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
        }

        public static bool Within(string path, string root)
        {
            return Full(path).StartsWith(Full(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static void NoLinks(string path)
        {
            string current = Full(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked or redirected folders are not supported: " + current);
                current = System.IO.Path.GetDirectoryName(current);
            }
        }

        public static string Child(string root, string relative)
        {
            string full = ChildPath(root, relative);
            NoLinks(full);
            return full;
        }

        // Manifest parsing validates names only. File operations check links immediately
        // before opening each target, avoiding repeated disk walks just to refresh the UI.
        internal static string ChildPath(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("Invalid file path in the installation record.");
            string[] parts = relative.Replace('/', '\\').Split('\\');
            if (parts.Any(p => p == ".." || p == "." || p.Length == 0))
                throw new InvalidDataException("Invalid relative file path.");
            string full = Full(System.IO.Path.Combine(root, relative.Replace('/', '\\')));
            if (!Within(full, root)) throw new InvalidDataException("File path leaves the mod folder.");
            return full;
        }

        public static void Independent(string source, string library)
        {
            if (String.Equals(Full(source), Full(library), StringComparison.OrdinalIgnoreCase) || Within(source, library) || Within(library, source))
                throw new IOException("The mod library must be separate from the Steam game folder.");
            NoLinks(source);
            NoLinks(library);
        }

        public static List<string> Files(string root)
        {
            NoLinks(root);
            List<string> files = new List<string>();
            Stack<string> pending = new Stack<string>();
            pending.Push(Full(root));
            while (pending.Count > 0)
            {
                string folder = pending.Pop();
                NoLinks(folder);
                foreach (string item in Directory.GetFileSystemEntries(folder))
                {
                    NoLinks(item);
                    if (Directory.Exists(item)) pending.Push(item); else files.Add(item);
                }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }
    }

    public sealed class ModLibrary
    {
        private const string Format = "rift-revival-managed-copy-v1";
        private const string ManifestName = "rift-revival-install.json";
        private static readonly string[] RuntimeFolders = { "Build", "Content", "framework" };
        public string Root { get; private set; }
        public string InstalledPath { get { return SafePaths.Child(Root, RecipePatch.ModId); } }
        public bool Exists { get { return Directory.Exists(InstalledPath); } }
        public string Executable { get { return SafePaths.Child(InstalledPath, @"Build\IR.exe"); } }

        public ModLibrary(string root) { Root = SafePaths.Full(root); SafePaths.NoLinks(Root); }

        public static string DetectSteamGame()
        {
            List<string> steamRoots = new List<string>();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key != null && key.GetValue("SteamPath") is string) steamRoots.Add((string)key.GetValue("SteamPath"));
            }
            steamRoots.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            foreach (string steam in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                string vdf = System.IO.Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        steamRoots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (string steam in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string candidate = System.IO.Path.Combine(steam, @"steamapps\common\Interstellar Rift");
                if (File.Exists(System.IO.Path.Combine(candidate, @"Build\IR.exe"))) return SafePaths.Full(candidate);
            }
            return "";
        }

        public static void ValidateSource(string source)
        {
            source = SafePaths.Full(source);
            SafePaths.NoLinks(source);
            foreach (string folder in RuntimeFolders)
                if (!Directory.Exists(SafePaths.Child(source, folder)))
                    throw new IOException("Choose the Interstellar Rift game folder containing Build, Content and framework.");
            foreach (string name in new[] { "IR.exe", "IRGhostClient.exe" })
            {
                string exe = SafePaths.Child(source, @"Build\" + name);
                if (!File.Exists(exe) || RecipePatch.FileHash(exe) != RecipePatch.StockHash)
                    throw new InvalidDataException("The selected game is not the supported original Steam build. Select your unmodified installation; nothing was changed.");
            }
        }

        private FileStream OperationLock()
        {
            SafePaths.NoLinks(Root);
            Directory.CreateDirectory(Root);
            try { return new FileStream(SafePaths.Child(Root, ".operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new IOException("Another launcher operation is using this library. Wait for it to finish."); }
        }

        private static void Report(IProgress<WorkProgress> progress, int percent, string message)
        {
            if (progress != null) progress.Report(new WorkProgress(percent, message));
        }

        public void Install(string source, IProgress<WorkProgress> progress, CancellationToken cancellation)
        {
            source = SafePaths.Full(source);
            SafePaths.Independent(source, Root);
            ValidateSource(source);
            if (Exists) throw new IOException("A mod copy already exists. Verify it, or remove that copy before reinstalling.");
            cancellation.ThrowIfCancellationRequested();
            List<string> files = RuntimeFolders.SelectMany(folder => SafePaths.Files(SafePaths.Child(source, folder))).ToList();
            long total = files.Sum(file => new FileInfo(file).Length);
            long reserve = 512L * 1024 * 1024;
            if (new DriveInfo(System.IO.Path.GetPathRoot(Root)).AvailableFreeSpace < total + reserve)
                throw new IOException("There is not enough free space for a separate game copy and a small working reserve.");

            using (OperationLock())
            {
                if (Exists) throw new IOException("The mod copy was installed by another operation. Refresh the launcher.");
                string stage = SafePaths.Child(Root, ".install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                bool committed = false;
                try
                {
                    File.WriteAllText(SafePaths.Child(stage, ".rift-stage-owner"), Format);
                    InstallManifest manifest = new InstallManifest {
                        Format = Format, ModId = RecipePatch.ModId, Version = RecipePatch.Version,
                        Profile = RecipePatch.Profile, Source = source, InstalledUtc = DateTime.UtcNow.ToString("o"),
                        Files = new List<InstalledFile>()
                    };
                    long copied = 0;
                    foreach (string file in files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string relative = file.Substring(source.TrimEnd('\\').Length + 1);
                        string target = SafePaths.Child(stage, relative);
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        SafePaths.NoLinks(file);
                        long size = new FileInfo(file).Length;
                        string hash = CopyChecked(file, target, cancellation);
                        manifest.Files.Add(new InstalledFile { Path = relative, Size = size, Hash = hash });
                        copied += size;
                        Report(progress, (int)(copied * 85 / Math.Max(1, total)), "Copying and checking game files…");
                    }
                    Report(progress, 87, "Applying the recipe and creating a separate profile…");
                    foreach (string name in new[] { "IR.exe", "IRGhostClient.exe" })
                    {
                        cancellation.ThrowIfCancellationRequested();
                        string relative = @"Build\" + name;
                        string target = SafePaths.Child(stage, relative);
                        byte[] patched = RecipePatch.Apply(File.ReadAllBytes(target));
                        File.WriteAllBytes(target, patched);
                        if (RecipePatch.FileHash(target) != RecipePatch.InstalledHash) throw new InvalidDataException("Patched game copy failed verification.");
                        manifest.Files.Single(f => String.Equals(f.Path, relative, StringComparison.OrdinalIgnoreCase)).Hash = RecipePatch.InstalledHash;
                    }
                    // Detect a Steam update during copying before publishing the installation.
                    ValidateSource(source);
                    Report(progress, 92, "Verifying the completed mod copy…");
                    WriteManifest(stage, manifest);
                    VerifyAt(stage, cancellation, null);
                    cancellation.ThrowIfCancellationRequested();
                    SafePaths.Independent(source, Root);
                    SafePaths.NoLinks(stage);
                    SafePaths.NoLinks(InstalledPath);
                    Directory.Move(stage, InstalledPath);
                    committed = true;
                    Report(progress, 100, "Installed. Keep Steam open, then choose Play.");
                }
                finally
                {
                    if (!committed && Directory.Exists(stage)) DeleteStage(stage);
                }
            }
        }

        private static string CopyChecked(string source, string destination, CancellationToken cancellation)
        {
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] buffer = new byte[1024 * 1024];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, count);
                    sha.TransformBlock(buffer, 0, count, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                output.Flush(true);
                return BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }

        private void DeleteStage(string stage)
        {
            if (!SafePaths.Within(stage, Root) || System.IO.Path.GetDirectoryName(stage) != Root ||
                !System.IO.Path.GetFileName(stage).StartsWith(".install-", StringComparison.Ordinal) ||
                File.ReadAllText(SafePaths.Child(stage, ".rift-stage-owner")) != Format)
                throw new IOException("An incomplete copy was retained because its cleanup path could not be verified.");
            SafePaths.Files(stage); // Verify the complete tree has no links before recursive deletion.
            Directory.Delete(stage, true);
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 32 };
        }

        private static void WriteManifest(string path, InstallManifest manifest)
        {
            using (FileStream stream = new FileStream(SafePaths.Child(path, ManifestName), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(Serializer().Serialize(manifest));
        }

        private static InstallManifest ReadManifest(string path)
        {
            string file = SafePaths.Child(path, ManifestName);
            if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024 * 1024)
                throw new InvalidDataException("This is not a complete launcher-managed installation.");
            InstallManifest manifest = Serializer().Deserialize<InstallManifest>(File.ReadAllText(file));
            if (manifest == null || manifest.Format != Format || manifest.ModId != RecipePatch.ModId ||
                manifest.Version != RecipePatch.Version || manifest.Profile != RecipePatch.Profile || manifest.Files == null || manifest.Files.Count < 2)
                throw new InvalidDataException("The mod installation record is not supported.");
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledFile item in manifest.Files)
            {
                if (item == null) throw new InvalidDataException("Invalid installation file record.");
                SafePaths.ChildPath(path, item.Path);
                string normalized = item.Path.Replace('/', '\\');
                if (!RuntimeFolders.Any(folder => normalized.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase)) ||
                    !unique.Add(normalized) || item.Size < 0 || item.Hash == null || !Regex.IsMatch(item.Hash, "\\A[0-9A-F]{64}\\z"))
                    throw new InvalidDataException("Invalid installation file record.");
            }
            foreach (string name in new[] { @"Build\IR.exe", @"Build\IRGhostClient.exe" })
            {
                InstalledFile exe = manifest.Files.SingleOrDefault(f => String.Equals(f.Path.Replace('/', '\\'), name, StringComparison.OrdinalIgnoreCase));
                if (exe == null || exe.Hash != ExpectedHash(manifest)) throw new InvalidDataException("The installation does not declare the required game files.");
            }
            return manifest;
        }

        public void VerifyCritical()
        {
            InstallManifest manifest = ReadManifest(InstalledPath);
            foreach (string name in new[] { "IR.exe", "IRGhostClient.exe" })
                if (RecipePatch.FileHash(SafePaths.Child(InstalledPath, @"Build\" + name)) != ExpectedHash(manifest))
                    throw new InvalidDataException("A required game file changed. Do not play with this copy; verify or reinstall it.");
        }

        private static int Mask(InstallManifest manifest) { return manifest.SelectionMask ?? (manifest.RecipeEnabled == false ? 0 : 1); }
        private static string ExpectedHash(InstallManifest manifest) {
            int mask=Mask(manifest);string current=CartridgePatch.ExpectedHash(mask);
            InstalledFile exe=manifest.Files.SingleOrDefault(f=>String.Equals(f.Path.Replace('/', '\\'),@"Build\IR.exe",StringComparison.OrdinalIgnoreCase));
            return exe!=null&&CartridgePatch.IsPreviousHash(mask,exe.Hash)?exe.Hash:current;
        }
        public bool RecipeEnabled { get { return (Mask(ReadManifest(InstalledPath)) & 1)!=0; } }

        // Selection is applied only with both executable files exclusively locked. A partial
        // operation fails closed through manifest/hash verification; ordinary errors roll back.
        public void SetRecipeEnabled(bool enabled)
        { int current=Mask(ReadManifest(InstalledPath)); SetMods((current & ~1) | (enabled?1:0)); }
        public void SetMods(int selection)
        {
            ModCatalog.Ids(selection);
            using (OperationLock()) {
                VerifyCritical();
                AssertStopped(InstalledPath);
                InstallManifest manifest = ReadManifest(InstalledPath);
                if (Mask(manifest) == selection && ExpectedHash(manifest)==CartridgePatch.ExpectedHash(selection)) return;
                string record = SafePaths.Child(InstalledPath, ManifestName);
                string oldRecord = File.ReadAllText(record);
                string pending = SafePaths.Child(InstalledPath, ".recipe-record-" + Guid.NewGuid().ToString("N"));
                string[] paths = { Executable, SafePaths.Child(InstalledPath, @"Build\IRGhostClient.exe") };
                FileStream[] streams = new FileStream[2];
                byte[][] original = new byte[2][];
                bool changed = false;
                try {
                    for (int i = 0; i < 2; i++) {
                        streams[i] = new FileStream(paths[i], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                        original[i] = new byte[checked((int)streams[i].Length)];
                        int read = 0, n;
                        while (read < original[i].Length && (n = streams[i].Read(original[i], read, original[i].Length - read)) > 0) read += n;
                        if (read != original[i].Length || RecipePatch.Hash(original[i]) != ExpectedHash(manifest)) throw new IOException("Game files changed during mod selection.");
                    }
                    string baselinePath=SafePaths.Child(Root,"original-profile-baseline.bin");
                    byte[] baseline;
                    if(File.Exists(baselinePath)) baseline=File.ReadAllBytes(baselinePath);
                    else {
                        if(Mask(manifest)>=2) throw new InvalidDataException("The original baseline is missing. Reinstall the managed copy before changing mods.");
                        baseline=RecipePatch.SelectRecipe(original[0],false);
                        using(FileStream saved=new FileStream(baselinePath,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { saved.Write(baseline,0,baseline.Length); saved.Flush(true); }
                    }
                    byte[] output=CartridgePatch.Apply(baseline,selection);
                    byte[][] selected = { output, output };
                    manifest.RecipeEnabled = (selection&1)!=0; manifest.SelectionMask=selection;
                    foreach (InstalledFile file in manifest.Files)
                        if (String.Equals(file.Path, @"Build\IR.exe", StringComparison.OrdinalIgnoreCase) || String.Equals(file.Path, @"Build\IRGhostClient.exe", StringComparison.OrdinalIgnoreCase)) { file.Hash = CartridgePatch.ExpectedHash(selection); file.Size=output.Length; }
                    File.WriteAllText(pending, Serializer().Serialize(manifest), new UTF8Encoding(false));
                    changed = true;
                    for (int i = 0; i < 2; i++) { streams[i].Position = 0; streams[i].Write(selected[i], 0, selected[i].Length); streams[i].SetLength(selected[i].Length); streams[i].Flush(true); }
                    SafePaths.CommitPreparedFile(pending, record);
                } catch {
                    if (changed) {
                        for (int i = 0; i < 2; i++) if (streams[i] != null && original[i] != null) { streams[i].Position = 0; streams[i].Write(original[i], 0, original[i].Length); streams[i].SetLength(original[i].Length); streams[i].Flush(true); }
                        File.WriteAllText(record, oldRecord, new UTF8Encoding(false));
                    }
                    throw;
                } finally {
                    foreach (FileStream stream in streams) if (stream != null) stream.Dispose();
                    if (File.Exists(pending)) File.Delete(pending);
                }
                VerifyCritical();
            }
        }

        public void Verify(IProgress<WorkProgress> progress, CancellationToken cancellation)
        {
            using (OperationLock()) VerifyAt(InstalledPath, cancellation, progress);
        }

        private static void VerifyAt(string path, CancellationToken cancellation, IProgress<WorkProgress> progress)
        {
            InstallManifest manifest = ReadManifest(path);
            int checkedFiles = 0;
            foreach (InstalledFile file in manifest.Files)
            {
                cancellation.ThrowIfCancellationRequested();
                string target = SafePaths.Child(path, file.Path);
                if (!File.Exists(target) || new FileInfo(target).Length != file.Size || RecipePatch.FileHash(target) != file.Hash)
                    throw new InvalidDataException("A copied file is missing or changed: " + file.Path + ". Remove and reinstall the mod copy.");
                checkedFiles++;
                Report(progress, checkedFiles * 100 / manifest.Files.Count, "Checking installed files…");
            }
            Report(progress, 100, "All recorded game files match this installation. Ready to play.");
        }

        public static void AssertStopped(string installPath)
        {
            foreach (string name in new[] { "IR", "IRGhostClient" })
            foreach (Process process in Process.GetProcessesByName(name))
            using (process)
            {
                string running;
                try { if (process.HasExited) continue; running = process.MainModule.FileName; }
                catch (Exception error)
                {
                    if (!(error is System.ComponentModel.Win32Exception) && !(error is InvalidOperationException)) throw;
                    try { if (process.HasExited) continue; } catch (InvalidOperationException) { continue; }
                    throw new IOException("A running game process could not be identified. Close its game window normally before trying again.");
                }
                if (SafePaths.Within(running, installPath))
                    throw new IOException("This mod copy is already running. Close its game window first. Use a separate copy for a local server.");
            }
        }

        public ProcessStartInfo PreparePlay()
        {
            VerifyCritical();
            AssertStopped(InstalledPath);
            Process[] steam = Process.GetProcessesByName("steam");
            bool open = steam.Length > 0;
            foreach (Process process in steam) process.Dispose();
            if (!open) throw new IOException("Open Steam and sign in before choosing Play.");
            string workingDirectory = System.IO.Path.GetDirectoryName(Executable);
            string appIdPath = System.IO.Path.Combine(workingDirectory, "steam_appid.txt");
            string appId = File.Exists(appIdPath) ? File.ReadAllText(appIdPath).Trim() : "";
            ProcessStartInfo start = new ProcessStartInfo {
                FileName = Executable, Arguments = "-mainmenu", WorkingDirectory = workingDirectory,
                UseShellExecute = false, WindowStyle = ProcessWindowStyle.Normal
            };
            if (appId.Length > 0) {
                start.EnvironmentVariables["SteamAppId"] = appId;
                start.EnvironmentVariables["SteamGameId"] = appId;
            }
            return start;
        }

        public Process Launch()
        {
            using (OperationLock()) return Process.Start(PreparePlay());
        }

        public void Remove()
        {
            using (OperationLock())
            {
                ReadManifest(InstalledPath);
                AssertStopped(InstalledPath);
                string target = InstalledPath;
                if (!SafePaths.Within(target, Root) || System.IO.Path.GetDirectoryName(target) != Root || System.IO.Path.GetFileName(target) != RecipePatch.ModId)
                    throw new IOException("The mod removal path could not be verified.");
                SafePaths.Files(target);
                Directory.Delete(target, true);
                // The game saves live in the separate AppData profile, which is never removed here.
            }
        }
    }
}
