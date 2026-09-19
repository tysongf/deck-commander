using System;
using System.Drawing;
using System.Linq;

namespace AjazzDeck.Core
{
    public sealed class DeckActionEventArgs : EventArgs
    {
        public KeyDefinition Key { get; set; }
        public int PageIndex { get; set; }
        /// <summary>Grid index of the key, top-left = 0, reading order.</summary>
        public int KeyIndex { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public string EventName { get; set; }

        public override string ToString()
        {
            return string.Format("page {0} key {1} (row {2}, col {3}){4}",
                PageIndex + 1, KeyIndex, Row + 1, Column + 1,
                string.IsNullOrWhiteSpace(EventName) ? "" : " -> " + EventName);
        }
    }

    /// <summary>A knob turned or pressed. Only the AKP03 and AKP05 families have knobs.</summary>
    public sealed class DeckEncoderActionEventArgs : EventArgs
    {
        public EncoderDefinition Encoder { get; set; }
        public int PageIndex { get; set; }
        public int EncoderIndex { get; set; }
        /// <summary>-1 for left, +1 for right, 0 for a press.</summary>
        public int Delta { get; set; }
        public string EventName { get; set; }

        public override string ToString()
        {
            return string.Format("page {0} knob {1} {2}{3}",
                PageIndex + 1, EncoderIndex,
                Delta == 0 ? "press" : (Delta < 0 ? "left" : "right"),
                string.IsNullOrWhiteSpace(EventName) ? "" : " -> " + EventName);
        }
    }

    public enum DeckMode { Normal, PageSelector }

    /// <summary>
    /// Drives the physical deck from a profile: paints pages, optionally handles the
    /// reserved navigation column, and raises an event when a key is pressed.
    /// UI free by design, so it can run headless alongside the game.
    /// </summary>
    public sealed class DeckController : IDisposable
    {
        readonly AjazzStreamDock _deck;
        readonly DeckLayout _layout;
        readonly byte[][] _pushed;
        readonly object _sync = new object();

        DeckProfile _profile = new DeckProfile();
        int _pageIndex;
        int _selectorOffset;

        public DeckController(AjazzStreamDock deck)
        {
            if (deck == null) throw new ArgumentNullException("deck");
            _deck = deck;
            _layout = DeckLayout.For(deck.Model);
            _pushed = new byte[_layout.TotalKeys][];
            _deck.KeyChanged += OnKeyChanged;
            _deck.EncoderChanged += OnEncoderChanged;
            _profile.Normalise(deck.Model);
        }

        public DeckMode Mode { get; private set; } = DeckMode.Normal;

        /// <summary>Geometry of the attached deck.</summary>
        public DeckLayout Layout { get { return _layout; } }

        public DeckModel Model { get { return _deck.Model; } }

        /// <summary>True when the rightmost column is acting as page controls.</summary>
        public bool NavReserved
        {
            get { return _profile != null && _profile.ReservePageColumn && _layout.CanReserveNav; }
        }

        public DeckProfile Profile
        {
            get { return _profile; }
            set
            {
                _profile = value ?? new DeckProfile();
                _profile.Normalise(_deck.Model);
                if (_pageIndex >= _profile.Pages.Count) _pageIndex = 0;
                Mode = DeckMode.Normal;
                _deck.SetBrightness(_profile.Brightness);
                Render(true);
                RaisePageChanged();
            }
        }

        public int PageIndex
        {
            get { return _pageIndex; }
            set
            {
                int n = _profile.Pages.Count;
                if (n == 0) return;
                int clamped = ((value % n) + n) % n;   // wrap in both directions
                if (clamped == _pageIndex && Mode == DeckMode.Normal) return;
                _pageIndex = clamped;
                Mode = DeckMode.Normal;
                Render();
                RaisePageChanged();
            }
        }

        public DeckPage CurrentPage
        {
            get
            {
                return _profile.Pages.Count > 0
                    ? _profile.Pages[Math.Min(_pageIndex, _profile.Pages.Count - 1)]
                    : null;
            }
        }

        /// <summary>Raised for every key press, even unconfigured ones. Used by the editor.</summary>
        public event EventHandler<DeckActionEventArgs> KeyPressed;

        /// <summary>Raised only for keys with an action assigned.</summary>
        public event EventHandler<DeckActionEventArgs> ActionTriggered;

        /// <summary>Raised when a knob is turned or pressed and has an event assigned.</summary>
        public event EventHandler<DeckEncoderActionEventArgs> EncoderTriggered;

        public event EventHandler PageChanged;

        void RaisePageChanged()
        {
            var h = PageChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        public void Start()
        {
            _deck.Initialize(_profile.Brightness);
            _deck.Start();
            Render(true);
        }

        // ---------- painting ----------

        public void Render(bool force = false)
        {
            lock (_sync)
            {
                for (int grid = 0; grid < _layout.TotalKeys; grid++)
                {
                    if (!_layout.HasScreen(grid)) continue;
                    using (var tile = RenderKey(grid))
                        Push(grid, DeckImage.ToKeyJpeg(tile, _deck.Model, grid), force);
                }
                RenderEncoders(force);
            }
        }

        void RenderEncoders(bool force)
        {
            if (!_deck.Model.HasEncoderScreens) return;
            var page = CurrentPage;
            if (page == null) return;
            var r = new KeyRenderer(_deck.Model.EncoderPixels.Width, _deck.Model.EncoderPixels.Height);
            for (int i = 0; i < _deck.Model.EncoderCount && i < page.Encoders.Count; i++)
            {
                using (var tile = r.RenderEncoder(page.Encoders[i]))
                    _deck.SetEncoderImageJpeg(i, DeckImage.ToEncoderJpeg(tile, _deck.Model));
            }
        }

        /// <summary>Repaints a single key by grid index.</summary>
        public void RenderSlot(int gridIndex)
        {
            if (Mode != DeckMode.Normal) { Render(); return; }
            if (gridIndex < 0 || gridIndex >= _layout.TotalKeys) return;
            if (!_layout.HasScreen(gridIndex)) return;
            lock (_sync)
                using (var tile = RenderKey(gridIndex))
                    Push(gridIndex, DeckImage.ToKeyJpeg(tile, _deck.Model, gridIndex), false);
        }

        Bitmap RenderKey(int grid)
        {
            if (Mode == DeckMode.Normal)
                return RenderNormalKey(_layout, _profile, _pageIndex, grid);

            // page selector, only reachable when the nav column is reserved
            var r = KeyRenderer.For(_deck.Model, grid);
            int total = Math.Max(1, _profile.Pages.Count);

            if (grid == _layout.PageIndicatorKey)
                return r.RenderIndicator(_pageIndex + 1, total, true);
            if (grid == _layout.PageUpKey)
                return r.RenderArrow(true, _selectorOffset > 0);
            if (grid == _layout.PageDownKey)
                return r.RenderArrow(false, _selectorOffset + _layout.ActionKeyCount(true) < total);

            var grids = _layout.ActionGrids(true);
            int slot = Array.IndexOf(grids, grid);
            if (slot < 0) return r.RenderEmpty();

            int page = _selectorOffset + slot;
            if (page >= total) return r.RenderEmpty();
            return r.RenderPageTile(page + 1, _profile.Pages[page].Name, page == _pageIndex);
        }

        /// <summary>
        /// Renders a key exactly as the hardware shows it in normal mode. The editor uses
        /// this for its preview so what you see on screen matches the deck.
        /// </summary>
        public static Bitmap RenderNormalKey(DeckLayout layout, DeckProfile profile,
                                             int pageIndex, int grid)
        {
            if (layout == null) layout = DeckLayout.Default;
            var r = KeyRenderer.For(layout.Model, grid);
            bool nav = profile != null && profile.ReservePageColumn && layout.CanReserveNav;
            int total = Math.Max(1, profile == null ? 1 : profile.Pages.Count);

            if (nav)
            {
                if (grid == layout.PageIndicatorKey)
                    return r.RenderIndicator(pageIndex + 1, total, false);
                if (grid == layout.PageUpKey)
                    return r.RenderArrow(true, total > 1);
                if (grid == layout.PageDownKey)
                    return r.RenderArrow(false, total > 1);
            }

            if (profile == null || pageIndex < 0 || pageIndex >= profile.Pages.Count)
                return r.RenderEmpty();

            var keys = profile.Pages[pageIndex].Keys;
            if (grid < 0 || grid >= keys.Count) return r.RenderEmpty();
            return r.Render(keys[grid]);
        }

        void Push(int grid, byte[] jpeg, bool force)
        {
            if (!force && _pushed[grid] != null && _pushed[grid].SequenceEqual(jpeg)) return;
            _deck.SetKeyImageJpeg(grid, jpeg);
            _pushed[grid] = jpeg;
        }

        // ---------- input ----------

        void OnKeyChanged(object sender, DeckKeyEventArgs e)
        {
            if (!e.Pressed) return;
            try { HandleKey(e.Key); }
            catch { /* never let a handler kill the reader thread */ }
        }

        void OnEncoderChanged(object sender, DeckEncoderEventArgs e)
        {
            try { HandleEncoder(e.Encoder, e.Delta); }
            catch { /* never let a handler kill the reader thread */ }
        }

        public void HandleEncoder(int encoder, int delta)
        {
            var page = CurrentPage;
            if (page == null || encoder < 0 || encoder >= page.Encoders.Count) return;
            var def = page.Encoders[encoder];
            if (def == null) return;

            string name = delta < 0 ? def.TurnLeftEvent
                        : delta > 0 ? def.TurnRightEvent
                        : def.PressEvent;
            if (string.IsNullOrWhiteSpace(name)) return;

            var h = EncoderTriggered;
            if (h != null)
                h(this, new DeckEncoderActionEventArgs
                {
                    Encoder = def, PageIndex = _pageIndex,
                    EncoderIndex = encoder, Delta = delta, EventName = name
                });
        }

        public void HandleKey(int grid)
        {
            int total = Math.Max(1, _profile.Pages.Count);
            bool nav = NavReserved;

            if (nav && _layout.HasPageIndicator && grid == _layout.PageIndicatorKey)
            {
                if (Mode == DeckMode.PageSelector) { Mode = DeckMode.Normal; Render(); }
                else if (total > 1)
                {
                    Mode = DeckMode.PageSelector;
                    int per = _layout.ActionKeyCount(true);
                    _selectorOffset = (_pageIndex / per) * per;
                    Render();
                }
                return;
            }

            if (nav && grid == _layout.PageUpKey)
            {
                if (Mode == DeckMode.PageSelector)
                {
                    int per = _layout.ActionKeyCount(true);
                    if (_selectorOffset > 0)
                    {
                        _selectorOffset = Math.Max(0, _selectorOffset - per);
                        Render();
                    }
                }
                else PageIndex = _pageIndex - 1;
                return;
            }

            if (nav && grid == _layout.PageDownKey)
            {
                if (Mode == DeckMode.PageSelector)
                {
                    int per = _layout.ActionKeyCount(true);
                    if (_selectorOffset + per < total)
                    {
                        _selectorOffset += per;
                        Render();
                    }
                }
                else PageIndex = _pageIndex + 1;
                return;
            }

            if (Mode == DeckMode.PageSelector)
            {
                var grids = _layout.ActionGrids(true);
                int slot = Array.IndexOf(grids, grid);
                if (slot < 0) return;
                int page = _selectorOffset + slot;
                if (page < total) PageIndex = page;
                return;
            }

            var current = CurrentPage;
            if (current == null || grid < 0 || grid >= current.Keys.Count) return;
            var def = current.Keys[grid];

            var kp = KeyPressed;
            if (kp != null)
                kp(this, new DeckActionEventArgs
                {
                    Key = def, PageIndex = _pageIndex, KeyIndex = grid,
                    Row = _layout.RowOf(grid), Column = _layout.ColumnOf(grid),
                    EventName = def == null ? "" : def.EventName
                });

            if (def == null || def.ActionType == DeckActionType.None) return;

            var h = ActionTriggered;
            if (h != null)
                h(this, new DeckActionEventArgs
                {
                    Key = def, PageIndex = _pageIndex, KeyIndex = grid,
                    Row = _layout.RowOf(grid), Column = _layout.ColumnOf(grid),
                    EventName = def.EventName
                });
        }

        public void Dispose()
        {
            _deck.KeyChanged -= OnKeyChanged;
            _deck.EncoderChanged -= OnEncoderChanged;
        }
    }
}
