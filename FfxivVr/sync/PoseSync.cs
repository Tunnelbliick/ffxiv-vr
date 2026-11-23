using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FfxivVR;

public unsafe class PoseSync(
    Configuration configuration,
    IObjectTable objectTable,
    IClientState clientState,
    ITargetManager targetManager,
    GameState gameState,
    SyncSkeletonModifier syncSkeletonModifier,
    PoseSyncClient client,
    Debugging debugging
)
{
    private const double PoseIntervalSeconds = 1.0 / 30;
    private const double KeepaliveIntervalSeconds = 1;

    private readonly Dictionary<ulong, RemotePose> remotes = new();
    private readonly Dictionary<ulong, ulong> playerIds = new();
    private readonly RemotePose loopback = new();
    private readonly byte[] sendBuffer = new byte[SyncProtocol.MaxPacketSize];
    private (uint world, uint territory, uint instance, int ward)? roomKey;
    private ulong room;
    private uint sequence;
    private double lastPoseSent = double.NegativeInfinity;
    private double lastKeepalive = double.NegativeInfinity;

    public int ActivePlayers { get; private set; }
    public PoseSyncStatus Status => client.Status;

    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private bool IsEnabled => (configuration.SyncSharePose || configuration.SyncShowOthers) && configuration.SyncServer.Trim().Length > 0;

    private bool IsSharingDue(double now) => configuration.SyncSharePose && IsEnabled && client.Token != 0 && room != 0 && now - lastPoseSent >= PoseIntervalSeconds;

    public bool WantsLocalPose()
    {
        if (!IsSharingDue(Now) && !debugging.SyncLoopback)
        {
            return false;
        }
        return !gameState.IsGPosing() && !gameState.IsInCutscene();
    }

    public void PublishLocalPose(SyncedPose pose)
    {
        var now = Now;
        if (debugging.SyncLoopback)
        {
            loopback.Receive(sequence, pose, now);
        }
        if (IsSharingDue(now) && LocalContentId() is { } contentId)
        {
            var length = SyncProtocol.WritePose(sendBuffer, client.Token, room, SyncProtocol.PlayerId(contentId, room), sequence, pose);
            client.Send(sendBuffer.AsSpan(0, length));
            lastPoseSent = now;
        }
        sequence++;
    }

    public void Update()
    {
        var now = Now;
        UpdateConnection();
        UpdateRoom();
        ReceiveMessages(now);
        SendKeepalive(now);
        ApplyRemotePoses(now);
        ApplyLoopback(now);
    }

    private void UpdateConnection()
    {
        if (IsEnabled && clientState.IsLoggedIn)
        {
            client.Connect(configuration.SyncServer.Trim());
        }
        else
        {
            client.Disconnect();
            remotes.Clear();
            ActivePlayers = 0;
        }
    }

    private void UpdateRoom()
    {
        var player = objectTable.LocalPlayer;
        if (player == null)
        {
            return;
        }
        var housing = HousingManager.Instance();
        var ward = housing != null ? (int)housing->GetCurrentWard() : -1;
        var key = (player.CurrentWorld.RowId, (uint)clientState.TerritoryType, (uint)clientState.Instance, ward);
        if (roomKey == key)
        {
            return;
        }
        roomKey = key;
        room = SyncProtocol.RoomId(key.Item1, key.Item2, key.Item3, key.ward);
        playerIds.Clear();
        remotes.Clear();
    }

    private void ReceiveMessages(double now)
    {
        while (client.Incoming.TryDequeue(out var message))
        {
            if (!configuration.SyncShowOthers || message.Room != room)
            {
                continue;
            }
            if (!remotes.TryGetValue(message.Sender, out var remote))
            {
                remote = new RemotePose();
                remotes[message.Sender] = remote;
            }
            remote.Receive(message.Sequence, message.Pose, now);
        }
        List<ulong>? expired = null;
        foreach (var (sender, remote) in remotes)
        {
            if (remote.IsExpired(now))
            {
                (expired ??= new()).Add(sender);
            }
        }
        expired?.ForEach(sender => remotes.Remove(sender));
    }

    private void SendKeepalive(double now)
    {
        if (!IsEnabled || room == 0 || now - lastKeepalive < KeepaliveIntervalSeconds)
        {
            return;
        }
        var length = SyncProtocol.WriteKeepalive(sendBuffer, client.Token, room);
        client.Send(sendBuffer.AsSpan(0, length));
        lastKeepalive = now;
    }

    private ulong? LocalContentId()
    {
        var player = objectTable.LocalPlayer;
        if (player == null)
        {
            return null;
        }
        var contentId = ((Character*)player.Address)->ContentId;
        return contentId == 0 ? null : contentId;
    }

    private bool CanApply()
    {
        return !gameState.IsInCutscene() && !gameState.IsGPosing() && !gameState.IsBetweenAreas();
    }

    private void ApplyRemotePoses(double now)
    {
        var active = 0;
        if (configuration.SyncShowOthers && remotes.Count > 0 && CanApply())
        {
            var localContentId = LocalContentId();
            foreach (var player in objectTable.PlayerObjects)
            {
                var character = (Character*)player.Address;
                var contentId = character->ContentId;
                if (contentId == 0 || contentId == localContentId)
                {
                    continue;
                }
                if (!playerIds.TryGetValue(contentId, out var id))
                {
                    id = SyncProtocol.PlayerId(contentId, room);
                    playerIds[contentId] = id;
                }
                if (!remotes.TryGetValue(id, out var remote) || remote.Advance(now) is not var (pose, weight))
                {
                    continue;
                }
                ApplyTo(character, pose, weight);
                active++;
            }
        }
        ActivePlayers = active;
    }

    private void ApplyLoopback(double now)
    {
        if (!debugging.SyncLoopback || !CanApply())
        {
            return;
        }
        if (loopback.Advance(now) is not var (pose, weight))
        {
            return;
        }
        var target = targetManager.Target;
        if (target == null || target.Address == objectTable.LocalPlayer?.Address)
        {
            return;
        }
        var gameObject = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;
        if (!gameObject->IsCharacter())
        {
            return;
        }
        ApplyTo((Character*)gameObject, pose, weight);
    }

    private void ApplyTo(Character* character, SyncedPose pose, float weight)
    {
        var drawObject = character->GameObject.DrawObject;
        if (drawObject == null || drawObject->GetObjectType() != ObjectType.CharacterBase)
        {
            return;
        }
        var characterBase = (CharacterBase*)drawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human)
        {
            return;
        }
        syncSkeletonModifier.Apply(characterBase->Skeleton, pose, weight);
    }
}