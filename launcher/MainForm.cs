using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RiftRevival.Launcher
{
    internal static class Art
    {
        internal static readonly Color Bg = Color.FromArgb(11, 23, 32), Line = Color.FromArgb(37, 61, 75), Text = Color.FromArgb(241, 244, 247), Muted = Color.FromArgb(177, 200, 215), Teal = Color.FromArgb(49, 193, 187);
        private static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        private static readonly PrivateFontCollection icons = new PrivateFontCollection();
        private static IntPtr fontMemory;
        internal static Image Image(string name)
        {
            if (!cache.ContainsKey(name)) using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Art." + name)) {
                if (stream == null) throw new IOException("A launcher artwork resource is missing: " + name);
                using (Image loaded = System.Drawing.Image.FromStream(stream)) cache[name] = new Bitmap(loaded);
            }
            return cache[name];
        }
        internal static void InitializeFont()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Art.icons.ttf")) {
                byte[] bytes = new byte[stream.Length]; stream.Read(bytes, 0, bytes.Length); fontMemory = Marshal.AllocCoTaskMem(bytes.Length); Marshal.Copy(bytes, 0, fontMemory, bytes.Length); icons.AddMemoryFont(fontMemory, bytes.Length);
            }
        }
        internal static Font Font(float px, bool bold) { return new Font("Segoe UI", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
        internal static void Label(Graphics g, string text, float x, float y, float width, float height, float size, Color color, bool bold = false, StringAlignment align = StringAlignment.Near)
        {
            using (Font font = Font(size, bold)) using (Brush brush = new SolidBrush(color)) using (StringFormat sf = new StringFormat { Alignment = align, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = height <= 60 ? StringFormatFlags.NoWrap : 0 }) g.DrawString(text, font, brush, new RectangleF(x, y, width, height), sf);
        }
        internal static void Icon(Graphics g, int glyph, RectangleF bounds, Color color)
        {
            using (Font font = new Font(icons.Families[0], bounds.Height * .8f, FontStyle.Regular, GraphicsUnit.Pixel)) using (Brush brush = new SolidBrush(color)) using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) g.DrawString(Char.ConvertFromUtf32(glyph), font, brush, bounds, sf);
        }
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height)); GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static void Box(Graphics g, RectangleF r, Color fill, Color border, float radius = 6)
        {
            using (GraphicsPath p = Round(r, radius)) { using (Brush b = new SolidBrush(fill)) g.FillPath(b, p); using (Pen pen = new Pen(border)) g.DrawPath(pen, p); }
        }
        internal static void Picture(Graphics g, string name, RectangleF box)
        {
            Image image = Image(name); g.DrawImage(image, box);
        }
        internal static void ModEmblem(Graphics g, RectangleF destination)
        {
            Box(g, destination, Color.FromArgb(19, 37, 48), Line, 5);
            float unit = Math.Min(destination.Width, destination.Height) / 10f;
            RectangleF core = new RectangleF(destination.X + destination.Width / 2f - unit * 2,
                destination.Y + destination.Height / 2f - unit * 2, unit * 4, unit * 4);
            Box(g, core, Color.FromArgb(23, 57, 65), Teal, unit);
            using (Pen pen = new Pen(Teal, Math.Max(1, unit / 3f)))
            {
                g.DrawLine(pen, destination.X + unit * 2, destination.Y + unit * 2, core.Left, core.Top);
                g.DrawLine(pen, destination.Right - unit * 2, destination.Y + unit * 2, core.Right, core.Top);
                g.DrawLine(pen, destination.X + unit * 2, destination.Bottom - unit * 2, core.Left, core.Bottom);
                g.DrawLine(pen, destination.Right - unit * 2, destination.Bottom - unit * 2, core.Right, core.Bottom);
            }
        }
    }

    public sealed class DesignButton : Button
    {
        public bool Primary, Selected, Borderless, Switch, Checked, Navigation, Selector;
        public int Glyph;
        private bool hover;
        public DesignButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; BackColor = Art.Bg; ForeColor = Art.Text; TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            float scale = Height / (Switch ? 40f : (float)(Tag is float ? (float)Tag : 46f));
            Color color = Enabled ? Art.Text : Color.FromArgb(109, 132, 146);
            Color fill = Primary || Selected ? Color.FromArgb(37, 143, 145) : Art.Bg;
            if (hover && Enabled) fill = Primary || Selected ? Color.FromArgb(44, 164, 161) : Color.FromArgb(24, 47, 61);
            g.Clear(BackColor);
            if (Switch) {
                float h = Height * .78f, w = h * 1.72f;
                Art.Box(g, new RectangleF(1, (Height - h) / 2, w, h), Checked ? Color.FromArgb(36, 158, 158) : Color.FromArgb(59, 78, 92), Checked ? Art.Teal : Color.FromArgb(104, 123, 137), h / 2);
                using (Brush b = new SolidBrush(Enabled ? Color.White : Color.FromArgb(176, 187, 197))) g.FillEllipse(b, Checked ? w - h + 6 : 5, (Height - h) / 2 + 4, h - 8, h - 8);
                Art.Label(g, Checked ? "On" : "Off", w + 12, 0, Width - w - 12, Height, 17 * scale, color); return;
            }
            if (!Borderless) Art.Box(g, new RectangleF(.5f, .5f, Width - 1, Height - 1), fill, Primary || Selected ? Art.Teal : Art.Line, 5 * scale);
            else if (Selected || hover) using (Brush b = new SolidBrush(Selected ? Color.FromArgb(26, 53, 62) : fill)) g.FillRectangle(b, ClientRectangle);
            if (Navigation) {
                if (Selected) using (Brush marker = new SolidBrush(Art.Teal)) g.FillRectangle(marker, 0, 0, 8 * scale, Height);
                Art.Icon(g, Glyph, new RectangleF(34 * scale, (Height - 36 * scale) / 2, 36 * scale, 36 * scale), Selected ? Color.FromArgb(137, 225, 224) : Art.Muted);
                Art.Label(g, Text, 94 * scale, 0, Width - 98 * scale, Height, 24 * scale, Selected ? Art.Text : Art.Muted);
                return;
            }
            if (Selector) {
                Art.Label(g, Text, 14 * scale, 0, Width - 53 * scale, Height, 20 * scale, color);
                Art.Icon(g, 0xf282, new RectangleF(Width - 30 * scale, (Height - 17 * scale) / 2, 17 * scale, 17 * scale), Art.Muted); return;
            }
            float iconWidth = Glyph != 0 ? (Borderless ? 18 : 28) * scale : 0;
            float textWidth;
            using (Font f = Art.Font((Primary ? 28 : 18) * scale, Primary)) textWidth = g.MeasureString(Text, f).Width;
            float start = Math.Max(8 * scale, (Width - textWidth - iconWidth - (Glyph == 0 ? 0 : 12 * scale)) / 2);
            if (Glyph != 0) Art.Icon(g, Glyph, new RectangleF(start, (Height - iconWidth) / 2, iconWidth, iconWidth), color);
            Art.Label(g, Text, start + iconWidth + (Glyph == 0 ? 0 : 12 * scale), 0, Width - start - iconWidth - 4, Height, (Primary ? 28 : 18) * scale, color, Primary);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }

    public sealed class ModScroll : Control
    {
        private int value,maximum;
        public event EventHandler ValueChanged;
        public int Maximum {get{return maximum;}set{maximum=Math.Max(0,value);Value=Math.Min(this.value,maximum);Invalidate();}}
        public int Value {get{return value;}set{int next=Math.Max(0,Math.Min(maximum,value));if(next==this.value)return;this.value=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
        public ModScroll(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Art.Bg;TabStop=true;AccessibleRole=AccessibleRole.ScrollBar;AccessibleName="Scroll mod list";Cursor=Cursors.Hand;}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);float h=Height*4f/(Maximum+4);Art.Box(e.Graphics,new RectangleF(2,1,Width-4,Height-2),Art.Bg,Art.Line,3);Art.Box(e.Graphics,new RectangleF(2,1+(Maximum==0?0:(Height-h-2)*Value/Maximum),Width-4,h),Color.FromArgb(52,92,105),Art.Line,3);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();Capture=true;SetFromMouse(e.Y);}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture)SetFromMouse(e.Y);}
        protected override void OnMouseUp(MouseEventArgs e){Capture=false;base.OnMouseUp(e);}
        private void SetFromMouse(int y){Value=(int)Math.Round(Maximum*Math.Max(0,Math.Min(1,y/(double)Math.Max(1,Height))));}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Down){Value++;e.Handled=true;}else if(e.KeyCode==Keys.Up){Value--;e.Handled=true;}base.OnKeyDown(e);}
    }

    public sealed class ModRow : Control
    {
        public readonly ModDefinition Mod;
        public readonly DesignButton Toggle;
        public bool Selected, Installed;
        public ModRow(ModDefinition mod,Action select,Action change)
        {
            Mod=mod; BackColor=Art.Bg; Cursor=Cursors.Hand; TabStop=true; AccessibleName=mod.Name;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            Toggle=new DesignButton { Switch=true, AccessibleName="Enable "+mod.Name+" on next launch", Text="Enable "+mod.Name };
            Toggle.Click+=delegate {change();}; Controls.Add(Toggle); Click+=delegate {select();};
            KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter || e.KeyCode==Keys.Space){select();e.Handled=true;}};
            Resize+=delegate{float s=Width/802f;Toggle.Bounds=new Rectangle((int)(674*s),(int)(33*s),(int)(104*s),(int)(40*s));};
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float s=Width/802f; Graphics g=e.Graphics;g.ScaleTransform(s,s);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=TextRenderingHint.ClearTypeGridFit;
            Color fill=Selected?Color.FromArgb(18,48,56):Color.FromArgb(13,29,40);
            Art.Box(g,new RectangleF(1,1,800,102),fill,Selected?Art.Teal:Art.Line);
            if(Mod.Bit==1) Art.ModEmblem(g,new RectangleF(14,7,112,90));
            else {
                Art.Box(g,new RectangleF(14,8,112,88),Color.FromArgb(19,37,48),Art.Line);
                Art.Box(g,new RectangleF(46,23,47,54),Color.FromArgb(23,57,65),Art.Teal,4);
                using(Pen p=new Pen(Art.Teal,2)){g.DrawRectangle(p,55,32,28,22);for(int n=0;n<4;n++)g.DrawLine(p,56+n*8,65,56+n*8,73);}
            }
            Art.Label(g,Mod.Name+(Mod.Bit==1?" — Recipe Mod":""),149,8,417,31,22,Art.Text,true);
            Art.Label(g,Mod.Summary,149,40,480,25,17,Art.Muted);
            Art.Label(g,Mod.Bit==1?(Installed?"Installed":"Included"):"Cartridge preview · Admin creation",149,69,420,25,15,Art.Teal);
            Art.Label(g,"v"+RecipePatch.Version,572,32,83,38,17,Art.Muted);
            Toggle.BackColor=fill;
            if(Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g,new Rectangle(5,5,790,94));
        }
    }

    public sealed class ProgressSink : IProgress<WorkProgress>
    {
        private WorkProgress last;
        public void Report(WorkProgress value) { Interlocked.Exchange(ref last, value); }
        public WorkProgress Take() { return Interlocked.Exchange(ref last, null); }
    }

    public sealed class MainForm : Form
    {
        private readonly ModLibrary library;
        private readonly ModSets sets;
        private readonly ProgressSink progress = new ProgressSink();
        private readonly System.Windows.Forms.Timer ticker = new System.Windows.Forms.Timer();
        private readonly Dictionary<Control, RectangleF> positions = new Dictionary<Control, RectangleF>();
        private readonly List<Control> libraryControls = new List<Control>(), packControls = new List<Control>(), settingControls = new List<Control>();
        private readonly ComboBox setPicker = new ComboBox();
        private readonly TextBox search = new TextBox(), source = new TextBox();
        private readonly ListBox packList = new ListBox();
        private readonly List<ModRow> modRows=new List<ModRow>();
        private readonly ModScroll modScroll=new ModScroll();
        private ModDefinition selectedMod=ModCatalog.All[0];
        private readonly ProgressBar bar = new ProgressBar();
        private DesignButton play, verify, toggle, install, remove, cancel, modsNav, packsNav, settingsNav, all, enabledFilter, disabledFilter, setSelector, browse, copyCartridgeCommand, checkUpdates;
        private string page = "mods", filter = "All", message = "", error = "";
        private bool valid, busy, loadingSets, closeWhenFinished;
        private CancellationTokenSource cancellation;
        private float scale = 1;

        public MainForm(string libraryRoot)
        {
            library = new ModLibrary(libraryRoot); sets = new ModSets(libraryRoot);
            Text = "Rift Revival — Mod Launcher"; FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None; BackColor = Art.Bg; ForeColor = Art.Text; DoubleBuffered = true;
            ClientSize = new Size(1586, 992); MinimumSize = new Size(1110, 695); StartPosition = FormStartPosition.CenterScreen;
            Size available = Screen.PrimaryScreen.WorkingArea.Size;
            if (available.Width < Width + 30 || available.Height < Height + 30) { float fit = Math.Min((available.Width - 40) / 1586f, (available.Height - 40) / 992f); Size = new Size((int)(1586 * fit), (int)(992 * fit)); }
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Art.launcher.ico")) if (stream != null) Icon = new Icon(stream);

            AddButton("", 1418, 1, 54, 40, delegate { WindowState = FormWindowState.Minimized; }, 0xf2ea, false, true);
            AddButton("", 1472, 1, 54, 40, delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }, 0xf584, false, true);
            AddButton("", 1526, 1, 59, 40, delegate { Close(); }, 0xf659, false, true);
            modsNav = AddButton("My Mods", 1, 261, 252, 80, delegate { SetPage("mods"); }, 0xf685, false, true);
            packsNav = AddButton("Mod Packs", 1, 342, 252, 80, delegate { SetPage("packs"); }, 0xf45b, false, true);
            settingsNav = AddButton("Settings", 1, 422, 252, 80, delegate { SetPage("settings"); }, 0xf3e5, false, true);
            modsNav.Navigation = packsNav.Navigation = settingsNav.Navigation = true;

            setPicker.DropDownStyle = ComboBoxStyle.DropDownList; setPicker.FlatStyle = FlatStyle.Flat; setPicker.BackColor = Art.Bg; setPicker.ForeColor = Art.Text; setPicker.DrawMode = DrawMode.OwnerDrawFixed;
            setPicker.DrawItem += delegate(object sender, DrawItemEventArgs e) { if (e.Index < 0) return; e.DrawBackground(); Art.Label(e.Graphics, setPicker.Items[e.Index].ToString(), e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 15, e.Bounds.Height, 18 * scale, Art.Text); };
            Place(setPicker, 427, 268, 391, 37);
            setPicker.Visible = false;
            setSelector = AddButton("Rift Revival", 427, 262, 391, 45, delegate {
                ContextMenuStrip menu = new ContextMenuStrip { BackColor = Art.Bg, ForeColor = Art.Text, Font = Art.Font(20 * scale, false), ShowImageMargin = false };
                foreach (ModSet entry in sets.Document.Sets) { string name = entry.Name; menu.Items.Add(name, null, delegate { Safe(delegate { sets.Select(name); PopulateSets(); }); }); }
                menu.Closed += delegate { menu.Dispose(); }; menu.Show(setSelector, new Point(0, setSelector.Height));
            }); setSelector.Selector = true; setSelector.AccessibleRole = AccessibleRole.ComboBox;
            setPicker.SelectedIndexChanged += delegate { if (!loadingSets && setPicker.SelectedItem != null) Safe(delegate { sets.Select(setPicker.SelectedItem.ToString()); SyncSelection(); }); };
            AddButton("Save set", 831, 262, 146, 45, delegate { Safe(delegate { sets.Save(); Notify("Mod set saved."); }); }, 0xf7d8);
            AddButton("Create set", 990, 262, 164, 45, CreateSet, 0xf4fe);
            search.BorderStyle = BorderStyle.None; search.BackColor = Art.Bg; search.ForeColor = Art.Muted; Place(search, 325, 354, 407, 26); libraryControls.Add(search);
            SendMessage(search.Handle, 0x1501, new IntPtr(1), "Search mods");
            search.TextChanged += delegate { modScroll.Value=0; SyncVisibility(); Invalidate(); };
            all = AddButton("All", 771, 344, 82, 39, delegate { SetFilter("All"); });
            enabledFilter = AddButton("Enabled", 854, 344, 108, 39, delegate { SetFilter("Enabled"); });
            disabledFilter = AddButton("Disabled", 963, 344, 102, 39, delegate { SetFilter("Disabled"); });
            libraryControls.AddRange(new Control[] { all, enabledFilter, disabledFilter });
            foreach(ModDefinition entry in ModCatalog.All) {
                ModDefinition mod=entry;
                ModRow row=new ModRow(mod,delegate {selectedMod=mod;SyncSelection();},delegate {Safe(delegate {sets.SetEnabled(mod.Id,!sets.Active.Mods.Contains(mod.Id));selectedMod=mod;SyncSelection();});});
                modRows.Add(row);Place(row,268,404,802,104);libraryControls.Add(row);
                row.MouseWheel+=delegate(object sender,MouseEventArgs e){ScrollMods(e.Delta);};
            }
            toggle=modRows[0].Toggle;
            copyCartridgeCommand=AddButton("Copy admin command",1114,692,270,40,delegate{Safe(delegate{Clipboard.SetText("givecartridgeInv \"Your Player Name\" "+selectedMod.Selector+" 0");Notify("Command copied. Replace Your Player Name before using it as an admin.");});},0xf290);
            Place(modScroll,1073,404,13,440);
            modScroll.ValueChanged+=delegate{SyncVisibility();Arrange();};
            MouseWheel+=delegate(object sender,MouseEventArgs e){if(page=="mods") ScrollMods(e.Delta);};
            verify = AddButton("Verify files", 932, 900, 190, 57, async delegate { await RunWork(delegate(CancellationToken t) { library.Verify(progress, t); }, "All installed files verified."); }, 0xf360);
            play = AddButton("PLAY", 1137, 895, 259, 68, async delegate { await Play(); }, 0xf4f4, true);
            cancel = AddButton("Cancel", 700, 908, 125, 45, delegate { if (cancellation != null) cancellation.Cancel(); }); cancel.Visible = false;
            bar.Style = ProgressBarStyle.Continuous; Place(bar, 282, 979, 1281, 5); bar.Visible = false;

            packList.BorderStyle = BorderStyle.None; packList.BackColor = Art.Bg; packList.ForeColor = Art.Text; Place(packList, 302, 427, 600, 322); packControls.Add(packList);
            packControls.Add(AddButton("Use selected set", 302, 778, 220, 50, delegate { if (packList.SelectedItem != null) Safe(delegate { sets.Select(packList.SelectedItem.ToString()); PopulateSets(); SetPage("mods"); }); }, 0xf26a));
            packControls.Add(AddButton("Export set", 540, 778, 170, 50, ExportSet, 0xf358));
            packControls.Add(AddButton("Import set", 728, 778, 170, 50, ImportSet, 0xf356));
            source.BorderStyle = BorderStyle.FixedSingle; source.BackColor = Art.Bg; source.ForeColor = Art.Text; Place(source, 305, 445, 995, 37); settingControls.Add(source);
            try { source.Text = ModLibrary.DetectSteamGame(); } catch { source.Text = ""; }
            browse = AddButton("Browse", 1320, 442, 190, 45, delegate { using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "Select the original Interstellar Rift game folder", ShowNewFolderButton = false }) { if (Directory.Exists(source.Text)) dialog.SelectedPath = source.Text; if (dialog.ShowDialog(this) == DialogResult.OK) source.Text = dialog.SelectedPath; } }, 0xf3d8); settingControls.Add(browse);
            install = AddButton("Install mod", 305, 615, 205, 54, async delegate { string selected = source.Text; await RunWork(delegate(CancellationToken t) { library.Install(selected, progress, t); }, "Game copy installed. Choose Play to launch."); }, 0xf30a); settingControls.Add(install);
            remove = AddButton("Remove copy", 532, 615, 205, 54, async delegate {
                if (MessageBox.Show(this, "Remove only the launcher's copied game files? Your Steam installation and saved worlds will be kept.", "Remove game copy", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) await RunWork(delegate(CancellationToken t) { library.Remove(); }, "Game copy removed. Saved worlds and Steam files were kept.", false);
            }, 0xf5de); settingControls.Add(remove);
            checkUpdates=AddButton("Check launcher updates",1000,615,510,54,async delegate {await UpdateLauncher();},0xf130);settingControls.Add(checkUpdates);
            PopulateSets(); RefreshState(); SetPage("mods");
            ticker.Interval = 150; ticker.Tick += delegate { WorkProgress p = progress.Take(); if (busy && p != null) { bar.Value = Math.Max(0, Math.Min(100, p.Percent)); message = p.Message; Invalidate(); } }; ticker.Start();
            Resize += delegate { Arrange(); }; Arrange();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; closeWhenFinished = true; if (cancellation != null) cancellation.Cancel(); Notify("Finishing safely before closing…"); } };
            FormClosed += delegate { ticker.Dispose(); if (cancellation != null) cancellation.Dispose(); };
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84 && m.Result == new IntPtr(1)) {
                Point p = PointToClient(new Point((short)((long)m.LParam & 65535), (short)(((long)m.LParam >> 16) & 65535)));
                if (p.Y < 40 * scale && p.X < 1400 * scale) m.Result = new IntPtr(2);
                else if (WindowState == FormWindowState.Normal && p.X > Width - 10 && p.Y > Height - 10) m.Result = new IntPtr(17);
            }
        }
        private DesignButton AddButton(string text, int x, int y, int w, int h, Action action, int glyph = 0, bool primary = false, bool borderless = false)
        {
            DesignButton button = new DesignButton { Text = text, AccessibleName = text, Primary = primary, Glyph = glyph, Borderless = borderless, Tag = (float)h };
            button.Click += delegate { action(); }; Place(button, x, y, w, h); return button;
        }
        private void Place(Control c, int x, int y, int w, int h) { positions[c] = new RectangleF(x, y, w, h); Controls.Add(c); }
        private void Arrange()
        {
            scale = Math.Min(ClientSize.Width / 1586f, ClientSize.Height / 992f);
            foreach (KeyValuePair<Control, RectangleF> pair in positions) { RectangleF r = pair.Value; pair.Key.Bounds = new Rectangle((int)(r.X * scale), (int)(r.Y * scale), (int)(r.Width * scale), (int)(r.Height * scale)); if (pair.Key is TextBox || pair.Key is ComboBox || pair.Key is ListBox) pair.Key.Font = Art.Font(18 * scale, false); }
            setPicker.ItemHeight = (int)(31 * scale); Invalidate();
        }
        private List<ModRow> FilteredRows() {return modRows.Where(r=>(String.IsNullOrWhiteSpace(search.Text)||(r.Mod.Name+" "+r.Mod.Summary).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0)&&(filter=="All"||(filter=="Enabled")==sets.Active.Mods.Contains(r.Mod.Id))).ToList();}
        private void ScrollMods(int delta){modScroll.Value=Math.Max(0,Math.Min(modScroll.Maximum,modScroll.Value+(delta<0?1:-1)));}
        private void SetFilter(string value) { filter = value; modScroll.Value=0; SyncVisibility(); Invalidate(); }
        private void SetPage(string value) { page = value; SyncVisibility(); Invalidate(); }
        private void SyncVisibility()
        {
            foreach (Control c in libraryControls) c.Visible = page == "mods";
            foreach (Control c in packControls) c.Visible = page == "packs";
            foreach (Control c in settingControls) c.Visible = page == "settings";
            List<ModRow> shown=FilteredRows();int maximum=Math.Max(0,shown.Count-4);
            modScroll.Maximum=maximum;if(modScroll.Value>maximum)modScroll.Value=maximum;
            modScroll.Visible=page=="mods"&&maximum>0;
            if(copyCartridgeCommand!=null)copyCartridgeCommand.Visible=page=="mods"&&selectedMod.Bit!=1;
            foreach(ModRow row in modRows){int index=shown.IndexOf(row)-modScroll.Value;bool visible=page=="mods"&&index>=0&&index<4&&shown.Contains(row);row.Visible=visible;if(visible)positions[row]=new RectangleF(268,404+index*112,802,104);}
            Arrange();
            modsNav.Selected = page == "mods"; packsNav.Selected = page == "packs"; settingsNav.Selected = page == "settings";
            all.Selected = filter == "All"; enabledFilter.Selected = filter == "Enabled"; disabledFilter.Selected = filter == "Disabled";
            foreach (Control c in Controls) c.Invalidate();
        }
        private void PopulateSets()
        {
            loadingSets = true; setPicker.Items.Clear(); packList.Items.Clear();
            foreach (ModSet set in sets.Document.Sets) { setPicker.Items.Add(set.Name); packList.Items.Add(set.Name); }
            setPicker.SelectedItem = sets.Active.Name; packList.SelectedItem = sets.Active.Name; loadingSets = false; SyncSelection();
        }
        private void SyncSelection() {foreach(ModRow row in modRows){row.Toggle.Checked=sets.Active.Mods.Contains(row.Mod.Id);row.Selected=row.Mod==selectedMod;row.Toggle.Invalidate();row.Invalidate();} setSelector.Text = sets.Active.Name; SyncVisibility(); Invalidate(); }
        private void Safe(Action work) { try { work(); } catch (Exception ex) { error = ex.Message; MessageBox.Show(this, ex.Message, "Rift Revival", MessageBoxButtons.OK, MessageBoxIcon.Information); Invalidate(); } }
        private void Notify(string text) { message = text; error = ""; Invalidate(); }
        private void CreateSet()
        {
            using (Form dialog = new Form { Text = "Create mod set", ClientSize = new Size(460, 156), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, BackColor = Art.Bg, ForeColor = Art.Text }) {
                Label label = new Label { Text = "Name your mod set", Bounds = new Rectangle(20, 16, 400, 28), Font = Art.Font(18, false) };
                TextBox name = new TextBox { Bounds = new Rectangle(20, 52, 420, 30), MaxLength = 48, Font = Art.Font(18, false) };
                Button create = new Button { Text = "Create", Bounds = new Rectangle(330, 104, 110, 32), DialogResult = DialogResult.OK }; dialog.Controls.AddRange(new Control[] { label, name, create }); dialog.AcceptButton = create;
                if (dialog.ShowDialog(this) == DialogResult.OK) Safe(delegate { sets.Create(name.Text); PopulateSets(); Notify("Mod set created."); });
            }
        }
        private void ExportSet() { using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Rift Revival mod set (*.json)|*.json", FileName = "rift-revival-mod-set.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) Safe(delegate { File.WriteAllText(dialog.FileName, sets.ExportActive()); Notify("Mod set exported. It contains a mod list, not game files."); }); }
        private void ImportSet() { using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Rift Revival mod set (*.json)|*.json" }) if (dialog.ShowDialog(this) == DialogResult.OK) Safe(delegate { if (new FileInfo(dialog.FileName).Length > 65536) throw new IOException("Mod set file is too large."); sets.Import(File.ReadAllText(dialog.FileName)); PopulateSets(); Notify("Mod set imported."); }); }
        private void RefreshState()
        {
            valid = false; bool exists = library.Exists;
            try { if (exists) { library.VerifyCritical(); valid = true; } } catch (Exception e) { error = e.Message; }
            play.Enabled = !busy; play.Text = valid ? "PLAY" : "INSTALL";
            checkUpdates.Enabled = !busy;
            verify.Enabled = !busy && exists; install.Enabled = !busy && !exists; remove.Enabled = !busy && exists; source.ReadOnly = busy || exists; browse.Enabled = !busy && !exists;
            foreach(ModRow row in modRows){row.Toggle.Enabled=!busy;row.Installed=valid;row.Invalidate();} setPicker.Enabled = !busy; setSelector.Enabled = !busy; cancel.Visible = busy; bar.Visible = busy; Invalidate();
        }
        private async Task RunWork(Action<CancellationToken> action, string success, bool canCancel = true)
        {
            if (busy) return; busy = true; cancellation = new CancellationTokenSource(); error = ""; progress.Take(); RefreshState(); cancel.Visible = canCancel;
            try { await Task.Run(delegate { action(cancellation.Token); }); Notify(success); }
            catch (OperationCanceledException) { Notify("Cancelled. Your original Steam game was not changed."); }
            catch (Exception e) { error = e.Message; MessageBox.Show(this, e.Message, "Operation could not finish", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { progress.Take(); busy = false; cancellation.Dispose(); cancellation = null; RefreshState(); if (closeWhenFinished) Close(); }
        }
        private async Task UpdateLauncher()
        {
            if(busy)return;
            busy=true;cancellation=new CancellationTokenSource();RefreshState();Notify("Checking GitHub for launcher updates…");
            string stage=null;
            try {
                LauncherRelease release=await Task.Run(delegate{return LauncherUpdates.Check(cancellation.Token);});
                cancellation.Token.ThrowIfCancellationRequested();
                if(release==null){Notify("No newer stable launcher release is available.");return;}
                using(Form dialog=new Form {Text="Launcher update "+release.Version,ClientSize=new Size(640,440),StartPosition=FormStartPosition.CenterParent,BackColor=Art.Bg,ForeColor=Art.Text,MinimizeBox=false,MaximizeBox=false,FormBorderStyle=FormBorderStyle.FixedDialog}) {
                    TextBox notes=new TextBox {Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Art.Bg,ForeColor=Art.Text,Bounds=new Rectangle(20,20,600,340),Text="Launcher "+LauncherUpdates.CurrentVersion+" → "+release.Version+Environment.NewLine+Environment.NewLine+release.Notes+Environment.NewLine+Environment.NewLine+"Updates only the launcher. Your game copy, saves and mod selections stay in place."};
                    Button update=new Button {Text="Update and restart",Bounds=new Rectangle(400,385,220,35),DialogResult=DialogResult.OK};
                    Button later=new Button {Text="Later",Bounds=new Rectangle(280,385,100,35),DialogResult=DialogResult.Cancel};dialog.Controls.AddRange(new Control[]{notes,update,later});dialog.CancelButton=later;
                    if(dialog.ShowDialog(this)!=DialogResult.OK){Notify("Update postponed.");return;}
                }
                Notify("Downloading and verifying the launcher update…");
                stage=await Task.Run(delegate{return LauncherUpdates.Prepare(release,AppDomain.CurrentDomain.BaseDirectory,cancellation.Token);});
                cancellation.Token.ThrowIfCancellationRequested();cancel.Visible=false;Notify("Starting the update helper…");
                // The helper must signal readiness before this window is allowed to close.
                await Task.Run(delegate{LauncherUpdates.StartHelper(stage);});stage=null;closeWhenFinished=true;
            } catch(OperationCanceledException){Notify("Launcher update cancelled.");}
            catch(Exception e){error=e.Message;MessageBox.Show(this,e.Message,"Launcher update",MessageBoxButtons.OK,MessageBoxIcon.Information);}
            finally {if(stage!=null){try{LauncherUpdates.DeleteStage(stage);}catch{}}busy=false;cancellation.Dispose();cancellation=null;RefreshState();if(closeWhenFinished)Close();}
        }
        private async Task Play()
        {
            if (busy) return;
            if (!valid) { SetPage("settings"); Notify("Install the separate game copy first."); return; }
            int selection = sets.Active.Mask;
            busy = true; error = ""; RefreshState(); cancel.Visible = false;
            try {
                Notify("Preparing selected mods…"); await Task.Run(delegate { library.SetMods(selection); });
                using (Process process = library.Launch()) {
                    Notify("Starting Interstellar Rift…"); await Task.Delay(4000);
                    if (process != null && process.HasExited) throw new IOException("The game exited during startup. Keep Steam open and check its log before retrying.");
                    Notify("Launch requested. Join a server using the same gameplay mods.");
                }
            } catch (Exception e) { error = e.Message; MessageBox.Show(this, e.Message, "Could not launch", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { busy = false; RefreshState(); if (closeWhenFinished) Close(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (Brush b = new SolidBrush(Color.FromArgb(8, 19, 27))) g.FillRectangle(b, 0, 0, 254, 992);
            using (Pen p = new Pen(Art.Line)) { g.DrawRectangle(p, 0, 0, 1585, 991); g.DrawLine(p, 254, 42, 254, 992); g.DrawLine(p, 0, 42, 1586, 42); }
            g.DrawImage(Art.Image("logo.png"), new RectangleF(70, 64, 114, 114));
            Art.Label(g, "RIFT", 35, 181, 190, 24, 24, Art.Text, false, StringAlignment.Center);
            Art.Label(g, "REVIVAL", 35, 207, 190, 24, 24, Art.Text, false, StringAlignment.Center);
            g.DrawImage(Art.Image("logo.png"), new RectangleF(17, 7, 26, 27));
            Art.Label(g, "RIFT REVIVAL", 55, 4, 220, 33, 16, Art.Text);
            using (Brush b = new SolidBrush(Art.Teal)) g.FillRectangle(b, 1, page == "mods" ? 261 : page == "packs" ? 342 : 422, 8, 80);
            Image heroBanner = Art.Image("hero.png"); float heroHeight = heroBanner.Width * 201f / 1332;
            g.DrawImage(heroBanner, new RectangleF(254, 43, 566, 201), new RectangleF(0, (heroBanner.Height - heroHeight) * .40f, 913, heroHeight), GraphicsUnit.Pixel);
            using (LinearGradientBrush banner = new LinearGradientBrush(new RectangleF(820,43,766,201), Color.FromArgb(17,54,64), Color.FromArgb(7,20,29), LinearGradientMode.Horizontal))
                g.FillRectangle(banner, new RectangleF(820,43,766,201));
            Art.ModEmblem(g, new RectangleF(1400, 76, 118, 118));
            Art.Label(g, page == "mods" ? "My Mods" : page == "packs" ? "Mod Packs" : "Settings", 282, 93, 665, 66, 48, Art.Text, true);
            Art.Label(g, page == "mods" ? "Manage your Interstellar Rift mods." : page == "packs" ? "Save and share your favourite mod selections." : "Manage your game installation.", 283, 164, 760, 39, 26, Art.Text);
            using (Pen p = new Pen(Art.Line)) { g.DrawLine(p, 254, 244, 1586, 244); g.DrawLine(p, 254, 324, 1586, 324); g.DrawLine(p, 254, 878, 1586, 878); }
            Art.Label(g, "Active mod set:", 282, 262, 145, 44, 19, Art.Muted);
            Art.Box(g, new RectangleF(427, 262, 391, 45), Art.Bg, Color.FromArgb(53, 85, 101));
            if (page == "mods") PaintMods(g); else if (page == "packs") PaintPacks(g); else PaintSettings(g);
            using (Brush b = new SolidBrush(Art.Teal)) g.FillEllipse(b, 283, 918, 20, 20);
            Art.Label(g, sets.Active.Mods.Count+ (sets.Active.Mods.Count==1?" mod enabled":" mods enabled"), 315, 899, 144, 59, 20, Art.Text);
            using (Pen p = new Pen(Art.Line)) g.DrawLine(p, 459, 914, 459, 944);
            Art.Label(g, "Changes apply on next launch.", 482, 899, 423, 59, 18, Art.Muted);
            using (Brush b = new SolidBrush(valid ? Art.Teal : Color.FromArgb(236, 177, 95))) g.FillEllipse(b, 1421, 921, 15, 15);
            Art.Label(g, busy ? "Working…" : valid ? "Ready to play" : "Not installed", 1442, 902, 140, 52, 17, Art.Muted);
            if (!String.IsNullOrEmpty(message) || !String.IsNullOrEmpty(error)) Art.Label(g, String.IsNullOrEmpty(error) ? message : error, 282, 961, 1275, 24, 14, String.IsNullOrEmpty(error) ? Art.Muted : Color.FromArgb(242, 186, 137));
        }
        private void PaintMods(Graphics g)
        {
            Art.Box(g, new RectangleF(268, 339, 486, 48), Art.Bg, Art.Line); Art.Icon(g, 0xf52a, new RectangleF(284, 349, 27, 28), Art.Muted);
            Art.Box(g, new RectangleF(767, 339, 303, 48), Art.Bg, Art.Line);
            if(FilteredRows().Count==0) Art.Label(g,"No matching mods",298,416,600,70,22,Art.Muted);
            Art.Box(g, new RectangleF(1089, 325, 476, 530), Color.FromArgb(10, 24, 33), Art.Line);
            if(selectedMod.Bit!=1) {PaintCartridge(g);return;}
            Art.Label(g, "Focusing Crystal", 1112, 342, 425, 44, 33, Art.Text, true);
            Art.Label(g, "Recipe Mod", 1112, 383, 420, 39, 27, Art.Text);
            Art.Box(g, new RectangleF(1114, 430, 112, 37), Art.Bg, Color.FromArgb(49, 83, 100)); Art.Icon(g, valid ? 0xf26a : 0xf30a, new RectangleF(1125, 439, 21, 21), Art.Teal); Art.Label(g, valid ? "Installed" : "Included", 1151, 430, 75, 37, 16, Art.Text);
            Art.Box(g, new RectangleF(1238, 430, 121, 37), Art.Bg, Color.FromArgb(49, 83, 100)); using (Brush b = new SolidBrush(sets.Active.Enabled ? Art.Teal : Art.Muted)) g.FillEllipse(b, 1249, 440, 17, 17); Art.Label(g, sets.Active.Enabled ? "Enabled" : "Disabled", 1273, 430, 85, 37, 16, Art.Text);
            using (Pen p = new Pen(Art.Line)) { g.DrawLine(p, 1114, 480, 1543, 480); g.DrawLine(p, 1114, 746, 1543, 746); }
            Art.Label(g, "Tier III focusing crystals require 50 Carbon\nand 50 Quartz.", 1112, 490, 432, 62, 21, Art.Text);
            string[] images = { "carbon.png", "quartz.png", "crystal.png" }; string[] labels = { "50 Carbon", "50 Quartz", "1 Crystal" }; int[] x = { 1114, 1260, 1420 };
            for (int i = 0; i < 3; i++) {
                RectangleF cell = new RectangleF(x[i], 570, i == 2 ? 124 : 112, 120);
                Art.Box(g, cell, Color.FromArgb(18, 31, 41), Art.Line);
                Art.ModEmblem(g, new RectangleF(cell.X + 19, cell.Y + 18, cell.Width - 38, 72));
                Art.Label(g, labels[i], x[i] - 6, 692, 133, 33, 20, Art.Text, false, StringAlignment.Center);
            }
            Art.Icon(g, 0xf4fe, new RectangleF(1232, 621, 23, 26), Art.Muted); Art.Icon(g, 0xf138, new RectangleF(1380, 620, 31, 28), Art.Muted);
            Art.Icon(g, 0xf431, new RectangleF(1116, 767, 30, 30), Art.Muted); Art.Label(g, "Requires the same recipe mod on the server.", 1158, 765, 388, 43, 18, Art.Muted);
        }
        private void PaintPacks(Graphics g)
        {
            Art.Box(g, new RectangleF(282, 348, 650, 502), Color.FromArgb(10, 24, 33), Art.Line);
            Art.Label(g, "Your saved mod sets", 302, 365, 580, 45, 30, Art.Text, true);
            Art.Label(g, "Build your next play session", 981, 366, 560, 48, 30, Art.Text, true);
            Art.Label(g, "A mod set remembers which mods are enabled.\n\nSelect a set, then choose Use selected set.\n\nExport shares the mod list and version identifiers. It does not include game files.\n\nIncludes the recipe and five independently selectable cartridges. Imports containing unknown mods are rejected.", 984, 426, 528, 330, 22, Art.Muted);
        }
        private void PaintCartridge(Graphics g)
        {
            ModDefinition mod=selectedMod;bool on=sets.Active.Mods.Contains(mod.Id);
            Art.Label(g,mod.Name,1112,342,425,50,31,Art.Text,true);
            Art.Label(g,"Cartridge Mod · Preview",1112,393,420,38,25,Art.Text);
            Art.Label(g,on?"Enabled on next launch":"Disabled",1114,437,425,32,18,on?Art.Teal:Art.Muted);
            using(Pen p=new Pen(Art.Line))g.DrawLine(p,1114,480,1543,480);
            Art.Label(g,mod.Description,1114,496,425,154,21,Art.Text);
            Art.Label(g,"Research target: ~"+(mod.Ticks/1200)+" active minutes",1114,652,425,33,19,Art.Muted);
            Art.Label(g,"No new loot drops. Existing items keep their stats.",1114,735,425,30,16,Art.Muted);
            using(Pen p=new Pen(Art.Line))g.DrawLine(p,1114,770,1543,770);
            Art.Icon(g,0xf431,new RectangleF(1116,789,28,28),Art.Muted);
            Art.Label(g,"Use the same mod set on client and server.",1158,783,387,55,18,Art.Muted);
        }
        private void PaintSettings(Graphics g)
        {
            Art.Box(g, new RectangleF(282, 348, 1282, 503), Color.FromArgb(10, 24, 33), Art.Line);
            Art.Label(g, "Game installation", 304, 366, 1100, 43, 30, Art.Text, true);
            Art.Label(g, "Original Steam game folder", 305, 411, 995, 30, 19, Art.Muted);
            Art.Label(g, "The first installation creates a separate game copy and needs about 5 GB.\nYour Steam files stay unchanged. Saved worlds use the launcher's separate profile.", 305, 502, 1179, 79, 22, Art.Muted);
            Art.Label(g, "Library location", 305, 702, 400, 34, 19, Art.Text, true);
            Art.Label(g, library.Root, 305, 740, 1190, 54, 17, Art.Muted);
            Art.Label(g,"Launcher "+LauncherUpdates.CurrentVersion+" · stable updates",1000,580,510,30,17,Art.Muted);
        }
        public void RenderPreview(string path, string requestedPage, int width, int height)
        {
            if(requestedPage=="cartridge"){selectedMod=ModCatalog.All[4];requestedPage="mods";modScroll.Value=modScroll.Maximum;SyncSelection();}
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Location = new Point(-32000, -32000); ClientSize = new Size(width, height); SetPage(requestedPage);
            Show(); Arrange(); PerformLayout(); Application.DoEvents();
            using (Bitmap image = new Bitmap(Width, Height)) { DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
            Close();
        }
    }
    public static class Program
    {
        [STAThread] public static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Library");
            try {
                if(args.Length==5&&args[0]=="--apply-update"){LauncherUpdates.RunHelper(args[1],args[2],Int32.Parse(args[3]),args[4]);return 0;}
                if(args.Length==3&&args[0]=="--recover-update") {
                    using(var recoveryMutex=new Mutex(false,LauncherUpdates.InstanceMutex)) {
                        bool owned;try{owned=recoveryMutex.WaitOne(0);}catch(AbandonedMutexException){owned=true;}
                        if(!owned)throw new IOException("Close the launcher before recovering an update.");
                        try{LauncherUpdates.Recover(args[1],args[2]);}finally{recoveryMutex.ReleaseMutex();}
                    }return 0;
                }
                Art.InitializeFont();
                if (args.Length >= 2 && args[0] == "--render-preview") {
                    using (MainForm form = new MainForm(root)) form.RenderPreview(Path.GetFullPath(args[1]), args.Length > 2 ? args[2] : "mods", args.Length > 3 ? Int32.Parse(args[3]) : 1586, args.Length > 4 ? Int32.Parse(args[4]) : 992); return 0;
                }
                using (Mutex mutex = new Mutex(false, LauncherUpdates.InstanceMutex)) {
                    bool owns;try{owns=mutex.WaitOne(0);}catch(AbandonedMutexException){owns=true;}
                    if (!owns) { MessageBox.Show("Rift Revival Launcher is already open or updating. Use its existing window.", "Rift Revival"); return 1; }
                    try { using (MainForm form = new MainForm(root)) {
                        if(args.Length==3&&args[0]=="--updated") {
                            string stage=Path.GetFullPath(args[1]);Guid acknowledgement;
                            if(!stage.StartsWith(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Updates")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(args[2],"N",out acknowledgement))throw new IOException("Invalid update startup acknowledgement.");
                            form.Shown+=delegate{File.WriteAllText(Path.Combine(stage,"started-"+acknowledgement.ToString("N")+".txt"),"ready");};
                        }
                        Application.Run(form);
                    } } finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception error) {
                if (args.Length > 0) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-error.log"), error.ToString()); return 1; }
                MessageBox.Show(error.Message, "Rift Revival could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
    }
}
