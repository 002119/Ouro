using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace Ouro.Views
{
    /// <summary>
    /// Non-intrusive, top-most gaming HUD notification window.
    /// Appears in the top-right corner over full-screen or borderless games
    /// when a clip is triggered with F12 without taking keyboard focus.
    /// </summary>
    public partial class ClipOverlayWindow : Window
    {
        #region Win32 Extended Window Styles
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        #endregion

        private static ClipOverlayWindow? _activeInstance;

        public ClipOverlayWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Make window completely click-through and non-activating (never steals input from 3D games)
            var hwnd = new WindowInteropHelper(this).Handle;
            IntPtr currentExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            IntPtr newExStyle = new IntPtr(currentExStyle.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, newExStyle);
        }

        public static void ShowNotification(string gameName, string specs, string badge = "REPLAY EN DISCO")
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _activeInstance?.Close();
                    _activeInstance = null;

                    var overlay = new ClipOverlayWindow();
                    overlay.TxtBadge.Text = badge;
                    overlay.TxtGameName.Text = string.IsNullOrWhiteSpace(gameName) ? "ESCRITORIO" : gameName.ToUpper();
                    overlay.TxtSpecs.Text = specs;

                    // Position in top-right corner of primary screen
                    double screenWidth = SystemParameters.WorkArea.Width;
                    overlay.Left = screenWidth - overlay.Width - 16;
                    overlay.Top = 24;

                    overlay.Show();

                    var anim = (Storyboard)overlay.Resources["FadeInOutAnimation"];
                    anim.Completed += (s, args) =>
                    {
                        try
                        {
                            overlay.Close();
                            if (_activeInstance == overlay)
                            {
                                _activeInstance = null;
                            }
                        }
                        catch { }
                    };
                    anim.Begin();
                    _activeInstance = overlay;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClipOverlayWindow] Notification display error: {ex.Message}");
                }
            }));
        }
    }
}
