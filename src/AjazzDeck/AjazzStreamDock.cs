using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AjazzDeck
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
        public override string ToString()
        {
            return string.Format("VID_{0:X4}&PID_{1:X4} serial={2} usagePage=0x{3:X4} in={4} out={5}",
                VendorId, ProductId, Serial, UsagePage, InputReportLength, OutputReportLength);
        }
    }

    public sealed class DeckKeyEventArgs : EventArgs
    {
        /// <summary>Row-major key index, top-left = 0, bottom-right = 14.</summary>
        public int Key { get; set; }
        /// <summary>Row, 0 = top.</summary>
        public int Row { get; set; }
        /// <summary>Column, 0 = left.</summary>
        public int Column { get; set; }
        /// <summary>Raw key number as reported by the hardware (1-15).</summary>
        public int DeviceKey { get; set; }
        /// <summary>True for a press. Protocol v1 sends one event per press.</summary>
        public bool Pressed { get; set; }
        public byte[] Raw { get; set; }
        public override string ToString()
        {
            return string.Format("key {0} (row {1}, col {2}, device {3})", Key, Row, Column, DeviceKey);
        }
    }

    /// <summary>
    /// Ajazz / Mirabox "Stream Dock" family (AKP153, AKP153E, HSV293S...).
    /// Vendor-defined HID, 512-byte zero-padded packets with an ASCII "CRT" header.
    /// </summary>
    public sealed class AjazzStreamDock : IDisposable
    {
        public const ushort AjazzVendorId = 0x0300;

        /// <summary>AKP153E physical layout: 3 rows of 5 keys.</summary>
        public const int Rows = 3;
        public const int Columns = 5;
        public const int KeyCount = Rows * Columns;

        static readonly byte[] Crt = { 0x43, 0x52, 0x54, 0x00, 0x00 };

        IntPtr _handle = (IntPtr)(-1);
        IntPtr _writeEvent = IntPtr.Zero;
        int _inLen, _outLen;
        Thread _reader;
        volatile bool _running;
        readonly object _writeLock = new object();

        public DeckInfo Info { get; private set; }

        /// <summary>Read-loop diagnostics.</summary>
        public long ReadCycles { get; private set; }
        public long ReadTimeouts { get; private set; }
        public int LastReadError { get; private set; }

        /// <summary>Raised for every key transition reported by the device.</summary>
        public event EventHandler<DeckKeyEventArgs> KeyChanged;

        /// <summary>Raised for every input report, before parsing. Useful for protocol work.</summary>
        public event Action<byte[]> RawReport;

        /// <summary>Byte offset of the key number inside the 512-byte payload.</summary>
        public int KeyOffset { get; set; } = 9;

        /// <summary>
        /// Byte offset of the press/release state, or -1 when the device reports a single
        /// event per press. Protocol v1 (AKP153E) has no state byte, so -1 is correct.
        /// </summary>
        public int StateOffset { get; set; } = -1;

        /// <summary>
        /// The deck numbers keys column-major from the right: device key 1 is top-right,
        /// 13 is top-left. Converts that to a natural row-major index (top-left = 0).
        /// </summary>
        public static bool TryMapDeviceKey(int deviceKey, out int index, out int row, out int column)
        {
            index = row = column = -1;
            if (deviceKey < 1 || deviceKey > KeyCount) return false;
            row = (deviceKey - 1) % Rows;
            int c = (13 + row - deviceKey);
            if (c % Rows != 0) return false;
            column = c / Rows;
            if (column < 0 || column >= Columns) return false;
            index = row * Columns + column;
            return true;
        }

        /// <summary>Inverse of <see cref="TryMapDeviceKey"/>: row-major index to device key.</summary>
        public static int IndexToDeviceKey(int index)
        {
            if (index < 0 || index >= KeyCount) throw new ArgumentOutOfRangeException("index");
            int row = index / Columns, column = index % Columns;
            return 13 + row - 3 * column;
        }

        AjazzStreamDock() { }

        public static List<DeckInfo> Enumerate(ushort vendorId = AjazzVendorId)
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
                    if (vendorId != 0 && attr.VendorID != vendorId) continue;

                    IntPtr pp;
                    var caps = new Native.HIDP_CAPS();
                    if (Native.HidD_GetPreparsedData(h, out pp))
                    {
                        Native.HidP_GetCaps(pp, ref caps);
                        Native.HidD_FreePreparsedData(pp);
                    }
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
                        OutputReportLength = caps.OutputReportByteLength
                    });
                }
                finally { Native.CloseHandle(h); }
            }
            return found;
        }

        public static AjazzStreamDock Open(DeckInfo info)
        {
            if (info == null) throw new ArgumentNullException("info");
            var d = new AjazzStreamDock();
            d._handle = Native.CreateFileW(info.Path,
                Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero,
                Native.OPEN_EXISTING, Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (d._handle == (IntPtr)(-1))
                throw new InvalidOperationException("Cannot open deck, win32 error " +
                    Marshal.GetLastWin32Error());
            d.Info = info;
            d._inLen = info.InputReportLength;
            d._outLen = info.OutputReportLength;
            d._writeEvent = Native.CreateEventW(IntPtr.Zero, true, false, null);
            Native.HidD_SetNumInputBuffers(d._handle, 64);
            return d;
        }

        /// <summary>Opens the first Ajazz deck present.</summary>
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
        /// Initialisation used by the reference implementation:
        /// brightness, clear every key, then flush.
        /// </summary>
        public void Initialize(int brightness = 50)
        {
            SetBrightness(brightness);
            ClearAll();
            Commit();
        }

        /// <summary>CRT..DIS - wake / begin display session.</summary>
        public void Wake() { Write(Cmd(0x44, 0x49, 0x53)); }

        /// <summary>CRT..CONNECT - keepalive.</summary>
        public void KeepAlive() { Write(Cmd(0x43, 0x4F, 0x4E, 0x4E, 0x45, 0x43, 0x54)); }

        /// <summary>CRT..HAN - put the panel to sleep.</summary>
        public void Sleep() { Write(Cmd(0x48, 0x41, 0x4E)); }

        /// <summary>CRT..LIG - screen brightness, 0-100.</summary>
        public void SetBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            Write(Cmd(0x4C, 0x49, 0x47, 0x00, 0x00, (byte)percent));
        }

        /// <summary>CRT..STP - commit pending image writes.</summary>
        public void Commit() { Write(Cmd(0x53, 0x54, 0x50)); }

        /// <summary>CRT..CLE - clear one key by row-major index, or all keys.</summary>
        public void ClearKey(int index)
        {
            Write(Cmd(0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, (byte)IndexToDeviceKey(index)));
        }
        public void ClearAll() { Write(Cmd(0x43, 0x4C, 0x45, 0x00, 0x00, 0x00, 0xFF)); }

        /// <summary>
        /// CRT..BAT - upload a JPEG to a key, then stream the data.
        /// <paramref name="index"/> is the row-major index (top-left = 0); it is translated
        /// to the hardware key number, which counts column-major from the right.
        /// </summary>
        public void SetKeyImageJpeg(int index, byte[] jpeg)
        {
            if (jpeg == null) throw new ArgumentNullException("jpeg");
            int size = jpeg.Length;
            Write(Cmd(0x42, 0x41, 0x54, 0x00, 0x00,
                      (byte)((size >> 8) & 0xFF), (byte)(size & 0xFF),
                      (byte)IndexToDeviceKey(index)));

            int chunk = _outLen - 1;
            for (int off = 0; off < size; off += chunk)
            {
                var packet = new byte[_outLen];
                Array.Copy(jpeg, off, packet, 1, Math.Min(chunk, size - off));
                Write(packet);
            }
            Commit();
        }

        // ---------- input ----------

        public void Start()
        {
            if (_running) return;
            _running = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "AjazzDeckReader" };
            _reader.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_reader != null) _reader.Join(1500);
            _reader = null;
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

        void Dispatch(byte[] report)
        {
            // report[0] is the HID report id; the 512-byte payload starts at index 1.
            int keyIdx = 1 + KeyOffset;
            if (keyIdx >= report.Length) return;
            int deviceKey = report[keyIdx];
            if (deviceKey == 0) return;

            bool pressed = true;
            int stIdx = StateOffset >= 0 ? 1 + StateOffset : -1;
            if (stIdx >= 0 && stIdx < report.Length) pressed = report[stIdx] != 0;

            int index, row, column;
            if (!TryMapDeviceKey(deviceKey, out index, out row, out column)) return;

            var h = KeyChanged;
            if (h != null)
                h(this, new DeckKeyEventArgs
                {
                    Key = index, Row = row, Column = column,
                    DeviceKey = deviceKey, Pressed = pressed, Raw = report
                });
        }

        public void Dispose()
        {
            Stop();
            if (_writeEvent != IntPtr.Zero) { Native.CloseHandle(_writeEvent); _writeEvent = IntPtr.Zero; }
            if (_handle != (IntPtr)(-1)) { Native.CloseHandle(_handle); _handle = (IntPtr)(-1); }
        }
    }
}



