using Oculus.Avatar2;

namespace Oculus.Skinning.GpuSkinning
{
    /**
     * Component that encapsulates the meshes of a skinned avatar.
     * This component implements skinning using the Avatar SDK
     * and uses the GPU. It performs skinning on every avatar
     * at each frame. It is used when the skinning configuration
     * is set to SkinningConfig.OVR_UNITY_GPU_COMPUTE and motion smoothing
     * is *not* enabled in the GPU skinning configuration.
     *
     * @see OvrAvatarSkinnedRenderable
     * @see OvrAvatarComputeSkinnedRenderable
     * @see OvrGpuSkinningConfiguration.MotionSmoothing
     */
    public class OvrAvatarComputeSkinnedRenderable : OvrAvatarComputeSkinnedRenderableBase
    {
        protected override string LogScope => nameof(OvrAvatarComputeSkinnedRenderable);

        // Only need single frame of "animation/skinning data" before validity
        protected override int NumAnimationFramesBeforeValidData => 1;

        // Only need a single output frame
        internal override OvrComputeUtils.MaxOutputFrames MeshAnimatorOutputFrames =>
            OvrComputeUtils.MaxOutputFrames.ONE;

        protected override void OnAnimationEnabledChanged(bool isNowEnabled)
        {
            if (isNowEnabled)
            {
                NumValidAnimationFrames = 0;
                MeshAnimator?.SetWriteDestination(SkinningOutputFrame.FrameZero);
            }
        }

        internal override void AnimationFrameUpdate()
        {
            // ASSUMPTION: This call will always be followed by calls to update morphs and/or skinning.
            // With that assumption, new data will be written by the morph target combiner and/or skinner, so there
            // will be valid data at end of frame.
            IncrementValidAnimationFramesIfNeeded();

            if (MeshAnimator != null)
            {
                OvrAvatarManager.Instance.SkinningController.AddActivateComputeAnimator(MeshAnimator);
            }
        }

        internal override void RenderFrameUpdate()
        {
            // Intentionally empty
        }
    }
}
