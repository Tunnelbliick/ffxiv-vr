using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace FfxivVR;

public class DebugWindow : Window
{
    private readonly Debugging debugging;
    private readonly GameState gameState;
    private readonly CameraShake cameraShake;

    public DebugWindow(Debugging debugging, GameState gameState, CameraShake cameraShake) : base("FFXIV VR Debug")
    {
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize;
        Size = new Vector2(450, 700);
        this.debugging = debugging;
        this.gameState = gameState;
        this.cameraShake = cameraShake;
    }

    public override void Draw()
    {
        using (ImRaii.TabBar("tabs"))
        {
            using (var tab = ImRaii.TabItem("Debug"))
            {
                if (tab)
                {
                    var input = debugging.DebugInfo;
                    var toShow = string.Join("\n", input.OrderBy(e => e.Key).Select((entry) => $"{entry.Key}: {entry.Value}"));
                    ImGui.InputTextMultiline("##Debug", ref toShow, 10000, new Vector2(400, 400));
                    var xRotation = debugging.XRotation;
                    if (ImGui.SliderAngle("X Rotation", ref xRotation, -180, 180))
                    {
                        debugging.XRotation = xRotation;
                    }
                    var yRotation = debugging.YRotation;
                    if (ImGui.SliderAngle("Y Rotation", ref yRotation, -180, 180))
                    {
                        debugging.YRotation = yRotation;
                    }
                    var zRotation = debugging.ZRotation;
                    if (ImGui.SliderAngle("Z Rotation", ref zRotation, -180, 180))
                    {
                        debugging.ZRotation = zRotation;
                    }
                }
            }
            using (var tab = ImRaii.TabItem("Controls"))
            {
                if (tab)
                {
                    ImGui.Checkbox("Trace Logging", ref debugging.Trace);
                    ImGui.Checkbox("Always Motion Controls", ref debugging.AlwaysMotionControls);
                    ImGui.Checkbox("Enable tracking in 3rd person", ref debugging.ForceTracking);
                    ImGui.Checkbox("Mirror my synced pose onto my target", ref debugging.SyncLoopback);
                    ImGui.InputInt("Index", ref debugging.Index);
                    ImGui.SliderFloat("Float", ref debugging.Float, -1, 1);
                }
            }
            using (var tab = ImRaii.TabItem("Custom Data"))
            {
                if (tab)
                {
                    renderCustomData();
                }
            }
        }
    }
    private unsafe void renderCustomData()
    {
        ImGui.Text($"Shake look-at offset: {cameraShake.RawOffset.X:F3}, {cameraShake.RawOffset.Y:F3}, {cameraShake.RawOffset.Z:F3}");
        ImGui.Text($"Shake offset: {cameraShake.Offset.Length:F3} (peak {cameraShake.PeakOffset:F3})");
        ImGui.Text($"Shake roll: {float.RadiansToDegrees(cameraShake.RawRoll):F2}°, filtered {float.RadiansToDegrees(cameraShake.Roll):F2}° (peak {float.RadiansToDegrees(cameraShake.PeakRoll):F2}°)");
        if (ImGui.Button("Test camera shake"))
        {
            cameraShake.StartTest(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Plays a short stomp-like shake in VR, scaled by the Camera shake setting");
        var gameCamera = gameState.GetActiveCamera();
        var charBase = gameState.GetCharacterBaseExtended();
        var charOrGpose = gameState.getCharacterOrGpose();
        var currentCamera = gameState.GetCurrentCamera();

        if (gameCamera != null)
        {
            ImGui.Text($"Game Camera Address: 0x{Convert.ToString((long)gameCamera, 16)}");
            ImGui.Text($"DirH: {float.RadiansToDegrees(gameCamera->DirH):F2}°");
            ImGui.Text($"DirV: {float.RadiansToDegrees(gameCamera->DirV):F2}°");
            ImGui.Text($"ZoomMode: {gameCamera->ZoomMode}");
            ImGui.Text($"ControlMode: {gameCamera->ControlMode}");
        }

        if (charOrGpose != null && currentCamera != null)
        {
            ImGui.Text($"CameraOffset.Y: {charOrGpose->GameObject.CameraOffset.Y:F2} == {currentCamera->Position.Y - charOrGpose->Position.Y:F2}");
        }

        if (charBase != null)
        {
            ImGui.Text($"Character Base Address: 0x{Convert.ToString((long)charBase, 16)}");
            ImGui.Text($"Height: {charBase->Height:F2}");
        }
    }
}