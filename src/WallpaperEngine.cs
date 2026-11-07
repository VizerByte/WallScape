using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WallScape
{
    /// <summary>
    /// Plays the wallpaper transition on the WorkerW window (above the
    /// wallpaper, below the desktop icons) and then lets Windows apply the
    /// real wallpaper.
    /// </summary>
    internal class WallpaperEngine : IDisposable
    {
        private readonly Config _config;
        private readonly string _newPath;
        private readonly string _oldPath;
        private TransitionForm _form;
        private Bitmap _oldBitmap;
        private Bitmap _newBitmap;
        private bool _started;
        private bool _disposed;

        public event EventHandler Finished;

        public WallpaperEngine(Config config, string newPath, string oldPath)
        {
            _config = config;
            _newPath = newPath;
            _oldPath = oldPath;
        }

        public void Start()
        {
            if (_started)
                return;
            _started = true;

            if (_config.Animation == "none")
            {
                CommitImmediately();
                return;
            }

            try
            {
                Run();
            }
            catch
            {
                CommitImmediately();
            }
        }

        private void Run()
        {
            IntPtr worker = Native.FindWorkerW();
            if (worker == IntPtr.Zero)
            {
                CommitImmediately();
                return;
            }

            string oldPath = _oldPath;
            if (string.IsNullOrEmpty(oldPath))
                oldPath = WallpaperApi.GetCurrentWallpaperOrTranscoded();

            _oldBitmap = ImageUtil.LoadBitmap(oldPath);
            _newBitmap = ImageUtil.LoadBitmap(_newPath);
            if (_oldBitmap == null || _newBitmap == null)
            {
                CommitImmediately();
                return;
            }

            Native.RECT primary = Native.GetPrimaryMonitor();
            Rectangle target = Rectangle.FromLTRB(primary.Left, primary.Top, primary.Right, primary.Bottom);

            List<Rectangle> monitors = new List<Rectangle>();
            foreach (Native.RECT rect in Native.GetMonitors())
                monitors.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));

            WallpaperFit fit = Layout.ReadFitMode();
            Color background = Native.GetDesktopBackgroundColor();

            Bitmap oldScaled = Layout.Render(_oldBitmap, target, fit, monitors, background);
            Bitmap newScaled;
            try
            {
                newScaled = Layout.Render(_newBitmap, target, fit, monitors, background);
            }
            catch
            {
                oldScaled.Dispose();
                throw;
            }

            // The overlay only needs the screen-sized copies; release the
            // multi-megapixel sources before the animation starts.
            ReleaseSources();

            _form = new TransitionForm(worker, target, oldScaled, newScaled, _newPath, _config);
            _form.Finished += OnFormFinished;
            _form.ShowOverlay();
        }

        private void ReleaseSources()
        {
            if (_oldBitmap != null)
            {
                _oldBitmap.Dispose();
                _oldBitmap = null;
            }
            if (_newBitmap != null)
            {
                _newBitmap.Dispose();
                _newBitmap = null;
            }
        }

        private void OnFormFinished(object sender, EventArgs e)
        {
            _form = null;
            if (!_disposed && Finished != null)
                Finished(this, EventArgs.Empty);
        }

        private void CommitImmediately()
        {
            WallpaperApi.SetWallpaper(_newPath);
            if (!_disposed && Finished != null)
                Finished(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            TransitionForm form = _form;
            _form = null;
            if (form != null)
            {
                form.Finished -= OnFormFinished;
                try
                {
                    form.Close();
                }
                catch
                {
                }
                try
                {
                    form.Dispose();
                }
                catch
                {
                }
            }

            if (_oldBitmap != null)
            {
                _oldBitmap.Dispose();
                _oldBitmap = null;
            }
            if (_newBitmap != null)
            {
                _newBitmap.Dispose();
                _newBitmap = null;
            }
        }

        private class TransitionForm : Form
        {
            private readonly IntPtr _worker;
            private readonly Rectangle _target;
            private readonly Bitmap _oldImage;
            private readonly Bitmap _newImage;
            private readonly string _newPath;
            private readonly string _animation;
            private readonly int _duration;
            private readonly int _hold;
            private readonly Timer _timer;
            private readonly Stopwatch _clock;

            private float _progress;
            private bool _holding;
            private bool _committed;
            private bool _closed;
            private bool _cleaned;

            public event EventHandler Finished;

            public TransitionForm(IntPtr worker, Rectangle target, Bitmap oldImage, Bitmap newImage,
                string newPath, Config config)
            {
                _worker = worker;
                _target = target;
                _oldImage = oldImage;
                _newImage = newImage;
                _newPath = newPath;
                _animation = config.Animation;
                _duration = Math.Max(100, config.DurationMs);
                _hold = Math.Max(80, config.HoldMs);

                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Color.Black;
                DoubleBuffered = true;

                _timer = new Timer();
                _timer.Interval = 15;
                _timer.Tick += OnTick;
                _clock = new Stopwatch();
            }

            public void ShowOverlay()
            {
                IntPtr handle = Handle;

                Native.SetParent(handle, _worker);

                int style = unchecked((int)Native.GetWindowLongPtr(handle, Native.GWL_STYLE).ToInt64());
                style = (style | Native.WS_CHILD) & ~Native.WS_POPUP;
                Native.SetWindowLongPtr(handle, Native.GWL_STYLE, new IntPtr((long)(uint)style));

                Native.RECT workerRect;
                Native.GetWindowRect(_worker, out workerRect);

                int x = _target.Left - workerRect.Left;
                int y = _target.Top - workerRect.Top;

                Native.SetWindowPos(handle, IntPtr.Zero, x, y, _target.Width, _target.Height,
                    Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);
                Native.ShowWindow(handle, Native.SW_SHOWNOACTIVATE);

                Refresh();
                _clock.Start();
                _timer.Start();
            }

            private void OnTick(object sender, EventArgs e)
            {
                if (_holding)
                {
                    if (_clock.ElapsedMilliseconds >= _hold)
                    {
                        // Never tear down the form from inside its own timer tick.
                        _timer.Stop();
                        try
                        {
                            BeginInvoke(new MethodInvoker(CloseOverlay));
                        }
                        catch
                        {
                            CloseOverlay();
                        }
                    }
                    return;
                }

                double elapsed = _clock.Elapsed.TotalMilliseconds;
                float progress = (float)Math.Min(1.0, elapsed / _duration);
                if (progress < _progress)
                    progress = _progress;
                _progress = progress;

                if (progress >= 1f)
                {
                    Commit();
                    _holding = true;
                    _clock.Reset();
                    _clock.Start();
                    Invalidate();
                    return;
                }

                Invalidate();
            }

            private void Commit()
            {
                if (_committed)
                    return;
                _committed = true;
                WallpaperApi.SetWallpaper(_newPath);
            }

            private void CloseOverlay()
            {
                if (_closed)
                    return;
                _closed = true;
                _timer.Stop();
                try
                {
                    Hide();
                }
                catch
                {
                }
                try
                {
                    Close();
                }
                catch
                {
                }
                Cleanup();
                try
                {
                    Dispose();
                }
                catch
                {
                }
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                base.OnFormClosed(e);
                Cleanup();
            }

            private void Cleanup()
            {
                if (_cleaned)
                    return;
                _cleaned = true;

                try
                {
                    _timer.Stop();
                    _timer.Dispose();
                }
                catch
                {
                }

                try
                {
                    if (_oldImage != null)
                        _oldImage.Dispose();
                }
                catch
                {
                }

                try
                {
                    if (_newImage != null)
                        _newImage.Dispose();
                }
                catch
                {
                }

                EventHandler handler = Finished;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                // OnPaint covers the whole window; avoid painting the old frame twice.
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

                e.Graphics.DrawImageUnscaled(_oldImage, 0, 0);

                float eased = EaseOutQuad(_progress);
                if (_progress >= 1f)
                {
                    e.Graphics.DrawImageUnscaled(_newImage, 0, 0);
                    return;
                }

                using (GraphicsPath clip = BuildClip(eased))
                {
                    e.Graphics.SetClip(clip, CombineMode.Replace);
                    e.Graphics.DrawImageUnscaled(_newImage, 0, 0);
                }
            }

            private static float EaseOutQuad(float t)
            {
                return t * (2f - t);
            }

            private GraphicsPath BuildClip(float t)
            {
                int w = _target.Width;
                int h = _target.Height;
                float cx = w / 2f;
                float cy = h / 2f;
                GraphicsPath path = new GraphicsPath();

                switch (_animation)
                {
                    case "slide_top":
                    {
                        float radius = (float)Math.Sqrt(cx * cx + (float)h * h) * t;
                        if (radius < 1f)
                            radius = 1f;
                        path.AddEllipse(cx - radius, -radius, radius * 2f, radius * 2f);
                        break;
                    }
                    case "diamond":
                    {
                        float maxRadius = Math.Max(Math.Abs(cx) + Math.Abs(cy),
                            Math.Max(Math.Abs(w - cx) + Math.Abs(cy),
                                Math.Max(Math.Abs(cx) + Math.Abs(h - cy),
                                    Math.Abs(w - cx) + Math.Abs(h - cy))));
                        float radius = maxRadius * t;
                        if (radius < 1f)
                            radius = 1f;
                        PointF[] points =
                        {
                            new PointF(cx, cy - radius),
                            new PointF(cx + radius, cy),
                            new PointF(cx, cy + radius),
                            new PointF(cx - radius, cy)
                        };
                        path.AddPolygon(points);
                        break;
                    }
                    case "split":
                    {
                        float half = h / 2f * t;
                        if (half < 1f)
                            half = 1f;
                        path.AddRectangle(new RectangleF(0f, 0f, w, half));
                        path.AddRectangle(new RectangleF(0f, h - half, w, half));
                        break;
                    }
                    case "circle":
                    default:
                    {
                        float radius = (float)Math.Sqrt(cx * cx + cy * cy) * t;
                        if (radius < 1f)
                            radius = 1f;
                        path.AddEllipse(cx - radius, cy - radius, radius * 2f, radius * 2f);
                        break;
                    }
                }

                return path;
            }
        }
    }
}
