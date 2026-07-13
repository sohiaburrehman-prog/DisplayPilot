using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

using PrimaryDisplaySwap.Models;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;

namespace PrimaryDisplaySwap.Services;

/// <summary>Shared UI helpers for profile list rows and profile testing.</summary>
internal static class ProfileUiHelper
{
    internal sealed class MonitorComboItem(MonitorInfo monitor, string label)
    {
        public MonitorInfo Monitor { get; } = monitor;
        public string Label { get; } = label;

        public override string ToString() => Label;
    }

    public static UIElement BuildProfileRow(
        AppProfile profile,
        IReadOnlyList<MonitorInfo> monitors,
        AppSettings settings,
        FrameworkElement host,
        bool isWinnerNow,
        bool isMatchedNow,
        Action<string, bool> onEnabledChanged,
        Action<string> onEdit,
        Action<string> onDuplicate,
        Action<string> onRemove,
        Action<string> onTest)
    {
        var border = new Border
        {
            Background = (Brush)host.FindResource("CardBrush"),
            BorderBrush = (Brush)host.FindResource("HairlineBrush"),
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
        var titleRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = profile.DisplayLabel,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)host.FindResource("TextPrimaryBrush"),
        });

        if (isWinnerNow || isMatchedNow)
        {
            titleRow.Children.Add(new Border
            {
                Background = isWinnerNow
                    ? (Brush)host.FindResource("AccentBrush")
                    : (Brush)host.FindResource("CardHoverBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = isWinnerNow ? "Controlling display" : "Matched · waiting",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)host.FindResource("TextPrimaryBrush"),
                },
            });
        }

        info.Children.Add(titleRow);

        var targetLabel = profile.TargetMonitorName;
        foreach (var monitor in monitors)
        {
            if (string.Equals(monitor.DeviceName, profile.TargetMonitorDeviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(monitor.Name, profile.TargetMonitorName, StringComparison.OrdinalIgnoreCase))
            {
                targetLabel = MonitorDisplayHelper.GetDisplayName(monitor, settings);
                break;
            }
        }

        var scene = string.IsNullOrWhiteSpace(profile.DisplaySceneId)
            ? null
            : settings.LayoutPresets.FirstOrDefault(s => string.Equals(
                s.Id,
                profile.DisplaySceneId,
                StringComparison.Ordinal));
        var actionLabel = string.IsNullOrWhiteSpace(profile.DisplaySceneId)
            ? $"→ {targetLabel}"
            : scene is null ? "→ missing scene" : $"→ scene: {scene.Name}";
        var detail = $"{actionLabel}  ·  {DescribePriority(profile.Priority)} priority";
        if (profile.RestoreOnExit)
        {
            detail += "  ·  restores on exit";
        }
        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath))
        {
            detail += "  ·  path constrained";
        }
        if (!string.IsNullOrWhiteSpace(profile.WindowTitleContains))
        {
            detail += "  ·  title constrained";
        }
        if (!profile.Enabled)
        {
            detail += "  ·  disabled";
        }

        if (profile.LastTriggeredUtc > DateTime.MinValue)
        {
            var local = profile.LastTriggeredUtc.ToLocalTime();
            detail += $"  ·  last triggered {local:g}";
        }

        info.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Foreground = (Brush)host.FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var enabledToggle = new CheckBox
        {
            Style = (Style)host.FindResource("DarkCheckBox"),
            IsChecked = profile.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            ToolTip = "Enable or disable this profile",
        };
        var id = profile.Id;
        AutomationProperties.SetName(enabledToggle, $"{(profile.Enabled ? "Disable" : "Enable")} profile {profile.DisplayLabel}");
        enabledToggle.Checked += (_, _) => onEnabledChanged(id, true);
        enabledToggle.Unchecked += (_, _) => onEnabledChanged(id, false);
        Grid.SetColumn(enabledToggle, 1);
        grid.Children.Add(enabledToggle);

        var editButton = new Button
        {
            Style = (Style)host.FindResource("MiniButton"),
            Content = "Edit",
            Width = 64,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(editButton, $"Edit profile {profile.DisplayLabel}");
        editButton.Click += (_, _) => onEdit(id);
        Grid.SetColumn(editButton, 2);
        grid.Children.Add(editButton);

        var moreButton = new Button
        {
            Style = (Style)host.FindResource("MiniButton"),
            Content = "⋯",
            Width = 40,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "More profile actions",
        };
        AutomationProperties.SetName(moreButton, $"More actions for {profile.DisplayLabel}");

        var menu = new ContextMenu
        {
            Background = (Brush)host.FindResource("MapSurfaceBrush"),
            BorderBrush = (Brush)host.FindResource("HairlineBrush"),
            Foreground = (Brush)host.FindResource("TextPrimaryBrush"),
        };
        var testItem = new MenuItem { Header = "Test profile" };
        testItem.Click += (_, _) => onTest(id);
        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) => onDuplicate(id);
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => onRemove(id);
        menu.Items.Add(testItem);
        menu.Items.Add(duplicateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        moreButton.ContextMenu = menu;
        moreButton.Click += (_, _) =>
        {
            menu.PlacementTarget = moreButton;
            menu.IsOpen = true;
        };

        Grid.SetColumn(moreButton, 3);
        grid.Children.Add(moreButton);

        border.Child = grid;
        return border;
    }

    private static string DescribePriority(int priority) => priority switch
    {
        <= -10 => "Low",
        0 => "Normal",
        >= 100 => "Critical",
        >= 10 => "High",
        _ => $"Custom ({priority})",
    };

    public static void TestProfile(
        AppProfile profile,
        DisplayManager displayManager,
        AppSettings settings,
        Action<string>? setStatus = null)
    {
        try
        {
            var processes = ProcessWatcherService.GetRunningProcesses(includeDetails: true);
            var running = processes.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<MonitorInfo> monitors;
            try
            {
                monitors = displayManager.GetMonitors();
            }
            catch
            {
                monitors = Array.Empty<MonitorInfo>();
            }

            var scene = string.IsNullOrWhiteSpace(profile.DisplaySceneId)
                ? null
                : settings.LayoutPresets.FirstOrDefault(s => string.Equals(
                    s.Id,
                    profile.DisplaySceneId,
                    StringComparison.Ordinal));
            var effectiveProfile = profile.Clone();
            if (!string.IsNullOrWhiteSpace(profile.DisplaySceneId))
            {
                effectiveProfile.TargetMonitorName = scene?.Name ?? "Missing display scene";
                effectiveProfile.TargetMonitorDeviceName = scene?.PrimaryMonitorDeviceName
                    ?? $"missing-scene:{profile.DisplaySceneId}";
            }

            var evaluation = ProfileMatcher.Evaluate(
                effectiveProfile,
                running,
                monitors,
                runningProcesses: processes);

            AppLogger.Log($"Profile test [{profile.DisplayLabel}]: {evaluation.Summary}");

            var details = evaluation.Summary;
            if (evaluation.TargetConnected && evaluation.TargetMonitor is not null)
            {
                var label = MonitorDisplayHelper.GetDisplayName(evaluation.TargetMonitor, settings);
                details += $"\n\nTarget monitor: {label} ({evaluation.TargetMonitor.DeviceName})";
            }

            var canApply = scene is not null
                ? evaluation.ProfileEnabled && evaluation.ProcessRunning && evaluation.TargetConnected
                : evaluation.WouldMatch;
            if (canApply && evaluation.TargetMonitor is not null)
            {
                var apply = MessageBox.Show(
                    details + (scene is null
                        ? "\n\nApply now and set that monitor as primary?"
                        : $"\n\nApply the full \"{scene.Name}\" scene now?"),
                    "Profile test — match",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (apply == MessageBoxResult.Yes)
                {
                    if (scene is not null)
                    {
                        var result = LayoutPresetService.TryApply(scene, settings, displayManager);
                        var keep = result.Applied && result.Changes.Count > 0 && result.RollbackScene is not null
                            ? SceneConfirmationWindow.Confirm(null, scene.Name)
                            : result.Applied;
                        if (!keep && result.RollbackScene is not null)
                        {
                            LayoutPresetService.TryRestore(result.RollbackScene, settings, displayManager);
                        }
                        setStatus?.Invoke(keep ? $"Applied scene — {scene.Name}." : "Scene reverted.");
                    }
                    else
                    {
                        displayManager.SetPrimaryByDeviceName(evaluation.TargetMonitor.DeviceName);
                        var name = MonitorDisplayHelper.GetDisplayName(evaluation.TargetMonitor, settings);
                        AppLogger.Log($"Profile test apply [{profile.DisplayLabel}]: primary set to '{name}'.");
                        setStatus?.Invoke($"Applied — {name} is now primary.");
                    }
                }

                return;
            }

            MessageBox.Show(
                details,
                evaluation.ProcessRunning ? "Profile test — partial match" : "Profile test — no match",
                MessageBoxButton.OK,
                evaluation.ProcessRunning ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Profile test failed [{profile.DisplayLabel}]: {ex.Message}");
            MessageBox.Show(
                $"Could not test this profile:\n{ex.Message}",
                "Profile test failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
