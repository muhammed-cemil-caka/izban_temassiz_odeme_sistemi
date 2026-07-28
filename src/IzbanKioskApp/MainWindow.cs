using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IzbanKioskApp.Core;
using Avalonia.Platform;
using Avalonia.Media.Imaging;

namespace IzbanKioskApp
{
    public partial class MainWindow : Window
    {
        // UI Controls Binds
        private TextBlock _clockText = null!;
        private Button _languageToggleBtn = null!;
        private TextBlock _languageToggleText = null!;
        private Button _helpBtn = null!;
        private TextBlock _helpBtnText = null!;
        
        private TextBlock _headerStationText = null!;
        private TextBlock _headerKioskStatusText = null!;
        
        private TextBlock _cardBrandText = null!;
        private TextBlock _cardSubBrandText = null!;
        private TextBlock _idleHeadingText1 = null!;
        private TextBlock _idleHeadingText2 = null!;
        private TextBlock _idleSubHeadingText = null!;
        
        private TextBlock _cardInfoLabel = null!;
        private TextBlock _cardUidText = null!;
        private TextBlock _balanceLabel = null!;
        private TextBlock _currentBalanceText = null!;
        
        private TextBlock _selectAmountInstruction = null!;
        private TextBlock _btn20SubText = null!;
        private TextBlock _btn50SubText = null!;
        private TextBlock _btn100SubText = null!;
        private TextBlock _btn200SubText = null!;
        private TextBlock _btnOtherText = null!;
        private TextBlock _btnOtherSubText = null!;
        
        private TextBlock _selectedAmountText = null!;
        private TextBlock _paymentStatusText = null!;
        private TextBlock _paymentLabelDetails = null!;
        
        private TextBlock _successHeadingText = null!;
        private TextBlock _finalBalanceText = null!;
        private TextBlock _successSubHeadingText1 = null!;
        private TextBlock _successSubHeadingText2 = null!;
        
        private TextBlock _footerStatusText = null!;
        private TextBlock _footerNfcLabel = null!;
        private Button _toggleCardBtn = null!;
        
        private TextBlock _numpadTitleText = null!;
        private TextBlock _numpadValueText = null!;
        private Button _cancelNumpadBtn = null!;
        private Button _closeHelpBtn = null!;
        private Image _izbanLogoImage = null!;
        private Grid _fallbackLogo = null!;
        private Image _posLogoImage = null!;
        private TextBlock _posFallbackText = null!;
        private Image _izmirimCardImage = null!;
        private Grid _fallbackCardGraphic = null!;

        // Screens Binds
        private Grid _idleScreen = null!;
        private Grid _amountScreen = null!;
        private Grid _numpadScreen = null!;
        private Grid _paymentScreen = null!;
        private Grid _successScreen = null!;
        private Grid _helpModal = null!;
        private Grid _warningModal = null!;

        // Buttons Binds
        private Button _btn20 = null!;
        private Button _btn50 = null!;
        private Button _btn100 = null!;
        private Button _btn200 = null!;
        private Button _btnOther = null!;

        // State Machine Vars
        private decimal _currentBalance = 45.50m;
        private string _cardUid = "35-IZM-9921";
        private CancellationTokenSource? _paymentCts;
        private string _currentLang = "TR";
        private bool _isCardPresent = false;
        private string _numpadValue = "0";
        private bool _isWarningModalActive = false;

        // Localization Dictionary
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
                { "SuccessSub2", "Kartınızı okuyucudan çekebilirsiniz. Kart çekildiğinde işlem sonlanacaktır." },
                { "FooterStatus", "Kart Okuyucu ve POS Terminali Hazır" },
                { "FooterNfcNoCard", "NFC SENSÖR: KART YOK" },
                { "FooterNfcCard", "NFC SENSÖR: KART VAR" },
                { "HelpTitle", "KULLANIM KILAVUZU & YARDIM" },
                { "Help1", "1. İzmirim Kartınızı alttaki okuyucu haznesine yerleştirin." },
                { "Help2", "2. Yüklemek istediğiniz hazır tutarı seçin veya DİĞER TUTAR butonundan klavyeyi açarak el ile girin." },
                { "Help3", "3. Kredi kartınızı temassız banka POS cihazına yaklaştırıp ödemeyi tamamlayın." },
                { "Help4", "İletişim Hattı: ALO 153 / Çağrı Merkezi: 444 15 20" },
                { "HelpClose", "Kapat" }
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
                { "SuccessSub2", "You can now remove your card from the reader. System will reset on card pull." },
                { "FooterStatus", "Card Reader and POS Terminal Ready" },
                { "FooterNfcNoCard", "NFC SENSOR: NO CARD" },
                { "FooterNfcCard", "NFC SENSOR: CARD PRESENT" },
                { "HelpTitle", "USER GUIDE & HELP" },
                { "Help1", "1. Place your Izmirim Card in the reader slot at the bottom." },
                { "Help2", "2. Choose a preselected amount or tap OTHER AMOUNT to input a custom balance." },
                { "Help3", "3. Tap your credit card on the contactless POS keypad to proceed with payment." },
                { "Help4", "Hotline: ALO 153 / Calls Center: 444 15 20" },
                { "HelpClose", "Close" }
            } }
        };

        public MainWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);

            // Bind UI Core Elements
            _clockText = this.FindControl<TextBlock>("ClockText") ?? throw new Exception("ClockText not found");
            _languageToggleBtn = this.FindControl<Button>("LanguageToggleBtn") ?? throw new Exception("LanguageToggleBtn not found");
            _languageToggleText = this.FindControl<TextBlock>("LanguageToggleText") ?? throw new Exception("LanguageToggleText not found");
            _helpBtn = this.FindControl<Button>("HelpBtn") ?? throw new Exception("HelpBtn not found");
            _helpBtnText = this.FindControl<TextBlock>("HelpBtnText") ?? throw new Exception("HelpBtnText not found");
            
            _headerStationText = this.FindControl<TextBlock>("HeaderStationText") ?? throw new Exception("HeaderStationText not found");
            _headerKioskStatusText = this.FindControl<TextBlock>("HeaderKioskStatusText") ?? throw new Exception("HeaderKioskStatusText not found");

            _cardBrandText = this.FindControl<TextBlock>("CardBrandText") ?? throw new Exception("CardBrandText not found");
            _cardSubBrandText = this.FindControl<TextBlock>("CardSubBrandText") ?? throw new Exception("CardSubBrandText not found");
            _idleHeadingText1 = this.FindControl<TextBlock>("IdleHeadingText1") ?? throw new Exception("IdleHeadingText1 not found");
            _idleHeadingText2 = this.FindControl<TextBlock>("IdleHeadingText2") ?? throw new Exception("IdleHeadingText2 not found");
            _idleSubHeadingText = this.FindControl<TextBlock>("IdleSubHeadingText") ?? throw new Exception("IdleSubHeadingText not found");

            _cardInfoLabel = this.FindControl<TextBlock>("CardInfoLabel") ?? throw new Exception("CardInfoLabel not found");
            _cardUidText = this.FindControl<TextBlock>("CardUidText") ?? throw new Exception("CardUidText not found");
            _balanceLabel = this.FindControl<TextBlock>("BalanceLabel") ?? throw new Exception("BalanceLabel not found");
            _currentBalanceText = this.FindControl<TextBlock>("CurrentBalanceText") ?? throw new Exception("CurrentBalanceText not found");

            _selectAmountInstruction = this.FindControl<TextBlock>("SelectAmountInstruction") ?? throw new Exception("SelectAmountInstruction not found");
            _btn20SubText = this.FindControl<TextBlock>("Btn20SubText") ?? throw new Exception("Btn20SubText not found");
            _btn50SubText = this.FindControl<TextBlock>("Btn50SubText") ?? throw new Exception("Btn50SubText not found");
            _btn100SubText = this.FindControl<TextBlock>("Btn100SubText") ?? throw new Exception("Btn100SubText not found");
            _btn200SubText = this.FindControl<TextBlock>("Btn200SubText") ?? throw new Exception("Btn200SubText not found");
            _btnOtherText = this.FindControl<TextBlock>("BtnOtherText") ?? throw new Exception("BtnOtherText not found");
            _btnOtherSubText = this.FindControl<TextBlock>("BtnOtherSubText") ?? throw new Exception("BtnOtherSubText not found");

            _selectedAmountText = this.FindControl<TextBlock>("SelectedAmountText") ?? throw new Exception("SelectedAmountText not found");
            _paymentStatusText = this.FindControl<TextBlock>("PaymentStatusText") ?? throw new Exception("PaymentStatusText not found");
            _paymentLabelDetails = this.FindControl<TextBlock>("PaymentLabelDetails") ?? throw new Exception("PaymentLabelDetails not found");

            _successHeadingText = this.FindControl<TextBlock>("SuccessHeadingText") ?? throw new Exception("SuccessHeadingText not found");
            _finalBalanceText = this.FindControl<TextBlock>("FinalBalanceText") ?? throw new Exception("FinalBalanceText not found");
            _successSubHeadingText1 = this.FindControl<TextBlock>("SuccessSubHeadingText1") ?? throw new Exception("SuccessSubHeadingText1 not found");
            _successSubHeadingText2 = this.FindControl<TextBlock>("SuccessSubHeadingText2") ?? throw new Exception("SuccessSubHeadingText2 not found");

            _footerStatusText = this.FindControl<TextBlock>("FooterStatusText") ?? throw new Exception("FooterStatusText not found");
            _footerNfcLabel = this.FindControl<TextBlock>("FooterNfcLabel") ?? throw new Exception("FooterNfcLabel not found");
            _toggleCardBtn = this.FindControl<Button>("ToggleCardBtn") ?? throw new Exception("ToggleCardBtn not found");

            _numpadTitleText = this.FindControl<TextBlock>("NumpadTitleText") ?? throw new Exception("NumpadTitleText not found");
            _numpadValueText = this.FindControl<TextBlock>("NumpadValueText") ?? throw new Exception("NumpadValueText not found");
            _cancelNumpadBtn = this.FindControl<Button>("CancelNumpadBtn") ?? throw new Exception("CancelNumpadBtn not found");
            _closeHelpBtn = this.FindControl<Button>("CloseHelpBtn") ?? throw new Exception("CloseHelpBtn not found");
            
            _izbanLogoImage = this.FindControl<Image>("IzbanLogoImage") ?? throw new Exception("IzbanLogoImage not found");
            _fallbackLogo = this.FindControl<Grid>("FallbackLogo") ?? throw new Exception("FallbackLogo not found");
            _posLogoImage = this.FindControl<Image>("PosLogoImage") ?? throw new Exception("PosLogoImage not found");
            _posFallbackText = this.FindControl<TextBlock>("PosFallbackText") ?? throw new Exception("PosFallbackText not found");
            _izmirimCardImage = this.FindControl<Image>("IzmirimCardImage") ?? throw new Exception("IzmirimCardImage not found");
            _fallbackCardGraphic = this.FindControl<Grid>("FallbackCardGraphic") ?? throw new Exception("FallbackCardGraphic not found");

            // Try loading logo securely
            try
            {
                var stream = AssetLoader.Open(new Uri("avares://IzbanKioskApp/Assets/izban_logo.png"));
                var bitmap = new Bitmap(stream);
                
                _izbanLogoImage.Source = bitmap;
                _izbanLogoImage.IsVisible = true;
                _fallbackLogo.IsVisible = false;

                _posLogoImage.Source = bitmap;
                _posLogoImage.IsVisible = true;
                _posFallbackText.IsVisible = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGO LOAD WARN] Using fallback concentric vector logo. Details: {ex.Message}");
                _izbanLogoImage.IsVisible = false;
                _fallbackLogo.IsVisible = true;

                _posLogoImage.IsVisible = false;
                _posFallbackText.IsVisible = true;
            }

            // Try loading İzmirim Card image securely
            try
            {
                var cardStream = AssetLoader.Open(new Uri("avares://IzbanKioskApp/Assets/izmirim_kart.png"));
                var cardBitmap = new Bitmap(cardStream);
                _izmirimCardImage.Source = cardBitmap;
                _izmirimCardImage.IsVisible = true;
                _fallbackCardGraphic.IsVisible = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CARD IMAGE LOAD WARN] Using fallback vector card layout. Details: {ex.Message}");
                _izmirimCardImage.IsVisible = false;
                _fallbackCardGraphic.IsVisible = true;
            }

            // Bind Screens Binds
            _idleScreen = this.FindControl<Grid>("IdleScreen") ?? throw new Exception("IdleScreen not found");
            _amountScreen = this.FindControl<Grid>("AmountScreen") ?? throw new Exception("AmountScreen not found");
            _numpadScreen = this.FindControl<Grid>("NumpadScreen") ?? throw new Exception("NumpadScreen not found");
            _paymentScreen = this.FindControl<Grid>("PaymentScreen") ?? throw new Exception("PaymentScreen not found");
            _successScreen = this.FindControl<Grid>("SuccessScreen") ?? throw new Exception("SuccessScreen not found");
            _helpModal = this.FindControl<Grid>("HelpModal") ?? throw new Exception("HelpModal not found");
            _warningModal = this.FindControl<Grid>("WarningModal") ?? throw new Exception("WarningModal not found");

            // Bind Action Grid Buttons
            _btn20 = this.FindControl<Button>("Btn20") ?? throw new Exception("Btn20 not found");
            _btn50 = this.FindControl<Button>("Btn50") ?? throw new Exception("Btn50 not found");
            _btn100 = this.FindControl<Button>("Btn100") ?? throw new Exception("Btn100 not found");
            _btn200 = this.FindControl<Button>("Btn200") ?? throw new Exception("Btn200 not found");
            _btnOther = this.FindControl<Button>("BtnOther") ?? throw new Exception("BtnOther not found");

            // Window Setup Settings
            WindowState = WindowState.Maximized;
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Start Live Clock Ticker
            _clockText.Text = DateTime.Now.ToString("dd.MM.yyyy - HH:mm:ss");
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (s, e) =>
            {
                _clockText.Text = DateTime.Now.ToString("dd.MM.yyyy - HH:mm:ss");
            });
            timer.Start();

            // Set up Event Listeners
            _languageToggleBtn.Click += OnLanguageToggleClick;
            _helpBtn.Click += OnHelpBtnClick;
            _closeHelpBtn.Click += OnCloseHelpBtnClick;
            _toggleCardBtn.Click += OnToggleCardClick;
            
            _btn20.Click += async (s, e) => await StartPaymentFlow(20);
            _btn50.Click += async (s, e) => await StartPaymentFlow(50);
            _btn100.Click += async (s, e) => await StartPaymentFlow(100);
            _btn200.Click += async (s, e) => await StartPaymentFlow(200);
            
            _btnOther.Click += OnBtnOtherClick;
            _cancelNumpadBtn.Click += OnCancelNumpadBtnClick;

            // Load Initial State
            ApplyLanguage();
            ResetKioskToDefault();
        }

        private void ResetKioskToDefault()
        {
            _isCardPresent = false;
            _numpadValue = "0";
            _numpadValueText.Text = "0 TL";
            
            // Sync Footer simulator trigger to green Placing card
            _toggleCardBtn.Content = _currentLang == "TR" ? "📥 KART YAKLAŞTIR" : "📥 PLACE CARD";
            _toggleCardBtn.Background = SolidColorBrush.Parse("#10B981");
            _footerNfcLabel.Text = GetText("FooterNfcNoCard");
            _footerNfcLabel.Foreground = SolidColorBrush.Parse("#EF4444");

            SetCurrentScreen(_idleScreen);
        }

        private void SetCurrentScreen(Grid screen)
        {
            _idleScreen.IsVisible = (_idleScreen == screen);
            _amountScreen.IsVisible = (_amountScreen == screen);
            _numpadScreen.IsVisible = (_numpadScreen == screen);
            _paymentScreen.IsVisible = (_paymentScreen == screen);
            _successScreen.IsVisible = (_successScreen == screen);
        }

        // --- Localization Operations ---
        private string GetText(string key)
        {
            if (LangDict.TryGetValue(_currentLang, out var section) && section.TryGetValue(key, out var val))
            {
                return val;
            }
            return key;
        }

        private void ApplyLanguage()
        {
            // Toggle Button Context
            _languageToggleText.Text = _currentLang == "TR" ? "🌍 EN" : "🌍 TR";

            // Headers
            _headerStationText.Text = GetText("HeaderStation");
            _headerKioskStatusText.Text = GetText("HeaderKioskStatus");
            _helpBtnText.Text = GetText("HelpBtnText");

            // Idle Screen
            _cardBrandText.Text = GetText("CardBrand");
            _cardSubBrandText.Text = GetText("CardSubBrand");
            _idleHeadingText1.Text = GetText("IdleHeading1");
            _idleHeadingText2.Text = GetText("IdleHeading2");
            _idleSubHeadingText.Text = GetText("IdleSubHeading");

            // Amount Select Screen
            _cardInfoLabel.Text = GetText("CardInfoLabel");
            _balanceLabel.Text = GetText("BalanceLabel");
            _selectAmountInstruction.Text = GetText("SelectAmountInstruction");
            _btn20SubText.Text = GetText("BtnSubText");
            _btn50SubText.Text = GetText("BtnSubText");
            _btn100SubText.Text = GetText("BtnSubText");
            _btn200SubText.Text = GetText("BtnSubText");
            _btnOtherText.Text = GetText("BtnOtherText");
            _btnOtherSubText.Text = GetText("BtnOtherSubText");

            // Keypad Screen
            _numpadTitleText.Text = GetText("NumpadTitle");
            _cancelNumpadBtn.Content = GetText("Cancel");

            // Payment Screen
            _selectedAmountText.Text = $"{GetText("SelectedAmount")}: 0 TL";
            _paymentStatusText.Text = GetText("PaymentStatus");
            _paymentLabelDetails.Text = GetText("PaymentLabelDetails");

            // Success Screen
            _successHeadingText.Text = GetText("SuccessHeading");
            _successSubHeadingText1.Text = GetText("SuccessSub1");
            _successSubHeadingText2.Text = GetText("SuccessSub2");

            // Footer
            _footerStatusText.Text = GetText("FooterStatus");
            
            // NFC Card indicator text
            if (_isCardPresent)
            {
                _toggleCardBtn.Content = _currentLang == "TR" ? "📤 KARTI ÇEK" : "📤 REMOVE CARD";
                _footerNfcLabel.Text = GetText("FooterNfcCard");
            }
            else
            {
                _toggleCardBtn.Content = _currentLang == "TR" ? "📥 KART YAKLAŞTIR" : "📥 PLACE CARD";
                _footerNfcLabel.Text = GetText("FooterNfcNoCard");
            }

            // Modals
            var helpModalTitle = this.FindControl<TextBlock>("HelpModalTitle");
            if (helpModalTitle != null) helpModalTitle.Text = GetText("HelpTitle");
            var helpBodyText1 = this.FindControl<TextBlock>("HelpBodyText1");
            if (helpBodyText1 != null) helpBodyText1.Text = GetText("Help1");
            var helpBodyText2 = this.FindControl<TextBlock>("HelpBodyText2");
            if (helpBodyText2 != null) helpBodyText2.Text = GetText("Help2");
            var helpBodyText3 = this.FindControl<TextBlock>("HelpBodyText3");
            if (helpBodyText3 != null) helpBodyText3.Text = GetText("Help3");
            var helpBodyText4 = this.FindControl<TextBlock>("HelpBodyText4");
            if (helpBodyText4 != null) helpBodyText4.Text = GetText("Help4");
            var closeHelpBtnText = this.FindControl<Button>("CloseHelpBtn");
            if (closeHelpBtnText != null) closeHelpBtnText.Content = GetText("HelpClose");
            
            var warnTitleText = this.FindControl<TextBlock>("WarnTitleText");
            var warnBodyText = this.FindControl<TextBlock>("WarnBodyText");
            if (warnTitleText != null) warnTitleText.Text = _currentLang == "TR" ? "UYARI: KART ERKEN ÇEKİLDİ!" : "WARNING: CARD REMOVED EARLY!";
            if (warnBodyText != null) warnBodyText.Text = _currentLang == "TR" ? "İşlem iptal edilmiştir. Kartınızdan ücret alınmadı." : "Transaction cancelled. No charges were made to your account.";
        }

        private void OnLanguageToggleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _currentLang = _currentLang == "TR" ? "EN" : "TR";
            ApplyLanguage();
        }

        // --- Modals Triggers ---
        private void OnHelpBtnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _helpModal.IsVisible = true;
        }

        private void OnCloseHelpBtnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _helpModal.IsVisible = false;
        }

        // --- NFC Card Presence Operations ---
        private async void OnToggleCardClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!_isCardPresent)
            {
                // Put Card
                _isCardPresent = true;
                try { Console.Beep(1200, 150); } catch { }

                _toggleCardBtn.Content = _currentLang == "TR" ? "📤 KARTI ÇEK" : "📤 REMOVE CARD";
                _toggleCardBtn.Background = SolidColorBrush.Parse("#EF4444");
                _footerNfcLabel.Text = GetText("FooterNfcCard");
                _footerNfcLabel.Foreground = SolidColorBrush.Parse("#10B981");

                try
                {
                    // Asenkron olarak NFC kart okuma servisini çağır
                    var (cardUid, balance) = await AppServices.NfcReader.ReadCardAsync();
                    _cardUid = cardUid;
                    _currentBalance = balance;

                    _cardUidText.Text = $"Card UID: {DatabaseService.MaskCardUid(_cardUid)}";
                    _currentBalanceText.Text = $"{_currentBalance:F2} TL";
                    SetCurrentScreen(_amountScreen);
                }
                catch (Exception ex)
                {
                    _paymentStatusText.Text = _currentLang == "TR" ? "Kart Okuma Hatası!" : "Card Reading Error!";
                    Console.WriteLine($"[NFC READ ERROR] {ex.Message}");
                }
            }
            else
            {
                // Pulled card
                _isCardPresent = false;
                
                _toggleCardBtn.Content = _currentLang == "TR" ? "📥 KART YAKLAŞTIR" : "📥 PLACE CARD";
                _toggleCardBtn.Background = SolidColorBrush.Parse("#10B981");
                _footerNfcLabel.Text = GetText("FooterNfcNoCard");
                _footerNfcLabel.Foreground = SolidColorBrush.Parse("#EF4444");

                EvaluateCardRemoval();
            }
        }

        private void EvaluateCardRemoval()
        {
            // If card is removed during ongoing selection or POS waiting
            if (_amountScreen.IsVisible || _numpadScreen.IsVisible || _paymentScreen.IsVisible)
            {
                // Erken Kart Çekme - Cancel immediately!
                _paymentCts?.Cancel();
                TriggerEarlyRemovalWarning();
            }
            else if (_successScreen.IsVisible)
            {
                // Normal complete route, user removed card after success.
                // Cancel pending waiting task immediately and reset.
                _paymentCts?.Cancel();
                ResetKioskToDefault();
            }
        }

        private void TriggerEarlyRemovalWarning()
        {
            if (_isWarningModalActive) return;
            _isWarningModalActive = true;

            try { Console.Beep(800, 500); } catch { }
            
            _warningModal.IsVisible = true;
            
            // Lock and reset
            _numpadValue = "0";
            _numpadValueText.Text = "0 TL";

            var resetTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(2500), DispatcherPriority.Normal, (s, e) =>
            {
                _warningModal.IsVisible = false;
                _isWarningModalActive = false;
                ResetKioskToDefault();
                ((DispatcherTimer)s!).Stop();
            });
            resetTimer.Start();
        }

        // --- Other amount button ---
        private void OnBtnOtherClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _numpadValue = "0";
            _numpadValueText.Text = "0 TL";
            SetCurrentScreen(_numpadScreen);
        }

        private void OnCancelNumpadBtnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            SetCurrentScreen(_amountScreen);
        }

        // --- Keypad Buttons Events called by XAML ---
        public void OnNumpadKeyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string key)
            {
                if (_numpadValue == "0")
                {
                    _numpadValue = key;
                }
                else
                {
                    string nextVal = _numpadValue + key;
                    // Clamp to maximum 500 TL limit
                    if (int.TryParse(nextVal, out int val) && val <= 500)
                    {
                        _numpadValue = nextVal;
                    }
                }
                _numpadValueText.Text = $"{_numpadValue} TL";
            }
        }

        public void OnNumpadDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_numpadValue.Length > 1)
            {
                _numpadValue = _numpadValue.Substring(0, _numpadValue.Length - 1);
            }
            else
            {
                _numpadValue = "0";
            }
            _numpadValueText.Text = $"{_numpadValue} TL";
        }

        public void OnNumpadConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (decimal.TryParse(_numpadValue, out decimal val) && val > 0)
            {
                _ = StartPaymentFlow(val);
            }
        }

        // --- Payment State Logic ---
        private async Task StartPaymentFlow(decimal amount)
        {
            _paymentCts = new CancellationTokenSource();
            var token = _paymentCts.Token;

            _selectedAmountText.Text = $"{GetText("SelectedAmount")}: {amount:F2} TL";
            _paymentStatusText.Text = GetText("PaymentStatus");
            _paymentStatusText.Foreground = SolidColorBrush.Parse("#EF4444");

            SetCurrentScreen(_paymentScreen);

            try
            {
                // Banka POS Cihazı ile Ödeme Alma İşlemi (Asenkron Servis Çağrısı)
                var (paySuccess, approvalCode, errMsg) = await AppServices.PosTerminal.ProcessPaymentAsync(amount);
                
                if (token.IsCancellationRequested) return;

                if (!paySuccess)
                {
                    await DatabaseService.LogTransactionAsync(_cardUid, amount, "FAILED_PROV", "FAILED");
                    throw new Exception(errMsg ?? (string)(_currentLang == "TR" ? "Banka POS ödemeyi reddetti." : "Bank POS declined the card payment."));
                }

                // Beep confirmation sound
                try { Console.Beep(1800, 250); } catch { }

                // Double check if card was removed in exact microsecond
                if (!_isCardPresent)
                {
                    TriggerEarlyRemovalWarning();
                    return;
                }

                _paymentStatusText.Text = GetText("WritingCardStatus");
                _paymentStatusText.Foreground = SolidColorBrush.Parse("#0F172A");

                // Asenkron olarak karta yeni bakiyeyi yaz (NFC Sektör Güncellemesi)
                bool writeSuccess = await AppServices.NfcReader.WriteBalanceAsync(_cardUid, _currentBalance + amount);
                
                if (token.IsCancellationRequested) return;

                if (!writeSuccess)
                {
                    throw new Exception((string)(_currentLang == "TR" ? "İzmirim Kart'a bakiye yazılamadı." : "Failed to write balance to Izmirim Kart."));
                }

                if (!_isCardPresent)
                {
                    TriggerEarlyRemovalWarning();
                    return;
                }

                // Log SQLite transaction asynchronously
                await DatabaseService.LogTransactionAsync(_cardUid, amount, approvalCode, "SUCCESS");

                _currentBalance += amount;

                // Move visual success
                _finalBalanceText.Text = $"{GetText("FinalBalance")}: {_currentBalance:F2} TL";
                SetCurrentScreen(_successScreen);
                
                // Do not auto-reset in 4 seconds unconditionally. Let the user pull the card.
                // In case they leave the kiosk, we can auto reset after 15 seconds.
                var timeoutTask = Task.Delay(15000, token);
                
                // We will wait for either card removal OR timeout!
                while (_isCardPresent && !token.IsCancellationRequested)
                {
                    if (timeoutTask.IsCompleted)
                    {
                        break;
                    }
                    await Task.Delay(100, token);
                }

                ResetKioskToDefault();
            }
            catch (TaskCanceledException)
            {
                // cleanly cancelled, nothing to do
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PAYMENT LOGIC FAIL...] {ex.Message}");
                _paymentStatusText.Text = _currentLang == "TR" 
                    ? $"❌ HATA: {ex.Message}" 
                    : $"❌ ERROR: {ex.Message}";
                _paymentStatusText.Foreground = SolidColorBrush.Parse("#EF4444");
                
                try
                {
                    await Task.Delay(4000, token);
                }
                catch { }
                ResetKioskToDefault();
            }
            finally
            {
                _paymentCts?.Dispose();
                _paymentCts = null;
            }
        }
    }
}