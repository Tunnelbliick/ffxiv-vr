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
    private void ShakeTestButton(string label, ShakeSource source)
    {
        if (ImGui.Button(label))
        {
            cameraShake.StartTest(source, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        }
    }

    private unsafe void renderCustomData()
    {
        ImGui.Text($"Shake look-at offset: {cameraShake.RawOffset.X:F3}, {cameraShake.RawOffset.Y:F3}, {cameraShake.RawOffset.Z:F3}");
        ImGui.Text($"Shake camera matrix offset: {cameraShake.RawEyeOffset.X:F3}, {cameraShake.RawEyeOffset.Y:F3}, {cameraShake.RawEyeOffset.Z:F3}");
        ImGui.Text($"Shake offset: look-at {cameraShake.Offset.Length:F3}, matrix {cameraShake.EyeOffset.Length:F3} (peak {cameraShake.PeakOffset:F3})");
        ImGui.Text($"Shake angles: pitch {float.RadiansToDegrees(cameraShake.Pitch):F2}°, yaw {float.RadiansToDegrees(cameraShake.Yaw):F2}°, roll {float.RadiansToDegrees(cameraShake.Roll):F2}° (peak {float.RadiansToDegrees(cameraShake.PeakAngle):F2}°)");
        ImGui.Text("Test camera shake:");
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Fakes a short stomp in the game camera data the plugin reads, before it looks for shake, so each button goes through the same path a real shake from that source takes. Scaled by the Camera shake setting. Look-at only moves the view up and down in the level and floor-locked third-person cameras, and not at all in the free orbit camera, which follows the game camera's own pitch instead.");
        }
        ShakeTestButton("Look-at point", ShakeSource.LookAt);
        ImGui.SameLine();
        ShakeTestButton("Camera position", ShakeSource.CameraPosition);
        ImGui.SameLine();
        ShakeTestButton("Pitch/yaw", ShakeSource.CameraRotation);
        ShakeTestButton("Roll", ShakeSource.CameraRoll);
        ImGui.SameLine();
        ShakeTestButton("Full quake", ShakeSource.Quake);
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