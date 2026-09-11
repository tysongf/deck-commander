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
        public int Row { get { return DeckLayout.RowOf(KeyIndex); } }
        public int Column { get { return DeckLayout.ColumnOf(KeyIndex); } }
        public string EventName { get; set; }

        public override string ToString()
        {
            return string.Format("page {0} key {1} (row {2}, col {3}){4}",
                PageIndex + 1, KeyIndex, Row + 1, Column + 1,
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
        readonly byte[][] _pushed = new byte[DeckLayout.TotalKeys][];
        readonly object _sync = new object();

        DeckProfile _profile = new DeckProfile();
        int _pageIndex;
        int _selectorOffset;

        public DeckController(AjazzStreamDock deck)
        {
            if (deck == null) throw new ArgumentNullException("deck");
            _deck = deck;
            _deck.KeyChanged += OnKeyChanged;
        }

        public DeckMode Mode { get; private set; } = DeckMode.Normal;

        /// <summary>True when the rightmost column is acting as page controls.</summary>
        public bool NavReserved
        {
            get { return _profile != null && _profile.ReservePageColumn; }
        }

        public DeckProfile Profile
        {
            get { return _profile; }
            set
            {
                _profile = value ?? new DeckProfile();
                _profile.Normalise();
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
                for (int grid = 0; grid < DeckLayout.TotalKeys; grid++)
                {
                    using (var tile = RenderKey(grid))
                        Push(grid, DeckImage.ToKeyJpeg(tile), force);
                }
            }
        }

        /// <summary>Repaints a single key by grid index.</summary>
        public void RenderSlot(int gridIndex)
        {
            if (Mode != DeckMode.Normal) { Render(); return; }
            if (gridIndex < 0 || gridIndex >= DeckLayout.TotalKeys) return;
            lock (_sync)
                using (var tile = RenderKey(gridIndex))
                    Push(gridIndex, DeckImage.ToKeyJpeg(tile), false);
        }

        Bitmap RenderKey(int grid)
        {
            if (Mode == DeckMode.Normal)
                return RenderNormalKey(_profile, _pageIndex, grid);

            // page selector, only reachable when the nav column is reserved
            int total = Math.Max(1, _profile.Pages.Count);

            if (grid == DeckLayout.PageIndicatorKey)
                return KeyRenderer.RenderIndicator(_pageIndex + 1, total, true);
            if (grid == DeckLayout.PageUpKey)
                return KeyRenderer.RenderArrow(true, _selectorOffset > 0);
            if (grid == DeckLayout.PageDownKey)
                return KeyRenderer.RenderArrow(false,
                    _selectorOffset + DeckLayout.ActionKeyCount(true) < total);

            var grids = DeckLayout.ActionGrids(true);
            int slot = Array.IndexOf(grids, grid);
            if (slot < 0) return KeyRenderer.RenderEmpty();

            int page = _selectorOffset + slot;
            if (page >= total) return KeyRenderer.RenderEmpty();
            return KeyRenderer.RenderPageTile(page + 1, _profile.Pages[page].Name, page == _pageIndex);
        }

        /// <summary>
        /// Renders a key exactly as the hardware shows it in normal mode. The editor uses
        /// this for its preview so what you see on screen matches the deck.
        /// </summary>
        public static Bitmap RenderNormalKey(DeckProfile profile, int pageIndex, int grid)
        {
            bool nav = profile != null && profile.ReservePageColumn;
            int total = Math.Max(1, profile == null ? 1 : profile.Pages.Count);

            if (nav)
            {
                if (grid == DeckLayout.PageIndicatorKey)
                    return KeyRenderer.RenderIndicator(pageIndex + 1, total, false);
                if (grid == DeckLayout.PageUpKey)
                    return KeyRenderer.RenderArrow(true, total > 1);
                if (grid == DeckLayout.PageDownKey)
                    return KeyRenderer.RenderArrow(false, total > 1);
            }

            if (profile == null || pageIndex < 0 || pageIndex >= profile.Pages.Count)
                return KeyRenderer.RenderEmpty();

            return KeyRenderer.Render(profile.Pages[pageIndex].Keys[grid]);
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

        public void HandleKey(int grid)
        {
            int total = Math.Max(1, _profile.Pages.Count);
            bool nav = NavReserved;

            if (nav && grid == DeckLayout.PageIndicatorKey)
            {
                if (Mode == DeckMode.PageSelector) { Mode = DeckMode.Normal; Render(); }
                else if (total > 1)
                {
                    Mode = DeckMode.PageSelector;
                    int per = DeckLayout.ActionKeyCount(true);
                    _selectorOffset = (_pageIndex / per) * per;
                    Render();
                }
                return;
            }

            if (nav && grid == DeckLayout.PageUpKey)
            {
                if (Mode == DeckMode.PageSelector)
                {
                    int per = DeckLayout.ActionKeyCount(true);
                    if (_selectorOffset > 0)
                    {
                        _selectorOffset = Math.Max(0, _selectorOffset - per);
                        Render();
                    }
                }
                else PageIndex = _pageIndex - 1;
                return;
            }

            if (nav && grid == DeckLayout.PageDownKey)
            {
                if (Mode == DeckMode.PageSelector)
                {
                    int per = DeckLayout.ActionKeyCount(true);
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
                var grids = DeckLayout.ActionGrids(true);
                int slot = Array.IndexOf(grids, grid);
                if (slot < 0) return;
                int page = _selectorOffset + slot;
                if (page < total) PageIndex = page;
                return;
            }

            var current = CurrentPage;
            if (current == null) return;
            var def = current.Keys[grid];

            var kp = KeyPressed;
            if (kp != null)
                kp(this, new DeckActionEventArgs
                {
                    Key = def, PageIndex = _pageIndex, KeyIndex = grid,
                    EventName = def == null ? "" : def.EventName
                });

            if (def == null || def.ActionType == DeckActionType.None) return;

            var h = ActionTriggered;
            if (h != null)
                h(this, new DeckActionEventArgs
                {
                    Key = def, PageIndex = _pageIndex, KeyIndex = grid,
                    EventName = def.EventName
                });
        }

        public void Dispose()
        {
            _deck.KeyChanged -= OnKeyChanged;
        }
    }
}
