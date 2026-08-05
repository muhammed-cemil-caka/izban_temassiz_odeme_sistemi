using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.Win7Prototype;

namespace IzbanKiosk.Tests;

public sealed class ReceiptDocumentBuilderTests
{
    private static CardSnapshotResponse Snapshot() => new()
    {
        CardNumber = "1234567890123456",
        CardUid = "04A2B3C4D5",
        StoragePseudonym = "psd0123456789abcdef0123",
        CardType = "Tam",
        BalanceMinor = 4250,
        BalanceScale = 100,
        Currency = "TRY",
        IsSamVerified = true
    };

    private static readonly DateTime Timestamp = new(2026, 8, 5, 14, 30, 15);

    [Fact]
    public void BalanceReceipt_MasksAllButLastFourCardDigits()
    {
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "ALSANCAK", "0482", Timestamp, false);

        Assert.Contains("************3456", receipt);
        Assert.DoesNotContain("1234567890123456", receipt);
    }

    [Fact]
    public void BalanceReceipt_NeverContainsTheNfcUid()
    {
        // The physical UID is shown on screen but must not reach paper or storage.
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "ALSANCAK", "0482", Timestamp, false);

        Assert.DoesNotContain("04A2B3C4D5", receipt);
    }

    [Fact]
    public void BalanceReceipt_RendersBalanceUsingTheReportedScale()
    {
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "ALSANCAK", "0482", Timestamp, false);

        Assert.Contains("42,50 TL", receipt);
    }

    [Fact]
    public void BalanceReceipt_StaysAsciiForTheAnsiVendorApi()
    {
        var snapshot = Snapshot();
        snapshot.CardType = "Öğrenci";

        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(snapshot, "ŞİRİNYER", "0482", Timestamp, false);

        Assert.Contains("Ogrenci", receipt);
        Assert.Contains("SIRINYER", receipt);
        Assert.All(receipt, character => Assert.True(character < 128, $"Non-ASCII character '{character}' would be mangled by KioskPrint.dll."));
    }

    [Fact]
    public void BalanceReceipt_CentresHeaderAndFooterLinesForTheVendorLibrary()
    {
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "ALSANCAK", "0482", Timestamp, false);

        Assert.Contains("[C]IZBAN - IZMIRIM KART", receipt);
        Assert.Contains("[C]BAKIYE SORGULAMA FISI", receipt);
    }

    [Fact]
    public void UnknownBalanceScale_DoesNotInventAnAmount()
    {
        var snapshot = Snapshot();
        snapshot.BalanceScale = 0;

        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(snapshot, "ALSANCAK", "0482", Timestamp, false);

        Assert.Contains("Bakiye".PadRight(18) + ": -", receipt);
        Assert.DoesNotContain("4250", receipt);
        Assert.DoesNotContain("42,50", receipt);
    }

    [Fact]
    public void IdempotencyKey_IsStableForOneCardPresentation()
    {
        var snapshot = Snapshot();

        string first = ReceiptDocumentBuilder.BuildIdempotencyKey(snapshot, Timestamp);
        string second = ReceiptDocumentBuilder.BuildIdempotencyKey(snapshot, Timestamp);

        Assert.Equal(first, second);
        Assert.Contains(snapshot.StoragePseudonym, first);
        Assert.DoesNotContain(snapshot.CardNumber, first);
    }

    [Fact]
    public void IdempotencyKey_DiffersForASecondPresentation()
    {
        var snapshot = Snapshot();

        string first = ReceiptDocumentBuilder.BuildIdempotencyKey(snapshot, Timestamp);
        string second = ReceiptDocumentBuilder.BuildIdempotencyKey(snapshot, Timestamp.AddSeconds(5));

        Assert.NotEqual(first, second);
    }
}
