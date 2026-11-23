using FFXIVClientStructs.Havok.Animation.Rig;
using FFXIVClientStructs.Havok.Common.Base.Math.QsTransform;
using Silk.NET.Maths;
using System.Collections.Generic;

namespace FfxivVR;

public unsafe class BodySolver(
    SkeletonModifier skeletonModifier
)
{
    private static readonly string[] SpineBones = [HumanBones.SpineA, HumanBones.SpineB, HumanBones.SpineC];
    private readonly Dictionary<SkeletonStructure, Bone[]> touchedBones = new();

    public Bone[] GetTouchedBones(SkeletonStructure structure)
    {
        if (touchedBones.TryGetValue(structure, out var cached))
        {
            return cached;
        }
        var bones = new List<Bone>();
        string[] torso = [.. SpineBones, HumanBones.Neck, HumanBones.ShoulderLeft, HumanBones.ShoulderRight];
        foreach (var name in torso)
        {
            if (structure.GetBone(name) is Bone bone)
            {
                bones.Add(bone);
            }
        }
        string[] arms = [HumanBones.ArmLeft, HumanBones.ArmRight];
        foreach (var arm in arms)
        {
            if (structure.GetBone(arm) is Bone bone)
            {
                bones.Add(bone);
                bones.AddRange(structure.GetDescendants(bone, b => !b.Contains(HumanBones.WeaponBoneSubstring)));
            }
        }
        var result = bones.ToArray();
        touchedBones[structure] = result;
        return result;
    }

    public static hkQsTransformf[] SaveLocalTransforms(hkaPose* pose, Bone[] bones)
    {
        var saved = new hkQsTransformf[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            saved[i] = *bones[i].GetLocalTransforms(pose);
        }
        return saved;
    }

    public static void RestoreLocalTransforms(hkaPose* pose, Bone[] bones, hkQsTransformf[] saved)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            *bones[i].GetLocalTransforms(pose) = saved[i];
        }
    }

    public void Solve(hkaPose* pose, SkeletonStructure structure, Vector3D<float> displacement, Quaternion<float> head, Vector3D<float>? leftWrist, Vector3D<float>? rightWrist)
    {
        if (structure.GetBone(HumanBones.Neck) is not Bone neck || structure.GetBone(HumanBones.Face) is not Bone face)
        {
            return;
        }
        var spine = new Bone[SpineBones.Length];
        var joints = new Vector3D<float>[SpineBones.Length];
        var rotations = new Quaternion<float>[SpineBones.Length];
        for (int i = 0; i < SpineBones.Length; i++)
        {
            if (structure.GetBone(SpineBones[i]) is not Bone bone)
            {
                return;
            }
            var transforms = bone.GetModelTransforms(pose);
            spine[i] = bone;
            joints[i] = transforms->Translation.ToVector3D();
            rotations[i] = transforms->Rotation.ToQuaternion();
        }
        var neckPosition = neck.GetModelTransforms(pose)->Translation.ToVector3D();
        var neckRotation = SyncSkeletonModifier.NeckRotation(head) * structure.GetReferenceModelRotation(neck);
        var faceOffset = Vector3D.Transform(face.ReferencePose.Translation.ToVector3D(), neckRotation);
        var target = neckPosition + faceOffset + displacement;
        var yaw = SyncSkeletonModifier.ClampHeadRotation(head).ToYawPitchRoll().yaw;

        var deltas = BodyIK.SolveSpine(joints, neckPosition, faceOffset, target, yaw);
        for (int i = 0; i < spine.Length; i++)
        {
            skeletonModifier.SetModelRotation(spine[i], pose, deltas[i] * rotations[i], 1);
        }
        skeletonModifier.SetModelRotation(neck, pose, neckRotation, 1);

        AssistShoulder(pose, structure, HumanBones.ShoulderLeft, HumanBones.ArmLeft, HumanBones.ForearmLeft, HumanBones.HandLeft, leftWrist);
        AssistShoulder(pose, structure, HumanBones.ShoulderRight, HumanBones.ArmRight, HumanBones.ForearmRight, HumanBones.HandRight, rightWrist);
    }

    private void AssistShoulder(hkaPose* pose, SkeletonStructure structure, string shoulderName, string armName, string forearmName, string handName, Vector3D<float>? wrist)
    {
        if (wrist is not { } target)
        {
            return;
        }
        if (structure.GetBone(shoulderName) is not Bone shoulder
            || structure.GetBone(armName) is not Bone arm
            || structure.GetBone(forearmName) is not Bone forearm
            || structure.GetBone(handName) is not Bone hand)
        {
            return;
        }
        var shoulderTransforms = shoulder.GetModelTransforms(pose);
        var shoulderPosition = shoulderTransforms->Translation.ToVector3D();
        var shoulderRotation = shoulderTransforms->Rotation.ToQuaternion();
        var armPosition = arm.GetModelTransforms(pose)->Translation.ToVector3D();
        var forearmPosition = forearm.GetModelTransforms(pose)->Translation.ToVector3D();
        var handPosition = hand.GetModelTransforms(pose)->Translation.ToVector3D();
        var armLength = (forearmPosition - armPosition).Length + (handPosition - forearmPosition).Length;
        var assist = BodyIK.ShoulderAssist(shoulderPosition, armPosition, armLength, target);
        skeletonModifier.SetModelRotation(shoulder, pose, assist * shoulderRotation, 1);
    }
}