using System.Runtime.InteropServices;
using IzbanKiosk.LegacyHardwareBridge.Interop;

namespace IzbanKiosk.Tests;

public sealed class LegacyInteropLayoutTests
{
    [Fact]
    public void VendorStructs_UseNet40SafeSequentialLayout_WithExactX86Sizes()
    {
        Assert.Equal(LayoutKind.Sequential, typeof(EmvRdr35NativeMethods.CARD_LAYOUT).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(EmvRdr35NativeMethods.AV2_LAYOUT).StructLayoutAttribute!.Value);
        Assert.Equal(LayoutKind.Sequential, typeof(EmvRdr35NativeMethods.TOffCardInf).StructLayoutAttribute!.Value);

        Assert.Equal(27, Marshal.SizeOf<EmvRdr35NativeMethods.CARD_LAYOUT>());
        Assert.Equal(62, Marshal.SizeOf<EmvRdr35NativeMethods.AV2_LAYOUT_EXT>());
        Assert.Equal(13, Marshal.SizeOf<EmvRdr35NativeMethods.AV2_LAYOUT>());
        Assert.Equal(18, Marshal.SizeOf<EmvRdr35NativeMethods.TOffCardInf>());

        Assert.Equal(0, Marshal.OffsetOf<EmvRdr35NativeMethods.AV2_LAYOUT>(nameof(EmvRdr35NativeMethods.AV2_LAYOUT.Av2HostMode)).ToInt32());
        Assert.Equal(1, Marshal.OffsetOf<EmvRdr35NativeMethods.AV2_LAYOUT>(nameof(EmvRdr35NativeMethods.AV2_LAYOUT.Av2_Uid)).ToInt32());
        Assert.Equal(11, Marshal.OffsetOf<EmvRdr35NativeMethods.AV2_LAYOUT>(nameof(EmvRdr35NativeMethods.AV2_LAYOUT.Av2_CRC16_CAFE)).ToInt32());

        Assert.Equal(0, Marshal.OffsetOf<EmvRdr35NativeMethods.AV2_LAYOUT_EXT>(nameof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT.bSamInitOK)).ToInt32());
        Assert.Equal(2, Marshal.OffsetOf<EmvRdr35NativeMethods.AV2_LAYOUT_EXT>(nameof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT.bSamUnlocked)).ToInt32());
        Assert.Equal(60, Marshal.OffsetOf<EmvRdr35NativeMethods.AV2_LAYOUT_EXT>(nameof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT.wSamAtrLen)).ToInt32());
    }
}
