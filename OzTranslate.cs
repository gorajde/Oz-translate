// Oz-translate — fare imlecinin altındaki İngilizce metni okuyup Türkçeye çeviren
// sistem tepsisi uygulaması. Windows yerleşik OCR + Google Çeviri kullanır.
// Tek dosya, .NET Framework, ekstra kurulum gerektirmez.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;

namespace OzTranslate
{
    static class NativeMethods
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_HOTKEY = 0x0312;
        public const uint VK_TAB = 0x09;
        public const uint VK_Z = 0x5A;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_NOREPEAT = 0x4000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
    }

    // Google Çeviri (resmi olmayan ücretsiz uç nokta). İngilizce -> Türkçe.
    static class Translator
    {
        static readonly HttpClient _http;
        static readonly object _lock = new object();
        static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();

        static Translator()
        {
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(8);
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) OzTranslate/1.0");
        }

        public static string Translate(string text)
        {
            if (!TextPolicy.HasText(text)) return "";
            lock (_lock)
            {
                string hit;
                if (_cache.TryGetValue(text, out hit)) return hit;
            }

            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=tr&dt=t&q="
                         + Uri.EscapeDataString(text);
            string json = _http.GetStringAsync(url).GetAwaiter().GetResult();

            string sourceLanguage = "";
            StringBuilder sb = new StringBuilder();
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 8 * 1024 * 1024;
                object root = ser.DeserializeObject(json);
                object[] arr = root as object[];
                if (arr != null && arr.Length > 2) sourceLanguage = arr[2] as string ?? "";
                object[] sentences = (arr != null && arr.Length > 0) ? arr[0] as object[] : null;
                if (sentences != null)
                {
                    foreach (object o in sentences)
                    {
                        object[] seg = o as object[];
                        if (seg != null && seg.Length > 0 && seg[0] != null)
                            sb.Append(seg[0].ToString());
                    }
                }
            }
            catch { }

            string result = TextPolicy.DisplayTranslation(text, sb.ToString(), sourceLanguage);
            // Boş sonuçları da önbelleğe al: Türkçe/aynı metni tekrar gönderme.
            if (!string.IsNullOrEmpty(sourceLanguage))
            {
                lock (_lock)
                {
                    if (_cache.Count > 300) _cache.Clear();
                    _cache[text] = result;
                }
            }
            return result;
        }
    }

    static class TextPolicy
    {
        public static bool HasText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            foreach (char c in text) if (char.IsLetter(c)) return true;
            return false;
        }

        static string ComparisonKey(string text)
        {
            StringBuilder key = new StringBuilder();
            foreach (char c in (text ?? "").Normalize(NormalizationForm.FormKD).ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) key.Append(c == 'ı' ? 'i' : c);
            return key.ToString();
        }

        public static string DisplayTranslation(string source, string translated, string language)
        {
            if (!HasText(source) || !HasText(translated) || string.IsNullOrEmpty(language)) return "";
            if (language.Equals("tr", StringComparison.OrdinalIgnoreCase) ||
                language.StartsWith("tr-", StringComparison.OrdinalIgnoreCase)) return "";
            if (ComparisonKey(source) == ComparisonKey(translated)) return "";
            return translated.Trim();
        }
    }

    class AppSettings
    {
        public int Background { get; set; }
        public int Foreground { get; set; }
        public int Border { get; set; }
        public int OpacityPercent { get; set; }
        public float FontSize { get; set; }

        public AppSettings()
        {
            Background = Color.FromArgb(34, 37, 43).ToArgb();
            Foreground = Color.FromArgb(240, 240, 242).ToArgb();
            Border = Color.FromArgb(0, 150, 215).ToArgb();
            OpacityPercent = 96;
            FontSize = 12.5f;
        }

        static string FilePath { get { return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Oz-translate", "settings.json"); } }

        public void Validate()
        {
            OpacityPercent = Math.Max(30, Math.Min(100, OpacityPercent));
            if (float.IsNaN(FontSize) || float.IsInfinity(FontSize)) FontSize = 12.5f;
            FontSize = Math.Max(8, Math.Min(28, FontSize));
        }

        public static AppSettings Load()
        {
            try
            {
                AppSettings value = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (value != null) { value.Validate(); return value; }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
        }
    }

    class SettingsForm : Form
    {
        readonly Button _background = new Button(), _foreground = new Button(), _border = new Button();
        readonly NumericUpDown _opacity = new NumericUpDown(), _fontSize = new NumericUpDown();
        readonly CheckBox _startup = new CheckBox();
        readonly Func<bool> _getStartup;
        readonly Func<bool, bool> _setStartup;
        readonly Action<AppSettings> _apply;

        public SettingsForm(AppSettings settings, Func<bool> getStartup, Func<bool, bool> setStartup, Action<AppSettings> apply)
        {
            _getStartup = getStartup; _setStartup = setStartup; _apply = apply;
            Text = "Oz-translate ayarları";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 440);
            Font = new Font("Segoe UI", 10);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill; layout.Padding = new Padding(18);
            layout.ColumnCount = 2; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            Controls.Add(layout);
            AddRow(layout, "Balon arka planı", _background);
            AddRow(layout, "Yazı rengi", _foreground);
            AddRow(layout, "Çerçeve rengi", _border);
            foreach (Button button in new Button[] { _background, _foreground, _border })
            {
                button.Text = "renk seç"; button.UseVisualStyleBackColor = false;
                button.Click += delegate(object sender, EventArgs e)
                {
                    Button selected = (Button)sender;
                    using (ColorDialog dialog = new ColorDialog())
                    {
                        dialog.Color = selected.BackColor;
                        if (dialog.ShowDialog(this) == DialogResult.OK) SetColor(selected, dialog.Color);
                    }
                };
            }
            _opacity.Minimum = 30; _opacity.Maximum = 100;
            AddRow(layout, "Opaklık (%)", _opacity);
            _fontSize.Minimum = 8; _fontSize.Maximum = 28; _fontSize.DecimalPlaces = 1; _fontSize.Increment = 0.5m;
            AddRow(layout, "Yazı boyutu (punto)", _fontSize);
            _startup.Text = "Windows ile başlat"; _startup.AutoSize = true;
            layout.Controls.Add(_startup); layout.SetColumnSpan(_startup, 2);
            Label help = new Label();
            help.Text = "ctrl + alt + z: çeviriyi aç / kapat\r\ntab: balon açıkken 15 satır çevir\r\nö simgesine çift tık: ayarları aç\r\nx: ayarları gizle; program tepside çalışmaya devam eder\r\nbalonu taşımak için üstündeki tutamacı sürükle\r\ndüşük opaklık balonu daha saydam yapar";
            help.Font = new Font("Segoe UI", 8.5f); help.AutoSize = true;
            help.Margin = new Padding(0, 12, 0, 12);
            layout.Controls.Add(help); layout.SetColumnSpan(help, 2);
            Button reset = new Button(); reset.Text = "Varsayılanlara sıfırla"; reset.AutoSize = true;
            reset.Click += delegate { ApplySettings(new AppSettings(), false); };
            layout.Controls.Add(reset);
            Button save = new Button(); save.Text = "Uygula"; save.AutoSize = true;
            save.Click += delegate { ApplySettings(ReadSettings(), _startup.Checked); };
            layout.Controls.Add(save);
            LoadSettings(settings);
        }

        static void AddRow(TableLayoutPanel layout, string title, Control control)
        {
            Label label = new Label(); label.Text = title; label.AutoSize = true; label.Anchor = AnchorStyles.Left;
            control.Dock = DockStyle.Fill; control.Margin = new Padding(3, 3, 3, 6);
            layout.Controls.Add(label); layout.Controls.Add(control);
        }

        static void SetColor(Button button, Color color)
        {
            button.BackColor = Color.FromArgb(255, color);
            button.ForeColor = color.GetBrightness() < 0.5f ? Color.White : Color.Black;
        }

        public void LoadSettings(AppSettings settings)
        {
            SetColor(_background, Color.FromArgb(settings.Background));
            SetColor(_foreground, Color.FromArgb(settings.Foreground));
            SetColor(_border, Color.FromArgb(settings.Border));
            _opacity.Value = settings.OpacityPercent; _fontSize.Value = (decimal)settings.FontSize;
            _startup.Checked = _getStartup();
        }

        AppSettings ReadSettings()
        {
            return new AppSettings { Background = _background.BackColor.ToArgb(), Foreground = _foreground.BackColor.ToArgb(),
                Border = _border.BackColor.ToArgb(), OpacityPercent = (int)_opacity.Value, FontSize = (float)_fontSize.Value };
        }

        void ApplySettings(AppSettings settings, bool startup)
        {
            try { settings.Save(); }
            catch { MessageBox.Show(this, "ayarlar kaydedilemedi. klasörün yazma iznini kontrol edin.", "Oz-translate"); return; }
            _apply(settings);
            if (!_setStartup(startup)) MessageBox.Show(this, "windows ile başlatma tercihi kaydedilemedi.", "Oz-translate");
            LoadSettings(settings);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }

    // Geçici hata ayıklama günlüğü (test için). Enabled=false yapılarak kapatılabilir.
    static class Logger
    {
        static readonly object _l = new object();
        static string _path;
        public static bool Enabled = false; // hata ayıklama günlüğü (gerekirse true yapıp yeniden derleyin)

        static string Path_()
        {
            if (_path == null)
                _path = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(Application.ExecutablePath), "oz-debug.log");
            return _path;
        }

        public static void Log(string s)
        {
            if (!Enabled) return;
            try
            {
                lock (_l)
                    File.AppendAllText(Path_(),
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }

    // Windows yerleşik OCR motoru sarmalayıcısı (WinRT).
    class OcrService
    {
        readonly OcrEngine _engine;

        public OcrService()
        {
            OcrEngine e = OcrEngine.TryCreateFromUserProfileLanguages();
            if (e == null)
            {
                var langs = OcrEngine.AvailableRecognizerLanguages;
                if (langs != null && langs.Count > 0)
                    e = OcrEngine.TryCreateFromLanguage(langs[0]);
            }
            _engine = e;
        }

        public bool Available { get { return _engine != null; } }

        public string LanguageTag
        {
            get { return _engine != null ? _engine.RecognizerLanguage.LanguageTag : "(yok)"; }
        }

        // WinRT async işlemini, birleşik Windows.winmd (SDK) gerektirmeden bekler.
        static T Await<T>(IAsyncOperation<T> op)
        {
            using (ManualResetEventSlim done = new ManualResetEventSlim(false))
            {
                T result = default(T);
                Exception err = null;
                op.Completed = delegate(IAsyncOperation<T> a, AsyncStatus s)
                {
                    try { result = a.GetResults(); }
                    catch (Exception ex) { err = ex; }
                    finally { done.Set(); }
                };
                done.Wait();
                if (err != null) throw err;
                return result;
            }
        }

        public OcrResult Recognize(Bitmap bmp)
        {
            if (_engine == null) return null;

            byte[] bytes;
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Bmp);
                bytes = ms.ToArray();
            }

            using (InMemoryRandomAccessStream ras = new InMemoryRandomAccessStream())
            {
                using (DataWriter writer = new DataWriter(ras.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    Await<uint>(writer.StoreAsync());
                    Await<bool>(writer.FlushAsync());
                    writer.DetachStream();
                }
                ras.Seek(0);
                BitmapDecoder decoder = Await<BitmapDecoder>(BitmapDecoder.CreateAsync(ras));
                using (SoftwareBitmap bitmap = Await<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync()))
                    return Await<OcrResult>(_engine.RecognizeAsync(bitmap));
            }
        }
    }

    // Tıklamayı engellemeyen (click-through), odağı çalmayan, yarı saydam çeviri balonu.
    class PopupForm : Form
    {
        const int PAD = 12;
        const int GAP = 14;
        const int MAXW = 470;
        const int RADIUS = 12;

        Font _font;
        AppSettings _appearance;
        readonly DragHandle _handle;
        Point _dragCursor, _dragLocation;
        public bool Dragging { get; private set; }
        public bool ContainsPointer(Point point)
        {
            return Visible && (Bounds.Contains(point) || _handle.Bounds.Contains(point) || Dragging);
        }

        public void ApplySettings(AppSettings settings)
        {
            _appearance = settings;
            if (_font != null) _font.Dispose();
            _font = new Font("Segoe UI", settings.FontSize, FontStyle.Regular);
            Opacity = settings.OpacityPercent / 100.0;
            BackColor = Color.FromArgb(settings.Background);
            _handle.BackColor = Color.FromArgb(settings.Border);
            Hide();
            Invalidate();
        }

        // Ayrı tutamaç penceresi: metin balonu tıklama geçişini korur.
        sealed class DragHandle : Form
        {
            public DragHandle()
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
                Size = new Size(64, 18); TopMost = true; Cursor = Cursors.SizeAll;
                AccessibleName = "balonu taşımak için sürükle";
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                using (Pen pen = new Pen(BackColor.GetBrightness() < 0.5f ? Color.White : Color.Black))
                    for (int y = 6; y <= 12; y += 3) e.Graphics.DrawLine(pen, 22, y, 42, y);
            }
        }
        string _text = "";

        public PopupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; // DPI ölçeklemesinin konumu kaydırmasını engelle
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            _handle = new DragHandle();
            _handle.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                Dragging = true; _dragCursor = Cursor.Position; _dragLocation = Location; _handle.Capture = true;
            };
            _handle.MouseMove += delegate
            {
                if (!Dragging) return;
                Point cursor = Cursor.Position;
                Rectangle area = Screen.FromPoint(cursor).WorkingArea;
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, _dragLocation.X + cursor.X - _dragCursor.X)),
                    Math.Max(area.Top + 18, Math.Min(area.Bottom - Height, _dragLocation.Y + cursor.Y - _dragCursor.Y)));
                PlaceHandle();
            };
            _handle.MouseUp += delegate { Dragging = false; _handle.Capture = false; };
            _handle.MouseCaptureChanged += delegate { if (!_handle.Capture) Dragging = false; };
            ApplySettings(new AppSettings());
            Visible = false;
            IntPtr forceHandle = this.Handle; // arka plandan BeginInvoke için handle'ı oluştur
            GC.KeepAlive(forceHandle);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_LAYERED = 0x00080000;
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void ShowAt(string text, Rectangle block, Point cursor)
        {
            if (string.IsNullOrWhiteSpace(text)) { Hide(); return; }
            _text = text;

            Size max = new Size(MAXW - 2 * PAD, 4096);
            Size sz = TextRenderer.MeasureText(_text, _font, max,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int w = Math.Min(MAXW, sz.Width + 2 * PAD + 2);
            int h = sz.Height + 2 * PAD + 2;
            if (w < 60) w = 60;
            ClientSize = new Size(w, h);

            Region previous = Region;
            using (GraphicsPath gp = Round(new Rectangle(0, 0, w, h), RADIUS))
                Region = new Region(gp);
            if (previous != null) previous.Dispose();

            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;
            int x, y; string br;
            if (block.Right + GAP + w <= wa.Right) { x = block.Right + GAP; y = cursor.Y - h / 2; br = "right"; }
            else if (block.Left - GAP - w >= wa.Left) { x = block.Left - GAP - w; y = cursor.Y - h / 2; br = "left"; }
            else if (block.Bottom + GAP + h <= wa.Bottom) { x = block.Left; y = block.Bottom + GAP; br = "below"; }
            else { x = block.Left; y = block.Top - GAP - h; br = "above"; }

            if (x < wa.Left) x = wa.Left;
            if (x > wa.Right - w) x = wa.Right - w;
            if (y < wa.Top) y = wa.Top;
            if (y > wa.Bottom - h) y = wa.Bottom - h;
            Location = new Point(x, y);
            Logger.Log("  place cur=" + cursor.X + "," + cursor.Y + " wa=" + wa.X + "," + wa.Y + "," + wa.Width + "," + wa.Height +
                       " size=" + w + "x" + h + " branch=" + br + " -> " + x + "," + y);

            if (!Visible) Show();
            PlaceHandle();
            if (!_handle.Visible) _handle.Show(this);
            // WinForms/DPI bazen Location'ı kaydırır; konum+boyutu fiziksel piksellerle kesin uygula.
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, x, y, w, h, NativeMethods.SWP_NOACTIVATE);
            Invalidate();
            Logger.Log("ShowAt want=" + x + "," + y + " real=" + Bounds.X + "," + Bounds.Y + " size=" + w + "x" + h);
        }

        void PlaceHandle()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            _handle.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - _handle.Width, Left + (Width - _handle.Width) / 2)),
                Top - _handle.Height >= area.Top ? Top - _handle.Height : Top);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible && _handle != null) { _handle.Hide(); Dragging = false; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (_handle != null) _handle.Dispose(); if (_font != null) _font.Dispose(); }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (GraphicsPath gp = Round(rc, RADIUS))
            {
                using (Brush b = new SolidBrush(Color.FromArgb(_appearance.Background))) g.FillPath(b, gp);
                using (Pen pen = new Pen(Color.FromArgb(_appearance.Border), 1.4f)) g.DrawPath(pen, gp);
            }
            Rectangle txt = new Rectangle(PAD, PAD, ClientSize.Width - 2 * PAD, ClientSize.Height - 2 * PAD);
            TextRenderer.DrawText(g, _text, _font, txt, Color.FromArgb(_appearance.Foreground),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Top | TextFormatFlags.Left);
        }
    }

    // Genel kısayol tuşunu (Ctrl+Alt+Z) dinleyen gizli pencere.
    class HotkeyWindow : NativeWindow
    {
        public event Action HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                Action h = HotkeyPressed;
                if (h != null) h();
            }
            base.WndProc(ref m);
        }

        public void Destroy()
        {
            DestroyHandle();
        }
    }

    class TrayContext : ApplicationContext
    {
        const int TIMER_MS = 110;
        const int DWELL_TICKS = 4;     // ~440 ms hareketsizlik
        const int MOVE_THRESHOLD = 6;  // piksel
        const int HOTKEY_ID = 1;

        readonly NotifyIcon _tray;
        readonly ToolStripMenuItem _miActive;
        readonly ToolStripMenuItem _miAuto;
        readonly SettingsForm _settingsForm;
        AppSettings _settings;
        DateTime _popupGraceUntil;
        bool _exiting;
        readonly PopupForm _popup;
        readonly System.Windows.Forms.Timer _timer;
        readonly OcrService _ocr;
        readonly HotkeyWindow _hotkey;
        readonly Icon _iconOn;
        readonly Icon _iconOff;

        readonly NativeMethods.LowLevelKeyboardProc _hookProc;
        IntPtr _hookId = IntPtr.Zero;

        volatile bool _active = true;
        volatile int _generation = 0;
        bool _popupVisible = false;
        Point _lastPos = Point.Empty;
        int _stillTicks = 0;
        bool _pending = false;
        int _pendingLines = 5;
        Point _translationPoint;
        int _busy = 0;

        public TrayContext()
        {
            _ocr = new OcrService();
            _popup = new PopupForm();
            _settings = AppSettings.Load();
            _popup.ApplySettings(_settings);
            _settingsForm = new SettingsForm(_settings, IsAutoStart, SetAutoStart, delegate(AppSettings settings)
            {
                _settings = settings; _generation++; HidePopup(); _popup.ApplySettings(settings);
            });
            _settingsForm.VisibleChanged += delegate
            {
                _generation++; HidePopup(); _pending = true; _stillTicks = 0; _lastPos = Cursor.Position;
            };

            _iconOn = MakeIcon(Color.FromArgb(0, 150, 215));
            _iconOff = MakeIcon(Color.FromArgb(120, 120, 120));

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem header = new ToolStripMenuItem("Oz-translate");
            header.Enabled = false;
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            _miActive = new ToolStripMenuItem("Aktif (Ctrl+Alt+Z)");
            _miActive.Checked = true;
            _miActive.CheckOnClick = true;
            _miActive.Click += delegate { ToggleActive(); };
            menu.Items.Add(_miActive);

            _miAuto = new ToolStripMenuItem("Windows açılışında başlat");
            _miAuto.CheckOnClick = true;
            _miAuto.Checked = IsAutoStart();
            _miAuto.Click += delegate
            {
                if (!SetAutoStart(_miAuto.Checked))
                    MessageBox.Show("windows ile başlatma tercihi kaydedilemedi.", "Oz-translate");
                _miAuto.Checked = IsAutoStart();
            };
            menu.Items.Add(_miAuto);
            menu.Opening += delegate { _miAuto.Checked = IsAutoStart(); };
            ToolStripMenuItem miSettings = new ToolStripMenuItem("Ayarlar");
            miSettings.Click += delegate { ShowSettings(); };
            menu.Items.Add(miSettings);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem miExit = new ToolStripMenuItem("Çıkış");
            miExit.Click += delegate { ExitApp(); };
            menu.Items.Add(miExit);

            _tray = new NotifyIcon();
            _tray.Icon = _iconOn;
            _tray.Text = "Oz-translate (Aktif)";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowSettings(); };

            if (!_ocr.Available)
            {
                _tray.ShowBalloonTip(5000, "Oz-translate",
                    "OCR motoru bulunamadı. Bir dil paketi gerekli.", ToolTipIcon.Warning);
            }

            _hotkey = new HotkeyWindow();
            _hotkey.HotkeyPressed += delegate { ToggleActive(); };
            NativeMethods.RegisterHotKey(_hotkey.Handle, HOTKEY_ID,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_Z);

            _hookProc = HookProc;
            using (Process proc = Process.GetCurrentProcess())
            using (ProcessModule mod = proc.MainModule)
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookProc,
                    NativeMethods.GetModuleHandle(mod.ModuleName), 0);

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = TIMER_MS;
            _timer.Tick += OnTick;
            _timer.Start();

            _tray.ShowBalloonTip(3000, "Oz-translate",
                "Aktif. Fareyi İngilizce metnin üzerinde tutun. (OCR dili: " + _ocr.LanguageTag + ")",
                ToolTipIcon.Info);
        }

        static Icon MakeIcon(Color color)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush b = new SolidBrush(color)) g.FillEllipse(b, 1, 1, 30, 30);
                    using (Font f = new Font("Segoe UI", 15, FontStyle.Bold))
                    {
                        StringFormat sf = new StringFormat();
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        g.DrawString("Ö", f, Brushes.White, new RectangleF(0, 0, 32, 33), sf);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        void ShowSettings()
        {
            if (!_settingsForm.Visible) { _settingsForm.LoadSettings(_settings); _settingsForm.Show(); }
            if (_settingsForm.WindowState == FormWindowState.Minimized) _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
        }

        void OnTick(object sender, EventArgs e)
        {
            if (!_active || _settingsForm.Visible || _exiting) return;
            Point p = Cursor.Position;
            if (_popup.ContainsPointer(p))
            {
                _lastPos = p; _stillTicks = 0; _popupGraceUntil = DateTime.UtcNow.AddMilliseconds(1000);
                return;
            }
            int dx = p.X - _lastPos.X;
            int dy = p.Y - _lastPos.Y;
            if (dx * dx + dy * dy > MOVE_THRESHOLD * MOVE_THRESHOLD)
            {
                _lastPos = p;
                _stillTicks = 0;
                _pending = true;
                _pendingLines = 5;
                _generation++;       // devam eden işi geçersiz kıl
                // Tutamaca ulaşmak için kısa süre tanı; OCR önce balonu gizler.
                if (_popupVisible && _popupGraceUntil == DateTime.MinValue)
                    _popupGraceUntil = DateTime.UtcNow.AddMilliseconds(1000);
                return;
            }
            if (_popupVisible && _popupGraceUntil != DateTime.MinValue)
            {
                if (DateTime.UtcNow < _popupGraceUntil) return;
                HidePopup();
            }
            if (_pending)
            {
                _stillTicks++;
                if (_stillTicks >= DWELL_TICKS)
                {
                    _pending = false;
                    BeginTranslate(p, _pendingLines, _generation);
                }
            }
        }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _active && _popupVisible && !_settingsForm.Visible && !_popup.Dragging)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    NativeMethods.KBDLLHOOKSTRUCT k =
                        (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    if (k.vkCode == NativeMethods.VK_TAB)
                    {
                        Point p = Cursor.Position;
                        if (_popup.ContainsPointer(p)) p = _translationPoint;
                        _generation++;
                        BeginTranslate(p, 15, _generation);
                        return new IntPtr(1); // Tab'ı yut (odak değişmesin)
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        void BeginTranslate(Point p, int lines, int gen)
        {
            if (!_ocr.Available || _exiting) return;
            // Aynı anda yalnızca bir OCR/çeviri görevi çalışabilir.
            // Meşgul durumda bayrağı değiştirme: bayrak çalışan göreve aittir.
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                _pending = true; _pendingLines = lines; _stillTicks = 0;
                Logger.Log("OCR request deferred: worker busy");
                return;
            }

            HidePopup();
            Task.Run(delegate
            {
                try
                {
                    if (gen != _generation) return;

                    Rectangle scr = Screen.FromPoint(p).Bounds;
                    int W = Math.Min(760, scr.Width);
                    int H = lines >= 15 ? Math.Min(560, scr.Height) : Math.Min(260, scr.Height);
                    int anchorUp = lines >= 15 ? 160 : 130;
                    int left = Clamp(p.X - W / 2, scr.Left, scr.Right - W);
                    int top = Clamp(p.Y - anchorUp, scr.Top, scr.Bottom - H);

                    const int SCALE = 2; // küçük yazılarda OCR doğruluğu için 2 kat büyüt
                    OcrResult ocr;
                    using (Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(left, top, 0, 0, new Size(W, H), CopyPixelOperation.SourceCopy);
                        if (gen != _generation) return;
                        using (Bitmap big = new Bitmap(W * SCALE, H * SCALE, PixelFormat.Format32bppRgb))
                        {
                            using (Graphics g2 = Graphics.FromImage(big))
                            {
                                g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                                g2.PixelOffsetMode = PixelOffsetMode.HighQuality;
                                g2.DrawImage(bmp, new Rectangle(0, 0, W * SCALE, H * SCALE),
                                    new Rectangle(0, 0, W, H), GraphicsUnit.Pixel);
                            }
                            if (gen != _generation) return;
                            ocr = _ocr.Recognize(big);
                        }
                    }
                    if (ocr == null || gen != _generation) return;

                    Rectangle block;
                    string anchorText;
                    string text = PickLines(ocr, left, top, p.X - left, p.Y - top, lines, SCALE, out block, out anchorText);
                    if (string.IsNullOrEmpty(text)) { Logger.Log("lines=" + lines + " OCR: (metin yok)"); PostHide(gen); return; }
                    Logger.Log("OCR recognized text; characters=" + text.Length);

                    // Çevredeki İngilizce satırlar, imleç altındaki Türkçe satırın
                    // dilini bastırmasın. Önce hedef satırın çevrilebilirliğini doğrula.
                    string anchorTranslation = Translator.Translate(anchorText);
                    if (gen != _generation) return;
                    if (string.IsNullOrWhiteSpace(anchorTranslation)) { PostHide(gen); return; }
                    string tr = Translator.Translate(text);
                    if (gen != _generation) return;
                    if (string.IsNullOrWhiteSpace(tr)) { PostHide(gen); return; }
                    Logger.Log("Translation completed; characters=" + tr.Length);
                    PostShow(gen, tr, block, p);
                }
                catch (Exception ex)
                {
                    // Metinleri veya URL'leri günlüğe yazma: yalnızca hata türü.
                    Logger.Log("Translation worker error: " + ex.GetType().Name);
                    PostHide(gen);
                }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        static string PickLines(OcrResult ocr, int left, int top, int cursorX, int cursorY, int lines, int scale, out Rectangle block, out string anchorText)
        {
            block = Rectangle.Empty;
            anchorText = "";
            List<double[]> rows = new List<double[]>(); // t,b,l,r,centerY
            List<string> texts = new List<string>();
            foreach (OcrLine ln in ocr.Lines)
            {
                if (ln.Words == null || ln.Words.Count == 0) continue;
                double t = double.MaxValue, b = double.MinValue, l = double.MaxValue, r = double.MinValue;
                foreach (OcrWord w in ln.Words)
                {
                    Rect rc = w.BoundingRect;
                    double wx = rc.X / scale, wy = rc.Y / scale, ww = rc.Width / scale, wh = rc.Height / scale;
                    if (wy < t) t = wy;
                    if (wy + wh > b) b = wy + wh;
                    if (wx < l) l = wx;
                    if (wx + ww > r) r = wx + ww;
                }
                rows.Add(new double[] { t, b, l, r, (t + b) / 2 });
                texts.Add(ln.Text);
            }
            if (rows.Count == 0) return "";

            // satırları dikey konuma göre sırala
            int[] idx = new int[rows.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, delegate(int a, int c) { return rows[a][4].CompareTo(rows[c][4]); });

            int target = -1;
            double best = double.MaxValue;
            for (int i = 0; i < idx.Length; i++)
            {
                double[] row = rows[idx[i]];
                if (cursorY < row[0] - 4 || cursorY > row[1] + 4 ||
                    cursorX < row[2] - 8 || cursorX > row[3] + 8) continue;
                double distance = Math.Abs(row[4] - cursorY);
                if (distance < best) { best = distance; target = i; }
            }
            if (target < 0) return "";
            anchorText = texts[idx[target]];

            // imlecin sütununu (yatay aralık) belirle: hedef satır + imleç x'i
            double colL = rows[idx[target]][2];
            double colR = rows[idx[target]][3];
            if (cursorX < colL) colL = cursorX;
            if (cursorX > colR) colR = cursorX;

            // yalnızca aynı sütunla yatayda örtüşen satırları tut (yan ikon/sütunları ele)
            List<int> col = new List<int>();
            int tposInCol = 0;
            for (int i = 0; i < idx.Length; i++)
            {
                double l = rows[idx[i]][2], r = rows[idx[i]][3];
                if (r >= colL && l <= colR)
                {
                    if (i == target) tposInCol = col.Count;
                    col.Add(idx[i]);
                }
            }
            if (col.Count == 0) col.Add(idx[target]);

            int want = lines >= 15 ? 15 : 5;
            int start = Math.Max(0, tposInCol - 2);
            int end = Math.Min(col.Count - 1, start + want - 1);
            start = Math.Max(0, end - want + 1);

            StringBuilder sb = new StringBuilder();
            double minL = 1e9, maxR = -1e9, minT = 1e9, maxB = -1e9;
            for (int i = start; i <= end; i++)
            {
                double[] rw = rows[col[i]];
                string s = texts[col[i]];
                if (s == null) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(s);
                if (rw[2] < minL) minL = rw[2];
                if (rw[3] > maxR) maxR = rw[3];
                if (rw[0] < minT) minT = rw[0];
                if (rw[1] > maxB) maxB = rw[1];
            }
            string text = sb.ToString().Trim();
            if (text.Length == 0) return "";
            block = Rectangle.FromLTRB(left + (int)minL, top + (int)minT, left + (int)maxR, top + (int)maxB);
            return text;
        }

        void PostShow(int gen, string text, Rectangle block, Point cursor)
        {
            if (_popup.IsDisposed) return;
            try
            {
                _popup.BeginInvoke((Action)delegate
                {
                    if (gen != _generation || !_active || _exiting || _settingsForm.Visible) return;
                    _popup.ShowAt(text, block, cursor);
                    _translationPoint = cursor;
                    _popupVisible = true;
                });
            }
            catch { }
        }

        void PostHide(int gen)
        {
            if (_popup.IsDisposed) return;
            try
            {
                _popup.BeginInvoke((Action)delegate
                {
                    if (gen != _generation) return;
                    HidePopup();
                });
            }
            catch { }
        }

        void HidePopup()
        {
            _popupGraceUntil = DateTime.MinValue;
            if (_popupVisible)
            {
                _popup.Hide();
                _popupVisible = false;
            }
        }

        void ToggleActive()
        {
            _active = !_active;
            _pending = _active; _stillTicks = 0;
            _miActive.Checked = _active;
            _tray.Icon = _active ? _iconOn : _iconOff;
            _tray.Text = _active ? "Oz-translate (Aktif)" : "Oz-translate (Pasif)";
            if (!_active)
            {
                _generation++;
                HidePopup();
            }
            _tray.ShowBalloonTip(1500, "Oz-translate",
                _active ? "Aktif" : "Pasif", ToolTipIcon.Info);
        }

        static int Clamp(int v, int lo, int hi)
        {
            if (hi < lo) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY))
                {
                    if (k == null) return false;
                    return k.GetValue("Oz-translate") != null;
                }
            }
            catch { return false; }
        }

        static bool SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RUN_KEY, true)
                                       ?? Registry.CurrentUser.CreateSubKey(RUN_KEY))
                {
                    if (on) k.SetValue("Oz-translate", "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue("Oz-translate") != null) k.DeleteValue("Oz-translate");
                }
            }
            catch { return false; }
            return IsAutoStart() == on;
        }

        void ExitApp()
        {
            _exiting = true; _active = false; _generation++;
            _settingsForm.Dispose();
            try { _timer.Stop(); } catch { }
            try { if (_hookId != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hookId); } catch { }
            try { NativeMethods.UnregisterHotKey(_hotkey.Handle, HOTKEY_ID); } catch { }
            try { _hotkey.Destroy(); } catch { }
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            try { _popup.Dispose(); } catch { }
            ExitThread();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool createdNew;
            using (Mutex mtx = new Mutex(true, "OzTranslate_SingleInstance_4f1c9b2a", out createdNew))
            {
                if (!createdNew) return;
                try { NativeMethods.SetProcessDPIAware(); } catch { }
                try
                {
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12;
                }
                catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }
}
