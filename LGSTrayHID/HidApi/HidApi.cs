using System.Runtime.InteropServices;

using System.Runtime.CompilerServices;

namespace LGSTrayHID.HidApi
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct HidApiVersion
    {
        readonly int Major;
        readonly int Minor;
        readonly int Patch;

        public override readonly string ToString()
        {
            return $"{Major}.{Minor}.{Patch}";
        }
    }

    internal static partial class HidApi
    {
        [LibraryImport("hidapi", EntryPoint = "hid_init")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int HidInit();

        [LibraryImport("hidapi", EntryPoint = "hid_exit")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int HidExit();

        [LibraryImport("hidapi", EntryPoint = "hid_enumerate")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static unsafe partial HidDeviceInfo* HidEnumerate(ushort vendor_id, ushort product_id);

        [LibraryImport("hidapi", EntryPoint = "hid_free_enumeration")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static unsafe partial void HidFreeEnumeration(HidDeviceInfo* devs);

        [LibraryImport("hidapi", EntryPoint = "hid_open_path")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe partial nint _HidOpenPath(byte* path);

        internal static unsafe nint HidOpenPath(ref HidDeviceInfo dev)
        {
            return _HidOpenPath(dev.Path);
        }

        internal static unsafe nint HidOpenPath(string path)
        {
            nint pathPtr = Marshal.StringToHGlobalAnsi(path);
            try
            {
                return _HidOpenPath((byte*)pathPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(pathPtr);
            }
        }

        [LibraryImport("hidapi", EntryPoint = "hid_close")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static unsafe partial void HidClose(nint dev);

        [LibraryImport("hidapi", EntryPoint = "hid_write")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static unsafe partial int HidWrite(nint dev, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2), In] byte[] data, nuint length);

        [LibraryImport("hidapi", EntryPoint = "hid_read_timeout")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static unsafe partial int HidReadTimeOut(nint dev, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2), Out] byte[] data, nuint length, int milliseconds);

        [LibraryImport("hidapi", EntryPoint = "hid_version")]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe partial HidApiVersion* _HidVersion();

        internal unsafe static HidApiVersion HidVersion()
        {
            return *_HidVersion();
        }
    }
}
