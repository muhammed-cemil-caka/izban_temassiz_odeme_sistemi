using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.ComponentModel;
using Avalonia.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using IzbanKioskApp.ViewModels;

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

        private StackPanel _receiptPromptPanel = null!;
        private TextBlock _receiptPromptTitle = null!;
        private Button _receiptYesBtn = null!;
        private Button _receiptNoBtn = null!;
        private TextBlock _receiptStatusLabel = null!;
        private Border _printerWarningPanel = null!;
        private TextBlock _printerWarningText = null!;
        
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

        public MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        public MainWindow()
        {
            InitializeComponent();
        }

        public MainWindow(MainWindowViewModel viewModel) : this()
        {
            DataContext = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            
            // Initial sync of properties
            SyncUIFromViewModel();

            // Start clock timer to tick ViewModel
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (s, e) =>
            {
                viewModel.UpdateClock();
            });
            timer.Start();
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

            _receiptPromptPanel = this.FindControl<StackPanel>("ReceiptPromptPanel") ?? throw new Exception("ReceiptPromptPanel not found");
            _receiptPromptTitle = this.FindControl<TextBlock>("ReceiptPromptTitle") ?? throw new Exception("ReceiptPromptTitle not found");
            _receiptYesBtn = this.FindControl<Button>("ReceiptYesBtn") ?? throw new Exception("ReceiptYesBtn not found");
            _receiptNoBtn = this.FindControl<Button>("ReceiptNoBtn") ?? throw new Exception("ReceiptNoBtn not found");
            _receiptStatusLabel = this.FindControl<TextBlock>("ReceiptStatusLabel") ?? throw new Exception("ReceiptStatusLabel not found");
            _printerWarningPanel = this.FindControl<Border>("PrinterWarningPanel") ?? throw new Exception("PrinterWarningPanel not found");
            _printerWarningText = this.FindControl<TextBlock>("PrinterWarningText") ?? throw new Exception("PrinterWarningText not found");

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

            // Load static assets
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
                Console.WriteLine($"[LOGO LOAD WARN] Fallback concentric vector logo. Details: {ex.Message}");
                _izbanLogoImage.IsVisible = false;
                _fallbackLogo.IsVisible = true;
                _posLogoImage.IsVisible = false;
                _posFallbackText.IsVisible = true;
            }

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
                Console.WriteLine($"[CARD IMAGE LOAD WARN] Fallback vector card. Details: {ex.Message}");
                _izmirimCardImage.IsVisible = false;
                _fallbackCardGraphic.IsVisible = true;
            }

            // Bind Screens
            _idleScreen = this.FindControl<Grid>("IdleScreen") ?? throw new Exception("IdleScreen not found");
            _amountScreen = this.FindControl<Grid>("AmountScreen") ?? throw new Exception("AmountScreen not found");
            _numpadScreen = this.FindControl<Grid>("NumpadScreen") ?? throw new Exception("NumpadScreen not found");
            _paymentScreen = this.FindControl<Grid>("PaymentScreen") ?? throw new Exception("PaymentScreen not found");
            _successScreen = this.FindControl<Grid>("SuccessScreen") ?? throw new Exception("SuccessScreen not found");
            _helpModal = this.FindControl<Grid>("HelpModal") ?? throw new Exception("HelpModal not found");
            _warningModal = this.FindControl<Grid>("WarningModal") ?? throw new Exception("WarningModal not found");

            // Bind Buttons
            _btn20 = this.FindControl<Button>("Btn20") ?? throw new Exception("Btn20 not found");
            _btn50 = this.FindControl<Button>("Btn50") ?? throw new Exception("Btn50 not found");
            _btn100 = this.FindControl<Button>("Btn100") ?? throw new Exception("Btn100 not found");
            _btn200 = this.FindControl<Button>("Btn200") ?? throw new Exception("Btn200 not found");
            _btnOther = this.FindControl<Button>("BtnOther") ?? throw new Exception("BtnOther not found");

            // Window Setup Settings
            WindowState = WindowState.Maximized;
            SystemDecorations = SystemDecorations.None;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Forward Clicks to ViewModel
            _languageToggleBtn.Click += (s, e) => ViewModel.ToggleLanguage();
            _helpBtn.Click += (s, e) => ViewModel.ToggleHelp();
            _closeHelpBtn.Click += (s, e) => ViewModel.CloseHelp();
            _toggleCardBtn.Click += async (s, e) => await ViewModel.ToggleSimulatedCardAsync();
            
            _btn20.Click += async (s, e) => await ViewModel.SelectAmountAsync(20);
            _btn50.Click += async (s, e) => await ViewModel.SelectAmountAsync(50);
            _btn100.Click += async (s, e) => await ViewModel.SelectAmountAsync(100);
            _btn200.Click += async (s, e) => await ViewModel.SelectAmountAsync(200);
            
            _btnOther.Click += (s, e) => ViewModel.SelectOtherAmount();
            _cancelNumpadBtn.Click += (s, e) => ViewModel.CancelNumpad();

            _receiptYesBtn.Click += async (s, e) => await ViewModel.RequestReceiptAsync();
            _receiptNoBtn.Click += async (s, e) => await ViewModel.DeclineReceiptAsync();
        }

        // --- Keyboard Events Forwarding ---
        public void OnNumpadKeyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content is string key)
            {
                ViewModel.ProcessNumpadKey(key);
            }
        }

        public void OnNumpadDeleteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ViewModel.DeleteNumpadChar();
        }

        public async void OnNumpadConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            await ViewModel.ConfirmNumpadAsync();
        }

        private void SyncUIFromViewModel()
        {
            if (DataContext == null) return;
            
            _clockText.Text = ViewModel.ClockText;
            _headerStationText.Text = ViewModel.StationText;
            _headerKioskStatusText.Text = ViewModel.KioskStatusText;
            _languageToggleText.Text = ViewModel.LanguageToggleText;
            _helpBtnText.Text = ViewModel.HelpBtnText;

            _cardBrandText.Text = ViewModel.CardBrandText;
            _cardSubBrandText.Text = ViewModel.CardSubBrandText;
            _idleHeadingText1.Text = ViewModel.IdleHeadingText1;
            _idleHeadingText2.Text = ViewModel.IdleHeadingText2;
            _idleSubHeadingText.Text = ViewModel.IdleSubHeadingText;

            _cardInfoLabel.Text = ViewModel.CardInfoLabel;
            _cardUidText.Text = ViewModel.CardUidText;
            _balanceLabel.Text = ViewModel.BalanceLabel;
            _currentBalanceText.Text = ViewModel.CurrentBalanceText;

            _selectAmountInstruction.Text = ViewModel.SelectAmountInstruction;
            _btn20SubText.Text = ViewModel.BtnSubText;
            _btn50SubText.Text = ViewModel.BtnSubText;
            _btn100SubText.Text = ViewModel.BtnSubText;
            _btn200SubText.Text = ViewModel.BtnSubText;
            _btnOtherText.Text = ViewModel.BtnOtherText;
            _btnOtherSubText.Text = ViewModel.BtnOtherSubText;

            _numpadTitleText.Text = ViewModel.NumpadTitleText;
            _numpadValueText.Text = ViewModel.NumpadValueText;
            _cancelNumpadBtn.Content = ViewModel.CancelNumpadBtnText;

            _selectedAmountText.Text = ViewModel.SelectedAmountText;
            _paymentStatusText.Text = ViewModel.PaymentStatusText;
            _paymentStatusText.Foreground = SolidColorBrush.Parse(ViewModel.PaymentStatusColor);
            _paymentLabelDetails.Text = ViewModel.PaymentLabelDetails;

            _successHeadingText.Text = ViewModel.SuccessHeadingText;
            _finalBalanceText.Text = ViewModel.FinalBalanceText;
            _successSubHeadingText1.Text = ViewModel.SuccessSubHeadingText1;
            _successSubHeadingText2.Text = ViewModel.SuccessSubHeadingText2;

            _receiptPromptPanel.IsVisible = ViewModel.IsReceiptPromptVisible;
            _receiptPromptTitle.Text = ViewModel.ReceiptPromptText;
            _receiptYesBtn.Content = ViewModel.ReceiptYesButtonText;
            _receiptYesBtn.IsEnabled = ViewModel.IsReceiptYesEnabled;
            _receiptNoBtn.Content = ViewModel.ReceiptNoButtonText;
            _receiptNoBtn.IsEnabled = !ViewModel.IsReceiptPrinting;
            _receiptStatusLabel.Text = ViewModel.ReceiptStatusText;
            _printerWarningPanel.IsVisible = ViewModel.IsPrinterWarningVisible;
            _printerWarningText.Text = ViewModel.PrinterWarningText;

            _footerStatusText.Text = ViewModel.FooterStatusText;
            _footerNfcLabel.Text = ViewModel.FooterNfcLabelText;
            _footerNfcLabel.Foreground = SolidColorBrush.Parse(ViewModel.FooterNfcLabelColor);

            _toggleCardBtn.Content = ViewModel.ToggleCardBtnText;
            _toggleCardBtn.Background = SolidColorBrush.Parse(ViewModel.ToggleCardBtnColor);

            _idleScreen.IsVisible = ViewModel.IsIdleScreenVisible;
            _amountScreen.IsVisible = ViewModel.IsAmountScreenVisible;
            _numpadScreen.IsVisible = ViewModel.IsNumpadScreenVisible;
            _paymentScreen.IsVisible = ViewModel.IsPaymentScreenVisible;
            _successScreen.IsVisible = ViewModel.IsSuccessScreenVisible;
            _helpModal.IsVisible = ViewModel.IsHelpModalVisible;
            _warningModal.IsVisible = ViewModel.IsWarningModalVisible;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(ViewModel.ClockText):
                        _clockText.Text = ViewModel.ClockText;
                        break;
                    case nameof(ViewModel.StationText):
                        _headerStationText.Text = ViewModel.StationText;
                        break;
                    case nameof(ViewModel.KioskStatusText):
                        _headerKioskStatusText.Text = ViewModel.KioskStatusText;
                        break;
                    case nameof(ViewModel.LanguageToggleText):
                        _languageToggleText.Text = ViewModel.LanguageToggleText;
                        break;
                    case nameof(ViewModel.HelpBtnText):
                        _helpBtnText.Text = ViewModel.HelpBtnText;
                        break;
                    case nameof(ViewModel.CardBrandText):
                        _cardBrandText.Text = ViewModel.CardBrandText;
                        break;
                    case nameof(ViewModel.CardSubBrandText):
                        _cardSubBrandText.Text = ViewModel.CardSubBrandText;
                        break;
                    case nameof(ViewModel.IdleHeadingText1):
                        _idleHeadingText1.Text = ViewModel.IdleHeadingText1;
                        break;
                    case nameof(ViewModel.IdleHeadingText2):
                        _idleHeadingText2.Text = ViewModel.IdleHeadingText2;
                        break;
                    case nameof(ViewModel.IdleSubHeadingText):
                        _idleSubHeadingText.Text = ViewModel.IdleSubHeadingText;
                        break;
                    case nameof(ViewModel.CardInfoLabel):
                        _cardInfoLabel.Text = ViewModel.CardInfoLabel;
                        break;
                    case nameof(ViewModel.CardUidText):
                        _cardUidText.Text = ViewModel.CardUidText;
                        break;
                    case nameof(ViewModel.BalanceLabel):
                        _balanceLabel.Text = ViewModel.BalanceLabel;
                        break;
                    case nameof(ViewModel.CurrentBalanceText):
                        _currentBalanceText.Text = ViewModel.CurrentBalanceText;
                        break;
                    case nameof(ViewModel.SelectAmountInstruction):
                        _selectAmountInstruction.Text = ViewModel.SelectAmountInstruction;
                        break;
                    case nameof(ViewModel.BtnSubText):
                        _btn20SubText.Text = ViewModel.BtnSubText;
                        _btn50SubText.Text = ViewModel.BtnSubText;
                        _btn100SubText.Text = ViewModel.BtnSubText;
                        _btn200SubText.Text = ViewModel.BtnSubText;
                        break;
                    case nameof(ViewModel.BtnOtherText):
                        _btnOtherText.Text = ViewModel.BtnOtherText;
                        break;
                    case nameof(ViewModel.BtnOtherSubText):
                        _btnOtherSubText.Text = ViewModel.BtnOtherSubText;
                        break;
                    case nameof(ViewModel.NumpadTitleText):
                        _numpadTitleText.Text = ViewModel.NumpadTitleText;
                        break;
                    case nameof(ViewModel.NumpadValueText):
                        _numpadValueText.Text = ViewModel.NumpadValueText;
                        break;
                    case nameof(ViewModel.CancelNumpadBtnText):
                        _cancelNumpadBtn.Content = ViewModel.CancelNumpadBtnText;
                        break;
                    case nameof(ViewModel.SelectedAmountText):
                        _selectedAmountText.Text = ViewModel.SelectedAmountText;
                        break;
                    case nameof(ViewModel.PaymentStatusText):
                        _paymentStatusText.Text = ViewModel.PaymentStatusText;
                        break;
                    case nameof(ViewModel.PaymentStatusColor):
                        _paymentStatusText.Foreground = SolidColorBrush.Parse(ViewModel.PaymentStatusColor);
                        break;
                    case nameof(ViewModel.PaymentLabelDetails):
                        _paymentLabelDetails.Text = ViewModel.PaymentLabelDetails;
                        break;
                    case nameof(ViewModel.SuccessHeadingText):
                        _successHeadingText.Text = ViewModel.SuccessHeadingText;
                        break;
                    case nameof(ViewModel.FinalBalanceText):
                        _finalBalanceText.Text = ViewModel.FinalBalanceText;
                        break;
                    case nameof(ViewModel.SuccessSubHeadingText1):
                        _successSubHeadingText1.Text = ViewModel.SuccessSubHeadingText1;
                        break;
                    case nameof(ViewModel.SuccessSubHeadingText2):
                        _successSubHeadingText2.Text = ViewModel.SuccessSubHeadingText2;
                        break;
                    case nameof(ViewModel.IsReceiptPromptVisible):
                        _receiptPromptPanel.IsVisible = ViewModel.IsReceiptPromptVisible;
                        break;
                    case nameof(ViewModel.ReceiptPromptText):
                        _receiptPromptTitle.Text = ViewModel.ReceiptPromptText;
                        break;
                    case nameof(ViewModel.ReceiptYesButtonText):
                        _receiptYesBtn.Content = ViewModel.ReceiptYesButtonText;
                        break;
                    case nameof(ViewModel.IsReceiptYesEnabled):
                        _receiptYesBtn.IsEnabled = ViewModel.IsReceiptYesEnabled;
                        break;
                    case nameof(ViewModel.ReceiptNoButtonText):
                        _receiptNoBtn.Content = ViewModel.ReceiptNoButtonText;
                        break;
                    case nameof(ViewModel.IsReceiptPrinting):
                        _receiptNoBtn.IsEnabled = !ViewModel.IsReceiptPrinting;
                        break;
                    case nameof(ViewModel.ReceiptStatusText):
                        _receiptStatusLabel.Text = ViewModel.ReceiptStatusText;
                        break;
                    case nameof(ViewModel.IsPrinterWarningVisible):
                        _printerWarningPanel.IsVisible = ViewModel.IsPrinterWarningVisible;
                        break;
                    case nameof(ViewModel.PrinterWarningText):
                        _printerWarningText.Text = ViewModel.PrinterWarningText;
                        break;
                    case nameof(ViewModel.FooterStatusText):
                        _footerStatusText.Text = ViewModel.FooterStatusText;
                        break;
                    case nameof(ViewModel.FooterNfcLabelText):
                        _footerNfcLabel.Text = ViewModel.FooterNfcLabelText;
                        break;
                    case nameof(ViewModel.FooterNfcLabelColor):
                        _footerNfcLabel.Foreground = SolidColorBrush.Parse(ViewModel.FooterNfcLabelColor);
                        break;
                    case nameof(ViewModel.ToggleCardBtnText):
                        _toggleCardBtn.Content = ViewModel.ToggleCardBtnText;
                        break;
                    case nameof(ViewModel.ToggleCardBtnColor):
                        _toggleCardBtn.Background = SolidColorBrush.Parse(ViewModel.ToggleCardBtnColor);
                        break;
                    case nameof(ViewModel.IsIdleScreenVisible):
                        _idleScreen.IsVisible = ViewModel.IsIdleScreenVisible;
                        break;
                    case nameof(ViewModel.IsAmountScreenVisible):
                        _amountScreen.IsVisible = ViewModel.IsAmountScreenVisible;
                        break;
                    case nameof(ViewModel.IsNumpadScreenVisible):
                        _numpadScreen.IsVisible = ViewModel.IsNumpadScreenVisible;
                        break;
                    case nameof(ViewModel.IsPaymentScreenVisible):
                        _paymentScreen.IsVisible = ViewModel.IsPaymentScreenVisible;
                        break;
                    case nameof(ViewModel.IsSuccessScreenVisible):
                        _successScreen.IsVisible = ViewModel.IsSuccessScreenVisible;
                        break;
                    case nameof(ViewModel.IsHelpModalVisible):
                        _helpModal.IsVisible = ViewModel.IsHelpModalVisible;
                        break;
                    case nameof(ViewModel.IsWarningModalVisible):
                        _warningModal.IsVisible = ViewModel.IsWarningModalVisible;
                        if (ViewModel.IsWarningModalVisible)
                        {
                            try 
                            { 
                                if (OperatingSystem.IsWindows())
                                {
                                    Console.Beep(800, 500); 
                                }
                            } 
                            catch { }
                        }
                        break;
                }
            });
        }
    }
}