using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.Havok.Animation.Rig;
using Silk.NET.Maths;
using System;

namespace FfxivVR;

public unsafe class SyncSkeletonModifier(
    SkeletonModifier skeletonModifier
)
{
    private const float NeckShare = 0.35f;
    private static readonly float MaxHeadYaw = float.DegreesToRadians(80);
    private static readonly float MaxHeadPitch = float.DegreesToRadians(60);
    private static readonly float MaxHeadRoll = float.DegreesToRadians(40);
    private static readonly Quaternion<float> VRToModelForward = MathFactory.YRotation(MathF.PI);
    private static readonly int NeckIndex = SyncBones.IndexOf(HumanBones.Neck);

    public static Quaternion<float> ComputeHeadRotation(Quaternion<float> skeletonWorldRotation, float cameraYRotation, Quaternion<float> hmdOrientation)
    {
        var skeletonRotation = MathFactory.YRotation(cameraYRotation) / skeletonWorldRotation;
        return Quaternion<float>.Normalize(skeletonRotation * hmdOrientation * VRToModelForward.Inverse());
    }

    public static Quaternion<float> ClampHeadRotation(Quaternion<float> head)
    {
        var (yaw, pitch, roll) = head.ToYawPitchRoll();
        return Quaternion<float>.CreateFromYawPitchRoll(
            Math.Clamp(yaw, -MaxHeadYaw, MaxHeadYaw),
            Math.Clamp(pitch, -MaxHeadPitch, MaxHeadPitch),
            Math.Clamp(roll, -MaxHeadRoll, MaxHeadRoll));
    }

    public SyncedPose? Capture(Skeleton* skeleton, SyncRegions regions, Quaternion<float>? head)
    {
        if (skeleton == null)
        {
            return null;
        }
        var pose = SkeletonModifier.GetPose(skeleton);
        if (pose == null)
        {
            return null;
        }
        if (skeletonModifier.GetSkeletonStructure(skeleton) is not { } structure)
        {
            return null;
        }
        var synced = new SyncedPose
        {
            Regions = regions,
            Head = head,
        };
        for (int i = 0; i < SyncBones.All.Length; i++)
        {
            var syncBone = SyncBones.All[i];
            if ((syncBone.Regions & regions) == 0)
            {
                continue;
            }
            if (structure.GetBone(syncBone.Name) is not Bone bone)
            {
                continue;
            }
            synced.Bones[i] = bone.GetModelTransforms(pose)->Rotation.ToQuaternion();
        }
        return synced;
    }

    public void Apply(Skeleton* skeleton, SyncedPose synced, float weight)
    {
        if (skeleton == null || weight <= 0)
        {
            return;
        }
        var pose = SkeletonModifier.GetPose(skeleton);
        if (pose == null)
        {
            return;
        }
        if (skeletonModifier.GetSkeletonStructure(skeleton) is not { } structure)
        {
            return;
        }
        if (structure.GetBone(HumanBones.Neck) == null || structure.GetBone(HumanBones.ArmLeft) == null)
        {
            return;
        }
        ResetRegions(pose, structure, synced.Regions, weight);
        for (int i = 0; i < SyncBones.All.Length; i++)
        {
            if (synced.Bones[i] is not { } rotation)
            {
                continue;
            }
            if (structure.GetBone(SyncBones.All[i].Name) is not Bone bone)
            {
                continue;
            }
            skeletonModifier.SetModelRotation(bone, pose, rotation, weight);
        }
        if (synced.Head is { } head)
        {
            ApplyHead(pose, structure, head, synced.Bones[NeckIndex] != null, weight);
        }
    }

    private void ResetRegions(hkaPose* pose, SkeletonStructure structure, SyncRegions regions, float weight)
    {
        Func<string, bool> notWeapon = b => !b.Contains(HumanBones.WeaponBoneSubstring);
        if ((regions & SyncRegions.UpperBody) != 0)
        {
            skeletonModifier.ResetRotationTree(pose, structure, HumanBones.SpineA, b => notWeapon(b) && b != HumanBones.Face, weight);
        }
        else if ((regions & SyncRegions.Arms) != 0)
        {
            skeletonModifier.ResetRotationTree(pose, structure, HumanBones.ArmLeft, notWeapon, weight);
            skeletonModifier.ResetRotationTree(pose, structure, HumanBones.ArmRight, notWeapon, weight);
        }
        if ((regions & SyncRegions.Legs) != 0)
        {
            skeletonModifier.ResetRotationTree(pose, structure, HumanBones.LegLeftA, notWeapon, weight);
            skeletonModifier.ResetRotationTree(pose, structure, HumanBones.LegRightA, notWeapon, weight);
        }
    }

    private void ApplyHead(hkaPose* pose, SkeletonStructure structure, Quaternion<float> head, bool neckSynced, float weight)
    {
        if (structure.GetBone(HumanBones.Face) is not Bone face)
        {
            return;
        }
        var clamped = ClampHeadRotation(head);
        if (!neckSynced && structure.GetBone(HumanBones.Neck) is Bone neck)
        {
            var neckRotation = Quaternion<float>.Slerp(Quaternion<float>.Identity, clamped, NeckShare);
            skeletonModifier.SetModelRotation(neck, pose, neckRotation * structure.GetReferenceModelRotation(neck), weight);
        }
        skeletonModifier.SetModelRotation(face, pose, clamped * structure.GetReferenceModelRotation(face), weight);
    }
}