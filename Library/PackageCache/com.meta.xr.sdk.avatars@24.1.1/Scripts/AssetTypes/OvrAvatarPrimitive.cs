using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using static Oculus.Avatar2.CAPI;

/// @file OvrAvatarPrimitive.cs

namespace Oculus.Avatar2
{
    /**
     * Encapsulates a mesh associated with an avatar asset.
     * Asynchronously loads the mesh and its material and
     * converts it to a Unity Mesh and Material.
     * A primitive may be shared across avatar levels of detail
     * and across avatar renderables.
     * @see OvrAvatarRenderable
     */
    public sealed class OvrAvatarPrimitive : OvrAvatarAsset<CAPI.ovrAvatar2Primitive>
    {
        private const string primitiveLogScope = "ovrAvatarPrimitive";
        //:: Internal

        private const int LOD_INVALID = -1;

        /// Name of the asset this mesh belongs to.
        /// The asset name is established when the asset is loaded.
        public override string assetName => shortName;

        /// Type of asset (e.e. "OvrAvatarPrimitive", "OvrAvatarImage")
        public override string typeName => primitiveLogScope;

        /// Name of this primitive.
        public readonly string name = null;

        ///
        /// Short name of this primitive.
        /// Defaults to the asset name.
        /// @see assetName
        ///
        public readonly string shortName = null;

        /// Unity Material used by this primitive.
        public Material material { get; private set; } = null;

        /// Unity Mesh used by this primitive.
        public Mesh mesh { get; private set; } = null;

        /// True if this primitive has a computed bounding volume.
        public bool hasBounds { get; private set; }

        /// Triangle and vertex counts for this primitive.
        public ref readonly AvatarLODCostData CostData => ref _costData;

        /// Gets the GPU skinning version of this primitive.
        public OvrAvatarGpuSkinnedPrimitive gpuPrimitive { get; private set; } = null;

        public OvrAvatarComputeSkinnedPrimitive computePrimitive { get; private set; }
#pragma warning disable CA2213 // Disposable fields should be disposed - it is, but the linter is confused
        private OvrAvatarGpuSkinnedPrimitiveBuilder gpuPrimitiveBuilder = null;
#pragma warning restore CA2213 // Disposable fields should be disposed

        ///
        /// Index of highest quality level of detail this primitive belongs to.
        /// One primitive may be used by more than one level of detail.
        /// This is the lowest set bit in @ref CAPI.ovrAvatar2EntityLODFlags provided from native SDK.
        ///
        public uint HighestQualityLODIndex => (uint)lod;

        ///
        /// LOD bit flags for this primitive.
        /// These flags indicate which levels of detail this primitive is used by.
        /// @see HighestQualityLODIndex
        ///
        public CAPI.ovrAvatar2EntityLODFlags lodFlags { get; private set; }

        ///
        /// Type of shader being used by this primitive.
        /// The shader type depends on what part of the avatar is being shaded.
        ///
        public OvrAvatarShaderManagerMultiple.ShaderType shaderType { get; private set; }

        private OvrAvatarShaderConfiguration _shaderConfig;

        // MeshInfo, only tracked for cleanup on cancellation
        private MeshInfo _meshInfo;

        // NOTE: Once this is initialized, it should not be "reset" even if the Primitive is disposed
        // Other systems may need to reference this data during shutdown, and it's a PITA if they each have to make copies
        private AvatarLODCostData _costData = default;

        // TODO: A primitive can technically belong to any number of LODs with gaps in between.
        private int lod = LOD_INVALID;

        // TODO: Make this debug only
        public Int32[] joints;

        ///
        /// Get which body parts of the avatar this primitive is used by.
        /// These are established when the primitive is loaded.
        ///
        public CAPI.ovrAvatar2EntityManifestationFlags manifestationFlags { get; private set; }

        ///
        /// Get which view(s) (first person, third person) this primitive applies to.
        /// These are established when the primitive is loaded.
        ///
        public CAPI.ovrAvatar2EntityViewFlags viewFlags { get; private set; }

        ///
        /// If the user wants only a subset of the mesh, as specified by
        /// indices, these flags will control which submeshes are included.
        /// NOTE: In the current implementation all verts are downloaded,
        /// but the indices referencing them are excluded.
        ///
        public CAPI.ovrAvatar2EntitySubMeshInclusionFlags subMeshInclusionFlags { get; private set; }

        ///
        /// If the user wants to lower the avatar quality for faster rendering, they can
        /// do that here.
        ///
        public CAPI.ovrAvatar2EntityQuality quality { get; private set; }

        /// True if this primitive has joints (is skinned).
        public bool HasJoints => JointCount > 0;

        /// True if this primitive has blend shapes (morph targets).
        public bool HasMorphs => morphTargetCount > 0;

        /// Number of joints affecting this primitive.
        public UInt32 JointCount => joints != null ? (uint)joints.Length : 0;

        /// Number of vertices in this primitive's mesh.
        public UInt32 meshVertexCount => _meshVertexCount;

        /// Number of vertices affected by morph targets.
        // TODO: Accurate count of vertices affected by morph targets
        // Assumes that if there are morph targets, all verts are affected by morphs
        public UInt32 morphVertexCount => HasMorphs ? meshVertexCount : 0;

        public UInt32 skinningCost => data.skinningCost;

        /// Number of triangles in this primitive.
        public UInt32 triCount { get; private set; }

        /// Number of morph targets affecting this primitive.
        public UInt32 morphTargetCount => _morphTargetCount;

        /// True if this primitive has tangents for each vertex.
        public bool hasTangents { get; private set; }

        private UInt32 bufferVertexCount => _bufferVertexCount;

        /// True if this primitive has finished loading.
        public override bool isLoaded
        {
            get => base.isLoaded && meshLoaded && materialLoaded && gpuSkinningLoaded && computeSkinningLoaded;
        }

        // Indicates that this Primitive no longer needs access to CAPI asset data and the resource can be released
        internal bool hasCopiedAllResourceData =>
            !(_needsMeshData || _needsMorphData || _needsImageData || _needsCompactSkinningData);

        // Vertex count for the entire asset buffer, may include data for multiple primitives
        private UInt32 _bufferVertexCount = UInt32.MaxValue;

        // Vertex count for this mesh's primitive
        private UInt32 _meshVertexCount = UInt32.MaxValue;
        private UInt32 _morphTargetCount = UInt32.MaxValue;

        // Task thread completion checks
        private bool meshLoaded = false;
        private bool materialLoaded = false;
        private bool gpuSkinningLoaded = false;
        private bool computeSkinningLoaded = false;

        // Resource copy status
        private bool _needsMeshData = true;
        private bool _needsMorphData = true;
        private bool _needsImageData = true;
        private bool _needsCompactSkinningData = true;

        // TODO: Remove via better state management
        private bool _hasCancelled = false;

#if !UNITY_WEBGL
        // Cancellation token for Tasks
#pragma warning disable CA2213 // Disposable fields should be disposed -> It is, but the linter is confused
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
#pragma warning restore CA2213 // Disposable fields should be disposed
#endif // !UNITY_WEBGL

        // Async load coroutines for cancellation
        private OvrTime.SliceHandle _loadMeshAsyncSliceHandle;
        private OvrTime.SliceHandle _loadMaterialAsyncSliceHandle;

        [Flags]
        private enum VertexFormat : UInt32
        {
            VF_POSITION = 1,
            VF_NORMAL = 2,
            VF_TANGENT = 4,
            VF_COLOR = 8,
            VF_TEXCOORD0 = 16,
            VF_COLOR_ORMT = 32,
            VF_BONE_WEIGHTS = 64,
            VF_BONE_INDICES = 128,
        }

        // Unity 2022 requires skinned mesh attributes be on specific streams. We create separate NativeArrays and
        // strides for each stream.
        // Stream 0: Position, Normal, Tangent
        // Stream 1: Color, TexCoord0, TextCoord1
        // Stream 2: BlendWeight, BlendIndices
        private const int VF_STREAM_COUNT = 3;

        // These aren't really necessary but make it clear which stream is being used and where.
        private const int VF_STREAM_0 = 0;
        private const int VF_STREAM_1 = 1;
        private const int VF_STREAM_2 = 2;


        private unsafe readonly struct VertexBufferFormat : IDisposable
        {
            public VertexBufferFormat(VertexFormat vertexFormat_, int vertexCount_
                , in VertexBufferStrides vertexStrides_
                , VertexAttributeDescriptor[] vertexLayout_, NativeArray<byte>[] vertexStreams_)
            {
                vertexFormat = vertexFormat_;
                vertexCount = vertexCount_;
                vertexStrides = vertexStrides_;
                vertexLayout = vertexLayout_;
                vertexStreams = vertexStreams_;
            }

            public readonly VertexFormat vertexFormat;
            public readonly Int32 vertexCount;

            public readonly VertexBufferStrides vertexStrides;
            public readonly VertexAttributeDescriptor[] vertexLayout;

            public readonly NativeArray<byte>[] vertexStreams;

            public struct VertexBufferStrides
            {
                private fixed int _lengths[VF_STREAM_COUNT];

                public ref int this[int idx] => ref _lengths[idx];
            }

            public void Dispose()
            {
                for (int idx = 0; idx < VF_STREAM_COUNT; ++idx)
                {
                    vertexStreams[idx].Reset();
                }
            }
        }
        private static int GetVertexStride(in VertexBufferFormat bufferFormat, int streamIndex)
        {
            unsafe
            {
                return bufferFormat.vertexStrides[streamIndex];
            }
        }

        // Data shared across threads
        private sealed class MeshInfo : IDisposable
        {
            public NativeArray<UInt16> triangles;

            private NativeArray<Vector3> verts_;

            // New vertex format.
            public VertexBufferFormat VertexBufferFormat;

            // NOTE: Held during GPUPrimitiveBuilding
            public ref readonly NativeArray<Vector3> verts => ref verts_;

            public void SetVertexBuffer(in NativeArray<Vector3> buffer)
            {
                verts_ = buffer;
                pendingMeshVerts_ = true;
            }

            // NOTE: Held during GPUPrimitiveBuilding
            private NativeArray<Vector3> normals_;

            public ref readonly NativeArray<Vector3> normals => ref normals_;

            public void SetNormalsBuffer(in NativeArray<Vector3> buffer)
            {
                normals_ = buffer;
                pendingMeshNormals_ = true;
            }

            // NOTE: Held during GPUPrimitiveBuilding
            private NativeArray<Vector4> tangents_;

            public ref readonly NativeArray<Vector4> tangents => ref tangents_;

            public void SetTangentsBuffer(in NativeArray<Vector4> buffer)
            {
                tangents_ = buffer;
                pendingMeshTangents_ = true;
                hasTangents = buffer.IsCreated && buffer.Length > 0;
            }

            // This holds vertex colors, texture coordinates, vertex properties, and material type
            public NativeArray<byte> staticAttributes;
            public bool hasColors;
            public bool hasTextureCoords;
            public bool hasProperties;

            // Documentation for `SetBoneWeights(NativeArray)` is... lacking
            // - https://docs.unity3d.com/ScriptReference/Mesh.SetBoneWeights.html
            private BoneWeight[] boneWeights_;

            public ref readonly BoneWeight[] boneWeights => ref boneWeights_;

            public void SetBoneWeights(BoneWeight[] buffer)
            {
                boneWeights_ = buffer;
                pendingMeshBoneWeights_ = buffer != null && buffer.Length > 0;
            }

            // Skin
            // As of 2020.3, no NativeArray bindPoses setter
            public Matrix4x4[] bindPoses;

            // Track vertex count after verts has been freed
            public uint vertexCount { get; set; }
            public bool hasTangents { get; private set; }

            public void WillBuildGpuPrimitive()
            {
                pendingGpuPrimitive_ = true;
                pendingNeutralPoseTex_ = vertexCount > 0;
            }

            public void DidBuildGpuPrimitive()
            {
                pendingGpuPrimitive_ = false;
                if (CanResetVerts) { verts_.Reset(); }
                if (CanResetNormals) { normals_.Reset(); }
                if (CanResetTangents) { tangents_.Reset(); }
                if (CanResetBoneWeights) { boneWeights_ = null; }
            }

            public void NeutralPoseTexComplete()
            {
                pendingNeutralPoseTex_ = false;
                if (CanResetVerts) { verts_.Reset(); }
                if (CanResetNormals) { normals_.Reset(); }
                if (CanResetTangents) { tangents_.Reset(); }
            }

            public void CancelledBuildPrimitives()
            {
                DidBuildGpuPrimitive();
                NeutralPoseTexComplete();
            }

            public void MeshVertsComplete()
            {
                pendingMeshVerts_ = false;
                if (CanResetVerts) { verts_.Reset(); }
            }

            public void MeshNormalsComplete()
            {
                pendingMeshNormals_ = false;
                if (CanResetNormals) { normals_.Reset(); }
            }

            public void MeshTangentsComplete()
            {
                pendingMeshTangents_ = false;
                if (CanResetTangents) { tangents_.Reset(); }
            }

            public void MeshBoneWeightsComplete()
            {
                pendingMeshBoneWeights_ = false;
                if (CanResetBoneWeights) { boneWeights_ = null; }
            }

            private bool CanResetVerts => !pendingGpuPrimitive_ && !pendingNeutralPoseTex_ && !pendingMeshVerts_;
            private bool CanResetNormals => !pendingGpuPrimitive_ && !pendingNeutralPoseTex_ && !pendingMeshNormals_;
            private bool CanResetTangents => !pendingGpuPrimitive_ && !pendingNeutralPoseTex_ && !pendingMeshTangents_;
            private bool CanResetBoneWeights => !pendingGpuPrimitive_ && !pendingMeshBoneWeights_;

            private bool pendingMeshVerts_ = false;
            private bool pendingMeshTangents_ = false;
            private bool pendingMeshNormals_ = false;
            private bool pendingMeshBoneWeights_ = false;

            private bool pendingGpuPrimitive_ = false;

            private bool pendingNeutralPoseTex_ = false;

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            private void Dispose(bool isDispose)
            {
                boneWeights_ = null;
                bindPoses = null;

                triangles.Reset();

                verts_.Reset();
                normals_.Reset();
                tangents_.Reset();

                staticAttributes.Reset();

                OvrAvatarLog.Assert(isDispose, primitiveLogScope);
            }

            ~MeshInfo()
            {
                OvrAvatarLog.LogError("Finalized MeshInfo", primitiveLogScope);
                Dispose(false);
            }
        }

        private class MaterialInfo
        {
            public CAPI.ovrAvatar2MaterialTexture[] texturesData = null;
            public CAPI.ovrAvatar2Image[] imageData = null;
            public bool hasMetallic = false;
        }

        // TODO: Look into readonly struct, this doesn't appear to be shared across threads
        private struct MorphTargetInfo
        {
            public readonly string name;

            // TODO: Maybe make these NativeArrays too?
            public readonly Vector3[] targetPositions;
            public readonly Vector3[] targetNormals;
            public readonly Vector3[] targetTangents;

            public MorphTargetInfo(string nameIn, Vector3[] posIn, Vector3[] normIn, Vector3[] tanIn)
            {
                this.name = nameIn;
                this.targetPositions = posIn;
                this.targetNormals = normIn;
                this.targetTangents = tanIn;
            }
        }

        internal OvrAvatarPrimitive(OvrAvatarResourceLoader loader, in CAPI.ovrAvatar2Primitive primitive) : base(
            primitive.id, primitive)
        {
            // TODO: Can we defer this until later as well?
            mesh = new Mesh();

            // Name

            unsafe
            {
                const int bufferSize = 1024;
                byte* nameBuffer = stackalloc byte[bufferSize];
                var result = CAPI.ovrAvatar2Asset_GetPrimitiveName(assetId, nameBuffer, bufferSize);
                if (result.IsSuccess())
                {
                    string meshPrimitiveName = Marshal.PtrToStringAnsi((IntPtr)nameBuffer);
                    if (!string.IsNullOrEmpty(meshPrimitiveName)) { name = meshPrimitiveName; }
                }
                else { OvrAvatarLog.LogWarning($"GetPrimitiveName {result}", primitiveLogScope); }
            }

            if (name == null) { name = "Mesh" + primitive.id; }

            mesh.name = name;
            shortName = name.Replace("Primitive", "p");
        }

        // Must *not* be called more than once
        private bool _startedLoad = false;

        internal void StartLoad(OvrAvatarResourceLoader loader)
        {
            OvrAvatarLog.LogInfo($"staring primitive load for loader: {loader.resourceId}");
            Debug.Assert(!_startedLoad);
            Debug.Assert(!_loadMeshAsyncSliceHandle.IsValid);
            Debug.Assert(!_loadMaterialAsyncSliceHandle.IsValid);

            _startedLoad = true;

            var vertCountResult =
                CAPI.ovrAvatar2VertexBuffer_GetVertexCount(data.vertexBufferId, out _bufferVertexCount);
            if (!vertCountResult.EnsureSuccess("ovrAvatar2VertexBuffer_GetVertexCount", primitiveLogScope))
            {
                _bufferVertexCount = 0;
                _needsMeshData = false;
            }

            var morphResult = CAPI.ovrAvatar2Result.Unknown;
            //primitives might not have a morph target
            if (data.morphTargetBufferId != CAPI.ovrAvatar2MorphTargetBufferId.Invalid)
            {
                morphResult =
                    CAPI.ovrAvatar2VertexBuffer_GetMorphTargetCount(data.morphTargetBufferId, out _morphTargetCount);
                //but if they do, then getting the ocunt shouldn't fail
                morphResult.EnsureSuccess("ovrAvatar2VertexBuffer_GetMorphTargetCount", primitiveLogScope);
            }

            if (morphResult.IsFailure())
            {
                _morphTargetCount = 0;
                _needsMorphData = false;
            }

            _needsCompactSkinningData = _needsMeshData && OvrAvatarManager.Instance.OvrComputeSkinnerSupported;

            _loadMeshAsyncSliceHandle = OvrTime.Slice(LoadMeshAsync());
            _loadMaterialAsyncSliceHandle = OvrTime.Slice(LoadMaterialAsync(loader, loader.resourceId));

            // there are additional flags which will be set before publicly reporting `isLoaded`
            base.isLoaded = true;
        }

        private bool _CanCleanupCancellationToken =>
            !_loadMeshAsyncSliceHandle.IsValid && !_loadMaterialAsyncSliceHandle.IsValid;

#if !UNITY_WEBGL
        private void _TryCleanupCancellationToken()
        {
            if (!_CanCleanupCancellationToken)
            {
                // Cancellation called while timesliced operations in progress
                return;
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        private bool AreAllTasksCancelled()
        {
            bool allCancelled = true;

            for (int idx = 0; idx < _apiTasks.Length; ++idx)
            {
                ref Task task = ref _apiTasks[idx];
                if (task != null)
                {
                    if (task.IsCompleted)
                    {
                        task.Dispose();
                        task = null;
                    }
                    else
                    {
                        OvrAvatarLog.LogDebug(
                            $"Cancelled Task {task} is still running",
                            primitiveLogScope);
                        allCancelled = false;
                    }
                }
            }

            if (allCancelled && _apiTasks.Length > 0) { _apiTasks = Array.Empty<Task>(); }

            if (_texturesDataTask != null)
            {
                if (_texturesDataTask.IsCompleted)
                {
                    _texturesDataTask.Dispose();
                    _texturesDataTask = null;
                }
                else
                {
                    OvrAvatarLog.LogError(
                        $"Cancelled Task {_texturesDataTask} is still running",
                        primitiveLogScope);
                    allCancelled = false;
                }
            }

            return allCancelled;
        }
#else // !UNITY_WEBGL
        private void _TryCleanupCancellationToken() {}
        private bool AreAllTasksCancelled() => true;
#endif // UNITY_WEBGL

        protected override void _ExecuteCancel()
        {
            OvrAvatarLog.Assert(!_hasCancelled);
            // TODO: Remove this check, this should not be possible
            if (_hasCancelled)
            {
                OvrAvatarLog.LogError($"Double cancelled primitive {name}", primitiveLogScope);
                return;
            }
#if !UNITY_WEBGL
            // TODO: We can probably skip all of this if cancellation token is null
            _cancellationTokenSource?.Cancel();
#endif

            if (_loadMeshAsyncSliceHandle.IsValid)
            {
                OvrAvatarLog.LogDebug($"Stopping LoadMeshAsync slice {shortName}", primitiveLogScope);
                bool didCancel = _loadMeshAsyncSliceHandle.Cancel();
                OvrAvatarLog.Assert(didCancel, primitiveLogScope);
            }

            if (_loadMaterialAsyncSliceHandle.IsValid)
            {
                OvrAvatarLog.LogDebug($"Stopping LoadMaterialAsync slice {shortName}", primitiveLogScope);
                bool didCancel = _loadMaterialAsyncSliceHandle.Cancel();
                OvrAvatarLog.Assert(didCancel, primitiveLogScope);
            }
            if (AreAllTasksCancelled())
            {
                _FinishCancel();
            }
            else
            {
                OvrTime.Slice(_WaitForCancellation());
            }
            _hasCancelled = true;
        }

        private IEnumerator<OvrTime.SliceStep> _WaitForCancellation()
        {
            // Wait for all tasks to complete before proceeding with cleanup
            while (!AreAllTasksCancelled()) { yield return OvrTime.SliceStep.Delay; }

            // Finish cancellation, Dispose of Tasks and Tokens
            _FinishCancel();

            // Ensure any misc assets created during cancellation window are properly disposed
            Dispose(true);
        }

        private void _FinishCancel()
        {
            if (gpuPrimitiveBuilder != null)
            {
                OvrAvatarLog.LogDebug($"Stopping gpuPrimitiveBuilder {shortName}", primitiveLogScope);

                gpuPrimitiveBuilder.Dispose();
                gpuPrimitiveBuilder = null;
            }

            if (computePrimitive != null)
            {
                computePrimitive.Dispose();
                computePrimitive = null;
            }

            _needsImageData = _needsMeshData = _needsMorphData = _needsCompactSkinningData = false;
            _TryCleanupCancellationToken();
        }

        protected override void Dispose(bool disposing)
        {
            _loadMeshAsyncSliceHandle.Clear();
            _loadMaterialAsyncSliceHandle.Clear();

            if (!(mesh is null))
            {
                if (disposing) { Mesh.Destroy(mesh); }
                else
                {
                    OvrAvatarLog.LogError(
                        $"Mesh asset was not destroyed before OvrAvatarPrimitive ({name}, {assetId}) was finalized",
                        primitiveLogScope);
                }

                mesh = null;
            }

            if (!(material is null))
            {
                if (disposing) { Material.Destroy(material); }
                else
                {
                    OvrAvatarLog.LogError(
                        $"Material asset was not destroyed before OvrAvatarPrimitive ({name}, {assetId}) was finalized",
                        primitiveLogScope);
                }

                material = null;
            }

            if (!(gpuPrimitive is null))
            {
                if (disposing) { gpuPrimitive.Dispose(); }
                else
                {
                    OvrAvatarLog.LogError(
                        $"OvrAvatarGPUSkinnedPrimitive asset was not destroyed before OvrAvatarPrimitive ({name}, {assetId}) was finalized"
                        ,
                        primitiveLogScope);
                }

                gpuPrimitive = null;
            }

            if (!(computePrimitive is null))
            {
                if (disposing) { computePrimitive.Dispose(); }
                else
                {
                    OvrAvatarLog.LogError(
                        $"OvrAvatarComputeSkinnedPrimitive asset was not destroyed before OvrAvatarPrimitive ({name}, {assetId}) was finalized"
                        ,
                        primitiveLogScope);
                }

                computePrimitive = null;
            }

            DisposeVertexBuffer(_meshInfo);
            _meshInfo?.Dispose();

            joints = null;
            _shaderConfig = null;

            meshLoaded = false;
            materialLoaded = false;
        }

        //:: Main Thread Loading

        #region Main Thread Loading
#if !UNITY_WEBGL
        private Task[] _apiTasks = Array.Empty<Task>();
#endif // !UNITY_WEBGL

        private void Sliced_UploadStreamBuffersAndDispose(in VertexBufferFormat vertexBuffer, ref int idx, int lastStream)
        {
            for (; idx < lastStream; ++idx)
            {
                ref NativeArray<byte> streamBuffer = ref vertexBuffer.vertexStreams[idx];
                if (idx == VF_STREAM_1)
                {
                    // for static attributes, we didn't need to make a copy, so vertexStreams[idx] will be empty,
                    // can just pass what we got from the SDK to Unity directly.
                    streamBuffer = ref _meshInfo.staticAttributes;
                }

                if (!streamBuffer.IsCreated) { continue; }

                if (streamBuffer.Length > 0)
                {
                    mesh.SetVertexBufferData(streamBuffer, 0, 0,
                        vertexBuffer.vertexCount * GetVertexStride(vertexBuffer, idx), idx);
                    // Reset the buffer as soon as we're done with it to reduce peak memory allocations
                }
                streamBuffer.Reset();

                if (OvrTime.ShouldHold)
                {
                    // Resume on the next stream, next frame
                    idx++;
                    return;
                }
            }
        }

        private IEnumerator<OvrTime.SliceStep> LoadMeshAsync()
        {
            GetLodInfo();
            GetManifestationInfo();
            GetViewInfo();
            GetSubMeshInclusionInfo();
            GetQualityInfo();

            // load triangles
            // load mesh & morph targets
            // create unity mesh and/or gpu skinning resources

#if !UNITY_WEBGL
            var unitySkinning = OvrAvatarManager.Instance.UnitySMRSupported;
            var gpuSkinning = OvrAvatarManager.Instance.OvrGPUSkinnerSupported;
            var computeSkinning = OvrAvatarManager.Instance.OvrComputeSkinnerSupported;
#else // UNITY_WEBGL
            var unitySkinning = false;
            var gpuSkinning = false;
            var computeSkinning = false;
#endif // UNITY_WEBGL
            var computeSkinningOnly = computeSkinning && !unitySkinning && !gpuSkinning;
            var disableMeshOptimization = OvrAvatarManager.Instance.disableMeshOptimization;

            var setupSkin = data.jointCount > 0 && (gpuSkinning || unitySkinning);
            var hasAnyJoints = data.jointCount > 0;
            var setupMorphTargets = morphTargetCount > 0 && (gpuSkinning || unitySkinning);

            _meshInfo = new MeshInfo();
            var morphTargetInfo = setupMorphTargets ? new MorphTargetInfo[morphTargetCount] : Array.Empty<MorphTargetInfo>();

#if !UNITY_WEBGL
            const int alwaysPerformedTasks = 2;
            var taskCount = alwaysPerformedTasks + (hasAnyJoints ? 1 : 0);
            var tasks = new Task[taskCount];
            // for now, all tasks are "CAPI" tasks
            _apiTasks = tasks;

            tasks[0] = Task.Run(() => { RetrieveTriangles(_meshInfo); });
#else
#endif // !UNITY_WEBGL
            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

#if !UNITY_WEBGL
            var tasksAfterTriangles = new Task[setupMorphTargets ? 2 : 1];
            tasksAfterTriangles[0] =
                tasks[0].ContinueWith(antecedent => RetrieveMeshData(_meshInfo, computeSkinningOnly, disableMeshOptimization));

            if (setupMorphTargets)
            {
                tasksAfterTriangles[1] = tasks[0].ContinueWith(
                    antecedent =>
                        SetupMorphTargets(morphTargetInfo));
            }

            tasks[1] = Task.WhenAll(tasksAfterTriangles);
#else
#endif // !UNITY_WEBGL
            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

            if (setupSkin)
            {
#if !UNITY_WEBGL
                tasks[2] = Task.Run(() => SetupSkin(ref _meshInfo));
#else
#endif // !UNITY_WEBGL
            }
            else if (hasAnyJoints)
            {
#if !UNITY_WEBGL
                tasks[2] = Task.Run(SetupJointIndicesOnly);
#else
#endif // UNITY_WEBGL
            }
            else
            {
                joints = Array.Empty<int>();
            }

            if (gpuSkinning)
            {
                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                // Gpu skinning needs both a primitive and a primitive "builder"
                gpuPrimitiveBuilder = new OvrAvatarGpuSkinnedPrimitiveBuilder(shortName, morphTargetCount);
            }
            else
            {
                // Don't need to wait for gpu skinning
                gpuSkinningLoaded = true;
            }

            if (!computeSkinning)
            {
                // Don't need to wait for compute skinning
                computeSkinningLoaded = true;
            }

            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

            CAPI.ovrAvatar2Vector3f minPos;
            CAPI.ovrAvatar2Vector3f maxPos;
            CAPI.ovrAvatar2Result result;

            var getSkinnedMinMaxPosition = data.jointCount > 0;
            if (getSkinnedMinMaxPosition)
            {
                result = CAPI.ovrAvatar2Primitive_GetSkinnedMinMaxPosition(data.id, out minPos, out maxPos);
            }
            else
            {
                result = CAPI.ovrAvatar2Primitive_GetMinMaxPosition(data.id, out minPos, out maxPos);
            }

            hasBounds = false;
            Bounds? sdkBounds = null;
            if (result.IsSuccess())
            {
                Vector3 unityMin = minPos;
                Vector3 unityMax = maxPos;
                sdkBounds = new Bounds(Vector3.zero, unityMax - unityMin);
                hasBounds = true;
            }

            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

#if !UNITY_WEBGL
            while (!AllTasksFinished(tasks))
            {
                if (AnyTasksFaulted(tasks))
                {
                    // Allow Slicer to cancel before CancelLoad is called or we will cancel during slice!
                    OvrAvatarLog.LogError("Task fault detected! Disposing resource.", primitiveLogScope);
                    OvrTime.PostCleanupToUnityMainThread(Dispose);
                    yield return OvrTime.SliceStep.Cancel;
                }

                yield return OvrTime.SliceStep.Delay;
            }
#endif // !UNITY_WEBGL

            _needsMeshData = false;
            _needsMorphData = false;

#if !UNITY_WEBGL
            if (AllTasksSucceeded(tasks))
#endif // !UNITY_WEBGL
            {
#if !UNITY_WEBGL
                _apiTasks = Array.Empty<Task>();
#endif // !UNITY_WEBGL

                hasTangents = _meshInfo.hasTangents;

                // TODO: Better way to setup this dependency, we need all preprocessing completed to build GPU resources though :/
                if (gpuPrimitiveBuilder != null)
                {
#if !UNITY_WEBGL
                    Array.Resize(ref tasks, 1);
                    tasks[0] =
#endif // !UNITY_WEBGL
                        gpuPrimitiveBuilder.CreateGpuPrimitiveHelperTask(
                        _meshInfo,
                        morphTargetInfo,
                        hasTangents);

                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                }
                else
                {
#if !UNITY_WEBGL
                    tasks = Array.Empty<Task>();
#endif // !UNITY_WEBGL
                }

                // TODO: It would be ideal to pull this directly from nativeSDK - requires LOD buffer split
                _meshVertexCount = _meshInfo.vertexCount;

                // Apply mesh info on main thread

                // Create a vertex buffer using the format and stride.
                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                CreateVertexBuffer(_meshInfo);


                // Set the mesh vertex buffer parameters from the vertex buffer created above.
                // Note: final vertex buffer data is not set until the finalized below.
                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                var vertexBuffer = _meshInfo.VertexBufferFormat;
                mesh.SetVertexBufferParams(vertexBuffer.vertexCount, vertexBuffer.vertexLayout);

                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                StripExcludedSubMeshes(ref _meshInfo.triangles);

                // get number of submeshes
                // foreach submesh, check to see if it is included
                // if it is not, then remove this range from the index buffer

                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                mesh.SetIndices(_meshInfo.triangles, MeshTopology.Triangles, 0, !hasBounds, 0);
                _meshInfo.triangles.Reset();

                // When UnitySMR is supported, include extra animation data
                if (OvrAvatarManager.Instance.UnitySMRSupported)
                {
                    if (setupSkin)
                    {
                        if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                        mesh.bindposes = _meshInfo.bindPoses;
                        _meshInfo.bindPoses = null;
                    }

                    foreach (var morphTarget in morphTargetInfo)
                    {
                        if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                        mesh.AddBlendShapeFrame(
                            morphTarget.name,
                            1,
                            morphTarget.targetPositions,
                            morphTarget.targetNormals,
                            morphTarget.targetTangents);
                    }
                }

                if (OvrAvatarManager.Instance.HasMeshLoadListener)
                {
                    yield return OvrTime.SliceStep.Stall;
                    // Call the mesh loaded callback before the native arrays are reset.
                    InvokeOnMeshLoaded(mesh, _meshInfo);
                }

                // Vertex buffer data.
                // Copy the vertex data into the vertex buffer array.
                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                CopyMeshDataIntoVertexBufferAndDispose(_meshInfo);

                // Upload vertex data to the mesh.
                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                const int kEndStream = VF_STREAM_COUNT;
                int nextUploadStream = 0;
                do
                {
                    Sliced_UploadStreamBuffersAndDispose(in vertexBuffer, ref nextUploadStream, kEndStream);
                    if (nextUploadStream < kEndStream) { yield return OvrTime.SliceStep.Hold; }
                } while (nextUploadStream < kEndStream);

                // Upload mesh data to GPU
                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                var markNoLongerReadable = !OvrAvatarManager.Instance.disableMeshOptimization;
                mesh.UploadMeshData(markNoLongerReadable);

                // It seems that almost every vert data assignment will recalculate (and override) bounds - excellent engine...
                // So, we must delay this to the very end for no logical reason
                if (sdkBounds.HasValue)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    mesh.bounds = sdkBounds.Value;
                }

                if (gpuPrimitiveBuilder != null)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
#if !UNITY_WEBGL
                    // TODO: This is not ideal timing for this operation, but it does minimize disruption in this file which is key right now 3/3/2021
                    // - As of now, there really isn't any meaningful work that can be done off the main thread - pending D26787881
                    while (!AllTasksFinished(tasks)) { yield return OvrTime.SliceStep.Delay; }
#else // UNITY_WEBGL
#endif // UNITY_WEBGL
                    // Main thread operations (currently almost all of it), sliced as best possible
                    Profiler.BeginSample("Build GPUPrimitive");
                    gpuPrimitive = gpuPrimitiveBuilder.BuildPrimitive(_meshInfo, joints);
                    Profiler.EndSample();
                }

                if (computeSkinning)
                {
                    Profiler.BeginSample("Build ComputePrimitive");
                    computePrimitive = BuildPrimitive(
                        data.compactSkinningDataId,
                        (origIndexToCompactSkinningIndex, indexFormat) => GenerateMeshToCompactSkinningIndices(
                            origIndexToCompactSkinningIndex,
                            indexFormat),
                        () =>
                        {
                            OvrAvatarLog.LogWarning("Failed to build compute skinning primitive");
                            _needsCompactSkinningData = false;
                        },
                        () =>
                        {
                            // Can free compact skinning data
                            _needsCompactSkinningData = false;
                        },
                        () => { _needsCompactSkinningData = false; });
                    Profiler.EndSample();
                }

                while (gpuPrimitive != null && gpuPrimitive.IsLoading)
                {
                    yield return OvrTime.SliceStep.Delay;
                }
                if (gpuPrimitiveBuilder != null)
                {
                    gpuPrimitiveBuilder.Dispose();
                    gpuPrimitiveBuilder = null;
                }
                gpuSkinningLoaded = true;

                while (computePrimitive != null && computePrimitive.IsLoading)
                {
                    yield return OvrTime.SliceStep.Delay;
                }
                computeSkinningLoaded = true;

                if (gpuPrimitive != null && gpuPrimitive.MetaData.NumMorphTargetAffectedVerts == 0
                    || computePrimitive != null && computePrimitive.VertexBuffer?.NumMorphedVerts == 0)
                {
                    _morphTargetCount = 0;
                }


                _costData = new AvatarLODCostData(this);
                meshLoaded = true;
            }
#if !UNITY_WEBGL
            else if (isCancelled)
            {
                // Ignore cancellation related exceptions
                OvrAvatarLog.LogDebug($"LoadMeshAsync was cancelled", primitiveLogScope);
            }
            else
            {
                // Log errors from Tasks
                foreach (var task in tasks)
                {
                    if (task is { Status: TaskStatus.Faulted }) { LogTaskErrors(task); }
                }
            }
#endif // !UNITY_WEBGL

            _loadMeshAsyncSliceHandle.Clear();
            _TryCleanupCancellationToken();
        }

        public OvrAvatarComputeSkinnedPrimitive BuildPrimitive(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            Func<NativeArray<byte>, CAPI.ovrAvatar2DataFormat, NativeArray<byte>> genVertToCompactIndex,
            Action failureCallback,
            Action compactSkinningDataLoadedCallback,
            Action finishCallback)
        {
            var primitive = new OvrAvatarComputeSkinnedPrimitive(
                id,
                genVertToCompactIndex,
                failureCallback,
                compactSkinningDataLoadedCallback,
                finishCallback);

            return primitive;
        }


        private NativeArray<byte> GenerateMeshToCompactSkinningIndices(
            in NativeArray<byte> origIndexToCompactSkinningIndex,
            CAPI.ovrAvatar2DataFormat indexFormat)
        {
            // The caller expects a copy here, so copy the native array
            NativeArray<byte> copy = new NativeArray<byte>(
                origIndexToCompactSkinningIndex.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);
            copy.CopyFrom(origIndexToCompactSkinningIndex);

            return copy;
        }

#if !UNITY_WEBGL
        private Task _texturesDataTask = null;
#endif // !UNITY_WEBGL

        private IEnumerator<OvrTime.SliceStep> LoadMaterialAsync(OvrAvatarResourceLoader loader,
            CAPI.ovrAvatar2Id resourceId)
        {
            // Info to pass between threads
            var materialInfo = new MaterialInfo();

            // Marshal texture data on separate thread
#if !UNITY_WEBGL
            _texturesDataTask = Task.Run(() => Material_GetTexturesData(resourceId, ref materialInfo));
            while (WaitForTask(_texturesDataTask, out var step))
            {
                yield return step;
            }

            _texturesDataTask = null;
#else // UNITY_WEBGL
#endif // UNITY_WEBGL

            // The rest, unfortunately, must be on the main thread
            _needsImageData = !(materialInfo.texturesData is null) && !(materialInfo.imageData is null);
            if (_needsImageData)
            {
                // Check for images needed by this material. Request image loads on main thread and wait for them.
                uint numImages = (uint)materialInfo.imageData.Length;
                var images = new OvrAvatarImage[(int)numImages];

                for (uint imgIdx = 0; imgIdx < numImages; ++imgIdx)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    FindTexture(loader, materialInfo, images, imgIdx, resourceId);
                }

                _needsImageData = false;

                // Image load wait loop.

                // Wait until all images are fully loaded
                foreach (var image in images)
                {
                    if (image == null) { continue; }

                    while (!image.isLoaded)
                    {
                        if (!image.isCancelled)
                        {
                            // Loading in progress, delay next slice
                            yield return OvrTime.SliceStep.Delay;
                        }
                        else // isCancelled
                        {
                            OvrAvatarLog.LogVerbose(
                                $"Image {image} cancelled during resource load.",
                                primitiveLogScope);

                            // Resume checking next frame
                            // TODO: Switch to Wait, but currently no unit test - use Delay for now
                            // yield return OvrTime.SliceStep.Wait;
                            yield return OvrTime.SliceStep.Delay;

                            break; // move to next images
                        }
                    }
                }
            }

            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

            // Configure shader manager and create material
            if (OvrAvatarManager.Instance == null || OvrAvatarManager.Instance.ShaderManager == null)
            {
                OvrAvatarLog.LogError(
                    $"ShaderManager must be initialized so that a shader can be specified to generate Avatar primitive materials.");
            }
            else
            {
                bool hasTextures = materialInfo.texturesData != null && materialInfo.texturesData.Length > 0;
                shaderType =
                    OvrAvatarManager.Instance.ShaderManager.DetermineConfiguration(
                        name, materialInfo.hasMetallic,
                        false, hasTextures);
                _shaderConfig = OvrAvatarManager.Instance.ShaderManager.GetConfiguration(shaderType);
            }

            if (_shaderConfig == null)
            {
                OvrAvatarLog.LogError($"Could not find config for shaderType {shaderType}", primitiveLogScope);
                yield break;
            }

            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

            if (_shaderConfig.Material != null) { material = new Material(_shaderConfig.Material); }
            else
            {
                var shader = _shaderConfig.Shader;

                if (shader == null)
                {
                    OvrAvatarLog.LogError($"Could not find shader for shaderType {shaderType}", primitiveLogScope);
                    yield break;
                }

                material = new Material(shader);
            }

            material.name = name;

            // Create and apply textures
            foreach (var textureData in materialInfo.texturesData)
            {
                if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                // Find corresponding image
                if (OvrAvatarManager.GetOvrAvatarAsset(textureData.imageId, out OvrAvatarImage image))
                {
                    ApplyTexture(image.texture, textureData);
                }
                else
                {
                    OvrAvatarLog.LogError($"Could not find image {textureData.imageId}", primitiveLogScope);
                }
            }

            // TODO: Should this happen before applying textures?
            if (material != null)
            {
                _shaderConfig.RegisterShaderUsage();

                // Finalize dynamically created material
                _shaderConfig.ApplyKeywords(material);
                _shaderConfig.ApplyFloatConstants(material);

                bool enableNormalMap = (quality <= CAPI.ovrAvatar2EntityQuality.Standard);
                bool enablePropertyHairMap = (quality <= CAPI.ovrAvatar2EntityQuality.Standard);
                if (enableNormalMap)
                {
                    material.EnableKeyword("HAS_NORMAL_MAP_ON");
                    material.SetFloat("HAS_NORMAL_MAP", 1.0f);
                }
                else
                {
                    material.DisableKeyword("HAS_NORMAL_MAP_ON");
                    material.SetFloat("HAS_NORMAL_MAP", 0.0f);
                }

                if (enablePropertyHairMap)
                {
                    material.EnableKeyword("ENABLE_HAIR_ON");
                    material.SetFloat("ENABLE_HAIR", 1.0f);
                }
                else
                {
                    material.DisableKeyword("ENABLE_HAIR_ON");
                    material.SetFloat("ENABLE_HAIR", 0.0f);
                }
            }

            LoadAndApplyExtensions();

            materialLoaded = true;
            _loadMaterialAsyncSliceHandle.Clear();
            _TryCleanupCancellationToken();
        }

        private bool LoadAndApplyExtensions()
        {
            var result = CAPI.ovrAvatar2Primitive_GetNumMaterialExtensions(assetId, out uint numExtensions);
            if (!result.IsSuccess())
            {
                OvrAvatarLog.LogError(
                    $"GetNumMaterialExtensions assetId:{assetId}, result:{result}"
                    , primitiveLogScope);
                return false;
            }

            bool success = true;
            for (uint extensionIdx = 0; extensionIdx < numExtensions; extensionIdx++)
            {
                if (OvrAvatarMaterialExtension.LoadExtension(
                        assetId,
                        extensionIdx,
                        out var extension))
                {
                    extension.ApplyEntriesToMaterial(material, _shaderConfig.ExtensionConfiguration);
                }
                else
                {
                    OvrAvatarLog.LogWarning(
                        $"Unable to load material extension at index {extensionIdx} for assetId:{assetId}",
                        primitiveLogScope);
                    success = false;
                }
            }

            return success;
        }

#if !UNITY_WEBGL
        private bool WaitForTask(Task task, out OvrTime.SliceStep step)
        {
            // TODO: isCancelled should be mostly unnecessary here.... mostly.
            if (isCancelled || task.Status == TaskStatus.Faulted)
            {
                step = OvrTime.SliceStep.Cancel;
                LogTaskErrors(task);
                return true;
            }

            if (!task.IsCompleted)
            {
                step = OvrTime.SliceStep.Delay;
                return true;
            }

            step = OvrTime.SliceStep.Continue;
            return false;
        }

        private bool AllTasksFinished(Task[] tasks)
        {
            foreach (Task task in tasks)
            {
                if (task is { IsCompleted: false }) return false;
            }

            return true;
        }

        private bool AllTasksSucceeded(Task[] tasks)
        {
            foreach (Task task in tasks)
            {
                if (task != null && (!task.IsCompleted || task.Status == TaskStatus.Faulted)) return false;
            }

            return true;
        }

        private bool AnyTasksFaulted(Task[] tasks)
        {
            foreach (Task task in tasks)
            {
                if (task is { Status: TaskStatus.Faulted }) { return true; }
            }

            return false;
        }

        private void LogTaskErrors(Task task)
        {
            foreach (var e in task.Exception.InnerExceptions)
            {
                OvrAvatarLog.LogError($"{e.Message}\n{e.StackTrace}", primitiveLogScope);
            }
        }
#endif // !UNITY_WEBGL

        // Helper method for matching imageData to textureData, allows use of local references
        private void FindTexture(OvrAvatarResourceLoader loader, MaterialInfo materialInfo, OvrAvatarImage[] images,
            uint imageIndex, CAPI.ovrAvatar2Id resourceId)
        {
            ref readonly var imageData = ref materialInfo.imageData[imageIndex];

            for (var texIdx = 0; texIdx < materialInfo.texturesData.Length; ++texIdx)
            {
                ref readonly var textureData = ref materialInfo.texturesData[texIdx];

                if (textureData.imageId == imageData.id)
                {
                    OvrAvatarLog.LogVerbose(
                        $"Found match for image index {imageIndex} to texture index {texIdx}",
                        primitiveLogScope);
                    // Resolve the image now.
                    OvrAvatarImage image;
                    if (!OvrAvatarManager.GetOvrAvatarAsset(imageData.id, out image))
                    {
                        OvrAvatarLog.LogDebug($"Created image for id {imageData.id}", primitiveLogScope);
                        image = loader.CreateImage(in textureData, in imageData, imageIndex, resourceId);
                    }

                    OvrAvatarLog.Assert(image != null, primitiveLogScope);
                    images[imageIndex] = image;

                    break;
                }
            }

            if (images[imageIndex] == null)
            {
                OvrAvatarLog.LogWarning($"Failed to find textures data for image {imageData.id}", primitiveLogScope);
                // TODO: Assign some sort of fallback image?
            }
        }

        #endregion

        private CancellationToken GetCancellationToken()
        {
            return
#if !UNITY_WEBGL
            _cancellationTokenSource.Token;
#else
            CancellationToken.None;
#endif
        }

        private void StripExcludedSubMeshes(ref NativeArray<ushort> triangles)
        {
            var ct = GetCancellationToken();
            ct.ThrowIfCancellationRequested();

            if (subMeshInclusionFlags != CAPI.ovrAvatar2EntitySubMeshInclusionFlags.All)
            {
                uint subMeshCount = 0;
                var countResult = CAPI.ovrAvatar2Primitive_GetSubMeshCount(assetId, out subMeshCount);
                ct.ThrowIfCancellationRequested();

                if (countResult.IsSuccess())
                {
                    unsafe
                    {
                        for (uint subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                        {
                            CAPI.ovrAvatar2PrimitiveSubmesh subMesh;
                            var subMeshResult =
                                CAPI.ovrAvatar2Primitive_GetSubMeshByIndex(assetId, subMeshIndex, out subMesh);
                            ct.ThrowIfCancellationRequested();
                            if (subMeshResult.IsSuccess())
                            {
                                // TODO this should honor the _activeSubMeshesIncludeUntyped flag
                                // ^ This is not possible as that is an OvrAvatarEntity flag
                                var inclusionType = subMesh.inclusionFlags;
                                if ((inclusionType & subMeshInclusionFlags) == 0 &&
                                    inclusionType != CAPI.ovrAvatar2EntitySubMeshInclusionFlags.None)
                                {
                                    uint triangleIndex = subMesh.indexStart;
                                    for (uint triangleCount = 0; triangleCount < subMesh.indexCount; triangleCount++)
                                    {
                                        // current strategy is to degenerate the triangle...
                                        int triangleBase = (int)(triangleIndex + triangleCount);
                                        triangles[triangleBase] = 0;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void GetLodInfo()
        {
            lod = LOD_INVALID;

            var result = CAPI.ovrAvatar2Asset_GetLodFlags(assetId, out var lodFlag);
            if (result.IsSuccess())
            {
                lodFlags = lodFlag;

                // TODO: Handle lods as flags, not a single int. Until then, take the highest quality lod available (lowest bit)
                const UInt32 highBit = (UInt32)CAPI.ovrAvatar2EntityLODFlags.LOD_4;
                UInt32 flagValue = (UInt32)lodFlag;

                int i = 0, maskValue = 1 << 0;
                do
                {
                    if ((flagValue & maskValue) != 0)
                    {
                        lod = i;
                        break;
                    }

                    maskValue = 1 << ++i;
                } while (maskValue <= highBit);
            }
        }

        private void GetViewInfo()
        {
            var result = CAPI.ovrAvatar2Asset_GetViewFlags(assetId, out var flags);
            if (result.IsSuccess())
            {
                viewFlags = flags;
            }
            else
            {
                OvrAvatarLog.LogWarning($"GetViewFlags Failed: {result}", primitiveLogScope);
            }
        }

        private void GetManifestationInfo()
        {
            var result = CAPI.ovrAvatar2Asset_GetManifestationFlags(assetId, out var flags);
            if (result.IsSuccess())
            {
                manifestationFlags = flags;
            }
            else
            {
                OvrAvatarLog.LogWarning($"GetManifestationFlags Failed: {result}", primitiveLogScope);
            }
        }

        private void GetSubMeshInclusionInfo()
        {
            // sub mesh inclusion flags used at this stage will work as load filters,
            // they must be specified in the creationInfo of the AvatarEntity before loading.
            var result = CAPI.ovrAvatar2Asset_GetSubMeshInclusionFlags(assetId, out var flags);
            if (result.IsSuccess())
            {
                subMeshInclusionFlags = flags;
            }
            else
            {
                OvrAvatarLog.LogWarning($"GetSubMeshInclusionInfo Failed: {result}", primitiveLogScope);
            }
        }

        private void GetQualityInfo()
        {
            var result = CAPI.ovrAvatar2Asset_GetQuality(assetId, out var flags);
            if (result.IsSuccess())
            {
                quality = flags;
            }
            else
            {
                OvrAvatarLog.LogWarning($"GetQuality Failed: {result}", primitiveLogScope);
            }
        }

        /////////////////////////////////////////////////
        //:: Vertex Buffer API
        // TODO: Factor out into its own file, currently meshInfo access is required for that.

        private void CreateVertexBuffer(MeshInfo meshInfo)
        {
            unsafe
            {
                // Apply mesh info on main thread
                // Get the vertex format information from the fetched vertex data.
                // We need to build a dynamic layout based on the actual data present.
                var vertexCount = (int)meshInfo.vertexCount;
                var vertexFormat = GetVertexFormat(meshInfo, out var vertexStrides);

                var vertices = new NativeArray<byte>[VF_STREAM_COUNT];
                // always have positions of some sort
                vertices[VF_STREAM_0] = new NativeArray<byte>(vertexStrides[VF_STREAM_0] * vertexCount, _nativeAllocator, _nativeArrayInit);

                // VF_STREAM_1 is a direct copy from SDK to Unity, so nothing to do here

                // may or may not have a stream 2, we wont if using compute skinner
                if (vertexStrides[VF_STREAM_2] > 0)
                {
                    vertices[VF_STREAM_2] = new NativeArray<byte>(vertexStrides[VF_STREAM_2] * vertexCount, _nativeAllocator, _nativeArrayInit);

                }

                // Create a vertex buffer using the format and stride.
                meshInfo.VertexBufferFormat = new VertexBufferFormat
                (
                    /* vertexFormat = */ vertexFormat,
                    /* vertexCount = */ vertexCount,
                    /* vertexStrides = */ vertexStrides,
                    /* vertexLayout = */ CreateVertexLayout(vertexFormat, meshInfo),
                    /* vertexStreams = */ vertices
                );
            }
        }

        private VertexFormat GetVertexFormat(MeshInfo meshInfo, out VertexBufferFormat.VertexBufferStrides vertexStrides)
        {
            // TODO: Support different attribute formats rather than hardcoding them. This will be useful for quantizing
            // vertex data to reduce vertex shader read bandwidth.
            // TODO: Use constants for vector and color sizes.
            VertexFormat vertexFormat = VertexFormat.VF_POSITION;
            vertexStrides = default;

            if (meshInfo.verts.IsCreated && meshInfo.verts.Length > 0)
            {
                vertexStrides[VF_STREAM_0] = 3 * sizeof(float);
            }
            else
            {
                // don't actually need positions, just stand in garbage data to keep unity happy
                vertexStrides[VF_STREAM_0] = sizeof(UInt32);
            }

            if (OvrAvatarManager.Instance.UnitySMRSupported)
            {
                if (meshInfo.normals.IsCreated && meshInfo.normals.Length > 0)
                {
                    vertexFormat |= VertexFormat.VF_NORMAL;
                    vertexStrides[VF_STREAM_0] += 3 * sizeof(float);
                }
                if (meshInfo.hasTangents && meshInfo.tangents.Length > 0)
                {
                    vertexFormat |= VertexFormat.VF_TANGENT;
                    vertexStrides[VF_STREAM_0] += 4 * sizeof(float);
                }
            }

            if (meshInfo.staticAttributes.Length > 0)
            {
                if (meshInfo.hasColors)
                {
                    vertexFormat |= VertexFormat.VF_COLOR;
                    vertexStrides[VF_STREAM_1] += 4;
                }
                if (meshInfo.hasTextureCoords)
                {
                    vertexFormat |= VertexFormat.VF_TEXCOORD0;
                    vertexStrides[VF_STREAM_1] += 2 * sizeof(UInt16);
                }
                if (meshInfo.hasProperties)
                {
                    vertexFormat |= VertexFormat.VF_COLOR_ORMT;
                    vertexStrides[VF_STREAM_1] += 4;
                }
            }
            if (data.jointCount > 0 && OvrAvatarManager.Instance.UnitySMRSupported)
            {
                vertexFormat |= (VertexFormat.VF_BONE_WEIGHTS | VertexFormat.VF_BONE_INDICES);
                vertexStrides[VF_STREAM_2] += 4 * sizeof(float);    // weights
                vertexStrides[VF_STREAM_2] += 4;    // bone indices
            }

            OvrAvatarLog.LogVerbose($"Vertex Format = {vertexFormat}, Strides = [{vertexStrides[VF_STREAM_0]}, {vertexStrides[VF_STREAM_1]}, {vertexStrides[VF_STREAM_2]}]", primitiveLogScope);
            return vertexFormat;
        }

        private VertexAttributeDescriptor[] CreateVertexLayout(VertexFormat format, MeshInfo meshInfo)
        {
            const int kMaxDescriptors = 8;

            var numDescriptorsNeeded = ((UInt32)format).PopCount();
            Debug.Assert(numDescriptorsNeeded <= kMaxDescriptors);

            // TODO: Support different attribute formats rather than hardcoding them.
            // This will be useful for quantizing vertex data to reduce vertex shader read bandwidth.
            var vertexLayout = new VertexAttributeDescriptor[(int)numDescriptorsNeeded];

            int numDescriptors = 0;
            // Note: Unity expects vertex attributes to exist in a specific order, any deviation causes an error.
            // Order: Position, Normal, Tangent, Color, TexCoord0, TexCoord1, ..., BlendWeights, BlendIndices
            if ((format & VertexFormat.VF_POSITION) == VertexFormat.VF_POSITION)
            {
                if (meshInfo.verts.IsCreated && meshInfo.verts.Length > 0)
                {
                    vertexLayout[numDescriptors++] =
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, VF_STREAM_0);
                }
                else
                {
                    // just some stand in garbage values to keep Unity happy. need 4 elements because Unity also requires 4 byte alignment.
                    // Not perfect, but 4 bytes instead of 12 isn't too bad.
                    vertexLayout[numDescriptors++] =
                        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.SNorm8, 4, VF_STREAM_0);
                }
            }
            if ((format & VertexFormat.VF_NORMAL) == VertexFormat.VF_NORMAL)
            {
                vertexLayout[numDescriptors++] =
                    new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, VF_STREAM_0);
            }
            if ((format & VertexFormat.VF_TANGENT) == VertexFormat.VF_TANGENT)
            {
                vertexLayout[numDescriptors++] =
                    new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, VF_STREAM_0);
            }
            if ((format & VertexFormat.VF_COLOR) == VertexFormat.VF_COLOR)
            {
                vertexLayout[numDescriptors++] =
                    new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, VF_STREAM_1);
            }
            if ((format & VertexFormat.VF_TEXCOORD0) == VertexFormat.VF_TEXCOORD0)
            {
                vertexLayout[numDescriptors++] =
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.UNorm16, 2, VF_STREAM_1);
            }
            if ((format & VertexFormat.VF_COLOR_ORMT) == VertexFormat.VF_COLOR_ORMT)
            {
                vertexLayout[numDescriptors++] =
                    new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm8, 4, VF_STREAM_1);
            }
            if ((format & VertexFormat.VF_BONE_WEIGHTS) == VertexFormat.VF_BONE_WEIGHTS)
            {
                vertexLayout[numDescriptors++] =
                    new VertexAttributeDescriptor(VertexAttribute.BlendWeight, VertexAttributeFormat.Float32, 4, VF_STREAM_2);
            }
            if ((format & VertexFormat.VF_BONE_INDICES) == VertexFormat.VF_BONE_INDICES)
            {
                vertexLayout[numDescriptors++] =
                    new VertexAttributeDescriptor(VertexAttribute.BlendIndices, VertexAttributeFormat.UInt8, 4, VF_STREAM_2);
            }

            Debug.Assert(numDescriptors > 0);
            Debug.Assert(numDescriptors == numDescriptorsNeeded);
            return vertexLayout;
        }

        private void CopyMeshDataIntoVertexBufferAndDispose(MeshInfo meshInfo)
        {
            var vertexBuffer = meshInfo.VertexBufferFormat;
            var vertexFormat = vertexBuffer.vertexFormat;

            unsafe
            {
                // VF_STREAM_0
                if ((vertexFormat & (VertexFormat.VF_POSITION | VertexFormat.VF_NORMAL | VertexFormat.VF_TANGENT)) != 0)
                {
                    var vertexStride = vertexBuffer.vertexStrides[VF_STREAM_0];

                    var vertsPtr = meshInfo.verts.IsCreated && !meshInfo.verts.IsNull() && meshInfo.verts.Length > 0
                            ? (Vector3*)meshInfo.verts.GetPtr() : null;
                    Vector3* normsPtr =
                        meshInfo.normals.IsCreated && !meshInfo.normals.IsNull() && meshInfo.normals.Length > 0
                            ? (Vector3*)meshInfo.normals.GetPtr() : null;
                    var tangsPtr =
                        meshInfo.tangents.IsCreated && !meshInfo.tangents.IsNull() && meshInfo.tangents.Length > 0
                            ? (Vector4*)meshInfo.tangents.GetPtr() : null;

                    Vector4 defaultTangent = Vector3.forward;
                    Vector3 defaultNormal = Vector3.forward;
                    Vector3 defaultPos = Vector3.zero;

                    var outBuffer = vertexBuffer.vertexStreams[VF_STREAM_0].GetPtr();
                    for (int i = 0; i < vertexBuffer.vertexCount; i++)
                    {
                        byte* outBufferOffset = outBuffer + (vertexStride * i);
                        if ((vertexFormat & VertexFormat.VF_POSITION) == VertexFormat.VF_POSITION)
                        {
                            if (vertsPtr != null)
                            {
                                const int kPositionSize = 3 * sizeof(float);
                                Vector3* outPos = (Vector3*)outBufferOffset;
                                *outPos = vertsPtr != null ? vertsPtr[i] : defaultPos;
                                outBufferOffset += kPositionSize;
                            }
                            else
                            {
                                //dummy data to keep Unity happy, while using as little memory as possible
                                const int kPositionSize = sizeof(UInt32);
                                UInt32* outPos = (UInt32*)outBufferOffset;
                                *outPos = 0;
                                outBufferOffset += kPositionSize;
                            }
                        }

                        if ((vertexFormat & VertexFormat.VF_NORMAL) == VertexFormat.VF_NORMAL)
                        {
                            const int kNormalSize = 3 * sizeof(float);
                            Vector3* outNrm = (Vector3*)outBufferOffset;
                            *outNrm = normsPtr != null ? normsPtr[i] : defaultNormal;
                            outBufferOffset += kNormalSize;
                        }

                        if ((vertexFormat & VertexFormat.VF_TANGENT) == VertexFormat.VF_TANGENT)
                        {
                            const int kTangentSize = 4 * sizeof(float);
                            Vector4* outTan = (Vector4*)outBufferOffset;
                            *outTan = tangsPtr != null ? tangsPtr[i] : defaultTangent;
                            outBufferOffset += kTangentSize;
                        }
                    }

                    meshInfo.MeshVertsComplete();
                    meshInfo.MeshNormalsComplete();
                    meshInfo.MeshTangentsComplete();
                }

                // VF_STREAM_2
                if ((vertexFormat & (VertexFormat.VF_BONE_WEIGHTS | VertexFormat.VF_BONE_INDICES)) != 0)
                {
                    var vertexStride = vertexBuffer.vertexStrides[VF_STREAM_2];

                    var outBuffer = vertexBuffer.vertexStreams[VF_STREAM_2].GetPtr();
                    fixed (BoneWeight* boneWeightsPtr = meshInfo.boneWeights)
                    {
                        for (int i = 0; i < vertexBuffer.vertexCount; i++)
                        {
                            byte* outBufferOffset = outBuffer + (vertexStride * i);
                            var boneWeightPtr = boneWeightsPtr + i;
                            if ((vertexFormat & VertexFormat.VF_BONE_WEIGHTS) == VertexFormat.VF_BONE_WEIGHTS)
                            {
                                const int kBoneWeightSize = 4 * sizeof(float);
                                Vector4* outWeights = (Vector4*)outBufferOffset;
                                outWeights->x = boneWeightPtr->weight0;
                                outWeights->y = boneWeightPtr->weight1;
                                outWeights->z = boneWeightPtr->weight2;
                                outWeights->w = boneWeightPtr->weight3;
                                outBufferOffset += kBoneWeightSize;
                            }

                            if ((vertexFormat & VertexFormat.VF_BONE_INDICES) == VertexFormat.VF_BONE_INDICES)
                            {
                                const int kBoneIndexSize = 4;
                                Color32* outIndices = (Color32*)outBufferOffset;
                                outIndices->r = (byte)boneWeightPtr->boneIndex0;
                                outIndices->g = (byte)boneWeightPtr->boneIndex1;
                                outIndices->b = (byte)boneWeightPtr->boneIndex2;
                                outIndices->a = (byte)boneWeightPtr->boneIndex3;
                                outBufferOffset += kBoneIndexSize;
                            }
                        }
                    }

                    meshInfo.MeshBoneWeightsComplete();
                } // VF_STREAM_2
            }
        }

        private static void DisposeVertexBuffer(MeshInfo meshInfo)
        {
            if (meshInfo?.VertexBufferFormat.vertexStreams != null)
            {
                for (int i = 0; i < VF_STREAM_COUNT; ++i)
                {
                    meshInfo.VertexBufferFormat.vertexStreams[i].Reset();
                }
            }
        }

        /////////////////////////////////////////////////
        //:: Build Mesh

        private void RetrieveTriangles(MeshInfo meshInfo)
        {
            // Get index buffer, we will use this to strip out data for other LODs
            meshInfo.triangles = CreateIndexBuffer(data);
            if (!meshInfo.triangles.IsCreated)
            {
                throw new Exception("RetrieveTriangles failed");
            }

            // TODO: Confirm topology - we only currently support triangle
            triCount = (uint)(meshInfo.triangles.Length / 3);
        }

        private void RetrieveMeshData(MeshInfo meshInfo, bool usesComputeSkinnerOnly, bool disableMeshOptimization)
        {
            var ct = GetCancellationToken();
            ct.ThrowIfCancellationRequested();

            // Apply Data
            meshInfo.vertexCount = _bufferVertexCount;


            meshInfo.staticAttributes = CreateVertexStaticData(meshInfo, ct);

            // Ideally we won't upload positions. However Unity does require some minimal placeholder values,
            // so we will just put 4 bytes of garbage data in positions by default. There are a few cases
            // where we will need to turn this optimization off though.
            bool keepPositions = false;
            // need to keep positions if mesh optimization is disabled
            keepPositions = keepPositions || disableMeshOptimization;
            // need to keep positions if using any other skinners other than the compute skinner
            keepPositions = keepPositions || !usesComputeSkinnerOnly;
            // Have to keep positions if in the editor.
#if UNITY_EDITOR
            keepPositions = true;
#endif

            if (keepPositions)
            {
                meshInfo.SetVertexBuffer(CreateVertexPositions(ct));
            }

            // All of these aren't needed if only using the compute skinner
            if (!usesComputeSkinnerOnly)
            {
                meshInfo.SetNormalsBuffer(CreateVertexNormals(ct));
                meshInfo.SetTangentsBuffer(CreateVertexTangents(ct));
                meshInfo.SetBoneWeights(data.jointCount > 0 ? RetrieveBoneWeights(ct) : null);
            }
        }

        private void InvokeOnMeshLoaded(Mesh sourceMesh, MeshInfo meshInfo)
        {
            OvrAvatarLog.LogInfo($"InvokeOnAvatarMeshLoaded", "", OvrAvatarManager.Instance);
            Profiler.BeginSample("OvrAvatarManager::InvokeOnAvatarMeshLoaded Callbacks");
            try
            {
                if (OvrAvatarManager.Instance.HasMeshLoadListener)
                {
                    var destMesh = new OvrAvatarManager.MeshData(
                        sourceMesh.name,
                        sourceMesh.triangles,
                        // We want to decouple the GPU vertex representation from the CPU representation.
                        // So instead of reading from the mesh directly, we read from the internal mesh info.
                        (meshInfo.verts.IsCreated) ? meshInfo.verts.ToArray() : Array.Empty<Vector3>(),
                        (meshInfo.normals.IsCreated) ? meshInfo.normals.ToArray() : Array.Empty<Vector3>(),
                        (meshInfo.tangents.IsCreated) ? meshInfo.tangents.ToArray() : Array.Empty<Vector4>(),
                        meshInfo.boneWeights,
                        // Bind poses are not part of the vertex data.
                        sourceMesh.bindposes
                    );
                    OvrAvatarManager.Instance.InvokeMeshLoadEvent(this, destMesh);
                }
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(
                    "OnAvatarMeshLoaded user callback", e, primitiveLogScope,
                    OvrAvatarManager.Instance);
            }
            finally { Profiler.EndSample(); }
        }

        #region Retrieve Primitive Data

        private delegate CAPI.ovrAvatar2Result VertexBufferAccessor(
            CAPI.ovrAvatar2VertexBufferId vertexBufferId, IntPtr buffer, UInt32 bytes,
            UInt32 stride);

        private delegate CAPI.ovrAvatar2Result VertexBufferAccessorWithPrimId(
            CAPI.ovrAvatar2Id primitiveId, CAPI.ovrAvatar2VertexBufferId vertexBufferId, IntPtr buffer, UInt32 bytes,
            UInt32 stride);

        private NativeArray<T> CreateVertexData<T>(
            VertexBufferAccessor accessor
            , string accessorName, CancellationToken ct) where T : unmanaged
        {
            ct.ThrowIfCancellationRequested();

            NativeArray<T> vertsBufferArray = default;
            try
            {
                vertsBufferArray = new NativeArray<T>((int)bufferVertexCount, _nativeAllocator, _nativeArrayInit);
                IntPtr vertsBuffer = vertsBufferArray.GetIntPtr();

                if (vertsBuffer == IntPtr.Zero)
                {
                    OvrAvatarLog.LogError($"ERROR: Null buffer allocated for input during `{accessorName}` - aborting");
                    return default;
                }

                var elementSize = UnsafeUtility.SizeOf<T>();
                UInt32 stride = (UInt32)elementSize;
                UInt32 bufferSize = vertsBufferArray.GetBufferSize(elementSize);
                var result = accessor(
                    data.vertexBufferId, vertsBuffer, bufferSize, stride);

                switch (result)
                {
                    case CAPI.ovrAvatar2Result.Success:
                        var resultBuffer = vertsBufferArray;
                        vertsBufferArray = default;
                        return resultBuffer;

                    case CAPI.ovrAvatar2Result.DataNotAvailable:
                        return default;

                    default:
                        OvrAvatarLog.LogError($"{accessorName} {result}", primitiveLogScope);
                        return default;
                }
            }
            finally { vertsBufferArray.Reset(); }
        }

        private NativeArray<T> CreateVertexDataWithPrimId<T>(VertexBufferAccessorWithPrimId accessor
            , string accessorName, CancellationToken ct) where T : unmanaged
        {
            ct.ThrowIfCancellationRequested();

            NativeArray<T> vertsBufferArray = default;
            try
            {
                vertsBufferArray = new NativeArray<T>((int)bufferVertexCount, _nativeAllocator, _nativeArrayInit);
                {
                    var elementSize = UnsafeUtility.SizeOf<T>();
                    var vertsBuffer = vertsBufferArray.GetIntPtr();
                    if (vertsBuffer == IntPtr.Zero)
                    {
                        OvrAvatarLog.LogError($"ERROR: Null buffer allocated for input during `{accessorName}` - aborting");
                        return default;
                    }

                    var bufferSize = vertsBufferArray.GetBufferSize(elementSize);
                    var stride = (UInt32)elementSize;

                    var result = accessor(data.id, data.vertexBufferId, vertsBuffer, bufferSize, stride);
                    var resultBuffer = vertsBufferArray;
                    vertsBufferArray = default;
                    return resultBuffer;
                }
            }
            finally { vertsBufferArray.Reset(); }
        }

        private NativeArray<Vector3> CreateVertexPositions(CancellationToken ct)
        {
            return CreateVertexData<Vector3>(
                CAPI.ovrAvatar2VertexBuffer_GetPositions, "GetVertexPositions", ct);
        }

        private NativeArray<Vector3> CreateVertexNormals(CancellationToken ct)
        {
            return CreateVertexData<Vector3>(
                CAPI.ovrAvatar2VertexBuffer_GetNormals, "GetVertexNormals", ct);
        }

        private NativeArray<Vector4> CreateVertexTangents(CancellationToken ct)
        {
            return CreateVertexData<Vector4>(
                CAPI.ovrAvatar2VertexBuffer_GetTangents, "GetVertexTangents", ct);
        }

        private NativeArray<byte> CreateVertexStaticData(MeshInfo meshInfo, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            NativeArray<byte> resultBuffer = default;
            NativeArray<ovrAvatar2BufferMetaData> metaData = default;
            try
            {
                metaData = new NativeArray<ovrAvatar2BufferMetaData>(3, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                UInt64 dataBufferSize;
                if (!OvrCompactMeshData_GetMetaData(data.vertexBufferId, ovrAvatar2CompactMeshAttributes.All, metaData, out dataBufferSize))
                {
                    OvrAvatarLog.LogError($"Failed to retrieve static mesh meta data from Avatar SDK", primitiveLogScope);
                    return default;
                }

                if (metaData[0].dataFormat != ovrAvatar2DataFormat.Invalid)
                {
                    meshInfo.hasColors = true;
                }

                if (metaData[1].dataFormat != ovrAvatar2DataFormat.Invalid)
                {
                    meshInfo.hasTextureCoords = true;
                }

                if (metaData[2].dataFormat != ovrAvatar2DataFormat.Invalid)
                {
                    meshInfo.hasProperties = true;
                }

                resultBuffer = new NativeArray<byte>((int)dataBufferSize, _nativeAllocator, NativeArrayOptions.UninitializedMemory);

                unsafe
                {
                    ovrAvatar2DataBlock dataBlock;
                    dataBlock.data = resultBuffer.GetPtr();
                    dataBlock.size = dataBufferSize;
                    if (!OvrCompactMeshData_CopyBuffer(this.assetId, data.vertexBufferId, ovrAvatar2CompactMeshAttributes.All, dataBlock))
                    {
                        OvrAvatarLog.LogError($"Failed to retrieve static mesh attributes from Avatar SDK", primitiveLogScope);
                        resultBuffer.Dispose();
                        return default;
                    }
                }
            }
            finally
            {
                metaData.Reset();
            }

            return resultBuffer;
        }

        private BoneWeight[] RetrieveBoneWeights(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var vec4usStride = (UInt32)UnsafeUtility.SizeOf<CAPI.ovrAvatar2Vector4us>();
            var vec4fStride = (UInt32)UnsafeUtility.SizeOf<CAPI.ovrAvatar2Vector4f>();

            var indicesBuffer =
                new NativeArray<CAPI.ovrAvatar2Vector4us>((int)bufferVertexCount, _nativeAllocator, _nativeArrayInit);
            var weightsBuffer =
                new NativeArray<CAPI.ovrAvatar2Vector4f>((int)bufferVertexCount, _nativeAllocator, _nativeArrayInit);

            try
            {
                IntPtr indicesPtr = indicesBuffer.GetIntPtr();
                IntPtr weightsPtr = weightsBuffer.GetIntPtr();

                if (indicesPtr == IntPtr.Zero || weightsPtr == IntPtr.Zero)
                {
                    OvrAvatarLog.LogError("ERROR: Null buffer allocated for input during `RetrieveBoneWeights` - aborting");
                    return Array.Empty<BoneWeight>();
                }

                var indicesBufferSize = indicesBuffer.GetBufferSize(vec4usStride);
                var weightsBufferSize = weightsBuffer.GetBufferSize(vec4fStride);

                var result = CAPI.ovrAvatar2VertexBuffer_GetJointIndices(
                    data.vertexBufferId, indicesPtr, indicesBufferSize, vec4usStride);
                ct.ThrowIfCancellationRequested();
                if (result == CAPI.ovrAvatar2Result.DataNotAvailable) { return Array.Empty<BoneWeight>(); }
                else if (result != CAPI.ovrAvatar2Result.Success)
                {
                    OvrAvatarLog.LogError($"GetVertexJointIndices {result}", primitiveLogScope);
                    return null;
                }

                result = CAPI.ovrAvatar2VertexBuffer_GetJointWeights(
                    data.vertexBufferId, weightsPtr,
                    weightsBufferSize, vec4fStride);
                ct.ThrowIfCancellationRequested();
                if (result == CAPI.ovrAvatar2Result.DataNotAvailable) { return Array.Empty<BoneWeight>(); }
                else if (result != CAPI.ovrAvatar2Result.Success)
                {
                    OvrAvatarLog.LogError($"GetVertexJointWeights {result}", primitiveLogScope);
                    return null;
                }

                ct.ThrowIfCancellationRequested();

                using (var boneWeightsSrc =
                       new NativeArray<BoneWeight>((int)bufferVertexCount, _nativeAllocator, _nativeArrayInit))
                {
                    unsafe
                    {
                        var srcPtr = boneWeightsSrc.GetPtr();

                        // Check for allocation failure
                        if (srcPtr == null)
                        {
                            OvrAvatarLog.LogError("ERROR: Null buffer allocated for output during `RetrieveBoneWeights` - aborting");
                            return Array.Empty<BoneWeight>();
                        }

                        var indices = indicesBuffer.GetPtr();
                        var weights = weightsBuffer.GetPtr();

                        for (int i = 0; i < bufferVertexCount; ++i)
                        {
                            ref CAPI.ovrAvatar2Vector4us jointIndex = ref indices[i];
                            ref CAPI.ovrAvatar2Vector4f jointWeight = ref weights[i];

                            srcPtr[i] = new BoneWeight
                            {
                                boneIndex0 = jointIndex.x,
                                boneIndex1 = jointIndex.y,
                                boneIndex2 = jointIndex.z,
                                boneIndex3 = jointIndex.w,
                                weight0 = jointWeight.x,
                                weight1 = jointWeight.y,
                                weight2 = jointWeight.z,
                                weight3 = jointWeight.w
                            };
                        }
                    }

                    ct.ThrowIfCancellationRequested();

                    return boneWeightsSrc.ToArray();
                }
            }
            finally
            {
                indicesBuffer.Dispose();
                weightsBuffer.Dispose();
            }
        }

        private NativeArray<UInt16> CreateIndexBuffer(in CAPI.ovrAvatar2Primitive prim)
        {
            var ct = GetCancellationToken();
            ct.ThrowIfCancellationRequested();

            NativeArray<UInt16> triBuffer = default;
            try
            {
                triBuffer = new NativeArray<UInt16>((int)data.indexCount, _nativeAllocator, _nativeArrayInit);

                UInt32 bufferSize = triBuffer.GetBufferSize(sizeof(UInt16));
                bool result = CAPI.OvrAvatar2Primitive_GetIndexData(assetId, in triBuffer, bufferSize);
                if (!result)
                {
                    return default;
                }

                ct.ThrowIfCancellationRequested();
                return triBuffer;
            }
            finally { }
        }

        #endregion

        /////////////////////////////////////////////////
        //:: Build Material

        #region Build Material

        private void Material_GetTexturesData(CAPI.ovrAvatar2Id resourceId, ref MaterialInfo materialInfo)
        {
            var ct = GetCancellationToken();
            ct.ThrowIfCancellationRequested();

            CAPI.ovrAvatar2Result result;

            // Get data for all textures
            materialInfo.texturesData = new CAPI.ovrAvatar2MaterialTexture[data.textureCount];
            for (UInt32 i = 0; i < data.textureCount; ++i)
            {
                ref var materialTexture = ref materialInfo.texturesData[i];
                result = CAPI.ovrAvatar2Primitive_GetMaterialTextureByIndex(assetId, i, out materialTexture);
                ct.ThrowIfCancellationRequested();
                if (result != CAPI.ovrAvatar2Result.Success)
                {
                    OvrAvatarLog.LogError($"GetMaterialTextureByIndex ({i}) {result}", primitiveLogScope);

                    materialInfo.texturesData[i] = default;
                    continue;
                }

                if (materialTexture.type == CAPI.ovrAvatar2MaterialTextureType.MetallicRoughness)
                {
                    materialInfo.hasMetallic = true;
                }
            }

            // Get data for all images
            result = CAPI.ovrAvatar2Asset_GetImageCount(resourceId, out UInt32 imageCount);
            ct.ThrowIfCancellationRequested();
            if (result != CAPI.ovrAvatar2Result.Success)
            {
                OvrAvatarLog.LogError($"GetImageCount {result}", primitiveLogScope);
                return;
            }

            materialInfo.imageData = new CAPI.ovrAvatar2Image[imageCount];

            for (UInt32 i = 0; i < imageCount; ++i)
            {
                ref var imageData = ref materialInfo.imageData[i];
                result = CAPI.ovrAvatar2Asset_GetImageByIndex(resourceId, i, out imageData);
                ct.ThrowIfCancellationRequested();
                if (result != CAPI.ovrAvatar2Result.Success)
                {
                    OvrAvatarLog.LogError($"GetImageByIndex ({i}) {result}", primitiveLogScope);

                    materialInfo.imageData[i] = default;
                    continue;
                }
            }
        }

        private void ApplyTexture(Texture2D texture, CAPI.ovrAvatar2MaterialTexture textureData)
        {
            switch (textureData.type)
            {
                case CAPI.ovrAvatar2MaterialTextureType.BaseColor:
                    if (!string.IsNullOrEmpty(_shaderConfig.NameTextureParameter_baseColorTexture))
                        material.SetTexture(_shaderConfig.NameTextureParameter_baseColorTexture, texture);
                    material.SetColor(
                        _shaderConfig.NameColorParameter_BaseColorFactor,
                        _shaderConfig.UseColorParameter_BaseColorFactor ? textureData.factor : Color.white);
                    material.mainTexture = texture;
                    break;

                case CAPI.ovrAvatar2MaterialTextureType.Normal:
                    if (!string.IsNullOrEmpty(_shaderConfig.NameTextureParameter_normalTexture))
                        material.SetTexture(_shaderConfig.NameTextureParameter_normalTexture, texture);
                    break;

                case CAPI.ovrAvatar2MaterialTextureType.Emissive:
                    if (!string.IsNullOrEmpty(_shaderConfig.NameTextureParameter_emissiveTexture))
                        material.SetTexture(_shaderConfig.NameTextureParameter_emissiveTexture, texture);
                    break;

                case CAPI.ovrAvatar2MaterialTextureType.Occulusion:
                    if (!string.IsNullOrEmpty(_shaderConfig.NameTextureParameter_occlusionTexture))
                        material.SetTexture(_shaderConfig.NameTextureParameter_occlusionTexture, texture);
                    break;

                case CAPI.ovrAvatar2MaterialTextureType.MetallicRoughness:
                    if (!string.IsNullOrEmpty(_shaderConfig.NameTextureParameter_metallicRoughnessTexture))
                        material.SetTexture(_shaderConfig.NameTextureParameter_metallicRoughnessTexture, texture);
                    material.SetFloat(
                        _shaderConfig.NameFloatParameter_MetallicFactor,
                        _shaderConfig.UseFloatParameter_MetallicFactor ? textureData.factor.x : 1f);
                    material.SetFloat(
                        _shaderConfig.NameFloatParameter_RoughnessFactor,
                        _shaderConfig.UseFloatParameter_RoughnessFactor ? textureData.factor.y : 1f);
                    break;

                case CAPI.ovrAvatar2MaterialTextureType.UsedInExtension:
                    // Let extensions handle it
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        #endregion

        /////////////////////////////////////////////////
        //:: Build Other Data

        private void SetupMorphTargets(MorphTargetInfo[] morphTargetInfo)
        {
            var ct = GetCancellationToken();
            ct.ThrowIfCancellationRequested();

            var sizeOfOvrVector3 = UnsafeUtility.SizeOf<CAPI.ovrAvatar2Vector3f>();
            UInt32 bufferSize = (UInt32)(sizeOfOvrVector3 * bufferVertexCount);
            UInt32 stride = (UInt32)sizeOfOvrVector3;

            if (morphTargetInfo.Length != morphTargetCount)
            {
                throw new Exception(
                    $"Incorrect morphTargetInfo[] size. Was {morphTargetInfo.Length}, but expected {morphTargetCount}");
            }

            unsafe
            {
                const int nameBufferLength = 255;
                byte* nameBuffer = stackalloc byte[nameBufferLength];

                var minLength = Mathf.Min(morphTargetCount, morphTargetInfo.Length);
                for (UInt32 iMorphTarget = 0; iMorphTarget < minLength; ++iMorphTarget)
                {
                    // Would be nice if we had a single simple CAPI that returned what attributes were available, one call to get all 3
                    // we want to figure out which are available before we spend time allocating giant buffers.
                    var positionsResult =
                        CAPI.ovrAvatar2MorphTarget_GetVertexPositions(
                            data.morphTargetBufferId, iMorphTarget,
                            null, 0, stride);
                    if (!positionsResult.IsSuccess())
                    {
                        OvrAvatarLog.LogError(
                            $"MorphTarget_GetVertexPositions ({iMorphTarget}) {positionsResult}",
                            primitiveLogScope);
                        continue;
                    }

                    var normalsResult =
                        CAPI.ovrAvatar2MorphTarget_GetVertexNormals(
                            data.morphTargetBufferId, iMorphTarget,
                            null, 0, stride);
                    bool normalsAvailable = normalsResult.IsSuccess();
                    if (!normalsAvailable && normalsResult != CAPI.ovrAvatar2Result.DataNotAvailable)
                    {
                        OvrAvatarLog.LogError(
                            $"MorphTarget_GetVertexNormals ({iMorphTarget}) {normalsResult}",
                            primitiveLogScope);
                        continue;
                    }

                    var tangentsResult =
                        CAPI.ovrAvatar2MorphTarget_GetVertexTangents(
                            data.morphTargetBufferId, iMorphTarget,
                            null, 0, stride);
                    bool tangentsAvailable = tangentsResult.IsSuccess();
                    if (!tangentsAvailable && tangentsResult != CAPI.ovrAvatar2Result.DataNotAvailable)
                    {
                        OvrAvatarLog.LogError(
                            $"MorphTarget_GetVertexTangents ({iMorphTarget}) {tangentsResult}",
                            primitiveLogScope);
                        continue;
                    }

                    ct.ThrowIfCancellationRequested();

                    NativeArray<Vector3> positionsArray = default;
                    NativeArray<Vector3> normalsArray = default;
                    NativeArray<Vector3> tangentsArray = default;
                    try
                    {
                        // Positions
                        positionsArray =
                            new NativeArray<Vector3>((int)bufferVertexCount, _nativeAllocator, _nativeArrayInit);

                        positionsResult =
                            CAPI.ovrAvatar2MorphTarget_GetVertexPositions(
                                data.morphTargetBufferId, iMorphTarget,
                                positionsArray.CastOvrPtr(), bufferSize, stride);
                        ct.ThrowIfCancellationRequested();
                        if (!positionsResult.IsSuccess())
                        {
                            OvrAvatarLog.LogError(
                                $"MorphTarget_GetVertexPositions ({iMorphTarget}) {positionsResult}",
                                primitiveLogScope);
                            continue;
                        }

                        // Normals
                        if (normalsAvailable)
                        {
                            normalsArray = new NativeArray<Vector3>(
                                (int)bufferVertexCount, _nativeAllocator,
                                _nativeArrayInit);

                            normalsResult =
                                CAPI.ovrAvatar2MorphTarget_GetVertexNormals(
                                    data.morphTargetBufferId, iMorphTarget,
                                    normalsArray.CastOvrPtr(), bufferSize, stride);
                            ct.ThrowIfCancellationRequested();
                            normalsAvailable = normalsResult.IsSuccess();
                            if (!normalsAvailable && normalsResult != CAPI.ovrAvatar2Result.DataNotAvailable)
                            {
                                OvrAvatarLog.LogError(
                                    $"MorphTarget_GetVertexNormals ({iMorphTarget}) {normalsResult}",
                                    primitiveLogScope);
                                continue;
                            }
                        }

                        // Tangents
                        if (tangentsAvailable)
                        {
                            tangentsArray = new NativeArray<Vector3>(
                                (int)bufferVertexCount, _nativeAllocator,
                                _nativeArrayInit);

                            tangentsResult =
                                CAPI.ovrAvatar2MorphTarget_GetVertexTangents(
                                    data.morphTargetBufferId, iMorphTarget,
                                    tangentsArray.CastOvrPtr(),
                                    bufferSize, stride);
                            ct.ThrowIfCancellationRequested();
                            tangentsAvailable = tangentsResult.IsSuccess();
                            if (!tangentsAvailable && tangentsResult != CAPI.ovrAvatar2Result.DataNotAvailable)
                            {
                                OvrAvatarLog.LogError(
                                    $"MorphTarget_GetVertexTangents ({iMorphTarget}) {tangentsResult}",
                                    primitiveLogScope);
                                continue;
                            }
                        }

                        var nameResult =
                            CAPI.ovrAvatar2Asset_GetMorphTargetName(
                                data.morphTargetBufferId, iMorphTarget,
                                nameBuffer, nameBufferLength);
                        ct.ThrowIfCancellationRequested();

                        var name = string.Empty;
                        if (nameResult.IsSuccess())
                        {
                            name = Marshal.PtrToStringAnsi((IntPtr)nameBuffer);
                        }
                        else if (nameResult != CAPI.ovrAvatar2Result.NotFound)
                        {
                            OvrAvatarLog.LogError($"ovrAvatar2MorphTarget_GetName failed with {nameResult}"
                                , primitiveLogScope);
                        }

                        // If we failed to query the name, use the index
                        if (string.IsNullOrEmpty(name)) { name = "morphTarget" + iMorphTarget; }

                        // Add Morph Target
                        morphTargetInfo[iMorphTarget] = new MorphTargetInfo(
                            name,
                            positionsArray.ToArray(),
                            normalsAvailable ? normalsArray.ToArray() : null,
                            tangentsAvailable ? tangentsArray.ToArray() : null
                        );
                    }
                    finally
                    {
                        positionsArray.Reset();
                        normalsArray.Reset();
                        tangentsArray.Reset();
                    }
                }
            }
        }

        private void SetupSkin(ref MeshInfo meshInfo)
        {
            var ct = GetCancellationToken();
            ct.ThrowIfCancellationRequested();

            var bindPoses = Array.Empty<Matrix4x4>();
            var buildJoints = Array.Empty<int>();

            var jointCount = data.jointCount;
            if (jointCount > 0)
            {
                using var jointsInfoArray =
                    new NativeArray<CAPI.ovrAvatar2JointInfo>(
                        (int)jointCount, _nativeAllocator,
                        _nativeArrayInit);
                unsafe
                {
                    var jointInfoBuffer = jointsInfoArray.GetPtr();
                    var result = CAPI.ovrAvatar2Primitive_GetJointInfo(
                        assetId, jointInfoBuffer,
                        jointsInfoArray.GetBufferSize());
                    ct.ThrowIfCancellationRequested();

                    if (result.EnsureSuccess("ovrAvatar2Primitive_GetJointInfo", primitiveLogScope))
                    {
                        buildJoints = new int[jointCount];
                        bindPoses = new Matrix4x4[jointCount];
                        for (int i = 0; i < jointCount; ++i)
                        {
                            var jointInfoPtr = jointInfoBuffer + i;
                            ref var bindPose = ref bindPoses[i];

                            buildJoints[i] = jointInfoPtr->jointIndex;
                            jointInfoPtr->inverseBind.CopyToUnityMatrix(out bindPose); //Convert to Matrix4x4
                        }
                    }
                } // unsafe
            }

            ct.ThrowIfCancellationRequested();
            meshInfo.bindPoses = bindPoses;
            joints = buildJoints;
        }

        private void SetupJointIndicesOnly()
        {
            var ct = GetCancellationToken();
            ct.ThrowIfCancellationRequested();

            var buildJoints = Array.Empty<int>();

            var jointCount = data.jointCount;
            if (jointCount > 0)
            {
                using var jointsInfoArray =
                    new NativeArray<CAPI.ovrAvatar2JointInfo>(
                        (int)jointCount, _nativeAllocator,
                        _nativeArrayInit);
                unsafe
                {
                    var jointInfoBuffer = jointsInfoArray.GetPtr();
                    var result = CAPI.ovrAvatar2Primitive_GetJointInfo(
                        assetId, jointInfoBuffer,
                        jointsInfoArray.GetBufferSize());
                    ct.ThrowIfCancellationRequested();

                    if (result.EnsureSuccess("ovrAvatar2Primitive_GetJointInfo", primitiveLogScope))
                    {
                        buildJoints = new int[jointCount];
                    }
                } // unsafe
            }

            ct.ThrowIfCancellationRequested();
            joints = buildJoints;
        }

        private sealed class OvrAvatarGpuSkinnedPrimitiveBuilder : IDisposable
        {
            NativeArray<IntPtr> deltaPositions;
            NativeArray<IntPtr> deltaNormals;
            NativeArray<IntPtr> deltaTangents;

            GCHandle[] morphPosHandles;
            GCHandle[] morphNormalHandles;
            GCHandle[] morphTangentHandles;

            Task createPrimitivesTask = null;

            private MeshInfo _gpuSkinningMeshInfo;

            readonly string shortName;
            readonly uint morphTargetCount;

            public OvrAvatarGpuSkinnedPrimitiveBuilder(string name, uint morphTargetCnt)
            {
                shortName = name;
                morphTargetCount = morphTargetCnt;
            }

            public
#if !UNITY_WEBGL
                Task
#else // UNITY_WEBGL
                void
#endif // UNITY_WEBGL
                CreateGpuPrimitiveHelperTask(
                MeshInfo meshInfo,
                MorphTargetInfo[] morphTargetInfo,
                bool hasTangents)
            {
                OvrAvatarLog.AssertConstMessage(
                    createPrimitivesTask == null
                    , "recreating gpu and/or compute primitive",
                    primitiveLogScope);

                _gpuSkinningMeshInfo = meshInfo;
                _gpuSkinningMeshInfo.WillBuildGpuPrimitive();

#if !UNITY_WEBGL
                createPrimitivesTask = Task.Run(
                    () =>
#endif // !UNITY_WEBGL
                    {
                        // TODO: should get pointers to morph target data directly from Native

                        if (morphTargetCount == 0)
                        {
                            //nothing to do
                            return;
                        }

                        deltaPositions = new NativeArray<IntPtr>(
                            (int)morphTargetCount, _nativeAllocator, _nativeArrayInit);
                        if (deltaPositions.GetIntPtr() == IntPtr.Zero)
                        {
                            OvrAvatarLog.LogError($"ERROR: Null buffer allocated for `deltaPositions` `CreateGpuPrimitiveHelperTask` - aborting");
                            return;
                        }

                        deltaNormals = new NativeArray<IntPtr>(
                            (int)morphTargetCount, _nativeAllocator, _nativeArrayInit);
                        if (deltaNormals.GetIntPtr() == IntPtr.Zero)
                        {
                            OvrAvatarLog.LogError($"ERROR: Null buffer allocated for `deltaNormals` `CreateGpuPrimitiveHelperTask` - aborting");
                            return;
                        }
                        if (hasTangents)
                        {
                            deltaTangents =
                                new NativeArray<IntPtr>((int)morphTargetCount, _nativeAllocator, _nativeArrayInit);
                            if (deltaTangents.GetIntPtr() == IntPtr.Zero)
                            {
                                OvrAvatarLog.LogError($"ERROR: Null buffer allocated for `deltaTangents` `CreateGpuPrimitiveHelperTask` - aborting");
                                return;
                            }
                        }

                        try
                        {
                            morphPosHandles = new GCHandle[morphTargetCount];
                            morphNormalHandles = new GCHandle[morphTargetCount];
                            if (hasTangents) { morphTangentHandles = new GCHandle[morphTargetCount]; }
                        }
                        catch (OutOfMemoryException)
                        {
                            return;
                        }

                        var minLength = Mathf.Min(morphTargetCount, morphTargetInfo.Length);
                        for (var i = 0; i < minLength; ++i)
                        {
                            morphPosHandles[i] = GCHandle.Alloc(
                                morphTargetInfo[i].targetPositions, GCHandleType.Pinned);
                            morphNormalHandles[i] = GCHandle.Alloc(
                                morphTargetInfo[i].targetNormals, GCHandleType.Pinned);

                            deltaPositions[i] = morphPosHandles[i].AddrOfPinnedObject();
                            deltaNormals[i] = morphNormalHandles[i].AddrOfPinnedObject();

                            if (hasTangents)
                            {
                                morphTangentHandles[i] =
                                    GCHandle.Alloc(morphTargetInfo[i].targetTangents, GCHandleType.Pinned);
                                deltaTangents[i] = morphTangentHandles[i].AddrOfPinnedObject();
                            }
                        }

                        createPrimitivesTask = null;
                    }
#if !UNITY_WEBGL
                    );
                return createPrimitivesTask;
#endif //!UNITY_WEBGL
            }

            public OvrAvatarGpuSkinnedPrimitive BuildPrimitive(MeshInfo meshInfo, Int32[] joints)
            {
                OvrAvatarLog.Assert(meshInfo == _gpuSkinningMeshInfo, primitiveLogScope);

                var primitive = new OvrAvatarGpuSkinnedPrimitive(
                    shortName,
                    _gpuSkinningMeshInfo.vertexCount,
                    in _gpuSkinningMeshInfo.verts,
                    in _gpuSkinningMeshInfo.normals,
                    in _gpuSkinningMeshInfo.tangents,
                    morphTargetCount,
                    in deltaPositions,
                    in deltaNormals,
                    in deltaTangents,
                    (uint)joints.Length,
                    _gpuSkinningMeshInfo.boneWeights,
                    () => { _gpuSkinningMeshInfo.NeutralPoseTexComplete(); },
                    () =>
                    {
                        _gpuSkinningMeshInfo.DidBuildGpuPrimitive();
                        _gpuSkinningMeshInfo = null;
                    });


                return primitive;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            private void Dispose(bool isDisposing)
            {
#if !UNITY_WEBGL
                if (createPrimitivesTask != null)
                {
                    createPrimitivesTask.Wait();
                    createPrimitivesTask = null;
                }
#endif // !UNITY_WEBGL

                deltaPositions.Reset();
                deltaNormals.Reset();
                deltaTangents.Reset();

                FreeHandles(ref morphPosHandles);
                FreeHandles(ref morphNormalHandles);
                FreeHandles(ref morphTangentHandles);

                if (_gpuSkinningMeshInfo != null)
                {
                    _gpuSkinningMeshInfo.CancelledBuildPrimitives();
                    _gpuSkinningMeshInfo = null;
                }
            }

            private static void FreeHandles(ref GCHandle[] handles)
            {
                if (handles != null)
                {
                    foreach (var handle in handles)
                    {
                        if (handle.IsAllocated) { handle.Free(); }
                    }

                    handles = null;
                }
            }

            ~OvrAvatarGpuSkinnedPrimitiveBuilder()
            {
                Dispose(false);
            }
        }

        private const Allocator _nativeAllocator = Allocator.Persistent;
        private const NativeArrayOptions _nativeArrayInit = NativeArrayOptions.UninitializedMemory;
    }
}
