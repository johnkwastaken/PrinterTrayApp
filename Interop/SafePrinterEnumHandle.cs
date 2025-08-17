using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PrinterTrayApp.Interop;

public sealed class SafePrinterEnumHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePrinterEnumHandle() : base(true) 
    {
    }

    public SafePrinterEnumHandle(IntPtr preexistingHandle) : base(true)
    {
        SetHandle(preexistingHandle);
    }

    protected override bool ReleaseHandle()
    {
        if (!IsInvalid)
        {
            Marshal.FreeHGlobal(handle);
            return true;
        }
        return false;
    }

    public static SafePrinterEnumHandle Allocate(int size)
    {
        var handle = Marshal.AllocHGlobal(size);
        return new SafePrinterEnumHandle(handle);
    }
}