using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Ouro.ViewModels;

namespace Ouro.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Pure code-behind: handles borderless window dragging, system tray minimization, and restoration.
    /// DataContext is injected externally via Dependency Injection.
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isExplicitExit;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.OnViewLoaded();
                }
            };
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MainViewModel oldVm)
            {
                oldVm.RequestShowWindow -= RestoreWindow;
            }

            if (e.NewValue is MainViewModel newVm)
            {
                newVm.RequestShowWindow += RestoreWindow;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Intercepts the window close event to hide the window to the system tray
        /// instead of shutting down the background capture pipeline.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            bool isShuttingDown = Application.Current?.Dispatcher?.HasShutdownStarted ?? false;

            if (!_isExplicitExit && !isShuttingDown)
            {
                e.Cancel = true;
                if (DataContext is MainViewModel vm)
                {
                    vm.SetUiVisible(false);
                }
                Hide();
                return;
            }

            base.OnClosing(e);
        }

        /// <summary>
        /// Minimizing hides the application from the taskbar, placing it in the tray area.
        /// </summary>
        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.SetUiVisible(false);
                }
                Hide();
            }
        }

        /// <summary>
        /// Restores and brings the main window to the foreground.
        /// </summary>
        public void RestoreWindow()
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SetUiVisible(true);
            }
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
        }

        public void ForceClose()
        {
            _isExplicitExit = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                TrayIcon?.Dispose();
            }
            catch { }

            base.OnClosed(e);
        }
    }
}
