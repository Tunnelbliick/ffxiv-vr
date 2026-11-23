using FFXIVClientStructs.Havok.Animation.Rig;
using Silk.NET.Maths;
using Silk.NET.OpenXR;

namespace FfxivVR;

public unsafe class ControllerTrackingSkeletonModifier(
    SkeletonModifier skeletonModifier
)
{
    internal void Apply(hkaPose* pose, SkeletonStructure structure, Quaternion<float> skeletonRotation, Vector3D<float> head, PalmPose palmPose)
    {
        skeletonModifier.ResetPoseTree(pose, structure, HumanBones.ArmLeft, b => !b.Contains(HumanBones.WeaponBoneSubstring));
        skeletonModifier.ResetPoseTree(pose, structure, HumanBones.ArmRight, b => !b.Contains(HumanBones.WeaponBoneSubstring));
        var (leftWrist, rightWrist) = GetWrists(palmPose);
        if (leftWrist is Posef left)
        {
            skeletonModifier.UpdateArmIK(left, pose, structure, head, HumanBones.ArmLeft, HumanBones.ForearmLeft, HumanBones.HandLeft, skeletonRotation);
            skeletonModifier.RotateHand(left, HumanBones.HandLeft, HumanBones.WristLeft, structure, pose, skeletonRotation);
        }

        if (rightWrist is Posef right)
        {
            skeletonModifier.UpdateArmIK(right, pose, structure, head, HumanBones.ArmRight, HumanBones.ForearmRight, HumanBones.HandRight, skeletonRotation);
            skeletonModifier.RotateHand(right, HumanBones.HandRight, HumanBones.WristRight, structure, pose, skeletonRotation);
        }
    }

    internal static (Posef? left, Posef? right) GetWrists(PalmPose palmPose)
    {
        Posef? left = palmPose.LeftPalm is Posef leftController ? ControllerToWrist(leftController, MathFactory.ZRotation(float.DegreesToRadians(90))) : null;
        Posef? right = palmPose.RightPalm is Posef rightController ? ControllerToWrist(rightController, MathFactory.ZRotation(float.DegreesToRadians(-90))) : null;
        return (left, right);
    }

    private static Posef ControllerToWrist(Posef leftController, Quaternion<float> rotation)
    {
        var wristRotation = leftController.Orientation.ToQuaternion() * rotation;
        var wristPosition = leftController.Position.ToVector3D() - Vector3D.Transform(new Vector3D<float>(0, 0, -0.08f), wristRotation);
        return new Posef(wristRotation.ToQuaternionf(), wristPosition.ToVector3f());
    }
}