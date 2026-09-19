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
        readonly ObservableCollection<EncoderTileVm> _knobs = new ObservableCollection<EncoderTileVm>();

        DeckProfile _profile = new DeckProfile();
        string _path;
        IDeckDevice _device;
        VirtualDeckWindow _virtualWindow;
        DeckController _ctrl;
        DeckModel _model = DeckModel.Default;
        DeckLayout _layout = DeckLayout.Default;
        int _selectedGrid = -1;
        int _selectedEncoder = -1;
        bool _ready;
        bool _suppress;
        HotkeyManager _hotkeys;

        /// <summary>Bound by the preview's UniformGrid, so the grid follows the chosen deck.</summary>
        public static readonly DependencyProperty GridRowsProperty =
            DependencyProperty.Register("GridRows", typeof(int), typeof(MainWindow),
                new PropertyMetadata(3));

        public static readonly DependencyProperty GridColumnsProperty =
            DependencyProperty.Register("GridColumns", typeof(int), typeof(MainWindow),
                new PropertyMetadata(6));

        public int GridRows
        {
            get { return (int)GetValue(GridRowsProperty); }
            set { SetValue(GridRowsProperty, value); }
        }

        public int GridColumns
        {
            get { return (int)GetValue(GridColumnsProperty); }
            set { SetValue(GridColumnsProperty, value); }
        }

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

            KeyGrid.ItemsSource = _tiles;
            EncoderStrip.ItemsSource = _knobs;

            DeviceBox.ItemsSource = DeckModel.All;
            DeviceBox.SelectedItem = _model;

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
            RebuildForModel();
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
            get { return _profile != null && _profile.ReservePageColumn && _layout.CanReserveNav; }
        }

        KeyDefinition SelectedKey
        {
            get
            {
                var page = SelectedPage;
                if (page == null || _selectedGrid < 0 || _selectedGrid >= page.Keys.Count) return null;
                if (_layout.IsNavKey(_selectedGrid, NavReserved)) return null;
                return page.Keys[_selectedGrid];
            }
        }

        EncoderDefinition SelectedEncoder
        {
            get
            {
                var page = SelectedPage;
                if (page == null || _selectedEncoder < 0 ||
                    _selectedEncoder >= page.Encoders.Count) return null;
                return page.Encoders[_selectedEncoder];
            }
        }

        void Status(string text) { StatusText.Text = text; }

        // ---------- device ----------

        /// <summary>
        /// Rebuilds the preview for the current model: grid shape, tile sizes, knob strip,
        /// and the profile's page storage, which is remapped row and column wise.
        /// </summary>
        void RebuildForModel()
        {
            _layout = DeckLayout.For(_model);
            _profile.Normalise(_model);

            GridRows = _layout.Rows;
            GridColumns = _layout.Columns;

            // Fit the centre column, and keep the relative sizes of the real screens so a
            // narrow sixth column reads as narrow here too.
            int maxPx = 1;
            for (int i = 0; i < _layout.TotalKeys; i++)
                maxPx = Math.Max(maxPx, _model.KeyPixels(i));
            double cell = Math.Max(52.0, Math.Min(112.0, 700.0 / _layout.Columns - 12));

            _tiles.Clear();
            for (int i = 0; i < _layout.TotalKeys; i++)
            {
                double f = _model.KeyPixels(i) / (double)maxPx;
                _tiles.Add(new KeyTileVm
                {
                    GridIndex = i,
                    HasScreen = _layout.HasScreen(i),
                    IsPressable = _layout.IsPressable(i),
                    TileWidth = Math.Round(cell * f),
                    TileHeight = Math.Round(cell * f)
                });
            }

            _knobs.Clear();
            for (int i = 0; i < _model.EncoderCount; i++)
                _knobs.Add(new EncoderTileVm { EncoderIndex = i });
            EncoderStrip.Visibility = _model.EncoderCount > 0
                ? Visibility.Visible : Visibility.Collapsed;

            ReserveNavBox.IsEnabled = _layout.CanReserveNav;
            if (!_layout.CanReserveNav) _profile.ReservePageColumn = false;

            if (_selectedGrid >= _layout.TotalKeys) _selectedGrid = -1;
            if (_selectedEncoder >= _model.EncoderCount) _selectedEncoder = -1;
        }

        void Device_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || _suppress) return;
            var model = DeviceBox.SelectedItem as DeckModel;
            if (model == null || model == _model) return;

            if (_ctrl != null && _device != null && _device.Model != model)
            {
                // The attached deck decides; putting the picker back is less surprising
                // than silently designing for a deck that is not the one plugged in.
                _suppress = true;
                DeviceBox.SelectedItem = _model;
                _suppress = false;
                Status("Disconnect first to design for a different deck.");
                return;
            }

            _model = model;
            RebuildForModel();
            RefreshAll();
            Status(string.Format("Designing for {0}: {1}x{2}{3}, {4}px keys, protocol v{5}.",
                _model.Name, _model.Rows, _model.Columns,
                _model.EncoderCount > 0 ? " plus " + _model.EncoderCount + " knobs" : "",
                _model.KeyPixels(0), _model.ProtocolVersion));
        }

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
            for (int i = 0; i < _tiles.Count; i++)
            {
                using (var bmp = DeckController.RenderNormalKey(_layout, _profile, page, i))
                    _tiles[i].Image = ToImageSource(bmp);
                _tiles[i].IsNav = _layout.IsNavKey(i, NavReserved);
                _tiles[i].IsSelected = (i == _selectedGrid);
            }

            var current = SelectedPage;
            for (int i = 0; i < _knobs.Count; i++)
            {
                _knobs[i].Label = (current != null && i < current.Encoders.Count)
                    ? current.Encoders[i].Label : "";
                _knobs[i].IsSelected = (i == _selectedEncoder);
            }

            int usable = _layout.ActionKeyCount(NavReserved);
            GridHint.Text = NavReserved
                ? string.Format("Click a key to edit it. The right column is reserved for page controls, {0} action keys.", usable)
                : string.Format("Click a key to edit it. All {0} keys are available; change pages on the left.", usable);
        }

        void RefreshTile(int grid)
        {
            if (grid < 0 || grid >= _tiles.Count) return;
            using (var bmp = DeckController.RenderNormalKey(_layout, _profile, SelectedPageIndex, grid))
                _tiles[grid].Image = ToImageSource(bmp);
        }

        void LoadInspector()
        {
            _suppress = true;
            try
            {
                if (_selectedEncoder >= 0)
                {
                    LoadEncoderInspector();
                    return;
                }

                EncoderFields.Visibility = Visibility.Collapsed;
                KeyFields.Visibility = Visibility.Visible;

                if (_selectedGrid < 0)
                {
                    KeyTitle.Text = "No key selected";
                    KeyHint.Text = "Select a key in the grid.";
                    KeyFields.IsEnabled = false;
                    return;
                }

                int row = _layout.RowOf(_selectedGrid), col = _layout.ColumnOf(_selectedGrid);
                KeyTitle.Text = string.Format("Row {0}, column {1}", row + 1, col + 1);

                if (_layout.IsNavKey(_selectedGrid, NavReserved))
                {
                    KeyHint.Text = _selectedGrid == _layout.PageUpKey ? "Reserved: page up."
                        : _selectedGrid == _layout.PageDownKey ? "Reserved: page down."
                        : "Reserved: page indicator. Press it on the deck to open the page selector, "
                          + "press again to cancel.";
                    KeyFields.IsEnabled = false;
                    return;
                }

                var key = SelectedKey;
                if (key == null) { KeyFields.IsEnabled = false; return; }

                bool pressable = _layout.IsPressable(_selectedGrid);
                KeyHint.Text = !pressable
                    ? "Display only. This slot has a screen but no switch under it, so it can "
                      + "show status but will never fire an event."
                    : _layout.HasScreen(_selectedGrid)
                        ? "Action key."
                        : "Action key with no screen behind it: it fires events but cannot be drawn on.";
                KeyFields.IsEnabled = true;
                ActionBox.IsEnabled = pressable;
                EventBox.IsEnabled = pressable;

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

        void LoadEncoderInspector()
        {
            KeyFields.Visibility = Visibility.Collapsed;
            EncoderFields.Visibility = Visibility.Visible;

            var enc = SelectedEncoder;
            KeyTitle.Text = "Knob " + (_selectedEncoder + 1);
            if (enc == null)
            {
                KeyHint.Text = "Not available on this page.";
                EncoderFields.IsEnabled = false;
                return;
            }

            KeyHint.Text = "Turning and pressing fire separate events.";
            EncoderFields.IsEnabled = true;
            EncLabelBox.Text = enc.Label ?? "";
            EncLeftBox.Text = enc.TurnLeftEvent ?? "";
            EncRightBox.Text = enc.TurnRightEvent ?? "";
            EncPressBox.Text = enc.PressEvent ?? "";
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
                var found = AjazzStreamDock.Enumerate();
                if (found.Count == 0)
                    throw new InvalidOperationException(
                        "No deck found. " + DeckModel.All.Count +
                        " models are recognised; if yours is not one of them it will not appear here.");

                var deck = AjazzStreamDock.Open(found[0]);

                // The deck that is actually plugged in wins over the picker.
                if (deck.Model != _model)
                {
                    _model = deck.Model;
                    _suppress = true;
                    DeviceBox.SelectedItem = _model;
                    _suppress = false;
                    RebuildForModel();
                    RefreshAll();
                }

                Attach(deck);
                Status("Connected: " + _device.Description);
            }
            catch (Exception ex)
            {
                Disconnect();
                Status("Connect failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Could not open the deck",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Runs the currently selected deck on screen. It goes through the same controller
        /// as the hardware, so page navigation, image pushing and events all behave the
        /// same; there is simply no USB at the other end.
        /// </summary>
        void Virtual_Click(object sender, RoutedEventArgs e)
        {
            if (_virtualWindow != null) { _virtualWindow.Activate(); return; }
            if (_ctrl != null) Disconnect();

            try
            {
                var deck = new VirtualDeck(_model);
                _virtualWindow = new VirtualDeckWindow(deck) { Owner = this };
                _virtualWindow.Closed += (s2, e2) =>
                {
                    _virtualWindow = null;
                    if (_device is VirtualDeck) Disconnect();
                };
                _virtualWindow.Show();

                Attach(deck);
                Status("Virtual deck running: " + _device.Description);
            }
            catch (Exception ex)
            {
                Disconnect();
                Status("Virtual deck failed: " + ex.Message);
            }
        }

        /// <summary>Wires a device to a controller and hands it the current profile.</summary>
        void Attach(IDeckDevice device)
        {
            _device = device;
            _ctrl = new DeckController(device);
            _ctrl.KeyPressed += (s, ev) => Dispatcher.Invoke(() =>
            {
                EventLog.Text = ev.ToString();
                SelectGrid(ev.KeyIndex);
            });
            _ctrl.EncoderTriggered += (s, ev) => Dispatcher.Invoke(() =>
            {
                EventLog.Text = ev.ToString();
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
            ConnectBtn.IsEnabled = !(device is VirtualDeck);
            VirtualBtn.Content = device is VirtualDeck ? "Show virtual deck" : "Virtual deck";
            DeviceBox.IsEnabled = false;
        }

        void Disconnect()
        {
            try { if (_ctrl != null) _ctrl.Dispose(); } catch { }
            try { if (_device != null) _device.Dispose(); } catch { }
            _ctrl = null;
            _device = null;
            if (_virtualWindow != null)
            {
                var w = _virtualWindow;
                _virtualWindow = null;
                try { w.Close(); } catch { }
            }
            ConnectBtn.Content = "Connect deck";
            ConnectBtn.IsEnabled = true;
            VirtualBtn.Content = "Virtual deck";
            DeviceBox.IsEnabled = true;
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
                ? string.Format("Right column reserved for page controls, {0} action keys.",
                    _layout.ActionKeyCount(true))
                : string.Format("All {0} keys usable. Change pages here in Deck Commander.",
                    _layout.TotalKeys));
        }

        void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BrightnessText == null) return;
            int v = (int)e.NewValue;
            BrightnessText.Text = v.ToString();
            if (!_ready) return;
            _profile.Brightness = v;
            if (_device != null)
            {
                try { _device.SetBrightness(v); } catch { }
            }
        }

        // ---------- grid ----------

        void Tile_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            SelectGrid((int)border.Tag);
        }

        void Encoder_Click(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            SelectEncoder((int)border.Tag);
        }

        void SelectGrid(int grid)
        {
            _selectedGrid = grid;
            _selectedEncoder = -1;
            for (int i = 0; i < _tiles.Count; i++) _tiles[i].IsSelected = (i == grid);
            for (int i = 0; i < _knobs.Count; i++) _knobs[i].IsSelected = false;
            LoadInspector();
        }

        void SelectEncoder(int encoder)
        {
            _selectedEncoder = encoder;
            _selectedGrid = -1;
            for (int i = 0; i < _tiles.Count; i++) _tiles[i].IsSelected = false;
            for (int i = 0; i < _knobs.Count; i++) _knobs[i].IsSelected = (i == encoder);
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

        void Encoder_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppress || !_ready) return;
            var enc = SelectedEncoder;
            if (enc == null) return;

            enc.Label = EncLabelBox.Text;
            enc.TurnLeftEvent = EncLeftBox.Text;
            enc.TurnRightEvent = EncRightBox.Text;
            enc.PressEvent = EncPressBox.Text;

            if (_selectedEncoder >= 0 && _selectedEncoder < _knobs.Count)
                _knobs[_selectedEncoder].Label = enc.Label;

            if (_ctrl != null && _model.HasEncoderScreens)
            {
                try { _ctrl.Render(false); } catch (Exception ex) { Status("Deck write failed: " + ex.Message); }
            }
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
            if (page == null || _selectedGrid < 0 || _selectedGrid >= page.Keys.Count) return;
            if (_layout.IsNavKey(_selectedGrid, NavReserved)) return;
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
            if (_layout.HasPageIndicator) RefreshTile(_layout.PageIndicatorKey);
        }

        DeckPage NewPage(string name)
        {
            var page = new DeckPage { Name = name };
            page.Normalise(_layout.Rows, _layout.Columns,
                _layout.Rows, _layout.Columns, _model.EncoderCount);
            return page;
        }

        void AddPage_Click(object sender, RoutedEventArgs e)
        {
            _profile.Pages.Add(NewPage("Page " + (_profile.Pages.Count + 1)));
            RefreshPageList();
            PageList.SelectedIndex = _profile.Pages.Count - 1;
            SyncDeckProfile();
        }

        void DuplicatePage_Click(object sender, RoutedEventArgs e)
        {
            var page = SelectedPage;
            if (page == null) return;
            var copy = NewPage(page.Name + " copy");
            for (int i = 0; i < page.Keys.Count && i < copy.Keys.Count; i++)
                copy.Keys[i] = page.Keys[i].Clone();
            for (int i = 0; i < page.Encoders.Count && i < copy.Encoders.Count; i++)
                copy.Encoders[i] = page.Encoders[i].Clone();
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
            _profile.Normalise(_model);
            _path = null;
            _selectedGrid = -1;
            _selectedEncoder = -1;
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
                // Fit whatever the profile was written for onto the deck in front of us.
                var loaded = ProfileStore.Load(dlg.FileName, _model);
                if (loaded == null) { Status("Nothing loaded."); return; }
                _profile = loaded;
                _path = dlg.FileName;
                _selectedGrid = -1;
                _selectedEncoder = -1;
                Title = "Deck Commander - " + Path.GetFileName(_path);
                _suppress = true;
                BrightnessSlider.Value = _profile.Brightness;
                ReserveNavBox.IsChecked = _profile.ReservePageColumn;
                _suppress = false;
                RebuildForModel();
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
