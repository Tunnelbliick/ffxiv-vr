using System.Diagnostics;

namespace FfxivVR;

public class VRStartStop(
    VRLifecycle vrLifecycle,
    Transitions transitions,
    SteamVRLauncher steamVR,
    Logger logger
)
{
    public void ToggleVR()
    {
        if (vrLifecycle.IsEnabled() || steamVR.IsWaiting)
        {
            StopVR();
        }
        else
        {
            StartVR();
        }
    }
    public void StartVR(bool launchSteamVR = true)
    {
        if (steamVR.IsWaiting)
        {
            logger.Info("Still waiting for SteamVR to start");
            return;
        }
        if (steamVR.NeedsLaunch())
        {
            if (!launchSteamVR)
            {
                logger.Debug("SteamVR is not running, skipping start");
            }
            else if (steamVR.Launch(Now()))
            {
                logger.Info("Starting SteamVR, VR turns on once it's ready");
            }
            else
            {
                logger.Error("SteamVR is not running and could not be started. Start SteamVR, then start VR again.");
            }
            return;
        }
        if (!transitions.PreStartVR())
        {
            return;
        }
        vrLifecycle.EnableVR();
        if (vrLifecycle.IsEnabled())
        {
            transitions.PostStartVR();
        }
        else
        {
            transitions.PostStopVR();
        }
    }
    public void StopVR()
    {
        if (steamVR.IsWaiting)
        {
            steamVR.Cancel();
            logger.Info("Stopped waiting for SteamVR");
            return;
        }
        vrLifecycle.RequestStop();
        transitions.PostStopVR();
    }

    public void RestartVR()
    {
        if (vrLifecycle.IsEnabled())
        {
            vrLifecycle.RequestRestart();
            transitions.PostStopVR();
        }
        else
        {
            StartVR();
        }
    }

    public void Update()
    {
        if (vrLifecycle.TakePendingRestart())
        {
            StartVR();
        }
        if (!steamVR.IsWaiting)
        {
            return;
        }
        switch (steamVR.Poll(Now()))
        {
            case SteamVRLauncher.LaunchStatus.Ready:
                StartVR();
                break;
            case SteamVRLauncher.LaunchStatus.TimedOut:
                logger.Error($"SteamVR did not finish starting within {SteamVRLauncher.TimeoutSeconds:0} seconds. Check that your headset is connected, then start VR again.");
                break;
        }
    }

    private static double Now()
    {
        return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }
}