using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AjazzDeck;
using AjazzDeck.Core;

namespace DeckCommander
{
    public partial class MainWindow : Window
    {
        readonly ObservableCollection<KeyTileVm> _tiles = new ObservableCollection<KeyTileVm>();

        DeckProfile _profile = new DeckProfile();
        string _path;
        AjazzStreamDock _deck;
        DeckController _ctrl;
        int _selectedGrid = -1;
        bool _ready;
        bool _suppress;
        HotkeyManager _hotkeys;

        static readonly string[] Presets =
        {
            // neutrals
            "#0E1015", "#2A2E37", "#4A5160", "#6C7482", "#9AA3B5", "#D6DCE8",
            // red, orange, amber, yellow
            "#7A2020", "#B03A3A", "#E05A5A",
            "#7A4212", "#B5661F", "#E88C34",
            "#7A5A12", "#B58A1F", "#E8B93A",
            "#77701A", "#B5A81F", "#E5DA45",
            // green, teal
            "#2A5E22", "#4E9E3E", "#71CB57",
            "#1B5E5A", "#2E8B87", "#45C0BA",
            // blue, indigo
            "#1E4C86", "#2F6FB5", "#4E9BE8",
            "#28306E", "#3F4CA8", "#6472E0",
            // purple, magenta, pink
            "#3B2E7A", "#5A45B0", "#7F68E0",
            "#63245F", "#93368F", "#C755C0",
            "#7A2547", "#B53A6B", "#E85F95"
        };

        public MainWindow()
        {
            InitializeComponent();

            for (int i = 0; i < DeckLayout.TotalKeys; i++)
                _tiles.Add(new KeyTileVm { GridIndex = i, IsNav = false });
            KeyGrid.ItemsSource = _tiles;

            GlyphBox.ItemsSource = Enum.GetValues(typeof(KeyGlyph));
            ActionBox.ItemsSource = Enum.GetValues(typeof(DeckActionType));

            foreach (var hex in Presets)
            {
                var swatch = new Button
                {
                    Style = (Style)FindResource("Swatch"),
                    Background = new SolidColorBrush(ToMediaColour(hex)),
                    Tag = hex,
                    ToolTip = hex
                };
                swatch.Click += (s, e) =>
                {
                    ColourBox.Text = (string)((Button)s).Tag;
                };
                Swatches.Items.Add(swatch);
            }

            NewProfile();
            ReserveNavBox.IsChecked = _profile.ReservePageColumn;
            _ready = true;
            RefreshAll();
        }

        // ---------- helpers ----------

        static System.Windows.Media.Color ToMediaColour(string hex)
        {
            var c = KeyRenderer.ParseColour(hex, System.Drawing.Color.Gray);
            return System.Windows.Media.Color.FromRgb(c.R, c.G, c.B);
        }

        static ImageSource ToImageSource(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                return img;
            }
        }

        int SelectedPageIndex
        {
            get
            {
                int i = PageList.SelectedIndex;
                if (i < 0 && _profile.Pages.Count > 0) i = 0;
                return i;
            }
        }

        DeckPage SelectedPage
        {
            get
            {
                int i = SelectedPageIndex;
                return (i >= 0 && i < _profile.Pages.Count) ? _profile.Pages[i] : null;
            }
        }

        bool NavReserved
        {
            get { return _profile != null && _profile.ReservePageColumn; }
        }

        KeyDefinition SelectedKey
        {
            get
            {
                var page = SelectedPage;
                if (page == null || _selectedGrid < 0) return null;
                if (DeckLayout.IsNavKey(_selectedGrid, NavReserved)) return null;
                return page.Keys[_selectedGrid];
            }
        }

        void Status(string text) { StatusText.Text = text; }

        // ---------- refresh ----------

        void RefreshAll()
        {
            RefreshPageList();
            RefreshGrid();
            LoadInspector();
        }

        void RefreshPageList()
        {
            int keep = PageList.SelectedIndex;
            PageList.ItemsSource = null;
            PageList.ItemsSource = _profile.Pages;
            if (_profile.Pages.Count > 0)
                PageList.SelectedIndex = Math.Min(Math.Max(keep, 0), _profile.Pages.Count - 1);
        }

        void RefreshGrid()
        {
            int page = SelectedPageIndex;
            for (int i = 0; i < DeckLayout.TotalKeys; i++)
            {
                using (var bmp = DeckController.RenderNormalKey(_profile, page, i))
                    _tiles[i].Image = ToImageSource(bmp);
                _tiles[i].IsNav = DeckLayout.IsNavKey(i, NavReserved);
                _tiles[i].IsSelected = (i == _selectedGrid);
            }
            GridHint.Text = NavReserved
                ? "Click a key to edit it. The right column is reserved for page controls."
                : "Click a key to edit it. All 15 keys are available; change pages on the left.";
        }

        void RefreshTile(int grid)
        {
            using (var bmp = DeckController.RenderNormalKey(_profile, SelectedPageIndex, grid))
                _tiles[grid].Image = ToImageSource(bmp);
        }

        void LoadInspector()
        {
            _suppress = true;
            try
            {
                if (_selectedGrid < 0)
                {
                    KeyTitle.Text = "No key selected";
                    KeyHint.Text = "Select a key in the grid.";
                    KeyFields.IsEnabled = false;
                    return;
                }

                int row = _selectedGrid / DeckLayout.Columns, col = _selectedGrid % DeckLayout.Columns;

                if (DeckLayout.IsNavKey(_selectedGrid, NavReserved))
                {
                    KeyTitle.Text = string.Format("Row {0}, column {1}", row + 1, col + 1);
                    KeyHint.Text = _selectedGrid == DeckLayout.PageUpKey ? "Reserved: page up."
                        : _selectedGrid == DeckLayout.PageDownKey ? "Reserved: page down."
                        : "Reserved: page indicator. Press it on the deck to open the page selector, "
                          + "press again to cancel.";
                    KeyFields.IsEnabled = false;
                    return;
                }

                var key = SelectedKey;
                if (key == null) { KeyFields.IsEnabled = false; return; }

                KeyTitle.Text = string.Format("Row {0}, column {1}", row + 1, col + 1);
                KeyHint.Text = "Action key.";
                KeyFields.IsEnabled = true;

                LabelBox.Text = key.Label ?? "";
                GlyphBox.SelectedItem = key.Glyph;
                ColourBox.Text = key.Background ?? "";
                ImageBox.Text = key.ImagePath ?? "";
                ActionBox.SelectedItem = key.ActionType;
                EventBox.Text = key.EventName ?? "";
                ColourSwatch.Background = new SolidColorBrush(ToMediaColour(key.Background));
            }
            finally { _suppress = false; }
        }

        // ---------- deck ----------

        void PushSlot(int grid)
        {
            if (_ctrl == null) return;
            try
            {
                if (_ctrl.PageIndex != SelectedPageIndex) _ctrl.PageIndex = SelectedPageIndex;
                _ctrl.RenderSlot(grid);
            }
            catch (Exception ex) { Status("Deck write failed: " + ex.Message); }
        }

        void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (_ctrl != null) { Disconnect(); return; }
            try
            {
                _deck = AjazzStreamDock.OpenFirst();
                _ctrl = new DeckController(_deck);
                _ctrl.KeyPressed += (s, ev) => Dispatcher.Invoke(() =>
                {
                    EventLog.Text = ev.ToString();
                    SelectGrid(ev.KeyIndex);
                });
                _ctrl.PageChanged += (s, ev) => Dispatcher.Invoke(() =>
                {
                    if (PageList.SelectedIndex != _ctrl.PageIndex)
                        PageList.SelectedIndex = _ctrl.PageIndex;
                });

                _ctrl.Start();
                _profile.Brightness = (int)BrightnessSlider.Value;
                _ctrl.Profile = _profile;
                _ctrl.PageIndex = SelectedPageIndex;

                ConnectBtn.Content = "Disconnect";
                Status("Connected: " + _deck.Info);
            }
            catch (Exception ex)
            {
                Disconnect();
                Status("Connect failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Could not open the deck",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void Disconnect()
        {
            try { if (_ctrl != null) _ctrl.Dispose(); } catch { }
            try { if (_deck != null) _deck.Dispose(); } catch { }
            _ctrl = null;
            _deck = null;
            ConnectBtn.Content = "Connect deck";
            Status("Not connected.");
        }

        void ReserveNav_Changed(object sender, RoutedEventArgs e)
        {
            if (!_ready) return;
            _profile.ReservePageColumn = ReserveNavBox.IsChecked == true;
            RefreshGrid();
            LoadInspector();
            if (_ctrl != null)
            {
                try { _ctrl.Render(true); } catch (Exception ex) { Status("Deck sync failed: " + ex.Message); }
            }
            Status(_profile.ReservePageColumn
                ? "Right column reserved for page controls, 12 action keys."
                : "All 15 keys usable. Change pages here in Deck Commander.");
        }

        void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BrightnessText == null) return;
            int v = (int)e.NewValue;
            BrightnessText.Text = v.ToString();
            if (!_ready) return;
            _profile.Brightness = v;
            if (_deck != null)
            {
                try { _deck.SetBrightness(v); } catch { }
            }
        }

        // ---------- grid ----------

        void Tile_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            SelectGrid((int)border.Tag);
        }

        void SelectGrid(int grid)
        {
            _selectedGrid = grid;
            for (int i = 0; i < _tiles.Count; i++) _tiles[i].IsSelected = (i == grid);
            LoadInspector();
        }

        // ---------- inspector edits ----------

        void Field_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppress || !_ready) return;
            var key = SelectedKey;
            if (key == null) return;

            key.Label = LabelBox.Text;
            key.Glyph = GlyphBox.SelectedItem is KeyGlyph ? (KeyGlyph)GlyphBox.SelectedItem : KeyGlyph.None;
            key.Background = ColourBox.Text;
            key.ImagePath = ImageBox.Text;
            key.ActionType = ActionBox.SelectedItem is DeckActionType
                ? (DeckActionType)ActionBox.SelectedItem : DeckActionType.None;
            key.EventName = EventBox.Text;

            ColourSwatch.Background = new SolidColorBrush(ToMediaColour(key.Background));
            RefreshTile(_selectedGrid);
            PushSlot(_selectedGrid);
        }

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
                Title = "Choose a key image"
            };
            if (dlg.ShowDialog(this) == true) ImageBox.Text = dlg.FileName;
        }

        void ClearImage_Click(object sender, RoutedEventArgs e) { ImageBox.Text = ""; }

        void ClearKey_Click(object sender, RoutedEventArgs e)
        {
            var page = SelectedPage;
            if (page == null || _selectedGrid < 0) return;
            if (DeckLayout.IsNavKey(_selectedGrid, NavReserved)) return;
            page.Keys[_selectedGrid] = new KeyDefinition();
            LoadInspector();
            RefreshTile(_selectedGrid);
            PushSlot(_selectedGrid);
        }

        // ---------- pages ----------

        void PageList_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready) return;
            _suppress = true;
            PageNameBox.Text = SelectedPage != null ? SelectedPage.Name : "";
            _suppress = false;
            RefreshGrid();
            LoadInspector();
            if (_ctrl != null)
            {
                try { _ctrl.PageIndex = SelectedPageIndex; } catch { }
            }
        }

        void PageName_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppress || !_ready) return;
            var page = SelectedPage;
            if (page == null) return;
            page.Name = PageNameBox.Text;
            int keep = PageList.SelectedIndex;
            PageList.ItemsSource = null;
            PageList.ItemsSource = _profile.Pages;
            PageList.SelectedIndex = keep;
            RefreshTile(DeckLayout.PageIndicatorKey);
        }

        void AddPage_Click(object sender, RoutedEventArgs e)
        {
            _profile.Pages.Add(new DeckPage { Name = "Page " + (_profile.Pages.Count + 1) });
            RefreshPageList();
            PageList.SelectedIndex = _profile.Pages.Count - 1;
            SyncDeckProfile();
        }

        void DuplicatePage_Click(object sender, RoutedEventArgs e)
        {
            var page = SelectedPage;
            if (page == null) return;
            var copy = new DeckPage { Name = page.Name + " copy" };
            for (int i = 0; i < page.Keys.Count; i++) copy.Keys[i] = page.Keys[i].Clone();
            _profile.Pages.Insert(SelectedPageIndex + 1, copy);
            RefreshPageList();
            PageList.SelectedIndex = _profile.Pages.IndexOf(copy);
            SyncDeckProfile();
        }

        void DeletePage_Click(object sender, RoutedEventArgs e)
        {
            if (_profile.Pages.Count <= 1)
            {
                Status("A profile needs at least one page.");
                return;
            }
            int i = SelectedPageIndex;
            _profile.Pages.RemoveAt(i);
            RefreshPageList();
            PageList.SelectedIndex = Math.Min(i, _profile.Pages.Count - 1);
            SyncDeckProfile();
        }

        void MovePage(int delta)
        {
            int i = SelectedPageIndex, j = i + delta;
            if (i < 0 || j < 0 || j >= _profile.Pages.Count) return;
            var page = _profile.Pages[i];
            _profile.Pages.RemoveAt(i);
            _profile.Pages.Insert(j, page);
            RefreshPageList();
            PageList.SelectedIndex = j;
            SyncDeckProfile();
        }

        void PageUp_Click(object sender, RoutedEventArgs e) { MovePage(-1); }
        void PageDown_Click(object sender, RoutedEventArgs e) { MovePage(1); }

        void SyncDeckProfile()
        {
            if (_ctrl == null) return;
            try { _ctrl.Profile = _profile; _ctrl.PageIndex = SelectedPageIndex; }
            catch (Exception ex) { Status("Deck sync failed: " + ex.Message); }
        }

        // ---------- file ----------

        void NewProfile()
        {
            _profile = new DeckProfile { Name = "Untitled" };
            _profile.Normalise();
            _path = null;
            _selectedGrid = -1;
            Title = "Deck Commander - Untitled";
        }

        void New_Click(object sender, RoutedEventArgs e)
        {
            NewProfile();
            _suppress = true;
            ReserveNavBox.IsChecked = _profile.ReservePageColumn;
            _suppress = false;
            RefreshAll();
            SyncDeckProfile();
            Status("New profile.");
        }

        void Open_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Deck profile|*.json|All files|*.*",
                Title = "Open profile"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var loaded = ProfileStore.Load(dlg.FileName);
                if (loaded == null) { Status("Nothing loaded."); return; }
                _profile = loaded;
                _path = dlg.FileName;
                _selectedGrid = -1;
                Title = "Deck Commander - " + Path.GetFileName(_path);
                _suppress = true;
                BrightnessSlider.Value = _profile.Brightness;
                ReserveNavBox.IsChecked = _profile.ReservePageColumn;
                _suppress = false;
                RefreshAll();
                SyncDeckProfile();
                Status("Loaded " + _path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not open profile",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_path)) { SaveAs_Click(sender, e); return; }
            SaveTo(_path);
        }

        void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Deck profile|*.json",
                FileName = "profile.json",
                Title = "Save profile"
            };
            if (dlg.ShowDialog(this) == true) SaveTo(dlg.FileName);
        }

        void SaveTo(string path)
        {
            try
            {
                ProfileStore.Save(_profile, path);
                _path = path;
                Title = "Deck Commander - " + Path.GetFileName(path);
                Status("Saved " + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not save profile",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- global hotkeys ----------

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var src = (System.Windows.Interop.HwndSource)
                    System.Windows.PresentationSource.FromVisual(this);
                _hotkeys = new HotkeyManager(src);

                bool prev = _hotkeys.Register(
                    HotkeyManager.ModControl | HotkeyManager.ModAlt,
                    HotkeyManager.VkPageUp, () => StepPage(-1));
                bool next = _hotkeys.Register(
                    HotkeyManager.ModControl | HotkeyManager.ModAlt,
                    HotkeyManager.VkPageDown, () => StepPage(1));

                Status(prev && next
                    ? "Global hotkeys: Ctrl+Alt+PageUp / Ctrl+Alt+PageDown change pages."
                    : "Some global hotkeys are already taken by another application.");
            }
            catch (Exception ex) { Status("Hotkeys unavailable: " + ex.Message); }
        }

        void StepPage(int delta)
        {
            int n = _profile.Pages.Count;
            if (n <= 1) return;
            int i = SelectedPageIndex + delta;
            i = ((i % n) + n) % n;              // wrap in both directions
            PageList.SelectedIndex = i;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hotkeys != null) { _hotkeys.Dispose(); _hotkeys = null; }
            Disconnect();
            base.OnClosed(e);
        }
    }
}



