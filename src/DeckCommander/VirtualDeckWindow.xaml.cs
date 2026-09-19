using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AjazzDeck;

namespace DeckCommander
{
    /// <summary>
    /// Shows a <see cref="VirtualDeck"/> and turns clicks into key presses. Purely a
    /// viewer: it reads the images the controller pushed and calls Press, so the deck
    /// behaves exactly as the hardware does, page navigation and all.
    /// </summary>
    public partial class VirtualDeckWindow : Window
    {
        readonly ObservableCollection<KeyTileVm> _tiles = new ObservableCollection<KeyTileVm>();
        readonly ObservableCollection<KnobVm> _knobs = new ObservableCollection<KnobVm>();
        readonly VirtualDeck _deck;

        public static readonly DependencyProperty GridRowsProperty =
            DependencyProperty.Register("GridRows", typeof(int), typeof(VirtualDeckWindow),
                new PropertyMetadata(3));

        public static readonly DependencyProperty GridColumnsProperty =
            DependencyProperty.Register("GridColumns", typeof(int), typeof(VirtualDeckWindow),
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

        public VirtualDeckWindow(VirtualDeck deck)
        {
            if (deck == null) throw new ArgumentNullException("deck");
            InitializeComponent();
            _deck = deck;

            var model = deck.Model;
            GridRows = model.Rows;
            GridColumns = model.Columns;
            Title = "Virtual Deck - " + model.Name;
            Caption.Text = deck.Description;

            // Keep the relative screen sizes, so a narrow sixth column looks narrow.
            int maxPx = 1;
            for (int i = 0; i < model.KeyCount; i++)
                maxPx = Math.Max(maxPx, model.KeyPixels(i));
            double cell = Math.Max(48.0, Math.Min(104.0, 640.0 / model.Columns - 10));

            for (int i = 0; i < model.KeyCount; i++)
            {
                double f = model.KeyPixels(i) / (double)maxPx;
                _tiles.Add(new KeyTileVm
                {
                    GridIndex = i,
                    HasScreen = model.HasScreen(i),
                    IsPressable = model.IsPressable(i),
                    TileWidth = Math.Round(cell * f),
                    TileHeight = Math.Round(cell * f)
                });
            }
            KeyGrid.ItemsSource = _tiles;

            for (int i = 0; i < model.EncoderCount; i++)
                _knobs.Add(new KnobVm { EncoderIndex = i });
            KnobStrip.ItemsSource = _knobs;
            KnobStrip.Visibility = model.EncoderCount > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (!model.HasEncoderScreens && model.EncoderCount > 0)
                Hint.Text += " This deck's knobs have no screens, so those tiles stay blank.";

            _deck.KeyImageChanged += OnKeyImageChanged;
            _deck.EncoderImageChanged += OnEncoderImageChanged;
            _deck.BrightnessChanged += OnBrightnessChanged;

            RefreshAll();
            OnBrightnessChanged(_deck.Brightness);
        }

        // ---------- image plumbing ----------

        static ImageSource ToImageSource(Bitmap bmp)
        {
            if (bmp == null) return null;
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

        void RefreshAll()
        {
            for (int i = 0; i < _tiles.Count; i++) RefreshKey(i);
            for (int i = 0; i < _knobs.Count; i++) RefreshKnob(i);
        }

        void RefreshKey(int grid)
        {
            if (grid < 0 || grid >= _tiles.Count) return;
            using (var bmp = _deck.GetKeyBitmap(grid))
                _tiles[grid].Image = ToImageSource(bmp);
        }

        void RefreshKnob(int encoder)
        {
            if (encoder < 0 || encoder >= _knobs.Count) return;
            using (var bmp = _deck.GetEncoderBitmap(encoder))
                _knobs[encoder].Image = ToImageSource(bmp);
        }

        void OnKeyImageChanged(int grid)
        {
            // The controller pushes from its own thread; marshal before touching the VM.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (grid < 0) RefreshAll(); else RefreshKey(grid);
            }));
        }

        void OnEncoderImageChanged(int encoder)
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshKnob(encoder)));
        }

        void OnBrightnessChanged(int percent)
        {
            // The panel really does get dimmer, so show that rather than pretending.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Panel.Opacity = 0.25 + 0.75 * (percent / 100.0);
            }));
        }

        // ---------- input ----------

        void Key_Down(object sender, MouseButtonEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            int grid = (int)border.Tag;
            if (grid < 0 || grid >= _tiles.Count || !_tiles[grid].IsPressable) return;

            _tiles[grid].IsSelected = true;
            _deck.Press(grid);
        }

        void Key_Up(object sender, MouseEventArgs e)
        {
            var border = sender as Border;
            if (border == null) return;
            int grid = (int)border.Tag;
            if (grid >= 0 && grid < _tiles.Count) _tiles[grid].IsSelected = false;
        }

        static int TagOf(object sender)
        {
            var b = sender as Button;
            return b == null ? -1 : (int)b.Tag;
        }

        void Knob_Left(object sender, RoutedEventArgs e) { _deck.Turn(TagOf(sender), -1); }
        void Knob_Right(object sender, RoutedEventArgs e) { _deck.Turn(TagOf(sender), +1); }
        void Knob_Press(object sender, RoutedEventArgs e) { _deck.PressEncoder(TagOf(sender)); }

        protected override void OnClosed(EventArgs e)
        {
            _deck.KeyImageChanged -= OnKeyImageChanged;
            _deck.EncoderImageChanged -= OnEncoderImageChanged;
            _deck.BrightnessChanged -= OnBrightnessChanged;
            base.OnClosed(e);
        }
    }

    /// <summary>One knob in the virtual deck window.</summary>
    public sealed class KnobVm : System.ComponentModel.INotifyPropertyChanged
    {
        ImageSource _image;
        public int EncoderIndex { get; set; }

        public ImageSource Image
        {
            get { return _image; }
            set { _image = value; Raise("Image"); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        void Raise(string n)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new System.ComponentModel.PropertyChangedEventArgs(n));
        }
    }
}
