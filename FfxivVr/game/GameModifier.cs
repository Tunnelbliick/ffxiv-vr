using Dalamud.Game.Gui.NamePlate;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using Silk.NET.Maths;
using Silk.NET.OpenXR;
using System.Collections.Generic;

namespace FfxivVR;

public unsafe class GameModifier(
    GameState gameState,
    NameplateModifier nameplateModifier,
    SkeletonModifier skeletonModifier,
    BodySkeletonModifier bodySkeletonModifier,
    HandTrackingSkeletonModifier handTrackingSkeletonModifier,
    ControllerTrackingSkeletonModifier controllerTrackingSkeletonModifier,
    Configuration configuration,
    FirstPersonManager firstPersonManager,
    SyncSkeletonModifier syncSkeletonModifier,
    BodySolver bodySolver
)
{
    private enum TrackingSource
    {
        None,
        Body,
        Hands,
        Controllers,
    }

    public void HideHeadMesh()
    {
        if (gameState.IsInCutscene())
        {
            return;
        }
        if (!firstPersonManager.IsFirstPerson)
        {
            return;
        }
        var characterBase = gameState.GetCharacterBase();
        if (characterBase == null)
        {
            return;
        }
        var skeleton = characterBase->Skeleton;
        skeletonModifier.HideHead(skeleton, Conditions.Instance()->Mounted);
    }

    private const int HeadAttachBoneIndex = 1;

    internal bool IsHiddenHeadAttachBone(GameObject* gameObject, int attachBoneIndex)
    {
        if (attachBoneIndex != HeadAttachBoneIndex || !firstPersonManager.IsFirstPerson || gameState.IsInCutscene())
        {
            return false;
        }
        var characterBase = gameState.GetCharacterBase();
        return characterBase != null && (CharacterBase*)gameObject->DrawObject == characterBase;
    }


    // Test with Alte Roite mount
    // Stand on a slope with a mount
    public Matrix4X4<float>? GetCharacterPositionTransform()
    {
        var characterBase = gameState.GetCharacterBase();
        if (characterBase == null)
        {
            return null;
        }
        var character = gameState.getCharacterOrGpose();
        if (character == null)
        {
            return null;
        }
        var isDismounting = (character->Mount.Flags & 1) != 0;
        Matrix4X4<float> headTransforms;
        if (character->Mount.MountObject != null && character->Mount.MountObject->DrawObject != null && !isDismounting)
        {
            var mountPosition = character->Mount.MountObject->DrawObject->Position.ToVector3D();
            var mountTransform = skeletonModifier.GetMountTransform(character->Mount.MountObject) ?? Matrix4X4.CreateFromQuaternion(characterBase->DrawObject.Rotation.ToQuaternion());
            var worldTransform = MathFactory.CreateScaleRotationTranslationMatrix(characterBase->Scale.ToVector3D(), Quaternion<float>.Identity, mountPosition);
            headTransforms = mountTransform * worldTransform;
        }
        else
        {
            headTransforms = MathFactory.CreateScaleRotationTranslationMatrix(characterBase->Scale.ToVector3D(), characterBase->DrawObject.Rotation.ToQuaternion(), characterBase->Position.ToVector3D());
        }
        var actorModel = gameState.GetCharacterBaseExtended();
        var actorScale = Matrix4X4.CreateScale(actorModel->Height);
        var skeleton = characterBase->Skeleton;
        var headPosition = skeletonModifier.GetHeadPosition(skeleton);
        return actorScale * headTransforms;
    }

    internal Vector3D<float>? GetHeadOffset()
    {
        var characterBase = gameState.GetCharacterBase();
        if (characterBase == null)
        {
            return null;
        }
        var skeleton = characterBase->Skeleton;
        return skeletonModifier.GetHeadPosition(skeleton);
    }

    internal void UpdateMotionControls(VRInputData vrInputData, RuntimeAdjustments runtimeAdjustments, float cameraYRotation)
    {
        if (!TryGetTrackedSkeleton(cameraYRotation, out _, out var pose, out var structure, out var skeletonRotation, out var head))
        {
            return;
        }
        ApplyTracking(GetTrackingSource(vrInputData), vrInputData, runtimeAdjustments, pose, structure!, skeletonRotation, head);
    }

    internal SyncedPose? CaptureSyncPose(VRInputData vrInputData, RuntimeAdjustments runtimeAdjustments, float cameraYRotation, Posef hmd, bool lean)
    {
        if (!TryGetTrackedSkeleton(cameraYRotation, out var skeleton, out var pose, out var structure, out var skeletonRotation, out var head))
        {
            return null;
        }
        var source = GetTrackingSource(vrInputData);
        var regions = source switch
        {
            TrackingSource.Body => SyncRegions.UpperBody,
            TrackingSource.Hands or TrackingSource.Controllers => SyncRegions.Arms,
            _ => SyncRegions.None,
        };
        var hmdOrientation = hmd.Orientation.ToQuaternion();
        var headRotation = SyncSkeletonModifier.ComputeHeadRotation(skeleton->Transform.Rotation.ToQuaternion(), cameraYRotation, hmdOrientation);
        if (!lean || source == TrackingSource.Body)
        {
            return syncSkeletonModifier.Capture(skeleton, regions, headRotation);
        }
        var bones = bodySolver.GetTouchedBones(structure!);
        var saved = BodySolver.SaveLocalTransforms(pose, bones);
        var displacement = BodyIK.ClampLean(Vector3D.Transform(BodyIK.HeadDisplacement(hmd.Position.ToVector3D(), hmdOrientation), skeletonRotation));
        var (leftWrist, rightWrist) = GetWrists(source, vrInputData);
        Vector3D<float>? ToModel(Posef? wrist) => wrist is Posef w ? Vector3D.Transform(w.Position.ToVector3D(), skeletonRotation) + head : null;
        bodySolver.Solve(pose, structure!, displacement, headRotation, ToModel(leftWrist), ToModel(rightWrist));
        ApplyTracking(source, vrInputData, runtimeAdjustments, pose, structure!, skeletonRotation, head);
        var synced = syncSkeletonModifier.Capture(skeleton, regions | SyncRegions.Spine, headRotation);
        BodySolver.RestoreLocalTransforms(pose, bones, saved);
        return synced;
    }

    private bool TryGetTrackedSkeleton(float cameraYRotation, out Skeleton* skeleton, out hkaPose* pose, out SkeletonStructure? structure, out Quaternion<float> skeletonRotation, out Vector3D<float> head)
    {
        skeleton = null;
        pose = null;
        structure = null;
        skeletonRotation = Quaternion<float>.Identity;
        head = default;
        var characterBase = gameState.GetCharacterBase();
        if (characterBase == null)
        {
            return false;
        }
        skeleton = characterBase->Skeleton;
        if (skeleton == null)
        {
            return false;
        }
        pose = SkeletonModifier.GetPose(skeleton);
        if (pose == null)
        {
            return false;
        }
        structure = skeletonModifier.GetSkeletonStructure(skeleton);
        if (structure == null)
        {
            return false;
        }
        skeletonRotation = MathFactory.YRotation(cameraYRotation) / skeleton->Transform.Rotation.ToQuaternion();
        if (skeletonModifier.GetHeadPosition(skeleton) is not { } headPosition)
        {
            return false;
        }
        head = headPosition;
        return true;
    }

    private TrackingSource GetTrackingSource(VRInputData vrInputData)
    {
        if (configuration.BodyTracking && vrInputData.BodyJoints != null)
        {
            return TrackingSource.Body;
        }
        if (configuration.HandTracking && vrInputData.HandPose.HasData())
        {
            return TrackingSource.Hands;
        }
        if (configuration.ControllerTracking && vrInputData.PalmPose.HasData())
        {
            return TrackingSource.Controllers;
        }
        return TrackingSource.None;
    }

    private void ApplyTracking(TrackingSource source, VRInputData vrInputData, RuntimeAdjustments runtimeAdjustments, hkaPose* pose, SkeletonStructure structure, Quaternion<float> skeletonRotation, Vector3D<float> head)
    {
        switch (source)
        {
            case TrackingSource.Body:
                bodySkeletonModifier.Apply(pose, structure, skeletonRotation, vrInputData.BodyJoints!, vrInputData.HandPose);
                break;
            case TrackingSource.Hands:
                handTrackingSkeletonModifier.Apply(pose, structure, skeletonRotation, head, vrInputData.HandPose, runtimeAdjustments);
                break;
            case TrackingSource.Controllers:
                controllerTrackingSkeletonModifier.Apply(pose, structure, skeletonRotation, head, vrInputData.PalmPose);
                break;
        }
    }

    private static (Posef? left, Posef? right) GetWrists(TrackingSource source, VRInputData vrInputData)
    {
        switch (source)
        {
            case TrackingSource.Hands:
                return (
                    vrInputData.HandPose.LeftHand is { } left ? left[(int)HandJointEXT.WristExt].Pose : null,
                    vrInputData.HandPose.RightHand is { } right ? right[(int)HandJointEXT.WristExt].Pose : null);
            case TrackingSource.Controllers:
                return ControllerTrackingSkeletonModifier.GetWrists(vrInputData.PalmPose);
            default:
                return (null, null);
        }
    }

    internal void ResetVerticalCameraRotation(float rotation)
    {
        var camera = gameState.GetActiveCamera();
        if (camera != null)
        {
            camera->DirV = rotation;
        }
    }

    internal void OnNamePlateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        nameplateModifier.UpdateVRNameplates(context, handlers);
    }

}