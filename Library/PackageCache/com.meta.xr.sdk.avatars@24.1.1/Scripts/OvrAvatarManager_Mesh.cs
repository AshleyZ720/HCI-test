using UnityEngine;

namespace Oculus.Avatar2
{
    public partial class OvrAvatarManager
    {
        [Header("Mesh Optimization (Advanced)")]

        [Tooltip("Most developers will want to leave this false.\nDisabling this optimization is only needed if the avatar meshes need to be accessible from CPU after loading has finished.\nMeshes may still be accessed via `OvrAvatarManager.OnAvatarMeshLoaded` without disabling optimizations.")]
        [SerializeField]
        public bool disableMeshOptimization = false;
    }
}
