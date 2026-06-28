using System.Windows;

using PrimaryDisplaySwap.Controls;
using PrimaryDisplaySwap.Models;
using PrimaryDisplaySwap.Services;

using MessageBox = System.Windows.MessageBox;

namespace PrimaryDisplaySwap;

public partial class ProfilesWindow : Window
{
    private readonly DisplayManager _displayManager;
    private readonly SettingsService _settings;
    private readonly ProfileEditorControl _profileEditor;

    public ProfilesWindow(DisplayManager displayManager, SettingsService settings)
    {
        _displayManager = displayManager;
        _settings = settings;

        InitializeComponent();

        _profileEditor = new ProfileEditorControl(_displayManager, _settings);
        ProfileEditorPanel.Child = _profileEditor;

        _profileEditor.Saved += (_, _) => OnEditorClosed();
        _profileEditor.Cancelled += (_, _) => OnEditorClosed();
        _profileEditor.StatusChanged += (_, message) => SetStatus(message);

        RebuildProfileList();
    }

    public void BeginAddProfile()
    {
        ShowEditor(() => _profileEditor.BeginNew());
    }

    public void BeginEditProfile(string profileId)
    {
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
    }

    private void ShowEditor(Action begin)
    {
        begin();
        ProfileEditorPanel.Visibility = Visibility.Visible;
        ProfilesScroll.Visibility = Visibility.Collapsed;
        AddProfileButton.IsEnabled = false;
        UpdateEmptyState();
        ProfileEditorPanel.BringIntoView();
    }

    private void OnEditorClosed()
    {
        ProfileEditorPanel.Visibility = Visibility.Collapsed;
        ProfilesScroll.Visibility = Visibility.Visible;
        AddProfileButton.IsEnabled = true;
        RebuildProfileList();
    }

    private void RebuildProfileList()
    {
        ProfileListPanel.Children.Clear();

        var profiles = _settings.Current.Profiles;
        var editing = _profileEditor.IsEditing;

        NoProfilesText.Visibility = profiles.Count == 0 && !editing
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

        foreach (var profile in profiles)
        {
            ProfileListPanel.Children.Add(ProfileUiHelper.BuildProfileRow(
                profile,
                monitors,
                _settings.Current,
                Resources,
                SetProfileEnabled,
                BeginEditProfile,
                RemoveProfile,
                TestProfile));
        }

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var editing = _profileEditor.IsEditing;
        ListHintText.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
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

    private void AddProfile_Click(object sender, RoutedEventArgs e) => BeginAddProfile();

    private void SetStatus(string message) => StatusText.Text = message;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
