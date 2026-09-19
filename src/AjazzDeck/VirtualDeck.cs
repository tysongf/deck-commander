using System;
using System.Drawing;
using System.IO;

namespace AjazzDeck
{
    /// <summary>
    /// A deck with no hardware behind it. It accepts exactly what the real transport
    /// accepts, keeps the last image pushed to each key, and raises the same input events
    /// when something calls <see cref="Press"/>.
    /// </summary>
    /// <remarks>
    /// Useful for three things: building a profile for a deck you do not own, running the
    /// whole system with no deck plugged in at all, and checking that a model's key map and
    /// orientation are right, since an image that comes out sideways here would come out
    /// sideways on the metal.
    /// </remarks>
    public sealed class VirtualDeck : IDeckDevice
    {
        readonly byte[][] _keyJpeg;
        readonly byte[][] _encoderJpeg;
        bool _running;

        public DeckModel Model { get; private set; }

        public string Description
        {
            get
            {
                return string.Format("Virtual {0} ({1}x{2}, {3} keys{4})",
                    Model.Name, Model.Rows, Model.Columns, Model.PressableKeys,
                    Model.EncoderCount > 0 ? ", " + Model.EncoderCount + " knobs" : "");
            }
        }

        public int Brightness { get; private set; }

        public event EventHandler<DeckKeyEventArgs> KeyChanged;
        public event EventHandler<DeckEncoderEventArgs> EncoderChanged;

        /// <summary>Raised with a grid index whenever that key's image changes. -1 means all.</summary>
        public event Action<int> KeyImageChanged;

        /// <summary>Raised with an encoder index whenever a knob screen changes.</summary>
        public event Action<int> EncoderImageChanged;

        /// <summary>Raised when the brightness changes, so a viewer can dim itself.</summary>
        public event Action<int> BrightnessChanged;

        public VirtualDeck(DeckModel model)
        {
            if (model == null) throw new ArgumentNullException("model");
            Model = model;
            _keyJpeg = new byte[model.KeyCount][];
            _encoderJpeg = new byte[Math.Max(1, model.EncoderCount)][];
            Brightness = 60;
        }

        // ---------- what a viewer reads ----------

        /// <summary>The JPEG last pushed to a key, exactly as the hardware received it.</summary>
        public byte[] GetKeyJpeg(int grid)
        {
            return (grid >= 0 && grid < _keyJpeg.Length) ? _keyJpeg[grid] : null;
        }

        public byte[] GetEncoderJpeg(int encoder)
        {
            return (encoder >= 0 && encoder < _encoderJpeg.Length) ? _encoderJpeg[encoder] : null;
        }

        /// <summary>
        /// The key's image turned the right way up, as the panel would display it. Returns
        /// null when nothing has been pushed to that key yet.
        /// </summary>
        public Bitmap GetKeyBitmap(int grid)
        {
            return Decode(GetKeyJpeg(grid));
        }

        public Bitmap GetEncoderBitmap(int encoder)
        {
            return Decode(GetEncoderJpeg(encoder));
        }

        Bitmap Decode(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length == 0) return null;
            try
            {
                using (var ms = new MemoryStream(jpeg))
                using (var raw = new Bitmap(ms))
                {
                    var bmp = new Bitmap(raw);
                    bmp.RotateFlip(DeckImage.InverseOrientation(Model.Orientation));
                    return bmp;
                }
            }
            catch { return null; }
        }

        // ---------- what a viewer calls ----------

        /// <summary>Presses a key, exactly as the hardware would report it.</summary>
        public void Press(int grid)
        {
            if (!_running) return;
            if (!Model.IsPressable(grid)) return;

            var h = KeyChanged;
            if (h == null) return;

            h(this, new DeckKeyEventArgs
            {
                Key = grid,
                Row = Model.RowOf(grid),
                Column = Model.ColumnOf(grid),
                DeviceKey = Model.GridToDeviceKey(grid),
                Pressed = true,
                Raw = null
            });

            // Protocol v3 devices report the release too, so emulate that faithfully.
            if (Model.ReportsKeyUp)
                h(this, new DeckKeyEventArgs
                {
                    Key = grid,
                    Row = Model.RowOf(grid),
                    Column = Model.ColumnOf(grid),
                    DeviceKey = Model.GridToDeviceKey(grid),
                    Pressed = false,
                    Raw = null
                });
        }

        /// <summary>Turns a knob. <paramref name="delta"/> is -1 for left, +1 for right.</summary>
        public void Turn(int encoder, int delta)
        {
            if (!_running || encoder < 0 || encoder >= Model.EncoderCount) return;
            var h = EncoderChanged;
            if (h != null)
                h(this, new DeckEncoderEventArgs
                { Encoder = encoder, Delta = delta < 0 ? -1 : 1, Pressed = true });
        }

        /// <summary>Presses a knob.</summary>
        public void PressEncoder(int encoder)
        {
            if (!_running || encoder < 0 || encoder >= Model.EncoderCount) return;
            var h = EncoderChanged;
            if (h != null)
                h(this, new DeckEncoderEventArgs
                { Encoder = encoder, Delta = 0, Pressed = true });
        }

        // ---------- IDeckDevice ----------

        public void Initialize(int brightness)
        {
            SetBrightness(brightness);
            ClearAll();
        }

        public void Start() { _running = true; }
        public void Stop() { _running = false; }

        public void SetBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            if (Brightness == percent) return;
            Brightness = percent;
            var h = BrightnessChanged;
            if (h != null) h(percent);
        }

        public void SetKeyImageJpeg(int grid, byte[] jpeg)
        {
            if (!Model.HasScreen(grid))
                throw new InvalidOperationException(
                    string.Format("Key {0} on the {1} has no screen.", grid, Model.Name));
            _keyJpeg[grid] = jpeg;
            var h = KeyImageChanged;
            if (h != null) h(grid);
        }

        public void SetEncoderImageJpeg(int encoder, byte[] jpeg)
        {
            if (!Model.HasEncoderScreens)
                throw new InvalidOperationException(Model.Name + " has no knob screens.");
            if (encoder < 0 || encoder >= Model.EncoderCount)
                throw new ArgumentOutOfRangeException("encoder");
            _encoderJpeg[encoder] = jpeg;
            var h = EncoderImageChanged;
            if (h != null) h(encoder);
        }

        public void ClearKey(int grid)
        {
            if (grid < 0 || grid >= _keyJpeg.Length) return;
            _keyJpeg[grid] = null;
            var h = KeyImageChanged;
            if (h != null) h(grid);
        }

        public void ClearAll()
        {
            for (int i = 0; i < _keyJpeg.Length; i++) _keyJpeg[i] = null;
            for (int i = 0; i < _encoderJpeg.Length; i++) _encoderJpeg[i] = null;
            var h = KeyImageChanged;
            if (h != null) h(-1);
        }

        /// <summary>Nothing is buffered, so there is nothing to flush.</summary>
        public void Commit() { }

        public void Dispose() { Stop(); }
    }
}
