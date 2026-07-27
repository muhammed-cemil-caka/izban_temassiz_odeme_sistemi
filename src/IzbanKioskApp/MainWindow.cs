using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Threading.Tasks;
using IzbanKioskApp.Core;

namespace IzbanKioskApp
{
    public class MainWindow : Window
    {
        private TextBlock _statusText;
        private StackPanel _amountPanel;
        private Button _readCardBtn;
        private decimal _currentBalance = 45.50m;
        private string _cardUid = "35-IZM-9921";

        public MainWindow()
        {
            Title = "İZBAN KIOSK BAKIYE YUKLEME";
            Width = 600;
            Height = 800;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brushes.Black;

            // Ana Dikey Düzenleyici
            var mainStack = new StackPanel
            {
                Margin = new Avalonia.Thickness(30),
                Spacing = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Başlık
            mainStack.Children.Add(new TextBlock
            {
                Text = "İZBAN KİOSK",
                FontSize = 36,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.LimeGreen,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            // Durum ve Mesaj Ekranı
            _statusText = new TextBlock
            {
                Text = "LÜTFEN İZMİRİM KARTINIZI OKUTUNUZ",
                FontSize = 20,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Avalonia.Thickness(0, 20)
            };
            mainStack.Children.Add(_statusText);

            // Simülasyon Kart Okut Butonu
            _readCardBtn = new Button
            {
                Content = "💳 KARTI OKUT (NFC SIMULATOR)",
                FontSize = 18,
                Padding = new Avalonia.Thickness(20, 15),
                Background = Brushes.DarkGreen,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _readCardBtn.Click += OnCardReadClick;
            mainStack.Children.Add(_readCardBtn);

            // Tutar Seçim Butonları Paneli (Başlangıçta Gizli)
            _amountPanel = new StackPanel { Spacing = 15, IsVisible = false };
            
            int[] amounts = { 20, 50, 100, 200 };
            foreach (var amount in amounts)
            {
                var btn = new Button
                {
                    Content = $"{amount} TL YÜKLE",
                    FontSize = 22,
                    Width = 300,
                    Height = 60,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = Brushes.DodgerBlue,
                    Foreground = Brushes.White
                };
                int selected = amount;
                btn.Click += async (s, e) => await ProcessPayment(selected);
                _amountPanel.Children.Add(btn);
            }
            mainStack.Children.Add(_amountPanel);

            Content = mainStack;
        }

        private void OnCardReadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _readCardBtn.IsVisible = false;
            _statusText.Text = $"Mevcut Bakiye: {_currentBalance:F2} TL\nLütfen yüklemek istediğiniz tutara dokununuz:";
            _amountPanel.IsVisible = true;
        }

        private async Task ProcessPayment(decimal amount)
        {
            _amountPanel.IsVisible = false;
            _statusText.Text = $"Seçilen Tutar: {amount:F2} TL\nLÜTFEN KREDİ KARTINIZI POS CİHAZINA YAKLAŞTIRINIZ...";

            await Task.Delay(2500); // POS simülasyon beklemesi

            _statusText.Text = "Ödeme Alındı! Bakiye Kartınıza Yazılıyor...";
            await Task.Delay(1500);

            string approvalCode = "PROV_" + Random.Shared.Next(100000, 999999);
            DatabaseService.LogTransaction(_cardUid, amount, approvalCode, "SUCCESS");

            _currentBalance += amount;
            _statusText.Text = $"YÜKLEME BAŞARILI!\nYeni Bakiyeniz: {_currentBalance:F2} TL";
            _statusText.Foreground = Brushes.LimeGreen;

            await Task.Delay(4000); // 4 sn sonra başa dön

            // Reseti Çalıştır
            _statusText.Foreground = Brushes.White;
            _statusText.Text = "LÜTFEN İZMİRİM KARTINIZI OKUTUNUZ";
            _readCardBtn.IsVisible = true;
        }
    }
}