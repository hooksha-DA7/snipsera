using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Framelet;

internal enum InstallKind { GitHub, Store, OtherPackage }

internal static class Installation
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
    internal static InstallKind Kind()
    {
        uint length = 0;
        int result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        if (result == 15700) return InstallKind.GitHub;
        if (result != 122) return InstallKind.OtherPackage;
        try { return Package.Current.Id.Name == AppIdentity.StoreName && Package.Current.Id.Publisher == AppIdentity.StorePublisher ? InstallKind.Store : InstallKind.OtherPackage; }
        catch (InvalidOperationException) { return InstallKind.OtherPackage; }
        catch (COMException) { return InstallKind.OtherPackage; }
    }
}

internal sealed class PackagedStartupRegistration : IStartupRegistration
{
    internal const string TaskId = "SnipseraStartup";
    public async Task<StartupStatus> ReadAsync() => Status((await StartupTask.GetAsync(TaskId)).State);
    public async Task<StartupStatus> SetAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (!enabled) task.Disable();
        else if (task.State == StartupTaskState.Disabled) return Status(await task.RequestEnableAsync());
        return Status(task.State);
    }
    internal static StartupStatus Status(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => new(true, true, "Snipsera will start quietly in the tray when you sign in."),
        StartupTaskState.Disabled => new(false, true, "Off. Snipsera starts when you open it."),
        StartupTaskState.DisabledByUser => new(false, false, "Startup is disabled in Windows. Enable Snipsera in Startup apps to turn it back on.", true),
        StartupTaskState.DisabledByPolicy => new(false, false, "Your Windows policy has disabled startup for this app.", true),
        StartupTaskState.EnabledByPolicy => new(true, false, "Your Windows policy controls startup for this app.", true),
        _ => new(false, false, "Windows startup status is unavailable.", true)
    };
}

// Small storage seam lets checks use an in-memory registry, never the user's startup configuration.
internal interface IStartupValues
{
    string? Command { get; }
    byte[]? Approval { get; }
    void SetCommand(string command);
    void RemoveCommand();
}
internal sealed class StartupRegistryValues : IStartupValues
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    internal const string ValueName = "SnipseraStartup";
    public string? Command { get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string; } }
    public byte[]? Approval { get { using var key = Registry.CurrentUser.OpenSubKey(ApprovalKey); return key?.GetValue(ValueName) as byte[]; } }
    public void SetCommand(string command) { using var key = Registry.CurrentUser.CreateSubKey(RunKey); key.SetValue(ValueName, command, RegistryValueKind.String); }
    public void RemoveCommand() { using var key = Registry.CurrentUser.OpenSubKey(RunKey, true); key?.DeleteValue(ValueName, false); }
}

internal sealed class UnpackagedStartupRegistration(IStartupValues values, string? executable) : IStartupRegistration
{
    internal static string CommandLine(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.GetFileName(executable).Equals("Snipsera.exe", StringComparison.OrdinalIgnoreCase) || executable.IndexOfAny(['"', '\r', '\n']) >= 0)
            throw new InvalidOperationException("Use the installed or portable Snipsera EXE to enable startup.");
        string command = "\"" + executable + "\" --startup";
        if (command.Length > 260) throw new InvalidOperationException("Move Snipsera to a shorter folder path before enabling startup.");
        return command;
    }
    internal static bool BlockedByWindows(byte[]? approval) => approval is { Length: > 0 } && approval[0] is not (0 or 2 or 6);
    public Task<StartupStatus> ReadAsync()
    {
        if (executable is null) return Task.FromResult(new StartupStatus(false, false, "Use the installed or portable Snipsera EXE to enable startup."));
        string command;
        try { command = CommandLine(executable); }
        catch (InvalidOperationException ex) { return Task.FromResult(new StartupStatus(false, false, ex.Message)); }
        string? current = values.Command;
        bool registered = string.Equals(current, command, StringComparison.OrdinalIgnoreCase);
        if (BlockedByWindows(values.Approval) && string.IsNullOrEmpty(current)) return Task.FromResult(new StartupStatus(false, true,
            "Off. Turn this on to add Snipsera to Windows Startup apps, then enable it there.", true));
        if (BlockedByWindows(values.Approval)) return Task.FromResult(new StartupStatus(false, false,
            "Startup is disabled in Windows. Enable Snipsera in Startup apps to turn it back on.", true));
        return Task.FromResult(new StartupStatus(registered, true, registered ? "Snipsera will start quietly in the tray when you sign in. Keep portable files in the same folder."
            : string.IsNullOrEmpty(current) ? "Off. Snipsera starts when you open it." : "Another Snipsera copy is registered. Turning this on will use this copy instead.", true));
    }
    public async Task<StartupStatus> SetAsync(bool enabled)
    {
        if (enabled)
        {
            var current = await ReadAsync(); if (!current.CanChange) return current;
            values.SetCommand(CommandLine(executable!));
        }
        else if (executable is not null && string.Equals(values.Command, CommandLine(executable), StringComparison.OrdinalIgnoreCase)) values.RemoveCommand();
        return await ReadAsync();
    }
}

internal sealed class UnavailableStartupRegistration : IStartupRegistration
{
    public Task<StartupStatus> ReadAsync() => Task.FromResult(new StartupStatus(false, false, "Startup is unavailable for an isolated test profile."));
    public Task<StartupStatus> SetAsync(bool enabled) => ReadAsync();
}
