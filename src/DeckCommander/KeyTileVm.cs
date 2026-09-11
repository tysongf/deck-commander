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
}
