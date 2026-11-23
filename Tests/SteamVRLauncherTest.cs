namespace FfxivVR.Tests;

public class SteamVRLauncherTests
{
    private const string SteamVRRuntime = @"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\steamxr_win64.json";

    [Test]
    public void RecognizesTheSteamVRRuntime()
    {
        Assert.That(SteamVRLauncher.IsSteamVRRuntime(SteamVRRuntime), Is.True);
        Assert.That(SteamVRLauncher.IsSteamVRRuntime(@"C:\Program Files\Oculus\Support\oculus-runtime\oculus_openxr_64.json"), Is.False);
        Assert.That(SteamVRLauncher.IsSteamVRRuntime(@"C:\Program Files\VirtualDesktop.Streamer\OpenXR\virtualdesktop-openxr.json"), Is.False);
    }

    [Test]
    public void FindsVRStartupNextToTheRuntime()
    {
        Assert.That(SteamVRLauncher.StartupPath(SteamVRRuntime), Is.EqualTo(@"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\vrstartup.exe"));
    }
}