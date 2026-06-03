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
            if (string.IsNullOrEmpty(text)) return "";
            lock (_lock)
            {
                string hit;
                if (_cache.TryGetValue(text, out hit)) return hit;
            }

            string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=tr&dt=t&q="
                         + Uri.EscapeDataString(text);
            string json = _http.GetStringAsync(url).GetAwaiter().GetResult();

            StringBuilder sb = new StringBuilder();
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 8 * 1024 * 1024;
                object root = ser.DeserializeObject(json);
                object[] arr = root as object[];
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

            string result = sb.ToString().Trim();
            if (result.Length > 0)
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

            InMemoryRandomAccessStream ras = new InMemoryRandomAccessStream();
            DataWriter writer = new DataWriter(ras.GetOutputStreamAt(0));
            writer.WriteBytes(bytes);
            Await<uint>(writer.StoreAsync());
            Await<bool>(writer.FlushAsync());
            writer.DetachStream();
            ras.Seek(0);

            BitmapDecoder decoder = Await<BitmapDecoder>(BitmapDecoder.CreateAsync(ras));
            SoftwareBitmap sb = Await<SoftwareBitmap>(decoder.GetSoftwareBitmapAsync());
            return Await<OcrResult>(_engine.RecognizeAsync(sb));
        }
    }

    // Tıklamayı engellemeyen (click-through), odağı çalmayan, yarı saydam çeviri balonu.
    class PopupForm : Form
    {
        const int PAD = 12;
        const int GAP = 14;
        const int MAXW = 470;
        const int RADIUS = 12;

        readonly Font _font = new Font("Segoe UI", 12.5f, FontStyle.Regular);
        string _text = "";

        public PopupForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; // DPI ölçeklemesinin konumu kaydırmasını engelle
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0.96;
            BackColor = Color.FromArgb(34, 37, 43);
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
            _text = text == null ? "" : text;

            Size max = new Size(MAXW - 2 * PAD, 4096);
            Size sz = TextRenderer.MeasureText(_text, _font, max,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int w = Math.Min(MAXW, sz.Width + 2 * PAD + 2);
            int h = sz.Height + 2 * PAD + 2;
            if (w < 60) w = 60;
            ClientSize = new Size(w, h);

            using (GraphicsPath gp = Round(new Rectangle(0, 0, w, h), RADIUS))
                Region = new Region(gp);

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
            // WinForms/DPI bazen Location'ı kaydırır; konum+boyutu fiziksel piksellerle kesin uygula.
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, x, y, w, h, NativeMethods.SWP_NOACTIVATE);
            Invalidate();
            Logger.Log("ShowAt want=" + x + "," + y + " real=" + Bounds.X + "," + Bounds.Y + " size=" + w + "x" + h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rc = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (GraphicsPath gp = Round(rc, RADIUS))
            {
                using (Brush b = new SolidBrush(Color.FromArgb(34, 37, 43))) g.FillPath(b, gp);
                using (Pen pen = new Pen(Color.FromArgb(0, 150, 215), 1.4f)) g.DrawPath(pen, gp);
            }
            Rectangle txt = new Rectangle(PAD, PAD, ClientSize.Width - 2 * PAD, ClientSize.Height - 2 * PAD);
            TextRenderer.DrawText(g, _text, _font, txt, Color.FromArgb(240, 240, 242),
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
        int _busy = 0;

        public TrayContext()
        {
            _ocr = new OcrService();
            _popup = new PopupForm();

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
            _miAuto.Click += delegate { SetAutoStart(_miAuto.Checked); };
            menu.Items.Add(_miAuto);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem miExit = new ToolStripMenuItem("Çıkış");
            miExit.Click += delegate { ExitApp(); };
            menu.Items.Add(miExit);

            _tray = new NotifyIcon();
            _tray.Icon = _iconOn;
            _tray.Text = "Oz-translate (Aktif)";
            _tray.Visible = true;
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ToggleActive(); };

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

        void OnTick(object sender, EventArgs e)
        {
            if (!_active) return;
            Point p = Cursor.Position;
            int dx = p.X - _lastPos.X;
            int dy = p.Y - _lastPos.Y;
            if (dx * dx + dy * dy > MOVE_THRESHOLD * MOVE_THRESHOLD)
            {
                _lastPos = p;
                _stillTicks = 0;
                _pending = true;
                _generation++;       // devam eden işi geçersiz kıl
                HidePopup();
                return;
            }
            if (_pending)
            {
                _stillTicks++;
                if (_stillTicks >= DWELL_TICKS)
                {
                    _pending = false;
                    BeginTranslate(p, 5, _generation);
                }
            }
        }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _active && _popupVisible)
            {
                int msg = wParam.ToInt32();
                if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                {
                    NativeMethods.KBDLLHOOKSTRUCT k =
                        (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    if (k.vkCode == NativeMethods.VK_TAB)
                    {
                        Point p = Cursor.Position;
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
            if (!_ocr.Available) return;
            if (Interlocked.Exchange(ref _busy, 1) == 1 && lines < 15)
            {
                // Bir iş zaten sürüyor; normal taramayı atla (Tab her zaman geçsin).
                Interlocked.Exchange(ref _busy, 0);
                return;
            }
            _busy = 1;

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
                    string text = PickLines(ocr, left, top, p.X - left, p.Y - top, lines, SCALE, out block);
                    if (string.IsNullOrEmpty(text)) { Logger.Log("lines=" + lines + " OCR: (metin yok)"); PostHide(gen); return; }
                    Logger.Log("lines=" + lines + " EN=[" + text + "]");

                    string tr = Translator.Translate(text);
                    if (gen != _generation) return;
                    if (string.IsNullOrEmpty(tr)) tr = "[çeviri alınamadı]";
                    Logger.Log("        TR=[" + tr + "]");
                    PostShow(gen, tr, block, p);
                }
                catch { }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }

        static string PickLines(OcrResult ocr, int left, int top, int cursorX, int cursorY, int lines, int scale, out Rectangle block)
        {
            block = Rectangle.Empty;
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

            // imlecin bulunduğu satırı bul (dikeyde)
            int target = -1;
            for (int i = 0; i < idx.Length; i++)
            {
                double[] rw = rows[idx[i]];
                if (cursorY >= rw[0] - 4 && cursorY <= rw[1] + 4) { target = i; break; }
            }
            if (target < 0)
            {
                double best = double.MaxValue;
                for (int i = 0; i < idx.Length; i++)
                {
                    double d = Math.Abs(rows[idx[i]][4] - cursorY);
                    if (d < best) { best = d; target = i; }
                }
            }

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
            string text = Normalize(sb.ToString().Trim());
            if (text.Length == 0) return "";
            block = Rectangle.FromLTRB(left + (int)minL, top + (int)minT, left + (int)maxR, top + (int)maxB);
            return text;
        }

        // Türkçe OCR motorunun İngilizce metinde ürettiği noktalı İ / noktasız ı'yı düzelt.
        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace('İ', 'I').Replace('ı', 'i');
        }

        void PostShow(int gen, string text, Rectangle block, Point cursor)
        {
            if (_popup.IsDisposed) return;
            try
            {
                _popup.BeginInvoke((Action)delegate
                {
                    if (gen != _generation || !_active) return;
                    _popup.ShowAt(text, block, cursor);
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
            if (_popupVisible)
            {
                _popup.Hide();
                _popupVisible = false;
            }
        }

        void ToggleActive()
        {
            _active = !_active;
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

        static void SetAutoStart(bool on)
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
            catch { }
        }

        void ExitApp()
        {
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
                        SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                }
                catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }
}
