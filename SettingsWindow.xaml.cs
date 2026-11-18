using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsNotch.Rewards;

namespace WindowsNotch
{
    public partial class SettingsWindow : Window
    {
        private static readonly Brush MutedBrush = FrozenBrushes.Create(0x6E, 0x6E, 0x73);
        private static readonly Brush ErrorBrush = FrozenBrushes.Create(0xFF, 0x6B, 0x6B);
        private static readonly Brush AccentBrush = FrozenBrushes.Create(0x3D, 0x7E, 0xFF);

        private readonly AppSettings _settings;
        private bool _isReady;
        private bool _isClosing;
        private bool _initializing = true;
        private HotkeyTarget _recordingTarget = HotkeyTarget.None;
        private bool _isUpdatingVolume;
        private readonly AudioVolumeService _volume = new();
        private readonly DispatcherTimer _volumeTimer;

        private enum HotkeyTarget
        {
            None,
            Main,
            Focus
        }

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();

            _settings = settings;

            AutoStartToggle.IsChecked = StartupService.IsEnabled();
            _settings.AutoStart = AutoStartToggle.IsChecked == true;

            string language = Localization.Normalize(settings.Language);
            _settings.Language = language;
            IndonesianOption.IsChecked = language == "id";
            EnglishOption.IsChecked = language == "en";

            // Tampilkan hotkey saat ini
            HotkeyLabel.Text = FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
            FocusHotkeyLabel.Text = FormatHotkey(
                _settings.FocusHotkeyModifiers, _settings.FocusHotkeyKey);
            EnableLyricsToggle.IsChecked = _settings.EnableLyrics;
            HideNotchToggle.IsChecked = _settings.HideNotchInFocus;

            // Notchi Mobile settings
            MobileServerToggle.IsChecked = _settings.IsMobileServerEnabled;
            MobilePortBox.Text = _settings.MobileServerPort.ToString();
            MobileMaxFileSizeBox.Text = _settings.MobileMaxFileSizeMb.ToString();
            RefreshDevicesList();

            AutoStartToggle.Click += OnAutoStartToggleChanged;
            IndonesianOption.Checked += OnLanguageChanged;
            EnglishOption.Checked += OnLanguageChanged;
            MobileServerToggle.Click += OnMobileServerToggleChanged;
            MobilePortBox.LostFocus += OnMobilePortLostFocus;
            MobileMaxFileSizeBox.LostFocus += OnMobileMaxFileSizeLostFocus;
            PairDeviceButton.Click += OnPairDeviceButtonClick;
            GeneralTabRadio.Checked += (s, e) => SwitchTab(SettingsTab.General);
            MobileTabRadio.Checked += (s, e) => SwitchTab(SettingsTab.Mobile);
            RewardTabRadio.Checked += (s, e) => SwitchTab(SettingsTab.Reward);

            RedeemButton.Click += async (s, e) => await OnRedeemClickAsync();
            UseRewardBgToggle.Click += OnUseRewardBgToggleChanged;
            ReleaseDeviceButton.Click += async (s, e) => await OnReleaseDeviceClickAsync();
            RefreshRewardUi();

            // Volume sistem
            _volumeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _volumeTimer.Tick += OnVolumeTimerTick;
            UpdateVolumeUi(_volume.Volume, _volume.IsMuted);
            VolumeSlider.ValueChanged += OnVolumeSliderChanged;
            MuteToggle.Click += OnMuteToggleClick;
            _volumeTimer.Start();

            CloseButton.Click += (s, e) => CloseFromUser();

            var headerDrag = new WindowDragHelper(this, HeaderRow);
            headerDrag.DragEnded += (s, e) =>
            {
                _settings.SettingsLeft = Left;
                _settings.SettingsTop = Top;
                SettingsStore.Save(_settings);
            };

            var borderDrag = new WindowDragHelper(this, CardBorder, UIHelper.IsInteractive);
            borderDrag.DragEnded += (s, e) =>
            {
                _settings.SettingsLeft = Left;
                _settings.SettingsTop = Top;
                SettingsStore.Save(_settings);
            };

            HotkeyRecorder.MouseLeftButtonDown += OnHotkeyRecorderClick;
            FocusHotkeyRecorder.MouseLeftButtonDown += OnFocusHotkeyRecorderClick;
            EnableLyricsToggle.Click += OnEnableLyricsChanged;
            HideNotchToggle.Click += OnHideNotchChanged;

            Activated += (s, e) => _isReady = true;
            Deactivated += OnDeactivated;
            Closed += (s, e) =>
            {
                _isClosing = true;
                _isReady = false;
                _volumeTimer.Stop();
                _volumeTimer.Tick -= OnVolumeTimerTick;
                _volume.Dispose();
            };

            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;

            Loaded += (s, e) => Activate();

            _initializing = false;
        }

        private enum SettingsTab { General, Mobile, Reward }

        private void SwitchTab(SettingsTab tab)
        {
            if (GeneralTabContent == null || MobileTabContent == null || RewardTabContent == null) return;
            GeneralTabContent.Visibility = tab == SettingsTab.General ? Visibility.Visible : Visibility.Collapsed;
            MobileTabContent.Visibility = tab == SettingsTab.Mobile ? Visibility.Visible : Visibility.Collapsed;
            RewardTabContent.Visibility = tab == SettingsTab.Reward ? Visibility.Visible : Visibility.Collapsed;
        }

        // ===================== Klaim Reward =====================

        private static string FormatBadgeName(string? badgeId)
        {
            if (string.IsNullOrWhiteSpace(badgeId)) return "Founding Member";
            string clean = badgeId.StartsWith("badge_", StringComparison.OrdinalIgnoreCase)
                ? badgeId.Substring(6)
                : badgeId;
            clean = clean.Replace('_', ' ');
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clean);
        }

        private void RefreshRewardUi()
        {
            var stored = EntitlementStore.Load();
            bool hasActive = stored?.Payload != null;

            ActiveRewardPanel.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;

            if (hasActive && stored!.Payload != null)
            {
                var p = stored.Payload;
                _settings.ClaimNumber = p.ClaimNumber;
                _settings.RewardBgId = p.BgId;
                _settings.RewardBadgeId = p.BadgeId;
                _settings.RewardRarity = p.Rarity;
                _settings.RedeemedCode = p.Code;

                ActiveRewardBadgeText.Text = FormatBadgeName(p.BadgeId);
                ActiveRewardClaimNumberText.Text = $"#{p.ClaimNumber:D3}";
                string rarity = string.IsNullOrWhiteSpace(p.Rarity) ? "Common" : char.ToUpper(p.Rarity[0]) + p.Rarity.Substring(1);
                ActiveRewardRarityText.Text = $"Rarity: {rarity} · Background: {p.BgId}";

                // Muat pratinjau gambar background compact
                if (!string.IsNullOrWhiteSpace(p.BgId) && RewardAssetCache.HasAssets(p.BgId))
                {
                    string compactPath = RewardAssetCache.GetCompactPath(p.BgId);
                    ActiveRewardCompactImage.Source = RewardAssetCache.LoadBitmap(compactPath);
                }
                else
                {
                    ActiveRewardCompactImage.Source = null;
                }

                UseRewardBgToggle.IsChecked = _settings.UseRewardBackground;
            }
        }

        private async Task OnRedeemClickAsync()
        {
            string code = RewardCodeInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                SetRewardStatus("Masukkan kode klaim terlebih dahulu.", isError: true);
                return;
            }

            RedeemButton.IsEnabled = false;
            RewardCodeInput.IsEnabled = false;
            SetRewardStatus(Localization.Get("Redeeming") ?? "Memproses...", isError: false);

            var result = await RedeemService.RedeemCodeAsync(code);

            RedeemButton.IsEnabled = true;
            RewardCodeInput.IsEnabled = true;

            if (result.Success && result.Payload != null)
            {
                RewardCodeInput.Text = string.Empty;
                SetRewardStatus(result.Message ?? "Klaim berhasil!", isError: false);

                _settings.UseRewardBackground = true;
                _settings.RedeemedCode = result.Payload.Code;
                _settings.ClaimNumber = result.Payload.ClaimNumber;
                _settings.RewardBgId = result.Payload.BgId;
                _settings.RewardBadgeId = result.Payload.BadgeId;
                _settings.RewardRarity = result.Payload.Rarity;
                SettingsStore.Save(_settings);

                RefreshRewardUi();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SetRewardStatus(result.Message ?? "Gagal meredeem kode.", isError: true);
            }
        }

        private void OnUseRewardBgToggleChanged(object? sender, RoutedEventArgs e)
        {
            _settings.UseRewardBackground = UseRewardBgToggle.IsChecked == true;
            SettingsStore.Save(_settings);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task OnReleaseDeviceClickAsync()
        {
            string code = _settings.RedeemedCode ?? string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                var stored = EntitlementStore.Load();
                code = stored?.Payload?.Code ?? string.Empty;
            }

            ReleaseDeviceButton.IsEnabled = false;
            SetRewardStatus(Localization.Get("ReleasingDevice") ?? "Melepas perangkat...", isError: false);

            var result = await RedeemService.ReleaseDeviceAsync(code);

            ReleaseDeviceButton.IsEnabled = true;

            // Clear local settings
            if (!string.IsNullOrWhiteSpace(_settings.RewardBgId))
            {
                RewardAssetCache.ClearAssets(_settings.RewardBgId);
            }

            _settings.RedeemedCode = null;
            _settings.ClaimNumber = 0;
            _settings.RewardBgId = null;
            _settings.RewardBadgeId = null;
            _settings.RewardRarity = null;
            SettingsStore.Save(_settings);

            RefreshRewardUi();
            SetRewardStatus(result.Message ?? "Perangkat telah dilepas.", isError: !result.Success);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetRewardStatus(string message, bool isError)
        {
            RewardStatusText.Text = message;
            RewardStatusText.Foreground = isError ? ErrorBrush : MutedBrush;
            RewardStatusText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        // ===================== Volume sistem =====================

        private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingVolume) return;
            int percent = (int)Math.Round(VolumeSlider.Value);
            VolumeValueText.Text = $"{percent}%";
            _volume.Volume = (float)(VolumeSlider.Value / 100.0);

            // Menggeser slider otomatis mengaktifkan suara (unmute).
            if (_volume.IsMuted)
            {
                _volume.IsMuted = false;
                MuteToggle.IsChecked = true;
                UpdateMuteTooltip();
            }
        }

        private void OnMuteToggleClick(object sender, RoutedEventArgs e)
        {
            _volume.IsMuted = MuteToggle.IsChecked != true;
            UpdateMuteTooltip();
        }

        private void OnVolumeTimerTick(object? sender, EventArgs e)
        {
            if (_isUpdatingVolume) return;
            if (VolumeSlider.IsMouseCaptureWithin) return;
            UpdateVolumeUi(_volume.Volume, _volume.IsMuted);
        }

        private void UpdateVolumeUi(float volume, bool muted)
        {
            if (_isUpdatingVolume) return;
            _isUpdatingVolume = true;
            try
            {
                VolumeSlider.Value = volume * 100.0;
                VolumeValueText.Text = $"{(int)Math.Round(volume * 100.0)}%";
                MuteToggle.IsChecked = !muted;
                UpdateMuteTooltip();
            }
            finally
            {
                _isUpdatingVolume = false;
            }
        }

        private void UpdateMuteTooltip()
        {
            MuteToggle.ToolTip = MuteToggle.IsChecked == true
                ? Localization.Get("VolumeMute")
                : Localization.Get("VolumeUnmute");
        }

        public event EventHandler? SettingsChanged;

        private void RefreshDevicesList()
        {
            var devices = DeviceStore.Load();
            DevicesItemsControl.ItemsSource = null;
            DevicesItemsControl.ItemsSource = devices;
            NoDevicesLabel.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnMobileServerToggleChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            bool enabled = MobileServerToggle.IsChecked == true;
            _settings.IsMobileServerEnabled = enabled;
            SaveSettings();
        }

        private void OnMobilePortLostFocus(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(MobilePortBox.Text, out int port) && port >= 1024 && port <= 65535)
            {
                if (_settings.MobileServerPort != port)
                {
                    _settings.MobileServerPort = port;
                    SaveSettings();
                }
            }
            else
            {
                MobilePortBox.Text = _settings.MobileServerPort.ToString();
            }
        }

        private void OnMobileMaxFileSizeLostFocus(object sender, RoutedEventArgs e)
        {
            if (long.TryParse(MobileMaxFileSizeBox.Text, out long mb) && mb > 0)
            {
                if (_settings.MobileMaxFileSizeMb != mb)
                {
                    _settings.MobileMaxFileSizeMb = mb;
                    SaveSettings();
                }
            }
            else
            {
                MobileMaxFileSizeBox.Text = _settings.MobileMaxFileSizeMb.ToString();
            }
        }

        private void OnPairDeviceButtonClick(object sender, RoutedEventArgs e)
        {
            if (!_settings.IsMobileServerEnabled)
            {
                SetStatus("Aktifkan server terlebih dahulu", isError: true);
                return;
            }

            var qrWindow = new PairingQrWindow(_settings.MobileServerPort, _settings.PcId ?? Guid.NewGuid().ToString());
            qrWindow.Show();
        }

        private void OnRevokeDeviceClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string deviceId)
            {
                DeviceStore.RevokeDevice(deviceId);
                MobileServerService.Instance?.DisconnectDevice(deviceId);
                RefreshDevicesList();
                SetStatus("Perangkat dicabut", isError: false);
            }
        }

        private void OnHotkeyRecorderClick(object sender, MouseButtonEventArgs e)
        {
            BeginRecording(HotkeyTarget.Main);
            e.Handled = true;
        }

        private void OnFocusHotkeyRecorderClick(object sender, MouseButtonEventArgs e)
        {
            BeginRecording(HotkeyTarget.Focus);
            e.Handled = true;
        }

        private void BeginRecording(HotkeyTarget target)
        {
            if (_recordingTarget != HotkeyTarget.None) return;

            _recordingTarget = target;
            ResetRecorderVisuals();

            if (target == HotkeyTarget.Main)
            {
                HotkeyLabel.Text = Localization.Get("HotkeyRecording");
                HotkeyRecorder.BorderBrush = AccentBrush;
            }
            else
            {
                FocusHotkeyLabel.Text = Localization.Get("HotkeyRecording");
                FocusHotkeyRecorder.BorderBrush = AccentBrush;
            }
        }

        private void CancelRecording()
        {
            if (_recordingTarget == HotkeyTarget.None) return;

            _recordingTarget = HotkeyTarget.None;
            ResetRecorderVisuals();
        }

        private void ResetRecorderVisuals()
        {
            HotkeyLabel.Text = FormatHotkey(
                _settings.HotkeyModifiers, _settings.HotkeyKey);
            FocusHotkeyLabel.Text = FormatHotkey(
                _settings.FocusHotkeyModifiers, _settings.FocusHotkeyKey);

            var idleBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x36));
            HotkeyRecorder.BorderBrush = idleBrush;
            FocusHotkeyRecorder.BorderBrush = idleBrush;
        }

        private void OnEnableLyricsChanged(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            _settings.EnableLyrics = EnableLyricsToggle.IsChecked == true;
            SaveSettings();
        }

        private void OnHideNotchChanged(object sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            _settings.HideNotchInFocus = HideNotchToggle.IsChecked == true;
            SaveSettings();
        }

        private void OnAutoStartToggleChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            bool enabled = AutoStartToggle.IsChecked == true;

            if (!StartupService.SetEnabled(enabled))
            {
                _initializing = true;
                AutoStartToggle.IsChecked = !enabled;
                _initializing = false;
                SetStatus(Localization.Get("AutostartFailed"), isError: true);
                return;
            }

            _settings.AutoStart = enabled;
            SaveSettings();
        }

        private void OnLanguageChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            string language = EnglishOption.IsChecked == true ? "en" : "id";
            if (language == _settings.Language) return;

            _settings.Language = language;
            Localization.Apply(language);
            SaveSettings();
        }

        private void SaveSettings()
        {
            bool saved = SettingsStore.Save(_settings);
            SetStatus(
                Localization.Get(saved ? "Saved" : "SaveFailed"),
                isError: !saved);

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? ErrorBrush : MutedBrush;
        }

        private void CloseFromUser()
        {
            RequestClose();
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            if (!_isReady || _isClosing || !IsVisible) return;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            // Batalkan recording saat window kehilangan fokus
            if (_recordingTarget != HotkeyTarget.None)
            {
                CancelRecording();
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_isClosing || !IsVisible || IsActive) return;
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                RequestClose();
            }));
        }

        private void RequestClose()
        {
            if (_isClosing) return;

            _isClosing = true;

            if (!IsLoaded ||
                Dispatcher.HasShutdownStarted ||
                Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
                // The window is already in its closing sequence.
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_recordingTarget != HotkeyTarget.None)
            {
                HandleHotkeyRecording(e);
                return;
            }

            if (e.Key != Key.Escape) return;

            CloseFromUser();
            e.Handled = true;
        }

        private void HandleHotkeyRecording(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelRecording();
                e.Handled = true;
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or
                       Key.LeftShift or Key.RightShift or
                       Key.LeftAlt or Key.RightAlt or
                       Key.LWin or Key.RWin)
            {
                return;
            }

            uint mods = 0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= 4;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= 1;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= 8;

            if (mods == 0)
            {
                e.Handled = true;
                return;
            }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            HotkeyTarget target = _recordingTarget;

            if (target == HotkeyTarget.Main &&
                mods == _settings.FocusHotkeyModifiers &&
                vk == _settings.FocusHotkeyKey)
            {
                SetStatus(Localization.Get("HotkeyConflict"), isError: true);
                e.Handled = true;
                return;
            }

            if (target == HotkeyTarget.Focus &&
                mods == _settings.HotkeyModifiers &&
                vk == _settings.HotkeyKey)
            {
                SetStatus(Localization.Get("HotkeyConflict"), isError: true);
                e.Handled = true;
                return;
            }

            if (target == HotkeyTarget.Main)
            {
                _settings.HotkeyModifiers = mods;
                _settings.HotkeyKey = vk;
            }
            else if (target == HotkeyTarget.Focus)
            {
                _settings.FocusHotkeyModifiers = mods;
                _settings.FocusHotkeyKey = vk;
            }

            _recordingTarget = HotkeyTarget.None;
            ResetRecorderVisuals();

            SaveSettings();
            e.Handled = true;
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
        }

        /// <summary>
        /// Format kombinasi hotkey menjadi string yang mudah dibaca.
        /// </summary>
        internal static string FormatHotkey(uint modifiers, uint vk)
        {
            var parts = new List<string>();
            if ((modifiers & 8) != 0) parts.Add("Win");
            if ((modifiers & 2) != 0) parts.Add("Ctrl");
            if ((modifiers & 4) != 0) parts.Add("Shift");
            if ((modifiers & 1) != 0) parts.Add("Alt");

            if (vk != 0)
            {
                var key = KeyInterop.KeyFromVirtualKey((int)vk);
                string keyName = key switch
                {
                    Key.Space => "Space",
                    Key.Return => "Enter",
                    Key.Back => "Backspace",
                    _ => key.ToString()
                };
                parts.Add(keyName);
            }

            return parts.Count > 0 ? string.Join("+", parts) : "—";
        }

    }
}
