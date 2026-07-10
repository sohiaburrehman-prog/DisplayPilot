using System.Diagnostics;
using System.Windows;
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
        bool isActiveNow,
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

        if (isActiveNow)
        {
            titleRow.Children.Add(new Border
            {
                Background = (Brush)host.FindResource("AccentBrush"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "Active now",
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

        var detail = $"→ {targetLabel}  ·  priority {profile.Priority}";
        if (profile.RestoreOnExit)
        {
            detail += "  ·  restores on exit";
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
        enabledToggle.Checked += (_, _) => onEnabledChanged(id, true);
        enabledToggle.Unchecked += (_, _) => onEnabledChanged(id, false);
        Grid.SetColumn(enabledToggle, 1);
        grid.Children.Add(enabledToggle);

        var testButton = new Button
        {
            Style = (Style)host.FindResource("MiniButton"),
            Content = "Test",
            Width = 58,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Check if this profile would match right now",
        };
        testButton.Click += (_, _) => onTest(id);
        Grid.SetColumn(testButton, 2);
        grid.Children.Add(testButton);

        var editButton = new Button
        {
            Style = (Style)host.FindResource("MiniButton"),
            Content = "Edit",
            Width = 64,
            VerticalAlignment = VerticalAlignment.Center,
        };
        editButton.Click += (_, _) => onEdit(id);
        Grid.SetColumn(editButton, 3);
        grid.Children.Add(editButton);

        var duplicateButton = new Button
        {
            Style = (Style)host.FindResource("MiniButton"),
            Content = "Duplicate",
            Width = 78,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Create a copy of this profile",
        };
        duplicateButton.Click += (_, _) => onDuplicate(id);
        Grid.SetColumn(duplicateButton, 4);
        grid.Children.Add(duplicateButton);

        var removeButton = new Button
        {
            Style = (Style)host.FindResource("MiniButton"),
            Content = "Delete",
            Width = 72,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        removeButton.Click += (_, _) => onRemove(id);
        Grid.SetColumn(removeButton, 5);
        grid.Children.Add(removeButton);

        border.Child = grid;
        return border;
    }

    public static void TestProfile(
        AppProfile profile,
        DisplayManager displayManager,
        AppSettings settings,
        Action<string>? setStatus = null)
    {
        try
        {
            var running = ProcessWatcherService.GetRunningProcessNames();
            IReadOnlyList<MonitorInfo> monitors;
            try
            {
                monitors = displayManager.GetMonitors();
            }
            catch
            {
                monitors = Array.Empty<MonitorInfo>();
            }

            var evaluation = ProfileMatcher.Evaluate(profile, running, monitors);

            AppLogger.Log($"Profile test [{profile.DisplayLabel}]: {evaluation.Summary}");

            var details = evaluation.Summary;
            if (evaluation.TargetConnected && evaluation.TargetMonitor is not null)
            {
                var label = MonitorDisplayHelper.GetDisplayName(evaluation.TargetMonitor, settings);
                details += $"\n\nTarget monitor: {label} ({evaluation.TargetMonitor.DeviceName})";
            }

            if (evaluation.WouldMatch && evaluation.TargetMonitor is not null)
            {
                var apply = MessageBox.Show(
                    details + "\n\nApply now and set that monitor as primary?",
                    "Profile test — match",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (apply == MessageBoxResult.Yes)
                {
                    displayManager.SetPrimaryByDeviceName(evaluation.TargetMonitor.DeviceName);
                    var name = MonitorDisplayHelper.GetDisplayName(evaluation.TargetMonitor, settings);
                    AppLogger.Log($"Profile test apply [{profile.DisplayLabel}]: primary set to '{name}'.");
                    setStatus?.Invoke($"Applied — {name} is now primary.");
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
