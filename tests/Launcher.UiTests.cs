using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using RiftRevival.Launcher;

class LauncherUiTests
{
    static int checks;
    static void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; Console.WriteLine("PASS: " + text); }
    static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner); }
    static void Call(object owner, string method, params object[] args) { owner.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner, args); Application.DoEvents(); }
    [STAThread] static int Main(string[] args)
    {
        try {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            typeof(MainForm).Assembly.GetType("RiftRevival.Launcher.Art").GetMethod("InitializeFont", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            string root = Path.Combine(args[0], "ui-fixture-" + Guid.NewGuid().ToString("N"));
            using (MainForm form = new MainForm(root)) {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-32000, -32000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                DesignButton toggle = Field<DesignButton>(form, "toggle"); TextBox search = Field<TextBox>(form, "search");
                List<ModRow> initialRows=Field<List<ModRow>>(form,"modRows");
                Check(initialRows.Where(r=>r.Visible).Select(r=>r.Top).Distinct().Count()==4,"initial four rows occupy distinct positions");
                Check(toggle.Visible && toggle.Checked, "mod row begins enabled"); toggle.PerformClick(); Application.DoEvents();
                Check(!toggle.Checked && !new ModSets(root).Active.Enabled, "clicking switch persists disabled selection");
                Field<DesignButton>(form, "enabledFilter").PerformClick(); Application.DoEvents(); Check(!toggle.Visible, "Enabled filter hides disabled recipe");
                Field<DesignButton>(form, "disabledFilter").PerformClick(); Application.DoEvents(); Check(toggle.Visible, "Disabled filter shows disabled recipe");
                Field<DesignButton>(form, "all").PerformClick(); search.Text = "absentmod"; Application.DoEvents(); Check(!toggle.Visible, "search hides nonmatching mod");
                search.Text = "crystal"; Application.DoEvents(); Check(toggle.Visible, "search finds recipe by name");
                Field<DesignButton>(form, "packsNav").PerformClick(); Application.DoEvents(); Check(Field<ListBox>(form, "packList").Visible && !toggle.Visible, "Mod Packs navigation reveals saved sets");
                Field<DesignButton>(form, "settingsNav").PerformClick(); Application.DoEvents(); Check(Field<TextBox>(form, "source").Visible && !Field<ListBox>(form, "packList").Visible, "Settings reveals installation controls");
                Check(Field<DesignButton>(form,"checkUpdates").Visible&&Field<DesignButton>(form,"checkUpdates").Enabled,"launcher update action is visible and available in Settings");
                form.GetType().GetField("busy",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,true);Call(form,"RefreshState");
                Check(!Field<DesignButton>(form,"checkUpdates").Enabled,"update action disabled during another operation");
                form.GetType().GetField("busy",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,false);Call(form,"RefreshState");
                Field<DesignButton>(form, "modsNav").PerformClick(); search.Text = ""; Application.DoEvents();
                List<ModRow> rows=Field<List<ModRow>>(form,"modRows");
                Check(rows.Count==8,"recipe, five cartridge rows, glove battery, and backpack row exist");
                search.Text="solar";Application.DoEvents();
                ModRow solar=rows.Single(r=>r.Mod.Id=="solar-explorer-v1");
                Check(solar.Visible && !toggle.Visible,"search isolates a cartridge row");
                solar.Toggle.PerformClick();Application.DoEvents();
                Check(new ModSets(root).Active.Mask==2,"cartridge switch enables only its own mod");
                search.Text="";Application.DoEvents();
                ModScroll scroll=Field<ModScroll>(form,"modScroll");scroll.Value=scroll.Maximum;Application.DoEvents();
                ModRow hauler=rows.Single(r=>r.Mod.Id=="independent-hauler-v1");
                Check(hauler.Visible,"scrolling reaches the final cartridge");hauler.Toggle.PerformClick();Application.DoEvents();
                Check(new ModSets(root).Active.Mask==34,"second cartridge selection preserves the first");
                ModRow glove=rows.Single(r=>r.Mod.Id=="power-glove-battery-v1");
                Check(glove.Visible,"scrolling reaches the power glove battery mod");glove.Toggle.PerformClick();Application.DoEvents();
                Check(new ModSets(root).Active.Mask==98,"glove selection preserves both cartridges");
                ModRow backpack=rows.Single(r=>r.Mod.Id=="backpack-storage-v1");
                Check(backpack.Visible,"scrolling reaches the backpack storage mod");backpack.Toggle.PerformClick();Application.DoEvents();
                Check(new ModSets(root).Active.Mask==226,"backpack selection preserves glove and cartridge selections");
                scroll.Value=0;Application.DoEvents();
                foreach (Size size in new[] { new Size(1586,992), new Size(1280,800), new Size(1110,695) }) {
                    form.ClientSize = size; Call(form, "Arrange"); Application.DoEvents();
                    List<string> outside = new List<string>();
                    foreach (Control c in form.Controls) if (c.Visible && (c.Left < 0 || c.Top < 0 || c.Right > form.ClientSize.Width + 1 || c.Bottom > form.ClientSize.Height + 1))
                        outside.Add(c.GetType().Name + " '" + c.Text + "' " + c.Bounds + " within " + form.ClientSize);
                    Check(outside.Count == 0, "visible controls fit " + size.Width + "x" + size.Height + (outside.Count == 0 ? "" : ": " + String.Join("; ", outside)));
                    Call(form,"SetPage","settings");
                    DesignButton updates=Field<DesignButton>(form,"checkUpdates"),install=Field<DesignButton>(form,"install");
                    Check(updates.Visible&&updates.Right<=form.ClientSize.Width&&!updates.Bounds.IntersectsWith(install.Bounds),"Settings update button fits without installation overlap at "+size.Width);
                    Call(form,"SetPage","mods");
                }
                form.Close();
            }
            Console.WriteLine("PASS: " + checks + " native-control checks. No game launched; fixture settings only."); return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
