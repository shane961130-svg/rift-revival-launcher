using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using RiftRevival.Launcher;

internal static class LauncherTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        checks++;
        Console.WriteLine("PASS: " + message);
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (InvalidDataException) { rejected = true; } catch (IOException) { rejected = true; } catch (OperationCanceledException) { rejected = true; }
        Check(rejected, message);
    }

    private sealed class CancelProgress : IProgress<WorkProgress>
    {
        private readonly CancellationTokenSource source;
        public CancelProgress(CancellationTokenSource token) { source = token; }
        public void Report(WorkProgress value) { source.Cancel(); }
    }
    private sealed class ConsoleProgress : IProgress<WorkProgress>
    {
        private int last = -1;
        public void Report(WorkProgress value) {
            if (value.Percent / 10 != last) { last = value.Percent / 10; Console.WriteLine(value.Percent + "% " + value.Message); }
        }
    }

    private static string CopySourceFixture(string source, string root)
    {
        string fixture = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(fixture, "Build"));
        Directory.CreateDirectory(Path.Combine(fixture, "Content"));
        Directory.CreateDirectory(Path.Combine(fixture, "framework"));
        foreach (string name in new[] { "IR.exe", "IRGhostClient.exe" })
            File.Copy(Path.Combine(source, @"Build\" + name), Path.Combine(fixture, @"Build\" + name));
        File.WriteAllText(Path.Combine(fixture, @"Content\fixture.txt"), "content fixture");
        File.WriteAllText(Path.Combine(fixture, @"framework\fixture.txt"), "framework fixture");
        return fixture;
    }

    public static int Main(string[] args)
    {
        try {
            if (args.Length != 4) throw new Exception("Expected mode, source folder, workspace target, independent patched reference.");
            string mode = args[0], source = SafePaths.Full(args[1]), root = SafePaths.Full(args[2]), reference = args[3];
            ModLibrary.ValidateSource(source);
            string live = Path.Combine(source, @"Build\IR.exe");
            DateTime liveTime = File.GetLastWriteTimeUtc(live);
            byte[] original = File.ReadAllBytes(live);
            byte[] patched = RecipePatch.Apply(original);
            Check(RecipePatch.Hash(patched) == RecipePatch.InstalledHash, "expected combined recipe/profile hash");
            Check(patched.SequenceEqual(File.ReadAllBytes(reference)), "byte-for-byte agreement with independent PowerShell preparation");
            Check(RecipePatch.Hash(original) == RecipePatch.StockHash, "in-memory source remains stock");
            Reject(delegate { RecipePatch.Apply(new byte[512]); }, "unsupported executable rejected");

            if (mode == "preflight") {
                ModLibrary prepared = new ModLibrary(root);
                ProcessStartInfo plan = prepared.PreparePlay();
                Check(plan.FileName == prepared.Executable && plan.WorkingDirectory == Path.GetDirectoryName(prepared.Executable), "Play targets the verified separate copy and correct working folder");
                Check(plan.Arguments == "-mainmenu" && !plan.UseShellExecute && plan.WindowStyle == ProcessWindowStyle.Normal, "Play requests the normal interactive game menu directly");
                Check(plan.EnvironmentVariables["SteamAppId"] == "363360" && plan.EnvironmentVariables["SteamGameId"] == "363360", "Play passes the installed game's Steam identity");
                Console.WriteLine("PLAY PREFLIGHT PASSED; Steam is open. No game was launched.");
                return 0;
            }

            if (mode == "full-install") {
                ModLibrary full = new ModLibrary(root);
                if (!full.Exists) full.Install(source, new ConsoleProgress(), CancellationToken.None);
                full.Verify(new ConsoleProgress(), CancellationToken.None);
                full.VerifyCritical();
                Check(RecipePatch.FileHash(live) == RecipePatch.StockHash && File.GetLastWriteTimeUtc(live) == liveTime, "Steam executable untouched by full installation");
                Check(RecipePatch.FileHash(Path.Combine(source, @"Build\IRGhostClient.exe")) == RecipePatch.StockHash, "Steam simulation executable untouched");
                Console.WriteLine("FULL INSTALL VERIFIED: " + full.InstalledPath);
                Console.WriteLine("No game executable was run.");
                return 0;
            }
            if (mode != "fixtures") throw new Exception("Unknown test mode.");
            if (Directory.Exists(root)) throw new Exception("Use a fresh fixture directory.");
            Directory.CreateDirectory(root);
            string fixture = CopySourceFixture(source, root);
            string libraryRoot = Path.Combine(root, "library");
            ModLibrary library = new ModLibrary(libraryRoot);
            Check(!SafePaths.Within(libraryRoot + "-other", libraryRoot), "sibling-prefix paths are not descendants");
            Reject(delegate { SafePaths.Child(libraryRoot, @"..\outside.txt"); }, "parent traversal rejected");
            Reject(delegate { SafePaths.Child(libraryRoot, @"C:\outside.txt"); }, "absolute manifest path rejected");
            Reject(delegate { SafePaths.Child(libraryRoot, "file:stream"); }, "alternate file stream rejected");
            Reject(delegate { new ModLibrary(Path.Combine(fixture, "library")).Install(fixture, null, CancellationToken.None); }, "library inside source rejected");
            using (CancellationTokenSource cancelled = new CancellationTokenSource()) {
                cancelled.Cancel();
                Reject(delegate { library.Install(fixture, null, cancelled.Token); }, "pre-cancelled install rejected without publishing");
                Check(!Directory.Exists(libraryRoot), "pre-cancelled install creates no library");
            }
            using (CancellationTokenSource cancelled = new CancellationTokenSource()) {
                Reject(delegate { library.Install(fixture, new CancelProgress(cancelled), cancelled.Token); }, "mid-copy cancellation handled");
                Check(!library.Exists && Directory.GetDirectories(libraryRoot, ".install-*").Length == 0, "cancelled staging copy cleaned; no playable partial installation");
            }
            library.Install(fixture, null, CancellationToken.None);
            library.VerifyCritical();
            library.Verify(null, CancellationToken.None);
            Check(library.Exists, "fixture installation and all-file verification succeed");
            library.SetRecipeEnabled(false);
            Check(!library.RecipeEnabled && RecipePatch.FileHash(library.Executable) == RecipePatch.DisabledHash, "disabled recipe uses original cost and isolated profile");
            Check(RecipePatch.FileHash(Path.Combine(library.InstalledPath, @"Build\IRGhostClient.exe")) == RecipePatch.DisabledHash, "simulation recipe disabled with client");
            library.Verify(null, CancellationToken.None);
            library.SetRecipeEnabled(true);
            Check(library.RecipeEnabled && File.ReadAllBytes(library.Executable).SequenceEqual(patched), "enable restores exact approved recipe build");
            using (FileStream busyExe = new FileStream(Path.Combine(library.InstalledPath, @"Build\IRGhostClient.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(delegate { library.SetRecipeEnabled(false); }, "locked simulation prevents changing recipes");
            Check(library.RecipeEnabled && RecipePatch.FileHash(library.Executable) == RecipePatch.InstalledHash, "failed toggle preserves recipe and manifest");
            string setsRoot = Path.Combine(root, "sets");
            ModSets modSets = new ModSets(setsRoot);
            modSets.SetEnabled(false); modSets.Create("Exploration"); modSets.SetEnabled(true);
            ModSets reloaded = new ModSets(setsRoot);
            Check(reloaded.Active.Name == "Exploration" && reloaded.Active.Enabled, "named mod set and toggle persist after reopening");
            reloaded.Select("Rift Revival"); Check(!reloaded.Active.Enabled, "switching sets restores its own selection");
            reloaded.Select("Exploration"); string exported = reloaded.ExportActive();
            ModSets imported = new ModSets(Path.Combine(root, "imported-sets")); imported.Import(exported);
            Check(imported.Active.Name == "Exploration" && imported.Active.Enabled, "mod set export and import preserve selection");
            Reject(delegate { imported.Import(exported.Replace(RecipePatch.ModId, "untrusted-unknown-mod")); }, "unsupported imported mods rejected");
            Check(imported.Active.Name == "Exploration" && imported.Active.Enabled, "rejected import preserves existing settings");
            foreach(ModDefinition mod in ModCatalog.All.Skip(1)) reloaded.SetEnabled(mod.Id,true);
            Check(reloaded.Active.Mask==255,"all five cartridges, glove battery, and backpack can be selected with the recipe");
            reloaded.SetEnabled("thermal-engineer-v1",false);
            Check(reloaded.Active.Mask==251,"disabling one cartridge preserves all other selections");
            reloaded.SetEnabled(false);
            Check(reloaded.Active.Mask==250,"recipe toggle preserves cartridge, glove, and backpack selections");
            ModSets mixedImport=new ModSets(Path.Combine(root,"mixed-import"));mixedImport.Import(reloaded.ExportActive());
            Check(mixedImport.Active.Mask==250,"mixed cartridge, glove, and backpack set survives export and import");
            library.SetMods(255);library.Verify(null,CancellationToken.None);
            Check(RecipePatch.FileHash(library.Executable)==CartridgePatch.ExpectedHash(255),"all-mods client has reviewed hash");
            Check(RecipePatch.FileHash(Path.Combine(library.InstalledPath,@"Build\IRGhostClient.exe"))==CartridgePatch.ExpectedHash(255),"simulation and client use identical mod selections");
            library.SetMods(2);library.Verify(null,CancellationToken.None);
            Check(RecipePatch.FileHash(library.Executable)==CartridgePatch.ExpectedHash(2) && !library.RecipeEnabled,"independent single cartridge works with original recipe");
            using(FileStream lockedGhost=new FileStream(Path.Combine(library.InstalledPath,@"Build\IRGhostClient.exe"),FileMode.Open,FileAccess.Read,FileShare.Read))
                Reject(delegate{library.SetMods(62);},"locked simulation prevents cartridge changes");
            Check(RecipePatch.FileHash(library.Executable)==CartridgePatch.ExpectedHash(2),"rejected cartridge change preserves prior client");
            // A settings commit failure occurs after executable writes: both must roll back.
            string commitRecord=Path.Combine(library.InstalledPath,"rift-revival-install.json");
            using(FileStream lockedRecord=new FileStream(commitRecord,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
                Reject(delegate{library.SetMods(62);},"locked installation record rejects cartridge commit");
            library.Verify(null,CancellationToken.None);
            Check(RecipePatch.FileHash(library.Executable)==CartridgePatch.ExpectedHash(2),"commit failure rolls back executable contents and length");
            library.SetMods(0);library.Verify(null,CancellationToken.None);
            Check(RecipePatch.FileHash(library.Executable)==RecipePatch.DisabledHash,"all mods off restores exact original recipe and profile build");
            library.SetMods(1);library.Verify(null,CancellationToken.None);
            Check(File.ReadAllBytes(library.Executable).SequenceEqual(patched),"cartridges off restores exact previously tested recipe-only bytes");
            Check(RecipePatch.FileHash(Path.Combine(fixture, @"Build\IR.exe")) == RecipePatch.StockHash, "fixture source preserved");
            DateTime installedTime = File.GetLastWriteTimeUtc(library.Executable);
            Reject(delegate { library.Install(fixture, null, CancellationToken.None); }, "duplicate installation does not overwrite existing copy");
            Check(File.GetLastWriteTimeUtc(library.Executable) == installedTime, "duplicate-install rejection leaves installed file unchanged");
            using (FileStream locked = new FileStream(Path.Combine(libraryRoot, ".operation.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Reject(delegate { library.Verify(null, CancellationToken.None); }, "concurrent operation is rejected");

            byte[] good = File.ReadAllBytes(library.Executable);
            byte[] corrupt = (byte[])good.Clone(); corrupt[0] ^= 1;
            File.WriteAllBytes(library.Executable, corrupt);
            Reject(delegate { library.VerifyCritical(); }, "changed game executable blocks Play preflight");
            File.WriteAllBytes(library.Executable, good);
            string content = Path.Combine(library.InstalledPath, @"Content\fixture.txt");
            File.WriteAllText(content, "changed content");
            Reject(delegate { library.Verify(null, CancellationToken.None); }, "changed content detected by full verification");
            File.WriteAllText(content, "content fixture");

            string manifestPath = Path.Combine(library.InstalledPath, "rift-revival-install.json");
            string originalManifest = File.ReadAllText(manifestPath);
            JavaScriptSerializer json = new JavaScriptSerializer();
            InstallManifest manifest = json.Deserialize<InstallManifest>(originalManifest);
            manifest.Files[0].Path = @"..\outside.txt";
            File.WriteAllText(manifestPath, json.Serialize(manifest));
            Reject(delegate { library.VerifyCritical(); }, "tampered manifest traversal rejected");
            File.WriteAllText(manifestPath, originalManifest);
            library.Verify(null, CancellationToken.None);

            // Only this tiny, locally compiled process fixture is executed, never a game binary.
            string processRoot = Path.Combine(root, "process-fixture");
            Directory.CreateDirectory(processRoot);
            string processSource = Path.Combine(processRoot, "fixture.cs");
            string processExe = Path.Combine(processRoot, "IR.exe");
            File.WriteAllText(processSource, "class Fixture { static void Main() { System.Threading.Thread.Sleep(30000); } }");
            string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
            using (Process compile = Process.Start(new ProcessStartInfo(compiler, "/nologo /target:exe /out:\"" + processExe + "\" \"" + processSource + "\"") { UseShellExecute = false, CreateNoWindow = true })) {
                compile.WaitForExit(); if (compile.ExitCode != 0) throw new Exception("Fixture compiler failed.");
            }
            using (Process process = Process.Start(new ProcessStartInfo(processExe) { UseShellExecute = false, CreateNoWindow = true })) {
                try {
                    Thread.Sleep(200);
                    Reject(delegate { ModLibrary.AssertStopped(processRoot); }, "running exact-copy process is blocked");
                    ModLibrary.AssertStopped(library.InstalledPath);
                    Check(true, "unrelated same-name process does not block a separate copy");
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
            }
            string keep = Path.Combine(root, "keep.txt"); File.WriteAllText(keep, "keep");
            library.Remove();
            Check(!library.Exists && File.ReadAllText(keep) == "keep", "removal stays within its verified managed directory");
            Check(RecipePatch.FileHash(Path.Combine(fixture, @"Build\IR.exe")) == RecipePatch.StockHash, "removal preserves original source");
            library.Install(fixture, null, CancellationToken.None);
            library.Verify(null, CancellationToken.None);
            Check(library.Exists, "reinstallation after removal succeeds");
            Check(RecipePatch.FileHash(live) == RecipePatch.StockHash && File.GetLastWriteTimeUtc(live) == liveTime, "real Steam executable and timestamp unchanged");
            Console.WriteLine("PASS: " + checks + " checks. No game executable was run.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
