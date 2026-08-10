using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Card;
using IzbanKiosk.LegacyHardwareBridge.Pos;

namespace IzbanKiosk.Tests;

/// <summary>
/// Covers what happens to a passenger's money when a top-up goes wrong.
///
/// These are the paths nobody can stage on a real kiosk: a card that fails to load
/// after the bank has already approved, a reversal that itself fails, a card pulled
/// between the write and the check. The previous system left every one of them
/// silently at "money taken, nothing loaded", and tests are the only place they get
/// exercised before a passenger finds them.
/// </summary>
public class TopUpSagaTests
{
    private const long Amount = 2000;   // 20,00 TRY
    private const long Before = 5000;   // 50,00 TRY

    private sealed class FakePos : IPosTerminal
    {
        public bool Configured = true;
        public string Outcome = "Approved";
        public bool Approved = true;
        public bool ReversalSucceeds = true;
        public int ChargeCount;
        public int ReverseCount;
        public PosReversalRequest? LastReversal;

        public bool IsConfigured => Configured;
        public string LastErrorMessage => "POS yapılandırılmamış.";
        public bool Initialize() => Configured;

        public PosPaymentResponse Charge(PosPaymentRequest request)
        {
            ChargeCount++;
            return new PosPaymentResponse
            {
                RequestId = request.IdempotencyKey,
                Outcome = Outcome,
                IsApproved = Approved,
                ApprovalCode = "APP123",
                MaskedPosReference = "**** 4242",
                StatusMessage = Outcome
            };
        }

        public PosReversalResponse Reverse(PosReversalRequest request)
        {
            ReverseCount++;
            LastReversal = request;
            return new PosReversalResponse
            {
                Outcome = ReversalSucceeds ? "Reversed" : "OutcomeUnknown",
                IsReversed = ReversalSucceeds,
                StatusMessage = ReversalSucceeds ? "iade edildi" : "iade doğrulanamadı"
            };
        }

        public void Shutdown() { }
    }

    private sealed class FakeLoader : ICardLoader
    {
        public bool Authorised = true;
        public bool Succeeds = true;
        public int LoadCount;

        public bool IsAuthorised => Authorised;
        public string LastErrorMessage => "yazma yetkisi yok";

        public CardLoadResponse Load(CardLoadRequest request)
        {
            LoadCount++;
            return new CardLoadResponse
            {
                IsLoaded = Succeeds,
                BalanceAfterMinor = Succeeds ? request.BalanceBeforeMinor + request.AmountMinor : request.BalanceBeforeMinor,
                StatusMessage = Succeeds ? "yüklendi" : "karta yazılamadı"
            };
        }
    }

    private sealed class FakeReader : ICardBalanceReader
    {
        public long First = Before;
        public long Second = Before + Amount;
        public bool FirstFails;
        public bool SecondFails;
        private int _calls;

        public bool TryReadBalanceMinor(string storagePseudonym, out long balanceMinor, out string error)
        {
            _calls++;
            bool fails = _calls == 1 ? FirstFails : SecondFails;
            balanceMinor = _calls == 1 ? First : Second;
            error = fails ? "kart okunamadı" : string.Empty;
            if (fails) balanceMinor = 0;
            return !fails;
        }
    }

    private static PosPaymentRequest Request(string key = "key-1") => new PosPaymentRequest
    {
        IdempotencyKey = key,
        AmountMinor = Amount,
        Currency = "TRY",
        StoragePseudonym = "psd-1"
    };

    private static (TopUpSaga saga, FakePos pos, FakeLoader loader, FakeReader reader, List<string> events) Build()
    {
        var pos = new FakePos();
        var loader = new FakeLoader();
        var reader = new FakeReader();
        var events = new List<string>();
        var saga = new TopUpSaga(pos, loader, reader, (name, _) => events.Add(name));
        return (saga, pos, loader, reader, events);
    }

    [Fact]
    public void HappyPathLoadsAndVerifiesFromTheCard()
    {
        var (saga, pos, loader, _, events) = Build();

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.Completed, result.Outcome);
        Assert.True(result.IsCompleted);
        Assert.Equal(Before + Amount, result.BalanceAfterMinor);
        Assert.Equal(1, pos.ChargeCount);
        Assert.Equal(1, loader.LoadCount);
        Assert.Equal(0, pos.ReverseCount);
        Assert.Contains("TopUpIntent", events);
    }

    [Fact]
    public void NobodyIsChargedWhenTheCardCannotBeWritten()
    {
        // The rule the whole design turns on: no money moves for value the kiosk
        // cannot deliver.
        var (saga, pos, loader, _, _) = Build();
        loader.Authorised = false;

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.NotAuthorised, result.Outcome);
        Assert.Equal(0, pos.ChargeCount);
        Assert.Equal(0, loader.LoadCount);
    }

    [Fact]
    public void NobodyIsChargedWhenThePosIsNotConfigured()
    {
        var (saga, pos, loader, _, _) = Build();
        pos.Configured = false;

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.PosNotConfigured, result.Outcome);
        Assert.Equal(0, pos.ChargeCount);
        Assert.Equal(0, loader.LoadCount);
    }

    [Fact]
    public void NobodyIsChargedWhenTheStartingBalanceCannotBeRead()
    {
        // Without a figure to compare against, a successful-looking load could not be
        // proven, so the money must not be taken at all.
        var (saga, pos, loader, reader, _) = Build();
        reader.FirstFails = true;

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.NeedsReconciliation, result.Outcome);
        Assert.Equal(0, pos.ChargeCount);
        Assert.Equal(0, loader.LoadCount);
    }

    [Fact]
    public void ADeclinedCardNeverTouchesTheKartAndNeedsNoReversal()
    {
        var (saga, pos, loader, _, _) = Build();
        pos.Approved = false;
        pos.Outcome = "Declined";

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.Declined, result.Outcome);
        Assert.Equal(0, loader.LoadCount);
        Assert.Equal(0, pos.ReverseCount);
    }

    [Fact]
    public void AnUndeterminedChargeNeverLoadsTheCard()
    {
        // The charge may or may not have taken money. Loading would risk giving away
        // value that was never paid for.
        var (saga, pos, loader, _, _) = Build();
        pos.Approved = false;
        pos.Outcome = "OutcomeUnknown";

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.NeedsReconciliation, result.Outcome);
        Assert.Equal(0, loader.LoadCount);
    }

    [Fact]
    public void AFailedLoadReversesThePaymentWithTheSameKey()
    {
        var (saga, pos, loader, _, _) = Build();
        loader.Succeeds = false;

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.RefundedAfterLoadFailure, result.Outcome);
        Assert.False(result.IsCompleted);
        Assert.Equal(1, pos.ReverseCount);
        Assert.Equal("key-1", pos.LastReversal!.IdempotencyKey);
        Assert.Equal("APP123", pos.LastReversal.ApprovalCode);
        Assert.Equal(Amount, pos.LastReversal.AmountMinor);
    }

    [Fact]
    public void AFailedLoadAndAFailedReversalDemandAHuman()
    {
        // The worst reachable state: money gone, no value on the card, refund
        // unconfirmed. It must never read as success or as an ordinary failure.
        var (saga, pos, loader, _, _) = Build();
        loader.Succeeds = false;
        pos.ReversalSucceeds = false;

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.NeedsReconciliation, result.Outcome);
        Assert.False(result.IsCompleted);
        Assert.Equal(1, pos.ReverseCount);
    }

    [Fact]
    public void AVerificationReadThatFailsIsNotTreatedAsSuccess()
    {
        var (saga, pos, loader, reader, _) = Build();
        reader.SecondFails = true;

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.NeedsReconciliation, result.Outcome);
        Assert.False(result.IsCompleted);
    }

    [Fact]
    public void AReadBackThatDisagreesIsNotReversed()
    {
        // The card may genuinely hold the value while the figures disagree, so
        // refunding could hand money back for value the passenger kept.
        var (saga, pos, loader, reader, events) = Build();
        reader.Second = Before + Amount - 500;

        TopUpResponse result = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.NeedsReconciliation, result.Outcome);
        Assert.Equal(0, pos.ReverseCount);
        Assert.Contains("TopUpReadbackMismatch", events);
    }

    [Fact]
    public void ARepeatedKeyReturnsTheFirstOutcomeInsteadOfChargingAgain()
    {
        // A double tap, or the kiosk retrying after a dropped pipe.
        var (saga, pos, loader, _, _) = Build();

        TopUpResponse first = saga.Execute(Request());
        TopUpResponse second = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.Completed, second.Outcome);
        Assert.Equal(first.BalanceAfterMinor, second.BalanceAfterMinor);
        Assert.Equal(1, pos.ChargeCount);
        Assert.Equal(1, loader.LoadCount);
    }

    [Fact]
    public void ARefusalThatCostNothingIsNotRememberedAgainstTheKey()
    {
        // Refusing before the terminal means the passenger spent nothing; once the
        // machine is fixed the same key must be servable rather than replaying a
        // refusal forever.
        var (saga, pos, loader, _, _) = Build();
        loader.Authorised = false;
        Assert.Equal(TopUpOutcome.NotAuthorised, saga.Execute(Request()).Outcome);

        loader.Authorised = true;
        TopUpResponse retry = saga.Execute(Request());

        Assert.Equal(TopUpOutcome.Completed, retry.Outcome);
        Assert.Equal(1, pos.ChargeCount);
    }

    [Fact]
    public void AnInvalidRequestIsRefusedBeforeAnythingHappens()
    {
        var (saga, pos, loader, _, _) = Build();

        Assert.Equal(TopUpOutcome.NeedsReconciliation,
            saga.Execute(new PosPaymentRequest { IdempotencyKey = "k", AmountMinor = 0 }).Outcome);
        Assert.Equal(TopUpOutcome.NeedsReconciliation,
            saga.Execute(new PosPaymentRequest { IdempotencyKey = "", AmountMinor = Amount }).Outcome);
        Assert.Equal(0, pos.ChargeCount);
        Assert.Equal(0, loader.LoadCount);
    }

    [Fact]
    public void ThePlaceholderLoaderRefusesAndSaysWhy()
    {
        var loader = new NotAuthorisedCardLoader();

        CardLoadResponse response = loader.Load(new CardLoadRequest { BalanceBeforeMinor = Before });

        Assert.False(loader.IsAuthorised);
        Assert.False(response.IsLoaded);
        Assert.Equal(Before, response.BalanceAfterMinor);
        Assert.Contains("yazma yetkisi", response.StatusMessage);
    }
}
