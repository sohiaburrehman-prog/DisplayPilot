using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;

namespace PrimaryDisplaySwap;

public partial class WizardWindow : Window
{
    public enum FinishAction
    {
        OpenPanel,
        MinimizeToTray,
    }

    private readonly DisplayManager _displayManager;
    private readonly SettingsService _settings;
    private readonly StartupService _startupService;

    private readonly List<WizardStep> _steps = [];
    private int _stepIndex;
    private bool _capturingHotkey;

    private ComboBox? _profileProcessCombo;
    private ComboBox? _profileMonitorCombo;
    private ComboBox? _profileTargetCombo;
    private Border? _profileTargetPanel;
    private CheckBox? _startupCheck;
    private Button? _hotkeyButton;
    private HotkeyConfig _wizardHotkey = new();

    public FinishAction SelectedFinishAction { get; private set; } = FinishAction.OpenPanel;

    public WizardWindow(DisplayManager displayManager, SettingsService settings, StartupService startupService)
    {
        _displayManager = displayManager;
        _settings = settings;
        _startupService = startupService;

        InitializeComponent();

        PreviewKeyDown += OnPreviewKeyDown;

        _wizardHotkey = _settings.Current.OpenPanelHotkey.Clone();
        BuildSteps();
        ShowStep(0);
    }

    private sealed class WizardStep
    {
        public required string Title { get; init; }
        public required string Subtitle { get; init; }
        public required Action BuildContent { get; init; }
        public bool CanSkip { get; init; }
        public bool IsOptional { get; init; }
        public Action? OnEnter { get; init; }
        public Func<bool>? Validate { get; init; }
    }

    private void BuildSteps()
    {
        _steps.Clear();

        _steps.Add(new WizardStep
        {
            Title = "Welcome to DisplayPilot",
            Subtitle = "Quick setup (~30 seconds)",
            BuildContent = BuildWelcomeStep,
            CanSkip = false,
        });

        try
        {
            if (_displayManager.GetMonitors().Count >= 2)
            {
                _steps.Add(new WizardStep
                {
                    Title = "Choose your primary monitor",
                    Subtitle = "Which display should Windows treat as main?",
                    BuildContent = BuildPrimaryStep,
                    CanSkip = true,
                    IsOptional = true,
                });
            }
        }
        catch
        {
            // Skip primary step if monitors cannot be read.
        }

        _steps.Add(new WizardStep
        {
            Title = "Auto-swap for a game (optional)",
            Subtitle = "Switch primary when an app starts",
            BuildContent = BuildProfileStep,
            CanSkip = true,
            IsOptional = true,
        });

        _steps.Add(new WizardStep
        {
            Title = "Open-panel hotkey",
            Subtitle = "Global shortcut to show the flyout",
            BuildContent = BuildHotkeyStep,
            CanSkip = false,
        });

        _steps.Add(new WizardStep
        {
            Title = "Start with Windows",
            Subtitle = "Keep DisplayPilot ready after sign-in",
            BuildContent = BuildStartupStep,
            CanSkip = true,
            IsOptional = true,
        });

        _steps.Add(new WizardStep
        {
            Title = "You're all set",
            Subtitle = "DisplayPilot is ready",
            BuildContent = BuildDoneStep,
            CanSkip = false,
        });
    }

    private void ShowStep(int index)
    {
        _stepIndex = Math.Clamp(index, 0, _steps.Count - 1);
        var step = _steps[_stepIndex];

        StepTitleText.Text = step.Title;
        StepSubtitleText.Text = $"Step {_stepIndex + 1} of {_steps.Count}";

        StepHost.Children.Clear();
        step.BuildContent();
        step.OnEnter?.Invoke();

        BackButton.Visibility = _stepIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = step.CanSkip ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Content = step.IsOptional ? "Skip" : "Skip";

        var isLast = _stepIndex == _steps.Count - 1;
        NextButton.Content = isLast ? "Finish" : "Next";
    }

    private void AddBody(string text)
    {
        StepHost.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            LineHeight = 20,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
    }

    private void BuildWelcomeStep()
    {
        AddBody(
            "DisplayPilot lives in your system tray and lets you change which monitor Windows uses as the primary display.\n\n" +
            "• Click a monitor card or use the tray menu to set primary\n" +
            "• With two monitors, swap with one click\n" +
            "• Add auto-swap profiles so games launch on the right screen\n" +
            "• Rebind hotkeys and pick resolution per monitor in Settings");
    }

    private void BuildPrimaryStep()
    {
        AddBody("Pick the monitor you want as primary now. You can change this anytime from the tray menu.");

        var monitors = _displayManager.GetMonitors();
        foreach (var monitor in monitors)
        {
            var displayName = MonitorDisplayHelper.GetNumberedName(monitor, _settings.Current);
            var button = new Button
            {
                Style = (Style)FindResource("MonitorCard"),
                Margin = new Thickness(0, 10, 0, 0),
                Tag = monitor.Index,
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = displayName,
                            FontFamily = (FontFamily)FindResource("UiFont"),
                            FontSize = 12.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = (Brush)FindResource("TextPrimaryBrush"),
                        },
                        new TextBlock
                        {
                            Text = monitor.SpecsLabel + (monitor.IsPrimary ? "  ·  current primary" : ""),
                            FontFamily = (FontFamily)FindResource("UiFont"),
                            FontSize = 11,
                            Foreground = (Brush)FindResource("TextMutedBrush"),
                            Margin = new Thickness(0, 2, 0, 0),
                        },
                    },
                },
            };

            if (monitor.IsPrimary)
            {
                button.BorderBrush = (Brush)FindResource("AccentBrush");
            }

            var index = monitor.Index;
            button.Click += (_, _) =>
            {
                try
                {
                    _displayManager.SetPrimaryMonitor(index);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Wizard set-primary failed: {ex.Message}");
                }

                ShowStep(_stepIndex);
            };

            StepHost.Children.Add(button);
        }
    }

    private void BuildProfileStep()
    {
        AddBody(
            "When the chosen app starts, DisplayPilot can move the primary display to a monitor you pick. " +
            "You can also pick a launcher (Steam, Epic, etc.) and optionally set the game exe for reliable matching.");

        StepHost.Children.Add(new TextBlock
        {
            Text = "App or launcher",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 14, 0, 4),
        });

        _profileProcessCombo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
            IsEditable = true,
        };
        PopulateProfileProcessCombo();
        _profileProcessCombo.SelectionChanged += (_, _) => UpdateProfileTargetVisibility();
        _profileProcessCombo.LostFocus += (_, _) => UpdateProfileTargetVisibility();
        StepHost.Children.Add(_profileProcessCombo);

        _profileTargetPanel = new Border
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 12, 0, 0),
            Background = (Brush)FindResource("MapSurfaceBrush"),
            BorderBrush = (Brush)FindResource("HairlineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = BuildProfileTargetContent(),
        };
        StepHost.Children.Add(_profileTargetPanel);

        StepHost.Children.Add(new TextBlock
        {
            Text = "Make this monitor primary",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 14, 0, 4),
        });

        _profileMonitorCombo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
        };
        try
        {
            var monitors = _displayManager.GetMonitors().ToList();
            _profileMonitorCombo.ItemsSource = monitors;
            _profileMonitorCombo.DisplayMemberPath = nameof(MonitorInfo.Name);
            _profileMonitorCombo.SelectedIndex = monitors.Count > 0 ? 0 : -1;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Wizard monitor list failed: {ex.Message}");
        }

        StepHost.Children.Add(_profileMonitorCombo);
    }

    private UIElement BuildProfileTargetContent()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Resolve game from launcher (recommended)",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Pick a running game exe or type one (e.g. eldenring.exe). Launcher-only matching may miss some titles.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 4, 0, 8),
        });

        _profileTargetCombo = new ComboBox
        {
            Style = (Style)FindResource("DarkComboBox"),
            IsEditable = true,
        };
        var running = ProcessPickerHelper.GetRunningExesExcludingLaunchers();
        _profileTargetCombo.ItemsSource = running;
        stack.Children.Add(_profileTargetCombo);
        return stack;
    }

    private void PopulateProfileProcessCombo()
    {
        if (_profileProcessCombo is null)
        {
            return;
        }

        var groups = ProcessPickerHelper.BuildGroupedRunningProcesses();
        _profileProcessCombo.Items.Clear();
        foreach (var group in groups)
        {
            if (group.Items.Count == 0)
            {
                continue;
            }

            _profileProcessCombo.Items.Add(new ComboBoxItem
            {
                Content = group.Header,
                IsEnabled = false,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontWeight = FontWeights.Bold,
            });

            foreach (var item in group.Items)
            {
                _profileProcessCombo.Items.Add(item);
            }
        }

        foreach (var launcher in LauncherCatalog.KnownLaunchers)
        {
            if (groups.Any(g => g.Items.Contains(launcher, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!_profileProcessCombo.Items.OfType<string>().Contains(launcher, StringComparer.OrdinalIgnoreCase))
            {
                // Known launchers not running are still listed at the end.
            }
        }

        var notRunningLaunchers = LauncherCatalog.KnownLaunchers
            .Where(l => !groups.SelectMany(g => g.Items).Contains(l, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (notRunningLaunchers.Count > 0)
        {
            _profileProcessCombo.Items.Add(new ComboBoxItem
            {
                Content = "Common launchers (not running)",
                IsEnabled = false,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontWeight = FontWeights.Bold,
            });
            foreach (var launcher in notRunningLaunchers)
            {
                _profileProcessCombo.Items.Add(launcher);
            }
        }
    }

    private void UpdateProfileTargetVisibility()
    {
        if (_profileTargetPanel is null || _profileProcessCombo is null)
        {
            return;
        }

        var process = GetComboText(_profileProcessCombo);
        _profileTargetPanel.Visibility = LauncherCatalog.IsKnownLauncher(process)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string GetComboText(ComboBox combo)
    {
        if (combo.SelectedItem is string selected)
        {
            return selected;
        }

        return combo.Text?.Trim() ?? string.Empty;
    }

    private void BuildHotkeyStep()
    {
        AddBody("Press the shortcut you want for opening the control panel from anywhere. The default is Ctrl+Shift+M.");

        _hotkeyButton = new Button
        {
            Style = (Style)FindResource("MiniButton"),
            Content = HotkeyService.Describe(_wizardHotkey),
            MinWidth = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
        };
        _hotkeyButton.Click += (_, _) =>
        {
            _capturingHotkey = true;
            _hotkeyButton.Content = "Press keys…";
            _hotkeyButton.Focus();
        };
        StepHost.Children.Add(_hotkeyButton);

        StepHost.Children.Add(new TextBlock
        {
            Text = "Use at least one modifier (Ctrl, Alt, Shift, or Win). Esc cancels capture.",
            FontSize = 10.5,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
    }

    private void BuildStartupStep()
    {
        AddBody("DisplayPilot can start hidden in the tray when you sign in to Windows.");

        _startupCheck = new CheckBox
        {
            Style = (Style)FindResource("DarkCheckBox"),
            Content = "Start DisplayPilot with Windows",
            IsChecked = _startupService.IsEnabled,
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        };
        StepHost.Children.Add(_startupCheck);
    }

    private void BuildDoneStep()
    {
        AddBody("Setup is complete. Open the control panel now or minimize to the tray — you can always reopen with your hotkey or a double-click on the tray icon.");

        var openPanel = new RadioButton
        {
            Content = "Open control panel",
            IsChecked = true,
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            GroupName = "FinishAction",
        };
        openPanel.Checked += (_, _) => SelectedFinishAction = FinishAction.OpenPanel;
        StepHost.Children.Add(openPanel);

        var minimize = new RadioButton
        {
            Content = "Minimize to tray",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            GroupName = "FinishAction",
        };
        minimize.Checked += (_, _) => SelectedFinishAction = FinishAction.MinimizeToTray;
        StepHost.Children.Add(minimize);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey || _hotkeyButton is null)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _capturingHotkey = false;
            _hotkeyButton.Content = HotkeyService.Describe(_wizardHotkey);
            return;
        }

        if (IsModifierKey(key))
        {
            return;
        }

        var modifiers = ToWin32Modifiers(Keyboard.Modifiers);
        if (modifiers == 0)
        {
            return;
        }

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            return;
        }

        _wizardHotkey = new HotkeyConfig { Modifiers = modifiers, Key = vk, Enabled = true };
        _capturingHotkey = false;
        _hotkeyButton.Content = HotkeyService.Describe(_wizardHotkey);
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_stepIndex - 1);

    private void Skip_Click(object sender, RoutedEventArgs e) => ShowStep(_stepIndex + 1);

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_stepIndex < _steps.Count - 1)
        {
            ApplyCurrentStep();
            ShowStep(_stepIndex + 1);
            return;
        }

        ApplyCurrentStep();
        SelectedFinishAction = FinishAction.OpenPanel;
        CompleteWizard();
    }

    private void ApplyCurrentStep()
    {
        var step = _steps[_stepIndex];

        if (step.Title.StartsWith("Auto-swap", StringComparison.Ordinal))
        {
            SaveProfileIfConfigured();
        }
        else if (step.Title.StartsWith("Open-panel hotkey", StringComparison.Ordinal))
        {
            _settings.Update(s => s.OpenPanelHotkey = _wizardHotkey.Clone());
        }
        else if (step.Title.StartsWith("Start with Windows", StringComparison.Ordinal))
        {
            try
            {
                _startupService.SetEnabled(_startupCheck?.IsChecked == true);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Wizard startup toggle failed: {ex.Message}");
            }
        }
    }

    private void SaveProfileIfConfigured()
    {
        if (_profileProcessCombo is null || _profileMonitorCombo is null)
        {
            return;
        }

        var processName = GetComboText(_profileProcessCombo);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        if (_profileMonitorCombo.SelectedItem is not MonitorInfo monitor)
        {
            return;
        }

        var resolvedTarget = string.Empty;
        if (LauncherCatalog.IsKnownLauncher(processName) && _profileTargetCombo is not null)
        {
            resolvedTarget = GetComboText(_profileTargetCombo);
        }

        _settings.Update(s => s.Profiles.Add(new AppProfile
        {
            ProcessName = processName,
            ResolvedTargetProcessName = resolvedTarget,
            TargetMonitorName = monitor.Name,
            TargetMonitorDeviceName = monitor.DeviceName,
            RestoreOnExit = true,
            Enabled = true,
        }));
    }

    private void CompleteWizard()
    {
        _settings.Update(s => s.FirstRunCompleted = true);
        DialogResult = true;
        Close();
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
}
