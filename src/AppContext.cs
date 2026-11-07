using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WallScape
{
    internal class AppContext : ApplicationContext
    {
        private readonly Config _config;
        private readonly NotifyIcon _tray;
        private readonly HotkeyWindow _hotkey;
        private readonly uint _showMessage;
        private readonly Random _random = new Random();
        private readonly Timer _firstRunTimer;

        private GalleryForm _gallery;
        private WallpaperEngine _engine;
        private Icon _icon;
        private IntPtr _iconHandle = IntPtr.Zero;
        private bool _hotkeyOk;
        private bool _cleaned;

        public AppContext()
        {
            _config = Config.Load();
            _showMessage = Native.RegisterWindowMessage("WallScape.ShowGallery");

            // Portable first run: leave an editable ini next to the exe.
            if (!File.Exists(Config.ConfigPath))
                _config.Save();

            _icon = CreateIcon();
            _tray = new NotifyIcon();
            _tray.Icon = _icon;
            _tray.Text = "WallScape";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ToggleGallery(); };

            BuildMenu();

            _hotkey = new HotkeyWindow(this);
            RegisterHotkey();

            _firstRunTimer = new Timer();
            _firstRunTimer.Interval = 500;
            _firstRunTimer.Tick += OnFirstRunTick;
            _firstRunTimer.Start();
        }

        public uint ShowMessage
        {
            get { return _showMessage; }
        }

        private void OnFirstRunTick(object sender, EventArgs e)
        {
            _firstRunTimer.Stop();
            _firstRunTimer.Dispose();

            if (_config.Folders.Count == 0)
            {
                ShowBalloon("WallScape", "Pick the folder(s) that hold your wallpapers to get started.");
                OpenSettings();
            }
        }

        // ---- tray menu -----------------------------------------------------

        private void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;

            ToolStripMenuItem open = new ToolStripMenuItem("Open wallpaper gallery");
            open.Font = new Font(open.Font, FontStyle.Bold);
            open.ShortcutKeyDisplayString = _config.Hotkey;
            open.Click += delegate { ToggleGallery(); };
            menu.Items.Add(open);

            ToolStripMenuItem random = new ToolStripMenuItem("Change wallpaper (random)");
            random.Click += delegate { ChangeRandom(); };
            menu.Items.Add(random);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem folders = new ToolStripMenuItem("Wallpaper folders\u2026");
            folders.Click += delegate { OpenSettings(); };
            menu.Items.Add(folders);

            ToolStripMenuItem animation = new ToolStripMenuItem("Animation");
            AddAnimationItem(animation, "Circle", "circle");
            AddAnimationItem(animation, "Slide from top", "slide_top");
            AddAnimationItem(animation, "Diamond", "diamond");
            AddAnimationItem(animation, "Split", "split");
            AddAnimationItem(animation, "None", "none");
            menu.Items.Add(animation);

            ToolStripMenuItem settings = new ToolStripMenuItem("Settings\u2026");
            settings.Click += delegate { OpenSettings(); };
            menu.Items.Add(settings);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem startup = new ToolStripMenuItem("Start with Windows");
            startup.Checked = _config.AutoStart;
            startup.Click += delegate
            {
                _config.AutoStart = !_config.AutoStart;
                _config.Save();
                ApplyAutostart();
                RebuildMenuSoon();
            };
            menu.Items.Add(startup);

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { Exit(); };
            menu.Items.Add(exit);

            ContextMenuStrip old = _tray.ContextMenuStrip;
            _tray.ContextMenuStrip = menu;
            if (old != null)
                old.Dispose();
        }

        private void AddAnimationItem(ToolStripMenuItem parent, string label, string value)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Checked = _config.Animation == value;
            item.Click += delegate
            {
                _config.Animation = value;
                _config.Save();
                RebuildMenuSoon();
            };
            parent.DropDownItems.Add(item);
        }

        /// <summary>
        /// Rebuilding from inside a click handler would dispose the menu that
        /// is still dispatching the event, so defer it one message.
        /// </summary>
        private void RebuildMenuSoon()
        {
            Timer timer = new Timer();
            timer.Interval = 50;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                BuildMenu();
            };
            timer.Start();
        }

        // ---- hotkey / gallery ---------------------------------------------

        private void RegisterHotkey()
        {
            try
            {
                Native.UnregisterHotKey(_hotkey.Handle, 1);
            }
            catch
            {
            }

            uint modifiers;
            uint virtualKey;
            if (!Hotkey.TryParse(_config.Hotkey, out modifiers, out virtualKey))
            {
                _hotkeyOk = false;
                ShowBalloon("WallScape", "The hotkey \"" + _config.Hotkey + "\" is not valid. Open Settings to change it.");
                return;
            }

            _hotkeyOk = Native.RegisterHotKey(_hotkey.Handle, 1, modifiers, virtualKey);
            if (!_hotkeyOk)
            {
                ShowBalloon("WallScape",
                    "Could not register " + _config.Hotkey + ". Another program is probably using it; pick another hotkey in Settings.");
            }
        }

        private void ToggleGallery()
        {
            if (_gallery != null)
            {
                if (!_gallery.IsClosing)
                    _gallery.CloseGallery();
                return;
            }

            if (_engine != null)
                return;

            GalleryForm form = new GalleryForm(_config, OnWallpaperChosen);
            form.GalleryClosed += delegate
            {
                if (object.ReferenceEquals(_gallery, form))
                    _gallery = null;
            };
            _gallery = form;
            form.Show();
        }

        private void OnWallpaperChosen(string path)
        {
            if (_engine != null)
            {
                _engine.Dispose();
                _engine = null;
            }

            string oldPath = WallpaperApi.GetCurrentWallpaperOrTranscoded();
            WallpaperEngine engine = new WallpaperEngine(_config, path, oldPath);
            engine.Finished += delegate
            {
                if (object.ReferenceEquals(_engine, engine))
                {
                    engine.Dispose();
                    _engine = null;
                }
            };
            _engine = engine;
            engine.Start();
        }

        private void ChangeRandom()
        {
            string[] files = Scanner.Find(_config.Folders);
            if (files.Length == 0)
            {
                ShowBalloon("WallScape", "No wallpapers found. Add a folder first.");
                return;
            }

            string current = WallpaperApi.GetCurrentWallpaper();
            string pick = files[_random.Next(files.Length)];
            if (files.Length > 1 && current != null)
            {
                int guard = 0;
                while (string.Equals(pick, current, StringComparison.OrdinalIgnoreCase) && guard++ < 8)
                    pick = files[_random.Next(files.Length)];
            }
            OnWallpaperChosen(pick);
        }

        // ---- settings ------------------------------------------------------

        private void OpenSettings()
        {
            if (_gallery != null)
            {
                try
                {
                    _gallery.ForceClose();
                }
                catch
                {
                }
                _gallery = null;
            }

            using (SettingsForm form = new SettingsForm(_config, _icon))
            {
                if (form.ShowDialog() != DialogResult.OK)
                    return;

                form.ApplyTo(_config);
                _config.Save();
                ApplyAutostart();
                RegisterHotkey();
                BuildMenu();
            }
        }

        private void ApplyAutostart()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (run == null)
                        return;
                    if (_config.AutoStart)
                        run.SetValue("WallScape", "\"" + Application.ExecutablePath + "\"");
                    else
                        run.DeleteValue("WallScape", false);
                }
            }
            catch
            {
            }
        }

        // ---- lifetime ------------------------------------------------------

        private void ShowBalloon(string title, string text)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.ShowBalloonTip(5000);
            }
            catch
            {
            }
        }

        public void Exit()
        {
            if (_cleaned)
                return;

            if (_gallery != null)
            {
                try
                {
                    _gallery.ForceClose();
                }
                catch
                {
                }
                _gallery = null;
            }

            if (_engine != null)
            {
                try
                {
                    _engine.Dispose();
                }
                catch
                {
                }
                _engine = null;
            }

            Cleanup();
            ExitThread();
        }

        private void Cleanup()
        {
            if (_cleaned)
                return;
            _cleaned = true;

            try
            {
                if (_firstRunTimer != null)
                {
                    _firstRunTimer.Stop();
                    _firstRunTimer.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (_hotkey != null)
                {
                    Native.UnregisterHotKey(_hotkey.Handle, 1);
                    _hotkey.DestroyHandle();
                }
            }
            catch
            {
            }

            try
            {
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                if (_icon != null)
                    _icon.Dispose();
            }
            catch
            {
            }

            if (_iconHandle != IntPtr.Zero)
            {
                Native.DestroyIcon(_iconHandle);
                _iconHandle = IntPtr.Zero;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Cleanup();
            base.Dispose(disposing);
        }

        // ---- icon ----------------------------------------------------------

        private Icon CreateIcon()
        {
            Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                RectangleF box = new RectangleF(1f, 1f, 30f, 30f);
                using (GraphicsPath path = RoundedRect(box, 8f))
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Rectangle(0, 0, 32, 32), Color.FromArgb(0x1D, 0x3B, 0x7A),
                        Color.FromArgb(0x0B, 0x13, 0x2B), 55f))
                    {
                        g.FillPath(brush, path);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(90, 0x89, 0xB4, 0xFA), 1f))
                        g.DrawPath(pen, path);
                }

                using (SolidBrush sun = new SolidBrush(Color.FromArgb(0xF9, 0xE2, 0xAF)))
                    g.FillEllipse(sun, 20f, 6f, 6.5f, 6.5f);

                PointF[] mountains =
                {
                    new PointF(2f, 27f),
                    new PointF(11f, 15f),
                    new PointF(17f, 22f),
                    new PointF(22f, 17f),
                    new PointF(30f, 27f)
                };
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(0x89, 0xB4, 0xFA)))
                    g.FillPolygon(brush, mountains);
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(0x4C, 0x6F, 0xB8)))
                    g.FillPolygon(brush, new PointF[]
                    {
                        new PointF(2f, 27f),
                        new PointF(11f, 15f),
                        new PointF(14f, 19f),
                        new PointF(8f, 27f)
                    });
            }

            _iconHandle = bitmap.GetHicon();
            bitmap.Dispose();
            return Icon.FromHandle(_iconHandle);
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2f;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public bool HotkeyRegistered
        {
            get { return _hotkeyOk; }
        }

        // ---- hidden message window ----------------------------------------

        private class HotkeyWindow : NativeWindow
        {
            private readonly AppContext _app;

            public HotkeyWindow(AppContext app)
            {
                _app = app;
                CreateParams parameters = new CreateParams();
                parameters.Caption = "WallScape.Hotkey";
                CreateHandle(parameters);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_HOTKEY)
                {
                    _app.ToggleGallery();
                    return;
                }
                if ((uint)m.Msg == _app.ShowMessage)
                {
                    _app.ToggleGallery();
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
