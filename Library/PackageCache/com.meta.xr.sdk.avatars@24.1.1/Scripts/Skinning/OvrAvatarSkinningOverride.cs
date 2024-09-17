using UnityEngine;

namespace Oculus.Avatar2
{
    // Add this component alongside OvrAvatarEntity if you wish to override the `_skinningType` set in OvrAvatarManager.
    // Note that the the skinner you choose here must be in the `_skinningType` bitfield. See SkinningTypesExample
    // scene for usage.
    [RequireComponent(typeof(OvrAvatarEntity))]
    public class OvrAvatarSkinningOverride : MonoBehaviour
    {
        [SerializeField] public OvrAvatarEntity.SkinningConfig skinningTypeOverride = 
            OvrAvatarEntity.SkinningConfig.OVR_COMPUTE;
    }
}
