using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using FontFamily = System.Windows.Media.FontFamily;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace PrimaryDisplaySwap;

public partial class SettingsWindow : Window
{
    private enum CaptureTarget { None, OpenPanel, Cycle }

    private readonly DisplayManager _displayManager;
    private readonly SettingsService _settings;

    private CaptureTarget _capturing = CaptureTarget.None;
    private string? _editingProfileId;
    private bool _suppressToggleEvents;

    public SettingsWindow(DisplayManager displayManager, SettingsService settings)
    {
        _displayManager = displayManager;
        _settings = settings;

        InitializeComponent();

        PreviewKeyDown += OnPreviewKeyDown;

        LoadFromSettings();
        RebuildProfileList();
    }

    private void LoadFromSettings()
    {
        _suppressToggleEvents = true;

        OpenPanelCapture.Content = HotkeyService.Describe(_settings.Current.OpenPanelHotkey);
        CycleEnabledCheck.IsChecked = _settings.Current.CyclePrimaryHotkey.Enabled;
        CycleCapture.Content = HotkeyService.Describe(_settings.Current.CyclePrimaryHotkey);
        CycleCapture.IsEnabled = _settings.Current.CyclePrimaryHotkey.Enabled;
        AutoUpdateCheck.IsChecked = _settings.Current.AutoUpdateCheckEnabled;

        _suppressToggleEvents = false;
    }

    public void RefreshMonitors()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshMonitors);
            return;
        }

        if (ProfileEditor.Visibility == Visibility.Visible)
        {
            PopulateMonitorCombo();
        }

        RebuildProfileList();
    }

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
            return; // Wait for a non-modifier key.
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
            // Guard against binding both hotkeys to the same combo.
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

    // ─────────────────────── Profiles ───────────────────────

    private void RebuildProfileList()
    {
        ProfileListPanel.Children.Clear();
        var profiles = _settings.Current.Profiles;
        NoProfilesText.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var profile in profiles)
        {
            ProfileListPanel.Children.Add(BuildProfileRow(profile));
        }
    }

    private UIElement BuildProfileRow(AppProfile profile)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("CardBrush"),
            BorderBrush = (Brush)FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = profile.ProcessName,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });

        var detail = $"→ {profile.TargetMonitorName}";
        if (profile.RestoreOnExit)
        {
            detail += "  ·  restores on exit";
        }
        if (!profile.Enabled)
        {
            detail += "  ·  disabled";
        }

        info.Children.Add(new TextBlock
        {
            Text = detail,
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var enabledToggle = new CheckBox
        {
            Style = (Style)FindResource("DarkCheckBox"),
            IsChecked = profile.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            ToolTip = "Enable or disable this profile",
        };
        var id = profile.Id;
        enabledToggle.Checked += (_, _) => SetProfileEnabled(id, true);
        enabledToggle.Unchecked += (_, _) => SetProfileEnabled(id, false);
        Grid.SetColumn(enabledToggle, 1);
        grid.Children.Add(enabledToggle);

        var editButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Edit",
            Width = 64,
            VerticalAlignment = VerticalAlignment.Center,
        };
        editButton.Click += (_, _) => BeginEditProfile(id);
        Grid.SetColumn(editButton, 2);
        grid.Children.Add(editButton);

        var removeButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Remove",
            Width = 78,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        removeButton.Click += (_, _) => RemoveProfile(id);
        Grid.SetColumn(removeButton, 3);
        grid.Children.Add(removeButton);

        border.Child = grid;
        return border;
    }

    private void SetProfileEnabled(string id, bool enabled)
    {
        _settings.Update(s =>
        {
            var profile = s.Profiles.FirstOrDefault(p => p.Id == id);
            if (profile is not null)
            {
                profile.Enabled = enabled;
            }
        });
    }

    private void RemoveProfile(string id)
    {
        _settings.Update(s => s.Profiles.RemoveAll(p => p.Id == id));
        RebuildProfileList();
        if (_editingProfileId == id)
        {
            HideEditor();
        }

        SetStatus("Profile removed.");
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        _editingProfileId = null;
        ProfileEditorTitle.Text = "New profile";
        ProcessNameBox.Text = string.Empty;
        RestoreOnExitCheck.IsChecked = true;
        PopulateRunningProcesses();
        PopulateMonitorCombo();
        TargetMonitorCombo.SelectedIndex = TargetMonitorCombo.Items.Count > 0 ? 0 : -1;
        ProfileEditor.Visibility = Visibility.Visible;
    }

    private void BeginEditProfile(string id)
    {
        var profile = _settings.Current.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        _editingProfileId = id;
        ProfileEditorTitle.Text = "Edit profile";
        ProcessNameBox.Text = profile.ProcessName;
        RestoreOnExitCheck.IsChecked = profile.RestoreOnExit;
        PopulateRunningProcesses();
        PopulateMonitorCombo();

        var match = TargetMonitorCombo.Items.OfType<MonitorInfo>()
            .FirstOrDefault(m => string.Equals(m.Name, profile.TargetMonitorName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            TargetMonitorCombo.SelectedItem = match;
        }

        ProfileEditor.Visibility = Visibility.Visible;
    }

    private void PopulateMonitorCombo()
    {
        try
        {
            var monitors = _displayManager.GetMonitors().ToList();
            TargetMonitorCombo.ItemsSource = monitors;
            TargetMonitorCombo.DisplayMemberPath = nameof(MonitorInfo.NumberedName);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Settings: could not list monitors: {ex.Message}");
        }
    }

    private void PopulateRunningProcesses()
    {
        try
        {
            var names = Process.GetProcesses()
                .Select(p =>
                {
                    try { return p.ProcessName; }
                    catch { return string.Empty; }
                    finally { p.Dispose(); }
                })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Select(n => n + ".exe")
                .ToList();

            names.Insert(0, "Pick running app…");
            RunningProcessCombo.ItemsSource = names;
            RunningProcessCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Settings: could not list processes: {ex.Message}");
        }
    }

    private void RunningProcess_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RunningProcessCombo.SelectedIndex > 0 && RunningProcessCombo.SelectedItem is string name)
        {
            ProcessNameBox.Text = name;
        }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            SetStatus("Enter a process name first.");
            return;
        }

        if (TargetMonitorCombo.SelectedItem is not MonitorInfo monitor)
        {
            SetStatus("Pick a target monitor first.");
            return;
        }

        var restore = RestoreOnExitCheck.IsChecked == true;

        _settings.Update(s =>
        {
            var existing = _editingProfileId is null
                ? null
                : s.Profiles.FirstOrDefault(p => p.Id == _editingProfileId);

            if (existing is null)
            {
                s.Profiles.Add(new AppProfile
                {
                    ProcessName = processName,
                    TargetMonitorName = monitor.Name,
                    TargetMonitorDeviceName = monitor.DeviceName,
                    RestoreOnExit = restore,
                    Enabled = true,
                });
            }
            else
            {
                existing.ProcessName = processName;
                existing.TargetMonitorName = monitor.Name;
                existing.TargetMonitorDeviceName = monitor.DeviceName;
                existing.RestoreOnExit = restore;
            }
        });

        HideEditor();
        RebuildProfileList();
        SetStatus("Profile saved.");
    }

    private void CancelProfile_Click(object sender, RoutedEventArgs e) => HideEditor();

    private void HideEditor()
    {
        ProfileEditor.Visibility = Visibility.Collapsed;
        _editingProfileId = null;
    }

    // ─────────────────────── Updates ───────────────────────

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

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Could not open URL '{url}': {ex.Message}");
        }
    }

    /// <summary>Scrolls the profiles section into view; optionally opens the add-profile editor.</summary>
    public void FocusProfilesSection(bool beginAdd = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => FocusProfilesSection(beginAdd));
            return;
        }

        ProfilesSectionAnchor.BringIntoView();

        if (beginAdd && ProfileEditor.Visibility != Visibility.Visible)
        {
            AddProfile_Click(this, new RoutedEventArgs());
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
