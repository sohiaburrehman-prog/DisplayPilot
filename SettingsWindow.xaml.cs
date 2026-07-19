using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Win32;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace PrimaryDisplaySwap;

public partial class SettingsWindow : Window
{
    private enum CaptureTarget { None, OpenPanel, Cycle }

    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly DisplayManager? _displayManager;
    private readonly Action? _runWizard;
    private readonly Action? _openProfileManager;
    private readonly Action? _openLog;
    private readonly Func<HotkeyConfig, HotkeyConfig, HotkeyApplyResult>? _validateHotkeys;

    private CaptureTarget _capturing = CaptureTarget.None;
    private bool _suppressToggleEvents;
    private UpdateInfo? _availableUpdate;
    private bool _displaysPopulated;

    public SettingsWindow(
        SettingsService settings,
        StartupService startup,
        Action? runWizard = null,
        Action? openProfileManager = null,
        Action? openLog = null,
        Func<HotkeyConfig, HotkeyConfig, HotkeyApplyResult>? validateHotkeys = null,
        DisplayManager? displayManager = null)
    {
        _settings = settings;
        _startup = startup;
        _displayManager = displayManager;
        _runWizard = runWizard;
        _openProfileManager = openProfileManager;
        _openLog = openLog;
        _validateHotkeys = validateHotkeys;

        InitializeComponent();

        PreviewKeyDown += OnPreviewKeyDown;
        ContentRendered += OnContentRendered;

        LoadFromSettings();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        var maximum = Math.Max(420, SystemParameters.WorkArea.Height - 32);
        MinHeight = Math.Min(MinHeight, maximum);
        MaxHeight = maximum;
        Height = Math.Min(Height, maximum);
    }

    public void LoadFromSettings()
    {
        _suppressToggleEvents = true;

        try
        {
            StartupCheck.IsChecked = _startup.IsEnabled;
        }
        catch (Exception ex)
        {
            StartupCheck.IsChecked = false;
            AppLogger.Log($"Could not read startup setting: {ex.Message}");
        }

        OpenPanelCapture.Content = HotkeyService.Describe(_settings.Current.OpenPanelHotkey);
        CycleEnabledCheck.IsChecked = _settings.Current.CyclePrimaryHotkey.Enabled;
        CycleCapture.Content = HotkeyService.Describe(_settings.Current.CyclePrimaryHotkey);
        CycleCapture.IsEnabled = _settings.Current.CyclePrimaryHotkey.Enabled;
        AutoUpdateCheck.IsChecked = _settings.Current.AutoUpdateCheckEnabled;
        ThemeCombo.SelectedIndex = (int)_settings.Current.Theme;
        CompactTrayMenuCheck.IsChecked = _settings.Current.CompactTrayMenu;

        _suppressToggleEvents = false;
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
        {
            return;
        }

        var enabled = StartupCheck.IsChecked == true;
        try
        {
            _startup.SetEnabled(enabled);
            SetStatus(enabled ? "DisplayPilot will start with Windows." : "Windows startup disabled.");
        }
        catch (Exception ex)
        {
            _suppressToggleEvents = true;
            StartupCheck.IsChecked = !enabled;
            _suppressToggleEvents = false;
            AppLogger.Log($"Could not change startup setting: {ex.Message}");
            MessageBox.Show(ex.Message, "Could not change startup setting", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressToggleEvents)
        {
            return;
        }

        var choice = (ThemePreference)Math.Clamp(ThemeCombo.SelectedIndex, 0, 2);
        _settings.Update(s => s.Theme = choice);
        // App subscribes to settings changes and re-applies the theme live.
    }

    private void CompactTrayMenu_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
        {
            return;
        }

        var compact = CompactTrayMenuCheck.IsChecked == true;
        _settings.Update(s => s.CompactTrayMenu = compact);
        SetStatus(compact
            ? "Compact tray menu enabled."
            : "Dense tray menu restored.");
    }

    private void OpenProfileManager_Click(object sender, RoutedEventArgs e) =>
        _openProfileManager?.Invoke();

    // ─────────────────────── Hotkey capture ───────────────────────

    private void CaptureOpenPanel_Click(object sender, RoutedEventArgs e) =>
        BeginCapture(CaptureTarget.OpenPanel, OpenPanelCapture);

    private void CaptureCycle_Click(object sender, RoutedEventArgs e) =>
        BeginCapture(CaptureTarget.Cycle, CycleCapture);

    private void BeginCapture(CaptureTarget target, Button button)
    {
        _capturing = target;
        button.Content = "Press keys…";
        CaptureHint.Text = "Press a shortcut with at least one modifier (Ctrl, Alt, Shift or Win). Esc cancels.";
        CaptureHint.Visibility = Visibility.Visible;
        button.Focus();
    }

    private void CancelCapture()
    {
        _capturing = CaptureTarget.None;
        CaptureHint.Visibility = Visibility.Collapsed;
        OpenPanelCapture.Content = HotkeyService.Describe(_settings.Current.OpenPanelHotkey);
        CycleCapture.Content = HotkeyService.Describe(_settings.Current.CyclePrimaryHotkey);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing == CaptureTarget.None)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        var modifiers = ToWin32Modifiers(Keyboard.Modifiers);
        if (modifiers == 0)
        {
            CaptureHint.Text = "Use at least one modifier (Ctrl, Alt, Shift or Win).";
            return;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            return;
        }

        var newHotkey = new HotkeyConfig { Modifiers = modifiers, Key = vk, Enabled = true };

        if (_capturing == CaptureTarget.OpenPanel)
        {
            if (newHotkey.Matches(_settings.Current.CyclePrimaryHotkey) &&
                _settings.Current.CyclePrimaryHotkey.IsBound)
            {
                CaptureHint.Text = "That shortcut is already used by Cycle primary display.";
                return;
            }

            if (!ValidateHotkeys(newHotkey, _settings.Current.CyclePrimaryHotkey))
            {
                return;
            }

            if (!_settings.Update(s => s.OpenPanelHotkey = newHotkey))
            {
                _validateHotkeys?.Invoke(
                    _settings.Current.OpenPanelHotkey.Clone(),
                    _settings.Current.CyclePrimaryHotkey.Clone());
                return;
            }
        }
        else if (_capturing == CaptureTarget.Cycle)
        {
            if (newHotkey.Matches(_settings.Current.OpenPanelHotkey) &&
                _settings.Current.OpenPanelHotkey.IsBound)
            {
                CaptureHint.Text = "That shortcut is already used by Open control panel.";
                return;
            }

            if (!ValidateHotkeys(_settings.Current.OpenPanelHotkey, newHotkey))
            {
                return;
            }

            if (!_settings.Update(s =>
            {
                s.CyclePrimaryHotkey = newHotkey;
                s.CyclePrimaryHotkey.Enabled = true;
            }))
            {
                _validateHotkeys?.Invoke(
                    _settings.Current.OpenPanelHotkey.Clone(),
                    _settings.Current.CyclePrimaryHotkey.Clone());
                return;
            }
            _suppressToggleEvents = true;
            CycleEnabledCheck.IsChecked = true;
            _suppressToggleEvents = false;
        }

        _capturing = CaptureTarget.None;
        CaptureHint.Visibility = Visibility.Collapsed;
        LoadFromSettings();
        SetStatus("Hotkey updated.");
    }

    private void CycleEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
        {
            return;
        }

        var enabled = CycleEnabledCheck.IsChecked == true;
        var candidate = _settings.Current.CyclePrimaryHotkey.Clone();
        candidate.Enabled = enabled;
        if (!ValidateHotkeys(_settings.Current.OpenPanelHotkey, candidate))
        {
            _suppressToggleEvents = true;
            CycleEnabledCheck.IsChecked = !enabled;
            _suppressToggleEvents = false;
            return;
        }

        if (!_settings.Update(s => s.CyclePrimaryHotkey.Enabled = enabled))
        {
            _validateHotkeys?.Invoke(
                _settings.Current.OpenPanelHotkey.Clone(),
                _settings.Current.CyclePrimaryHotkey.Clone());
            _suppressToggleEvents = true;
            CycleEnabledCheck.IsChecked = !enabled;
            _suppressToggleEvents = false;
            return;
        }
        CycleCapture.IsEnabled = enabled;

        if (enabled && !_settings.Current.CyclePrimaryHotkey.IsBound)
        {
            SetStatus("Now set a shortcut for Cycle primary display.");
        }
    }

    private bool ValidateHotkeys(HotkeyConfig openPanel, HotkeyConfig cycle)
    {
        if (_validateHotkeys is null)
        {
            return true;
        }

        var result = _validateHotkeys(openPanel.Clone(), cycle.Clone());
        if (!result.HasFailure)
        {
            return true;
        }

        ShowHotkeyFailure(result.FailureMessage);
        return false;
    }

    public void ShowHotkeyFailure(string message)
    {
        NavList.SelectedIndex = 1;
        CaptureHint.Text = message;
        CaptureHint.Visibility = Visibility.Visible;
        SetStatus(message);
    }

    // ─────────────────────── Sidebar navigation ───────────────────────

    private void Nav_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Pages array order matches the sidebar items.
        var pages = new UIElement?[] { PageGeneral, PageShortcuts, PageDisplays, PageAutomation, PageSupport };
        var index = Math.Clamp(NavList.SelectedIndex, 0, pages.Length - 1);
        for (var i = 0; i < pages.Length; i++)
        {
            if (pages[i] is not null)
            {
                pages[i]!.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Monitor enumeration costs a few Win32 calls; only do it when the
        // Displays page is first shown (Refresh re-runs it on demand).
        if (index == 2 && !_displaysPopulated)
        {
            PopulateDisplays();
        }
    }

    // ─────────────────────── Displays page ───────────────────────

    private void RefreshDisplays_Click(object sender, RoutedEventArgs e) => PopulateDisplays();

    private void PopulateDisplays()
    {
        _displaysPopulated = true;
        DisplaysPanel.Children.Clear();

        if (_displayManager is null)
        {
            DisplaysPanel.Children.Add(MutedText("Display information is not available in this window."));
            return;
        }

        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = _displayManager.GetMonitors();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Settings displays page: monitor enumeration failed: {ex.Message}");
            DisplaysPanel.Children.Add(MutedText("Could not enumerate displays. See the activity log."));
            return;
        }

        foreach (var monitor in monitors)
        {
            DisplaysPanel.Children.Add(BuildDisplayCard(monitor));
        }
    }

    private UIElement BuildDisplayCard(MonitorInfo monitor)
    {
        var deviceName = monitor.DeviceName;

        var content = new StackPanel();

        // Header: numbered friendly name + primary badge, specs underneath.
        var title = new TextBlock
        {
            Text = MonitorDisplayHelper.GetNumberedName(monitor, _settings.Current) +
                   (monitor.IsPrimary ? "  ·  primary" : string.Empty),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
        };
        var specs = new TextBlock
        {
            Text = $"{monitor.SpecsLabel}  ·  {deviceName}",
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 10),
        };
        content.Children.Add(title);
        content.Children.Add(specs);

        // Nickname row.
        var nicknameGrid = new Grid();
        nicknameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nicknameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nicknameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nicknameLabel = new TextBlock
        {
            Text = "Nickname",
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 0, 10, 0),
        };
        var nicknameBox = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            VerticalAlignment = VerticalAlignment.Center,
            Text = _settings.Current.MonitorNicknames.TryGetValue(deviceName, out var nick) ? nick : string.Empty,
        };
        var saveButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Save",
            Width = 64,
            Margin = new Thickness(8, 0, 0, 0),
        };
        saveButton.Click += (_, _) =>
        {
            var value = nicknameBox.Text.Trim();
            _settings.Update(s =>
            {
                if (string.IsNullOrEmpty(value))
                {
                    s.MonitorNicknames.Remove(deviceName);
                }
                else
                {
                    s.MonitorNicknames[deviceName] = value;
                }
            });
            SetStatus(string.IsNullOrEmpty(value)
                ? $"Nickname cleared for {monitor.Name}."
                : $"{monitor.Name} is now \"{value}\".");
            PopulateDisplays();
        };

        Grid.SetColumn(nicknameLabel, 0);
        Grid.SetColumn(nicknameBox, 1);
        Grid.SetColumn(saveButton, 2);
        nicknameGrid.Children.Add(nicknameLabel);
        nicknameGrid.Children.Add(nicknameBox);
        nicknameGrid.Children.Add(saveButton);
        content.Children.Add(nicknameGrid);

        // HDR toggle, shown only when the display reports support.
        var hdr = TryGetHdrStatus(deviceName);
        if (hdr is { Supported: true })
        {
            var hdrCheck = new CheckBox
            {
                Style = (Style)FindResource("DarkCheckBox"),
                Content = "HDR",
                IsChecked = hdr.Enabled,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush"),
            };
            hdrCheck.Click += async (_, _) =>
            {
                var enable = hdrCheck.IsChecked == true;
                try
                {
                    hdrCheck.IsEnabled = false;
                    await System.Threading.Tasks.Task.Run(() => _displayManager!.SetHdrEnabled(deviceName, enable));
                    SetStatus($"{MonitorDisplayHelper.GetDisplayName(monitor, _settings.Current)}: HDR {(enable ? "on" : "off")}.");
                }
                catch (Exception ex)
                {
                    hdrCheck.IsChecked = !enable;
                    SetStatus(ex.Message);
                }
                finally
                {
                    hdrCheck.IsEnabled = true;
                }
            };
            content.Children.Add(hdrCheck);
        }

        return new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("MapSurfaceBrush"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = content,
        };
    }

    private DisplayManager.HdrStatus? TryGetHdrStatus(string deviceName)
    {
        try
        {
            return _displayManager?.GetHdrStatus(deviceName);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"HDR status lookup failed for {deviceName}: {ex.Message}");
            return null;
        }
    }

    private TextBlock MutedText(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
    };

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin or
        Key.System;

    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if ((modifiers & ModifierKeys.Control) != 0) result |= AppSettings.ModControl;
        if ((modifiers & ModifierKeys.Alt) != 0) result |= AppSettings.ModAlt;
        if ((modifiers & ModifierKeys.Shift) != 0) result |= AppSettings.ModShift;
        if ((modifiers & ModifierKeys.Windows) != 0) result |= AppSettings.ModWin;
        return result;
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export DisplayPilot settings",
            Filter = "JSON settings (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"DisplayPilot-settings-v{AppSettings.CurrentSchemaVersion}.json",
            DefaultExt = ".json",
            AddExtension = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = _settings.ExportToJson();
            File.WriteAllText(dialog.FileName, json);
            AppLogger.Log($"Settings exported to {dialog.FileName}.");
            SetStatus("Settings exported.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Settings export failed: {ex.Message}");
            MessageBox.Show(
                $"Could not export settings:\n{ex.Message}",
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import DisplayPilot settings",
            Filter = "JSON settings (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            if (!SettingsService.TryParseImport(json, out var imported, out var error))
            {
                AppLogger.Log($"Settings import rejected: {error}");
                MessageBox.Show(
                    error ?? "The selected file is not valid DisplayPilot settings.",
                    "Import failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!ValidateHotkeys(imported!.OpenPanelHotkey, imported.CyclePrimaryHotkey))
            {
                MessageBox.Show(
                    "The imported shortcuts could not be registered. Your current settings were left unchanged.",
                    "Import blocked",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Replace your current settings with the imported file?\n\n" +
                $"Profiles: {imported!.Profiles.Count}\n" +
                $"Schema version: {imported.SchemaVersion}\n\n" +
                "Your current settings.json will be backed up to settings.json.bak first.",
                "Import settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            if (!_settings.BackupCurrentSettings())
            {
                var proceed = MessageBox.Show(
                    "Could not back up the current settings file. Import anyway?",
                    "Backup failed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (proceed != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            if (!_settings.ImportReplace(imported))
            {
                MessageBox.Show(
                    "Import could not be applied.",
                    "Import failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            LoadFromSettings();
            SetStatus("Settings imported.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Settings import failed: {ex.Message}");
            MessageBox.Show(
                $"Could not import settings:\n{ex.Message}",
                "Import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RunWizard_Click(object sender, RoutedEventArgs e)
    {
        if (_runWizard is null)
        {
            MessageBox.Show(
                "Setup wizard is not available from this window.",
                AppInfo.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _runWizard();
    }

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvents)
        {
            return;
        }

        var enabled = AutoUpdateCheck.IsChecked == true;
        _settings.Update(s => s.AutoUpdateCheckEnabled = enabled);
    }

    private async void CheckNow_Click(object sender, RoutedEventArgs e)
    {
        CheckNowButton.IsEnabled = false;
        OpenUpdateButton.Visibility = Visibility.Collapsed;
        _availableUpdate = null;
        UpdateStatusText.Text = "Checking…";

        try
        {
            var service = new UpdateService();
            var info = await service.CheckForUpdateAsync();

            if (info is null)
            {
                UpdateStatusText.Text = "Could not reach GitHub. Try again later.";
            }
            else
            {
                _settings.Update(s => s.LastUpdateCheckUtc = DateTime.UtcNow);
                if (info.UpdateAvailable)
                {
                    _availableUpdate = info;
                    UpdateStatusText.Text = $"{info.LatestTag} is available.";
                    OpenUpdateButton.Visibility = Visibility.Visible;
                }
                else
                {
                    UpdateStatusText.Text = $"You're up to date (v{AppInfo.AppVersion}).";
                }
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text = "Update check failed.";
            AppLogger.Log($"Settings check-now failed: {ex.Message}");
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void OpenUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
        {
            OpenUrl(_availableUpdate.ReleaseUrl);
        }
    }

    private static void OpenUrl(string url) => UrlLaunchHelper.TryOpenWebUrl(url);

    private void OpenLog_Click(object sender, RoutedEventArgs e) => _openLog?.Invoke();

    private void SetStatus(string message) => StatusText.Text = message;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
