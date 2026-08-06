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
    public void BalanceReceipt_KeepsTurkishCharacters()
    {
        // The deployed AUSKiosk 5.2.0.4 printed "Kart Dolum Fişi" and "BAŞARISIZ İŞLEM
        // FİŞİ" through this same DLL on this same hardware, so the ANSI code page
        // carries Turkish. Folding to ASCII made the slips look unfinished for nothing.
        var snapshot = Snapshot();
        snapshot.CardType = "Öğrenci";

        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(snapshot, "ŞİRİNYER", "0482", Timestamp, false);

        Assert.Contains("BAKİYE SORGULAMA FİŞİ", receipt);
        Assert.Contains("ŞİRİNYER", receipt);
        Assert.Contains("Öğrenci", receipt);
        Assert.Contains("BAŞARILI", receipt);
    }

    [Fact]
    public void BalanceReceipt_OmitsStationLineWhenNoStationIsConfigured()
    {
        // The fleet-wide default leaves the station unset. Printing a placeholder would
        // put the same "station" on every receipt in the network; the kiosk number
        // already identifies the machine.
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "", "51591", Timestamp, false);

        Assert.DoesNotContain("İstasyon", receipt);
        Assert.Contains("Otomat No", receipt);
        Assert.Contains("51591", receipt);
    }

    [Fact]
    public void BalanceReceipt_KeepsStationLineWhenConfigured()
    {
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "ALSANCAK", "51591", Timestamp, false);

        Assert.Contains("İstasyon", receipt);
        Assert.Contains("ALSANCAK", receipt);
    }

    [Fact]
    public void BalanceReceipt_OmitsBareNumericCardTypeCode()
    {
        // The reader returned "1" on the first physical slip. A bare code means nothing
        // to a passenger and inventing a fare name from it could print the wrong
        // entitlement, so the line is dropped instead.
        var snapshot = Snapshot();
        snapshot.CardType = "1";

        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(snapshot, "ALSANCAK", "0482", Timestamp, false);

        Assert.DoesNotContain("Kart Tipi", receipt);
    }

    [Fact]
    public void BalanceReceipt_SeparatorFitsThePaperWidth()
    {
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "ALSANCAK", "0482", Timestamp, false);

        foreach (string line in receipt.Split('\n'))
        {
            Assert.True(line.TrimEnd('\r').Length <= 46,
                $"Line exceeds the 56 mm roll width and will be clipped: '{line}'");
        }
    }

    [Fact]
    public void BalanceReceipt_CentresHeaderAndFooterLinesForTheVendorLibrary()
    {
        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(Snapshot(), "ALSANCAK", "0482", Timestamp, false);

        Assert.Contains("[C]İZBAN - İZMİRİM KART", receipt);
        Assert.Contains("[C]BAKİYE SORGULAMA FİŞİ", receipt);
    }

    [Fact]
    public void UnknownBalanceScale_DoesNotInventAnAmount()
    {
        var snapshot = Snapshot();
        snapshot.BalanceScale = 0;

        string receipt = ReceiptDocumentBuilder.BuildBalanceReceipt(snapshot, "ALSANCAK", "0482", Timestamp, false);

        Assert.Contains("Bakiye".PadRight(15) + ": -", receipt);
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
