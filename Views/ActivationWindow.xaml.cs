using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using Ouro.Services.Licensing;

namespace Ouro.Views
{
    public partial class ActivationWindow : Window
    {
        private readonly LicenseManager _licenseManager;
        private bool _isUnlocked;

        public ActivationWindow(LicenseManager licenseManager)
        {
            InitializeComponent();
            _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));

            TxtHwid.Text = HwidService.GetCurrentHwid();
        }

        private void OnWindowDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void OnCopyHwidClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtHwid.Text);
                TxtCopyIcon.Text = "✔ ";
                TxtCopyLabel.Text = "Copiado!";
                BtnCopyHwid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E2A24"));

                await Task.Delay(2000);
                TxtCopyIcon.Text = "📋 ";
                TxtCopyLabel.Text = "Copiar ID";
                BtnCopyHwid.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E2330"));
            }
            catch { }
        }

        private void OnOpenDiscordClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://discord.gg/kB4uTYBRqD")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError($"No se pudo abrir el navegador: {ex.Message}");
            }
        }

        private void OnKeyTextChanged(object sender, TextChangedEventArgs e)
        {
            TxtStatus.Visibility = Visibility.Collapsed;
        }

        private void OnBrowseLicenseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Selecciona tu licencia de Ouro",
                Filter = "Licencia de Ouro (*.ourolic;*.json;*.txt)|*.ourolic;*.json;*.txt|Todos los archivos (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                TxtLicenseKey.Text = File.ReadAllText(dialog.FileName).Trim();
                TxtStatus.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShowError($"No se pudo leer el archivo: {ex.Message}");
            }
        }

        private async void OnValidateClick(object sender, RoutedEventArgs e)
        {
            if (_isUnlocked) return;

            string licenseText = TxtLicenseKey.Text.Trim();
            if (string.IsNullOrEmpty(licenseText))
            {
                ShowError("Por favor pega tu licencia o carga el archivo .ourolic.");
                return;
            }

            BtnValidate.IsEnabled = false;

            var (isValid, message, license) = _licenseManager.ValidateAndActivateLicense(licenseText);
            if (!isValid || license == null)
            {
                BtnValidate.IsEnabled = true;
                ShowError(message);
                return;
            }

            _isUnlocked = true;

            // Trigger the Cinematic Decrypt & Unlock Sequence
            await PlayCinematicUnlockSequenceAsync(license);

            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            TxtStatus.Text = message;
            TxtStatus.Visibility = Visibility.Visible;

            // Subtle shake animation on input border
            var da = new DoubleAnimation
            {
                From = -6,
                To = 6,
                Duration = TimeSpan.FromMilliseconds(50),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3)
            };
            TxtLicenseKey.BeginAnimation(Canvas.LeftProperty, da);
        }

        private async Task PlayCinematicUnlockSequenceAsync(LicenseModel license)
        {
            // 1. Hide input, reveal Decrypt Overlay
            InputPanel.Visibility = Visibility.Collapsed;
            DecryptOverlay.Visibility = Visibility.Visible;

            // Update Badge Text (Annual 1-year license)
            TxtBadgeTitle.Text = "✦ LICENCIA ANUAL ACTIVA ✦";
            TxtBadgeSubtitle.Text = license.ExpiresAt.HasValue
                ? $"// VÁLIDA HASTA {license.ExpiresAt:dd/MM/yyyy}"
                : "// MOTOR DE REPETICIÓN EN RAM DESBLOQUEADO";

            // Play audio confirmation chime
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/clip_chime.wav");
                var streamResource = Application.GetResourceStream(uri);
                if (streamResource?.Stream != null)
                {
                    using var player = new SoundPlayer(streamResource.Stream);
                    player.Play();
                }
            }
            catch
            {
                SystemSounds.Asterisk.Play();
            }

            // Phase 1: Laser Scan Sweep (Top to Bottom)
            var laserAnim = new DoubleAnimation
            {
                From = -20,
                To = 320,
                Duration = TimeSpan.FromMilliseconds(550),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            LaserBeam.BeginAnimation(Canvas.TopProperty, laserAnim);

            // Terminal Telemetry Lines (staggered reveal)
            AnimateOpacity(TxtTerminal1, 0, 1, 100);
            await Task.Delay(140);
            AnimateOpacity(TxtTerminal2, 0, 1, 100);
            await Task.Delay(140);
            AnimateOpacity(TxtTerminal3, 0, 1, 100);
            await Task.Delay(180);
            AnimateOpacity(TxtTerminal4, 0, 1, 150);

            await Task.Delay(350);

            // Phase 2: Fade terminal text, reveal Exclusivity Badge with scale surge
            AnimateOpacity(TerminalTextPanel, 1, 0, 200);
            LaserCanvas.Visibility = Visibility.Collapsed;

            BadgePanel.Opacity = 1;
            var scaleAnimX = new DoubleAnimation
            {
                From = 0.75,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
            };
            var scaleAnimY = scaleAnimX.Clone();
            BadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
            BadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);

            // Pulse glowing card aura
            if (CardBorder.Effect is DropShadowEffect shadow)
            {
                var blurAnim = new DoubleAnimation(40, 75, TimeSpan.FromMilliseconds(400)) { AutoReverse = true };
                shadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);
            }

            await Task.Delay(1100);

            // Phase 3: Warp Dissolve Transition into Main App
            var warpScale = new DoubleAnimation(1.0, 1.08, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(350));

            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, warpScale);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, warpScale);
            RootGrid.BeginAnimation(OpacityProperty, fadeOut);

            await Task.Delay(360);
        }

        private static void AnimateOpacity(UIElement element, double from, double to, int durationMs)
        {
            var anim = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs)
            };
            element.BeginAnimation(OpacityProperty, anim);
        }
    }
}
