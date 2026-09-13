using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace RiftRevival.Launcher
{
    public sealed class ModSet
    {
        public string Name { get; set; }
        public List<string> Mods { get; set; }
        public bool Enabled { get { return Mods.Contains(RecipePatch.ModId); } }
        public int Mask { get { return ModCatalog.Mask(Mods); } }
    }
    public sealed class SetDocument
    {
        public int Format { get; set; }
        public string Active { get; set; }
        public List<ModSet> Sets { get; set; }
    }
    public sealed class ModSets
    {
        private readonly string path;
        public SetDocument Document { get; private set; }
        public ModSet Active { get { return Document.Sets.Single(s => s.Name == Document.Active); } }
        public ModSets(string root)
        {
            path = SafePaths.Child(root, "mod-sets.json");
            if (File.Exists(path)) {
                if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Mod set settings are too large.");
                Document = new JavaScriptSerializer().Deserialize<SetDocument>(File.ReadAllText(path)); Validate();
            } else Document = new SetDocument { Format = 1, Active = "Rift Revival", Sets = new List<ModSet> {
                new ModSet { Name = "Rift Revival", Mods = new List<string> { RecipePatch.ModId } },
                new ModSet { Name = "Original recipes", Mods = new List<string>() }
            } };
        }
        private void Validate()
        {
            if (Document == null || Document.Format != 1 || Document.Sets == null || Document.Sets.Count < 1 || Document.Sets.Count > 100) throw new InvalidDataException("Unsupported mod set settings.");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModSet set in Document.Sets) {
                if (set == null || String.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 48 || set.Name.Any(Char.IsControl) || !names.Add(set.Name) || set.Mods == null || set.Mods.Count > ModCatalog.All.Length) throw new InvalidDataException("Invalid or unsupported mod set.");
                try { ModCatalog.Mask(set.Mods); } catch(ArgumentException) { throw new InvalidDataException("Invalid or unsupported mod set."); }
            }
            if (!Document.Sets.Any(s => s.Name == Document.Active)) throw new InvalidDataException("The selected mod set is missing.");
        }
        public void Save()
        {
            Validate(); SafePaths.NoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, new JavaScriptSerializer().Serialize(Document), new UTF8Encoding(false)); SafePaths.CommitPreparedFile(temp, path); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Select(string name) { if (!Document.Sets.Any(s => s.Name == name)) throw new InvalidDataException("Unknown mod set."); Document.Active = name; Save(); }
        public void SetEnabled(bool value) { SetEnabled(RecipePatch.ModId,value); }
        public void SetEnabled(string id,bool value) {
            if(!ModCatalog.All.Any(m=>m.Id==id)) throw new InvalidDataException("Unknown mod.");
            List<string> old=Active.Mods;
            Active.Mods=old.Where(m=>m!=id).ToList(); if(value) Active.Mods.Add(id);
            try { Save(); } catch { Active.Mods=old; throw; }
        }
        public void Create(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name.Length > 48 || name.Any(Char.IsControl) || Document.Sets.Count >= 100 || Document.Sets.Any(s => String.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Choose a unique name of 1–48 characters.");
            Document.Sets.Add(new ModSet { Name = name, Mods = new List<string>(Active.Mods) }); Document.Active = name; Save();
        }
        public string ExportActive() { return new JavaScriptSerializer().Serialize(new SetDocument { Format = 1, Active = Active.Name, Sets = new List<ModSet> { Active } }); }
        public void Import(string json)
        {
            if (json == null || json.Length > 65536) throw new InvalidDataException("Mod pack file is too large.");
            SetDocument imported = new JavaScriptSerializer().Deserialize<SetDocument>(json);
            SetDocument before = Document;
            try { Document = imported; Validate(); if (Document.Sets.Count != 1) throw new InvalidDataException("Import one mod set at a time."); }
            finally { Document = before; }
            ModSet incoming = imported.Sets[0];
            if (Document.Sets.Count >= 100) throw new InvalidDataException("This preview supports up to 100 mod sets.");
            if (Document.Sets.Any(s => String.Equals(s.Name, incoming.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("A set with that name already exists. Rename it before importing.");
            Document.Sets.Add(incoming); Document.Active = incoming.Name; Save();
        }
    }
}
