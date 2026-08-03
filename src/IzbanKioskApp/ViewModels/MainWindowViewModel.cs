using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Services;
using IzbanKiosk.Application.Hardware.Pos;
using IzbanKiosk.Application.Hardware.Nfc;
using IzbanKiosk.Application.Hardware.Balance;
using IzbanKiosk.Application.Hardware.Receipt;

namespace IzbanKioskApp.ViewModels
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private readonly TransactionCoordinator _transactionCoordinator;
        private readonly INfcReader _nfcReader;
        private readonly IPosTerminal _posTerminal;
        private readonly IAuthoritativeBalanceProvider _balanceProvider;
        private readonly RecoveryService _recoveryService;
        private readonly ReceiptService _receiptService;
        private readonly IReceiptPrinter _receiptPrinter;
        private readonly ReceiptPrinterOptions _receiptPrinterOptions;

        private bool _isReceiptPromptVisible;
        private bool _isReceiptPrinting;
        private bool _isReceiptYesEnabled;
        private string _receiptPromptText = "";
        private string _receiptYesButtonText = "";
        private string _receiptNoButtonText = "";
        private string _receiptStatusText = "";
        private bool _printerReady;
        private bool _receiptDecisionTaken;
        private TaskCompletionSource<string>? _receiptDecisionTcs;
        private CancellationTokenSource? _receiptDecisionTimeoutCts;
        private readonly SemaphoreSlim _receiptDecisionLock = new SemaphoreSlim(1, 1);
        private bool _isPrinterWarningVisible;
        private string _printerWarningText = "";

        private CancellationTokenSource? _transactionCts;
        private CancellationTokenSource? _cardRemovalCts;
        private bool _isCardPresent;
        private string _cardUid = "35-IZM-9921";
        private long _currentBalanceMinor = 4550; // 45.50 TL
        private string _numpadValue = "0";
        private string _currentLang = "TR";
        private bool _isWarningModalActive;

        // UI View state visibilities
        private bool _isIdleScreenVisible = true;
        private bool _isAmountScreenVisible;
        private bool _isNumpadScreenVisible;
        private bool _isPaymentScreenVisible;
        private bool _isSuccessScreenVisible;
        private bool _isHelpModalVisible;
        private bool _isWarningModalVisible;

        // UI text content properties
        private string _clockText = "";
        private string _stationText = "";
        private string _kioskStatusText = "";
        private string _languageToggleText = "🌍 EN";
        private string _helpBtnText = "";
        private string _cardBrandText = "";
        private string _cardSubBrandText = "";
        private string _idleHeadingText1 = "";
        private string _idleHeadingText2 = "";
        private string _idleSubHeadingText = "";
        private string _cardInfoLabel = "";
        private string _cardUidText = "";
        private string _balanceLabel = "";
        private string _currentBalanceText = "";
        private string _selectAmountInstruction = "";
        private string _btnSubText = "";
        private string _btnOtherText = "";
        private string _btnOtherSubText = "";
        private string _numpadTitleText = "";
        private string _numpadValueText = "0 TL";
        private string _cancelNumpadBtnText = "";
        private string _selectedAmountText = "";
        private string _paymentStatusText = "";
        private string _paymentStatusColor = "#EF4444"; // Hex code
        private string _paymentLabelDetails = "";
        private string _successHeadingText = "";
        private string _finalBalanceText = "";
        private string _successSubHeadingText1 = "";
        private string _successSubHeadingText2 = "";
        private string _footerStatusText = "";
        private string _footerNfcLabelText = "";
        private string _footerNfcLabelColor = "#EF4444";
        private string _toggleCardBtnText = "";
        private string _toggleCardBtnColor = "#10B981";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindowViewModel(
            TransactionCoordinator transactionCoordinator,
            INfcReader nfcReader,
            IPosTerminal posTerminal,
            IAuthoritativeBalanceProvider balanceProvider,
            RecoveryService recoveryService,
            ReceiptService receiptService,
            IReceiptPrinter receiptPrinter,
            ReceiptPrinterOptions receiptPrinterOptions)
        {
            _transactionCoordinator = transactionCoordinator;
            _nfcReader = nfcReader;
            _posTerminal = posTerminal;
            _balanceProvider = balanceProvider;
            _recoveryService = recoveryService;
            _receiptService = receiptService;
            _receiptPrinter = receiptPrinter;
            _receiptPrinterOptions = receiptPrinterOptions;

            UpdateClock();
            ApplyLanguage();
            ResetKioskToDefault();

            // Run async status check
            Task.Run(async () =>
            {
                try
                {
                    var status = await _receiptPrinter.HealthCheckAsync(CancellationToken.None);
                    _printerReady = (status.Code == ReceiptPrinterStatusCode.Ready || status.Code == ReceiptPrinterStatusCode.PaperLow);
                }
                catch
                {
                    _printerReady = false;
                }
                ApplyFooterStatusText();
            });
        }

        // --- Properties to Bind ---
        public bool IsIdleScreenVisible
        {
            get => _isIdleScreenVisible;
            set { _isIdleScreenVisible = value; OnPropertyChanged(); }
        }

        public bool IsAmountScreenVisible
        {
            get => _isAmountScreenVisible;
            set { _isAmountScreenVisible = value; OnPropertyChanged(); }
        }

        public bool IsNumpadScreenVisible
        {
            get => _isNumpadScreenVisible;
            set { _isNumpadScreenVisible = value; OnPropertyChanged(); }
        }

        public bool IsPaymentScreenVisible
        {
            get => _isPaymentScreenVisible;
            set { _isPaymentScreenVisible = value; OnPropertyChanged(); }
        }

        public bool IsSuccessScreenVisible
        {
            get => _isSuccessScreenVisible;
            set { _isSuccessScreenVisible = value; OnPropertyChanged(); }
        }

        public bool IsHelpModalVisible
        {
            get => _isHelpModalVisible;
            set { _isHelpModalVisible = value; OnPropertyChanged(); }
        }

        public bool IsWarningModalVisible
        {
            get => _isWarningModalVisible;
            set { _isWarningModalVisible = value; OnPropertyChanged(); }
        }

        public bool IsReceiptPromptVisible
        {
            get => _isReceiptPromptVisible;
            set { _isReceiptPromptVisible = value; OnPropertyChanged(); }
        }

        public bool IsReceiptPrinting
        {
            get => _isReceiptPrinting;
            set { _isReceiptPrinting = value; OnPropertyChanged(); }
        }

        public bool IsReceiptYesEnabled
        {
            get => _isReceiptYesEnabled;
            set { _isReceiptYesEnabled = value; OnPropertyChanged(); }
        }

        public string ReceiptPromptText
        {
            get => _receiptPromptText;
            set { _receiptPromptText = value; OnPropertyChanged(); }
        }

        public string ReceiptYesButtonText
        {
            get => _receiptYesButtonText;
            set { _receiptYesButtonText = value; OnPropertyChanged(); }
        }

        public string ReceiptNoButtonText
        {
            get => _receiptNoButtonText;
            set { _receiptNoButtonText = value; OnPropertyChanged(); }
        }

        public string ReceiptStatusText
        {
            get => _receiptStatusText;
            set { _receiptStatusText = value; OnPropertyChanged(); }
        }

        public bool IsPrinterWarningVisible
        {
            get => _isPrinterWarningVisible;
            set { _isPrinterWarningVisible = value; OnPropertyChanged(); }
        }

        public string PrinterWarningText
        {
            get => _printerWarningText;
            set { _printerWarningText = value; OnPropertyChanged(); }
        }

        public string ClockText
        {
            get => _clockText;
            set { _clockText = value; OnPropertyChanged(); }
        }

        public string StationText
        {
            get => _stationText;
            set { _stationText = value; OnPropertyChanged(); }
        }

        public string KioskStatusText
        {
            get => _kioskStatusText;
            set { _kioskStatusText = value; OnPropertyChanged(); }
        }

        public string LanguageToggleText
        {
            get => _languageToggleText;
            set { _languageToggleText = value; OnPropertyChanged(); }
        }

        public string HelpBtnText
        {
            get => _helpBtnText;
            set { _helpBtnText = value; OnPropertyChanged(); }
        }

        public string CardBrandText
        {
            get => _cardBrandText;
            set { _cardBrandText = value; OnPropertyChanged(); }
        }

        public string CardSubBrandText
        {
            get => _cardSubBrandText;
            set { _cardSubBrandText = value; OnPropertyChanged(); }
        }

        public string IdleHeadingText1
        {
            get => _idleHeadingText1;
            set { _idleHeadingText1 = value; OnPropertyChanged(); }
        }

        public string IdleHeadingText2
        {
            get => _idleHeadingText2;
            set { _idleHeadingText2 = value; OnPropertyChanged(); }
        }

        public string IdleSubHeadingText
        {
            get => _idleSubHeadingText;
            set { _idleSubHeadingText = value; OnPropertyChanged(); }
        }

        public string CardInfoLabel
        {
            get => _cardInfoLabel;
            set { _cardInfoLabel = value; OnPropertyChanged(); }
        }

        public string CardUidText
        {
            get => _cardUidText;
            set { _cardUidText = value; OnPropertyChanged(); }
        }

        public string BalanceLabel
        {
            get => _balanceLabel;
            set { _balanceLabel = value; OnPropertyChanged(); }
        }

        public string CurrentBalanceText
        {
            get => _currentBalanceText;
            set { _currentBalanceText = value; OnPropertyChanged(); }
        }

        public string SelectAmountInstruction
        {
            get => _selectAmountInstruction;
            set { _selectAmountInstruction = value; OnPropertyChanged(); }
        }

        public string BtnSubText
        {
            get => _btnSubText;
            set { _btnSubText = value; OnPropertyChanged(); }
        }

        public string BtnOtherText
        {
            get => _btnOtherText;
            set { _btnOtherText = value; OnPropertyChanged(); }
        }

        public string BtnOtherSubText
        {
            get => _btnOtherSubText;
            set { _btnOtherSubText = value; OnPropertyChanged(); }
        }

        public string NumpadTitleText
        {
            get => _numpadTitleText;
            set { _numpadTitleText = value; OnPropertyChanged(); }
        }

        public string NumpadValueText
        {
            get => _numpadValueText;
            set { _numpadValueText = value; OnPropertyChanged(); }
        }

        public string CancelNumpadBtnText
        {
            get => _cancelNumpadBtnText;
            set { _cancelNumpadBtnText = value; OnPropertyChanged(); }
        }

        public string SelectedAmountText
        {
            get => _selectedAmountText;
            set { _selectedAmountText = value; OnPropertyChanged(); }
        }

        public string PaymentStatusText
        {
            get => _paymentStatusText;
            set { _paymentStatusText = value; OnPropertyChanged(); }
        }

        public string PaymentStatusColor
        {
            get => _paymentStatusColor;
            set { _paymentStatusColor = value; OnPropertyChanged(); }
        }

        public string PaymentLabelDetails
        {
            get => _paymentLabelDetails;
            set { _paymentLabelDetails = value; OnPropertyChanged(); }
        }

        public string SuccessHeadingText
        {
            get => _successHeadingText;
            set { _successHeadingText = value; OnPropertyChanged(); }
        }

        public string FinalBalanceText
        {
            get => _finalBalanceText;
            set { _finalBalanceText = value; OnPropertyChanged(); }
        }

        public string SuccessSubHeadingText1
        {
            get => _successSubHeadingText1;
            set { _successSubHeadingText1 = value; OnPropertyChanged(); }
        }

        public string SuccessSubHeadingText2
        {
            get => _successSubHeadingText2;
            set { _successSubHeadingText2 = value; OnPropertyChanged(); }
        }

        public string FooterStatusText
        {
            get => _footerStatusText;
            set { _footerStatusText = value; OnPropertyChanged(); }
        }

        public string FooterNfcLabelText
        {
            get => _footerNfcLabelText;
            set { _footerNfcLabelText = value; OnPropertyChanged(); }
        }

        public string FooterNfcLabelColor
        {
            get => _footerNfcLabelColor;
            set { _footerNfcLabelColor = value; OnPropertyChanged(); }
        }

        public string ToggleCardBtnText
        {
            get => _toggleCardBtnText;
            set { _toggleCardBtnText = value; OnPropertyChanged(); }
        }

        public string ToggleCardBtnColor
        {
            get => _toggleCardBtnColor;
            set { _toggleCardBtnColor = value; OnPropertyChanged(); }
        }

        public bool IsCardPresent => _isCardPresent;

        // --- Commands / Actions ---
        public void UpdateClock()
        {
            ClockText = DateTime.Now.ToString("dd.MM.yyyy - HH:mm:ss");
        }

        public void ToggleLanguage()
        {
            _currentLang = _currentLang == "TR" ? "EN" : "TR";
            ApplyLanguage();
        }

        public void ToggleHelp()
        {
            IsHelpModalVisible = true;
        }

        public void CloseHelp()
        {
            IsHelpModalVisible = false;
        }

        private void StopCardRemovalListener()
        {
            if (_cardRemovalCts != null)
            {
                try { _cardRemovalCts.Cancel(); } catch { }
                try { _cardRemovalCts.Dispose(); } catch { }
                _cardRemovalCts = null;
            }
        }

        private void StartCardRemovalListener(CardReference cardRef)
        {
            StopCardRemovalListener();
            _cardRemovalCts = new CancellationTokenSource();
            var token = _cardRemovalCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await _nfcReader.WaitForCardRemovalAsync(
                        new TransactionId(Guid.NewGuid()),
                        cardRef,
                        TimeSpan.FromHours(1),
                        token
                    );

                    if (!token.IsCancellationRequested)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await HandleCardRemovedAsync();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CARD REMOVAL LISTENER ERROR/CANCEL] {ex.Message}");
                }
            }, token);
        }

        public void ResetKioskToDefault()
        {
            StopCardRemovalListener();

            _isCardPresent = false;
            _numpadValue = "0";
            NumpadValueText = "0 TL";

            ToggleCardBtnText = _currentLang == "TR" ? "📥 KART YAKLAŞTIR" : "📥 PLACE CARD";
            ToggleCardBtnColor = "#10B981";
            FooterNfcLabelText = GetText("FooterNfcNoCard");
            FooterNfcLabelColor = "#EF4444";

            // Signal mock reader that card is gone if present
            var method = _nfcReader.GetType().GetMethod("SetCardPresent");
            if (method != null)
            {
                method.Invoke(_nfcReader, new object[] { false });
            }

            IsReceiptPromptVisible = false;
            IsReceiptPrinting = false;
            ReceiptStatusText = "";

            if (_receiptDecisionTcs != null && !_receiptDecisionTcs.Task.IsCompleted)
            {
                _receiptDecisionTcs.TrySetResult("TIMEOUT");
            }
            _receiptDecisionTcs = null;
            _receiptDecisionTaken = false;

            if (_receiptDecisionTimeoutCts != null)
            {
                try { _receiptDecisionTimeoutCts.Cancel(); } catch { }
                try { _receiptDecisionTimeoutCts.Dispose(); } catch { }
                _receiptDecisionTimeoutCts = null;
            }

            SetCurrentScreen(1); // 1 = Idle Screen
        }

        private string _simulatedCardUid = "35-IZM-9921";
        public string SimulatedCardUid
        {
            get => _simulatedCardUid;
            set { _simulatedCardUid = value; OnPropertyChanged(); }
        }

        public async Task ToggleSimulatedCardAsync()
        {
            if (!_isCardPresent)
            {
                _isCardPresent = true;
                ToggleCardBtnText = _currentLang == "TR" ? "📤 KARTI ÇEK" : "📤 REMOVE CARD";
                ToggleCardBtnColor = "#EF4444";
                FooterNfcLabelText = GetText("FooterNfcCard");
                FooterNfcLabelColor = "#10B981";

                var setCardPresentMethod = _nfcReader.GetType().GetMethod("SetCardPresent");
                if (setCardPresentMethod != null)
                {
                    setCardPresentMethod.Invoke(_nfcReader, new object[] { true });
                }

                var method = _nfcReader.GetType().GetMethod("SetSimulatedCardUid");
                if (method != null)
                {
                    method.Invoke(_nfcReader, new object[] { string.IsNullOrWhiteSpace(_simulatedCardUid) ? "35-IZM-9921" : _simulatedCardUid });
                }

                try
                {
                    // Simulated read
                    var cardRef = await _nfcReader.WaitForCardAsync(new TransactionId(Guid.NewGuid()), TimeSpan.FromSeconds(5), CancellationToken.None);
                    if (cardRef != null)
                    {
                        var snapshot = await _nfcReader.ReadCardSnapshotAsync(new TransactionId(Guid.NewGuid()), cardRef, CancellationToken.None);
                        _cardUid = cardRef.Masked;
                        _currentBalanceMinor = snapshot.BalanceMinor;

                        CardUidText = $"Card UID: {MaskCardUid(_cardUid)}";
                        CurrentBalanceText = $"{(_currentBalanceMinor / 100.0):F2} TL";
                        SetCurrentScreen(2); // 2 = Amount Screen

                        StartCardRemovalListener(cardRef);
                    }
                }
                catch (Exception ex)
                {
                    PaymentStatusText = _currentLang == "TR" ? "Kart Okuma Hatası!" : "Card Reading Error!";
                    Console.WriteLine($"[NFC READ ERROR] {ex.Message}");
                }
            }
            else
            {
                var setCardPresentMethod = _nfcReader.GetType().GetMethod("SetCardPresent");
                if (setCardPresentMethod != null)
                {
                    setCardPresentMethod.Invoke(_nfcReader, new object[] { false });
                }
                else
                {
                    await HandleCardRemovedAsync();
                }
            }
        }

        public async Task HandleCardRemovedAsync()
        {
            if (!_isCardPresent) return; // Idempotent check

            _isCardPresent = false;
            ToggleCardBtnText = _currentLang == "TR" ? "📥 KART YAKLAŞTIR" : "📥 PLACE CARD";
            ToggleCardBtnColor = "#10B981";
            FooterNfcLabelText = GetText("FooterNfcNoCard");
            FooterNfcLabelColor = "#EF4444";

            StopCardRemovalListener();

            if (IsIdleScreenVisible)
            {
                return;
            }

            if (IsAmountScreenVisible || IsNumpadScreenVisible)
            {
                _transactionCts?.Cancel();
                await TriggerEarlyRemovalWarningAsync(autoReset: true);
            }
            else if (IsPaymentScreenVisible)
            {
                await TriggerEarlyRemovalWarningAsync(autoReset: false);
            }
            else if (IsSuccessScreenVisible)
            {
                await TryResolveReceiptDecisionAsync("CARD_REMOVED");
            }
        }

        private async Task TriggerEarlyRemovalWarningAsync(bool autoReset = true)
        {
            if (_isWarningModalActive) return;
            _isWarningModalActive = true;

            IsWarningModalVisible = true;
            _numpadValue = "0";
            NumpadValueText = "0 TL";

            await Task.Delay(2500);

            IsWarningModalVisible = false;
            _isWarningModalActive = false;

            if (autoReset)
            {
                ResetKioskToDefault();
            }
        }

        public void SelectOtherAmount()
        {
            _numpadValue = "0";
            NumpadValueText = "0 TL";
            SetCurrentScreen(3); // 3 = Numpad Screen
        }

        public void CancelNumpad()
        {
            SetCurrentScreen(2); // 2 = Amount Screen
        }

        public void ProcessNumpadKey(string key)
        {
            if (_numpadValue == "0")
            {
                _numpadValue = key;
            }
            else
            {
                string nextVal = _numpadValue + key;
                if (int.TryParse(nextVal, out int val) && val <= 500)
                {
                    _numpadValue = nextVal;
                }
            }
            NumpadValueText = $"{_numpadValue} TL";
        }

        public void DeleteNumpadChar()
        {
            if (_numpadValue.Length > 1)
            {
                _numpadValue = _numpadValue.Substring(0, _numpadValue.Length - 1);
            }
            else
            {
                _numpadValue = "0";
            }
            NumpadValueText = $"{_numpadValue} TL";
        }

        public async Task ConfirmNumpadAsync()
        {
            if (decimal.TryParse(_numpadValue, out decimal val) && val > 0)
            {
                await StartPaymentFlowAsync(val);
            }
        }

        public async Task SelectAmountAsync(decimal amount)
        {
            await StartPaymentFlowAsync(amount);
        }

        // --- Core Payment & Saga flow via coordinator ---
        private async Task StartPaymentFlowAsync(decimal amount)
        {
            _transactionCts = new CancellationTokenSource();
            var token = _transactionCts.Token;

            SelectedAmountText = $"{GetText("SelectedAmount")}: {amount:F2} TL";
            PaymentStatusText = GetText("PaymentStatus");
            PaymentStatusColor = "#EF4444";

            SetCurrentScreen(4); // 4 = Payment Screen

            try
            {
                string idempotencyKey = Guid.NewGuid().ToString("N");
                Money chargeMoney = new Money((long)(amount * 100));

                // Process transaction Saga through coordinator
                var result = await _transactionCoordinator.ProcessTransactionAsync(
                    idempotencyKey,
                    chargeMoney,
                    TimeSpan.FromSeconds(30),
                    token
                );

                if (token.IsCancellationRequested) return;

                if (result.State == KioskTransactionState.Completed)
                {
                    // Use result.NewBalanceMinor instead of adding client-side
                    _currentBalanceMinor = result.NewBalanceMinor;
                    FinalBalanceText = $"{GetText("FinalBalance")}: {(_currentBalanceMinor / 100.0):F2} TL";
                    
                    SetCurrentScreen(5); // 5 = Success Screen
                    
                    IsReceiptPromptVisible = true;
                    IsReceiptPrinting = false;
                    IsReceiptYesEnabled = _printerReady;
                    ReceiptStatusText = "";
                    ApplyLanguage(); // refresh texts

                    _receiptDecisionTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _receiptDecisionTaken = false;

                    int timeoutSecs = _receiptPrinterOptions?.DecisionTimeoutSeconds ?? 20;
                    var timeoutCts = new CancellationTokenSource();
                    _receiptDecisionTimeoutCts = timeoutCts;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(timeoutSecs * 1000, timeoutCts.Token);
                            await TryResolveReceiptDecisionAsync("TIMEOUT");
                        }
                        catch (TaskCanceledException) { }
                    });

                    // Wait for decision
                    string decision = await _receiptDecisionTcs.Task;
                    timeoutCts.Cancel();

                    if (decision == "YES")
                    {
                        IsReceiptYesEnabled = false;
                        IsReceiptPrinting = true;
                        ReceiptStatusText = GetText("ReceiptPrinting");

                        // Record Requested and print
                        await _receiptService.RecordDecisionAsync(result.Id.Value.ToString(), "Requested", CancellationToken.None);
                        var printRes = await _receiptService.PrintReceiptAsync(result.Id.Value.ToString(), "ALSANCAK İSTASYONU", "K-082", CancellationToken.None);

                        if (printRes.Success)
                        {
                            ReceiptStatusText = GetText("ReceiptPrinted");
                            await Task.Delay(3000);
                        }
                        else
                        {
                            if (printRes.Outcome == ReceiptPrintOutcome.PaperOut)
                            {
                                ReceiptStatusText = GetText("ReceiptFailed") + " (" + GetText("PaperOut") + ")";
                            }
                            else if (printRes.Outcome == ReceiptPrintOutcome.Offline)
                            {
                                ReceiptStatusText = GetText("ReceiptUnavailable");
                            }
                            else
                            {
                                ReceiptStatusText = GetText("ReceiptFailed");
                            }
                            await Task.Delay(4000);
                        }
                        ResetKioskToDefault();
                    }
                    else if (decision == "NO")
                    {
                        IsReceiptYesEnabled = false;
                        IsReceiptPrinting = true;
                        ReceiptStatusText = GetText("ReceiptDeclined");
                        await _receiptService.RecordDecisionAsync(result.Id.Value.ToString(), "Declined", CancellationToken.None);
                        await Task.Delay(2000);
                        ResetKioskToDefault();
                    }
                    else if (decision == "TIMEOUT")
                    {
                        IsReceiptYesEnabled = false;
                        IsReceiptPrinting = true;
                        await _receiptService.RecordDecisionAsync(result.Id.Value.ToString(), "TimedOut", CancellationToken.None);
                        ReceiptStatusText = GetText("ReceiptTimeout");
                        await Task.Delay(2000);
                        ResetKioskToDefault();
                    }
                    else if (decision == "CARD_REMOVED")
                    {
                        IsReceiptYesEnabled = false;
                        IsReceiptPrinting = true;
                        await _receiptService.RecordDecisionAsync(result.Id.Value.ToString(), "Offered", CancellationToken.None);
                        ResetKioskToDefault();
                    }
                    else
                    {
                        ResetKioskToDefault();
                    }
                }
                else
                {
                    throw new Exception(result.ErrorMessage ?? (string)(_currentLang == "TR" ? "Banka veya kart yükleme hatası." : "POS or load write failed."));
                }
            }
            catch (TaskCanceledException)
            {
                // Cleanly cancelled
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PAYMENT LOGIC FAIL...] {ex.Message}");
                PaymentStatusText = _currentLang == "TR" ? $"❌ HATA: {ex.Message}" : $"❌ ERROR: {ex.Message}";
                PaymentStatusColor = "#EF4444";

                try
                {
                    await Task.Delay(4000, token);
                }
                catch { }
                ResetKioskToDefault();
            }
            finally
            {
                _transactionCts?.Dispose();
                _transactionCts = null;
            }
        }

        public async Task<bool> TryResolveReceiptDecisionAsync(string decision)
        {
            await _receiptDecisionLock.WaitAsync();
            try
            {
                if (_receiptDecisionTaken || _receiptDecisionTcs == null)
                {
                    return false;
                }

                _receiptDecisionTaken = true;
                _receiptDecisionTcs.TrySetResult(decision);

                if (_receiptDecisionTimeoutCts != null)
                {
                    try { _receiptDecisionTimeoutCts.Cancel(); } catch { }
                }

                return true;
            }
            finally
            {
                _receiptDecisionLock.Release();
            }
        }

        public async Task RequestReceiptAsync()
        {
            await TryResolveReceiptDecisionAsync("YES");
        }

        public async Task DeclineReceiptAsync()
        {
            await TryResolveReceiptDecisionAsync("NO");
        }

        private void SetCurrentScreen(int screenNumber)
        {
            IsIdleScreenVisible = (screenNumber == 1);
            IsAmountScreenVisible = (screenNumber == 2);
            IsNumpadScreenVisible = (screenNumber == 3);
            IsPaymentScreenVisible = (screenNumber == 4);
            IsSuccessScreenVisible = (screenNumber == 5);

            AppServices.IsUserActive = !IsIdleScreenVisible;
        }

        // --- Helpers ---
        private static string MaskCardUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";
            if (uid.Length <= 4) return uid;
            return uid.Substring(0, 2) + "-IZM-••••";
        }

        private void ApplyFooterStatusText()
        {
            if (_printerReady)
            {
                FooterStatusText = GetText("FooterPrinterReady");
                IsPrinterWarningVisible = false;
            }
            else
            {
                FooterStatusText = GetText("FooterStatus");
                IsPrinterWarningVisible = true;
            }
        }

        private void ApplyLanguage()
        {
            LanguageToggleText = _currentLang == "TR" ? "🌍 EN" : "🌍 TR";

            StationText = GetText("HeaderStation");
            KioskStatusText = GetText("HeaderKioskStatus");
            HelpBtnText = GetText("HelpBtnText");

            CardBrandText = GetText("CardBrand");
            CardSubBrandText = GetText("CardSubBrand");
            IdleHeadingText1 = GetText("IdleHeading1");
            IdleHeadingText2 = GetText("IdleHeading2");
            IdleSubHeadingText = GetText("IdleSubHeading");

            CardInfoLabel = GetText("CardInfoLabel");
            BalanceLabel = GetText("BalanceLabel");
            SelectAmountInstruction = GetText("SelectAmountInstruction");
            BtnSubText = GetText("BtnSubText");
            BtnOtherText = GetText("BtnOtherText");
            BtnOtherSubText = GetText("BtnOtherSubText");

            NumpadTitleText = GetText("NumpadTitle");
            CancelNumpadBtnText = GetText("Cancel");

            SelectedAmountText = $"{GetText("SelectedAmount")}: 0 TL";
            PaymentStatusText = GetText("PaymentStatus");
            PaymentLabelDetails = GetText("PaymentLabelDetails");

            SuccessHeadingText = GetText("SuccessHeading");
            SuccessSubHeadingText1 = GetText("SuccessSub1");
            SuccessSubHeadingText2 = GetText("SuccessSub2");

            ReceiptPromptText = GetText("ReceiptPrompt");
            ReceiptYesButtonText = GetText("ReceiptYes");
            ReceiptNoButtonText = GetText("ReceiptNo");
            PrinterWarningText = GetText("FooterPrinterUnavailable");

            // Apply footer text based on printer state
            ApplyFooterStatusText();

            if (_isCardPresent)
            {
                ToggleCardBtnText = _currentLang == "TR" ? "📤 KARTI ÇEK" : "📤 REMOVE CARD";
                FooterNfcLabelText = GetText("FooterNfcCard");
            }
            else
            {
                ToggleCardBtnText = _currentLang == "TR" ? "📥 KART YAKLAŞTIR" : "📥 PLACE CARD";
                FooterNfcLabelText = GetText("FooterNfcNoCard");
            }
        }

        private string GetText(string key)
        {
            if (LangDict.TryGetValue(_currentLang, out var dict) && dict.TryGetValue(key, out var text))
            {
                return text;
            }
            return key;
        }

        private static readonly Dictionary<string, Dictionary<string, string>> LangDict = new()
        {
            { "TR", new() {
                { "HeaderStation", "ALSANCAK İSTASYONU" },
                { "HeaderKioskStatus", "Kiosk ID: #0482 / Hat Durumu: Normal" },
                { "HelpBtnText", "YARDIM AL" },
                { "CardBrand", "İZMİRİM KART" },
                { "CardSubBrand", "Ulaşım & Yaşam Kartı" },
                { "IdleHeading1", "LÜTFEN İZMİRİM KARTINIZI KART OKUYUCU" },
                { "IdleHeading2", "BÖLGESİNE YERLEŞTİRİNİZ" },
                { "IdleSubHeading", "Kartınızı aşağıdaki okutma yuvasına yerleştirin. Yükleme tamamlanana kadar kartınızı çekmeyin." },
                { "CardInfoLabel", "OKUNAN İZMİRİM KART" },
                { "BalanceLabel", "MEVCUT BAKİYE" },
                { "SelectAmountInstruction", "LÜTFEN YÜKLEMEK İSTEDİĞİNİZ TUTARI SEÇİNİZ" },
                { "BtnSubText", "Tutarını Yükle" },
                { "BtnOtherText", "DİĞER TUTAR" },
                { "BtnOtherSubText", "Klavyeden Farklı Tutar Girin" },
                { "NumpadTitle", "LÜTFEN YÜKLEMEK İSTEDİĞİNİZ TUTARI GİRİNİZ" },
                { "Cancel", "VAZGEÇ" },
                { "SelectedAmount", "SEÇİLEN TUTAR" },
                { "PaymentStatus", "📟 LÜTFEN KREDİ KARTINIZI POS CİHAZINA YAKLAŞTIRINIZ..." },
                { "PaymentLabelDetails", "Banka POS terminali hazırlandı. Temassız ödeme işlemi için kredi kartınızı POS ünitesine okutun." },
                { "WritingCardStatus", "Ödeme POS'tan Onaylandı! Bakiye Yazılıyor..." },
                { "SuccessHeading", "YÜKLEME BAŞARILI!" },
                { "FinalBalance", "Yeni Bakiyeniz" },
                { "SuccessSub1", "İşleminiz tamamlanmıştır. İyi yolculuklar dileriz!" },
                { "SuccessSub2", "Makbuz seçiminizi yapabilir ve kartınızı okuyucudan çekebilirsiniz." },
                { "FooterStatus", "Kart Okuyucu ve POS Terminali Hazır" },
                { "FooterNfcNoCard", "NFC SENSÖR: KART YOK" },
                { "FooterNfcCard", "NFC SENSÖR: KART VAR" },
                { "ReceiptPrompt", "MAKBUZ İSTER MİSİNİZ?" },
                { "ReceiptYes", "EVET - MAKBUZ YAZDIR" },
                { "ReceiptNo", "HAYIR - İSTEMİYORUM" },
                { "ReceiptPrinting", "Makbuzunuz hazırlanıyor..." },
                { "ReceiptPrinted", "Makbuzunuzu almayı unutmayınız." },
                { "ReceiptDeclined", "Makbuz yazdırılmadı. İyi yolculuklar." },
                { "ReceiptTimeout", "Seçim yapılmadığı için makbuz yazdırılmadı." },
                { "ReceiptUnavailable", "Yükleme tamamlandı ancak makbuz hizmeti şu anda kullanılamıyor." },
                { "ReceiptFailed", "Yükleme tamamlandı ancak makbuz yazdırılamadı." },
                { "FooterPrinterReady", "Kart Okuyucu, POS Terminali ve Makbuz Yazıcısı Hazır" },
                { "FooterPrinterUnavailable", "Makbuz yazıcısı kullanılamıyor" },
                { "PaperOut", "Kağıt Bitti" }
            } },
            { "EN", new() {
                { "HeaderStation", "ALSANCAK STATION" },
                { "HeaderKioskStatus", "Kiosk ID: #0482 / Line Status: Normal" },
                { "HelpBtnText", "GET HELP" },
                { "CardBrand", "IZMIRIM CARD" },
                { "CardSubBrand", "Transit & Life Card" },
                { "IdleHeading1", "PLEASE PLACE YOUR IZMIRIM CARD" },
                { "IdleHeading2", "ON THE CARD READER AREA" },
                { "IdleSubHeading", "Place your card inside the reading tray below. Do not remove until transit load is complete." },
                { "CardInfoLabel", "READ IZMIRIM CARD" },
                { "BalanceLabel", "CURRENT BALANCE" },
                { "SelectAmountInstruction", "PLEASE SELECT THE AMOUNT YOU WANT TO LOAD" },
                { "BtnSubText", "Load Amount" },
                { "BtnOtherText", "OTHER AMOUNT" },
                { "BtnOtherSubText", "Type Custom Amount on Keyboard" },
                { "NumpadTitle", "PLEASE ENTER THE AMOUNT YOU WANT TO LOAD" },
                { "Cancel", "CANCEL" },
                { "SelectedAmount", "SELECTED AMOUNT" },
                { "PaymentStatus", "📟 PLEASE TAP YOUR CREDIT CARD ON THE POS TERMINAL..." },
                { "PaymentLabelDetails", "Bank POS terminal ready. Tap your credit card to complete the contactless payment." },
                { "WritingCardStatus", "Payment Approved by POS! Writing to Izmirim Card..." },
                { "SuccessHeading", "LOAD SUCCESSFUL!" },
                { "FinalBalance", "Your New Balance" },
                { "SuccessSub1", "Your transaction is complete. Have a nice trip!" },
                { "SuccessSub2", "Please make your receipt selection and you can remove your card." },
                { "FooterStatus", "Card Reader and POS Terminal Ready" },
                { "FooterNfcNoCard", "NFC SENSOR: NO CARD" },
                { "FooterNfcCard", "NFC SENSOR: CARD PRESENT" },
                { "ReceiptPrompt", "WOULD YOU LIKE A RECEIPT?" },
                { "ReceiptYes", "YES - PRINT RECEIPT" },
                { "ReceiptNo", "NO - CONTINUE WITHOUT RECEIPT" },
                { "ReceiptPrinting", "Your receipt is printing..." },
                { "ReceiptPrinted", "Please take your receipt." },
                { "ReceiptDeclined", "No receipt printed. Have a nice trip." },
                { "ReceiptTimeout", "No selection made, receipt not printed." },
                { "ReceiptUnavailable", "Transit load complete but receipt printer is currently unavailable." },
                { "ReceiptFailed", "Transit load complete but receipt printing failed." },
                { "FooterPrinterReady", "Card Reader, POS Terminal and Receipt Printer Ready" },
                { "FooterPrinterUnavailable", "Receipt printer is unavailable" },
                { "PaperOut", "Paper Out" }
            } }
        };
    }
}
