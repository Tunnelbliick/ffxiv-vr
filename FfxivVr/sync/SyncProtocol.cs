using Silk.NET.Maths;
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace FfxivVR;

[Flags]
public enum SyncRegions : byte
{
    None = 0,
    Arms = 1,
    UpperBody = 2,
    Legs = 4,
    Spine = 8,
}

public record SyncBone(string Name, SyncRegions Regions);

public static class SyncBones
{
    private const SyncRegions Arm = SyncRegions.Arms | SyncRegions.UpperBody;
    private const SyncRegions Torso = SyncRegions.UpperBody | SyncRegions.Spine;
    public const SyncRegions Known = SyncRegions.Arms | SyncRegions.UpperBody | SyncRegions.Legs | SyncRegions.Spine;

    public static readonly SyncBone[] All = [
        new(HumanBones.SpineA, Torso),
        new(HumanBones.SpineB, Torso),
        new(HumanBones.SpineC, Torso),
        new(HumanBones.Neck, Torso),
        new(HumanBones.ShoulderLeft, Torso),
        new(HumanBones.ArmLeft, Arm),
        new(HumanBones.ForearmLeft, Arm),
        new(HumanBones.HandLeft, Arm),
        new(HumanBones.WristLeft, Arm),
        new(HumanBones.ThumbLeftA, Arm),
        new(HumanBones.ThumbLeftB, Arm),
        new(HumanBones.IndexFingerLeftA, Arm),
        new(HumanBones.IndexFingerLeftB, Arm),
        new(HumanBones.MiddleFingerLeftA, Arm),
        new(HumanBones.MiddleFingerLeftB, Arm),
        new(HumanBones.RingFingerLeftA, Arm),
        new(HumanBones.RingFingerLeftB, Arm),
        new(HumanBones.PinkyFingerLeftA, Arm),
        new(HumanBones.PinkyFingerLeftB, Arm),
        new(HumanBones.ShoulderRight, Torso),
        new(HumanBones.ArmRight, Arm),
        new(HumanBones.ForearmRight, Arm),
        new(HumanBones.HandRight, Arm),
        new(HumanBones.WristRight, Arm),
        new(HumanBones.ThumbRightA, Arm),
        new(HumanBones.ThumbRightB, Arm),
        new(HumanBones.IndexFingerRightA, Arm),
        new(HumanBones.IndexFingerRightB, Arm),
        new(HumanBones.MiddleFingerRightA, Arm),
        new(HumanBones.MiddleFingerRightB, Arm),
        new(HumanBones.RingFingerRightA, Arm),
        new(HumanBones.RingFingerRightB, Arm),
        new(HumanBones.PinkyFingerRightA, Arm),
        new(HumanBones.PinkyFingerRightB, Arm),
        new(HumanBones.Waist, SyncRegions.Legs),
        new(HumanBones.LegLeftA, SyncRegions.Legs),
        new(HumanBones.LegLeftB, SyncRegions.Legs),
        new(HumanBones.LegLeftC, SyncRegions.Legs),
        new(HumanBones.LegLeftD, SyncRegions.Legs),
        new(HumanBones.LegLeftE, SyncRegions.Legs),
        new(HumanBones.LegRightA, SyncRegions.Legs),
        new(HumanBones.LegRightB, SyncRegions.Legs),
        new(HumanBones.LegRightC, SyncRegions.Legs),
        new(HumanBones.LegRightD, SyncRegions.Legs),
        new(HumanBones.LegRightE, SyncRegions.Legs),
    ];

    public static int IndexOf(string name)
    {
        return Array.FindIndex(All, bone => bone.Name == name);
    }
}

public class SyncedPose
{
    public SyncRegions Regions;
    public Quaternion<float>? Head;
    public readonly Quaternion<float>?[] Bones = new Quaternion<float>?[SyncBones.All.Length];
}

public record PoseMessage(ulong Room, ulong Sender, uint Sequence, SyncedPose Pose);

public enum SyncPacketType : byte
{
    Keepalive = 1,
    Pose = 2,
    Welcome = 3,
}

public static class SyncProtocol
{
    public const ushort Magic = 0x5646;
    public const byte Version = 1;
    public const int DefaultPort = 7850;
    public const int MaxPacketSize = 512;
    public const int HeaderSize = 4;
    public const int TokenOffset = 4;
    public const int KeepaliveSize = 20;
    public const int WelcomeSize = 12;
    public const int PoseHeaderSize = 42;

    private const byte HeadFlag = 1;
    private const float ComponentRange = 0.70710678f;
    private const int ComponentBits = 10;
    private const int ComponentMax = (1 << ComponentBits) - 1;
    private const float ComponentHalf = (ComponentMax - 1) / 2f;

    public static uint PackQuaternion(Quaternion<float> rotation)
    {
        var q = Quaternion<float>.Normalize(rotation);
        Span<float> components = [q.X, q.Y, q.Z, q.W];
        var largest = 0;
        for (int i = 1; i < 4; i++)
        {
            if (MathF.Abs(components[i]) > MathF.Abs(components[largest]))
            {
                largest = i;
            }
        }
        var sign = components[largest] < 0 ? -1f : 1f;
        uint packed = (uint)largest << (ComponentBits * 3);
        var shift = ComponentBits * 2;
        for (int i = 0; i < 4; i++)
        {
            if (i == largest)
            {
                continue;
            }
            var normalized = Math.Clamp(components[i] * sign / ComponentRange, -1f, 1f);
            var quantized = (uint)MathF.Round(normalized * ComponentHalf + ComponentHalf);
            packed |= quantized << shift;
            shift -= ComponentBits;
        }
        return packed;
    }

    public static Quaternion<float> UnpackQuaternion(uint packed)
    {
        var largest = (int)(packed >> (ComponentBits * 3));
        Span<float> components = stackalloc float[4];
        var shift = ComponentBits * 2;
        var sumSquares = 0f;
        for (int i = 0; i < 4; i++)
        {
            if (i == largest)
            {
                continue;
            }
            var quantized = (packed >> shift) & ComponentMax;
            var value = Math.Clamp((quantized - ComponentHalf) / ComponentHalf, -1f, 1f) * ComponentRange;
            components[i] = value;
            sumSquares += value * value;
            shift -= ComponentBits;
        }
        components[largest] = MathF.Sqrt(MathF.Max(0f, 1f - sumSquares));
        return Quaternion<float>.Normalize(new Quaternion<float>(components[0], components[1], components[2], components[3]));
    }

    public static ulong RoomId(uint world, uint territory, uint instance, int ward)
    {
        Span<byte> data = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(data, world);
        BinaryPrimitives.WriteUInt32LittleEndian(data[4..], territory);
        BinaryPrimitives.WriteUInt32LittleEndian(data[8..], instance);
        BinaryPrimitives.WriteInt32LittleEndian(data[12..], ward);
        return Hash("ffxivvr-room", data);
    }

    public static ulong PlayerId(ulong contentId, ulong room)
    {
        Span<byte> data = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(data, contentId);
        BinaryPrimitives.WriteUInt64LittleEndian(data[8..], room);
        return Hash("ffxivvr-player", data);
    }

    private static ulong Hash(string prefix, ReadOnlySpan<byte> data)
    {
        var prefixBytes = Encoding.ASCII.GetBytes(prefix);
        var input = new byte[prefixBytes.Length + data.Length];
        prefixBytes.CopyTo(input, 0);
        data.CopyTo(input.AsSpan(prefixBytes.Length));
        return BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(input));
    }

    private static void WriteHeader(Span<byte> buffer, SyncPacketType type)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, Magic);
        buffer[2] = Version;
        buffer[3] = (byte)type;
    }

    public static SyncPacketType? ReadType(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt16LittleEndian(data) != Magic || data[2] != Version)
        {
            return null;
        }
        return (SyncPacketType)data[3];
    }

    public static int WriteKeepalive(Span<byte> buffer, ulong token, ulong room)
    {
        WriteHeader(buffer, SyncPacketType.Keepalive);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[4..], token);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[12..], room);
        return KeepaliveSize;
    }

    public static int WriteWelcome(Span<byte> buffer, ulong token)
    {
        WriteHeader(buffer, SyncPacketType.Welcome);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[4..], token);
        return WelcomeSize;
    }

    public static bool TryReadWelcome(ReadOnlySpan<byte> data, out ulong token)
    {
        token = 0;
        if (ReadType(data) != SyncPacketType.Welcome || data.Length < WelcomeSize)
        {
            return false;
        }
        token = BinaryPrimitives.ReadUInt64LittleEndian(data[4..]);
        return true;
    }

    public static int WritePose(Span<byte> buffer, ulong token, ulong room, ulong sender, uint sequence, SyncedPose pose)
    {
        WriteHeader(buffer, SyncPacketType.Pose);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[4..], token);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[12..], room);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[20..], sender);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[28..], sequence);
        buffer[32] = pose.Head != null ? HeadFlag : (byte)0;
        buffer[33] = (byte)pose.Regions;
        ulong mask = 0;
        for (int i = 0; i < pose.Bones.Length; i++)
        {
            if (pose.Bones[i] != null)
            {
                mask |= 1UL << i;
            }
        }
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[34..], mask);
        var offset = PoseHeaderSize;
        if (pose.Head is { } head)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PackQuaternion(head));
            offset += 4;
        }
        foreach (var bone in pose.Bones)
        {
            if (bone is { } rotation)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buffer[offset..], PackQuaternion(rotation));
                offset += 4;
            }
        }
        return offset;
    }

    public static bool TryReadPose(ReadOnlySpan<byte> data, out PoseMessage? message)
    {
        message = null;
        if (ReadType(data) != SyncPacketType.Pose || data.Length < PoseHeaderSize)
        {
            return false;
        }
        var flags = data[32];
        var mask = BinaryPrimitives.ReadUInt64LittleEndian(data[34..]);
        if (mask >> SyncBones.All.Length != 0)
        {
            return false;
        }
        var hasHead = (flags & HeadFlag) != 0;
        var expectedLength = PoseHeaderSize + (hasHead ? 4 : 0) + 4 * BitOperations.PopCount(mask);
        if (data.Length != expectedLength)
        {
            return false;
        }
        var pose = new SyncedPose
        {
            Regions = (SyncRegions)data[33] & SyncBones.Known,
        };
        var offset = PoseHeaderSize;
        if (hasHead)
        {
            pose.Head = UnpackQuaternion(BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]));
            offset += 4;
        }
        for (int i = 0; i < SyncBones.All.Length; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                pose.Bones[i] = UnpackQuaternion(BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]));
                offset += 4;
            }
        }
        message = new PoseMessage(
            Room: BinaryPrimitives.ReadUInt64LittleEndian(data[12..]),
            Sender: BinaryPrimitives.ReadUInt64LittleEndian(data[20..]),
            Sequence: BinaryPrimitives.ReadUInt32LittleEndian(data[28..]),
            Pose: pose);
        return true;
    }
}