using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;

namespace FfxivVR;

public class SteamVRLauncher(Logger logger)
{
    public enum LaunchStatus
    {
        Waiting,
        Ready,
        TimedOut,
    }

    public const double ReadySeconds = 5;
    public const double TimeoutSeconds = 90;
    private const double PollSeconds = 1;
    private const string SteamVRAppUri = "steam://run/250820";
    private static readonly string[] RequiredProcesses = ["vrserver", "vrcompositor"];

    private double? launchedAt;
    private double? runningSince;
    private double lastPoll = double.NegativeInfinity;

    public bool IsWaiting => launchedAt != null;

    public bool NeedsLaunch()
    {
        return ActiveRuntimePath() is string path && IsSteamVRRuntime(path) && !IsRunning();
    }

    public bool Launch(double now)
    {
        if (ActiveRuntimePath() is not string runtime)
        {
            return false;
        }
        var startup = StartupPath(runtime);
        try
        {
            Process.Start(new ProcessStartInfo(File.Exists(startup) ? startup : SteamVRAppUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e)
        {
            logger.Debug($"Failed to start SteamVR from {startup}: {e.Message}");
            return false;
        }
        launchedAt = now;
        runningSince = null;
        lastPoll = double.NegativeInfinity;
        return true;
    }

    public LaunchStatus Poll(double now)
    {
        if (launchedAt is not double launched)
        {
            return LaunchStatus.Ready;
        }
        if (now - lastPoll >= PollSeconds)
        {
            lastPoll = now;
            runningSince = IsRunning() ? runningSince ?? now : null;
        }
        if (runningSince is double since && now - since >= ReadySeconds)
        {
            Cancel();
            return LaunchStatus.Ready;
        }
        if (now - launched >= TimeoutSeconds)
        {
            Cancel();
            return LaunchStatus.TimedOut;
        }
        return LaunchStatus.Waiting;
    }

    public void Cancel()
    {
        launchedAt = null;
        runningSince = null;
    }

    public static bool IsSteamVRRuntime(string runtimeJson)
    {
        return Path.GetFileName(runtimeJson).StartsWith("steamxr", StringComparison.OrdinalIgnoreCase);
    }

    public static string StartupPath(string runtimeJson)
    {
        return Path.Combine(Path.GetDirectoryName(runtimeJson) ?? "", "bin", "win64", "vrstartup.exe");
    }

    private static string? ActiveRuntimePath()
    {
        if (Environment.GetEnvironmentVariable("XR_RUNTIME_JSON") is { Length: > 0 } overridden)
        {
            return overridden;
        }
        return Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Khronos\OpenXR\1", "ActiveRuntime", null) as string;
    }

    private static bool IsRunning()
    {
        foreach (var name in RequiredProcesses)
        {
            var processes = Process.GetProcessesByName(name);
            var running = processes.Length > 0;
            foreach (var process in processes)
            {
                process.Dispose();
            }
            if (!running)
            {
                return false;
            }
        }
        return true;
    }
}