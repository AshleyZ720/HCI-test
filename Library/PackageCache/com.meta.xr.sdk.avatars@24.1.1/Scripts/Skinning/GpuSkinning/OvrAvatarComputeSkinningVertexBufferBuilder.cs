// Due to Unity bug (fixed in version 2021.2), copy to a native array then copy native array to ComputeBuffer in one chunk
// (ComputeBuffer.SetData erases previously set data)
// https://issuetracker.unity3d.com/issues/partial-updates-of-computebuffer-slash-graphicsbuffer-using-setdata-dont-preserve-existing-data-when-using-opengl-es

#if UNITY_2021_2_OR_NEWER
#define COMPUTE_BUFFER_PARTIAL_UPDATE_ALLOWED
#endif

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Oculus.Avatar2;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;
using UnityEngine.Profiling;

namespace Oculus.Skinning.GpuSkinning
{
    // A load request
    internal class OvrAvatarComputeSkinningVertexBufferBuilder : IDisposable
    {
        private const string LOG_SCOPE = nameof(OvrAvatarComputeSkinningVertexBufferBuilder);

        public OvrAvatarComputeSkinningVertexBufferBuilder(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            Action failureCallback,
            Action compactSkinningDataLoadedCallback,
            Func<OvrAvatarComputeSkinningVertexBuffer, IEnumerator<OvrTime.SliceStep>> finishCallback)
        {
            Debug.Assert(id != CAPI.ovrAvatar2CompactSkinningDataId.Invalid);

            _buildSlice = OvrTime.Slice(
                BuildVertexBuffer(id, failureCallback, compactSkinningDataLoadedCallback, finishCallback));
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
                if (_buildSlice.IsValid)
                {
                    _buildSlice.Cancel();
                }
            }
            else
            {
                if (_buildSlice.IsValid)
                {
                    OvrAvatarLog.LogError(
                        "OvrAvatarComputeSkinningVertexBufferBuilder slice still valid when finalized",
                        LOG_SCOPE);

                    // Prevent OvrTime from stalling
                    _buildSlice.EmergencyShutdown();
                }
            }
        }

        ~OvrAvatarComputeSkinningVertexBufferBuilder()
        {
            Dispose(false);
        }

        private IEnumerator<OvrTime.SliceStep> BuildVertexBuffer(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            Action failureCallback,
            Action compactSkinningDataLoadedCallback,
            Func<OvrAvatarComputeSkinningVertexBuffer, IEnumerator<OvrTime.SliceStep>> finishCallback)
        {
            // TODO: Some of this work can be moved off the main thread (everything except creating Unity.Objects)
            if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

            NativeArray<byte> neutralPos = default;
            NativeArray<byte> neutralNorm = default;
            NativeArray<byte> neutralTan = default;
            NativeArray<byte> jointWeights = default;
            NativeArray<byte> jointIndices = default;
            NativeArray<byte> morphPos = default;
            NativeArray<byte> morphNorm = default;
            NativeArray<byte> morphTan = default;
            NativeArray<byte> morphIndices = default;
            NativeArray<byte> nextEntries = default;
            NativeArray<byte> numMorphsBuffer = default;

            VertexBufferMetaData vertexBufferMetaData = new VertexBufferMetaData();

            OvrAvatarComputeSkinningVertexBuffer.DataFormatsAndStrides dataFormats =
                new OvrAvatarComputeSkinningVertexBuffer.DataFormatsAndStrides();

            CAPI.OvrAvatar2CompactSkinningMetaData apiMetaData = default;

            try
            {
                string operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.InitializeCompactSkinning";

                Profiler.BeginSample(operationName);
                bool success = InitializeCompactSkinning(id, operationName);
                Profiler.EndSample();

                // Header comes first
                uint currentOffset = (uint)UnsafeUtility.SizeOf<VertexBufferMetaData>();

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetPositions";

                    Profiler.BeginSample(operationName);
                    success = GetPositions(
                        id,
                        operationName,
                        out neutralPos,
                        out var dataFormatAndStride,
                        out var posOffset,
                        out var posScale);

                    vertexBufferMetaData.positionsOffsetBytes = currentOffset;
                    vertexBufferMetaData.vertexInputPositionBias = posOffset;
                    vertexBufferMetaData.vertexInputPositionScale = posScale;

                    dataFormats.vertexPositions = dataFormatAndStride;

                    currentOffset += neutralPos.GetBufferSize();
                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetNormals";
                    Profiler.BeginSample(operationName);

                    success = GetNormals(id, operationName, out neutralNorm, out _);

                    vertexBufferMetaData.normalsOffsetBytes = currentOffset;
                    currentOffset += neutralNorm.GetBufferSize();

                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetTangents";
                    Profiler.BeginSample(operationName);

                    success = GetTangents(id, operationName, out neutralTan, out _);

                    vertexBufferMetaData.tangentsOffsetBytes = currentOffset;
                    currentOffset += neutralTan.GetBufferSize();

                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetJointWeights";
                    Profiler.BeginSample(operationName);

                    success = GetJointWeights(id, operationName, out jointWeights, out var dataFormatAndStride);

                    vertexBufferMetaData.jointWeightsOffsetBytes = currentOffset;
                    dataFormats.jointWeights = dataFormatAndStride;
                    currentOffset += jointWeights.GetBufferSize();

                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetJointIndices";
                    Profiler.BeginSample(operationName);

                    success = GetJointIndices(id, operationName, out jointIndices, out var dataFormatAndStride);

                    vertexBufferMetaData.jointIndicesOffsetBytes = currentOffset;
                    dataFormats.jointIndices = dataFormatAndStride;
                    currentOffset += jointIndices.GetBufferSize();

                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetMorphPosDeltas";

                    Profiler.BeginSample(operationName);
                    success = GetMorphPosDeltas(id, operationName, out morphPos, out var morphRange);

                    vertexBufferMetaData.morphTargetPosDeltasOffsetBytes = currentOffset;
                    vertexBufferMetaData.morphTargetsPosRange = morphRange;
                    currentOffset += morphPos.GetBufferSize();
                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetMorphNormDeltas";
                    Profiler.BeginSample(operationName);

                    success = GetMorphNormDeltas(id, operationName, out morphNorm, out var morphRange);

                    vertexBufferMetaData.morphTargetNormDeltasOffsetBytes = currentOffset;
                    vertexBufferMetaData.morphTargetsNormRange = morphRange;
                    currentOffset += morphNorm.GetBufferSize();

                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetMorphTanDeltas";
                    Profiler.BeginSample(operationName);

                    success = GetMorphTanDeltas(id, operationName, out morphTan, out var morphRange);

                    vertexBufferMetaData.morphTargetTanDeltasOffsetBytes = currentOffset;
                    vertexBufferMetaData.morphTargetsTanRange = morphRange;
                    currentOffset += morphTan.GetBufferSize();

                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetMorphIndices";
                    Profiler.BeginSample(operationName);

                    success = GetMorphIndices(id, operationName, out morphIndices, out var dataFormatAndStride);

                    vertexBufferMetaData.morphTargetIndicesOffsetBytes = currentOffset;
                    dataFormats.morphIndices = dataFormatAndStride;
                    currentOffset += morphIndices.GetBufferSize();

                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetMorphNextEntries";
                    Profiler.BeginSample(operationName);

                    success = GetMorphNextEntries(id, operationName, out nextEntries, out var dataFormatAndStride);

                    vertexBufferMetaData.morphTargetNextEntriesOffsetBytes = currentOffset;
                    dataFormats.nextEntryIndices = dataFormatAndStride;
                    currentOffset += nextEntries.GetBufferSize();

                    Profiler.EndSample();
                }

                // Grab "num morphs buffer"
                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetNumMorphsBuffer";

                    Profiler.BeginSample(operationName);
                    success = GetNumMorphsBuffer(id, operationName, out numMorphsBuffer);

                    vertexBufferMetaData.numMorphsBufferOffsetBytes = currentOffset;
                    currentOffset += numMorphsBuffer.GetBufferSize();
                    Profiler.EndSample();
                }

                // Grab "inverse reorder buffer"
                NativeArray<byte> inverseReorderBuffer = default;
                OvrComputeUtils.DataFormatAndStride vertexIndexDataFormatAndStride = default;
                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetVertIndexToCompactSkinningIndex";

                    Profiler.BeginSample(operationName);
                    success = GetInverseReorderBuffer(
                        id,
                        operationName,
                        out inverseReorderBuffer,
                        out vertexIndexDataFormatAndStride);
                    Profiler.EndSample();
                }

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.GetMetaData";
                    Profiler.BeginSample(operationName);

                    apiMetaData = CAPI.OvrCompactSkinningData_GetMetaData(id);

                    vertexBufferMetaData.numMorphedVerts = apiMetaData.numMorphedVerts;
                    vertexBufferMetaData.numSkinningOnlyVerts = apiMetaData.numJointsOnlyVerts;

                    Profiler.EndSample();
                }

                compactSkinningDataLoadedCallback?.Invoke();

                if (success)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }

                    var newBuffer = CreateVertexBuffer(
                        $"ComputeSkinningVertexBuffer_{id}",
                        neutralPos,
                        neutralNorm,
                        neutralTan,
                        jointWeights,
                        jointIndices,
                        morphPos,
                        morphNorm,
                        morphTan,
                        morphIndices,
                        nextEntries,
                        inverseReorderBuffer,
                        numMorphsBuffer,
                        vertexIndexDataFormatAndStride,
                        vertexBufferMetaData,
                        dataFormats,
                        apiMetaData,
                        currentOffset);

                    // Slice the finish callback if needed
                    IEnumerator<OvrTime.SliceStep> finishCallbackSlice = finishCallback(newBuffer);
                    OvrTime.SliceStep step;
                    do
                    {
                        operationName = "OvrAvatarComputeSkinningVertexBufferBuilder.finishCallbackSlice";
                        Profiler.BeginSample(operationName);
                        step = finishCallbackSlice.MoveNext() ? finishCallbackSlice.Current : OvrTime.SliceStep.Cancel;
                        Profiler.EndSample();
                        if (step != OvrTime.SliceStep.Cancel)
                        {
                            yield return step;
                        }
                    } while (step != OvrTime.SliceStep.Cancel);
                }
                else
                {
                    inverseReorderBuffer.Reset();
                    failureCallback?.Invoke();
                }
            }
            finally
            {
                neutralPos.Reset();
                neutralNorm.Reset();
                neutralTan.Reset();

                jointWeights.Reset();
                jointIndices.Reset();

                morphPos.Reset();
                morphNorm.Reset();
                morphTan.Reset();

                morphIndices.Reset();
                nextEntries.Reset();
                numMorphsBuffer.Reset();

                // Mark loading as finished
                _buildSlice.Clear();
            }
        }

        private static OvrAvatarComputeSkinningVertexBuffer CreateVertexBuffer(
            string name,
            in NativeArray<byte> neutralPos,
            in NativeArray<byte> neutralNorm,
            in NativeArray<byte> neutralTan,
            in NativeArray<byte> jointWeights,
            in NativeArray<byte> jointIndices,
            in NativeArray<byte> morphPos,
            in NativeArray<byte> morphNorm,
            in NativeArray<byte> morphTan,
            in NativeArray<byte> morphIndices,
            in NativeArray<byte> nextEntries,
            in NativeArray<byte> inverseReorderBuffer,
            in NativeArray<byte> numMorphsBuffer,
            in OvrComputeUtils.DataFormatAndStride vertexIndexFormat,
            in VertexBufferMetaData vertexBufferMetaData,
            in OvrAvatarComputeSkinningVertexBuffer.DataFormatsAndStrides dataFormats,
            in CAPI.OvrAvatar2CompactSkinningMetaData apiMetaData,
            uint totalBufferSize)
        {
            var computeBuffer = OvrComputeUtils.CreateRawComputeBuffer(totalBufferSize);
            computeBuffer.name = name;

#if COMPUTE_BUFFER_PARTIAL_UPDATE_ALLOWED
                // Header is always first
                computeBuffer.SetData(new[] { vertexBufferMetaData }, 0, 0, 1);

                // Neutral pose
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    neutralPos,
                    vertexBufferMetaData.positionsOffsetBytes);
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    neutralNorm,
                    vertexBufferMetaData.normalsOffsetBytes);
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    neutralTan,
                    vertexBufferMetaData.tangentsOffsetBytes);

                // joints
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    jointWeights,
                    vertexBufferMetaData.jointWeightsOffsetBytes);
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    jointIndices,
                    vertexBufferMetaData.jointIndicesOffsetBytes);

                // Morph deltas
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    morphPos,
                    vertexBufferMetaData.morphTargetPosDeltasOffsetBytes);
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    morphNorm,
                    vertexBufferMetaData.morphTargetNormDeltasOffsetBytes);
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    morphTan,
                    vertexBufferMetaData.morphTargetTanDeltasOffsetBytes);

                // Morph indices + next entries
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    morphIndices,
                    vertexBufferMetaData.morphTargetIndicesOffsetBytes);
                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    nextEntries,
                    vertexBufferMetaData.morphTargetNextEntriesOffsetBytes);

                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(
                    computeBuffer,
                    numMorphsBuffer,
                    vertexBufferMetaData.numMorphsBufferOffsetBytes);
#else
            using (var backingBuffer = new NativeArray<byte>(
                (int)totalBufferSize,
                Allocator.Temp,
                NativeArrayOptions.UninitializedMemory))
            {
                // Header is always first
                backingBuffer.ReinterpretStore(0, vertexBufferMetaData);

                // Neutral pose
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    neutralPos,
                    vertexBufferMetaData.positionsOffsetBytes);
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    neutralNorm,
                    vertexBufferMetaData.normalsOffsetBytes);
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    neutralTan,
                    vertexBufferMetaData.tangentsOffsetBytes);

                // joints
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    jointWeights,
                    vertexBufferMetaData.jointWeightsOffsetBytes);
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    jointIndices,
                    vertexBufferMetaData.jointIndicesOffsetBytes);

                // Morph deltas
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    morphPos,
                    vertexBufferMetaData.morphTargetPosDeltasOffsetBytes);
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    morphNorm,
                    vertexBufferMetaData.morphTargetNormDeltasOffsetBytes);
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    morphTan,
                    vertexBufferMetaData.morphTargetTanDeltasOffsetBytes);

                // Morph indices + next entries
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    morphIndices,
                    vertexBufferMetaData.morphTargetIndicesOffsetBytes);
                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    nextEntries,
                    vertexBufferMetaData.morphTargetNextEntriesOffsetBytes);

                OvrComputeUtils.CopyNativeArrayToNativeByteArray(
                    backingBuffer,
                    numMorphsBuffer,
                    vertexBufferMetaData.numMorphsBufferOffsetBytes);

                OvrComputeUtils.SetRawComputeBufferDataFromNativeArray(computeBuffer, backingBuffer, 0);
            }
#endif

            return new OvrAvatarComputeSkinningVertexBuffer(
                computeBuffer,
                inverseReorderBuffer,
                vertexIndexFormat,
                dataFormats,
                (int)vertexBufferMetaData.numMorphedVerts,
                apiMetaData.isInClientCoordSpace != 0,
                (Matrix4x4)apiMetaData.clientCoordSpaceTransform);
        }

        private static bool InitializeCompactSkinning(CAPI.ovrAvatar2CompactSkinningDataId id, string operationName)
        {
            try
            {
                if (!CAPI.OvrCompactSkinningData_Initialize(id))
                {
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetPositions(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> positions,
            out OvrComputeUtils.DataFormatAndStride dataFormatAndStride,
            out CAPI.ovrAvatar2Vector3f normalizationOffset,
            out CAPI.ovrAvatar2Vector3f normalizationScale)
        {
            positions = default;
            normalizationOffset = new CAPI.ovrAvatar2Vector3f();
            normalizationScale = new CAPI.ovrAvatar2Vector3f(1.0f, 1.0f, 1.0f);

            dataFormatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetPositionsMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogError(
                        $"Could not find CompactSkinningData positions meta data for id {id}",
                        LOG_SCOPE);
                    // Positions are required
                    return false;
                }

                // Positions are expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                positions = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                dataFormatAndStride = new OvrComputeUtils.DataFormatAndStride(bufferMetaData.dataFormat, (int)stride);

                return CAPI.OvrCompactSkinningData_CopyPositions(
                    id,
                    positions,
                    stride,
                    out normalizationOffset,
                    out normalizationScale).EnsureSuccess(
                    $"Could not find CompactSkinningData positions for id {id}",
                    LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetNormals(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> normals,
            out OvrComputeUtils.DataFormatAndStride dataFormatAndStride)
        {
            normals = default;
            dataFormatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetNormalsMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogError(
                        $"Could not find CompactSkinningData normals meta data for id {id}",
                        LOG_SCOPE);
                    // Normals are required
                    return false;
                }

                // Normals are expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                normals = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                dataFormatAndStride = new OvrComputeUtils.DataFormatAndStride(bufferMetaData.dataFormat, (int)stride);

                return CAPI.OvrCompactSkinningData_CopyNormals(id, normals, stride, out _, out _).EnsureSuccess(
                    $"Could not find CompactSkinningData normals for id {id}",
                    LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetTangents(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> tangents,
            out OvrComputeUtils.DataFormatAndStride dataFormatAndStride)
        {
            tangents = default;
            dataFormatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetTangentsMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData tangents meta data for id {id}",
                        LOG_SCOPE);

                    // Tangents are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // Tangents are expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                tangents = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                dataFormatAndStride = new OvrComputeUtils.DataFormatAndStride(bufferMetaData.dataFormat, (int)stride);

                return CAPI.OvrCompactSkinningData_CopyTangents(id, tangents, stride, out _, out _).EnsureSuccess(
                    $"Could not find CompactSkinningData tangents for id {id}",
                    LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetJointWeights(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> jointWeights,
            out OvrComputeUtils.DataFormatAndStride dataFormatAndStride)
        {
            jointWeights = default;
            dataFormatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetJointWeightsMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData joint weights meta data for id {id}",
                        LOG_SCOPE);

                    // Joint weights are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // Joint weights are expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                jointWeights = new NativeArray<byte>(
                    bufferSize,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                dataFormatAndStride = new OvrComputeUtils.DataFormatAndStride(bufferMetaData.dataFormat, (int)stride);

                return CAPI.OvrCompactSkinningData_CopyJointWeights(id, jointWeights, stride).EnsureSuccess(
                    $"Could not find CompactSkinningData joint weights for id {id}",
                    LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetJointIndices(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> jointIndices,
            out OvrComputeUtils.DataFormatAndStride dataFormatAndStride)
        {
            jointIndices = default;
            dataFormatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetJointIndicesMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData joint indices meta data for id {id}",
                        LOG_SCOPE);

                    // Joint indices are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // Joint weights are expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                jointIndices = new NativeArray<byte>(
                    bufferSize,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                dataFormatAndStride = new OvrComputeUtils.DataFormatAndStride(bufferMetaData.dataFormat, (int)stride);

                return CAPI.OvrCompactSkinningData_CopyJointIndices(id, jointIndices, stride).EnsureSuccess(
                    $"Could not find CompactSkinningData joint indices for id {id}",
                    LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetMorphPosDeltas(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> deltas,
            out CAPI.ovrAvatar2Vector3f normalizationScale)
        {
            deltas = default;
            normalizationScale = Vector3.one;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetMorphPositionDeltasMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData morph position deltas meta data for id {id}",
                        LOG_SCOPE);

                    // Morphs are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // Expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                deltas = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                return CAPI
                    .OvrCompactSkinningData_CopyMorphPositionDeltas(id, deltas, stride, out _, out normalizationScale)
                    .EnsureSuccess(
                        $"Could not find CompactSkinningData morph target position deltas for id {id}",
                        LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetMorphNormDeltas(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> deltas,
            out CAPI.ovrAvatar2Vector3f normalizationScale)
        {
            deltas = default;
            normalizationScale = Vector3.one;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetMorphNormalDeltasMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData morph normal deltas meta data for id {id}",
                        LOG_SCOPE);

                    // Morphs are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // Expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                deltas = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                return CAPI
                    .OvrCompactSkinningData_CopyMorphNormalDeltas(id, deltas, stride, out _, out normalizationScale)
                    .EnsureSuccess(
                        $"Could not find CompactSkinningData morph target normal deltas for id {id}",
                        LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetMorphTanDeltas(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> deltas,
            out CAPI.ovrAvatar2Vector3f normalizationScale)
        {
            deltas = default;
            normalizationScale = Vector3.one;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetMorphTangentDeltasMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData morph tangent deltas meta data for id {id}",
                        LOG_SCOPE);

                    // Morphs are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // Expected to start on uint (4 byte) boundaries. Force a stride
                // that is a multiple of that.
                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                deltas = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                return CAPI
                    .OvrCompactSkinningData_CopyMorphTangentDeltas(id, deltas, stride, out _, out normalizationScale)
                    .EnsureSuccess(
                        $"Could not find CompactSkinningData morph target tangent deltas for id {id}",
                        LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetMorphIndices(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> indices,
            out OvrComputeUtils.DataFormatAndStride dataFormatAndStride)
        {
            indices = default;
            dataFormatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetMorphIndicesMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData morph target indices meta data for id {id}",
                        LOG_SCOPE);

                    // Morphs are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // The overall size of the buffer needs to be a multiple of a uint (4 bytes)
                var bufferSize = (int)OvrComputeUtils.GetUintAlignedSize(bufferMetaData.dataSizeBytes);
                uint stride = bufferMetaData.strideBytes;

                indices = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                dataFormatAndStride = new OvrComputeUtils.DataFormatAndStride(bufferMetaData.dataFormat, (int)stride);

                return CAPI.OvrCompactSkinningData_CopyMorphIndices(id, indices, stride).EnsureSuccess(
                    $"Could not find CompactSkinningData morph target indices for id {id}",
                    LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetMorphNextEntries(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> nextEntries,
            out OvrComputeUtils.DataFormatAndStride dataFormatAndStride)
        {
            nextEntries = default;
            dataFormatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetMorphNextEntriesMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogVerbose(
                        $"Could not find CompactSkinningData morph target next entries meta data for id {id}",
                        LOG_SCOPE);

                    // Morphs are optional, so just early exit here, but still return true to "keep going"
                    return true;
                }

                // The overall size of the buffer needs to be a multiple of a uint (4 bytes)
                var bufferSize = (int)OvrComputeUtils.GetUintAlignedSize(bufferMetaData.dataSizeBytes);
                uint stride = bufferMetaData.strideBytes;

                nextEntries = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                dataFormatAndStride = new OvrComputeUtils.DataFormatAndStride(bufferMetaData.dataFormat, (int)stride);

                return CAPI.OvrCompactSkinningData_CopyMorphNextEntries(id, nextEntries, stride).EnsureSuccess(
                    $"Could not find CompactSkinningData morph target next entries for id {id}",
                    LOG_SCOPE);
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetInverseReorderBuffer(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> indices,
            out OvrComputeUtils.DataFormatAndStride formatAndStride)
        {
            indices = default;
            formatAndStride = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetVertexInverseReorderMetaData(id);

                if (!IsBufferValid(in bufferMetaData))
                {
                    OvrAvatarLog.LogInfo(
                        $"Could not find CompactSkinningData vertex inverse reorder meta data for id {id}",
                        LOG_SCOPE);

                    return false;
                }

                indices = new NativeArray<byte>(
                    (int)bufferMetaData.dataSizeBytes,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory);

                formatAndStride = new OvrComputeUtils.DataFormatAndStride(
                    bufferMetaData.dataFormat,
                    (int)bufferMetaData.strideBytes);

                return CAPI.OvrCompactSkinningData_CopyVertexInverseReorder(id, indices, bufferMetaData.strideBytes)
                    .EnsureSuccess($"Could not find CompactSkinningData vertex inverse reorder buffer for id {id}");
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool GetNumMorphsBuffer(
            CAPI.ovrAvatar2CompactSkinningDataId id,
            string operationName,
            out NativeArray<byte> numMorphs)
        {
            numMorphs = default;

            try
            {
                var bufferMetaData = CAPI.OvrCompactSkinningData_GetNumMorphsBufferMetaData(id);
                if (!IsBufferValid(in bufferMetaData))
                {
                    // Num morphs buffer is optional (can have no morphs), so return true
                    OvrAvatarLog.LogVerbose(
                        $"Invalid num morphs buffer meta data for compact skinning id {id}",
                        LOG_SCOPE);

                    return true;
                }

                var stride = OvrComputeUtils.GetUintAlignedSize(bufferMetaData.strideBytes);
                var bufferSize = (int)(stride * bufferMetaData.count);

                numMorphs = new NativeArray<byte>(bufferSize, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                return CAPI.OvrCompactSkinningData_CopyNumMorphsBuffer(id, numMorphs, bufferMetaData.strideBytes)
                    .EnsureSuccess($"Could not find num morphs buffer for id {id}");
            }
            catch (Exception e)
            {
                OvrAvatarLog.LogException(operationName, e);
                return false;
            }
        }

        private static bool IsBufferValid(in CAPI.ovrAvatar2BufferMetaData metaData)
        {
            return metaData.count != 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct VertexBufferMetaData
        {
            public uint positionsOffsetBytes;
            public uint normalsOffsetBytes;
            public uint tangentsOffsetBytes;
            public uint jointWeightsOffsetBytes;

            public uint jointIndicesOffsetBytes;
            public uint morphTargetPosDeltasOffsetBytes;
            public uint morphTargetNormDeltasOffsetBytes;
            public uint morphTargetTanDeltasOffsetBytes;

            public uint morphTargetIndicesOffsetBytes;
            public uint morphTargetNextEntriesOffsetBytes;
            public uint numMorphsBufferOffsetBytes;

            public uint numMorphedVerts;
            public uint numSkinningOnlyVerts;

            public CAPI.ovrAvatar2Vector3f vertexInputPositionBias;
            public CAPI.ovrAvatar2Vector3f vertexInputPositionScale;

            public CAPI.ovrAvatar2Vector3f morphTargetsPosRange;
            public CAPI.ovrAvatar2Vector3f morphTargetsNormRange;
            public CAPI.ovrAvatar2Vector3f morphTargetsTanRange;
        };

        #region Properties

        public bool IsLoading => _buildSlice.IsValid;

        #endregion


        #region Fields

        private OvrTime.SliceHandle _buildSlice;

        #endregion
    }
}
