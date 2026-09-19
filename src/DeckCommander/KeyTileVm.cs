using System.ComponentModel;
using System.Windows.Media;

namespace DeckCommander
{
    /// <summary>One tile in the on-screen deck preview.</summary>
    public sealed class KeyTileVm : INotifyPropertyChanged
    {
        ImageSource _image;
        bool _selected;
        bool _isNav;
        bool _hasScreen = true;
        bool _isPressable = true;
        double _tileWidth = 112;
        double _tileHeight = 112;

        public int GridIndex { get; set; }

        /// <summary>
        /// True when this key is currently acting as a page control. Must raise change
        /// notification: the template trigger that draws the RESERVED badge binds to it,
        /// and it flips whenever the navigation column is toggled.
        /// </summary>
        public bool IsNav
        {
            get { return _isNav; }
            set { if (_isNav != value) { _isNav = value; Raise("IsNav"); } }
        }

        /// <summary>
        /// False for keys that are pressable but have no display behind them, such as the
        /// bottom row of an AKP03. Those keys still fire events, they just cannot be drawn.
        /// </summary>
        public bool HasScreen
        {
            get { return _hasScreen; }
            set { if (_hasScreen != value) { _hasScreen = value; Raise("HasScreen"); } }
        }

        /// <summary>
        /// False for slots with a screen but no switch, such as the AKP153's narrow sixth
        /// column. They can be drawn on but will never fire an event.
        /// </summary>
        public bool IsPressable
        {
            get { return _isPressable; }
            set { if (_isPressable != value) { _isPressable = value; Raise("IsPressable"); } }
        }

        /// <summary>
        /// Preview size in device-independent pixels, scaled from the real screen size so
        /// a narrow sixth column looks narrow here too.
        /// </summary>
        public double TileWidth
        {
            get { return _tileWidth; }
            set { if (_tileWidth != value) { _tileWidth = value; Raise("TileWidth"); } }
        }

        public double TileHeight
        {
            get { return _tileHeight; }
            set { if (_tileHeight != value) { _tileHeight = value; Raise("TileHeight"); } }
        }

        public ImageSource Image
        {
            get { return _image; }
            set { _image = value; Raise("Image"); }
        }

        public bool IsSelected
        {
            get { return _selected; }
            set { if (_selected != value) { _selected = value; Raise("IsSelected"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string n)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>One knob in the on-screen preview, for the decks that have them.</summary>
    public sealed class EncoderTileVm : INotifyPropertyChanged
    {
        string _label = "";
        bool _selected;

        public int EncoderIndex { get; set; }

        public string Caption { get { return "Knob " + (EncoderIndex + 1); } }

        public string Label
        {
            get { return _label; }
            set { _label = value; Raise("Label"); }
        }

        public bool IsSelected
        {
            get { return _selected; }
            set { if (_selected != value) { _selected = value; Raise("IsSelected"); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        void Raise(string n)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(n));
        }
    }
}
