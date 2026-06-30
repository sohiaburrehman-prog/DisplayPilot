using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using PrimaryDisplaySwap.Controls;
using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace PrimaryDisplaySwap;

public partial class ProfilesWindow : Window
{
    private readonly DisplayManager _displayManager;
    private readonly SettingsService _settings;
    private readonly ProcessWatcherService? _processWatcher;
    private readonly ProfileEditorControl _profileEditor;

    public ProfilesWindow(
        DisplayManager displayManager,
        SettingsService settings,
        ProcessWatcherService? processWatcher = null)
    {
        _displayManager = displayManager;
        _settings = settings;
        _processWatcher = processWatcher;

        InitializeComponent();

        _profileEditor = new ProfileEditorControl(_displayManager, _settings);
        ProfileEditorPanel.Child = _profileEditor;

        _profileEditor.Saved += (_, _) => OnEditorClosed();
        _profileEditor.Cancelled += (_, _) => OnEditorClosed();
        _profileEditor.StatusChanged += (_, message) => SetStatus(message);

        if (_processWatcher is not null)
        {
            _processWatcher.ActiveProfileChanged += (_, _) => Dispatcher.BeginInvoke(RebuildProfileList);
        }

        RebuildProfileList();
        RebuildPresetList();
    }

    public void BeginAddProfile()
    {
        MainTabControl.SelectedIndex = 0;
        ShowEditor(() => _profileEditor.BeginNew());
    }

    public void BeginEditProfile(string profileId)
    {
        MainTabControl.SelectedIndex = 0;
        ShowEditor(() => _profileEditor.BeginEdit(profileId));
    }

    public void RefreshMonitors()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshMonitors);
            return;
        }

        _profileEditor.RefreshMonitors();
        RebuildProfileList();
        RebuildPresetList();
    }

    private void ShowEditor(Action begin)
    {
        begin();
        ProfileEditorPanel.Visibility = Visibility.Visible;
        ProfilesScroll.Visibility = Visibility.Collapsed;
        AddProfileButton.IsEnabled = false;
        SearchBox.IsEnabled = false;
        UpdateEmptyState();
        ProfileEditorPanel.BringIntoView();
    }

    private void OnEditorClosed()
    {
        ProfileEditorPanel.Visibility = Visibility.Collapsed;
        ProfilesScroll.Visibility = Visibility.Visible;
        AddProfileButton.IsEnabled = true;
        SearchBox.IsEnabled = true;
        RebuildProfileList();
    }

    private void RebuildProfileList()
    {
        ProfileListPanel.Children.Clear();

        var profiles = _settings.Current.Profiles;
        var editing = _profileEditor.IsEditing;
        var filter = SearchBox?.Text?.Trim() ?? string.Empty;
        var activeId = _processWatcher?.CurrentActiveProfile?.ProfileId ?? string.Empty;

        var filtered = string.IsNullOrEmpty(filter)
            ? profiles
            : profiles.Where(MatchesSearch).ToList();

        NoProfilesText.Visibility = profiles.Count == 0 && !editing
            ? Visibility.Visible
            : Visibility.Collapsed;
        NoSearchResultsText.Visibility = profiles.Count > 0 && filtered.Count == 0 && !editing
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyAddButton.Visibility = profiles.Count == 0 && !editing
            ? Visibility.Visible
            : Visibility.Collapsed;

        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = _displayManager.GetMonitors();
        }
        catch
        {
            monitors = Array.Empty<MonitorInfo>();
        }

        foreach (var profile in filtered)
        {
            ProfileListPanel.Children.Add(ProfileUiHelper.BuildProfileRow(
                profile,
                monitors,
                _settings.Current,
                this,
                isActiveNow: profile.Id == activeId,
                SetProfileEnabled,
                BeginEditProfile,
                DuplicateProfile,
                RemoveProfile,
                TestProfile));
        }

        UpdateEmptyState();
    }

    private bool MatchesSearch(AppProfile profile)
    {
        var filter = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        return profile.DisplayLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || profile.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || profile.ResolvedTargetProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || profile.TargetMonitorName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildPresetList()
    {
        PresetListPanel.Children.Clear();
        var presets = _settings.Current.LayoutPresets;
        NoPresetsText.Visibility = presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        IReadOnlyList<MonitorInfo> monitors;
        try
        {
            monitors = _displayManager.GetMonitors();
        }
        catch
        {
            monitors = Array.Empty<MonitorInfo>();
        }

        foreach (var preset in presets)
        {
            PresetListPanel.Children.Add(BuildPresetRow(preset, monitors));
        }
    }

    private UIElement BuildPresetRow(LayoutPreset preset, IReadOnlyList<MonitorInfo> monitors)
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

        var primary = monitors.FirstOrDefault(m =>
            string.Equals(m.DeviceName, preset.PrimaryMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        var primaryLabel = primary is not null
            ? MonitorDisplayHelper.GetDisplayName(primary, _settings.Current)
            : preset.PrimaryMonitorDeviceName;

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = preset.Name,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });
        info.Children.Add(new TextBlock
        {
            Text = $"Primary: {primaryLabel}  ·  {preset.MonitorModes.Count} monitor mode(s) saved",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var applyButton = new Button
        {
            Style = (Style)FindResource("AccentMiniButton"),
            Content = "Apply",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        applyButton.Click += (_, _) => ApplyPreset(preset);
        Grid.SetColumn(applyButton, 1);
        grid.Children.Add(applyButton);

        var renameButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Rename",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        renameButton.Click += (_, _) => RenamePreset(preset);
        Grid.SetColumn(renameButton, 2);
        grid.Children.Add(renameButton);

        var deleteButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Delete",
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center,
        };
        deleteButton.Click += (_, _) => DeletePreset(preset);
        Grid.SetColumn(deleteButton, 3);
        grid.Children.Add(deleteButton);

        border.Child = grid;
        return border;
    }

    private void ApplyPreset(LayoutPreset preset)
    {
        _ = Task.Run(() =>
        {
            var result = LayoutPresetService.TryApply(preset, _settings.Current, _displayManager);
            Dispatcher.BeginInvoke(() =>
            {
                if (result.Applied)
                {
                    SetStatus(result.Message);
                }
                else
                {
                    MessageBox.Show(result.Message, "Could not apply preset", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            });
        });
    }

    private void RenamePreset(LayoutPreset preset)
    {
        var dialog = new RenameMonitorDialog(preset.Name, "Rename layout preset", "Preset name:");
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.MonitorName))
        {
            return;
        }

        _settings.Update(s =>
        {
            var live = s.LayoutPresets.FirstOrDefault(p => p.Id == preset.Id);
            if (live is not null)
            {
                live.Name = dialog.MonitorName.Trim();
            }
        });
        RebuildPresetList();
        SetStatus("Preset renamed.");
    }

    private void DeletePreset(LayoutPreset preset)
    {
        var confirm = MessageBox.Show(
            $"Delete layout preset \"{preset.Name}\"?",
            "Delete preset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Update(s => s.LayoutPresets.RemoveAll(p => p.Id == preset.Id));
        RebuildPresetList();
        SetStatus("Preset deleted.");
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = NewPresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetStatus("Enter a name for the preset.");
            return;
        }

        try
        {
            var preset = LayoutPresetService.CaptureCurrent(name, _displayManager);
            _settings.Update(s => s.LayoutPresets.Add(preset));
            RebuildPresetList();
            SetStatus($"Saved preset \"{name}\".");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not save preset", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateEmptyState()
    {
        var editing = _profileEditor.IsEditing;
        ListHintText.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        SearchBox.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
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

    private void DuplicateProfile(string id)
    {
        var profile = _settings.Current.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        var copy = profile.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.LastTriggeredUtc = DateTime.MinValue;

        _settings.Update(s =>
        {
            var index = s.Profiles.FindIndex(p => p.Id == id);
            if (index >= 0)
            {
                s.Profiles.Insert(index + 1, copy);
            }
            else
            {
                s.Profiles.Add(copy);
            }
        });

        RebuildProfileList();
        SetStatus($"Duplicated profile \"{profile.DisplayLabel}\".");
    }

    private void RemoveProfile(string id)
    {
        var profile = _settings.Current.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Delete profile \"{profile.DisplayLabel}\"?",
            "Delete profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Update(s => s.Profiles.RemoveAll(p => p.Id == id));

        if (_profileEditor.EditingProfileId == id)
        {
            _profileEditor.HideEditor();
            OnEditorClosed();
        }
        else
        {
            RebuildProfileList();
        }

        SetStatus("Profile deleted.");
    }

    private void TestProfile(string id)
    {
        var profile = _settings.Current.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null)
        {
            return;
        }

        ProfileUiHelper.TestProfile(profile, _displayManager, _settings.Current, SetStatus);
        RebuildProfileList();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RebuildProfileList();

    private void AddProfile_Click(object sender, RoutedEventArgs e) => BeginAddProfile();

    private void SetStatus(string message) => StatusText.Text = message;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
