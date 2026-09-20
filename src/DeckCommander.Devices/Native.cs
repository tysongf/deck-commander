using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DeckCommander.Devices
{
    internal static class Native
    {
        internal const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        internal const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        internal const int ERROR_IO_PENDING = 997;
        internal const uint WAIT_TIMEOUT = 258, WAIT_OBJECT_0 = 0;
        internal const uint DIGCF_PRESENT = 0x02, DIGCF_DEVICEINTERFACE = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        internal struct OVERLAPPED
        {
            public IntPtr Internal, InternalHigh;
            public uint Offset, OffsetHigh;
            public IntPtr hEvent;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID, ProductID, VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDP_CAPS
        {
            public ushort Usage, UsagePage;
            public ushort InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec,
            uint create, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadFile(IntPtr h, byte[] buf, uint n, IntPtr read, ref OVERLAPPED ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteFile(IntPtr h, byte[] buf, uint n, IntPtr written, ref OVERLAPPED ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetOverlappedResult(IntPtr h, ref OVERLAPPED ov, out uint n, bool wait);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateEventW(IntPtr sec, bool manual, bool initial, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CancelIo(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr h);

        [DllImport("hid.dll")]
        internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetAttributes(IntPtr h, ref HIDD_ATTRIBUTES attr);
        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetPreparsedData(IntPtr h, out IntPtr pp);
        [DllImport("hid.dll")]
        internal static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll")]
        internal static extern int HidP_GetCaps(IntPtr pp, ref HIDP_CAPS caps);
        [DllImport("hid.dll")]
        internal static extern bool HidD_SetNumInputBuffers(IntPtr h, uint n);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        internal static extern bool HidD_GetSerialNumberString(IntPtr h, StringBuilder s, int len);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, IntPtr enumerator,
            IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo,
            ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set,
            ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize,
            ref int required, IntPtr devInfoData);
        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        internal static IEnumerable<string> EnumerateHidPaths()
        {
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == (IntPtr)(-1)) yield break;
            try
            {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref did); i++)
                {
                    int required = 0;
                    SetupDiGetDeviceInterfaceDetailW(set, ref did, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                    if (required <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(required);
                    string path = null;
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetailW(set, ref did, buf, required, ref required, IntPtr.Zero))
                            path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                    if (path != null) yield return path;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }
    }
}
