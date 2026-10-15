using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using OzTranslate;

static class RegressionTests
{
    static int count;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        count++; Console.WriteLine("PASS: " + name);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Check(!TextPolicy.HasText(null), "null input");
            Check(!TextPolicy.HasText("  \r\n"), "blank input");
            Check(!TextPolicy.HasText("123 ... %"), "numbers and punctuation");
            Check(TextPolicy.HasText("Open settings"), "English input");
            Check(TextPolicy.DisplayTranslation("Bu bir Türkçe metindir.", "This is text", "tr") == "", "detected Turkish suppressed");
            Check(TextPolicy.DisplayTranslation("ayarlar penceresini ac", "ayarları aç", "tr-TR") == "", "ASCII Turkish suppressed");
            Check(TextPolicy.DisplayTranslation("Open settings", "Ayarları aç", "en") == "Ayarları aç", "English translation retained");
            Check(TextPolicy.DisplayTranslation("Open", "Aç", "") == "", "missing language suppressed");
            Check(TextPolicy.DisplayTranslation("Hello", "", "en") == "", "empty translation suppressed");
            Check(TextPolicy.DisplayTranslation("123", "123", "en") == "", "no translatable text");
            Check(TextPolicy.DisplayTranslation("Microsoft!", " microsoft ", "en") == "", "identical proper name suppressed");
            Check(TextPolicy.DisplayTranslation("Hello, WORLD!", "hello world", "en") == "", "case punctuation whitespace ignored");
            Check(TextPolicy.DisplayTranslation("Ｆｉｌｅ", "File", "en") == "", "Unicode width ignored");
            Check(TextPolicy.DisplayTranslation("İstanbul", "istanbul", "en") == "", "OCR dotted I comparison");
            Check(TextPolicy.DisplayTranslation("Settings", "...", "en") == "", "punctuation-only result");

            AppSettings settings = new AppSettings();
            Check(settings.FontSize == 12.5f && settings.OpacityPercent == 96, "defaults");
            settings.FontSize = float.NaN; settings.OpacityPercent = -1; settings.Validate();
            Check(settings.FontSize == 12.5f && settings.OpacityPercent == 30, "invalid settings clamped");
            settings.FontSize = 100; settings.OpacityPercent = 999; settings.Validate();
            Check(settings.FontSize == 28 && settings.OpacityPercent == 100, "upper limits");
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            AppSettings restored = serializer.Deserialize<AppSettings>(serializer.Serialize(settings));
            Check(restored.FontSize == settings.FontSize && restored.Background == settings.Background, "settings JSON roundtrip");

            bool startup = false;
            using (SettingsForm form = new SettingsForm(new AppSettings(), delegate { return startup; },
                delegate(bool value) { startup = value; return true; }, delegate(AppSettings value) { }))
            {
                form.CreateControl();
                if (args.Length > 1 && args[1] == "True")
                {
                    form.Show();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(args[0], ImageFormat.Png);
                    }
                    form.Close();
                    Check(!form.Visible && !form.IsDisposed, "visible settings close hides");
                    form.Show();
                    Check(form.Visible, "same settings form reopens");
                }
                FormClosingEventArgs closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
                typeof(SettingsForm).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { closing });
                Check(closing.Cancel && !form.IsDisposed, "X hides reusable settings window");
            }
            using (PopupForm popup = new PopupForm())
            {
                popup.ApplySettings(new AppSettings { OpacityPercent = 70, FontSize = 18, Background = Color.Navy.ToArgb() });
                Check(Math.Abs(popup.Opacity - 0.7) < 0.01 && popup.BackColor == Color.FromArgb(Color.Navy.ToArgb()), "appearance applied");
                popup.ShowAt(" ", Rectangle.Empty, Point.Empty);
                Check(!popup.Visible, "blank popup cannot show");
            }

            OcrService ocr = new OcrService();
            Check(ocr.Available, "Windows OCR available");
            using (Bitmap image = new Bitmap(1100, 300))
            using (Graphics graphics = Graphics.FromImage(image))
            using (Font font = new Font("Arial", 28))
            {
                graphics.Clear(Color.White);
                graphics.DrawString("Open the settings window", font, Brushes.Black, 40, 40);
                var result = ocr.Recognize(image);
                Check(result != null && result.Lines.Count > 0, "real Windows OCR recognizes synthetic text");
                MethodInfo pick = typeof(TrayContext).GetMethod("PickLines", BindingFlags.Static | BindingFlags.NonPublic);
                object[] blank = { result, 0, 0, 1000, 250, 5, 1, Rectangle.Empty, "" };
                Check((string)pick.Invoke(null, blank) == "", "blank area does not select nearby text");
                var word = result.Lines[0].Words[0].BoundingRect;
                object[] hit = { result, 0, 0, (int)(word.X + word.Width / 2), (int)(word.Y + word.Height / 2), 5, 1, Rectangle.Empty, "" };
                Check(TextPolicy.HasText((string)pick.Invoke(null, hit)), "text under cursor selected");
                Check((string)hit[8] == result.Lines[0].Text, "cursor line retained for independent language detection");
                hit[5] = 15;
                Check(TextPolicy.HasText((string)pick.Invoke(null, hit)), "Tab 15-line selection");
            }
            if (args.Length > 1 && args[1] == "True")
            {
                TrayContext context = new TrayContext();
                Type type = typeof(TrayContext);
                BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
                try
                {
                    MethodInfo open = type.GetMethod("ShowSettings", fields);
                    open.Invoke(context, null);
                    SettingsForm form = (SettingsForm)type.GetField("_settingsForm", fields).GetValue(context);
                    open.Invoke(context, null);
                    Check(form.Visible && object.ReferenceEquals(form, type.GetField("_settingsForm", fields).GetValue(context)), "tray opens one settings instance");
                    form.Close();
                    HotkeyWindow hotkey = (HotkeyWindow)type.GetField("_hotkey", fields).GetValue(context);
                    MethodInfo dispatch = typeof(HotkeyWindow).GetMethod("WndProc", fields);
                    object[] message = { Message.Create(hotkey.Handle, NativeMethods.WM_HOTKEY, new IntPtr(1), IntPtr.Zero) };
                    dispatch.Invoke(hotkey, message);
                    Check(!(bool)type.GetField("_active", fields).GetValue(context), "Ctrl Alt Z dispatch disables translation");
                    dispatch.Invoke(hotkey, message);
                    Check((bool)type.GetField("_active", fields).GetValue(context), "Ctrl Alt Z dispatch enables translation");
                    type.GetField("_popupVisible", fields).SetValue(context, true);
                    type.GetField("_busy", fields).SetValue(context, 1);
                    IntPtr key = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.KBDLLHOOKSTRUCT)));
                    try
                    {
                        NativeMethods.KBDLLHOOKSTRUCT data = new NativeMethods.KBDLLHOOKSTRUCT(); data.vkCode = NativeMethods.VK_TAB;
                        System.Runtime.InteropServices.Marshal.StructureToPtr(data, key, false);
                        object value = type.GetMethod("HookProc", fields).Invoke(context, new object[] { 0, new IntPtr(NativeMethods.WM_KEYDOWN), key });
                        Check((IntPtr)value == new IntPtr(1) && (int)type.GetField("_pendingLines", fields).GetValue(context) == 15,
                            "Tab consumes key and preserves 15-line request while worker busy");
                    }
                    finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(key); }
                    PopupForm popup = (PopupForm)type.GetField("_popup", fields).GetValue(context);
                    popup.ShowAt("deneme çevirisi", new Rectangle(100, 100, 200, 80), new Point(150, 140));
                    Form handle = (Form)typeof(PopupForm).GetField("_handle", fields).GetValue(popup);
                    Check(popup.Visible && handle.Visible, "popup and drag handle visible together");
                    MethodInfo mouseDown = typeof(Control).GetMethod("OnMouseDown", fields);
                    mouseDown.Invoke(handle, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 30, 8, 0) });
                    Check(popup.Dragging, "drag starts on handle");
                    Point before = popup.Location;
                    typeof(PopupForm).GetField("_dragCursor", fields).SetValue(popup, new Point(Cursor.Position.X - 40, Cursor.Position.Y - 30));
                    typeof(Control).GetMethod("OnMouseMove", fields).Invoke(handle, new object[] { new MouseEventArgs(MouseButtons.Left, 0, 30, 8, 0) });
                    Check(popup.Location != before, "drag moves popup");
                    typeof(Control).GetMethod("OnMouseUp", fields).Invoke(handle, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 30, 8, 0) });
                    Check(!popup.Dragging, "drag ends on release");
                    popup.Hide();
                    Check(!handle.Visible, "handle hides with popup");
                }
                finally { type.GetMethod("ExitApp", fields).Invoke(context, null); }
            }
            if (args.Length > 2 && args[2] == "True")
            {
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                Check(TextPolicy.HasText(Translator.Translate("Open the settings window to change the text size.")), "live English translation");
                Check(Translator.Translate("Bu programın ayarlarını değiştirmek istiyorum.") == "", "live Turkish suppressed");
                Check(Translator.Translate("bu programin ayarlarini degistirmek istiyorum") == "", "live ASCII Turkish suppressed");
                Check(Translator.Translate("Microsoft") == "", "live identical proper name suppressed");
            }
            Console.WriteLine("SUCCESS: " + count + " checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
