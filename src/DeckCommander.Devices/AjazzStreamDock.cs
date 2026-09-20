using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DeckCommander.Devices
{
    public sealed class DeckInfo
    {
        public string Path { get; set; }
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public string Serial { get; set; }
        public ushort UsagePage { get; set; }
        public int InputReportLength { get; set; }
        public int OutputReportLength { get; set; }

        /// <summary>The catalogue entry this device matched, or null if it is unrecognised.</summary>
        public DeckModel Model { get; set; }

        public override string ToString()
        {
            return string.Format("{0} VID_{1:X4}&PID_{2:X4} serial={3} usagePage=0x{4:X4} in={5} out={6}",
                Model != null ? Model.Name : "Unknown device",
                VendorId, ProductId, Serial, UsagePage, InputReportLength, OutputReportLength);
        }
    }

    public sealed class DeckKeyEventArgs : EventArgs
    {
        /// <summary>Row-major grid index, top-left = 0.</summary>
        public int Key { get; set; }
        /// <summary>Row, 0 = top.</summary>
        public int Row { get; set; }
        /// <summary>Column, 0 = left.</summary>
        public int Column { get; set; }
        /// <summary>Raw key number as reported by the hardware.</summary>
        public int DeviceKey { get; set; }
        /// <summary>True for a press. Only protocol v3 devices also report the release.</summary>
        public bool Pressed { get; set; }
        public byte[] Raw { get; set; }
        public override string ToString()
        {
            return string.Format("key {0} (row {1}, col {2}, device {3}){4}",
                Key, Row, Column, DeviceKey, Pressed ? "" : " up");
        }
    }

    public sealed class DeckEncoderEventArgs : EventArgs
    {
        /// <summary>Encoder number, 0 = leftmost.</summary>
        public int Encoder { get; set; }
        /// <summary>-1 or +1 for a turn, 0 for a press.</summary>
        public int Delta { get; set; }
        public bool Pressed { get; set; }
        public bool IsTurn { get { return Delta != 0; } }
        public byte[] Raw { get; set; }
        public override string ToString()
        {
            return IsTurn
                ? string.Format("encoder {0} turn {1:+#;-#}", Encoder, Delta)
                : string.Format("encoder {0} press", Encoder);
        }
    }

    /// <summary>
    /// Ajazz / Mirabox "Stream Dock" family. Vendor-defined HID, zero-padded packets with
    /// an ASCII "CRT" header. Geometry, image format and key numbering all come from the
    /// matched <see cref="DeckModel"/>, so one class serves every device in the catalogue.
    /// </summary>
    public sealed class AjazzStreamDock : IDeckDevice
    {
        /// <summary>Ajazz's own USB vendor id. Rebadged decks use their own, see DeckModel.</summary>
        public const ushort AjazzVendorId = 0x0300;

        static readonly byte[] Crt = { 0x43, 0x52, 0x54, 0x00, 0x00 };
        static readonly byte[] Ack = { 0x41, 0x43, 0x4B };

        const int KeepAliveMs = 10000;

        IntPtr _handle = (IntPtr)(-1);
        IntPtr _writeEvent = IntPtr.Zero;
        int _inLen, _outLen;
        Thread _reader;
        Thread _keepAlive;
        volatile bool _running;
        volatile bool _keepAliveRunning;
        readonly object _writeLock = new object();

        public DeckInfo Info { get; private set; }
        public DeckModel Model { get; private set; }

        /// <summary>One line naming the device, for a status bar.</summary>
        public string Description { get { return Info == null ? Model.Name : Info.ToString(); } }

        /// <summary>Read-loop diagnostics.</summary>
        public long ReadCycles { get; private set; }
        public long ReadTimeouts { get; private set; }
        public int LastReadError { get; private set; }

        /// <summary>Raised for every key transition reported by the device.</summary>
        public event EventHandler<DeckKeyEventArgs> KeyChanged;

        /// <summary>Raised when a knob is turned or pressed. Only the knob decks send these.</summary>
        public event EventHandler<DeckEncoderEventArgs> EncoderChanged;

        /// <summary>Raised for every input report, before parsing. Useful for protocol work.</summary>
        public event Action<byte[]> RawReport;

        /// <summary>Byte offset of the input code inside the payload, after the report id.</summary>
        public int KeyOffset { get; set; } = 9;

        /// <summary>
        /// Byte offset of the press/release state, or -1 when the device reports a single
        /// event per press. Set from the model: only protocol v3 carries a state byte.
        /// </summary>
        public int StateOffset { get; set; } = -1;

        public int Rows { get { return Model.Rows; } }
        public int Columns { get { return Model.Columns; } }
        public int KeyCount { get { return Model.KeyCount; } }

        AjazzStreamDock() { }

        // ---------- enumeration ----------

        /// <summary>
        /// Finds every attached deck in the catalogue. Pass <paramref name="includeUnknown"/>
        /// to also return vendor-defined HID devices that did not match, which is how a new
        /// model gets identified in the first place.
        /// </summary>
        public static List<DeckInfo> Enumerate(bool includeUnknown = false)
        {
            var found = new List<DeckInfo>();
            foreach (var path in Native.EnumerateHidPaths())
            {
                IntPtr h = Native.CreateFileW(path, 0,
                    Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
                    Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == (IntPtr)(-1)) continue;
                try
                {
                    var attr = new Native.HIDD_ATTRIBUTES();
                    attr.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
                    if (!Native.HidD_GetAttributes(h, ref attr)) continue;

                    var model = DeckModel.Find(attr.VendorID, attr.ProductID);

                    IntPtr pp;
                    var caps = new Native.HIDP_CAPS();
                    if (Native.HidD_GetPreparsedData(h, out pp))
                    {
                        Native.HidP_GetCaps(pp, ref caps);
                        Native.HidD_FreePreparsedData(pp);
                    }

                    // Every deck in the family lives on the vendor-defined usage page.
                    // Requiring it for matched devices too keeps us off a sibling HID
                    // interface of the same composite device, which would accept a handle
                    // and then ignore everything we sent it.
                    bool vendorPage = caps.UsagePage >= 0xFF00;
                    if (!vendorPage) continue;
                    if (model == null && !includeUnknown) continue;

                    var sb = new StringBuilder(256);
                    string serial = Native.HidD_GetSerialNumberString(h, sb, 512) ? sb.ToString() : "";

                    found.Add(new DeckInfo
                    {
                        Path = path,
                        VendorId = attr.VendorID,
                        ProductId = attr.ProductID,
                        Serial = serial,
                        UsagePage = caps.UsagePage,
                        InputReportLength = caps.InputReportByteLength,
                        OutputReportLength = caps.OutputReportByteLength,
                        Model = model
                    });
                }
                finally { Native.CloseHandle(h); }
            }
            return found;
        }

        /// <summary>
        /// Opens a deck. An unrecognised device can still be opened by supplying
        /// <paramref name="assume"/>, which is how an untested model gets tried.
        /// </summary>
        public static AjazzStreamDock Open(DeckInfo info, DeckModel assume = null)
        {
            if (info == null) throw new ArgumentNullException("info");
            var model = info.Model ?? assume;
            if (model == null)
                throw new InvalidOperationException(
                    string.Format("Unrecognised device {0:X4}:{1:X4}. Pass a model to try it anyway.",
                        info.VendorId, info.ProductId));

            var d = new AjazzStreamDock();
            d._handle = Native.CreateFileW(info.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
                Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (d._handle == (IntPtr)(-1))
                throw new InvalidOperationException("Cannot open deck, win32 error " +
                    Marshal.GetLastWin32Error());

            d.Info = info;
            d.Model = model;
            // The descriptor is authoritative: Windows rejects a write of any other length.
            d._inLen = info.InputReportLength > 0 ? info.InputReportLength : model.PacketSize + 1;
            d._outLen = info.OutputReportLength > 0 ? info.OutputReportLength : model.PacketSize + 1;
            d.StateOffset = model.ReportsKeyUp ? 10 : -1;
            d._writeEvent = Native.CreateEventW(IntPtr.Zero, true, false, null);
            Native.HidD_SetNumInputBuffers(d._handle, 64);
            return d;
        }

        /// <summary>Opens the first recognised deck present.</summary>
        public static AjazzStreamDock OpenFirst()
        {
            var all = Enumerate();
            if (all.Count == 0) throw new InvalidOperationException("No Ajazz deck found.");
            return Open(all[0]);
        }

        // ---------- output commands ----------

        byte[] Cmd(params byte[] tail)
        {
            var b = new byte[_outLen];          // b[0] = report id 0
            Array.Copy(Crt, 0, b, 1, Crt.Length);
            if (tail != null && tail.Length > 0)
                Array.Copy(tail, 0, b, 1 + Crt.Length, Math.Min(tail.Length, b.Length - 1 - Crt.Length));
            return b;
        }

        public void Write(byte[] report)
        {
            lock (_writeLock)
            {
                var ov = new Native.OVERLAPPED { hEvent = _writeEvent };
                Native.ResetEvent(_writeEvent);
                bool ok = Native.WriteFile(_handle, report, (uint)report.Length, IntPtr.Zero, ref ov);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != Native.ERROR_IO_PENDING)
                        throw new InvalidOperationException("HID write failed, error " + err);
                    if (Native.WaitForSingleObject(_writeEvent, 3000) != Native.WAIT_OBJECT_0)
                    {
                        Native.CancelIo(_handle);
                        throw new TimeoutException("HID write timed out.");
                    }
                    uint n;
                    if (!Native.GetOverlappedResult(_handle, ref ov, out n, false))
                        throw new InvalidOperationException("HID write failed, error " +
                            Marshal.GetLastWin32Error());
                }
            }
        }

        /// <summary>
        /// Brings the panel out of standalone mode. Until this runs the deck shows its own
        /// menu and sends no input reports at all, which is the single most important fact
        /// about this hardware.
        /// </summary>
        public void Initialize(int brightness = 50)
        {
            Wake();
            SetBrightnessRaw(0);
            ClearAll();
            SetBrightness(brightness);
            Commit();
            if (Model.NeedsKeepAlive) StartKeepAlive();
        }

        /// <summary>CRT..DIS - wake / begin display session.</summary>
        public void Wake() { Write(Cmd(0x44, 0x49, 0x53)); }

        /// <summary>CRT..CONNECT - keepalive.</summary>
        public void KeepAlive() { Write(Cmd(0x43, 0x4F, 0x4E, 0x4E, 0x45, 0x43, 0x54)); }

        /// <summary>CRT..HAN - put the panel to sleep.</summary>
        public void Sleep() { Write(Cmd(0x48, 0x41, 0x4E)); }

        /// <summary>CRT..MOD - some firmwares need a mode before they accept anything else.</summary>
        public void SetMode(int mode)
        {
            Write(Cmd(0x4D, 0x4F, 0x44, 0x00, 0x00, (byte)(0x30 + mode)));
        }

        /// <summary>CRT..LIG - screen brightness, 0-100.</summary>
        public void SetBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            SetBrightnessRaw(percent);
        }

        void SetBrightnessRaw(int percent)
        {
            Write(Cmd(0x4C, 0x49, 0x47, 0x00, 0x00, (byte)percent));
        }

        /// <summary>CRT..LBLIG - knob LED brightness, 0-100. Knob decks only.</summary>
        public void SetLedBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            Write(Cmd(0x4C, 0x42, 0x4C, 0x49, 0x47, (byte)percent));
        }

        /// <summary>CRT..SETLB - per-knob LED colours. Knob decks only.</summary>
        public void SetLedColours(byte[][] rgb)
        {
            if (rgb == null) throw new ArgumentNullException("rgb");
            var tail = new byte[5 + rgb.Length * 3];
            tail[0] = 0x53; tail[1] = 0x45; tail[2] = 0x54; tail[3] = 0x4C; tail[4] = 0x42;
            for (int i = 0; i < rgb.Length; i++)
            {
                tail[5 + i * 3] = rgb[i][0];
                tail[6 + i * 3] = rgb[i][1];
                tail[7 + i * 3] = rgb[i][2];
            }
            Write(Cmd(tail));
        }

        /// <summary>CRT..STP - commit pending image writes.</summary>
        public void Commit() { Write(Cmd(0x53, 0x54, 0x50)); }

        /// <summary>CRT..CLE - clear one key by grid index.</summary>
        public void ClearKey(int grid)
        {
            ClearDeviceKey(Model.GridToDeviceKey(grid));
        }

        void ClearDeviceKey(int deviceKey)
        {
            Write(Cmd(0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, (byte)deviceKey));
        }

        public void ClearAll()
        {
            ClearDeviceKey(0xFF);
            // v2 and v3 will not repaint a cleared panel until it is flushed.
            if (Model.ClearNeedsCommit) Commit();
        }

        /// <summary>
        /// CRT..BAT - upload a JPEG to a key, then stream the data.
        /// <paramref name="grid"/> is the row-major grid index; it is translated to the
        /// hardware key number, which is numbered differently on every family.
        /// </summary>
        public void SetKeyImageJpeg(int grid, byte[] jpeg)
        {
            if (!Model.HasScreen(grid))
                throw new InvalidOperationException(
                    string.Format("Key {0} on the {1} has no screen.", grid, Model.Name));
            SendImage(Model.GridToDeviceKey(grid), jpeg);
        }

        /// <summary>Uploads a JPEG to a knob's screen. Knob decks with screens only.</summary>
        public void SetEncoderImageJpeg(int encoder, byte[] jpeg)
        {
            if (!Model.HasEncoderScreens)
                throw new InvalidOperationException(Model.Name + " has no knob screens.");
            SendImage(Model.EncoderToDeviceKey(encoder), jpeg);
        }

        void SendImage(int deviceKey, byte[] jpeg)
        {
            if (jpeg == null) throw new ArgumentNullException("jpeg");
            int size = jpeg.Length;
            Write(Cmd(0x42, 0x41, 0x54, 0x00, 0x00,
                      (byte)((size >> 8) & 0xFF), (byte)(size & 0xFF),
                      (byte)deviceKey));

            int chunk = _outLen - 1;
            for (int off = 0; off < size; off += chunk)
            {
                var packet = new byte[_outLen];
                Array.Copy(jpeg, off, packet, 1, Math.Min(chunk, size - off));
                Write(packet);
            }
            Commit();
        }

        // ---------- keepalive ----------

        void StartKeepAlive()
        {
            if (_keepAliveRunning) return;
            _keepAliveRunning = true;
            _keepAlive = new Thread(KeepAliveLoop)
            { IsBackground = true, Name = "DeckCommander.DevicesKeepAlive" };
            _keepAlive.Start();
        }

        void KeepAliveLoop()
        {
            // These devices drop off the bus after roughly 35 seconds of silence and come
            // back as a fresh connection, so keep a trickle of traffic going.
            while (_keepAliveRunning)
            {
                for (int i = 0; i < KeepAliveMs / 100 && _keepAliveRunning; i++) Thread.Sleep(100);
                if (!_keepAliveRunning) break;
                try { KeepAlive(); }
                catch { break; }
            }
        }

        // ---------- input ----------

        public void Start()
        {
            if (_running) return;
            _running = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "DeckCommander.DevicesReader" };
            _reader.Start();
        }

        public void Stop()
        {
            _running = false;
            _keepAliveRunning = false;
            if (_reader != null) _reader.Join(1500);
            _reader = null;
            if (_keepAlive != null) _keepAlive.Join(1500);
            _keepAlive = null;
        }

        void ReadLoop()
        {
            IntPtr ev = Native.CreateEventW(IntPtr.Zero, true, false, null);
            var buf = new byte[_inLen];
            var pin = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                while (_running)
                {
                    Array.Clear(buf, 0, buf.Length);
                    var ov = new Native.OVERLAPPED { hEvent = ev };
                    Native.ResetEvent(ev);
                    uint got = 0;
                    bool ok = Native.ReadFile(_handle, buf, (uint)_inLen, IntPtr.Zero, ref ov);
                    ReadCycles++;
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != Native.ERROR_IO_PENDING) { LastReadError = err; break; }
                        if (Native.WaitForSingleObject(ev, 250) == Native.WAIT_TIMEOUT)
                        {
                            ReadTimeouts++;
                            Native.CancelIo(_handle);
                            // A report may have landed between the timeout and the cancel;
                            // if so, GetOverlappedResult returns it and we must not drop it.
                            if (!Native.GetOverlappedResult(_handle, ref ov, out got, true) || got == 0)
                                continue;
                        }
                        else if (!Native.GetOverlappedResult(_handle, ref ov, out got, false))
                        {
                            LastReadError = Marshal.GetLastWin32Error();
                            break;
                        }
                    }
                    else
                    {
                        Native.GetOverlappedResult(_handle, ref ov, out got, true);
                    }
                    if (got == 0) continue;

                    var report = new byte[got];
                    Array.Copy(buf, report, (int)got);

                    var raw = RawReport;
                    if (raw != null) raw(report);

                    Dispatch(report);
                }
            }
            finally
            {
                pin.Free();
                Native.CancelIo(_handle);
                Native.CloseHandle(ev);
            }
        }

        /// <summary>
        /// True when the payload carries the ACK header. Very old firmware omits it, so a
        /// missing header is not on its own a reason to drop the report.
        /// </summary>
        static bool HasAck(byte[] report)
        {
            if (report.Length < 4) return false;
            for (int i = 0; i < Ack.Length; i++)
                if (report[1 + i] != Ack[i]) return false;
            return true;
        }

        void Dispatch(byte[] report)
        {
            // report[0] is the HID report id; the payload starts at index 1.
            int codeIdx = 1 + KeyOffset;
            if (codeIdx >= report.Length) return;

            DeckInput input;
            if (!Model.TryDecodeInput(report[codeIdx], out input)) return;

            bool pressed = true;
            int stIdx = StateOffset >= 0 ? 1 + StateOffset : -1;
            if (stIdx >= 0 && stIdx < report.Length) pressed = report[stIdx] != 0;

            switch (input.Kind)
            {
                case DeckInputKind.Key:
                    var kh = KeyChanged;
                    if (kh != null)
                        kh(this, new DeckKeyEventArgs
                        {
                            Key = input.Index,
                            Row = Model.RowOf(input.Index),
                            Column = Model.ColumnOf(input.Index),
                            DeviceKey = report[codeIdx],
                            Pressed = pressed,
                            Raw = report
                        });
                    break;

                case DeckInputKind.EncoderPress:
                case DeckInputKind.EncoderTurn:
                    var eh = EncoderChanged;
                    if (eh != null)
                        eh(this, new DeckEncoderEventArgs
                        {
                            Encoder = input.Index,
                            Delta = input.Delta,
                            Pressed = input.Kind == DeckInputKind.EncoderTurn || pressed,
                            Raw = report
                        });
                    break;

                // Release sentinel: nothing is held. Nothing to raise.
                case DeckInputKind.Release:
                    break;
            }
        }

        public void Dispose()
        {
            Stop();
            if (_writeEvent != IntPtr.Zero) { Native.CloseHandle(_writeEvent); _writeEvent = IntPtr.Zero; }
            if (_handle != (IntPtr)(-1)) { Native.CloseHandle(_handle); _handle = (IntPtr)(-1); }
        }
    }
}
