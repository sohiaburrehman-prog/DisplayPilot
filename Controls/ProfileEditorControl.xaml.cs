using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Microsoft.Win32;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

using Brush = System.Windows.Media.Brush;
using FontWeight = System.Windows.FontWeights;
using UserControl = System.Windows.Controls.UserControl;

namespace PrimaryDisplaySwap.Controls;

public partial class ProfileEditorControl : UserControl
{
    public event EventHandler? Saved;
    public event EventHandler? Cancelled;
    public event EventHandler<string>? StatusChanged;

    private readonly DisplayManager _displayManager;
    private readonly SettingsService _settings;
    private string? _editingProfileId;
    private bool _isEditing;

    private sealed record SceneComboItem(LayoutPreset? Scene, string Label);

    public ProfileEditorControl(DisplayManager displayManager, SettingsService settings)
    {
        _displayManager = displayManager;
        _settings = settings;

        InitializeComponent();
        Visibility = Visibility.Collapsed;

        ProcessNameBox.TextChanged += (_, _) => UpdateResolvedTargetVisibility();
    }

    public bool IsEditing => _isEditing;

    public void BeginNew()
    {
        _editingProfileId = null;
        EditorTitle.Text = "New profile";
        ProcessNameBox.Text = string.Empty;
        ResolvedTargetBox.Text = string.Empty;
        ExecutablePathBox.Text = string.Empty;
        WindowTitleBox.Text = string.Empty;
        SelectPriority(0);
        RestoreOnExitCheck.IsChecked = true;
        MoveWindowCheck.IsChecked = true;
        PopulateRunningProcesses();
        PopulateMonitorCombo();
        PopulateSceneCombo();
        UpdateResolvedTargetVisibility();
        TargetMonitorCombo.SelectedIndex = TargetMonitorCombo.Items.Count > 0 ? 0 : -1;
        _isEditing = true;
        Visibility = Visibility.Visible;
    }

    public void BeginEdit(string profileId)
    {
        var profile = _settings.Current.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null)
        {
            return;
        }

        _editingProfileId = profileId;
        EditorTitle.Text = "Edit profile";
        ProcessNameBox.Text = profile.ProcessName;
        ResolvedTargetBox.Text = profile.ResolvedTargetProcessName;
        ExecutablePathBox.Text = profile.ExecutablePath;
        WindowTitleBox.Text = profile.WindowTitleContains;
        SelectPriority(profile.Priority);
        RestoreOnExitCheck.IsChecked = profile.RestoreOnExit;
        MoveWindowCheck.IsChecked = profile.MoveWindowToTarget;
        PopulateRunningProcesses();
        PopulateMonitorCombo();
        PopulateSceneCombo();
        UpdateResolvedTargetVisibility();

        SceneCombo.SelectedItem = SceneCombo.Items.OfType<SceneComboItem>()
            .FirstOrDefault(item => string.Equals(
                item.Scene?.Id,
                profile.DisplaySceneId,
                StringComparison.Ordinal)) ?? SceneCombo.Items.OfType<SceneComboItem>().FirstOrDefault();

        var match = TargetMonitorCombo.Items.OfType<ProfileUiHelper.MonitorComboItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Monitor.Name, profile.TargetMonitorName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Monitor.DeviceName, profile.TargetMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            TargetMonitorCombo.SelectedItem = match;
        }

        _isEditing = true;
        Visibility = Visibility.Visible;
    }

    public void HideEditor()
    {
        _isEditing = false;
        Visibility = Visibility.Collapsed;
        _editingProfileId = null;
    }

    public void RefreshMonitors()
    {
        if (IsEditing)
        {
            PopulateMonitorCombo();
            UpdateSceneSelection();
        }
    }

    public string? EditingProfileId => _editingProfileId;

    private void PopulateMonitorCombo()
    {
        try
        {
            var items = _displayManager.GetMonitors()
                .Select(m => new ProfileUiHelper.MonitorComboItem(m, MonitorDisplayHelper.GetNumberedName(m, _settings.Current)))
                .ToList();
            TargetMonitorCombo.ItemsSource = items;
            TargetMonitorCombo.DisplayMemberPath = "Label";
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Profile editor: could not list monitors: {ex.Message}");
        }
    }

    private void PopulateRunningProcesses()
    {
        try
        {
            RunningProcessCombo.Items.Clear();
            RunningProcessCombo.Items.Add("Pick process…");

            var groupedProcesses = ProcessPickerHelper.BuildGroupedRunningProcesses();

            foreach (var group in groupedProcesses)
            {
                if (group.Items.Count == 0)
                {
                    continue;
                }

                RunningProcessCombo.Items.Add(new ComboBoxItem
                {
                    Content = group.Header,
                    IsEnabled = false,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    FontWeight = FontWeights.Bold,
                });

                foreach (var item in group.Items)
                {
                    RunningProcessCombo.Items.Add(item);
                }
            }

            var running = groupedProcesses
                .SelectMany(g => g.Items)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var notRunningLaunchers = LauncherCatalog.KnownLaunchers
                .Where(l => !running.Contains(l))
                .ToList();
            if (notRunningLaunchers.Count > 0)
            {
                RunningProcessCombo.Items.Add(new ComboBoxItem
                {
                    Content = "Common launchers (not running)",
                    IsEnabled = false,
                    Foreground = (Brush)FindResource("TextMutedBrush"),
                    FontWeight = FontWeights.Bold,
                });
                foreach (var launcher in notRunningLaunchers)
                {
                    RunningProcessCombo.Items.Add(launcher);
                }
            }

            RunningProcessCombo.SelectedIndex = 0;

            ResolvedTargetCombo.Items.Clear();
            ResolvedTargetCombo.Items.Add("Pick running game…");
            foreach (var exe in ProcessPickerHelper.GetRunningExesExcludingLaunchers())
            {
                ResolvedTargetCombo.Items.Add(exe);
            }

            ResolvedTargetCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Profile editor: could not list processes: {ex.Message}");
        }
    }

    private void RunningProcess_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (RunningProcessCombo.SelectedItem is string name)
        {
            ProcessNameBox.Text = name;
            UpdateResolvedTargetVisibility();
        }
    }

    private void ResolvedTarget_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ResolvedTargetCombo.SelectedItem is string name)
        {
            ResolvedTargetBox.Text = name;
        }
    }

    private void PopulateSceneCombo()
    {
        var items = new List<SceneComboItem>
        {
            new(null, "Set primary monitor only"),
        };
        items.AddRange(_settings.Current.LayoutPresets
            .OrderBy(scene => scene.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(scene => new SceneComboItem(scene, $"Apply scene: {scene.Name}")));
        SceneCombo.ItemsSource = items;
        SceneCombo.SelectedIndex = 0;
    }

    private void Scene_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateSceneSelection();
    }

    private void UpdateSceneSelection()
    {
        if (SceneCombo.SelectedItem is not SceneComboItem { Scene: { } scene })
        {
            TargetMonitorCombo.IsEnabled = true;
            SceneHint.Text = "Only the primary display changes; other display settings stay as they are.";
            return;
        }

        var target = TargetMonitorCombo.Items.OfType<ProfileUiHelper.MonitorComboItem>()
            .FirstOrDefault(item => string.Equals(
                item.Monitor.DeviceName,
                scene.PrimaryMonitorDeviceName,
                StringComparison.OrdinalIgnoreCase));
        if (target is not null)
        {
            TargetMonitorCombo.SelectedItem = target;
        }

        TargetMonitorCombo.IsEnabled = false;
        SceneHint.Text =
            $"Applies the full \"{scene.Name}\" scene. Its primary display is selected below for window placement.";
    }

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select the executable this profile should match",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true)
        {
            ExecutablePathBox.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(ProcessNameBox.Text))
            {
                ProcessNameBox.Text = Path.GetFileName(dialog.FileName);
            }
        }
    }

    private void SuggestRunningGames_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var recent = ProcessPickerHelper.GetRecentlyStartedExeLabels(withinMinutes: 15);
            if (recent.Count == 0)
            {
                StatusChanged?.Invoke(this, "No recently started games found (last 15 min). Launch a game via the launcher first.");
                return;
            }

            ResolvedTargetCombo.Items.Clear();
            ResolvedTargetCombo.Items.Add("Pick running game…");
            foreach (var exe in recent)
            {
                ResolvedTargetCombo.Items.Add(exe);
            }

            ResolvedTargetCombo.SelectedIndex = recent.Count == 1 ? 1 : 0;
            if (recent.Count == 1)
            {
                ResolvedTargetBox.Text = recent[0];
            }

            StatusChanged?.Invoke(this, $"{recent.Count} recently started process(es) — pick one from the list.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Profile editor: suggest running games failed: {ex.Message}");
            StatusChanged?.Invoke(this, "Could not list recently started processes.");
        }
    }

    private void UpdateResolvedTargetVisibility()
    {
        var process = ProcessNameBox.Text.Trim();
        ResolvedTargetPanel.Visibility = LauncherCatalog.IsKnownLauncher(process)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            StatusChanged?.Invoke(this, "Enter a process name first.");
            return;
        }

        var selectedScene = (SceneCombo.SelectedItem as SceneComboItem)?.Scene;
        if (TargetMonitorCombo.SelectedItem is not ProfileUiHelper.MonitorComboItem monitorItem)
        {
            StatusChanged?.Invoke(this, "Pick a target monitor first.");
            return;
        }


        var priority = GetSelectedPriority();

        var monitor = monitorItem.Monitor;
        var restore = RestoreOnExitCheck.IsChecked == true;
        var moveWindow = MoveWindowCheck.IsChecked == true;
        var resolvedTarget = LauncherCatalog.IsKnownLauncher(processName)
            ? ResolvedTargetBox.Text.Trim()
            : string.Empty;

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
                    ResolvedTargetProcessName = resolvedTarget,
                    DisplaySceneId = selectedScene?.Id ?? string.Empty,
                    TargetMonitorName = monitor.Name,
                    TargetMonitorDeviceName = monitor.DeviceName,
                    RestoreOnExit = restore,
                    Priority = priority,
                    MoveWindowToTarget = moveWindow,
                    ExecutablePath = ExecutablePathBox.Text.Trim(),
                    WindowTitleContains = WindowTitleBox.Text.Trim(),
                    Enabled = true,
                });
            }
            else
            {
                existing.ProcessName = processName;
                existing.ResolvedTargetProcessName = resolvedTarget;
                existing.DisplaySceneId = selectedScene?.Id ?? string.Empty;
                existing.TargetMonitorName = monitor.Name;
                existing.TargetMonitorDeviceName = monitor.DeviceName;
                existing.RestoreOnExit = restore;
                existing.Priority = priority;
                existing.MoveWindowToTarget = moveWindow;
                existing.ExecutablePath = ExecutablePathBox.Text.Trim();
                existing.WindowTitleContains = WindowTitleBox.Text.Trim();
            }
        });

        HideEditor();
        StatusChanged?.Invoke(this, "Profile saved.");
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        HideEditor();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void TestProfile_Click(object sender, RoutedEventArgs e)
    {
        var processName = ProcessNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            StatusChanged?.Invoke(this, "Enter a process name to test.");
            return;
        }

        var selectedScene = (SceneCombo.SelectedItem as SceneComboItem)?.Scene;
        if (TargetMonitorCombo.SelectedItem is not ProfileUiHelper.MonitorComboItem monitorItem)
        {
            StatusChanged?.Invoke(this, "Pick a target monitor to test.");
            return;
        }


        var priority = GetSelectedPriority();

        var draft = new AppProfile
        {
            Id = _editingProfileId ?? "draft",
            ProcessName = processName,
            ResolvedTargetProcessName = LauncherCatalog.IsKnownLauncher(processName)
                ? ResolvedTargetBox.Text.Trim()
                : string.Empty,
            DisplaySceneId = selectedScene?.Id ?? string.Empty,
            TargetMonitorName = monitorItem.Monitor.Name,
            TargetMonitorDeviceName = monitorItem.Monitor.DeviceName,
            RestoreOnExit = RestoreOnExitCheck.IsChecked == true,
            Priority = priority,
            ExecutablePath = ExecutablePathBox.Text.Trim(),
            WindowTitleContains = WindowTitleBox.Text.Trim(),
            Enabled = true,
        };

        ProfileUiHelper.TestProfile(draft, _displayManager, _settings.Current, msg => StatusChanged?.Invoke(this, msg));
    }

    private void SelectPriority(int priority)
    {
        while (PriorityCombo.Items.Count > 4)
        {
            PriorityCombo.Items.RemoveAt(PriorityCombo.Items.Count - 1);
        }

        foreach (var item in PriorityCombo.Items.OfType<ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var value) && value == priority)
            {
                PriorityCombo.SelectedItem = item;
                return;
            }
        }

        var custom = new ComboBoxItem
        {
            Content = $"Custom ({priority})",
            Tag = priority.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        PriorityCombo.Items.Add(custom);
        PriorityCombo.SelectedItem = custom;
    }

    private int GetSelectedPriority()
    {
        if (PriorityCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var priority))
        {
            return priority;
        }

        return 0;
    }
}
