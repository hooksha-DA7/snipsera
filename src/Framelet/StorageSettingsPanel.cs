using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using static Framelet.SettingsUi;

namespace Framelet;

// Save locations for Settings: where screenshots and recordings go, and whether screenshots are saved automatically.
// Every change applies to future saves only; files already saved stay where they are. Folder paths wrap rather than
// being cut off, and each failure is shown next to the folder it concerns.
public sealed class StorageSettingsPanel : StackPanel
{
    private readonly StorageLocations storage;
    private readonly TextBlock imagePath = Folder("ImageFolder"), videoPath = Folder("VideoFolder"),
        imageError = Ui.Text("", 12), videoError = Ui.Text("", 12), notice = Ui.Text("", 12);
    private readonly Border noticeCard;
    private readonly CheckBox automatic;
    private string? imageProblem, videoProblem;
    private bool choosing, attached = true;

    public StorageSettingsPanel(StorageLocations storage)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        AutomationProperties.SetAutomationId(this, "StorageSettings");

        // A recovered or reset configuration is explained here rather than silently changing where files go.
        Describe(notice, "StorageNotice", AutomationLiveSetting.Polite);
        noticeCard = new Border { BorderBrush = Ui.Blue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0), Child = notice };
        Children.Add(noticeCard);

        var images = new StackPanel();
        images.Children.Add(Caption("Folder")); images.Children.Add(imagePath);
        images.Children.Add(Actions(
            Labelled(Ui.Button("Change folder…", "ChangeImageFolder", () => Choose(true)), "Change screenshot folder"),
            Labelled(Ui.Button("Open folder", "OpenImageFolder", () => Open(true)), "Open screenshot folder"),
            Labelled(Ui.Button("Reset to default", "ResetImageFolder", () => Apply(true, () => storage.SetImageFolder(null))), "Reset screenshot folder to default")));
        Describe(imageError, "ImageFolderError", AutomationLiveSetting.Assertive); images.Children.Add(imageError);
        images.Children.Add(Rule());
        automatic = Box("Save screenshots automatically", "AutomaticImageSaving",
            "Each new screenshot is also saved to this folder as soon as you take it. When this is off, use Save on a capture to keep a copy.", images);
        automatic.Click += (_, _) => Apply(true, () => storage.SetAutomaticImageSaving(automatic.IsChecked == true));
        Children.Add(Group("Screenshots", images));

        var videos = new StackPanel();
        videos.Children.Add(Caption("Folder")); videos.Children.Add(videoPath);
        videos.Children.Add(Actions(
            Labelled(Ui.Button("Change folder…", "ChangeVideoFolder", () => Choose(false)), "Change recording folder"),
            Labelled(Ui.Button("Open folder", "OpenVideoFolder", () => Open(false)), "Open recording folder"),
            Labelled(Ui.Button("Reset to default", "ResetVideoFolder", () => Apply(false, () => storage.SetVideoFolder(null))), "Reset recording folder to default")));
        Describe(videoError, "VideoFolderError", AutomationLiveSetting.Assertive); videos.Children.Add(videoError);
        var always = Ui.Text("Videos and GIFs are always saved here automatically when you stop recording.", 12, Ui.Muted); always.Margin = new Thickness(0, 4, 0, 0);
        AutomationProperties.SetAutomationId(always, "RecordingsAlwaysSaved"); videos.Children.Add(always);
        Children.Add(Group("Recordings", videos));

        var future = Ui.Text("Changing a folder applies to future saves. Files you've already saved stay where they are and aren't moved.", 12, Ui.Muted);
        future.Margin = new Thickness(2, 12, 0, 0); AutomationProperties.SetAutomationId(future, "FolderChangeNote"); Children.Add(future);

        storage.Changed += Changed; Appearance.Current.Changed += Sync;
        Sync();
    }

    // Stops listening for storage changes; used when the Settings window closes.
    public void Detach()
    {
        if (!attached) return;
        attached = false; storage.Changed -= Changed; Appearance.Current.Changed -= Sync;
    }

    private void Choose(bool image)
    {
        // The folder dialog is modal; a second click while it is open does nothing.
        if (choosing) return;
        choosing = true;
        try
        {
            string current = image ? storage.ImageFolder : storage.VideoFolder;
            var dialog = new OpenFolderDialog { Title = image ? "Choose where to save screenshots" : "Choose where to save recordings", Multiselect = false };
            if (Directory.Exists(current)) dialog.InitialDirectory = current;
            var owner = Window.GetWindow(this);
            bool chosen = (owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;
            if (chosen) Apply(image, () => { if (image) storage.SetImageFolder(dialog.FolderName); else storage.SetVideoFolder(dialog.FolderName); });
        }
        catch (Exception ex) { Fail(image, ex.Message); }
        finally { choosing = false; }
    }

    private void Open(bool image)
    {
        string folder = image ? storage.ImageFolder : storage.VideoFolder;
        try
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("This folder doesn't exist yet. It's created the first time something is saved there.");
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
            Fail(image, null);
        }
        catch (Exception ex) { Fail(image, ex.Message); }
    }

    // Runs a change; a rejected folder keeps the previous one, so the error is shown and the controls return to the real state.
    private void Apply(bool image, Action change)
    {
        try { change(); Fail(image, null); }
        catch (Exception ex) { Fail(image, ex.Message); }
    }

    private void Fail(bool image, string? message) { if (image) imageProblem = message; else videoProblem = message; Sync(); }

    private void Changed()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Changed)); return; }
        if (attached) Sync();
    }

    private void Sync()
    {
        imagePath.Text = storage.ImageFolder; AutomationProperties.SetName(imagePath, "Screenshot folder: " + storage.ImageFolder);
        videoPath.Text = storage.VideoFolder; AutomationProperties.SetName(videoPath, "Recording folder: " + storage.VideoFolder);
        automatic.IsChecked = storage.AutomaticImageSaving;
        Show(notice, storage.Notice); noticeCard.Visibility = notice.Visibility;
        Show(imageError, imageProblem); Show(videoError, videoProblem);
        imageError.Foreground = videoError.Foreground = UpdateNotice.Error;
    }

    private static TextBlock Caption(string text) { var caption = Ui.Text(text, 12, Ui.Muted); caption.Margin = new Thickness(0, 0, 0, 2); return caption; }
    // Paths can be long and have no spaces; wrapping (rather than trimming) keeps the whole folder readable and selectable by screen readers.
    private static TextBlock Folder(string id)
    {
        var path = Ui.Text("", 13); path.TextWrapping = TextWrapping.Wrap; AutomationProperties.SetAutomationId(path, id);
        return path;
    }
    // The visible label stays short; the accessible name says which folder the action is for.
    private static Button Labelled(Button button, string name) { AutomationProperties.SetName(button, name); return button; }
}
