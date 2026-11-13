using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Ouro.Models;
using Ouro.Services.Audio;
using Ouro.Services.Capture;
using Ouro.Services.Configuration;
using Ouro.Services.Input;
using Ouro.Services.Orchestration;
using Ouro.Services.Social;
using Ouro.Utils;
using Ouro.Views;

namespace Ouro.ViewModels
{
    /// <summary>
    /// ViewModel bridging backend services (orchestrator, replay buffers, capture engine, settings, audio, social)
    /// to the high-performance WPF user interface.
    /// Manages real-time live preview streaming before/during recording, window selection, audio routing, hotkeys, and Discord clip sharing.
    /// </summary>
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly IClipOrchestrator? _orchestrator;
        private readonly ISettingsManager? _settingsManager;
        private readonly IRamReplayBuffer? _replayBuffer;
        private readonly CaptureEngine? _captureEngine;
        private readonly IInputService? _inputService;
        private readonly IAudioService? _audioService;
        private readonly IDiscordService _discordService;
        private readonly Ouro.Services.Licensing.LicenseManager? _licenseManager;

        private CancellationTokenSource? _toastCts;
        private WriteableBitmap? _previewBitmap;

        private bool _isEngineRunning;
        private bool _isSavingClip;
        private bool _isPreviewEnabled = true;
        private bool _hasPreviewFrame;

        private string _statusText = "Previsualización En Vivo — Replay Inactivo (Presiona 'Iniciar Replay')";
        private string _lastClipSaved = "No hay clips guardados en esta sesión";
        private bool _isToastVisible;
        private string _toastMessage = string.Empty;

        private bool _isSettingsPanelOpen;
        private int _selectedSettingsTab; // 0: Video, 1: Audio & Pantalla, 2: Disco & Atajos

        // Gallery & Discord Webhook Sharing State
        private bool _isGalleryOpen;
        private bool _isUploadingToDiscord;
        private string _discordWebhookUrl = "";
        private string _discordStatusMessage = "Ingresa tu Webhook de Discord para compartir clips";
        private ClipItemInfo? _selectedClip;
        private string _clipsCountText = "0 clips guardados";
        private bool _isInsideGameFolder;
        private GameFolderInfo? _selectedGameFolder;

        private int _selectedBufferDuration = 60;
        private int _selectedFramerate = 60;
        private int _selectedBitrate = 15;
        private string _selectedResolution = "1080p";
        private string _selectedCaptureMode = "Monitor";
        private string _selectedAudioMode = "DualTrack";
        private string _selectedHotkey = "F12";
        private string _outputDirectory = @"%USERPROFILE%\Videos\Ouro\Clips";

        private AudioDeviceInfo? _selectedOutputDevice;
        private AudioDeviceInfo? _selectedInputDevice;
        private WindowCaptureItem? _selectedWindow;

        public bool IsEngineRunning
        {
            get => _isEngineRunning;
            set
            {
                if (SetProperty(ref _isEngineRunning, value))
                {
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(CanSaveClip));
                    OnPropertyChanged(nameof(IsCapturingIndicatorVisible));
                    OnPropertyChanged(nameof(ReplayStatusBadgeText));
                    ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)TriggerInstantSaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsSavingClip
        {
            get => _isSavingClip;
            set
            {
                if (SetProperty(ref _isSavingClip, value))
                {
                    OnPropertyChanged(nameof(CanSaveClip));
                    ((RelayCommand)TriggerInstantSaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsPreviewEnabled
        {
            get => _isPreviewEnabled;
            set
            {
                if (SetProperty(ref _isPreviewEnabled, value))
                {
                    if (_captureEngine != null)
                    {
                        _captureEngine.IsPreviewEnabled = value;
                    }
                    OnPropertyChanged(nameof(PreviewToggleText));
                    OnPropertyChanged(nameof(PreviewToggleGlyph));
                    OnPropertyChanged(nameof(PreviewToggleStatusText));
                    OnPropertyChanged(nameof(ShowPreviewStream));
                    OnPropertyChanged(nameof(ShowPreviewPaused));
                    OnPropertyChanged(nameof(ShowPreviewStandby));
                }
            }
        }

        public string PreviewToggleText => IsPreviewEnabled ? "👁 Ocultar Previsualización" : "👁 Activar Previsualización";
        public string PreviewToggleGlyph => IsPreviewEnabled ? "👁" : "🕶";
        public string PreviewToggleStatusText => IsPreviewEnabled ? "Preview Activa" : "Preview Oculta (Modo Rendimiento)";

        public void SetUiVisible(bool isVisible)
        {
            if (_captureEngine != null)
            {
                _captureEngine.IsUiVisible = isVisible;
            }
        }

        public bool HasPreviewFrame
        {
            get => _hasPreviewFrame;
            set
            {
                if (SetProperty(ref _hasPreviewFrame, value))
                {
                    OnPropertyChanged(nameof(ShowPreviewStream));
                    OnPropertyChanged(nameof(ShowPreviewStandby));
                }
            }
        }

        public bool ShowPreviewStream => IsPreviewEnabled && HasPreviewFrame;
        public bool ShowPreviewPaused => !IsPreviewEnabled;
        public bool ShowPreviewStandby => IsPreviewEnabled && !HasPreviewFrame;

        public bool CanSaveClip => IsEngineRunning && !IsSavingClip;

        public string ReplayStatusBadgeText => IsEngineRunning
            ? $"● REPLAY EN RAM ACTIVO ({SelectedBufferDuration}s)"
            : "● PREVISUALIZACIÓN EN VIVO";

        public string TargetDescription
        {
            get
            {
                if (SelectedCaptureMode == "SpecificWindow" && SelectedWindow != null)
                {
                    return SelectedWindow.DisplayName;
                }
                if (SelectedCaptureMode == "GameOnly")
                {
                    return "Juego en Primer Plano (Auto)";
                }
                return SelectedMonitor != null ? SelectedMonitor.DisplayName : "Pantalla Completa (Monitor Principal)";
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string LastClipSaved
        {
            get => _lastClipSaved;
            set => SetProperty(ref _lastClipSaved, value);
        }

        public bool IsToastVisible
        {
            get => _isToastVisible;
            set => SetProperty(ref _isToastVisible, value);
        }

        public string ToastMessage
        {
            get => _toastMessage;
            set => SetProperty(ref _toastMessage, value);
        }

        public bool IsSettingsPanelOpen
        {
            get => _isSettingsPanelOpen;
            set
            {
                if (SetProperty(ref _isSettingsPanelOpen, value) && value)
                {
                    string outId = !string.IsNullOrWhiteSpace(SelectedOutputDevice?.Id)
                        ? SelectedOutputDevice.Id
                        : (_settingsManager?.Settings.SelectedAudioOutputDeviceId ?? "");
                    string inId = !string.IsNullOrWhiteSpace(SelectedInputDevice?.Id)
                        ? SelectedInputDevice.Id
                        : (_settingsManager?.Settings.SelectedAudioInputDeviceId ?? "");
                    RefreshAudioDevices(outId, inId);
                    ExecuteRefreshWindows();
                }
            }
        }

        public int SelectedSettingsTab
        {
            get => _selectedSettingsTab;
            set => SetProperty(ref _selectedSettingsTab, value);
        }

        public ImageSource? PreviewImage => _previewBitmap;
        public bool IsCapturingIndicatorVisible => IsEngineRunning;

        // --- Video & Quality Options ---
        public int[] AvailableBufferDurations { get; } = [10, 20, 30, 45, 60, 120, 300];
        public int SelectedBufferDuration
        {
            get => _selectedBufferDuration;
            set => SetProperty(ref _selectedBufferDuration, value);
        }

        public int[] AvailableFramerates { get; } = [30, 60, 120, 144];
        public int SelectedFramerate
        {
            get => _selectedFramerate;
            set => SetProperty(ref _selectedFramerate, value);
        }

        public int[] AvailableBitrates { get; } = [10, 15, 25, 50];
        public int SelectedBitrate
        {
            get => _selectedBitrate;
            set => SetProperty(ref _selectedBitrate, value);
        }

        public string[] AvailableResolutions { get; } = ["1080p", "720p", "1440p", "4K"];
        public string SelectedResolution
        {
            get => _selectedResolution;
            set => SetProperty(ref _selectedResolution, value);
        }

        // --- Screen & Window Capture Options ---
        public string[] AvailableCaptureModes { get; } = ["Monitor", "GameOnly", "SpecificWindow"];
        public string SelectedCaptureMode
        {
            get => _selectedCaptureMode;
            set
            {
                if (SetProperty(ref _selectedCaptureMode, value))
                {
                    OnPropertyChanged(nameof(IsSpecificWindowSelected));
                    OnPropertyChanged(nameof(TargetDescription));
                }
            }
        }

        public bool IsSpecificWindowSelected => SelectedCaptureMode == "SpecificWindow";

        public ObservableCollection<DisplayMonitorInfo> AvailableMonitors { get; } = new();
        private DisplayMonitorInfo? _selectedMonitor;
        public DisplayMonitorInfo? SelectedMonitor
        {
            get => _selectedMonitor;
            set
            {
                if (SetProperty(ref _selectedMonitor, value))
                {
                    OnPropertyChanged(nameof(TargetDescription));
                    if (value != null && SelectedCaptureMode == "Monitor")
                    {
                        if (_captureEngine != null)
                        {
                            _captureEngine.StartForMonitor(value.MonitorHandle != IntPtr.Zero ? value.MonitorHandle : null, value.DisplayName);
                        }
                        if (_settingsManager != null)
                        {
                            _settingsManager.Settings.SelectedDisplayIndex = value.Index;
                            Task.Run(async () => await _settingsManager.SaveSettingsAsync(_settingsManager.Settings));
                        }
                    }
                }
            }
        }

        public ObservableCollection<WindowCaptureItem> AvailableWindows { get; } = new();
        public WindowCaptureItem? SelectedWindow
        {
            get => _selectedWindow;
            set
            {
                if (SetProperty(ref _selectedWindow, value))
                {
                    OnPropertyChanged(nameof(TargetDescription));
                    if (value != null)
                    {
                        if (_orchestrator != null)
                        {
                            _orchestrator.TargetWindowHandle = value.Hwnd;
                        }
                        if (_captureEngine != null && SelectedCaptureMode == "SpecificWindow")
                        {
                            _captureEngine.StartForWindow(value.Hwnd, value.ProcessName);
                        }
                    }
                }
            }
        }

        // --- Audio Devices & Modes ---
        public string[] AvailableAudioModes { get; } = ["DualTrack", "GameOnly", "MicOnly"];
        public string SelectedAudioMode
        {
            get => _selectedAudioMode;
            set => SetProperty(ref _selectedAudioMode, value);
        }

        public ObservableCollection<AudioDeviceInfo> AvailableOutputDevices { get; } = new();
        public AudioDeviceInfo? SelectedOutputDevice
        {
            get => _selectedOutputDevice;
            set => SetProperty(ref _selectedOutputDevice, value);
        }

        public ObservableCollection<AudioDeviceInfo> AvailableInputDevices { get; } = new();
        public AudioDeviceInfo? SelectedInputDevice
        {
            get => _selectedInputDevice;
            set => SetProperty(ref _selectedInputDevice, value);
        }

        private double _gameVolume = 1.0;
        private double _micVolume = 1.0;

        public double GameVolume
        {
            get => _gameVolume;
            set
            {
                if (SetProperty(ref _gameVolume, value))
                {
                    OnPropertyChanged(nameof(GameVolumePercentageText));
                    _orchestrator?.UpdateVolumes(_gameVolume, _micVolume);
                    if (_settingsManager != null) _settingsManager.Settings.GameVolume = _gameVolume;
                }
            }
        }

        public string GameVolumePercentageText => $"{(int)Math.Round(GameVolume * 100)}%";

        public double MicVolume
        {
            get => _micVolume;
            set
            {
                if (SetProperty(ref _micVolume, value))
                {
                    OnPropertyChanged(nameof(MicVolumePercentageText));
                    _orchestrator?.UpdateVolumes(_gameVolume, _micVolume);
                    if (_settingsManager != null) _settingsManager.Settings.MicVolume = _micVolume;
                }
            }
        }

        public string MicVolumePercentageText => $"{(int)Math.Round(MicVolume * 100)}%";

        // --- Hotkey & Storage ---
        public string[] AvailableHotkeys { get; } = ["F12", "F11", "F10", "F9", "F8", "F7", "Ctrl+F12", "Alt+F10", "Ctrl+Shift+S"];
        public string SelectedHotkey
        {
            get => _selectedHotkey;
            set => SetProperty(ref _selectedHotkey, value);
        }

        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                if (SetProperty(ref _outputDirectory, value))
                {
                    UpdateDiskSpaceInfo();
                }
            }
        }

        public bool CanStart => !IsEngineRunning;
        public bool CanStop => IsEngineRunning;

        // Gallery & Discord Properties
        public ObservableCollection<ClipItemInfo> ClipsList { get; } = new();
        public ObservableCollection<GameFolderInfo> GameFolders { get; } = new();
        public ObservableCollection<ClipItemInfo> FilteredClipsList { get; } = new();

        public bool IsGalleryOpen
        {
            get => _isGalleryOpen;
            set
            {
                if (SetProperty(ref _isGalleryOpen, value) && value)
                {
                    UpdateDiskSpaceInfo();
                    ExecuteRefreshClips();
                }
            }
        }

        public bool IsInsideGameFolder
        {
            get => _isInsideGameFolder;
            set => SetProperty(ref _isInsideGameFolder, value);
        }

        public GameFolderInfo? SelectedGameFolder
        {
            get => _selectedGameFolder;
            set => SetProperty(ref _selectedGameFolder, value);
        }

        public bool IsUploadingToDiscord
        {
            get => _isUploadingToDiscord;
            set => SetProperty(ref _isUploadingToDiscord, value);
        }

        public string DiscordWebhookUrl
        {
            get => _discordWebhookUrl;
            set => SetProperty(ref _discordWebhookUrl, value ?? "");
        }

        private string _freeDiskSpaceText = "💾 Espacio en Disco: Calculando...";
        public string FreeDiskSpaceText
        {
            get => _freeDiskSpaceText;
            set => SetProperty(ref _freeDiskSpaceText, value);
        }

        private bool _isLowDiskSpace;
        public bool IsLowDiskSpace
        {
            get => _isLowDiskSpace;
            set
            {
                if (SetProperty(ref _isLowDiskSpace, value))
                {
                    OnPropertyChanged(nameof(DiskSpaceForeground));
                }
            }
        }

        public string DiskSpaceForeground => IsLowDiskSpace ? "#FF3366" : "#00FFA3";

        public void UpdateDiskSpaceInfo()
        {
            try
            {
                string targetPath = !string.IsNullOrWhiteSpace(OutputDirectory) ? OutputDirectory : @"C:\OuroClips";
                string fullPath = Environment.ExpandEnvironmentVariables(targetPath);
                string root = Path.GetPathRoot(Path.GetFullPath(fullPath)) ?? "C:\\";
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    FreeDiskSpaceText = $"💾 Disco {drive.Name.TrimEnd('\\')}: {freeGb:F1} GB libres";
                    IsLowDiskSpace = freeGb < 10.0;
                }
            }
            catch
            {
                FreeDiskSpaceText = "💾 Disco: Estado normal";
                IsLowDiskSpace = false;
            }
        }

        private int _selectedDiscordLimit = 48;
        public int SelectedDiscordLimit
        {
            get => _selectedDiscordLimit;
            set => SetProperty(ref _selectedDiscordLimit, value);
        }

        public ObservableCollection<DiscordLimitOption> AvailableDiscordLimits { get; } = new()
        {
            new DiscordLimitOption { Mb = 18, Label = "18 MB (Gratis)" },
            new DiscordLimitOption { Mb = 48, Label = "48 MB (Nivel 1)" }
        };

        private DiscordLimitOption? _selectedDiscordLimitOption;
        public DiscordLimitOption? SelectedDiscordLimitOption
        {
            get => _selectedDiscordLimitOption;
            set
            {
                if (SetProperty(ref _selectedDiscordLimitOption, value) && value != null)
                {
                    SelectedDiscordLimit = value.Mb;
                    if (_settingsManager != null && _settingsManager.Settings.DiscordMaxUploadMb != value.Mb)
                    {
                        _settingsManager.Settings.DiscordMaxUploadMb = value.Mb;
                        Task.Run(async () => await _settingsManager.SaveSettingsAsync(_settingsManager.Settings));
                    }
                }
            }
        }

        private bool _startWithWindows;
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (SetProperty(ref _startWithWindows, value))
                {
                    Utils.WindowsStartupHelper.SetStartupEnabled(value);
                    if (_settingsManager != null)
                    {
                        _settingsManager.Settings.StartWithWindows = value;
                        Task.Run(async () => await _settingsManager.SaveSettingsAsync(_settingsManager.Settings));
                    }
                }
            }
        }

        private bool _playSoundOnClip = true;
        public bool PlaySoundOnClip
        {
            get => _playSoundOnClip;
            set
            {
                if (SetProperty(ref _playSoundOnClip, value))
                {
                    if (_settingsManager != null)
                    {
                        _settingsManager.Settings.PlaySoundOnClip = value;
                        Task.Run(async () => await _settingsManager.SaveSettingsAsync(_settingsManager.Settings));
                    }
                }
            }
        }

        public string DiscordStatusMessage
        {
            get => _discordStatusMessage;
            set => SetProperty(ref _discordStatusMessage, value);
        }

        public ClipItemInfo? SelectedClip
        {
            get => _selectedClip;
            set
            {
                if (SetProperty(ref _selectedClip, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private bool _separateAudioTracksInMp4 = true;
        public bool SeparateAudioTracksInMp4
        {
            get => _separateAudioTracksInMp4;
            set => SetProperty(ref _separateAudioTracksInMp4, value);
        }

        private bool _enableVideoOverlay;
        public bool EnableVideoOverlay
        {
            get => _enableVideoOverlay;
            set => SetProperty(ref _enableVideoOverlay, value);
        }

        private string _overlayCustomText = "OURO";
        public string OverlayCustomText
        {
            get => _overlayCustomText;
            set => SetProperty(ref _overlayCustomText, value);
        }

        private bool _overlayIncludeTelemetry = true;
        public bool OverlayIncludeTelemetry
        {
            get => _overlayIncludeTelemetry;
            set => SetProperty(ref _overlayIncludeTelemetry, value);
        }

        public string ClipsCountText
        {
            get => _clipsCountText;
            set => SetProperty(ref _clipsCountText, value);
        }

        // Commands
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ToggleEngineCommand { get; }
        public ICommand ShowWindowCommand { get; }
        public ICommand ExitApplicationCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand SelectTabCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand SelectDriveCCommand { get; }
        public ICommand SelectDriveDCommand { get; }
        public ICommand OpenClipsFolderCommand { get; }
        public ICommand TriggerInstantSaveCommand { get; }
        public ICommand RefreshWindowsCommand { get; }
        public ICommand TogglePreviewCommand { get; }
        public ICommand ToggleGalleryCommand { get; }
        public ICommand RefreshClipsCommand { get; }
        public ICommand PlayClipCommand { get; }
        public ICommand OpenClipFolderCommand { get; }
        public ICommand DeleteClipCommand { get; }
        public ICommand ShareToDiscordCommand { get; }
        public ICommand TestDiscordWebhookCommand { get; }
        public ICommand PostManualCommand { get; }
        public ICommand SelectGameFolderCommand { get; }
        public ICommand BackToGameFoldersCommand { get; }
        public ICommand RenewLicenseCommand { get; }

        public event Action? RequestShowWindow;

        public MainViewModel() : this(null, null, null, null, null, null, null, null)
        {
        }

        [ActivatorUtilitiesConstructor]
        public MainViewModel(
            IClipOrchestrator? orchestrator,
            ISettingsManager? settingsManager = null,
            IRamReplayBuffer? replayBuffer = null,
            CaptureEngine? captureEngine = null,
            IInputService? inputService = null,
            IAudioService? audioService = null,
            IDiscordService? discordService = null,
            Ouro.Services.Licensing.LicenseManager? licenseManager = null)
        {
            _orchestrator = orchestrator;
            _settingsManager = settingsManager;
            _replayBuffer = replayBuffer;
            _captureEngine = captureEngine;
            _inputService = inputService;
            _audioService = audioService;
            _discordService = discordService ?? new DiscordWebhookService();
            _licenseManager = licenseManager;

            string initOutputDevId = "";
            string initInputDevId = "";
            string initWindowName = "";

            if (_settingsManager != null)
            {
                var s = _settingsManager.Settings;
                _selectedBufferDuration = s.BufferDurationSeconds;
                _selectedFramerate = s.TargetFPS > 0 ? s.TargetFPS : 60;
                _selectedBitrate = s.VideoBitrateMbps > 0 ? s.VideoBitrateMbps : 15;
                _selectedResolution = !string.IsNullOrWhiteSpace(s.Resolution) ? s.Resolution : "1080p";
                _selectedCaptureMode = !string.IsNullOrWhiteSpace(s.CaptureTargetMode) ? s.CaptureTargetMode : "Monitor";
                _selectedAudioMode = !string.IsNullOrWhiteSpace(s.AudioMode) ? s.AudioMode : "DualTrack";
                _selectedHotkey = !string.IsNullOrWhiteSpace(s.SaveHotkey) ? s.SaveHotkey : "F12";
                _outputDirectory = !string.IsNullOrWhiteSpace(s.OutputDirectoryPath) ? s.OutputDirectoryPath : @"%USERPROFILE%\Videos\Ouro\Clips";
                _discordWebhookUrl = s.DiscordWebhookUrl ?? "";
                _selectedDiscordLimit = s.DiscordMaxUploadMb > 0 ? (s.DiscordMaxUploadMb == 24 ? 18 : s.DiscordMaxUploadMb) : 18;
                _selectedDiscordLimitOption = AvailableDiscordLimits.FirstOrDefault(x => x.Mb == _selectedDiscordLimit) ?? AvailableDiscordLimits[0];
                _gameVolume = s.GameVolume >= 0 ? s.GameVolume : 1.0;
                _micVolume = s.MicVolume >= 0 ? s.MicVolume : 1.0;
                initOutputDevId = s.SelectedAudioOutputDeviceId ?? "";
                initInputDevId = s.SelectedAudioInputDeviceId ?? "";
                initWindowName = s.SelectedWindowTitle ?? "";
                _startWithWindows = s.StartWithWindows || Utils.WindowsStartupHelper.IsStartupEnabled();
                _playSoundOnClip = s.PlaySoundOnClip;
                _separateAudioTracksInMp4 = s.SeparateAudioTracksInMp4;
                _enableVideoOverlay = s.EnableVideoOverlay;
                _overlayCustomText = !string.IsNullOrWhiteSpace(s.OverlayCustomText) ? s.OverlayCustomText : "OURO";
                _overlayIncludeTelemetry = s.OverlayIncludeTelemetry;
            }

            // Initialize Physical Monitors
            var monitors = Utils.DisplayMonitorHelper.GetMonitors();
            AvailableMonitors.Clear();
            foreach (var m in monitors)
            {
                AvailableMonitors.Add(m);
            }
            int savedDisplayIdx = _settingsManager?.Settings.SelectedDisplayIndex ?? 0;
            _selectedMonitor = AvailableMonitors.FirstOrDefault(m => m.Index == savedDisplayIdx) ?? AvailableMonitors.FirstOrDefault();
            UpdateDiskSpaceInfo();

            _isEngineRunning = _orchestrator?.IsRunning ?? false;
            _statusText = _isEngineRunning
                ? $"🔴 REPLAY EN RAM ACTIVO — {_selectedResolution} @ {_selectedFramerate} FPS ({_selectedBufferDuration}s, {_selectedHotkey})"
                : "Previsualización En Vivo — Replay Inactivo (Presiona 'Iniciar Replay')";

            StartCommand = new RelayCommand(ExecuteStart, () => CanStart);
            StopCommand = new RelayCommand(ExecuteStop, () => CanStop);
            ToggleEngineCommand = new RelayCommand(ExecuteToggleEngine);
            ShowWindowCommand = new RelayCommand(ExecuteShowWindow);
            ExitApplicationCommand = new RelayCommand(ExecuteExitApplication);
            ToggleSettingsCommand = new RelayCommand(ExecuteToggleSettings);
            SaveSettingsCommand = new RelayCommand(async () => await ExecuteSaveSettingsAsync());
            SelectTabCommand = new RelayCommand(tab =>
            {
                if (int.TryParse(tab?.ToString(), out int idx))
                {
                    SelectedSettingsTab = idx;
                }
            });
            BrowseFolderCommand = new RelayCommand(ExecuteBrowseFolder);
            SelectDriveCCommand = new RelayCommand(() => OutputDirectory = @"C:\OuroClips");
            SelectDriveDCommand = new RelayCommand(() => OutputDirectory = @"D:\OuroClips");
            OpenClipsFolderCommand = new RelayCommand(ExecuteOpenClipsFolder);
            TriggerInstantSaveCommand = new RelayCommand(ExecuteTriggerInstantSave, () => CanSaveClip);
            RefreshWindowsCommand = new RelayCommand(() => ExecuteRefreshWindows());
            TogglePreviewCommand = new RelayCommand(() => IsPreviewEnabled = !IsPreviewEnabled);
            ToggleGalleryCommand = new RelayCommand(() => IsGalleryOpen = !IsGalleryOpen);
            RefreshClipsCommand = new RelayCommand(ExecuteRefreshClips);
            PlayClipCommand = new RelayCommand(ExecutePlayClip, () => SelectedClip != null);
            OpenClipFolderCommand = new RelayCommand(ExecuteOpenClipFolder, () => SelectedClip != null);
            DeleteClipCommand = new RelayCommand(ExecuteDeleteClip, () => SelectedClip != null);
            ShareToDiscordCommand = new RelayCommand(async () => await ExecuteShareToDiscordAsync(), () => SelectedClip != null && !IsUploadingToDiscord);
            TestDiscordWebhookCommand = new RelayCommand(async () => await ExecuteTestDiscordWebhookAsync(), () => !string.IsNullOrWhiteSpace(DiscordWebhookUrl));
            PostManualCommand = new RelayCommand(async () => await ExecutePostManualAsync(), () => !string.IsNullOrWhiteSpace(DiscordWebhookUrl));
            SelectGameFolderCommand = new RelayCommand(folder =>
            {
                if (folder is GameFolderInfo gf)
                {
                    SelectGameFolder(gf);
                }
            });
            BackToGameFoldersCommand = new RelayCommand(BackToGameFolders);
            RenewLicenseCommand = new RelayCommand(_ => ExecuteRenewLicense());

            if (_orchestrator != null)
            {
                _orchestrator.OnClipSaveStarted += HandleClipSaveStarted;
                _orchestrator.OnClipSaved += HandleClipSaved;
                _orchestrator.OnClipSaveFailed += HandleClipSaveFailed;
            }

            if (_captureEngine != null)
            {
                _captureEngine.PreviewFrameReady += OnPreviewFrameReady;
            }

            RefreshAudioDevices(initOutputDevId, initInputDevId);
            ExecuteRefreshWindows(initWindowName);

            // Activate live preview stream immediately on boot!
            try
            {
                _orchestrator?.EnsureCaptureStarted();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] Failed to initialize live preview: {ex.Message}");
            }
        }

        private void OnPreviewFrameReady(byte[] pixelData, int pitch, int width, int height)
        {
            if (!_isPreviewEnabled) return;

            Dispatch(() =>
            {
                try
                {
                    if (_previewBitmap == null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
                    {
                        _previewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                        OnPropertyChanged(nameof(PreviewImage));
                    }

                    _previewBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixelData, pitch, 0);

                    if (!_hasPreviewFrame)
                    {
                        HasPreviewFrame = true;
                    }
                }
                catch
                {
                    // Ignore transient memory map locks during window resizing
                }
            });
        }

        public void OnViewLoaded()
        {
            try
            {
                OnPropertyChanged(nameof(SelectedResolution));
                OnPropertyChanged(nameof(SelectedFramerate));
                OnPropertyChanged(nameof(SelectedBitrate));
                OnPropertyChanged(nameof(SelectedBufferDuration));
                OnPropertyChanged(nameof(SelectedAudioMode));
                OnPropertyChanged(nameof(SelectedHotkey));
                OnPropertyChanged(nameof(OutputDirectory));
                OnPropertyChanged(nameof(SelectedCaptureMode));
                OnPropertyChanged(nameof(SelectedOutputDevice));
                OnPropertyChanged(nameof(SelectedInputDevice));

                _orchestrator?.EnsureCaptureStarted();
                _captureEngine?.RequestPreviewRefresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] OnViewLoaded error: {ex.Message}");
            }
        }

        private void RefreshAudioDevices(string? matchOutputId = null, string? matchInputId = null)
        {
            Task.Run(() =>
            {
                var outputs = new System.Collections.Generic.List<AudioDeviceInfo>();
                var inputs = new System.Collections.Generic.List<AudioDeviceInfo>();

                if (_audioService != null)
                {
                    try
                    {
                        outputs.AddRange(_audioService.GetOutputDevices());
                        inputs.AddRange(_audioService.GetInputDevices());
                    }
                    catch { }
                }

                if (outputs.Count == 0)
                {
                    outputs.Add(new AudioDeviceInfo { Id = "", Name = "Dispositivo Predeterminado de Salida" });
                }
                if (inputs.Count == 0)
                {
                    inputs.Add(new AudioDeviceInfo { Id = "", Name = "Dispositivo Predeterminado (Micrófono)" });
                }

                Dispatch(() =>
                {
                    string targetOutId = !string.IsNullOrWhiteSpace(matchOutputId)
                        ? matchOutputId
                        : (!string.IsNullOrWhiteSpace(SelectedOutputDevice?.Id)
                            ? SelectedOutputDevice.Id
                            : (_settingsManager?.Settings.SelectedAudioOutputDeviceId ?? ""));

                    string targetInId = !string.IsNullOrWhiteSpace(matchInputId)
                        ? matchInputId
                        : (!string.IsNullOrWhiteSpace(SelectedInputDevice?.Id)
                            ? SelectedInputDevice.Id
                            : (_settingsManager?.Settings.SelectedAudioInputDeviceId ?? ""));

                    bool outChanged = AvailableOutputDevices.Count != outputs.Count || !AvailableOutputDevices.Select(x => x.Id).SequenceEqual(outputs.Select(x => x.Id));
                    if (outChanged)
                    {
                        AvailableOutputDevices.Clear();
                        foreach (var d in outputs) AvailableOutputDevices.Add(d);
                    }

                    bool inChanged = AvailableInputDevices.Count != inputs.Count || !AvailableInputDevices.Select(x => x.Id).SequenceEqual(inputs.Select(x => x.Id));
                    if (inChanged)
                    {
                        AvailableInputDevices.Clear();
                        foreach (var d in inputs) AvailableInputDevices.Add(d);
                    }

                    if (!string.IsNullOrWhiteSpace(targetOutId))
                    {
                        var found = AvailableOutputDevices.FirstOrDefault(d => d.Id == targetOutId)
                                 ?? AvailableOutputDevices.FirstOrDefault(d => d.Name.Contains(targetOutId, StringComparison.OrdinalIgnoreCase));
                        if (found != null)
                        {
                            SelectedOutputDevice = found;
                        }
                    }
                    else if (SelectedOutputDevice == null)
                    {
                        SelectedOutputDevice = AvailableOutputDevices.FirstOrDefault();
                    }

                    if (!string.IsNullOrWhiteSpace(targetInId))
                    {
                        var found = AvailableInputDevices.FirstOrDefault(d => d.Id == targetInId)
                                 ?? AvailableInputDevices.FirstOrDefault(d => d.Name.Contains(targetInId, StringComparison.OrdinalIgnoreCase));
                        if (found != null)
                        {
                            SelectedInputDevice = found;
                        }
                    }
                    else if (SelectedInputDevice == null)
                    {
                        SelectedInputDevice = AvailableInputDevices.FirstOrDefault();
                    }
                });
            });
        }

        private void ExecuteRefreshWindows(string? matchTitle = null)
        {
            Task.Run(() =>
            {
                try
                {
                    var list = WindowEnumerator.GetCaptureableWindows();
                    Dispatch(() =>
                    {
                        AvailableWindows.Clear();
                        foreach (var w in list)
                        {
                            AvailableWindows.Add(w);
                        }

                        if (!string.IsNullOrEmpty(matchTitle))
                        {
                            SelectedWindow = AvailableWindows.FirstOrDefault(w => w.Title.Contains(matchTitle, StringComparison.OrdinalIgnoreCase))
                                             ?? AvailableWindows.FirstOrDefault();
                        }
                        else if (SelectedWindow == null && AvailableWindows.Count > 0)
                        {
                            SelectedWindow = AvailableWindows[0];
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error refreshing windows: {ex.Message}");
                }
            });
        }

        private void ExecuteStart()
        {
            try
            {
                if (_orchestrator != null)
                {
                    if (SelectedWindow != null)
                    {
                        _orchestrator.TargetWindowHandle = SelectedWindow.Hwnd;
                    }
                    _orchestrator.Start();
                }

                IsEngineRunning = true;
                StatusText = $"🔴 REPLAY EN RAM ACTIVO — {SelectedResolution} @ {SelectedFramerate} FPS ({SelectedBufferDuration}s, {SelectedHotkey})";
                ShowToast("Replay en RAM Iniciado (0 MB/s en disco)");
            }
            catch (Exception ex)
            {
                ShowToast($"Error al iniciar replay: {ex.Message}");
            }
        }

        private void ExecuteStop()
        {
            try
            {
                if (_orchestrator != null)
                {
                    _orchestrator.Stop();
                }

                IsEngineRunning = false;
                StatusText = "Previsualización En Vivo — Replay Inactivo (Cero escritura a disco)";
                ShowToast("Replay Detenido (Previsualización Activa)");
            }
            catch (Exception ex)
            {
                ShowToast($"Error al detener replay: {ex.Message}");
            }
        }

        private void ExecuteToggleEngine()
        {
            if (IsEngineRunning)
            {
                ExecuteStop();
            }
            else
            {
                ExecuteStart();
            }
        }

        private void ExecuteShowWindow()
        {
            Dispatch(() =>
            {
                if (Application.Current?.MainWindow is Views.MainWindow mw)
                {
                    mw.RestoreWindow();
                }
                else
                {
                    var window = Application.Current?.MainWindow;
                    if (window != null)
                    {
                        window.Show();
                        if (window.WindowState == WindowState.Minimized)
                        {
                            window.WindowState = WindowState.Normal;
                        }
                        window.Activate();
                    }
                }
                RequestShowWindow?.Invoke();
            });
        }

        private void ExecuteExitApplication()
        {
            Dispatch(() =>
            {
                Application.Current?.Shutdown();
            });
        }

        private void ExecuteToggleSettings()
        {
            IsSettingsPanelOpen = !IsSettingsPanelOpen;
        }

        private void ExecuteBrowseFolder()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Seleccionar carpeta para guardar clips de Ouro",
                    InitialDirectory = Directory.Exists(OutputDirectory) ? OutputDirectory : "C:\\"
                };

                if (dialog.ShowDialog() == true)
                {
                    OutputDirectory = dialog.FolderName;
                }
            }
            catch (Exception ex)
            {
                ShowToast($"Error al explorar: {ex.Message}");
            }
        }

        private void ExecuteOpenClipsFolder()
        {
            try
            {
                string dir = Environment.ExpandEnvironmentVariables(OutputDirectory);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowToast($"Error al abrir carpeta: {ex.Message}");
            }
        }

        private void ExecuteTriggerInstantSave()
        {
            if (!IsEngineRunning)
            {
                ShowToast("Inicia primero el motor de repetición");
                return;
            }

            if (IsSavingClip) return;
            IsSavingClip = true;

            Task.Run(async () =>
            {
                try
                {
                    if (_orchestrator != null)
                    {
                        await _orchestrator.TriggerSaveClipAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Dispatch(() => ShowToast($"Error al guardar: {ex.Message}"));
                }
                finally
                {
                    Dispatch(() => IsSavingClip = false);
                }
            });
        }

        public static (int width, int height) GetDimensionsForResolution(string res) => res switch
        {
            "720p" => (1280, 720),
            "1440p" => (2560, 1440),
            "4K" => (3840, 2160),
            _ => (1920, 1080)
        };

        private async Task ExecuteSaveSettingsAsync()
        {
            var (w, h) = GetDimensionsForResolution(SelectedResolution);

            var updated = new OuroSettings
            {
                BufferDurationSeconds = SelectedBufferDuration,
                TargetFPS = SelectedFramerate,
                VideoBitrateMbps = SelectedBitrate,
                Resolution = SelectedResolution,
                OutputWidth = w,
                OutputHeight = h,
                CaptureTargetMode = SelectedCaptureMode,
                SelectedWindowTitle = SelectedWindow?.Title ?? "",
                AudioMode = SelectedAudioMode,
                SelectedAudioOutputDeviceId = SelectedOutputDevice?.Id ?? "",
                SelectedAudioInputDeviceId = SelectedInputDevice?.Id ?? "",
                GameVolume = GameVolume,
                MicVolume = MicVolume,
                OutputDirectoryPath = OutputDirectory,
                SaveHotkey = SelectedHotkey,
                DiscordWebhookUrl = DiscordWebhookUrl,
                DiscordMaxUploadMb = SelectedDiscordLimit,
                StartWithWindows = StartWithWindows,
                PlaySoundOnClip = PlaySoundOnClip,
                SelectedDisplayIndex = SelectedMonitor?.Index ?? 0,
                SeparateAudioTracksInMp4 = SeparateAudioTracksInMp4,
                EnableVideoOverlay = EnableVideoOverlay,
                OverlayCustomText = OverlayCustomText,
                OverlayIncludeTelemetry = OverlayIncludeTelemetry
            };

            Utils.WindowsStartupHelper.SetStartupEnabled(StartWithWindows);

            if (_settingsManager != null)
            {
                await _settingsManager.SaveSettingsAsync(updated).ConfigureAwait(false);
            }

            if (_replayBuffer != null)
            {
                _replayBuffer.MaxDurationSeconds = SelectedBufferDuration;
            }

            if (_orchestrator != null)
            {
                if (SelectedWindow != null)
                {
                    _orchestrator.TargetWindowHandle = SelectedWindow.Hwnd;
                }
                _orchestrator.UpdateSettings(updated);
            }

            if (_captureEngine != null)
            {
                _captureEngine.SetResolution(w, h);
                _captureEngine.SetTargetFramerate(SelectedFramerate);
                _captureEngine.ConfigureOverlay(EnableVideoOverlay, OverlayCustomText, OverlayIncludeTelemetry);
                _captureEngine.CaptureBaselineScreen();
            }

            if (IsEngineRunning)
            {
                _orchestrator?.Stop();
                _orchestrator?.Start();
            }
            else
            {
                _orchestrator?.EnsureCaptureStarted();
            }

            if (_inputService != null && !string.IsNullOrWhiteSpace(SelectedHotkey))
            {
                _inputService.RegisterSaveHotkey(SelectedHotkey);
            }

            Dispatch(() =>
            {
                IsSettingsPanelOpen = false;
                OnPropertyChanged(nameof(TargetDescription));
                StatusText = IsEngineRunning
                    ? $"🔴 REPLAY EN RAM ACTIVO — {SelectedResolution} @ {SelectedFramerate} FPS | Buffer {SelectedBufferDuration}s | {SelectedHotkey}"
                    : "Previsualización En Vivo — Configuración Actualizada";
                ShowToast("Configuración Guardada y Aplicada");
            });
        }

        private void HandleClipSaveStarted()
        {
            if (PlaySoundOnClip)
            {
                Utils.SoundFeedbackHelper.PlayClipChime();
            }

            Dispatch(() =>
            {
                StatusText = "⚡ Persistiendo clip en disco...";
            });
        }

        private void HandleClipSaved(string filePath)
        {
            Dispatch(() =>
            {
                string fileName = Path.GetFileName(filePath);
                string folderName = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "") ?? "Desktop";
                string resolvedGame = Utils.GameProcessResolver.ResolveGameName(folderName);
                string gameDisplayName = string.Equals(resolvedGame, "Desktop", StringComparison.OrdinalIgnoreCase)
                    ? "ESCRITORIO"
                    : resolvedGame.ToUpper();

                int durationSec = _orchestrator != null && _orchestrator.LastClipDurationSeconds > 0
                    ? (int)Math.Max(1, Math.Round(_orchestrator.LastClipDurationSeconds))
                    : SelectedBufferDuration;

                string specs = $"{SelectedResolution} @ {SelectedFramerate} FPS ({durationSec}s)";

                LastClipSaved = $"{fileName} ({DateTime.Now:HH:mm:ss})";
                StatusText = $"Clip guardado exitosamente: {fileName}";
                ShowToast($"Clip Guardado: {fileName}");

                ClipOverlayWindow.ShowNotification(gameDisplayName, specs, "REPLAY EN DISCO");
                ExecuteRefreshClips();
            });
        }

        private void ExecuteRefreshClips()
        {
            Task.Run(() =>
            {
                try
                {
                    string baseDir = Environment.ExpandEnvironmentVariables(OutputDirectory);
                    if (!Directory.Exists(baseDir))
                    {
                        Dispatch(() =>
                        {
                            ClipsList.Clear();
                            FilteredClipsList.Clear();
                            GameFolders.Clear();
                            ClipsCountText = "0 clips guardados";
                        });
                        return;
                    }

                    var files = Directory.GetFiles(baseDir, "*.mp4", SearchOption.AllDirectories)
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.CreationTime)
                        .Select(ClipItemInfo.FromFileInfo)
                        .ToList();

                    // Group clips by GameName
                    var groups = files.GroupBy(f => f.GameName)
                        .Select(g =>
                        {
                            long totalBytes = g.Sum(c => c.FileSizeBytes);
                            double mb = totalBytes / (1024.0 * 1024.0);
                            string sizeStr = mb < 1000 ? $"{mb:F1} MB" : $"{mb / 1024.0:F2} GB";
                            var latest = g.OrderByDescending(c => c.CreationTime).First();

                            return new GameFolderInfo
                            {
                                GameName = g.Key,
                                ClipCount = g.Count(),
                                TotalSizeBytes = totalBytes,
                                TotalSizeFormatted = sizeStr,
                                LatestClipTime = latest.CreationTime,
                                LatestClipRelative = latest.RelativeTime,
                                FolderPath = Path.GetDirectoryName(latest.FilePath) ?? "",
                                IsAllFolder = false
                            };
                        })
                        .OrderByDescending(g => g.LatestClipTime)
                        .ToList();

                    Dispatch(() =>
                    {
                        ClipsList.Clear();
                        foreach (var item in files)
                        {
                            ClipsList.Add(item);
                        }
                        ClipsCountText = $"{ClipsList.Count} clips guardados";

                        GameFolders.Clear();
                        if (files.Count > 0)
                        {
                            long allBytes = files.Sum(c => c.FileSizeBytes);
                            double allMb = allBytes / (1024.0 * 1024.0);
                            GameFolders.Add(new GameFolderInfo
                            {
                                GameName = "TODOS LOS JUEGOS",
                                ClipCount = files.Count,
                                TotalSizeBytes = allBytes,
                                TotalSizeFormatted = allMb < 1000 ? $"{allMb:F1} MB" : $"{allMb / 1024.0:F2} GB",
                                LatestClipTime = files.First().CreationTime,
                                LatestClipRelative = files.First().RelativeTime,
                                IsAllFolder = true
                            });
                        }

                        foreach (var g in groups)
                        {
                            GameFolders.Add(g);
                        }

                        // If user was already inside a folder, refresh the filtered list
                        if (SelectedGameFolder != null)
                        {
                            var match = GameFolders.FirstOrDefault(gf =>
                                (SelectedGameFolder.IsAllFolder && gf.IsAllFolder) ||
                                (!SelectedGameFolder.IsAllFolder && string.Equals(gf.GameName, SelectedGameFolder.GameName, StringComparison.OrdinalIgnoreCase)));
                            if (match != null)
                            {
                                SelectGameFolder(match);
                            }
                            else
                            {
                                IsInsideGameFolder = false;
                                SelectedGameFolder = null;
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MainViewModel] Error refreshing clips: {ex.Message}");
                }
            });
        }

        public void SelectGameFolder(GameFolderInfo folder)
        {
            SelectedGameFolder = folder;
            IsInsideGameFolder = true;

            FilteredClipsList.Clear();
            var matched = folder.IsAllFolder
                ? ClipsList
                : ClipsList.Where(c => string.Equals(c.GameName, folder.GameName, StringComparison.OrdinalIgnoreCase));

            foreach (var clip in matched)
            {
                FilteredClipsList.Add(clip);
            }

            if (SelectedClip == null || !FilteredClipsList.Any(c => c.FilePath == SelectedClip.FilePath))
            {
                SelectedClip = FilteredClipsList.FirstOrDefault();
            }
        }

        public void BackToGameFolders()
        {
            IsInsideGameFolder = false;
            SelectedGameFolder = null;
        }

        public string LicenseBadgeText
        {
            get
            {
                if (_licenseManager?.CurrentLicense != null)
                {
                    var lic = _licenseManager.CurrentLicense;
                    int days = lic.DaysRemaining;
                    string dayUnit = days == 1 ? "DÍA" : "DÍAS";
                    return $"✦ LICENCIA: ANUAL ({days} {dayUnit})";
                }
                return "✦ LICENCIA: ANUAL";
            }
        }

        public string CurrentHwidText => Ouro.Services.Licensing.HwidService.GetCurrentHwid();

        public string EngineHardwareStatusText => $"ACELERACIÓN: NVIDIA NVENC // BUFFER {_selectedBufferDuration}S EN RAM";

        private void ExecuteRenewLicense()
        {
            if (_licenseManager == null) return;
            var window = new ActivationWindow(_licenseManager);
            bool? res = window.ShowDialog();
            if (res == true)
            {
                OnPropertyChanged(nameof(LicenseBadgeText));
                ShowToast("✦ Licencia actualizada y verificada con éxito.");
            }
        }

        private void ExecutePlayClip()
        {
            if (SelectedClip == null || !File.Exists(SelectedClip.FilePath)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = SelectedClip.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowToast($"Error al reproducir: {ex.Message}");
            }
        }

        private void ExecuteOpenClipFolder()
        {
            if (SelectedClip == null || !File.Exists(SelectedClip.FilePath)) return;
            try
            {
                Process.Start("explorer.exe", $"/select,\"{SelectedClip.FilePath}\"");
            }
            catch (Exception ex)
            {
                ShowToast($"Error al abrir carpeta: {ex.Message}");
            }
        }

        private void ExecuteDeleteClip()
        {
            if (SelectedClip == null) return;
            var result = MessageBox.Show(
                $"¿Estás seguro de eliminar permanentemente este clip?\n\n{SelectedClip.FileName}",
                "Confirmar Eliminación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (File.Exists(SelectedClip.FilePath))
                    {
                        File.Delete(SelectedClip.FilePath);
                    }
                    var toRemove = SelectedClip;
                    ClipsList.Remove(toRemove);
                    FilteredClipsList.Remove(toRemove);
                    SelectedClip = FilteredClipsList.FirstOrDefault();
                    ExecuteRefreshClips();
                    ShowToast("Clip eliminado del disco.");
                }
                catch (Exception ex)
                {
                    ShowToast($"No se pudo eliminar: {ex.Message}");
                }
            }
        }

        private async Task ExecuteShareToDiscordAsync()
        {
            if (SelectedClip == null || !File.Exists(SelectedClip.FilePath))
            {
                ShowToast("Selecciona un clip válido para compartir.");
                return;
            }

            if (string.IsNullOrWhiteSpace(DiscordWebhookUrl))
            {
                DiscordStatusMessage = "⚠️ Por favor ingresa la URL de tu Webhook de Discord arriba.";
                return;
            }

            // Persist webhook url in settings
            if (_settingsManager != null && _settingsManager.Settings.DiscordWebhookUrl != DiscordWebhookUrl)
            {
                _settingsManager.Settings.DiscordWebhookUrl = DiscordWebhookUrl;
                await _settingsManager.SaveSettingsAsync(_settingsManager.Settings).ConfigureAwait(false);
            }

            IsUploadingToDiscord = true;
            DiscordStatusMessage = $"⏳ Optimizando y subiendo clip de {SelectedClip.GameName} a Discord ({SelectedClip.FileSizeFormatted})...";

            var specs = $"{SelectedResolution} @ {SelectedFramerate} FPS";
            var result = await _discordService.ShareClipAsync(DiscordWebhookUrl, SelectedClip.FilePath, SelectedClip.GameName, specs, SelectedDiscordLimit).ConfigureAwait(false);

            Dispatch(() =>
            {
                IsUploadingToDiscord = false;
                if (result.Success)
                {
                    DiscordStatusMessage = $"✅ {result.Message}";
                    ShowToast("🚀 Clip compartido en Discord exitosamente");
                }
                else
                {
                    DiscordStatusMessage = $"❌ {result.Message}";
                    ShowToast($"Fallo al compartir: {result.Message}");
                }
            });
        }

        private async Task ExecuteTestDiscordWebhookAsync()
        {
            if (string.IsNullOrWhiteSpace(DiscordWebhookUrl))
            {
                DiscordStatusMessage = "⚠️ Ingresa la URL del webhook primero.";
                return;
            }

            DiscordStatusMessage = "⏳ Probando conexión con Discord...";
            var result = await _discordService.TestWebhookAsync(DiscordWebhookUrl).ConfigureAwait(false);

            Dispatch(() =>
            {
                if (result.Success)
                {
                    DiscordStatusMessage = $"✅ {result.Message}";
                    ShowToast("Webhook de Discord verificado");
                    if (_settingsManager != null)
                    {
                        _settingsManager.Settings.DiscordWebhookUrl = DiscordWebhookUrl;
                        Task.Run(async () => await _settingsManager.SaveSettingsAsync(_settingsManager.Settings));
                    }
                }
                else
                {
                    DiscordStatusMessage = $"❌ {result.Message}";
                    ShowToast(result.Message);
                }
            });
        }

        private async Task ExecutePostManualAsync()
        {
            string targetUrl = (!string.IsNullOrWhiteSpace(DiscordWebhookUrl) && DiscordWebhookUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                ? DiscordWebhookUrl
                : (_settingsManager?.Settings.DiscordWebhookUrl ?? "");

            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                DiscordStatusMessage = "⚠️ Ingresa la URL del webhook de Discord primero.";
                return;
            }

            DiscordStatusMessage = "⏳ Publicando Manual oficial 'Cómo usar Ouro' en Discord...";
            var result = await _discordService.PostManualAsync(targetUrl).ConfigureAwait(false);

            Dispatch(() =>
            {
                if (result.Success)
                {
                    DiscordStatusMessage = $"✅ {result.Message}";
                    ShowToast("📖 Manual publicado en Discord (¡Listo para fijar!)");
                }
                else
                {
                    DiscordStatusMessage = $"❌ {result.Message}";
                    ShowToast(result.Message);
                }
            });
        }

        private void HandleClipSaveFailed(Exception ex)
        {
            Dispatch(() =>
            {
                StatusText = $"Fallo al guardar: {ex.Message}";
                ShowToast($"Error al guardar: {ex.Message}");
                ClipOverlayWindow.ShowNotification("ERROR AL GUARDAR", ex.Message, "ERROR");
            });
        }

        private void ShowToast(string message)
        {
            _toastCts?.Cancel();
            _toastCts = new CancellationTokenSource();
            var token = _toastCts.Token;

            ToastMessage = message;
            IsToastVisible = true;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3500, token);
                    if (!token.IsCancellationRequested)
                    {
                        Dispatch(() => IsToastVisible = false);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Ignore cancellation
                }
            }, token);
        }
    }
}
