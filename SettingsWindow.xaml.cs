using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Microsoft.Win32;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace PrimaryDisplaySwap;

public partial class SettingsWindow : Window
{
    private enum CaptureTarget { None, OpenPanel, Cycle }

    private readonly SettingsService _settings;
    private readonly Action? _runWizard;
    private readonly Action? _openProfileManager;

    private CaptureTarget _capturing = CaptureTarget.None;
    private bool _suppressToggleEvents;

    public SettingsWindow(
        SettingsService settings,
        Action? runWizard = null,
        Action? openProfileManager = null)
    {
        _settings = settings;
        _runWizard = runWizard;
        _openProfileManager = openProfileManager;

        InitializeComponent();

        PreviewKeyDown += OnPreviewKeyDown;
        ContentRendered += OnContentRendered;

        LoadFromSettings();
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;
        FitHeightToContent();
    }

    /// <summary>
    /// Expand to fit all sections at open so the ScrollViewer stays hidden at default
    /// size (100%/150% DPI). Scrollbar appears only if the user resizes below MinHeight.
    /// </summary>
    private void FitHeightToContent()
    {
        const double scrollVerticalMargin = 20; // ScrollViewer Margin 12 + 8
        const double layoutSlack = 4; // DPI rounding / scroll-bar gutter

        var root = (Grid)Content;
        root.UpdateLayout();
        UpdateLayout();

        // Window.Height is outer size (title bar + frame); inner rows were summed without chrome.
        var chromeHeight = ActualHeight - root.ActualHeight;
        if (chromeHeight < 1)
        {
            // Rare before first layout; typical Win11 title bar + frame ≈ 39–48 DIP.
            chromeHeight = 48;
        }

        var contentWidth = Math.Max(0, ActualWidth - 32); // horizontal ScrollViewer margin 16×2
        SettingsContent.Measure(new Size(contentWidth, double.PositiveInfinity));
        SettingsContent.UpdateLayout();

        var contentHeight = SettingsContent.ActualHeight > 0
            ? SettingsContent.ActualHeight
            : SettingsContent.DesiredSize.Height;

        var innerNeeded = HeaderBorder.ActualHeight
                          + FooterBorder.ActualHeight
                          + scrollVerticalMargin
                          + contentHeight;

        var target = Math.Ceiling(innerNeeded + chromeHeight + layoutSlack);
        if (Height < target)
        {
            Height = target;
        }
    }

    public void LoadFromSettings()
    {
        _suppressToggleEvents = true;

        OpenPanelCapture.Content = HotkeyService.Describe(_settings.Current.OpenPanelHotkey);
        CycleEnabledCheck.IsChecked = _settings.Current.CyclePrimaryHotkey.Enabled;
        CycleCapture.Content = HotkeyService.Describe(_settings.Current.CyclePrimaryHotkey);
        CycleCapture.IsEnabled = _settings.Current.CyclePrimaryHotkey.Enabled;
        AutoUpdateCheck.IsChecked = _settings.Current.AutoUpdateCheckEnabled;
        ThemeCombo.SelectedIndex = (int)_settings.Current.Theme;

        _suppressToggleEvents = false;
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

            _settings.Update(s => s.OpenPanelHotkey = newHotkey);
        }
        else if (_capturing == CaptureTarget.Cycle)
        {
            if (newHotkey.Matches(_settings.Current.OpenPanelHotkey) &&
                _settings.Current.OpenPanelHotkey.IsBound)
            {
                CaptureHint.Text = "That shortcut is already used by Open control panel.";
                return;
            }

            _settings.Update(s =>
            {
                s.CyclePrimaryHotkey = newHotkey;
                s.CyclePrimaryHotkey.Enabled = true;
            });
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
        _settings.Update(s => s.CyclePrimaryHotkey.Enabled = enabled);
        CycleCapture.IsEnabled = enabled;

        if (enabled && !_settings.Current.CyclePrimaryHotkey.IsBound)
        {
            SetStatus("Now set a shortcut for Cycle primary display.");
        }
    }

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
        UpdateStatusText.Text = "Checking…";

        try
        {
            var service = new UpdateService();
            var info = await service.CheckForUpdateAsync();
            _settings.Update(s => s.LastUpdateCheckUtc = DateTime.UtcNow);

            if (info is null)
            {
                UpdateStatusText.Text = "Could not reach GitHub. Try again later.";
            }
            else if (info.UpdateAvailable)
            {
                UpdateStatusText.Text = $"{info.LatestTag} is available — opening release…";
                OpenUrl(info.ReleaseUrl);
            }
            else
            {
                UpdateStatusText.Text = $"You're up to date (v{AppInfo.AppVersion}).";
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

    private static void OpenUrl(string url) => UrlLaunchHelper.TryOpenWebUrl(url);

    private void SetStatus(string message) => StatusText.Text = message;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
