using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WallScape
{
    /// <summary>
    /// Full-screen wallpaper picker. Uses UpdateLayeredWindow so the row of
    /// thumbnails can float over the live desktop with a per-pixel alpha dim.
    /// </summary>
    internal class GalleryForm : Form
    {
        private const int FadeInMs = 80;
        private const int FadeOutMs = 120;
        private const int SlideMs = 180;
        private const int SlideFastMs = 260;
        private const int VisiblePadding = 7;
        private const int PanIdleMs = 110;

        private static readonly Semaphore ThumbGate = new Semaphore(2, 2);

        private readonly Config _config;
        private readonly Action<string> _apply;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly Dictionary<int, Bitmap> _thumbs = new Dictionary<int, Bitmap>();
        private readonly Dictionary<int, Bitmap> _strips = new Dictionary<int, Bitmap>();
        private readonly HashSet<int> _pending = new HashSet<int>();

        private string[] _files;
        private bool _scanning;
        private bool _scanStarted;
        private int _index;
        private int _hover = -1;

        private float _scroll;
        private float _scrollFrom;
        private float _scrollTo;
        private long _scrollStart;
        private int _scrollDuration;

        // touchpad / wheel panning: chase a continuous target, snap on idle
        private float _panTarget;
        private long _panWheelAt = long.MinValue / 4;
        private bool _panActive;

        private float _fade;
        private float _fadeFrom;
        private float _fadeTo;
        private long _fadeStart;
        private int _fadeDuration;
        private bool _fadeClose;

        private bool _closed;
        private bool _closeStarted;
        private string _applyOnClose;

        private Bitmap _canvas;
        private Rectangle _screen;
        private float _scale = 1f;
        private float _cardW;
        private float _cardH;
        private float _step;
        private float _radius;
        private float _centerX;
        private float _centerY;
        private int _thumbW;
        private int _thumbH;
        private Color _accent;
        private Font _font;
        private Font _smallFont;
        private long _ignoreDeactivateUntil;
        private bool _menuOpen;

        // YASB-like parallelogram strip mode
        private bool _strip;
        private float _lean;
        private const int StripPad = 2; // bake padding so AA edges are not clipped

        public event EventHandler GalleryClosed;

        public GalleryForm(Config config, Action<string> apply)
        {
            _config = config;
            _apply = apply;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            _timer.Interval = 15;
            _timer.Tick += OnTick;
            _accent = ResolveAccent(config.Accent);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public bool IsClosing
        {
            get { return _closeStarted; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            Native.RECT primary = Native.GetPrimaryMonitor();
            _screen = Rectangle.FromLTRB(primary.Left, primary.Top, primary.Right, primary.Bottom);

            Native.SetWindowPos(Handle, IntPtr.Zero, _screen.Left, _screen.Top, _screen.Width, _screen.Height,
                Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);

            using (Graphics g = CreateGraphics())
            {
                _scale = g.DpiX / 96f;
            }
            if (_scale < 0.75f) _scale = 0.75f;
            if (_scale > 4f) _scale = 4f;

            RebuildMetrics();
        }

        private void RebuildMetrics()
        {
            // Whole-pixel card sizes let the thumbnails blit 1:1 instead of
            // being resampled on every animation frame.
            _cardW = Math.Max(80f, (float)Math.Round(_config.GalleryWidth * _scale));
            float aspect = _config.GalleryOrientation == "portrait" ? 16f / 9f : 9f / 16f;
            _cardH = Math.Max(1f, (float)Math.Floor(_cardW * aspect));

            _strip = string.Equals(_config.GalleryStyle, "strip", StringComparison.OrdinalIgnoreCase);
            if (_strip)
            {
                // Frames are cut diagonally and packed edge to edge: the lean
                // shifts each top edge sideways across the card height so a
                // frame's cut is exactly its neighbour's opposite cut.
                float autoLean = Math.Max(10f, Math.Min(96f, _cardH * 0.22f));
                _lean = _config.GalleryLean > 0
                    ? Math.Min(220f, _config.GalleryLean * _scale)
                    : (float)Math.Round(autoLean);
                _step = _cardW;
            }
            else
            {
                _lean = 0f;
                _step = _cardW + _config.GalleryGap * _scale;
            }
            _radius = _config.GalleryCornerRadius * _scale;
            _centerX = _screen.Width / 2f;
            _centerY = _screen.Height * 0.48f;

            DisposeStrips();
            _thumbW = Math.Max(1, (int)_cardW);
            _thumbH = Math.Max(1, (int)_cardH);

            if (_font != null) _font.Dispose();
            if (_smallFont != null) _smallFont.Dispose();
            _font = new Font("Segoe UI", 11f * _scale, FontStyle.Regular, GraphicsUnit.Point);
            _smallFont = new Font("Segoe UI", 9f * _scale, FontStyle.Regular, GraphicsUnit.Point);

            RebuildCanvas();
        }

        private void RebuildCanvas()
        {
            if (_canvas != null)
            {
                _canvas.Dispose();
                _canvas = null;
            }
            if (_screen.Width > 0 && _screen.Height > 0)
            {
                _canvas = new Bitmap(_screen.Width, _screen.Height, PixelFormat.Format32bppPArgb);
                _canvas.SetResolution(96f, 96f);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            _clock.Start();
            _ignoreDeactivateUntil = _clock.ElapsedMilliseconds + 350;

            Activate();
            Native.ForceForeground(Handle);

            _fade = 0f;
            Render();
            BeginFade(0f, 1f, FadeInMs);
            BeginScan();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_menuOpen)
                return;
            if (_clock.ElapsedMilliseconds < _ignoreDeactivateUntil)
                return;
            if (_closeStarted)
                return;
            CloseGallery();
        }

        // ---- scanning ------------------------------------------------------

        private void BeginScan()
        {
            if (_scanStarted)
                return;
            _scanStarted = true;
            _scanning = true;
            Render();

            // Snapshot the folder list on the UI thread: the background scan
            // must not enumerate a list the settings dialog could replace.
            string[] folders = _config.Folders.ToArray();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] files = Scanner.Find(folders);
                try
                {
                    BeginInvoke(new MethodInvoker(delegate { OnScanFinished(files); }));
                }
                catch
                {
                }
            });
        }

        private void OnScanFinished(string[] files)
        {
            if (_closed)
                return;

            _scanning = false;
            _files = files;

            string current = WallpaperApi.GetCurrentWallpaper();
            int start = 0;
            if (current != null)
            {
                string wanted = NormalizePath(current);
                for (int i = 0; i < files.Length; i++)
                {
                    if (string.Equals(NormalizePath(files[i]), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        start = i;
                        break;
                    }
                }
            }

            _index = start;
            _scroll = start;
            _scrollTo = start;
            _scrollDuration = 0;

            RequestThumbs();
            Render();

            ThreadPool.QueueUserWorkItem(delegate
            {
                ThumbnailCache.Prune(_config.CacheMaxMb, _config.CacheDays);
            });
        }

        private static string NormalizePath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private void RequestThumbs()
        {
            int count = Count;
            if (count == 0)
                return;

            // Only the cards near the screen can ever be seen; prefetch one
            // extra on each side so a scroll starts with artwork ready.
            int reach = (int)Math.Ceiling(VisibleLimit) + 1;
            for (int distance = -reach; distance <= reach; distance++)
            {
                int index = Wraps ? Mod(_index + distance, count) : _index + distance;
                if (index >= 0 && index < count)
                    RequestThumb(index);
            }
        }

        // ---- circular row ---------------------------------------------------

        private int Count
        {
            get { return _files == null ? 0 : _files.Length; }
        }

        /// <summary>
        /// YASB's default row joins the ends once there are enough images to
        /// fill the space on both sides of the selection.
        /// </summary>
        private bool Wraps
        {
            get { return Count >= 3; }
        }

        /// <summary>
        /// Largest card offset that can still touch the screen. Anything
        /// further away is fully clipped, so drawing and thumbnailing it is
        /// wasted work. Computed from the real screen so small displays cull
        /// more aggressively.
        /// </summary>
        private float VisibleLimit
        {
            get
            {
                float half = _screen.Width / 2f + _cardW / 2f;
                return Math.Max(1f, half / Math.Max(1f, _step)) + 0.01f;
            }
        }

        private static int Mod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static float Mod(float value, float divisor)
        {
            float result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        /// <summary>
        /// Signed offset from the animated scroll position to a card, choosing
        /// the nearest copy of the image when the row wraps.
        /// </summary>
        private float RelativeDistance(int index)
        {
            float distance = index - _scroll;
            int count = Count;
            if (Wraps && count > 1)
                distance = Mod(distance + count / 2f, count) - count / 2f;
            return distance;
        }

        private void NormalizeScroll()
        {
            int count = Count;
            if (!Wraps || count == 0)
                return;
            _scroll = Mod(_scroll, (float)count);
            _scrollTo = _scroll;
        }

        /// <summary>
        /// Cards currently on screen as (image index, signed distance), furthest
        /// first so the selected card paints on top.
        /// </summary>
        private List<KeyValuePair<int, float>> CollectVisible()
        {
            List<KeyValuePair<int, float>> cards = new List<KeyValuePair<int, float>>();
            int count = Count;
            if (count == 0)
                return cards;

            if (!Wraps)
            {
                int limit = (int)Math.Ceiling(VisibleLimit);
                int first = Math.Max(0, (int)Math.Floor(_scroll) - limit);
                int last = Math.Min(count - 1, (int)Math.Ceiling(_scroll) + limit);
                for (int i = first; i <= last; i++)
                    cards.Add(new KeyValuePair<int, float>(i, i - _scroll));
                return cards;
            }

            float limitFloat = VisibleLimit;
            int early = (int)Math.Floor(limitFloat) + 1;
            int centre = (int)Math.Floor(_scroll + 0.5f);
            Dictionary<int, float> nearest = new Dictionary<int, float>();
            for (int step = centre - early; step <= centre + early; step++)
            {
                int index = Mod(step, count);
                float distance = RelativeDistance(index);
                if (Math.Abs(distance) > limitFloat)
                    continue;

                float existing;
                if (!nearest.TryGetValue(index, out existing) || Math.Abs(distance) < Math.Abs(existing))
                    nearest[index] = distance;
            }

            foreach (KeyValuePair<int, float> pair in nearest)
                cards.Add(pair);

            cards.Sort(delegate(KeyValuePair<int, float> a, KeyValuePair<int, float> b)
            {
                return Math.Abs(b.Value).CompareTo(Math.Abs(a.Value));
            });
            return cards;
        }

        private void RequestThumb(int index)
        {
            if (_files == null || index < 0 || index >= _files.Length)
                return;
            if (_thumbs.ContainsKey(index) || _pending.Contains(index))
                return;

            _pending.Add(index);
            string path = _files[index];
            int width = _thumbW;
            int height = _thumbH;

            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap thumb = null;
                ThumbGate.WaitOne();
                try
                {
                    thumb = ThumbnailCache.Get(path, width, height);
                }
                finally
                {
                    ThumbGate.Release();
                }

                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        _pending.Remove(index);
                        if (thumb != null && !_closed)
                        {
                            _thumbs[index] = thumb;
                            if (_strip)
                                BakeStripFor(index);
                            Render();
                        }
                        else if (thumb != null)
                        {
                            thumb.Dispose();
                        }
                    }));
                }
                catch
                {
                    if (thumb != null)
                        thumb.Dispose();
                }
            });
        }

        // ---- selection / input ---------------------------------------------

        private long Now
        {
            get { return _clock.ElapsedMilliseconds; }
        }

        private void Select(int index)
        {
            int count = Count;
            if (count == 0)
                return;

            int target;
            if (Wraps)
            {
                target = Mod(index, count);
            }
            else
            {
                if (index < 0) index = 0;
                if (index > count - 1) index = count - 1;
                target = index;
            }
            if (target == _index)
                return;

            _panActive = false;

            // Keep the scroll position continuous so a wrap moves one card,
            // not the whole row: the next copy of the target may be negative.
            float wanted = Wraps ? _scroll + RelativeDistance(target) : target;

            _index = target;
            _scrollFrom = _scroll;
            _scrollTo = wanted;
            _scrollStart = Now;
            _scrollDuration = Math.Abs(wanted - _scroll) > 2f ? SlideFastMs : SlideMs;

            RequestThumbs();
            StartAnim();
        }

        private void MoveBy(int delta)
        {
            if (_files == null)
                return;
            Select(_index + delta);
        }

        /// <summary>
        /// Finishes a pan: pick the card nearest to where the row stopped and
        /// glide the exact remainder so the strip settles centered on it.
        /// </summary>
        private void SnapToNearest()
        {
            int count = Count;
            if (_files == null || count == 0)
                return;

            int target = (int)Math.Round(_panTarget);
            if (Wraps)
                target = Mod(target, count);
            else if (target < 0)
                target = 0;
            else if (target > count - 1)
                target = count - 1;

            if (target != _index)
            {
                Select(target);
                return;
            }

            float wanted = Wraps ? _scroll + RelativeDistance(target) : target;
            if (Math.Abs(wanted - _scroll) > 0.02f)
            {
                _scrollFrom = _scroll;
                _scrollTo = wanted;
                _scrollStart = Now;
                _scrollDuration = SlideMs;
            }
            else
            {
                _scroll = wanted;
                _scrollTo = wanted;
                _scrollDuration = 0;
            }
            RequestThumbs();
        }

        private int PageStep()
        {
            float half = _screen.Width / 2f - _cardW / 2f;
            int step = (int)(half / Math.Max(1f, _step));
            return Math.Max(1, step);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                Render();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Cursor = Cursors.Default;
                Render();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // Two-finger trackpad scroll arrives as many small wheel events:
            // pan the row continuously and let the tick snap onto a card.
            PanBy(-(float)e.Delta / 120f);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEHWHEEL)
            {
                // two-finger sideways swipe
                short delta = (short)((long)m.WParam >> 16);
                PanBy(delta / 120f);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>
        /// Starts/continues a touchpad-style pan of the row by the given
        /// number of cards. The tick chases the target smoothly and snaps the
        /// selection to the nearest card once the wheel goes quiet.
        /// </summary>
        private void PanBy(float cards)
        {
            if (_files == null || Count == 0)
                return;

            _panActive = true;
            if (_scrollDuration != 0)
            {
                // cancel any running slide so both motions never fight
                _scrollTo = _scroll;
                _scrollDuration = 0;
            }
            _panTarget += cards;
            if (!Wraps)
            {
                int count = Count;
                if (_panTarget < 0) _panTarget = 0;
                if (_panTarget > count - 1) _panTarget = count - 1;
            }
            _panWheelAt = Now;
            StartAnim();
        }

        /// <summary>
        /// While panning, the selection follows the row continuously instead of
        /// only at the end, so the accent frame and captions stay live.
        /// </summary>
        private void UpdateCenterFromScroll()
        {
            int count = Count;
            if (count == 0)
                return;
            int centre = (int)Math.Round(_scroll);
            if (Wraps)
                centre = Mod(centre, count);
            else if (centre < 0)
                centre = 0;
            else if (centre > count - 1)
                centre = count - 1;
            if (centre != _index)
            {
                _index = centre;
                RequestThumbs();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);

            int hit = HitTest(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (hit >= 0)
                {
                    Select(hit);
                    ShowContextMenu(hit, e.Location);
                }
                else
                {
                    CloseGallery();
                }
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            if (hit >= 0)
            {
                if (hit == _index)
                    ApplyIndex(hit);
                else
                    Select(hit);
            }
            else
            {
                CloseGallery();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left)
                return;

            int hit = HitTest(e.Location);
            if (hit >= 0)
                ApplyIndex(hit);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape)
            {
                CloseGallery();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Left)
            {
                MoveBy(-1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                MoveBy(1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageUp)
            {
                MoveBy(-PageStep());
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.PageDown)
            {
                MoveBy(PageStep());
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Home)
            {
                Select(0);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.End)
            {
                if (_files != null)
                    Select(_files.Length - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                ApplyIndex(_index);
                e.Handled = true;
            }
        }

        private int HitTest(Point point)
        {
            List<KeyValuePair<int, float>> cards = CollectVisible();
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                if (_strip)
                {
                    if (StripContains(cards[i].Value, point))
                        return cards[i].Key;
                }
                else if (CardRectAt(cards[i].Value).Contains(point))
                {
                    return cards[i].Key;
                }
            }
            return -1;
        }

        private void ShowContextMenu(int index, Point location)
        {
            if (_files == null || index < 0 || index >= _files.Length)
                return;

            string path = _files[index];
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Items.Add("Set as wallpaper", null, delegate { ApplyIndex(index); });
            menu.Items.Add("Open file location", null, delegate
            {
                try
                {
                    Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                catch
                {
                }
            });
            _menuOpen = true;
            menu.Closed += delegate
            {
                _menuOpen = false;
                try
                {
                    menu.Dispose();
                }
                catch
                {
                }
            };
            menu.Show(this, location);
        }

        private void ApplyIndex(int index)
        {
            if (_files == null || index < 0 || index >= _files.Length)
                return;
            if (_closeStarted)
                return;

            _panActive = false;
            _applyOnClose = _files[index];
            BeginFade(_fade, 0f, FadeOutMs);
        }

        public void CloseGallery()
        {
            if (_closeStarted || _closed)
                return;
            _panActive = false;
            _applyOnClose = null;
            BeginFade(_fade, 0f, FadeOutMs);
        }

        public void ForceClose()
        {
            if (_closed)
                return;
            _applyOnClose = null;
            CloseNow();
        }

        // ---- animation -----------------------------------------------------

        private void BeginFade(float from, float to, int duration)
        {
            _fadeFrom = from;
            _fadeTo = to;
            _fadeStart = Now;
            _fadeDuration = Math.Max(1, duration);
            _fadeClose = to <= 0.001f;
            if (_fadeClose)
                _closeStarted = true;
            StartAnim();
        }

        private void StartAnim()
        {
            if (!_timer.Enabled)
                _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            long now = Now;
            bool active = false;

            if (_fadeDuration > 0)
            {
                float t = (now - _fadeStart) / (float)_fadeDuration;
                if (t >= 1f)
                {
                    _fade = _fadeTo;
                    _fadeDuration = 0;
                }
                else
                {
                    _fade = _fadeFrom + (_fadeTo - _fadeFrom) * t;
                    active = true;
                }
            }

            // Touchpad/wheel pan: chase the continuous target so many small
            // wheel events read as one smooth gesture, then glide onto the
            // nearest card when the wheel goes quiet.
            if (_panActive)
            {
                if (_files == null || Count == 0)
                {
                    _panActive = false;
                }
                else if (Now - _panWheelAt >= PanIdleMs)
                {
                    _panActive = false;
                    SnapToNearest();
                    active = true;
                }
                else
                {
                    _scroll += (_panTarget - _scroll) * 0.45f;
                    if (Math.Abs(_panTarget - _scroll) < 0.002f)
                        _scroll = _panTarget;
                    UpdateCenterFromScroll();
                    active = true;
                }
            }

            if (_scrollDuration > 0)
            {
                float t = (now - _scrollStart) / (float)_scrollDuration;
                if (t >= 1f)
                {
                    _scroll = _scrollTo;
                    _scrollDuration = 0;
                    // Keep the continuous offset bounded once the row settles;
                    // RelativeDistance wraps regardless of how far it drifts.
                    NormalizeScroll();
                }
                else
                {
                    float eased = t * (2f - t);
                    _scroll = _scrollFrom + (_scrollTo - _scrollFrom) * eased;
                    active = true;
                }
            }

            Render();

            if (_fadeDuration == 0 && _fadeClose)
            {
                // Close from the message queue: disposing the form (and this
                // timer) inside its own Tick is not safe.
                _timer.Stop();
                try
                {
                    BeginInvoke(new MethodInvoker(CloseNow));
                }
                catch
                {
                    CloseNow();
                }
                return;
            }

            if (!active && _scrollDuration == 0)
                _timer.Stop();
        }

        private void CloseNow()
        {
            if (_closed)
                return;
            _closed = true;
            _timer.Stop();

            string apply = _applyOnClose;
            _applyOnClose = null;

            try
            {
                Close();
            }
            catch
            {
            }

            if (apply != null && _apply != null)
                _apply(apply);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _closed = true;
            _timer.Stop();
            _timer.Dispose();

            foreach (Bitmap bitmap in _thumbs.Values)
            {
                try
                {
                    bitmap.Dispose();
                }
                catch
                {
                }
            }
            _thumbs.Clear();
            DisposeStrips();

            if (_canvas != null)
            {
                _canvas.Dispose();
                _canvas = null;
            }
            if (_font != null)
            {
                _font.Dispose();
                _font = null;
            }
            if (_smallFont != null)
            {
                _smallFont.Dispose();
                _smallFont = null;
            }

            if (GalleryClosed != null)
                GalleryClosed(this, EventArgs.Empty);
        }

        // ---- painting ------------------------------------------------------

        private RectangleF CardRectAt(float distance)
        {
            // Whole-pixel positions keep the thumbnail blit on GDI+'s fast
            // path; a fractional origin forces a resample of the whole card.
            float x = (float)Math.Round(_centerX + distance * _step - _cardW / 2f);
            float y = (float)Math.Round(_centerY - _cardH / 2f);
            return new RectangleF(x, y, _cardW, _cardH);
        }

        private void Render()
        {
            if (_canvas == null || _closed)
                return;

            using (Graphics g = Graphics.FromImage(_canvas))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.CompositingQuality = CompositingQuality.HighQuality;

                int dim = (int)Math.Round(Math.Max(0f, Math.Min(1f, _config.Dim)) * 255f);
                g.Clear(Color.FromArgb(dim, 0, 0, 0));

                if (_files == null)
                {
                    DrawNotice(g, _scanning ? "Looking for wallpapers\u2026" : "No wallpapers found");
                }
                else if (_files.Length == 0)
                {
                    DrawNotice(g, "No wallpapers found. Right-click the tray icon \u2192 Wallpaper folders\u2026");
                }
                else
                {
                    if (_strip)
                        DrawStripCards(g);
                    else
                        DrawCards(g);
                    DrawCaptions(g);
                }
            }

            PushLayered(_fade);
        }

        private void DrawCaptions(Graphics g)
        {
            if (_files == null || _files.Length == 0 || _files.Length <= _index)
                return;

            StringFormat centered = new StringFormat();
            centered.Alignment = StringAlignment.Center;
            centered.LineAlignment = StringAlignment.Center;
            centered.Trimming = StringTrimming.EllipsisCharacter;
            centered.FormatFlags = StringFormatFlags.NoWrap;

            try
            {
                float nameY = _centerY - _cardH / 2f - 26f * _scale;
                if (nameY > 12f * _scale)
                {
                    string name = Path.GetFileNameWithoutExtension(_files[_index]);
                    RectangleF nameRect = new RectangleF(_screen.Width * 0.15f, nameY - 14f * _scale,
                        _screen.Width * 0.7f, 28f * _scale);
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(215, 255, 255, 255)))
                        g.DrawString(name, _font, brush, nameRect, centered);
                }

                float hintY = _centerY + _cardH / 2f + 28f * _scale;
                if (hintY < _screen.Height - 18f * _scale)
                {
                    RectangleF hintRect = new RectangleF(_screen.Width * 0.1f, hintY - 12f * _scale,
                        _screen.Width * 0.8f, 24f * _scale);
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
                        g.DrawString("Scroll or use \u2190 \u2192   \u00b7   Enter to apply   \u00b7   Esc to close",
                            _smallFont, brush, hintRect, centered);
                }
            }
            finally
            {
                centered.Dispose();
            }
        }

        private void DrawCards(Graphics g)
        {
            List<KeyValuePair<int, float>> cards = CollectVisible();

            for (int c = 0; c < cards.Count; c++)
            {
                RectangleF rect = CardRectAt(cards[c].Value);
                float distance = Math.Abs(cards[c].Value);
                float alpha = 1f;
                if (distance > 2f)
                    alpha = Math.Max(0f, 1f - (distance - 2f) * 0.2f);
                if (alpha <= 0.01f)
                    continue;

                DrawCardShadow(g, rect, alpha);
            }

            for (int c = 0; c < cards.Count; c++)
            {
                int i = cards[c].Key;
                RectangleF rect = CardRectAt(cards[c].Value);
                float distance = Math.Abs(cards[c].Value);
                float alpha = 1f;
                if (distance > 2f)
                    alpha = Math.Max(0f, 1f - (distance - 2f) * 0.2f);
                if (alpha <= 0.01f)
                    continue;

                using (GraphicsPath clip = RoundedRect(rect, _radius))
                {
                    GraphicsState state = g.Save();
                    g.SetClip(clip, CombineMode.Replace);

                    Bitmap thumb;
                    if (_thumbs.TryGetValue(i, out thumb) && thumb != null)
                    {
                        DrawWithAlpha(g, thumb, rect, alpha);
                    }
                    else
                    {
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(42 * alpha), 255, 255, 255)))
                            g.FillRectangle(brush, rect);

                        using (Pen pen = new Pen(Color.FromArgb((int)(34 * alpha), 255, 255, 255), 1f * _scale))
                            g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

                        if (i != _index)
                        {
                            string name = Path.GetFileNameWithoutExtension(_files[i]);
                            using (SolidBrush text = new SolidBrush(Color.FromArgb((int)(150 * alpha), 255, 255, 255)))
                            using (StringFormat format = new StringFormat())
                            {
                                format.Alignment = StringAlignment.Center;
                                format.LineAlignment = StringAlignment.Center;
                                format.Trimming = StringTrimming.EllipsisCharacter;
                                RectangleF textRect = new RectangleF(rect.X + 10 * _scale, rect.Y,
                                    rect.Width - 20 * _scale, rect.Height);
                                g.DrawString(name, _smallFont, text, textRect, format);
                            }
                        }
                    }

                    g.Restore(state);
                }

                if (i == _index)
                {
                    RectangleF border = RectangleF.Inflate(rect, -1.5f * _scale, -1.5f * _scale);
                    using (GraphicsPath path = RoundedRect(border, Math.Max(0f, _radius - 1.5f * _scale)))
                    using (Pen pen = new Pen(_accent, 3f * _scale))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
                else if (i == _hover)
                {
                    RectangleF border = RectangleF.Inflate(rect, -1f * _scale, -1f * _scale);
                    using (GraphicsPath path = RoundedRect(border, Math.Max(0f, _radius - _scale)))
                    using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f * _scale))
                        g.DrawPath(pen, path);
                }
            }
        }

        // ---- YASB-like parallelogram strip ---------------------------------

        private void BakeStripFor(int index)
        {
            Bitmap thumb;
            if (!_thumbs.TryGetValue(index, out thumb) || thumb == null)
                return;
            Bitmap baked = BakeStrip(thumb);
            if (baked == null)
                return;

            Bitmap previous;
            if (_strips.TryGetValue(index, out previous) && previous != null)
            {
                try { previous.Dispose(); }
                catch { }
            }
            _strips[index] = baked;
        }

        /// <summary>
        /// Builds one pre-cut frame: the artwork is sheared by the same lean as
        /// the card edges and the dim backdrop is baked into the empty zones,
        /// so the strip only ever pastes whole-pixel bitmaps with SourceCopy
        /// (no per-frame blending) and edges still antialias against the dim.
        /// </summary>
        private Bitmap BakeStrip(Bitmap thumb)
        {
            int w = thumb.Width;
            int h = thumb.Height;
            int bw = (int)Math.Ceiling(w + _lean) + StripPad * 2;
            int bh = h + StripPad * 2;
            Bitmap baked = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);

            try
            {
                using (Graphics g = Graphics.FromImage(baked))
                {
                    // The pads around the cut must stay transparent, not dim:
                    // cards are packed edge to edge, so a card's pads fall on
                    // its neighbours' artwork. Anything painted there would
                    // punch holes into the collage; transparent pads make the
                    // frames order-independent while the dimmed canvas beneath
                    // still darkens the strip's outer edges.
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.Clear(Color.FromArgb(0, 0, 0, 0));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;

                    // sheared draw, oversized slightly so the punch never shaves it
                    using (Matrix shear = new Matrix(1f, 0f, _lean / h, 1f, StripPad, StripPad))
                    {
                        g.Transform = shear;
                        g.DrawImage(thumb, new Rectangle(-StripPad, -StripPad,
                            w + StripPad * 2, h + StripPad * 2));
                    }
                    g.ResetTransform();

                    // erase everything outside the parallelogram (rect minus
                    // polygon), leaving antialiased edges back to alpha 0
                    using (GraphicsPath area = new GraphicsPath(FillMode.Alternate))
                    {
                        area.AddRectangle(new RectangleF(-2, -2, bw + 4, bh + 4));
                        area.AddPolygon(new PointF[]
                        {
                            new PointF(StripPad, StripPad),
                            new PointF(StripPad + w, StripPad),
                            new PointF(StripPad + w + _lean, StripPad + h),
                            new PointF(StripPad + _lean, StripPad + h)
                        });
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                            g.FillPath(brush, area);
                    }
                }
                return baked;
            }
            catch
            {
                baked.Dispose();
                return null;
            }
        }

        private void DisposeStrips()
        {
            foreach (Bitmap bitmap in _strips.Values)
            {
                try { bitmap.Dispose(); }
                catch { }
            }
            _strips.Clear();
        }

        /// <summary>
        /// Bounding screen box of one strip card. The parallelogram content is
        /// the card's own width, so the box (and bitmap) is one lean wider.
        /// </summary>
        private RectangleF StripBoxAt(float distance)
        {
            float cx = _centerX + distance * _step;
            float x = (float)Math.Round(cx - (_cardW + _lean) / 2f);
            float y = (float)Math.Round(_centerY - _cardH / 2f);
            return new RectangleF(x, y, _cardW + _lean, _cardH);
        }

        private GraphicsPath StripPathAt(float distance)
        {
            RectangleF box = StripBoxAt(distance);
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(new PointF[]
            {
                new PointF(box.X + StripPad, box.Y + StripPad),
                new PointF(box.X + StripPad + _cardW, box.Y + StripPad),
                new PointF(box.X + StripPad + _cardW + _lean, box.Y + StripPad + box.Height),
                new PointF(box.X + StripPad + _lean, box.Y + StripPad + box.Height)
            });
            return path;
        }

        private bool StripContains(float distance, Point point)
        {
            RectangleF box = StripBoxAt(distance);
            if (point.Y < box.Y || point.Y > box.Bottom || point.X < box.X || point.X > box.Right)
                return false;
            float left = box.X + StripPad
                + _lean * ((point.Y - box.Y) - StripPad) / Math.Max(1f, box.Height);
            return point.X >= left && point.X <= left + _cardW;
        }

        private void DrawStripCards(Graphics g)
        {
            List<KeyValuePair<int, float>> cards = CollectVisible();

            // one shared diagonal shadow behind the whole strip; only the thin
            // band under the row's bottom edge is ever visible
            if (cards.Count > 0)
            {
                float leftEdge = float.MaxValue, rightEdge = float.MinValue;
                float rowBottom = float.MinValue;
                foreach (KeyValuePair<int, float> card in cards)
                {
                    RectangleF box = StripBoxAt(card.Value);
                    leftEdge = Math.Min(leftEdge, box.X + StripPad + _lean);
                    rightEdge = Math.Max(rightEdge, box.X + StripPad + _cardW + _lean);
                    rowBottom = Math.Max(rowBottom, box.Y + StripPad + box.Height);
                }

                float depth = 4f * _scale;
                float dim = Math.Max(0f, Math.Min(1f, _config.Dim));
                int combined = (int)Math.Round((1f - (1f - dim) * (1f - 110f / 255f)) * 255f);
                using (GraphicsPath band = new GraphicsPath())
                {
                    band.AddPolygon(new PointF[]
                    {
                        new PointF(leftEdge, rowBottom),
                        new PointF(rightEdge, rowBottom),
                        new PointF(rightEdge - _lean, rowBottom + depth),
                        new PointF(leftEdge - _lean, rowBottom + depth)
                    });
                    CompositingMode previous = g.CompositingMode;
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(combined, 0, 0, 0)))
                        g.FillPath(brush, band);
                    g.CompositingMode = previous;
                }
            }

            // The baked bitmaps are one lean wider than the card step, so each
            // one's right-hand pad overlaps the next card's box. Painting any
            // card after its right neighbour would SourceCopy that dim pad over
            // the neighbour's left wedge and punch a hole in its artwork - so
            // the frames are pasted strictly left to right, letting every card
            // restore its own wedge after the previous card's pad landed.
            List<KeyValuePair<int, float>> order = new List<KeyValuePair<int, float>>(cards);
            order.Sort(delegate(KeyValuePair<int, float> a, KeyValuePair<int, float> b)
            {
                return StripBoxAt(a.Value).X.CompareTo(StripBoxAt(b.Value).X);
            });

            for (int c = 0; c < order.Count; c++)
            {
                int i = order[c].Key;
                float distance = order[c].Value;
                RectangleF box = StripBoxAt(distance);

                Bitmap baked;
                if (_strips.TryGetValue(i, out baked) && baked != null)
                {
                    // whole-pixel 1:1 paste with SourceOver: the pads are
                    // transparent so nothing on the canvas gets erased, and
                    // the opaque interior replaces the pixels it covers
                    Rectangle dest = Rectangle.Round(box);
                    dest.Size = new Size(baked.Width, baked.Height);
                    g.DrawImage(baked, dest);
                }
                else
                {
                    // artwork not ready yet: keep the leaning silhouette visible
                    using (GraphicsPath clip = StripPathAt(distance))
                    {
                        GraphicsState state = g.Save();
                        g.SetClip(clip, CombineMode.Replace);
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(42, 255, 255, 255)))
                            g.FillRectangle(brush, box.X, box.Y, box.Width, box.Height);

                        if (i != _index)
                        {
                            string name = Path.GetFileNameWithoutExtension(_files[i]);
                            using (SolidBrush text = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                            using (StringFormat format = new StringFormat())
                            {
                                format.Alignment = StringAlignment.Center;
                                format.LineAlignment = StringAlignment.Center;
                                format.Trimming = StringTrimming.EllipsisCharacter;
                                RectangleF textRect = new RectangleF(box.X + 10 * _scale, box.Y,
                                    box.Width - 20 * _scale, box.Height);
                                g.DrawString(name, _smallFont, text, textRect, format);
                            }
                        }
                        g.Restore(state);
                    }
                }
            }

            // Outlines go in a second pass so a neighbour's frame paste can
            // never cover half of the accent or hover border at a shared seam.
            for (int c = 0; c < order.Count; c++)
            {
                int i = order[c].Key;
                float distance = order[c].Value;

                if (i == _index)
                {
                    using (GraphicsPath path = StripPathAt(distance))
                    using (Pen pen = new Pen(_accent, 3f * _scale))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, path);
                    }
                }
                else if (i == _hover)
                {
                    using (GraphicsPath path = StripPathAt(distance))
                    using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), 1.5f * _scale))
                        g.DrawPath(pen, path);
                }
            }
        }

        private void DrawCardShadow(Graphics g, RectangleF rect, float alpha)
        {
            RectangleF shadow = OffsetRect(rect, 0f, 4f * _scale);
            float dim = Math.Max(0f, Math.Min(1f, _config.Dim));
            float shadowAlpha = Math.Max(0f, Math.Min(1f, 110f * alpha / 255f));
            int combined = (int)Math.Round((1f - (1f - dim) * (1f - shadowAlpha)) * 255f);

            CompositingMode previous = g.CompositingMode;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (GraphicsPath path = RoundedRect(shadow, _radius))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(combined, 0, 0, 0)))
                g.FillPath(brush, path);
            g.CompositingMode = previous;
        }

        private static RectangleF OffsetRect(RectangleF rect, float dx, float dy)
        {
            return new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
        }

        private void DrawWithAlpha(Graphics g, Bitmap bitmap, RectangleF rect, float alpha)
        {
            if (alpha >= 0.995f)
            {
                // SourceCopy avoids a per-pixel read-modify-write blend. With
                // an integer destination the bitmap is copied 1:1, which is
                // far cheaper than resampling it every frame.
                CompositingMode previous = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(bitmap, Rectangle.Round(rect));
                g.CompositingMode = previous;
                return;
            }

            ColorMatrix matrix = new ColorMatrix(new float[][]
            {
                new float[] {1f, 0f, 0f, 0f, 0f},
                new float[] {0f, 1f, 0f, 0f, 0f},
                new float[] {0f, 0f, 1f, 0f, 0f},
                new float[] {0f, 0f, 0f, alpha, 0f},
                new float[] {0f, 0f, 0f, 0f, 1f}
            });

            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                Rectangle dest = Rectangle.Round(rect);
                g.DrawImage(bitmap, dest, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        private void DrawNotice(Graphics g, string message)
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                RectangleF rect = new RectangleF(0f, _centerY - 40f * _scale, _screen.Width, 80f * _scale);
                g.DrawString(message, _font, brush, rect, format);
            }
        }

        private void PushLayered(float opacity)
        {
            if (_canvas == null || !IsHandleCreated)
                return;

            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memoryDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;

            try
            {
                hBitmap = _canvas.GetHbitmap(Color.FromArgb(0));
                previous = Native.SelectObject(memoryDc, hBitmap);

                Native.SIZE size = new Native.SIZE(_canvas.Width, _canvas.Height);
                Native.POINT source = new Native.POINT(0, 0);
                Native.POINT dest = new Native.POINT(_screen.Left, _screen.Top);

                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(opacity * 255f)));
                blend.AlphaFormat = Native.AC_SRC_ALPHA;

                Native.UpdateLayeredWindow(Handle, screenDc, ref dest, ref size, memoryDc, ref source, 0, ref blend,
                    Native.ULW_ALPHA);
            }
            finally
            {
                if (previous != IntPtr.Zero)
                    Native.SelectObject(memoryDc, previous);
                if (hBitmap != IntPtr.Zero)
                    Native.DeleteObject(hBitmap);
                if (memoryDc != IntPtr.Zero)
                    Native.DeleteDC(memoryDc);
                if (screenDc != IntPtr.Zero)
                    Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0.5f)
            {
                path.AddRectangle(rect);
                return path;
            }

            float diameter = radius * 2f;
            if (diameter > rect.Width) diameter = rect.Width;
            if (diameter > rect.Height) diameter = rect.Height;

            path.AddArc(rect.X, rect.Y, diameter, diameter, 180f, 90f);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270f, 90f);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private static Color ResolveAccent(string setting)
        {
            if (!string.IsNullOrEmpty(setting) && setting != "auto" && setting.StartsWith("#"))
            {
                try
                {
                    return ColorTranslator.FromHtml(setting);
                }
                catch
                {
                }
            }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("AccentColor");
                        if (value != null)
                        {
                            int color = Convert.ToInt32(value);
                            return Color.FromArgb(color & 0xFF, (color >> 8) & 0xFF, (color >> 16) & 0xFF);
                        }
                    }
                }
            }
            catch
            {
            }

            return Color.FromArgb(0x89, 0xB4, 0xFA);
        }
    }
}
