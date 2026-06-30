using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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

    public ProfileEditorControl(DisplayManager displayManager, SettingsService settings)
    {
        _displayManager = displayManager;
        _settings = settings;

        InitializeComponent();

        ProcessNameBox.TextChanged += (_, _) => UpdateResolvedTargetVisibility();
    }

    public bool IsEditing => Visibility == Visibility.Visible;

    public void BeginNew()
    {
        _editingProfileId = null;
        EditorTitle.Text = "New profile";
        ProcessNameBox.Text = string.Empty;
        ResolvedTargetBox.Text = string.Empty;
        RestoreOnExitCheck.IsChecked = true;
        PopulateRunningProcesses();
        PopulateMonitorCombo();
        UpdateResolvedTargetVisibility();
        TargetMonitorCombo.SelectedIndex = TargetMonitorCombo.Items.Count > 0 ? 0 : -1;
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
        RestoreOnExitCheck.IsChecked = profile.RestoreOnExit;
        PopulateRunningProcesses();
        PopulateMonitorCombo();
        UpdateResolvedTargetVisibility();

        var match = TargetMonitorCombo.Items.OfType<ProfileUiHelper.MonitorComboItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Monitor.Name, profile.TargetMonitorName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Monitor.DeviceName, profile.TargetMonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            TargetMonitorCombo.SelectedItem = match;
        }

        Visibility = Visibility.Visible;
    }

    public void HideEditor()
    {
        Visibility = Visibility.Collapsed;
        _editingProfileId = null;
    }

    public void RefreshMonitors()
    {
        if (IsEditing)
        {
            PopulateMonitorCombo();
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

        if (TargetMonitorCombo.SelectedItem is not ProfileUiHelper.MonitorComboItem monitorItem)
        {
            StatusChanged?.Invoke(this, "Pick a target monitor first.");
            return;
        }

        var monitor = monitorItem.Monitor;
        var restore = RestoreOnExitCheck.IsChecked == true;
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
                    TargetMonitorName = monitor.Name,
                    TargetMonitorDeviceName = monitor.DeviceName,
                    RestoreOnExit = restore,
                    Enabled = true,
                });
            }
            else
            {
                existing.ProcessName = processName;
                existing.ResolvedTargetProcessName = resolvedTarget;
                existing.TargetMonitorName = monitor.Name;
                existing.TargetMonitorDeviceName = monitor.DeviceName;
                existing.RestoreOnExit = restore;
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

        if (TargetMonitorCombo.SelectedItem is not ProfileUiHelper.MonitorComboItem monitorItem)
        {
            StatusChanged?.Invoke(this, "Pick a target monitor to test.");
            return;
        }

        var draft = new AppProfile
        {
            Id = _editingProfileId ?? "draft",
            ProcessName = processName,
            ResolvedTargetProcessName = LauncherCatalog.IsKnownLauncher(processName)
                ? ResolvedTargetBox.Text.Trim()
                : string.Empty,
            TargetMonitorName = monitorItem.Monitor.Name,
            TargetMonitorDeviceName = monitorItem.Monitor.DeviceName,
            RestoreOnExit = RestoreOnExitCheck.IsChecked == true,
            Enabled = true,
        };

        ProfileUiHelper.TestProfile(draft, _displayManager, _settings.Current, msg => StatusChanged?.Invoke(this, msg));
    }
}
