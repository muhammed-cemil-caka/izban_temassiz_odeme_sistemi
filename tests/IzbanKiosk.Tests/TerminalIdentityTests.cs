using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.Terminal;

namespace IzbanKiosk.Tests;

/// <summary>
/// Covers how a kiosk decides which terminal it is to the card scheme.
///
/// The package ships these keys present and set to zero. Treating only an absent key
/// as "needs filling" left every freshly installed kiosk claiming to be terminal
/// zero, which the loader refuses - card loading would have been silently dead on
/// exactly the machines nobody had tested yet.
/// </summary>
public class TerminalIdentityTests
{
    private static KioskHardwareSettings WithKiosk(string number, int no = 0, long uid = 0)
        => new KioskHardwareSettings { KioskNumber = number, TerminalNo = no, TerminalUid = uid };

    [Fact]
    public void ZeroIsTreatedAsUnsetAndFilledFromTheKioskNumber()
    {
        KioskHardwareSettings settings = WithKiosk("51591");

        Assert.True(settings.ResolveTerminalIdentity());
        Assert.Equal(51591, settings.TerminalNo);
        Assert.Equal(51591L, settings.TerminalUid);
    }

    [Fact]
    public void AnExistingIdentityIsNeverOverwritten()
    {
        KioskHardwareSettings settings = WithKiosk("51591", no: 22, uid: 999);

        Assert.False(settings.ResolveTerminalIdentity());
        Assert.Equal(22, settings.TerminalNo);
        Assert.Equal(999L, settings.TerminalUid);
    }

    [Fact]
    public void OnlyTheMissingHalfIsFilled()
    {
        KioskHardwareSettings settings = WithKiosk("51591", no: 22, uid: 0);

        Assert.True(settings.ResolveTerminalIdentity());
        Assert.Equal(22, settings.TerminalNo);
        Assert.Equal(51591L, settings.TerminalUid);
    }

    [Fact]
    public void AKioskWithNoNumberIsLeftUnconfiguredRatherThanGuessed()
    {
        // The loader refuses on zero, which is the right outcome: writing to the
        // scheme under an identity nobody chose is worse than not writing.
        KioskHardwareSettings settings = WithKiosk(string.Empty);

        Assert.False(settings.ResolveTerminalIdentity());
        Assert.Equal(0, settings.TerminalNo);
        Assert.Equal(0L, settings.TerminalUid);
    }

    [Fact]
    public void AKioskNumberTooLargeForTheVendorFieldDoesNotWrapAround()
    {
        // termNo is a ushort in the vendor call. A number above 65535 silently
        // truncated would identify the kiosk as some other terminal entirely.
        KioskHardwareSettings settings = WithKiosk("70000");

        settings.ResolveTerminalIdentity();

        Assert.Equal(0, settings.TerminalNo);
        Assert.Equal(70000L, settings.TerminalUid);
    }
}

/// <summary>
/// Covers the slip a passenger is handed after paying. It is the only evidence they
/// leave with, so the figures on it and the references a dispute is settled by have
/// to be right.
/// </summary>
public class TopUpReceiptTests
{
    private static CardSnapshotResponse Card() => new CardSnapshotResponse
    {
        CardNumber = "0491600225477801",
        StoragePseudonym = "psd-1"
    };

    private static TopUpResponse Result() => new TopUpResponse
    {
        RequestId = "req-1",
        Outcome = TopUpOutcome.Completed,
        IsCompleted = true,
        AmountMinor = 2000,
        BalanceAfterMinor = 7150,
        ReferenceNo = 900123,
        ApprovalCode = "APP123",
        MaskedPosReference = "**** 4242"
    };

    private static string Build(TopUpResponse result, string station = "")
        => ReceiptDocumentBuilder.BuildTopUpReceipt(
            Card(), result, station, "51591", new DateTime(2026, 8, 10, 14, 5, 9), false);

    [Fact]
    public void ShowsWhatWasPaidAndWhatTheCardHoldsNow()
    {
        string receipt = Build(Result());

        Assert.Contains("20,00 TL", receipt);
        Assert.Contains("71,50 TL", receipt);
        Assert.Contains("YÜKLEME MAKBUZU", receipt);
    }

    [Fact]
    public void CarriesTheReferencesADisputeIsSettledWith()
    {
        // The kiosk transaction number is what the back office reconciles against;
        // without it a slip and a record cannot be matched.
        string receipt = Build(Result());

        Assert.Contains("900123", receipt);
        Assert.Contains("APP123", receipt);
        Assert.Contains("51591", receipt);
        Assert.Contains("10.08.2026 14:05:09", receipt);
    }

    [Fact]
    public void NeverPrintsTheFullCardNumber()
    {
        string receipt = Build(Result());

        Assert.DoesNotContain("0491600225477801", receipt);
    }

    [Fact]
    public void LeavesOutPaymentLinesTheTerminalDidNotSupply()
    {
        // An empty "Onay Kodu:" on a payment slip reads like something went wrong.
        TopUpResponse result = Result();
        result.ApprovalCode = string.Empty;
        result.MaskedPosReference = string.Empty;

        string receipt = Build(result);

        Assert.DoesNotContain("Onay Kodu", receipt);
        Assert.DoesNotContain("Ödeme Kartı", receipt);
        Assert.Contains("İşlem No", receipt);
    }

    [Fact]
    public void OmitsTheStationRatherThanPrintingAFleetWidePlaceholder()
    {
        Assert.DoesNotContain("İstasyon", Build(Result()));
        Assert.Contains("İstasyon", Build(Result(), "ALSANCAK"));
    }
}
