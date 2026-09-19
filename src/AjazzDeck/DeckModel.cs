using System;
using System.Collections.Generic;
using System.Drawing;

namespace AjazzDeck
{
    /// <summary>What a single input report described.</summary>
    public enum DeckInputKind
    {
        None = 0,
        /// <summary>A key in the display grid. <see cref="DeckInput.Index"/> is the grid index.</summary>
        Key,
        /// <summary>An encoder (knob) pressed. <see cref="DeckInput.Index"/> is the encoder number.</summary>
        EncoderPress,
        /// <summary>An encoder turned. <see cref="DeckInput.Delta"/> is -1 or +1.</summary>
        EncoderTurn,
        /// <summary>The device reported "nothing is held" (protocol v1 release sentinel).</summary>
        Release
    }

    public struct DeckInput
    {
        public DeckInputKind Kind;
        public int Index;
        public int Delta;
    }

    /// <summary>How an input report's code is turned into a grid or encoder number.</summary>
    enum InputScheme
    {
        /// <summary>The code is the 1-based hardware key number (AKP153 / 293V3 families).</summary>
        DeviceKeyNumber,
        /// <summary>Six display keys, three plain buttons, three encoders (AKP03 family).</summary>
        Akp03,
        /// <summary>Ten display keys and four encoders (AKP05 family).</summary>
        Akp05
    }

    /// <summary>
    /// One supported device: its geometry, its wire protocol, and the two mappings that
    /// differ between families - grid position to hardware key number, and input code to
    /// grid position. Everything the rest of the code needs to vary per device lives here.
    /// </summary>
    /// <remarks>
    /// Only the Ajazz AKP153E (0300:1010, protocol v1) has been verified against hardware.
    /// Every other entry is transcribed from the OpenDeck plugin family and the mirajazz
    /// library; see README.md for the sources.
    /// </remarks>
    public sealed class DeckModel
    {
        public string Name { get; private set; }
        public ushort VendorId { get; private set; }
        public ushort ProductId { get; private set; }

        /// <summary>1, 2 or 3. Decides packet size, clear semantics and key-up reporting.</summary>
        public int ProtocolVersion { get; private set; }

        /// <summary>Rows and columns of the editable key grid.</summary>
        public int Rows { get; private set; }
        public int Columns { get; private set; }

        public int EncoderCount { get; private set; }

        /// <summary>Payload bytes per report, excluding the leading report id.</summary>
        public int PacketSize { get { return ProtocolVersion >= 2 ? 1024 : 512; } }

        /// <summary>v3 devices report press and release; v1 and v2 send one event per press.</summary>
        public bool ReportsKeyUp { get { return ProtocolVersion >= 3; } }

        /// <summary>Clearing every key needs an explicit flush on v2 and v3.</summary>
        public bool ClearNeedsCommit { get { return ProtocolVersion >= 2; } }

        /// <summary>These devices drop the connection without periodic traffic.</summary>
        public bool NeedsKeepAlive { get; private set; }

        /// <summary>Pre-rotation applied to every key image before it is sent.</summary>
        public RotateFlipType Orientation { get; private set; }

        int _keyPixels;
        int _narrowPixels;
        int[] _narrowGrids;
        int[] _blindGrids;     // slots with no screen behind them
        int[] _inertGrids;     // slots with no switch under them
        int[] _gridToDevice;   // grid index -> 0-based hardware key; null means identity
        int[] _deviceToGrid;   // 0-based hardware key -> grid index; null means identity
        int[] _encoderToDevice;
        InputScheme _scheme;

        /// <summary>Total grid slots, including any that are display-only or screenless.</summary>
        public int KeyCount { get { return Rows * Columns; } }

        static bool In(int[] set, int grid)
        {
            if (set == null) return false;
            for (int i = 0; i < set.Length; i++) if (set[i] == grid) return true;
            return false;
        }

        /// <summary>
        /// True when the slot has a screen and can be drawn on. False for the AKP03's
        /// three plain buttons, which fire events but cannot show anything.
        /// </summary>
        public bool HasScreen(int grid)
        {
            return grid >= 0 && grid < KeyCount && !In(_blindGrids, grid);
        }

        /// <summary>
        /// True when there is a switch under the slot. False for the AKP153's narrow sixth
        /// column, which is a display strip rather than a row of keys.
        /// </summary>
        public bool IsPressable(int grid)
        {
            return grid >= 0 && grid < KeyCount && !In(_inertGrids, grid);
        }

        /// <summary>How many of the grid's slots have a screen.</summary>
        public int DisplayKeys
        {
            get { return KeyCount - (_blindGrids == null ? 0 : _blindGrids.Length); }
        }

        /// <summary>How many of the grid's slots can actually be pressed.</summary>
        public int PressableKeys
        {
            get { return KeyCount - (_inertGrids == null ? 0 : _inertGrids.Length); }
        }

        /// <summary>Screen size in pixels for a grid slot. Square unless stated otherwise.</summary>
        public int KeyPixels(int grid)
        {
            if (_narrowGrids != null)
                for (int i = 0; i < _narrowGrids.Length; i++)
                    if (_narrowGrids[i] == grid) return _narrowPixels;
            return _keyPixels;
        }

        /// <summary>Encoder screen size, or Size.Empty when the encoders have no screens.</summary>
        public Size EncoderPixels { get; private set; }

        public bool HasEncoderScreens { get { return EncoderPixels.Width > 0; } }

        /// <summary>Grid index to the 1-based key number used on the wire.</summary>
        public int GridToDeviceKey(int grid)
        {
            if (grid < 0 || grid >= KeyCount) throw new ArgumentOutOfRangeException("grid");
            return (_gridToDevice == null ? grid : _gridToDevice[grid]) + 1;
        }

        /// <summary>Encoder number to the 1-based key number its screen answers to.</summary>
        public int EncoderToDeviceKey(int encoder)
        {
            if (_encoderToDevice == null || encoder < 0 || encoder >= _encoderToDevice.Length)
                throw new ArgumentOutOfRangeException("encoder");
            return _encoderToDevice[encoder] + 1;
        }

        /// <summary>Inverse of <see cref="GridToDeviceKey"/>.</summary>
        public bool TryDeviceKeyToGrid(int deviceKey, out int grid)
        {
            grid = -1;
            int zero = deviceKey - 1;
            if (zero < 0 || zero >= KeyCount) return false;
            grid = _deviceToGrid == null ? zero : _deviceToGrid[zero];
            return grid >= 0 && grid < KeyCount;
        }

        public int RowOf(int grid) { return grid / Columns; }
        public int ColumnOf(int grid) { return grid % Columns; }

        /// <summary>
        /// Decodes one input report's code byte. The families disagree completely here:
        /// the AKP153 sends a hardware key number, while the knob decks send opaque codes
        /// that mix keys, encoder presses and encoder rotation into one byte.
        /// </summary>
        public bool TryDecodeInput(byte code, out DeckInput input)
        {
            input = new DeckInput();

            switch (_scheme)
            {
                case InputScheme.DeviceKeyNumber:
                    if (code == 0) { input.Kind = DeckInputKind.Release; return true; }
                    int grid;
                    if (!TryDeviceKeyToGrid(code, out grid)) return false;
                    input.Kind = DeckInputKind.Key;
                    input.Index = grid;
                    return true;

                case InputScheme.Akp03:
                    switch (code)
                    {
                        case 0x00: input.Kind = DeckInputKind.Release; return true;
                        case 0x25: input.Kind = DeckInputKind.Key; input.Index = 6; return true;
                        case 0x30: input.Kind = DeckInputKind.Key; input.Index = 7; return true;
                        case 0x31: input.Kind = DeckInputKind.Key; input.Index = 8; return true;
                        case 0x90: return Turn(0, -1, out input);
                        case 0x91: return Turn(0, +1, out input);
                        case 0x50: return Turn(1, -1, out input);
                        case 0x51: return Turn(1, +1, out input);
                        case 0x60: return Turn(2, -1, out input);
                        case 0x61: return Turn(2, +1, out input);
                        case 0x33: return Press(0, out input);
                        case 0x35: return Press(1, out input);
                        case 0x34: return Press(2, out input);
                    }
                    if (code >= 1 && code <= 6)
                    {
                        input.Kind = DeckInputKind.Key;
                        input.Index = code - 1;
                        return true;
                    }
                    return false;

                case InputScheme.Akp05:
                    switch (code)
                    {
                        case 0xA0: return Turn(0, -1, out input);
                        case 0xA1: return Turn(0, +1, out input);
                        case 0x50: return Turn(1, -1, out input);
                        case 0x51: return Turn(1, +1, out input);
                        case 0x90: return Turn(2, -1, out input);
                        case 0x91: return Turn(2, +1, out input);
                        case 0x70: return Turn(3, -1, out input);
                        case 0x71: return Turn(3, +1, out input);
                        // The AKP05 reports an encoder press on two different codes each,
                        // depending on firmware; 0x00 is one of them, so it is not a release.
                        case 0x37: case 0x00: case 0x40: return Press(0, out input);
                        case 0x35: case 0x41: return Press(1, out input);
                        case 0x33: case 0x42: return Press(2, out input);
                        case 0x36: case 0x43: return Press(3, out input);
                    }
                    if (code >= 1 && code <= 10)
                    {
                        input.Kind = DeckInputKind.Key;
                        input.Index = code - 1;
                        return true;
                    }
                    return false;
            }
            return false;
        }

        static bool Turn(int encoder, int delta, out DeckInput input)
        {
            input = new DeckInput
            { Kind = DeckInputKind.EncoderTurn, Index = encoder, Delta = delta };
            return true;
        }

        static bool Press(int encoder, out DeckInput input)
        {
            input = new DeckInput { Kind = DeckInputKind.EncoderPress, Index = encoder };
            return true;
        }

        public override string ToString()
        {
            return string.Format("{0} ({1:X4}:{2:X4}, v{3}, {4}x{5})",
                Name, VendorId, ProductId, ProtocolVersion, Rows, Columns);
        }

        // ------------------------------------------------------------------
        // Families
        // ------------------------------------------------------------------

        // AKP153 / HSV293S: 3 rows of 6. The sixth column is a narrow strip of three
        // keys, easy to mistake for a bezel. On v3 its screens are smaller than the rest.
        static readonly int[] Akp153GridToDevice =
        { 12, 9, 6, 3, 0, 15, 13, 10, 7, 4, 1, 16, 14, 11, 8, 5, 2, 17 };
        static readonly int[] Akp153DeviceToGrid =
        { 4, 10, 16, 3, 9, 15, 2, 8, 14, 1, 7, 13, 0, 6, 12, 5, 11, 17 };
        // The sixth column is a narrow display strip, not a row of keys. Confirmed on an
        // AKP153E: it shows tiles but there is nothing to press. The OpenDeck plugin for
        // this family treats all 18 slots as keys, so a variant with switches there may
        // exist; input decoding below still accepts device keys 16-18 and maps them here,
        // and only the layout treats the column as inert. That way a deck with buttons
        // there keeps working, while a deck without them does not lose page navigation to
        // a column that cannot be pressed.
        static readonly int[] Akp153SixthColumn = { 5, 11, 17 };

        // The AKP03's bottom row is three plain buttons with no screens behind them.
        static readonly int[] Akp03PlainButtons = { 6, 7, 8 };

        // Mirabox 293 V3: images are addressed row-major from the bottom left, but key
        // presses arrive row-major from the top left. The middle row is identical either
        // way, which is what makes the asymmetry easy to miss.
        static readonly int[] V3GridToDevice =
        { 10, 11, 12, 13, 14, 5, 6, 7, 8, 9, 0, 1, 2, 3, 4 };

        // AKP05: the top row sits at the far end of the device's numbering, and the four
        // encoder screens occupy the slots below the keys.
        static readonly int[] Akp05GridToDevice = { 10, 11, 12, 13, 14, 5, 6, 7, 8, 9 };
        static readonly int[] Akp05EncoderToDevice = { 0, 1, 2, 3 };

        static DeckModel Akp153(string name, ushort vid, ushort pid, int protocol)
        {
            return new DeckModel
            {
                Name = name,
                VendorId = vid,
                ProductId = pid,
                ProtocolVersion = protocol,
                Rows = 3,
                Columns = 6,
                EncoderCount = 0,
                NeedsKeepAlive = false,
                Orientation = RotateFlipType.Rotate270FlipNone,
                _keyPixels = protocol >= 2 ? 95 : 85,
                _narrowPixels = protocol >= 2 ? 82 : 85,
                _narrowGrids = protocol >= 2 ? Akp153SixthColumn : null,
                _blindGrids = null,
                _inertGrids = Akp153SixthColumn,
                _gridToDevice = Akp153GridToDevice,
                _deviceToGrid = Akp153DeviceToGrid,
                _scheme = InputScheme.DeviceKeyNumber,
                EncoderPixels = Size.Empty
            };
        }

        static DeckModel Akp03(string name, ushort vid, ushort pid, int protocol)
        {
            return new DeckModel
            {
                Name = name,
                VendorId = vid,
                ProductId = pid,
                ProtocolVersion = protocol,
                Rows = 3,
                Columns = 3,
                EncoderCount = 3,
                NeedsKeepAlive = true,
                Orientation = protocol >= 3
                    ? RotateFlipType.Rotate90FlipNone
                    : RotateFlipType.RotateNoneFlipNone,
                _keyPixels = protocol >= 3 ? 64 : 60,
                _narrowPixels = 0,
                _narrowGrids = null,
                _blindGrids = Akp03PlainButtons,
                _inertGrids = null,
                _gridToDevice = null,
                _deviceToGrid = null,
                _scheme = InputScheme.Akp03,
                EncoderPixels = Size.Empty
            };
        }

        static DeckModel Akp05(string name, ushort vid, ushort pid)
        {
            return new DeckModel
            {
                Name = name,
                VendorId = vid,
                ProductId = pid,
                ProtocolVersion = 3,
                Rows = 2,
                Columns = 5,
                EncoderCount = 4,
                NeedsKeepAlive = true,
                Orientation = RotateFlipType.Rotate180FlipNone,
                _keyPixels = 112,
                _narrowPixels = 0,
                _narrowGrids = null,
                _blindGrids = null,
                _inertGrids = null,
                _gridToDevice = Akp05GridToDevice,
                _deviceToGrid = null,
                _encoderToDevice = Akp05EncoderToDevice,
                _scheme = InputScheme.Akp05,
                EncoderPixels = new Size(176, 112)
            };
        }

        static DeckModel Mirabox293V3()
        {
            return new DeckModel
            {
                Name = "Mirabox Stream Dock 293 V3",
                VendorId = 0x6603,
                ProductId = 0x1006,
                ProtocolVersion = 3,
                Rows = 3,
                Columns = 5,
                EncoderCount = 0,
                NeedsKeepAlive = true,
                Orientation = RotateFlipType.Rotate180FlipNone,
                _keyPixels = 112,
                _narrowPixels = 0,
                _narrowGrids = null,
                _blindGrids = null,
                _inertGrids = null,
                _gridToDevice = V3GridToDevice,
                _deviceToGrid = null,     // presses already arrive row-major from top left
                _scheme = InputScheme.DeviceKeyNumber,
                EncoderPixels = Size.Empty
            };
        }

        // ------------------------------------------------------------------
        // Catalogue
        // ------------------------------------------------------------------

        static readonly DeckModel[] _all =
        {
            // --- AKP153 / HSV293S family, protocol v1 ---
            Akp153("Ajazz AKP153E",                  0x0300, 0x1010, 1),
            Akp153("Ajazz AKP153R",                  0x0300, 0x1020, 1),
            Akp153("Ajazz AKP153",                   0x5548, 0x6674, 1),
            Akp153("Mirabox HSV293S",                0x5548, 0x6670, 1),
            Akp153("Mars Gaming MSD-ONE",            0x0B00, 0x1000, 1),
            Akp153("Mad Dog GK150K",                 0x0C00, 0x1000, 1),
            Akp153("Risemode Vision 01",             0x0A00, 0x1001, 1),
            Akp153("TMICE Stream Controller",        0x0500, 0x1001, 1),
            Akp153("Womier D15",                     0x0600, 0x1000, 1),
            Akp153("Monstargear MonstarDeck TS115",  0x0400, 0x1000, 1),

            // --- AKP153 / HSV293S family, protocol v3 ---
            Akp153("Ajazz AKP153E (rev. 2)",         0x0300, 0x3010, 3),
            Akp153("Ajazz AKP153R (rev. 2)",         0x0300, 0x3011, 3),
            Akp153("Mirabox HSV293SV3",              0x6603, 0x1014, 3),
            Akp153("Mirabox HSV293SV3",              0x6603, 0x1005, 3),
            Akp153("Mars Gaming MSD-ONE",            0x0B00, 0x1005, 3),
            Akp153("Soomfon Stream Controller",      0x1500, 0x3003, 3),
            Akp153("Streonor S15",                   0x1500, 0x3005, 3),

            // --- AKP03 / N3 family, protocol v2 ---
            Akp03("Ajazz AKP03",                     0x0300, 0x1001, 2),
            Akp03("Ajazz AKP03E",                    0x0300, 0x1002, 2),
            Akp03("Ajazz AKP03R",                    0x0300, 0x1003, 2),
            Akp03("Mirabox N3",                      0x6602, 0x1000, 2),
            Akp03("Mirabox N3",                      0x6602, 0x1002, 2),
            Akp03("Mars Gaming MSD-TWO",             0x0B00, 0x1001, 2),

            // --- AKP03 / N3 family, protocol v3 ---
            Akp03("Ajazz AKP03E (rev. 2)",           0x0300, 0x3002, 3),
            Akp03("Ajazz AKP03R (rev. 2)",           0x0300, 0x3003, 3),
            Akp03("Mirabox N3",                      0x6603, 0x1002, 3),
            Akp03("Mirabox N3",                      0x6603, 0x1003, 3),
            Akp03("Soomfon Stream Controller SE",    0x1500, 0x3001, 3),
            Akp03("TreasLin N3",                     0x5548, 0x1001, 3),
            Akp03("Redragon Skyrider SS-551",        0x0200, 0x2000, 3),

            // --- AKP05 / N4 family, protocol v3 ---
            Akp05("Ajazz AKP05",                     0x0300, 0x3006),
            Akp05("Ajazz AKP05E",                    0x0300, 0x3004),
            Akp05("Ajazz AKP05E Pro",                0x0300, 0x3013),
            Akp05("Ajazz AKP05CN Pro",               0x0300, 0x3014),
            Akp05("Mirabox N4",                      0x6602, 0x1001),
            Akp05("Mirabox N4E",                     0x6603, 0x1007),
            Akp05("Mirabox N4 Pro",                  0x5548, 0x1008),
            Akp05("Mirabox N4 Pro E",                0x5548, 0x1021),
            Akp05("VSDInside N4",                    0x5548, 0x1004),
            Akp05("VSDInside N4 Pro",                0x5548, 0x1023),
            Akp05("Mars Gaming MSD-Pro",             0x0B00, 0x1003),
            Akp05("Soomfon CN003",                   0x1500, 0x3002),
            Akp05("Streonor S10 Pro",                0x1500, 0x3007),
            Akp05("Redragon SS552",                  0x0200, 0x3001),

            // --- one-off ---
            Mirabox293V3()
        };

        public static IList<DeckModel> All { get { return Array.AsReadOnly(_all); } }

        /// <summary>The model used by the editor when no deck is attached.</summary>
        public static DeckModel Default { get { return Find(0x0300, 0x1010); } }

        public static DeckModel Find(ushort vendorId, ushort productId)
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].VendorId == vendorId && _all[i].ProductId == productId)
                    return _all[i];
            return null;
        }

        /// <summary>Every vendor id in the catalogue, for HID enumeration.</summary>
        public static ushort[] VendorIds()
        {
            var seen = new List<ushort>();
            for (int i = 0; i < _all.Length; i++)
                if (!seen.Contains(_all[i].VendorId)) seen.Add(_all[i].VendorId);
            return seen.ToArray();
        }

        /// <summary>Stable key for storing a model in a profile, e.g. "0300:1010".</summary>
        public string Key { get { return string.Format("{0:x4}:{1:x4}", VendorId, ProductId); } }

        public static DeckModel FromKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var parts = key.Split(':');
            if (parts.Length != 2) return null;
            try
            {
                return Find(Convert.ToUInt16(parts[0], 16), Convert.ToUInt16(parts[1], 16));
            }
            catch { return null; }
        }
    }
}
