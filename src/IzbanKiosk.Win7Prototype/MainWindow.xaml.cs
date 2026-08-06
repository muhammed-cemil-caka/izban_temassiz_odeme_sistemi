using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using IzbanKiosk.LegacyHardware.Contracts;
using Newtonsoft.Json;

namespace IzbanKiosk.Win7Prototype
{
    /// <summary>
    /// The kiosk UI: WPF on .NET Framework 4.0 / x86, the only combination the
    /// Windows 7 Embedded machine can run. All hardware access goes through the
    /// isolated legacy bridge process, never through this one.
    ///
    /// Financial operations remain fail-closed until a certified POS adapter and an
    /// authorised İzmirim Kart load command are integrated; balance enquiry and
    /// receipt printing are read-only and fully functional.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string PipeName = "IzbanKiosk.LegacyHardware.v1";
        private const string BridgeExeName = "IzbanKiosk.LegacyHardwareBridge.exe";
        private const string ExpectedBridgeVersion = "2.3.3-net40";
        private const string PackageVersion = "R18";
        private const int MaxManualAmount = 500;
        // Printer work is a technician action behind a button, not a passenger-path
        // poll, so it waits far longer for the shared pipe than card polling does.
        private const int PrinterRequestConnectTimeoutMs = 20000;

        private static readonly Brush ReadyBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xB8, 0x83));
        private static readonly Brush BusyBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xA9, 0x00));
        private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0x27, 0x3F));

        private readonly object _workerLock = new object();
        private readonly Queue<string> _bridgeDiagnostics = new Queue<string>();
        private readonly DispatcherTimer _clockTimer;
        private readonly KioskJournal _journal = new KioskJournal();
        private readonly List<string> _queueCandidates = new List<string>();

        private volatile bool _shutdownRequested;
        private volatile bool _hardwareReady;
        private volatile bool _printerReady;
        private volatile bool _printerStateKnown;
        // The bridge serves one named-pipe client at a time, on purpose: the vendor
        // DLLs are not thread-safe. The card-polling loop reconnects continuously, so
        // an operator-initiated printer request has to be let through explicitly or it
        // can fail to connect at all.
        private volatile bool _printerOperationInFlight;
        private volatile bool _cardPresent;
        private bool _workerRunning;
        private bool _english;
        private Thread? _workerThread;
        private Process? _bridgeProcess;
        private bool _ownsBridgeProcess;
        private KioskHardwareSettings? _hardwareSettings;
        private string _numpadDigits = "0";
        private CardSnapshotResponse? _currentSnapshot;
        private KioskScreen _screen = KioskScreen.Idle;

        private enum KioskScreen
        {
            Idle,
            Amount,
            Numpad,
            Payment,
            Error
        }

        public MainWindow()
        {
            InitializeComponent();
            PackageVersionText.Text = "ARAYÜZ + DONANIM • " + PackageVersion + " • BRIDGE " + ExpectedBridgeVersion;
            ApplyLanguage();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += delegate { ClockText.Text = DateTime.Now.ToString("dd.MM.yyyy - HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR")); };
            _clockTimer.Start();
            ClockText.Text = DateTime.Now.ToString("dd.MM.yyyy - HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR"));

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            StartHardwareWorker();
        }

        private void StartHardwareWorker()
        {
            lock (_workerLock)
            {
                if (_workerRunning || _shutdownRequested)
                {
                    return;
                }

                _workerRunning = true;
                _workerThread = new Thread(HardwareWorker)
                {
                    IsBackground = true,
                    Name = "IZBAN Kiosk Hardware Worker"
                };
                _workerThread.Start();
            }
        }

        private void HardwareWorker()
        {
            _hardwareReady = false;
            _cardPresent = false;
            OnUi(delegate
            {
                PrinterToolsPanel.Visibility = Visibility.Collapsed;
                SetHardwareStatus("DONANIM HAZIRLANIYOR", "NFC, SAM ve termal yazıcı kontrol ediliyor...", BusyBrush);
                ShowIdle();
            });

            try
            {
                try
                {
                    _hardwareSettings = KioskHardwareSettings.LoadFromApplicationDirectory();
                }
                catch (Exception ex)
                {
                    OnUi(delegate
                    {
                        ShowFatalError(
                            "Donanım ayar dosyası geçersiz",
                            "KioskHardware.config.json dosyasında COM portu ve termal yazıcı adı tanımlı olmalıdır.",
                            ex.Message);
                    });
                    return;
                }

                if (!EnsureBridgeStarted())
                {
                    OnUi(delegate
                    {
                        ShowFatalError(
                            "Donanım servisi başlatılamadı",
                            "Program klasörünün eksiksiz çıkarıldığını ve eski AUSKiosk uygulamasının kapalı olduğunu kontrol ediniz.",
                            GetDiagnosticSummary());
                    });
                    return;
                }

                BridgeResponse? initializeResponse = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "Initialize",
                    TimeoutMs = 5000
                }, 3000);

                if (initializeResponse == null || !initializeResponse.Success)
                {
                    OnUi(delegate
                    {
                        ShowFatalError(
                            "NFC okuyucu açılamadı",
                            "Okuyucunun COM4 bağlantısını ve sürücüsünü kontrol ediniz. Eski kiosk uygulaması açık olmamalıdır.",
                            FormatBridgeError(initializeResponse));
                    });
                    return;
                }

                BridgeResponse? healthResponse = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "HealthCheck",
                    TimeoutMs = 5000
                }, 3000);

                HardwareHealthResponse? health = healthResponse != null && !string.IsNullOrWhiteSpace(healthResponse.PayloadJson)
                    ? JsonConvert.DeserializeObject<HardwareHealthResponse>(healthResponse.PayloadJson)
                    : null;

                if (health == null || !health.Nfc.IsReady || !health.Nfc.IsSamVerified)
                {
                    OnUi(delegate
                    {
                        ShowFatalError(
                            "SAM doğrulaması başarısız",
                            "Gerçek İzmirim Kart bakiyesinin güvenli okunabilmesi için NFC okuyucu ve SAM kartı hazır olmalıdır.",
                            health == null ? FormatBridgeError(healthResponse) : health.Nfc.StatusMessage);
                    });
                    return;
                }

                // A thermal printer fault must not take the kiosk down. Balance enquiry
                // is the passenger-facing function and it does not need paper; the
                // printer state is surfaced in the footer and stays retryable from the
                // screen instead of blocking card reading entirely.
                OnUi(ApplyLanguage);

                _printerReady = health.Printer.IsReady;
                _printerStateKnown = true;
                string printerStatusMessage = health.Printer.StatusMessage;
                _journal.Record("KioskIdentity", new
                {
                    station = _hardwareSettings!.StationName,
                    kioskNumber = _hardwareSettings.KioskNumber,
                    source = _hardwareSettings.KioskNumberSource
                });
                _journal.Record("HardwareHealth", new
                {
                    nfcReady = health.Nfc.IsReady,
                    samVerified = health.Nfc.IsSamVerified,
                    printerReady = _printerReady,
                    printerName = health.Printer.PrinterName,
                    printerStatus = printerStatusMessage
                });

                _hardwareReady = true;
                OnUi(delegate
                {
                    PrinterToolsPanel.Visibility = Visibility.Visible;
                    UpdatePrinterIndicator(_printerReady, printerStatusMessage);
                    if (_printerReady)
                    {
                        SetHardwareStatus("TÜM DONANIM HAZIR", "NFC, SAM ve termal yazıcı hazır • Kart bekleniyor", ReadyBrush);
                    }
                    else
                    {
                        AddDiagnostic("Printer not ready: " + printerStatusMessage);
                        SetHardwareStatus("YAZICI HAZIR DEĞİL", "Kart okuma çalışıyor • Yazıcı için TANILA düğmesini kullanın", BusyBrush);
                    }
                    ShowIdle();
                });

                CardReadingLoop();
            }
            catch (Exception ex)
            {
                if (!_shutdownRequested)
                {
                    OnUi(delegate
                    {
                        ShowFatalError(
                            "Beklenmeyen donanım hatası",
                            "NFC okuyucu güvenli şekilde durduruldu. Yeniden deneyiniz.",
                            ex.GetType().Name + ": " + ex.Message);
                    });
                }
            }
            finally
            {
                lock (_workerLock)
                {
                    _workerRunning = false;
                }
            }
        }

        private void CardReadingLoop()
        {
            while (!_shutdownRequested && _hardwareReady)
            {
                if (_printerOperationInFlight)
                {
                    // Stop competing for the pipe until the printer request finishes.
                    Thread.Sleep(200);
                    continue;
                }

                BridgeResponse? waitResponse = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "WaitForCard",
                    TimeoutMs = 1200
                }, 2200);

                if (_shutdownRequested)
                {
                    return;
                }

                if (waitResponse == null)
                {
                    Thread.Sleep(250);
                    continue;
                }

                if (!waitResponse.Success)
                {
                    if (string.Equals(waitResponse.Error == null ? null : waitResponse.Error.Code, "ERR_CARD_NOT_PRESENT", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    _hardwareReady = false;
                    OnUi(delegate
                    {
                        ShowFatalError(
                            "Kart okuyucu ile iletişim kesildi",
                            "NFC okuyucu bağlantısını kontrol edip yeniden deneyiniz.",
                            FormatBridgeError(waitResponse));
                    });
                    return;
                }

                OnUi(delegate
                {
                    IdleDescriptionText.Text = _english ? "Reading card securely..." : "Kart güvenli biçimde okunuyor...";
                    FooterNfcText.Text = _english ? "NFC SENSOR: CARD PRESENT" : "NFC SENSÖR: KART VAR";
                    FooterNfcText.Foreground = ReadyBrush;
                });

                BridgeResponse? snapshotResponse = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "ReadCardSnapshot",
                    TimeoutMs = 5000
                }, 3000);

                CardSnapshotResponse? snapshot = snapshotResponse != null && snapshotResponse.Success && !string.IsNullOrWhiteSpace(snapshotResponse.PayloadJson)
                    ? JsonConvert.DeserializeObject<CardSnapshotResponse>(snapshotResponse.PayloadJson)
                    : null;

                if (!IsSafeSnapshot(snapshot))
                {
                    OnUi(delegate { ShowCardReadError(snapshotResponse, snapshot); });
                    WaitUntilCardRemoved();
                    OnUi(ShowIdle);
                    continue;
                }

                _currentSnapshot = snapshot;
                _cardPresent = true;
                // Only the pseudonym is journalled. The card number and the NFC UID stay
                // on the screen and on the passenger's own slip.
                _journal.Record("CardRead", new
                {
                    storagePseudonym = snapshot!.StoragePseudonym,
                    cardType = snapshot.CardType,
                    balanceMinor = snapshot.BalanceMinor,
                    balanceScale = snapshot.BalanceScale,
                    currency = snapshot.Currency,
                    samVerified = snapshot.IsSamVerified
                });
                OnUi(delegate { ShowAmountScreen(snapshot!); });
                WaitUntilCardRemoved();
                OnUi(HandleCardRemoved);
            }
        }

        private void WaitUntilCardRemoved()
        {
            while (!_shutdownRequested && _hardwareReady)
            {
                BridgeResponse? response = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "WaitForCardRemoval",
                    TimeoutMs = 900
                }, 2000);

                if (response != null && response.Success && !string.IsNullOrWhiteSpace(response.PayloadJson))
                {
                    CardRemovalResponse? result = JsonConvert.DeserializeObject<CardRemovalResponse>(response.PayloadJson);
                    if (result != null && result.IsRemoved)
                    {
                        return;
                    }
                }

                Thread.Sleep(150);
            }
        }

        private static bool IsSafeSnapshot(CardSnapshotResponse? snapshot)
        {
            return snapshot != null
                   && snapshot.IsCardValid
                   && snapshot.IsSamVerified
                   && snapshot.IsVerified
                   && snapshot.IsAuthoritative
                   && snapshot.IsBalanceScaleVerified
                   && snapshot.BalanceScale == 100
                   && snapshot.BalanceMinor >= 0
                   && !string.IsNullOrWhiteSpace(snapshot.CardNumber)
                   && !string.IsNullOrWhiteSpace(snapshot.CardUid);
        }

        private void ShowAmountScreen(CardSnapshotResponse snapshot)
        {
            _screen = KioskScreen.Amount;
            CardNumberText.Text = snapshot.CardNumber;
            CardUidText.Text = "NFC UID: " + snapshot.CardUid;
            BalanceText.Text = (snapshot.BalanceMinor / 100m).ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " TL";
            CardVerificationText.Text = _english ? "Verified by SAM • live card data" : "SAM doğrulaması başarılı • gerçek kart verisi";
            BalanceReceiptButton.IsEnabled = true;
            BalanceReceiptButton.Content = _english ? "PRINT BALANCE RECEIPT" : "BAKİYE FİŞİ YAZDIR";
            BalanceReceiptStatusText.Text = _printerReady
                ? string.Empty
                : (_english ? "Thermal printer is not ready; the receipt may not be produced." : "Termal yazıcı hazır değil; fiş çıkmayabilir.");
            BalanceReceiptStatusText.Foreground = _printerReady ? ReadyBrush : ErrorBrush;
            ShowOnly(AmountPanel);
            SetHardwareStatus("KART OKUNDU", _english ? "Live card balance is displayed" : "Gerçek kart kimliği ve bakiye gösteriliyor", ReadyBrush);
        }

        private void HandleCardRemoved()
        {
            bool activeTransactionScreen = _screen == KioskScreen.Amount || _screen == KioskScreen.Numpad || _screen == KioskScreen.Payment;
            _cardPresent = false;
            _currentSnapshot = null;
            FooterNfcText.Text = _english ? "NFC SENSOR: NO CARD" : "NFC SENSÖR: KART YOK";
            FooterNfcText.Foreground = ErrorBrush;

            if (!activeTransactionScreen)
            {
                ShowIdle();
                return;
            }

            WarningTitleText.Text = _english ? "WARNING: CARD REMOVED EARLY" : "UYARI: KART ERKEN ÇEKİLDİ";
            WarningBodyText.Text = _english
                ? "No payment was taken and no balance was written to the card. Returning to the home screen."
                : "İşlem başlatılmadı. Para çekilmedi ve karta yükleme yapılmadı. Ana ekrana dönülüyor.";
            WarningModal.Visibility = Visibility.Visible;
            SetHardwareStatus("İŞLEM İPTAL", _english ? "Card removed; no financial action was performed" : "Kart çekildi; finansal işlem yapılmadı", BusyBrush);

            var resetThread = new Thread(new ThreadStart(delegate
            {
                Thread.Sleep(2200);
                OnUi(delegate
                {
                    WarningModal.Visibility = Visibility.Collapsed;
                    ShowIdle();
                });
            })) { IsBackground = true, Name = "IZBAN Card Removal Reset" };
            resetThread.Start();
        }

        private void ShowIdle()
        {
            _screen = KioskScreen.Idle;
            _numpadDigits = "0";
            NumpadValueText.Text = "0 TL";
            WarningModal.Visibility = Visibility.Collapsed;
            if (_hardwareReady)
            {
                IdleDescriptionText.Text = _english
                    ? "Keep your card on the reader. The live balance is read after SAM verification."
                    : "Kartınızı okuyucunun üzerinde sabit tutunuz. Gerçek bakiye SAM ile doğrulanarak okunacaktır.";
                if (_printerReady)
                {
                    SetHardwareStatus("TÜM DONANIM HAZIR", _english ? "NFC, SAM and thermal printer ready • Waiting for card" : "NFC, SAM ve termal yazıcı hazır • Kart bekleniyor", ReadyBrush);
                }
                else
                {
                    SetHardwareStatus("YAZICI HAZIR DEĞİL", _english ? "Card reading works • Use DIAGNOSE for the printer" : "Kart okuma çalışıyor • Yazıcı için TANILA düğmesini kullanın", BusyBrush);
                }
            }
            ShowOnly(IdlePanel);
        }

        private void ShowOnly(UIElement panel)
        {
            IdlePanel.Visibility = panel == IdlePanel ? Visibility.Visible : Visibility.Collapsed;
            AmountPanel.Visibility = panel == AmountPanel ? Visibility.Visible : Visibility.Collapsed;
            NumpadPanel.Visibility = panel == NumpadPanel ? Visibility.Visible : Visibility.Collapsed;
            PaymentPanel.Visibility = panel == PaymentPanel ? Visibility.Visible : Visibility.Collapsed;
            ErrorPanel.Visibility = panel == ErrorPanel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AmountButton_Click(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            if (button == null || button.Tag == null || !_cardPresent || _currentSnapshot == null)
            {
                return;
            }

            int amount;
            if (int.TryParse(button.Tag.ToString(), out amount))
            {
                ShowPosNotConfigured(amount);
            }
        }

        private void OtherAmountButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_cardPresent || _currentSnapshot == null)
            {
                return;
            }

            _numpadDigits = "0";
            NumpadValueText.Text = "0 TL";
            _screen = KioskScreen.Numpad;
            ShowOnly(NumpadPanel);
        }

        private void NumpadKey_Click(object sender, RoutedEventArgs e)
        {
            Button? button = sender as Button;
            string key = button == null ? string.Empty : button.Content as string ?? string.Empty;
            if (key.Length != 1 || key[0] < '0' || key[0] > '9')
            {
                return;
            }

            string next = _numpadDigits == "0" ? key : _numpadDigits + key;
            int parsed;
            if (int.TryParse(next, out parsed) && parsed <= MaxManualAmount)
            {
                _numpadDigits = next;
                NumpadValueText.Text = _numpadDigits + " TL";
            }
        }

        private void NumpadDelete_Click(object sender, RoutedEventArgs e)
        {
            _numpadDigits = _numpadDigits.Length > 1 ? _numpadDigits.Substring(0, _numpadDigits.Length - 1) : "0";
            NumpadValueText.Text = _numpadDigits + " TL";
        }

        private void NumpadConfirm_Click(object sender, RoutedEventArgs e)
        {
            int amount;
            if (_cardPresent && _currentSnapshot != null && int.TryParse(_numpadDigits, out amount) && amount > 0)
            {
                ShowPosNotConfigured(amount);
            }
        }

        private void CancelNumpadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSnapshot != null && _cardPresent)
            {
                ShowAmountScreen(_currentSnapshot);
            }
            else
            {
                ShowIdle();
            }
        }

        private void ShowPosNotConfigured(int amount)
        {
            _screen = KioskScreen.Payment;
            SelectedAmountText.Text = (_english ? "SELECTED AMOUNT: " : "SEÇİLEN TUTAR: ") + amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " TL";
            PaymentStatusText.Text = _english ? "POS INTEGRATION PENDING" : "POS ENTEGRASYONU BEKLENİYOR";
            PaymentDetailsText.Text = _english
                ? "A certified bank POS SDK is not configured in this Windows 7 hardware profile. No money was charged and no amount was written to the İzmirim Card."
                : "Bu Windows 7 donanım profilinde banka POS SDK'sı henüz tanımlı değildir. Para çekilmedi ve İzmirim karta yükleme yapılmadı.";
            PaymentBackButton.Content = _english ? "RETURN TO AMOUNT SCREEN" : "TUTAR EKRANINA DÖN";
            ShowOnly(PaymentPanel);
            SetHardwareStatus("POS ENTEGRASYONU BEKLENİYOR", _english ? "No payment or card load was performed" : "Para çekilmedi ve karta yükleme yapılmadı", BusyBrush);
        }

        private void PaymentBackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSnapshot != null && _cardPresent)
            {
                ShowAmountScreen(_currentSnapshot);
            }
            else
            {
                ShowIdle();
            }
        }

        private void LanguageButton_Click(object sender, RoutedEventArgs e)
        {
            _english = !_english;
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            LanguageButton.Content = _english ? "🌍 TR" : "🌍 EN";
            HelpButton.Content = _english ? "❓ HELP" : "❓ YARDIM AL";
            string station = _hardwareSettings == null ? string.Empty : _hardwareSettings.StationName;
            string kioskNumber = _hardwareSettings == null ? string.Empty : _hardwareSettings.KioskNumber;
            StationText.Text = station.Length == 0
                ? (_english ? "STATION" : "İSTASYON")
                : station + " " + (_english ? "STATION" : "İSTASYONU");
            KioskStatusText.Text = (_english ? "Kiosk ID: #" : "Kiosk No: #") +
                (kioskNumber.Length == 0 ? "-" : kioskNumber) +
                (_english ? " / Hardware profile: Windows 7" : " / Donanım profili: Windows 7");
            KioskStatusText.ToolTip = _hardwareSettings == null || _hardwareSettings.KioskNumberSource.Length == 0
                ? null
                : (_english ? "Kiosk number source: " : "Kiosk numarası kaynağı: ") + _hardwareSettings.KioskNumberSource;
            IdleTitleText.Text = _english ? "PLEASE PLACE YOUR İZMİRİM CARD ON THE" : "LÜTFEN İZMİRİM KARTINIZI KART OKUYUCU";
            IdleTitleAccentText.Text = _english ? "CARD READER AREA" : "BÖLGESİNE YERLEŞTİRİNİZ";
            AmountTitleText.Text = _english ? "PLEASE SELECT THE AMOUNT YOU WANT TO LOAD" : "LÜTFEN YÜKLEMEK İSTEDİĞİNİZ TUTARI SEÇİNİZ";
            AmountButtonSubText1.Text = AmountButtonSubText2.Text = AmountButtonSubText3.Text = AmountButtonSubText4.Text = _english ? "Load Amount" : "Tutarını Yükle";
            OtherAmountTitleText.Text = _english ? "OTHER AMOUNT" : "DİĞER TUTAR";
            OtherAmountSubText.Text = _english ? "Enter a different amount by keypad" : "Klavyeden Farklı Tutar Girin";
            NumpadTitleText.Text = _english ? "ENTER THE AMOUNT YOU WANT TO LOAD" : "LÜTFEN YÜKLEMEK İSTEDİĞİNİZ TUTARI GİRİNİZ";
            CancelNumpadButton.Content = _english ? "CANCEL" : "VAZGEÇ";
            PrinterTestButton.Content = _english ? "PRINTER TEST RECEIPT" : "YAZICI TEST FİŞİ";
            PrinterDiagnoseButton.Content = _english ? "DIAGNOSE PRINTER" : "YAZICI TANILA";
            PrinterRetryButton.Content = _english ? "REINITIALIZE PRINTER" : "YAZICIYI YENİDEN BAŞLAT";
            PrinterPurgeButton.Content = _english ? "CLEAR QUEUE" : "KUYRUĞU TEMİZLE";
            PrinterOnlineButton.Content = _english ? "BRING PRINTER ONLINE" : "ÇEVRİMDIŞI MODU KAPAT";
            UseSelectedQueueButton.Content = _english ? "USE THIS QUEUE AND RESTART" : "BU KUYRUĞU KULLAN VE YENİDEN BAŞLAT";
            QueuePickerTitleText.Text = _english
                ? "If no paper appears the device may be on another queue. Pick one and try it:"
                : "Kâğıt çıkmıyorsa cihaz başka bir kuyrukta olabilir. Birini seçip deneyin:";
            ClosePrinterDiagnosticsButton.Content = _english ? "CLOSE" : "KAPAT";
            PrinterDiagnosticsTitleText.Text = _english ? "THERMAL PRINTER DIAGNOSTICS" : "TERMAL YAZICI TANILAMA";
            BalanceReceiptButton.Content = _english ? "PRINT BALANCE RECEIPT" : "BAKİYE FİŞİ YAZDIR";
            UpdatePrinterIndicator(_printerReady, string.Empty);
            HelpTitleText.Text = _english ? "USER GUIDE & HELP" : "KULLANIM KILAVUZU & YARDIM";
            HelpLine1.Text = _english ? "1. Keep your İzmirim Card on the reader." : "1. İzmirim Kartınızı okuyucu üzerinde sabit tutun.";
            HelpLine2.Text = _english ? "2. The live balance appears only after SAM verification." : "2. Gerçek bakiye SAM doğrulamasından sonra gösterilir.";
            HelpLine3.Text = _english ? "3. Amount selection is ready; a bank POS SDK is required for live payment." : "3. Tutar seçimi ekranı hazırdır; gerçek ödeme için POS SDK entegrasyonu gerekir.";
            CloseHelpButton.Content = _english ? "CLOSE" : "KAPAT";

            if (_screen == KioskScreen.Idle && _hardwareReady)
            {
                IdleDescriptionText.Text = _english
                    ? "Keep your card on the reader. The live balance is read after SAM verification."
                    : "Kartınızı okuyucunun üzerinde sabit tutunuz. Gerçek bakiye SAM ile doğrulanarak okunacaktır.";
            }
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpModal.Visibility = Visibility.Visible;
        }

        private void CloseHelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpModal.Visibility = Visibility.Collapsed;
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            StartHardwareWorker();
        }

        private void PrinterTestButton_Click(object sender, RoutedEventArgs e)
        {
            // Deliberately not gated on printer readiness: the test slip is the only way
            // to confirm that a repair actually worked.
            if (!_hardwareReady || !PrinterTestButton.IsEnabled)
            {
                return;
            }

            PrinterTestButton.IsEnabled = false;
            PrinterTestButton.Content = _english ? "SENDING TEST RECEIPT..." : "TEST FİŞİ GÖNDERİLİYOR...";
            _printerOperationInFlight = true;
            var printThread = new Thread(new ThreadStart(delegate
            {
                BridgeResponse? response = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "PrintTestReceipt",
                    TimeoutMs = 20000
                }, PrinterRequestConnectTimeoutMs);

                _printerOperationInFlight = false;

                bool printed = response != null && response.Success;
                string failureDetail = printed ? string.Empty : FormatBridgeError(response);
                _journal.Record("PrintTestReceipt", new { submitted = printed, detail = failureDetail });

                OnUi(delegate
                {
                    PrinterTestButton.IsEnabled = _hardwareReady;
                    PrinterTestButton.Content = _english ? "PRINTER TEST RECEIPT" : "YAZICI TEST FİŞİ";
                    SetPrinterState(printed, printed ? string.Empty : failureDetail);
                    if (printed)
                    {
                        IdleDescriptionText.Text = _english ? "Test receipt sent. Check the physical paper output." : "Test fişi yazıcıya gönderildi. Fiziksel kâğıt çıktısını kontrol ediniz.";
                        SetHardwareStatus("YAZICI TESTİ GÖNDERİLDİ", _english ? "Check the physical paper output" : "Test fişinin çıktığını kontrol ediniz", ReadyBrush);
                    }
                    else
                    {
                        IdleDescriptionText.Text = (_english ? "Test receipt could not be printed: " : "Test fişi yazdırılamadı: ") + failureDetail;
                        SetHardwareStatus("YAZICI TEST HATASI", _english ? "Check thermal printer and paper" : "Termal yazıcı ve kâğıdı kontrol ediniz", ErrorBrush);
                    }
                });
            })) { IsBackground = true, Name = "IZBAN Printer Test Worker" };
            printThread.Start();
        }

        private void PrinterDiagnoseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrinterDiagnoseButton.IsEnabled)
            {
                return;
            }

            PrinterDiagnoseButton.IsEnabled = false;
            PrinterDiagnoseButton.Content = _english ? "DIAGNOSING..." : "TANILANIYOR...";
            RunPrinterDiagnostics();
        }

        private void PrinterRetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrinterRetryButton.IsEnabled)
            {
                return;
            }

            PrinterRetryButton.IsEnabled = false;
            PrinterRetryButton.Content = _english ? "REINITIALIZING..." : "YENİDEN BAŞLATILIYOR...";
            _printerOperationInFlight = true;
            var retryThread = new Thread(new ThreadStart(delegate
            {
                BridgeResponse? response = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "PrinterReinitialize",
                    TimeoutMs = 20000
                }, PrinterRequestConnectTimeoutMs);

                _printerOperationInFlight = false;

                bool recovered = response != null && response.Success;
                _journal.Record("PrinterReinitialize", new { recovered, detail = recovered ? string.Empty : FormatBridgeError(response) });

                OnUi(delegate
                {
                    PrinterRetryButton.IsEnabled = true;
                    PrinterRetryButton.Content = _english ? "REINITIALIZE PRINTER" : "YAZICIYI YENİDEN BAŞLAT";
                    SetPrinterState(recovered, recovered ? string.Empty : FormatBridgeError(response));
                });

                RunPrinterDiagnostics();
            })) { IsBackground = true, Name = "IZBAN Printer Reinit Worker" };
            retryThread.Start();
        }

        private void RunPrinterDiagnostics()
        {
            _printerOperationInFlight = true;
            var diagnoseThread = new Thread(new ThreadStart(delegate
            {
                BridgeResponse? response = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "PrinterDiagnose",
                    TimeoutMs = 20000
                }, PrinterRequestConnectTimeoutMs);

                _printerOperationInFlight = false;

                PrinterDiagnosticsResponse? report = response != null && response.Success && !string.IsNullOrWhiteSpace(response.PayloadJson)
                    ? JsonConvert.DeserializeObject<PrinterDiagnosticsResponse>(response.PayloadJson)
                    : null;

                OnUi(delegate
                {
                    PrinterDiagnoseButton.IsEnabled = true;
                    PrinterDiagnoseButton.Content = _english ? "DIAGNOSE PRINTER" : "YAZICI TANILA";

                    if (report == null)
                    {
                        bool unreachable = response == null;
                        PrinterDiagnosticsSummaryText.Text = unreachable
                            ? (_english
                                ? "The hardware service could not be reached in time."
                                : "Donanım servisine zamanında ulaşılamadı.")
                            : (_english
                                ? "The hardware bridge did not return a printer report."
                                : "Donanım köprüsü yazıcı raporu döndürmedi.");
                        PrinterDiagnosticsSummaryText.Foreground = ErrorBrush;
                        PrinterDiagnosticsBodyText.Text = unreachable
                            ? "Bridge sureci " + (PrinterRequestConnectTimeoutMs / 1000) + " saniye icinde yanit vermedi.\n\n" +
                              "Bridge calisiyorsa mesguldur; birkac saniye sonra tekrar deneyin.\n" +
                              "Sorun surerse Bridge surecini kapatip uygulamayi yeniden baslatin.\n\n" +
                              "Son kopru mesajlari:\n" + GetDiagnosticSummary()
                            : FormatBridgeError(response);
                    }
                    else
                    {
                        SetPrinterState(report.IsReady, report.StatusMessage);
                        PrinterDiagnosticsSummaryText.Text = report.IsReady
                            ? (_english ? "Printer is ready." : "Yazıcı hazır.")
                            : report.StatusMessage;
                        PrinterDiagnosticsSummaryText.Foreground = report.IsReady ? ReadyBrush : ErrorBrush;
                        PrinterDiagnosticsBodyText.Text = FormatPrinterDiagnostics(report);
                        PopulateQueuePicker(report);
                    }

                    PrinterDiagnosticsModal.Visibility = Visibility.Visible;
                });
            })) { IsBackground = true, Name = "IZBAN Printer Diagnose Worker" };
            diagnoseThread.Start();
        }

        private string FormatPrinterDiagnostics(PrinterDiagnosticsResponse report)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Yapılandırılan ad : " + Dash(report.ConfiguredPrinterName));
            builder.AppendLine("Windows'ta kurulu : " + (report.IsInstalled ? "EVET" : "HAYIR"));
            builder.AppendLine("Eşleşen kuyruk    : " + Dash(report.ResolvedPrinterName));
            builder.AppendLine("Sürücü / Port     : " + Dash(report.DriverName) + " / " + Dash(report.PortName));
            builder.AppendLine();
            builder.AppendLine("Varsayılan (önce) : " + Dash(report.DefaultPrinterBefore));
            builder.AppendLine("Varsayılan (sonra): " + Dash(report.DefaultPrinterAfter));
            builder.AppendLine("FİŞ NEREYE GİDER  : " + Dash(report.ReceiptRoutingDevice) + "   <-- KioskPrint.dll bunu okur");
            builder.AppendLine("Yönlendirme       : " + (report.DefaultPrinterRoutingApplied ? "UYGULANDI" : "UYGULANAMADI"));
            builder.AppendLine();
            builder.AppendLine("Spooler okundu    : " + (report.SpoolerStatusRead ? "EVET" : "HAYIR"));
            builder.AppendLine("Durum bayrakları  : " + report.SpoolerStatusFlags + DescribePrinterStatus(report.SpoolerStatusFlags));
            builder.AppendLine("ÇEVRİMDIŞI MOD    : " + (report.IsWorkOffline ? "AÇIK  <-- ISLER GONDERILMIYOR!" : "kapalı"));
            builder.AppendLine("Win32 hata kodu   : " + report.Win32Error);
            builder.AppendLine("Kuyruktaki iş     : " + (report.VendorProbeCompleted ? report.VendorQueuedJobCount.ToString() : "okunamadı " + report.VendorProbeError));
            if (report.QueuedJobStates != null && report.QueuedJobStates.Count > 0)
            {
                builder.AppendLine("İşlerin durumu    : " + string.Join(", ", report.QueuedJobStates.ToArray()));
            }
            builder.AppendLine();
            builder.AppendLine("Windows'un gördüğü seri portlar (cihaz takılı mı?):");
            if (report.SerialPorts == null || report.SerialPorts.Count == 0)
            {
                builder.AppendLine("  (hiç yok - USB->sanal COM cihazı bağlı değil)");
            }
            else
            {
                foreach (string port in report.SerialPorts)
                {
                    builder.AppendLine("  • " + port);
                }
            }
            builder.AppendLine();
            builder.AppendLine("Kurulu kuyruklar (port = cihazın gerçekte bağlı olduğu yer):");
            if (report.InstalledPrinterDetails == null || report.InstalledPrinterDetails.Count == 0)
            {
                builder.AppendLine("  (hiç yok)");
            }
            else
            {
                foreach (InstalledPrinterInfo queue in report.InstalledPrinterDetails)
                {
                    string marker = queue.IsConfigured ? ">> " : "   ";
                    builder.AppendLine(marker + Pad(queue.Name, 38) + " " + Pad(queue.PortName, 10) +
                        " is:" + queue.QueuedJobCount + (queue.IsWorkOffline ? "  [CEVRIMDISI]" : string.Empty) +
                        (queue.IsDefault ? "  [varsayilan]" : string.Empty));
                }
                builder.AppendLine();
                builder.AppendLine(">> = yapılandırılan kuyruk. Aynı yazıcının birden çok kopyası varsa,");
                builder.AppendLine("   cihaz yalnızca BİR portta canlıdır; diğer kuyruklar işi alır ama basmaz.");
            }
            builder.AppendLine();
            builder.AppendLine("Ayar dosyası: KioskHardware.config.json -> ThermalPrinterName");
            return builder.ToString();
        }

        private static string DescribePrinterStatus(uint flags)
        {
            var parts = new List<string>();
            if ((flags & 0x00000010u) != 0) parts.Add("KAĞIT BİTTİ");
            if ((flags & 0x00000040u) != 0) parts.Add("KAĞIT SORUNU");
            if ((flags & 0x00000080u) != 0) parts.Add("ÇEVRİMDIŞI");
            if ((flags & 0x00400000u) != 0) parts.Add("KAPAK AÇIK");
            return parts.Count == 0 ? string.Empty : "  (" + string.Join(", ", parts.ToArray()) + ")";
        }

        /// <summary>
        /// Offers every queue that shares the configured driver. Which one the device
        /// is actually behind cannot be read from Windows - it has to be tried - so
        /// the candidates are ordered with the likeliest first: a USB port only exists
        /// while a USB printer is enumerated on it, whereas COM and LPT ports are
        /// always present whether or not anything is plugged in.
        /// </summary>
        private void PopulateQueuePicker(PrinterDiagnosticsResponse report)
        {
            QueuePickerList.Items.Clear();
            _queueCandidates.Clear();

            if (report.InstalledPrinterDetails == null || report.InstalledPrinterDetails.Count == 0)
            {
                QueuePickerPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var candidates = new List<InstalledPrinterInfo>(report.InstalledPrinterDetails);
            candidates.Sort(delegate(InstalledPrinterInfo left, InstalledPrinterInfo right)
            {
                int byLikelihood = PortLikelihood(right.PortName).CompareTo(PortLikelihood(left.PortName));
                return byLikelihood != 0 ? byLikelihood : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });

            foreach (InstalledPrinterInfo queue in candidates)
            {
                _queueCandidates.Add(queue.Name);
                QueuePickerList.Items.Add(Pad(queue.Name, 40) + " " + Pad(queue.PortName, 10) +
                    " is:" + queue.QueuedJobCount + (queue.IsConfigured ? "  [su an kullanilan]" : string.Empty));
            }

            QueuePickerList.SelectedIndex = 0;
            QueuePickerPanel.Visibility = Visibility.Visible;
        }

        private static int PortLikelihood(string portName)
        {
            string port = (portName ?? string.Empty).ToUpperInvariant();
            if (port.StartsWith("USB")) return 3;
            if (port.StartsWith("COM")) return 2;
            if (port.StartsWith("LPT")) return 1;
            return 0;
        }

        private void UseSelectedQueueButton_Click(object sender, RoutedEventArgs e)
        {
            int index = QueuePickerList.SelectedIndex;
            if (index < 0 || index >= _queueCandidates.Count || !UseSelectedQueueButton.IsEnabled)
            {
                return;
            }

            string selectedQueue = _queueCandidates[index];
            UseSelectedQueueButton.IsEnabled = false;
            UseSelectedQueueButton.Content = _english ? "RESTARTING..." : "YENİDEN BAŞLATILIYOR...";

            try
            {
                KioskHardwareSettings.SaveThermalPrinterName(selectedQueue);
            }
            catch (Exception ex)
            {
                UseSelectedQueueButton.IsEnabled = true;
                UseSelectedQueueButton.Content = _english ? "USE THIS QUEUE AND RESTART" : "BU KUYRUĞU KULLAN VE YENİDEN BAŞLAT";
                PrinterDiagnosticsSummaryText.Text = (_english ? "Settings file could not be written: " : "Ayar dosyası yazılamadı: ") + ex.Message;
                PrinterDiagnosticsSummaryText.Foreground = ErrorBrush;
                PrinterDiagnosticsBodyText.Text = "KioskHardware.config.json salt okunur olabilir veya Windows Embedded write filter aciktir.\n" +
                    "Dosyayi elle duzenleyip uygulamayi yeniden baslatin.";
                return;
            }

            _journal.Record("ThermalPrinterQueueChanged", new { queue = selectedQueue });
            RestartApplication();
        }

        /// <summary>
        /// KioskPrint.dll binds to a printer on its first call and never rebinds, so a
        /// different queue only takes effect in a fresh process. The bridge is stopped
        /// first: a surviving one would be reused by the new instance and would still
        /// be pointed at the old queue.
        /// </summary>
        private void RestartApplication()
        {
            SendRequest(new BridgeRequest { RequestId = Guid.NewGuid().ToString("N"), Command = "Shutdown" }, 2000);
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Thread.Sleep(150);
                if (SendRequest(new BridgeRequest { RequestId = Guid.NewGuid().ToString("N"), Command = "GetBridgeVersion" }, 200) == null)
                {
                    break;
                }
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                AddDiagnostic("Restart failed: " + ex.Message);
            }

            _shutdownRequested = true;
            Application.Current.Shutdown();
        }

        private static string Pad(string value, int width)
        {
            string text = value ?? string.Empty;
            return text.Length >= width ? text.Substring(0, width) : text.PadRight(width);
        }

        private static string Dash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private void PrinterOnlineButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrinterOnlineButton.IsEnabled)
            {
                return;
            }

            PrinterOnlineButton.IsEnabled = false;
            PrinterOnlineButton.Content = _english ? "BRINGING ONLINE..." : "AÇILIYOR...";
            _printerOperationInFlight = true;
            var onlineThread = new Thread(new ThreadStart(delegate
            {
                BridgeResponse? response = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "PrinterClearOffline",
                    TimeoutMs = 20000
                }, PrinterRequestConnectTimeoutMs);

                _printerOperationInFlight = false;

                bool cleared = response != null && response.Success;
                _journal.Record("PrinterClearOffline", new { cleared, detail = cleared ? string.Empty : FormatBridgeError(response) });

                OnUi(delegate
                {
                    PrinterOnlineButton.IsEnabled = true;
                    PrinterOnlineButton.Content = _english ? "BRING PRINTER ONLINE" : "ÇEVRİMDIŞI MODU KAPAT";
                    if (!cleared)
                    {
                        PrinterDiagnosticsSummaryText.Text = FormatBridgeError(response);
                        PrinterDiagnosticsSummaryText.Foreground = ErrorBrush;
                    }
                });

                RunPrinterDiagnostics();
            })) { IsBackground = true, Name = "IZBAN Printer Online Worker" };
            onlineThread.Start();
        }

        private void PrinterPurgeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrinterPurgeButton.IsEnabled)
            {
                return;
            }

            PrinterPurgeButton.IsEnabled = false;
            PrinterPurgeButton.Content = _english ? "CLEARING..." : "TEMİZLENİYOR...";
            _printerOperationInFlight = true;
            var purgeThread = new Thread(new ThreadStart(delegate
            {
                BridgeResponse? response = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "PrinterPurgeQueue",
                    TimeoutMs = 20000
                }, PrinterRequestConnectTimeoutMs);

                _printerOperationInFlight = false;

                bool purged = response != null && response.Success;
                _journal.Record("PrinterPurgeQueue", new { purged, detail = purged ? string.Empty : FormatBridgeError(response) });

                OnUi(delegate
                {
                    PrinterPurgeButton.IsEnabled = true;
                    PrinterPurgeButton.Content = _english ? "CLEAR QUEUE" : "KUYRUĞU TEMİZLE";
            PrinterOnlineButton.Content = _english ? "BRING PRINTER ONLINE" : "ÇEVRİMDIŞI MODU KAPAT";
            UseSelectedQueueButton.Content = _english ? "USE THIS QUEUE AND RESTART" : "BU KUYRUĞU KULLAN VE YENİDEN BAŞLAT";
            QueuePickerTitleText.Text = _english
                ? "If no paper appears the device may be on another queue. Pick one and try it:"
                : "Kâğıt çıkmıyorsa cihaz başka bir kuyrukta olabilir. Birini seçip deneyin:";
                    if (!purged)
                    {
                        PrinterDiagnosticsSummaryText.Text = FormatBridgeError(response);
                        PrinterDiagnosticsSummaryText.Foreground = ErrorBrush;
                    }
                });

                RunPrinterDiagnostics();
            })) { IsBackground = true, Name = "IZBAN Printer Purge Worker" };
            purgeThread.Start();
        }

        private void ClosePrinterDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            PrinterDiagnosticsModal.Visibility = Visibility.Collapsed;
        }

        private void SetPrinterState(bool ready, string statusMessage)
        {
            _printerReady = ready;
            _printerStateKnown = true;
            UpdatePrinterIndicator(ready, statusMessage);
        }

        private void UpdatePrinterIndicator(bool ready, string statusMessage)
        {
            // Before the first health check there is nothing to report; showing a red
            // "not ready" during startup would be a false alarm.
            if (!_printerStateKnown)
            {
                FooterPrinterText.Text = _english ? "PRINTER: CHECKING" : "YAZICI: KONTROL EDİLİYOR";
                FooterPrinterText.Foreground = BusyBrush;
                return;
            }

            FooterPrinterText.Text = ready
                ? (_english ? "PRINTER: READY" : "YAZICI: HAZIR")
                : (_english ? "PRINTER: NOT READY" : "YAZICI: HAZIR DEĞİL");
            FooterPrinterText.Foreground = ready ? ReadyBrush : ErrorBrush;
            FooterPrinterText.ToolTip = string.IsNullOrWhiteSpace(statusMessage) ? null : statusMessage;
        }

        private void BalanceReceiptButton_Click(object sender, RoutedEventArgs e)
        {
            CardSnapshotResponse? snapshot = _currentSnapshot;
            if (snapshot == null || !_cardPresent || !BalanceReceiptButton.IsEnabled)
            {
                return;
            }

            BalanceReceiptButton.IsEnabled = false;
            BalanceReceiptButton.Content = _english ? "PRINTING..." : "YAZDIRILIYOR...";
            BalanceReceiptStatusText.Text = string.Empty;

            DateTime timestamp = DateTime.Now;
            KioskHardwareSettings? settings = _hardwareSettings;
            if (settings == null)
            {
                BalanceReceiptStatusText.Text = _english
                    ? "Kiosk identity is not loaded; the receipt was not printed."
                    : "Kiosk kimliği yüklenmedi; fiş yazdırılmadı.";
                BalanceReceiptStatusText.Foreground = ErrorBrush;
                BalanceReceiptButton.IsEnabled = true;
                BalanceReceiptButton.Content = _english ? "PRINT BALANCE RECEIPT" : "BAKİYE FİŞİ YAZDIR";
                return;
            }

            string body = ReceiptDocumentBuilder.BuildBalanceReceipt(
                snapshot, settings.StationName, settings.KioskNumber, timestamp, _english);
            string idempotencyKey = ReceiptDocumentBuilder.BuildIdempotencyKey(snapshot, timestamp);

            _printerOperationInFlight = true;
            var printThread = new Thread(new ThreadStart(delegate
            {
                BridgeResponse? response = SendRequest(new BridgeRequest
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    Command = "PrintReceipt",
                    TimeoutMs = 20000,
                    PayloadJson = JsonConvert.SerializeObject(new PrintReceiptRequest
                    {
                        Text = body,
                        IdempotencyKey = idempotencyKey
                    })
                }, PrinterRequestConnectTimeoutMs);

                _printerOperationInFlight = false;

                bool printed = response != null && response.Success;
                string failureDetail = printed ? string.Empty : FormatBridgeError(response);
                _journal.Record("PrintBalanceReceipt", new
                {
                    submitted = printed,
                    idempotencyKey,
                    storagePseudonym = snapshot.StoragePseudonym,
                    balanceMinor = snapshot.BalanceMinor,
                    balanceScale = snapshot.BalanceScale,
                    detail = failureDetail
                });

                OnUi(delegate
                {
                    BalanceReceiptButton.IsEnabled = _cardPresent;
                    BalanceReceiptButton.Content = _english ? "PRINT BALANCE RECEIPT" : "BAKİYE FİŞİ YAZDIR";
                    SetPrinterState(printed, failureDetail);
                    BalanceReceiptStatusText.Text = printed
                        ? (_english ? "Receipt sent. Please take your slip." : "Fiş yazıcıya gönderildi. Lütfen fişinizi alınız.")
                        : (_english ? "Receipt could not be printed: " : "Fiş yazdırılamadı: ") + failureDetail;
                    BalanceReceiptStatusText.Foreground = printed ? ReadyBrush : ErrorBrush;
                });
            })) { IsBackground = true, Name = "IZBAN Balance Receipt Worker" };
            printThread.Start();
        }

        private void ShowCardReadError(BridgeResponse? response, CardSnapshotResponse? snapshot)
        {
            string code = snapshot == null ? string.Empty : snapshot.ErrorCode;
            if (string.IsNullOrWhiteSpace(code) && response != null && response.Error != null)
            {
                code = response.Error.Code;
            }

            ErrorTitleText.Text = _english ? "CARD COULD NOT BE READ SECURELY" : "KART GÜVENLİ BİÇİMDE OKUNAMADI";
            ErrorDescriptionText.Text = _english ? "Remove the card and try again." : "Kartı okuyucudan kaldırıp birkaç saniye sonra yeniden yaklaştırınız.";
            TechnicalErrorText.Text = string.IsNullOrWhiteSpace(code) ? "Card read response was not valid." : "Hata kodu: " + code;
            RetryButton.Visibility = Visibility.Collapsed;
            _screen = KioskScreen.Error;
            ShowOnly(ErrorPanel);
            SetHardwareStatus("KART OKUMA HATASI", _english ? "Remove card and try again" : "Kartı kaldırıp tekrar yaklaştırınız", ErrorBrush);
        }

        private void ShowFatalError(string title, string description, string technicalDetail)
        {
            ErrorTitleText.Text = title;
            ErrorDescriptionText.Text = description;
            TechnicalErrorText.Text = technicalDetail;
            RetryButton.Visibility = Visibility.Visible;
            _screen = KioskScreen.Error;
            ShowOnly(ErrorPanel);
            SetHardwareStatus("DONANIM HATASI", "NFC okuyucu veya termal yazıcı kullanılamıyor", ErrorBrush);
        }

        private void SetHardwareStatus(string header, string footer, Brush color)
        {
            // Header status uses the existing station area; footer remains the prominent health signal.
            FooterStatusText.Text = footer;
            FooterStatusDot.Fill = color;
        }

        private bool EnsureBridgeStarted()
        {
            BridgeResponse? existing = SendRequest(new BridgeRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Command = "GetBridgeVersion"
            }, 300);

            if (existing != null && existing.Success)
            {
                string existingVersion = GetBridgeVersion(existing);
                if (string.Equals(existingVersion, ExpectedBridgeVersion, StringComparison.Ordinal))
                {
                    AddDiagnostic("Compatible Bridge already running: " + existingVersion + ".");
                    return true;
                }

                AddDiagnostic("Closing stale Bridge version '" + existingVersion + "'; expected '" + ExpectedBridgeVersion + "'.");
                SendRequest(new BridgeRequest { RequestId = Guid.NewGuid().ToString("N"), Command = "Shutdown" }, 800);

                bool staleBridgeStopped = false;
                for (int attempt = 0; attempt < 30 && !_shutdownRequested; attempt++)
                {
                    Thread.Sleep(150);
                    BridgeResponse? probe = SendRequest(new BridgeRequest { RequestId = Guid.NewGuid().ToString("N"), Command = "GetBridgeVersion" }, 150);
                    if (probe == null)
                    {
                        staleBridgeStopped = true;
                        break;
                    }
                }

                if (!staleBridgeStopped)
                {
                    AddDiagnostic("Stale Bridge could not be stopped. Close every older IZBAN Kiosk window and retry.");
                    return false;
                }
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "Bridge", BridgeExeName),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "Bridge", BridgeExeName))
            };
            string? bridgePath = candidates.FirstOrDefault(File.Exists);
            if (bridgePath == null)
            {
                AddDiagnostic("Bridge executable is missing from the Bridge folder.");
                return false;
            }

            byte[] secret = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(secret);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = bridgePath,
                Arguments = "--port " + QuoteCommandLineArgument(_hardwareSettings!.NfcComPort) +
                            " --printer " + QuoteCommandLineArgument(_hardwareSettings.ThermalPrinterName),
                WorkingDirectory = Path.GetDirectoryName(bridgePath) ?? baseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.EnvironmentVariables["IZBAN_HMAC_SECRET"] = Convert.ToBase64String(secret);

            try
            {
                _bridgeProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _bridgeProcess.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { AddDiagnostic(args.Data); };
                _bridgeProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { AddDiagnostic(args.Data); };
                if (!_bridgeProcess.Start())
                {
                    AddDiagnostic("Bridge process could not be started.");
                    return false;
                }

                _ownsBridgeProcess = true;
                _bridgeProcess.BeginOutputReadLine();
                _bridgeProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                AddDiagnostic("Bridge launch failed: " + ex.Message);
                return false;
            }

            for (int attempt = 0; attempt < 15 && !_shutdownRequested; attempt++)
            {
                Thread.Sleep(300);
                if (_bridgeProcess.HasExited)
                {
                    AddDiagnostic("Bridge exited with code " + _bridgeProcess.ExitCode + ".");
                    return false;
                }

                BridgeResponse? response = SendRequest(new BridgeRequest { RequestId = Guid.NewGuid().ToString("N"), Command = "GetBridgeVersion" }, 300);
                if (response != null && response.Success)
                {
                    string startedVersion = GetBridgeVersion(response);
                    if (string.Equals(startedVersion, ExpectedBridgeVersion, StringComparison.Ordinal))
                    {
                        AddDiagnostic("Started Bridge version " + startedVersion + ".");
                        return true;
                    }

                    AddDiagnostic("Bridge version mismatch after startup: got '" + startedVersion + "', expected '" + ExpectedBridgeVersion + "'.");
                    return false;
                }
            }

            AddDiagnostic("Bridge startup timeout.");
            return false;
        }

        private static string QuoteCommandLineArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string GetBridgeVersion(BridgeResponse response)
        {
            if (string.IsNullOrWhiteSpace(response.PayloadJson))
            {
                return "unknown";
            }

            try
            {
                BridgeVersionPayload? payload = JsonConvert.DeserializeObject<BridgeVersionPayload>(response.PayloadJson);
                return payload == null || string.IsNullOrWhiteSpace(payload.Version) ? "unknown" : payload.Version;
            }
            catch
            {
                return "unknown";
            }
        }

        private sealed class BridgeVersionPayload
        {
            public string Version { get; set; } = string.Empty;
        }

        private static BridgeResponse? SendRequest(BridgeRequest request, int connectTimeoutMilliseconds)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None))
                {
                    pipe.Connect(connectTimeoutMilliseconds);
                    NamedPipeFraming.WriteMessage(pipe, JsonConvert.SerializeObject(request));
                    string responseText = NamedPipeFraming.ReadMessage(pipe);
                    return string.IsNullOrWhiteSpace(responseText) ? null : JsonConvert.DeserializeObject<BridgeResponse>(responseText);
                }
            }
            catch
            {
                return null;
            }
        }

        private void AddDiagnostic(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            lock (_bridgeDiagnostics)
            {
                while (_bridgeDiagnostics.Count >= 8)
                {
                    _bridgeDiagnostics.Dequeue();
                }
                _bridgeDiagnostics.Enqueue(message!.Trim());
            }
        }

        private string GetDiagnosticSummary()
        {
            lock (_bridgeDiagnostics)
            {
                return _bridgeDiagnostics.Count == 0 ? "Bridge yanıt vermedi." : string.Join(" | ", _bridgeDiagnostics.ToArray());
            }
        }

        private static string FormatBridgeError(BridgeResponse? response)
        {
            if (response == null || response.Error == null || string.IsNullOrWhiteSpace(response.Error.Code))
            {
                return "Donanım servisinden geçerli yanıt alınamadı.";
            }
            return response.Error.Code + ": " + response.Error.Message;
        }

        private void OnUi(Action action)
        {
            if (_shutdownRequested)
            {
                return;
            }

            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.Invoke(action);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.X && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                Close();
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _shutdownRequested = true;
            _hardwareReady = false;
            _clockTimer.Stop();

            if (_ownsBridgeProcess && _bridgeProcess != null)
            {
                try
                {
                    if (!_bridgeProcess.HasExited)
                    {
                        _bridgeProcess.Kill();
                        _bridgeProcess.WaitForExit(2000);
                    }
                }
                catch
                {
                    // The owned child may already be closed.
                }
                finally
                {
                    _bridgeProcess.Dispose();
                    _bridgeProcess = null;
                }
            }
        }
    }
}
