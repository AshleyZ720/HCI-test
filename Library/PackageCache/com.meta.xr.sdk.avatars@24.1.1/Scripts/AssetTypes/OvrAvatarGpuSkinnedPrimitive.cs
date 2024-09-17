using Oculus.Skinning;
using Oculus.Skinning.GpuSkinning;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;

namespace Oculus.Avatar2
{
    public sealed class OvrAvatarGpuSkinnedPrimitive : IDisposable
    {
        private const string LOG_SCOPE = "OvrAvatarGPUSkinnedPrimitive";
        private const NativeArrayOptions NATIVE_ARRAY_INIT = NativeArrayOptions.UninitializedMemory;

        public bool IsLoading => _buildTextureSlice.IsValid;

        public class SourceTextureMetaData
        {
            public CAPI.ovrTextureLayoutResult LayoutInMorphTargetsTex;
            public uint NumMorphTargetAffectedVerts;
            public int[] MeshVertexToAffectedIndex;
            public CAPI.ovrTextureLayoutResult LayoutInNeutralPoseTex;
            public CAPI.ovrTextureLayoutResult LayoutInJointsTex;
            public Vector3 PositionRange;
            public Vector3 NormalRange;
            public Vector3 TangentRange;
        }

        public OvrExpandableTextureArray NeutralPoseTex { get; private set; }
        public OvrExpandableTextureArray MorphTargetSourceTex { get; private set; }
        public OvrExpandableTextureArray JointsTex { get; private set; }
        public SourceTextureMetaData MetaData { get; private set; }

        private OvrTime.SliceHandle _buildTextureSlice;
        private OvrTime.SliceHandle _buildMorphTextureSlice;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private CAPI.ovrGpuMorphTargetTextureDesc _morphTexDesc;
        private NativeArray<Int32> _mVtoAvBuffer;


        public OvrAvatarGpuSkinnedPrimitive(string name,
            uint vertexCount, in NativeArray<Vector3> neutralPositions, in NativeArray<Vector3> neutralNormals, in NativeArray<Vector4> neutralTangents,
            uint morphTargetCount, in NativeArray<IntPtr> deltaPosPtr, in NativeArray<IntPtr> deltaNormPtr, in NativeArray<IntPtr> deltaTanPtr,
            uint jointsCount, BoneWeight[] boneWeights,
            // TODO: adjust scoping to give direct access to MeshInfo
            Action neutralPoseCallback, Action finishCallback)
        {
            var gpuSkinningConfig = GpuSkinningConfiguration.Instance;

            _buildTextureSlice = OvrTime.Slice(
                BuildTextures(gpuSkinningConfig, name, vertexCount, neutralPositions, neutralNormals, neutralTangents,
                morphTargetCount, deltaPosPtr, deltaNormPtr, deltaTanPtr,
                jointsCount, boneWeights,
                neutralPoseCallback, finishCallback)
            );
        }

        private IEnumerator<OvrTime.SliceStep> BuildTextures(GpuSkinningConfiguration gpuSkinningConfig, string name,
            uint vertexCount, NativeArray<Vector3> neutralPositions, NativeArray<Vector3> neutralNormals, NativeArray<Vector4> neutralTangents,
            uint morphTargetCount, NativeArray<IntPtr> deltaPosPtr, NativeArray<IntPtr> deltaNormPtr, NativeArray<IntPtr> deltaTanPtr,
            uint jointsCount, BoneWeight[] boneWeights,
            // TODO: adjust scoping to give direct access to MeshInfo
            Action neutralPoseCallback, Action finishCallback)
        {
            // TODO: Some of this work can be moved off the main thread (everything except creating Unity.Objects)
            var result = new SourceTextureMetaData();

            yield return OvrTime.SliceStep.Stall;
            Profiler.BeginSample("OvrAvatarGPUSkinnedPrimitive.CreateNeutralPoseTex");
            NeutralPoseTex = CreateNeutralPoseTex(name, vertexCount, in neutralPositions, in neutralNormals, in neutralTangents,
                gpuSkinningConfig.NeutralPoseFormat, ref result);
            Profiler.EndSample();
            neutralPoseCallback?.Invoke();
            MetaData = result;

            if (morphTargetCount > 0)
            {
                yield return OvrTime.SliceStep.Stall;
                Profiler.BeginSample("OvrAvatarGPUSkinnedPrimitive.CreateMorphTargetSourceTexSlice");

                try
                {
                    // CreateMorphTargetSourceTex migrated to time slicer to allow async execution and fewer hitches
                    _buildMorphTextureSlice = OvrTime.Slice(
                        CreateMorphTargetSourceTex(
                            name, vertexCount, morphTargetCount,
                            deltaPosPtr, deltaNormPtr, deltaTanPtr,
                            gpuSkinningConfig.SourceMorphFormat));

                    Profiler.EndSample(); // "OvrAvatarGPUSkinnedPrimitive.CreateMorphTargetSourceTexSlice"

                    do
                    {
                        // Delay until `CreateMorphTargetSourceTex` slice to completes
                        yield return OvrTime.SliceStep.Delay;
                    } while (_buildMorphTextureSlice.IsSlicing);
                }
                finally
                {
                    _buildMorphTextureSlice.Clear();
                }
            }

            if (jointsCount > 0)
            {
                yield return OvrTime.SliceStep.Stall;
                Profiler.BeginSample("OvrAvatarGPUSkinnedPrimitive.CreateJointsTex");
                JointsTex = CreateJointsTex(name, vertexCount, boneWeights, gpuSkinningConfig.JointsFormat, ref result);
                Profiler.EndSample(); // "OvrAvatarGPUSkinnedPrimitive.CreateJointsTex"
            }

            finishCallback?.Invoke();

            _buildTextureSlice.Clear();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool isDispose)
        {
            if (isDispose)
            {
                if (_buildTextureSlice.IsValid)
                {
                    _buildTextureSlice.Cancel();
                }
                if (_buildMorphTextureSlice.IsValid)
                {
                    _buildMorphTextureSlice.Cancel();
                }
                _cancellationTokenSource.Cancel();

                JointsTex?.Destroy();
                MorphTargetSourceTex?.Destroy();
                NeutralPoseTex?.Destroy();
            }
            else
            {
                if (_buildTextureSlice.IsValid)
                {
                    OvrAvatarLog.LogError("Build texture slice still valid when finalized", LOG_SCOPE);

                    // Prevent OvrTime from stalling
                    _buildTextureSlice.EmergencyShutdown();
                }
                if (_buildMorphTextureSlice.IsValid)
                {
                    _buildMorphTextureSlice.EmergencyShutdown();
                }
                if (NeutralPoseTex != null || MorphTargetSourceTex != null || JointsTex != null)
                {
                    OvrAvatarLog.LogError($"OvrAvatarGPUSkinnedPrimitive was not disposed before being destroyed", LOG_SCOPE);
                }
            }

            _mVtoAvBuffer.Reset();
            JointsTex = null;
            MorphTargetSourceTex = null;
            NeutralPoseTex = null;
        }

        ~OvrAvatarGpuSkinnedPrimitive()
        {
            Dispose(false);
        }

        private static OvrExpandableTextureArray CreateNeutralPoseTex(string name, uint vertexCount,
            in NativeArray<Vector3> positions, in NativeArray<Vector3> normals, in NativeArray<Vector4> tangents,
            GraphicsFormat neutralTexFormat, ref SourceTextureMetaData metaData)
        {
            var hasTangents = tangents.IsCreated && tangents.Length > 0;

            CAPI.ovrGpuSkinningTextureDesc texDesc = CAPI.OvrGpuSkinning_NeutralPoseTextureDesc(
                OvrGpuSkinningUtils.MAX_TEXTURE_DIMENSION,
                vertexCount,
                hasTangents);

            // Create expandable texture array and fill with data
            var output = new OvrExpandableTextureArray(
                $"neutral({name})",
                texDesc.width,
                texDesc.height,
                neutralTexFormat);

            OvrSkinningTypes.Handle handle = output.AddEmptyBlock(texDesc.width, texDesc.height);
            CAPI.ovrTextureLayoutResult layout = output.GetLayout(handle);

            {
                Texture2D tempTex = new Texture2D(
                    layout.w,
                    layout.h,
                    output.Format,
                    output.HasMips,
                    output.IsLinear);

                var texData = tempTex.GetRawTextureData<byte>();

                // This will validate the sizes match
                Debug.Assert(texData.Length == texDesc.dataSize);

                if (CAPI.OvrGpuSkinning_NeutralPoseEncodeTextureDataWithUnityTypes(in texDesc, vertexCount, in positions
                        , in normals, in tangents, ref texData))
                {
                    tempTex.Apply(false, true);

                    output.CopyFromTexture(layout, tempTex);
                }
                else
                {
                    OvrAvatarLog.LogError("Unable to create NeutralPose texture", LOG_SCOPE);
                }

                Texture2D.Destroy(tempTex);
            }

            metaData.LayoutInNeutralPoseTex = layout;

            return output;
        }

        // This whole function is modified to allow the CAPI calls to run as a Task on another thread.
        // Prevents significant loading hitches.
        private IEnumerator<OvrTime.SliceStep> CreateMorphTargetSourceTex(string name,
            uint vertexCount, uint morphTargetCount, NativeArray<IntPtr> deltaPosPtrs, NativeArray<IntPtr> deltaNormPtrs, NativeArray<IntPtr> deltaTanPtrs,
            GpuSkinningConfiguration.TexturePrecision morphSrcPrecision)
        {
            Debug.Assert(!deltaPosPtrs.IsNull());
            Debug.Assert(!deltaNormPtrs.IsNull());

            bool hasTangents = deltaTanPtrs.IsCreated && deltaTanPtrs.Length > 0;

            _mVtoAvBuffer = new NativeArray<Int32>((int) vertexCount, Allocator.Persistent, NATIVE_ARRAY_INIT);
            if (_mVtoAvBuffer.IsNull())
            {
                OvrAvatarLog.LogAllocationFailure("CreateMorphTargetSourceTex", LOG_SCOPE);
                yield return OvrTime.SliceStep.Cancel;
            }

            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
            Task encodeMeshVertTask;
            if (hasTangents) {
                encodeMeshVertTask = Task.Run(
                    () => {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        _morphTexDesc = CAPI.OvrGpuSkinning_MorphTargetEncodeMeshVertToAffectedVertWithTangents(
                            OvrGpuSkinningUtils.MAX_TEXTURE_DIMENSION,
                            vertexCount,
                            morphTargetCount,
                            morphSrcPrecision.GetOvrPrecision(),
                            in deltaPosPtrs,
                            in deltaNormPtrs,
                            in deltaTanPtrs,
                            in _mVtoAvBuffer);
                    }
                );
            }
            else {
                encodeMeshVertTask = Task.Run(
                    () => {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        _morphTexDesc = CAPI.OvrGpuSkinning_MorphTargetEncodeMeshVertToAffectedVert(
                            OvrGpuSkinningUtils.MAX_TEXTURE_DIMENSION,
                            vertexCount,
                            morphTargetCount,
                            morphSrcPrecision.GetOvrPrecision(),
                            in deltaPosPtrs,
                            in deltaNormPtrs,
                            in _mVtoAvBuffer
                      );
                    }
                );
            }

            while (!encodeMeshVertTask.IsCompleted) {
                yield return OvrTime.SliceStep.Delay;
            }

            MetaData.NumMorphTargetAffectedVerts = _morphTexDesc.numAffectedVerts;

            if (MetaData.NumMorphTargetAffectedVerts <= 0)
            {
                // TODO: Could we catch this much, much earlier?
                //HasMorphTargets = false;
                MetaData.MeshVertexToAffectedIndex = Array.Empty<int>();
                MetaData.LayoutInMorphTargetsTex = CAPI.ovrTextureLayoutResult.INVALID_LAYOUT;

                OvrAvatarLog.LogDebug($"Primitive ({name}) has morph target, but no affected verts", LOG_SCOPE);
                yield return OvrTime.SliceStep.Cancel;
            }

            var morphTargetTexels = _morphTexDesc.texWidth * _morphTexDesc.texHeight;
            Debug.Assert(morphTargetTexels > 0);

            // Create expandable texture array and fill with data
            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
            OvrExpandableTextureArray output = new OvrExpandableTextureArray(
                $"morphSrc({name})",
                 _morphTexDesc.texWidth,
                 _morphTexDesc.texHeight,
                morphSrcPrecision.GetGraphicsFormat());

            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
            OvrSkinningTypes.Handle handle = output.AddEmptyBlock(
                 _morphTexDesc.texWidth,
                 _morphTexDesc.texHeight);
            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
            CAPI.ovrTextureLayoutResult layout = output.GetLayout(handle);

            Texture2D tempTex = new Texture2D(
                layout.w,
                layout.h,
                output.Format,
                output.HasMips,
                output.IsLinear);

            var texData = tempTex.GetRawTextureData<byte>();

            // This will validate the sizes match
            Debug.Assert(texData.Length == _morphTexDesc.textureDataSize);

            Task encodeTextureDataTask;
            if (hasTangents)
            {
              encodeTextureDataTask = Task.Run(
                () => {
                  _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                  if (!CAPI.OvrGpuSkinning_MorphTargetEncodeTextureDataWithTangents(_morphTexDesc, in _mVtoAvBuffer
                          , morphSrcPrecision.GetOvrPrecision(), in deltaPosPtrs
                          , in deltaNormPtrs, in deltaTanPtrs, in texData))
                  {
                    OvrAvatarLog.LogError("failed to get morph data", LOG_SCOPE);
                  }
                });
            }
            else
            {
                encodeTextureDataTask = Task.Run(
                () => {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    if (!CAPI.OvrGpuSkinning_MorphTargetEncodeTextureData(_morphTexDesc, in _mVtoAvBuffer, morphSrcPrecision.GetOvrPrecision(), in deltaPosPtrs, in deltaNormPtrs, in texData))
                    {
                      OvrAvatarLog.LogError("failed to get morph data", LOG_SCOPE);
                    }
                });
            }

            while (!encodeTextureDataTask.IsCompleted) {
                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    yield return OvrTime.SliceStep.Cancel;
                }
                yield return OvrTime.SliceStep.Delay;
            }

            tempTex.Apply(false, true);

            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
            output.CopyFromTexture(layout, tempTex);

            Texture2D.Destroy(tempTex);

            MetaData.LayoutInMorphTargetsTex = layout;

            MetaData.MeshVertexToAffectedIndex = _mVtoAvBuffer.ToArray();
            _mVtoAvBuffer.Reset();

            MetaData.PositionRange = _morphTexDesc.positionRange;
            MetaData.NormalRange = _morphTexDesc.normalRange;
            MetaData.TangentRange = _morphTexDesc.tangentRange;

            MorphTargetSourceTex = output;
        }

        private static OvrExpandableTextureArray CreateJointsTex(string name, uint vertexCount, BoneWeight[] boneWeights,
            GraphicsFormat jointsTexFormat, ref SourceTextureMetaData metaData)
        {
            // TODO: get these two arrays directly! See RetrieveBoneWeights()
            var jointIndices = new NativeArray<CAPI.ovrAvatar2Vector4us>((int)vertexCount, Allocator.Persistent, NATIVE_ARRAY_INIT);
            var jointWeights = new NativeArray<CAPI.ovrAvatar2Vector4f>((int)vertexCount, Allocator.Persistent, NATIVE_ARRAY_INIT);
            if (jointIndices.IsNull() || jointWeights.IsNull())
            {
                if (!jointIndices.IsNull()) { jointIndices.Dispose(); }
                if (!jointWeights.IsNull()) { jointWeights.Dispose(); }
                OvrAvatarLog.LogAllocationFailure("CreateJointsTex", LOG_SCOPE);
                return null;
            }
            try
            {
                unsafe
                {
                    var jointIndicesPtr = jointIndices.GetPtr();
                    var jointWeightsPtr = jointWeights.GetPtr();

                    for (int i = 0; i < boneWeights.Length; i++)
                    {
                        ref readonly BoneWeight bw = ref boneWeights[i];
                        jointIndicesPtr[i] = new CAPI.ovrAvatar2Vector4us
                        {
                            x = (ushort)bw.boneIndex0, y = (ushort)bw.boneIndex1, z = (ushort)bw.boneIndex2
                            , w = (ushort)bw.boneIndex3,
                        };

                        jointWeightsPtr[i] = new CAPI.ovrAvatar2Vector4f
                        {
                            x = bw.weight0, y = bw.weight1, z = bw.weight2, w = bw.weight3,
                        };
                    }
                }

                var texDesc = CAPI.OvrGpuSkinning_JointTextureDesc(
                    OvrGpuSkinningUtils.MAX_TEXTURE_DIMENSION, vertexCount);

                var output = new OvrExpandableTextureArray(
                    "joints(" + name + ")",
                    texDesc.width,
                    texDesc.height,
                    jointsTexFormat);

                OvrSkinningTypes.Handle handle = output.AddEmptyBlock(texDesc.width, texDesc.height);
                CAPI.ovrTextureLayoutResult layout = output.GetLayout(handle);

                OvrAvatarLog.AssertConstMessage(layout.IsValid, "invalid texture layout detected", LOG_SCOPE);
                {
                    var tempTex = new Texture2D(
                        layout.w,
                        layout.h,
                        output.Format,
                        output.HasMips,
                        output.IsLinear);

                    var texData = tempTex.GetRawTextureData<byte>();

                    Debug.Assert(texData.Length == texDesc.dataSize);

                    IntPtr dataPtr;
                    unsafe { dataPtr = (IntPtr)texData.GetUnsafePtr(); }

                    bool didEncode = CAPI.OvrGpuSkinning_JointEncodeTextureData(
                        in texDesc,
                        vertexCount,
                        in jointIndices,
                        in jointWeights,
                        dataPtr,
                        texData.GetBufferSize());

                    // Free these `NativeArray`s as soon as we're done with them, to alleviate memory pressure for texture ops
                    jointIndices.Reset();
                    jointWeights.Reset();

                    OvrAvatarLog.AssertConstMessage(didEncode, "get skinning data failure", LOG_SCOPE);

                    tempTex.Apply(false, true);

                    output.CopyFromTexture(layout, tempTex);

                    Texture2D.Destroy(tempTex);

                    metaData.LayoutInJointsTex = layout;
                }

                return output;
            }
            finally
            {
                jointIndices.Reset();
                jointWeights.Reset();
            }
        }
    }
}
