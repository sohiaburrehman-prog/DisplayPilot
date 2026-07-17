using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

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
    private readonly DispatcherTimer _searchDebounce;
    private IReadOnlyList<MonitorInfo> _cachedMonitors = Array.Empty<MonitorInfo>();
    private bool _loadingConflictRule;
    private bool _presetOperationInProgress;

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
        ProfileEditorScroll.Content = _profileEditor;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            RebuildProfileList();
        };

        _profileEditor.Saved += (_, _) => OnEditorClosed();
        _profileEditor.Cancelled += (_, _) => OnEditorClosed();
        _profileEditor.StatusChanged += (_, message) => SetStatus(message);

        if (_processWatcher is not null)
        {
            _processWatcher.ActiveProfileChanged += ProcessWatcher_ActiveProfileChanged;
        }

        ContentRendered += (_, _) => ClampToWorkArea();

        RefreshMonitorCache();
        LoadConflictRule();
        RebuildProfileList();
        RebuildPresetList();
    }

    private void ClampToWorkArea()
    {
        var maximum = Math.Max(420, SystemParameters.WorkArea.Height - 32);
        MinHeight = Math.Min(MinHeight, maximum);
        MaxHeight = maximum;
        Height = Math.Min(Height, maximum);
    }

    private void ProcessWatcher_ActiveProfileChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RebuildProfileList);

    private void LoadConflictRule()
    {
        _loadingConflictRule = true;
        ConflictRuleCombo.SelectedIndex = _settings.Current.ProfileConflictRule == ProfileConflictRule.MostRecentlyActivated
            ? 1
            : 0;
        _loadingConflictRule = false;
    }

    private void ConflictRule_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingConflictRule || ConflictRuleCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        var rule = string.Equals(item.Tag?.ToString(), nameof(ProfileConflictRule.MostRecentlyActivated), StringComparison.Ordinal)
            ? ProfileConflictRule.MostRecentlyActivated
            : ProfileConflictRule.HighestPriority;
        _settings.Update(s => s.ProfileConflictRule = rule);
        SetStatus(rule == ProfileConflictRule.HighestPriority
            ? "Conflict rule saved: highest priority wins."
            : "Conflict rule saved: most recently activated wins.");
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

        RefreshMonitorCache();
        _profileEditor.RefreshMonitors();
        LoadConflictRule();
        RebuildProfileList();
        RebuildPresetList();
    }

    public void RefreshFromSettings()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshFromSettings);
            return;
        }

        LoadConflictRule();
        RebuildProfileList();
        RebuildPresetList();
    }

    private void RefreshMonitorCache()
    {
        try
        {
            _cachedMonitors = _displayManager.GetMonitors();
        }
        catch (Exception ex)
        {
            _cachedMonitors = Array.Empty<MonitorInfo>();
            AppLogger.Log($"Profile manager could not refresh monitors: {ex.Message}");
        }
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

        var matchedIds = _processWatcher?.CurrentMatchedProfileIds
            ?? new HashSet<string>(StringComparer.Ordinal);

        foreach (var profile in filtered)
        {
            ProfileListPanel.Children.Add(ProfileUiHelper.BuildProfileRow(
                profile,
                _cachedMonitors,
                _settings.Current,
                this,
                isWinnerNow: profile.Id == activeId,
                isMatchedNow: matchedIds.Contains(profile.Id),
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
            || profile.ExecutablePath.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || profile.WindowTitleContains.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || profile.TargetMonitorName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void RebuildPresetList()
    {
        PresetListPanel.Children.Clear();
        var presets = _settings.Current.LayoutPresets;
        NoPresetsText.Visibility = presets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var preset in presets)
        {
            PresetListPanel.Children.Add(BuildPresetRow(preset, _cachedMonitors));
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
            Text = preset.IsFullScene
                ? $"Primary: {primaryLabel}  ·  {preset.MonitorStates.Count} complete monitor state(s)"
                : $"Primary: {primaryLabel}  ·  legacy scene ({preset.MonitorModes.Count} mode(s))",
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

        var previewButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Preview",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        previewButton.Click += (_, _) => PreviewPreset(preset);
        Grid.SetColumn(previewButton, 2);
        grid.Children.Add(previewButton);

        var renameButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Rename",
            Width = 72,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        renameButton.Click += (_, _) => RenamePreset(preset);
        Grid.SetColumn(renameButton, 3);
        grid.Children.Add(renameButton);

        var deleteButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = "Delete",
            Width = 72,
            VerticalAlignment = VerticalAlignment.Center,
        };
        deleteButton.Click += (_, _) => DeletePreset(preset);
        Grid.SetColumn(deleteButton, 4);
        grid.Children.Add(deleteButton);

        border.Child = grid;
        return border;
    }

    private async void ApplyPreset(LayoutPreset preset)
    {
        if (_presetOperationInProgress)
        {
            return;
        }

        SetPresetBusy(true, $"Applying \"{preset.Name}\"…");
        try
        {
            var result = await Task.Run(() =>
                LayoutPresetService.TryApply(preset, _settings.Current, _displayManager));
            if (result.Applied)
            {
                if (result.Changes.Count > 0 && result.RollbackScene is not null &&
                    !SceneConfirmationWindow.Confirm(this, preset.Name))
                {
                    SetStatus("Restoring the previous display scene…");
                    var restore = await Task.Run(() => LayoutPresetService.TryRestore(
                        result.RollbackScene,
                        _settings.Current,
                        _displayManager));
                    SetStatus(restore.Applied
                        ? "Previous display scene restored."
                        : $"Could not restore the previous scene: {restore.Message}");
                }
                else
                {
                    SetStatus(result.Message);
                }
                RefreshMonitorCache();
                RebuildPresetList();
            }
            else
            {
                MessageBox.Show(result.Message, "Could not apply scene", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetPresetBusy(false);
        }
    }

    private async void PreviewPreset(LayoutPreset preset)
    {
        if (_presetOperationInProgress)
        {
            return;
        }

        SetPresetBusy(true, $"Preflighting \"{preset.Name}\"…");
        try
        {
            var result = await Task.Run(() =>
                LayoutPresetService.Preview(preset, _settings.Current, _displayManager));
            var details = result.Changes.Count == 0
                ? result.Message
                : result.Message + "\n\n" + string.Join("\n", result.Changes.Select(change => "• " + change));
            MessageBox.Show(
                details,
                result.Valid ? "Display scene preview" : "Scene cannot be applied",
                MessageBoxButton.OK,
                result.Valid ? MessageBoxImage.Information : MessageBoxImage.Warning);
            SetStatus(result.Message);
        }
        finally
        {
            SetPresetBusy(false);
        }
    }

    private void SetPresetBusy(bool busy, string? status = null)
    {
        _presetOperationInProgress = busy;
        PresetListPanel.IsEnabled = !busy;
        SavePresetButton.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status);
        }
    }

    private void RenamePreset(LayoutPreset preset)
    {
        var dialog = new RenameMonitorDialog(preset.Name, "Rename display scene", "Scene name:");
        dialog.Owner = this;
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
        SetStatus("Scene renamed.");
    }

    private void DeletePreset(LayoutPreset preset)
    {
        var confirm = MessageBox.Show(
            $"Delete display scene \"{preset.Name}\"?",
            "Delete scene",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Update(s => s.LayoutPresets.RemoveAll(p => p.Id == preset.Id));
        RebuildPresetList();
        SetStatus("Scene deleted.");
    }

    private async void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_presetOperationInProgress)
        {
            return;
        }

        var name = NewPresetNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetStatus("Enter a name for the scene.");
            return;
        }

        SetPresetBusy(true, "Capturing the current display scene…");
        try
        {
            var preset = await Task.Run(() => LayoutPresetService.CaptureCurrent(name, _displayManager));
            _settings.Update(s => s.LayoutPresets.Add(preset));
            RefreshMonitorCache();
            RebuildPresetList();
            SetStatus($"Captured scene \"{name}\".");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Could not capture scene", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetPresetBusy(false);
        }
    }

    private void UpdateEmptyState()
    {
        var editing = _profileEditor.IsEditing;
        ListHintText.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        ConflictRulePanel.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        SearchPanel.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
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

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e) => BeginAddProfile();

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabControl) || ExplainStateButton is null)
        {
            return;
        }

        var scenesSelected = MainTabControl.SelectedIndex == 1;
        ExplainStateButton.Content = scenesSelected ? "Explain display state" : "Explain profiles";
        System.Windows.Automation.AutomationProperties.SetName(
            ExplainStateButton,
            scenesSelected
                ? "Explain current displays and saved scenes"
                : "Explain profile matching and winner");
    }

    private async void ShowDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        ExplainStateButton.IsEnabled = false;
        var explainScenes = MainTabControl.SelectedIndex == 1;
        SetStatus(explainScenes ? "Inspecting displays and saved scenes…" : "Inspecting profile matches…");
        try
        {
            if (explainScenes)
            {
                var sceneSnapshot = await Task.Run(() =>
                    DisplaySceneDiagnosticsService.Capture(_settings.Current, _displayManager));
                ShowDiagnosticsDialog(
                    "DisplayPilot — Display scene diagnostics",
                    DisplaySceneDiagnosticsService.FormatReport(sceneSnapshot));
                SetStatus($"Explained {sceneSnapshot.Monitors.Count} display(s) and {sceneSnapshot.Scenes.Count} saved scene(s).");
            }
            else
            {
                var snapshot = await Task.Run(() =>
                {
                    var processes = ProcessWatcherService.GetRunningProcesses(includeDetails: true);
                    return ProfileDiagnosticsService.Capture(
                        _settings.Current,
                        _cachedMonitors,
                        processes,
                        _processWatcher?.CurrentActiveProfile?.ProfileId,
                        _processWatcher?.CurrentMatchedProfileIds);
                });
                ShowDiagnosticsDialog(
                    "DisplayPilot — Profile diagnostics",
                    ProfileDiagnosticsService.FormatReport(snapshot));
                SetStatus($"Explained {_settings.Current.Profiles.Count} profile(s).");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                explainScenes ? "Could not explain display scenes" : "Could not explain profiles",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SetStatus("Could not build the explanation.");
        }
        finally
        {
            ExplainStateButton.IsEnabled = true;
        }
    }

    private void ShowDiagnosticsDialog(string title, string text)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            Width = 720,
            Height = 600,
            MinWidth = 520,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (Brush)FindResource("FlyoutOpaqueBrush"),
            FontFamily = (System.Windows.Media.FontFamily)FindResource("UiFont"),
        };
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var report = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = (Brush)FindResource("CardBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            BorderBrush = (Brush)FindResource("HairlineBrush"),
            Padding = new Thickness(12),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
        };
        grid.Children.Add(report);
        var close = new Button
        {
            Content = "Close",
            Style = (Style)FindResource("AccentMiniButton"),
            Width = 110,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            IsCancel = true,
        };
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 1);
        grid.Children.Add(close);
        dialog.Content = grid;
        dialog.ShowDialog();
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _searchDebounce.Stop();
        if (_processWatcher is not null)
        {
            _processWatcher.ActiveProfileChanged -= ProcessWatcher_ActiveProfileChanged;
        }

        base.OnClosed(e);
    }
}
