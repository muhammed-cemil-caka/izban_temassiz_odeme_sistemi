using System;
using System.Runtime.InteropServices;

namespace IzbanKiosk.LegacyHardwareBridge.Interop
{
    public static class KioskPrintNativeMethods
    {
        private const string DLL_NAME = "KioskPrint.dll";

        [DllImport(DLL_NAME, EntryPoint = "PrinterBeginDoc", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        public static extern void PrinterBeginDoc();

        [DllImport(DLL_NAME, EntryPoint = "PrinterEndDoc", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        public static extern void PrinterEndDoc();

        [DllImport(DLL_NAME, EntryPoint = "PrinterTextOut", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        public static extern void PrinterTextOut(int X, int Y, string Text);

        [DllImport(DLL_NAME, EntryPoint = "PrinterCenteredTextOut", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        public static extern void PrinterCenteredTextOut(int Y, string Text, int Offset);

        [DllImport(DLL_NAME, EntryPoint = "PrinterBitmap", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        public static extern void PrinterBitmap(int Left, int Top, int Right, int Bottom, int bufferLen, IntPtr buffer);

        [DllImport(DLL_NAME, EntryPoint = "PrinterSetFont", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        public static extern void PrinterSetFont(string FontName, int FontSize);

        [DllImport(DLL_NAME, EntryPoint = "GetSpoolerJobCount", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        public static extern int GetSpoolerJobCount();
    }
}
