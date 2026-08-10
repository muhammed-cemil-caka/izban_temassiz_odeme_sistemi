using System;
using System.Runtime.InteropServices;

namespace IzbanKiosk.LegacyHardwareBridge.Interop
{
    public static class EmvRdr35NativeMethods
    {
        private const string DLL_NAME = "EMVRdr35Lib.dll";

        // Do not use Explicit layout with ByValArray fields on .NET Framework 4.0.
        // The CLR treats the arrays as managed references and rejects the type before
        // Main() with a TypeLoadException. Sequential + Pack=1 produces the identical
        // 27-byte native ABI without overlapping managed reference fields.
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CARD_LAYOUT
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] CARD_ATQA;

            public byte CARD_SAK;

            public byte CARD_UIDLEN_AT_RST;

            public byte CARD_UIDLEN_AT_GET_UID;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public byte[] CARD_UID_AT_RST;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public byte[] CARD_UID_AT_GET_UID;

            public ushort CARD_CRC16_CAFE;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct AV2_LAYOUT_EXT
        {
            public byte bSamInitOK;

            public byte Av2HostMode;

            public byte bSamUnlocked;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public byte[] Av2_Uid;

            public byte bSamIs_Dts_CtProd;

            public byte bBoardType;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] adwVer;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] abSamATR;

            public ushort wSamAtrLen;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct AV2_LAYOUT
        {
            public byte Av2HostMode;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public byte[] Av2_Uid;

            public ushort Av2_CRC16_CAFE;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TOffCardInf
        {
            public uint alias;

            public byte cardType;

            public byte cardSubType;

            public uint balance;

            public uint lastUsageDT;

            public ushort cardTrnCounter;

            public ushort autoTopUpRefNo;
        }

        [DllImport(DLL_NAME, EntryPoint = "InitComp", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint InitComp();

        [DllImport(DLL_NAME, EntryPoint = "CommOpen", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool CommOpen(uint hComp, string port);

        [DllImport(DLL_NAME, EntryPoint = "CommClose", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern void CommClose(uint hComp);

        [DllImport(DLL_NAME, EntryPoint = "DoneComp", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern void DoneComp(uint hComp);

        [DllImport(DLL_NAME, EntryPoint = "IsConnected", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool IsConnected(uint hComp);

        [DllImport(DLL_NAME, EntryPoint = "GetReaderParam", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool GetReaderParam(uint hComp, ref byte bRxGain, ref byte bMinLevel, IntPtr AV2_LAYOUT_EXT);

        [DllImport(DLL_NAME, EntryPoint = "ResetSamAv2", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ResetSamAv2(uint hComp, IntPtr av2P);

        [DllImport(DLL_NAME, EntryPoint = "SelectCardNoRats", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool SelectCardNoRats(uint hComp, IntPtr sCardP);

        [DllImport(DLL_NAME, EntryPoint = "IsOffValidCard", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool IsOffValidCard(IntPtr sCardP);

        [DllImport(DLL_NAME, EntryPoint = "IsOnTopupValidCard", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool IsOnTopupValidCard(IntPtr sCardP);

        [DllImport(DLL_NAME, EntryPoint = "ReadOffCard", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool ReadOffCard(uint hComp, IntPtr av2P, IntPtr cardInfP);

        /// <summary>
        /// Writes value onto the card. The one call in this library that moves money.
        ///
        /// Signature and parameter names are taken from the deployed AUSKiosk's own
        /// metadata rather than inferred, so the shape is exact. What that assembly
        /// cannot tell us is the unit of <paramref name="amount"/>: the surrounding
        /// configuration stores money in kuruş and <c>ReadOffCard</c> returns kuruş,
        /// which makes kuruş the reasonable reading - but "reasonable" is not good
        /// enough for a figure that lands on a passenger's card, so the caller has to
        /// declare the unit and the top-up flow proves it by reading the balance back.
        ///
        /// <paramref name="termNo"/>, <paramref name="termUId"/> and
        /// <paramref name="companyId"/> identify this terminal to the scheme and have
        /// no safe defaults; they come from the deployment.
        /// </summary>
        [DllImport(DLL_NAME, EntryPoint = "Topup", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern bool Topup(
            uint hComp, ushort termNo, uint termUId, byte companyId, int dbRefNo, uint amount, IntPtr av2P);
    }
}
