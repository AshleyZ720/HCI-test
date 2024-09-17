using System;
using System.Collections;
using System.Collections.Generic;

using Oculus.Skinning.GpuSkinning;

using Unity.Collections;

using UnityEngine;
using UnityEngine.Profiling;

namespace Oculus.Avatar2
{
    public sealed class OvrAvatarComputeSkinnedPrimitive : IDisposable
    {
        internal OvrAvatarComputeSkinnedPrimitive(
            CAPI.ovrAvatar2CompactSkinningDataId compactSkinningId,
            Func<NativeArray<byte>, CAPI.ovrAvatar2DataFormat, NativeArray<byte>> genVertToCompactIndex,
            Action failureCallback,
            Action compactSkinningDataLoadedCallback,
            Action finishCallback)
        {
            InitStaticMaps();

            _genVertToCompactIndex = genVertToCompactIndex;
            _finishCallback = finishCallback;
            _failureCallback = failureCallback;
            _compactSkinningDataLoadedCallback = compactSkinningDataLoadedCallback;
            _compactSkinningId = compactSkinningId;

            Debug.Assert(compactSkinningId != CAPI.ovrAvatar2CompactSkinningDataId.Invalid);
            Debug.Assert(_genVertToCompactIndex != null);

            // See if vertex buffer already exists for ID
            if (_vertexBufferInfos.TryGetValue(compactSkinningId, out var vbInfo))
            {
                // A "VertexBufferInfo" exists for the ID, but it's possible that
                // the created "vertex buffer" is no longer used and is invalid
                if (vbInfo.TryGetCreatedBuffer(out var buffer))
                {
                    // OvrAvatarComputeSkinningVertexBuffer still exists
                    SetAndRetainVertexBufferAndFinish(buffer, vbInfo);
                    return;
                }
            }
            else
            {
                vbInfo = new VertexBufferInfo();
                _vertexBufferInfos[compactSkinningId] = vbInfo;
            }

            // At this point, a "VertexBufferInfo" exists in the dictionary
            // for the compact skinning ID
            _isWaitingForVertexBuffer = true;
            vbInfo.CreateBuilderIfNeededAndAddPendingLoad(this, compactSkinningId);

            // Wait for the construction
            _buildSlice = OvrTime.Slice(WaitForVertexBufferBuild());
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

                _meshAndCompactSkinningIndices.Reset();

                // Remove this as a pending load of the "VertexBufferInfos" (if it exists)
                if (_vertexBufferInfos.TryGetValue(_compactSkinningId, out var vbInfo))
                {
                    // Release the buffer (does not necessarily dispose of it if
                    // other primitives are using it)
                    if (_vertexBuffer != null)
                    {
                        vbInfo.ReleaseBuffer();
                    }
                    vbInfo.RemovePendingLoad(this);

                    // Check if the "VertexBufferInfo" should be removed from the static
                    // dictionary
                    RemoveVertexBufferInfoFromMappingIfAble(vbInfo);
                }
            }
            else
            {
                if (_buildSlice.IsValid)
                {
                    OvrAvatarLog.LogError("Build buffers slice still valid when finalized", LOG_SCOPE);

                    // Prevent OvrTime from stalling
                    _buildSlice.EmergencyShutdown();
                }
            }

            _genVertToCompactIndex = null;
            _finishCallback = null;
            _failureCallback = null;
            _compactSkinningDataLoadedCallback = null;
        }

        ~OvrAvatarComputeSkinnedPrimitive()
        {
            Dispose(false);
        }

        private IEnumerator<OvrTime.SliceStep> WaitForVertexBufferBuild()
        {
            while (_isWaitingForVertexBuffer)
            {
                // Not sure what to do here
                yield return OvrTime.SliceStep.Delay;
            }

            // Clear out builder if it is still in the vertex buffer info
            DisposeBuilder();
        }

        private static void InitStaticMaps()
        {
            if (_staticMapsInitialized) return;

            _vertexBufferInfos = new Dictionary<CAPI.ovrAvatar2CompactSkinningDataId, VertexBufferInfo>();
            _staticMapsInitialized = true;
        }

        private void SetAndRetainVertexBufferAndFinish(in OvrAvatarComputeSkinningVertexBuffer buffer, in VertexBufferInfo vbInfo)
        {
            _vertexBuffer = buffer;
            vbInfo.RetainBuffer();

            GenerateMeshToCompactSkinningIndices();

            _finishCallback?.Invoke();
            _finishCallback = null;
        }

        private void OnVertexBufferCreated(in OvrAvatarComputeSkinningVertexBuffer buffer, in VertexBufferInfo vbInfo)
        {
            SetAndRetainVertexBufferAndFinish(buffer, vbInfo);

            _isWaitingForVertexBuffer = false;
            _buildSlice.Clear();
        }

        private void OnVertexBufferBuildFailed()
        {
            _isWaitingForVertexBuffer = false;
            _failureCallback?.Invoke();
            _failureCallback = null;
            _buildSlice.Clear();
        }

        private void OnVertexBufferBuildFreeCompactSkinning()
        {
            _isWaitingForVertexBuffer = false;
            _compactSkinningDataLoadedCallback?.Invoke();
            _compactSkinningDataLoadedCallback = null;
        }

        private void DisposeBuilder()
        {
            if (_vertexBufferInfos.TryGetValue(_compactSkinningId, out var vbInfo))
            {
                vbInfo.DisposeBuilder();
            }
        }

        private void RemoveVertexBufferInfoFromMappingIfAble(in VertexBufferInfo vbInfo)
        {
            // Check if the "VertexBufferInfo" should be removed from the static
            // dictionary
            if (!vbInfo.HasPendingLoads && !vbInfo.HasBuffer)
            {
                // No vertex buffer has been made, nothing is waiting for the vertex buffer
                // to be created. Cancel the building of the vertex buffer since it is no longer needed,
                // and remove this "vertex buffer info" from the bookkeeping
                vbInfo.DisposeBuilder();
                _vertexBufferInfos.Remove(_compactSkinningId);
            }
        }

        private void GenerateMeshToCompactSkinningIndices()
        {
            Debug.Assert(_vertexBuffer != null);
            Debug.Assert(_genVertToCompactIndex != null);

            var indexFormat = _vertexBuffer.VertexIndexFormatAndStride.dataFormat;
            using var meshVertToCompactSkinningIndex = _genVertToCompactIndex.Invoke(
                _vertexBuffer.OriginalToCompactSkinningIndex,
                indexFormat);

            // Based on the format, treat the byte array as an array of another type
            switch (indexFormat)
            {
                case CAPI.ovrAvatar2DataFormat.U32:
                    {
                        var reinterpreted = meshVertToCompactSkinningIndex.Reinterpret<UInt32Wrapper>(sizeof(byte));
                        _meshAndCompactSkinningIndices = getCompactSkinningAndMeshIndex(reinterpreted);
                        break;
                    }
                case CAPI.ovrAvatar2DataFormat.U16:
                    {
                        var reinterpreted =
                            meshVertToCompactSkinningIndex.Reinterpret<UInt16Wrapper>(sizeof(byte));
                        _meshAndCompactSkinningIndices = getCompactSkinningAndMeshIndex(reinterpreted);
                        break;
                    }
                case CAPI.ovrAvatar2DataFormat.U8:
                    {
                        var reinterpreted =
                            meshVertToCompactSkinningIndex.Reinterpret<UInt8Wrapper>(sizeof(byte));
                        _meshAndCompactSkinningIndices = getCompactSkinningAndMeshIndex(reinterpreted);
                        break;
                    }
                default:
                    {
                        OvrAvatarLog.LogWarning(
                            $"Unhandled compact skinning vertex index data format {indexFormat}");
                        Debug.Assert(false);

                        // Treat as bytes
                        var reinterpreted =
                            meshVertToCompactSkinningIndex.Reinterpret<UInt8Wrapper>(sizeof(byte));
                        _meshAndCompactSkinningIndices = getCompactSkinningAndMeshIndex(reinterpreted);
                        break;
                    }
            }
        }

        private static NativeArray<VertexIndices> getCompactSkinningAndMeshIndex<T>(
            in NativeArray<T> meshToCompactSkinningIndex) where T : unmanaged, IConvertibleTo<UInt32>
        {
            Profiler.BeginSample("Compute Primitive Sort.");
            var compactSkinningToMesh = new Dictionary<UInt32, UInt32>();
            int minIndex = int.MaxValue;
            int maxIndex = 0;

            // Do a O(n) pass, getting the max and min "compact skinning index"
            // Do this to save space later.

            // Since not every compact skinning index will be used, need a dictionary
            // and not an array
            unsafe
            {
                // Use C# unsafe pointers to avoid NativeArray indexer overhead
                var indicesPtr = meshToCompactSkinningIndex.GetPtr();
                for (int idx = 0; idx < meshToCompactSkinningIndex.Length; idx++)
                {
                    uint compactSkinningIndex = indicesPtr[idx].Convert();
                    minIndex = (int)Mathf.Min(minIndex, compactSkinningIndex);
                    maxIndex = (int)Mathf.Max(maxIndex, compactSkinningIndex);

                    compactSkinningToMesh.Add(compactSkinningIndex, (uint)idx);
                }
            }

            // Now, populate a BitArray to keep track of which compact skinning indices are in use
            // which is another O(n)
            var indicesUsed = new BitArray(maxIndex - minIndex + 1);
            unsafe
            {
                // Use C# unsafe pointers to avoid NativeArray indexer overhead
                var indicesPtr = meshToCompactSkinningIndex.GetPtr();
                for (int idx = 0; idx < meshToCompactSkinningIndex.Length; idx++)
                {
                    int compactSkinningIndex = (int)indicesPtr[idx].Convert();
                    indicesUsed.Set(compactSkinningIndex - minIndex, true);
                }
            }

            // Another O(n) pass to get in "compact skinning index" order
            var result = new NativeArray<VertexIndices>(
                meshToCompactSkinningIndex.Length,
                Allocator.Persistent,
                NativeArrayOptions.UninitializedMemory);

            // Add first entry
            // Fill in other entries
            int resultIndex = 0;
            for (int idx = minIndex; idx <= maxIndex; idx++)
            {
                if (indicesUsed[idx - minIndex])
                {
                    result[resultIndex++] = new VertexIndices
                    {
                        compactSkinningIndex = (uint)idx,
                        outputBufferIndex = compactSkinningToMesh[(uint)idx]
                    };
                }
            }
            Profiler.EndSample();

            return result;
        }

        private const string LOG_SCOPE = nameof(OvrAvatarComputeSkinnedPrimitive);

        #region Properties

        public bool IsLoading => _buildSlice.IsValid;
        internal OvrAvatarComputeSkinningVertexBuffer VertexBuffer => _vertexBuffer;

        internal NativeArray<VertexIndices> MeshAndCompactSkinningIndices => _meshAndCompactSkinningIndices;

        #endregion

        #region Fields

        private OvrTime.SliceHandle _buildSlice;
        private bool _isWaitingForVertexBuffer;

        private OvrAvatarComputeSkinningVertexBuffer _vertexBuffer;
        private NativeArray<VertexIndices> _meshAndCompactSkinningIndices;

        private Func<NativeArray<byte>, CAPI.ovrAvatar2DataFormat, NativeArray<byte>> _genVertToCompactIndex;

        private Action _failureCallback;
        private Action _compactSkinningDataLoadedCallback;
        private Action _finishCallback;

        private readonly CAPI.ovrAvatar2CompactSkinningDataId _compactSkinningId;

        #endregion

        #region Static Fields

        private static Dictionary<CAPI.ovrAvatar2CompactSkinningDataId, VertexBufferInfo> _vertexBufferInfos;

        private static bool _staticMapsInitialized = false;

        #endregion

        #region Nested Types

        internal struct VertexIndices
        {
            public uint compactSkinningIndex; // Index in the compact skinning vertex buffer
            public uint outputBufferIndex; // Index into the mesh output buffer
        }

        private interface IConvertibleTo<T> where T : unmanaged { T Convert(); }

        private struct UInt32Wrapper : IConvertibleTo<UInt32>
        {
            private UInt32 _myValue;
            public UInt32Wrapper(UInt32 myValue)
            {
                _myValue = myValue;
            }
            public uint Convert()
            {
                return _myValue;
            }
        }

        private struct UInt16Wrapper : IConvertibleTo<UInt32>
        {
            private UInt16 _myValue;
            public UInt16Wrapper(UInt16 myValue)
            {
                _myValue = myValue;
            }
            public uint Convert()
            {
                return _myValue;
            }
        }

        private struct UInt8Wrapper : IConvertibleTo<UInt32>
        {
            private byte _myValue;
            public UInt8Wrapper(byte myValue)
            {
                _myValue = myValue;
            }
            public uint Convert()
            {
                return _myValue;
            }
        }

        private class VertexBufferInfo
        {
            private WeakReference<OvrAvatarComputeSkinningVertexBuffer> _createdBuffer;
            private OvrAvatarComputeSkinningVertexBufferBuilder _currentBuilder;
            private List<OvrAvatarComputeSkinnedPrimitive> _pendingLoads;

            private int _bufferRetainCount = 0;

            public bool HasBuffer
            {
                get
                {
                    if (_createdBuffer == null)
                    {
                        return false;
                    }

                    return _createdBuffer.TryGetTarget(out _);
                }
            }

            public bool HasPendingLoads => _pendingLoads?.Count > 0;

            public bool TryGetCreatedBuffer(out OvrAvatarComputeSkinningVertexBuffer buffer)
            {
                if (_createdBuffer == null)
                {
                    buffer = null;
                    return false;
                }

                return _createdBuffer.TryGetTarget(out buffer);
            }

            public void CreateBuilderIfNeededAndAddPendingLoad(
                OvrAvatarComputeSkinnedPrimitive pending,
                CAPI.ovrAvatar2CompactSkinningDataId id)
            {
                // Check if new builder needs to be kicked off
                if (_currentBuilder == null || !_currentBuilder.IsLoading)
                {
                    _currentBuilder = new OvrAvatarComputeSkinningVertexBufferBuilder(
                        id,
                        OnVertexBufferBuildFailure,
                        OnCompactSkinningDataFinishedFailure,
                        OnVertexBufferCreation);
                }

                _pendingLoads ??= new List<OvrAvatarComputeSkinnedPrimitive>(1);
                _pendingLoads.Add(pending);
            }

            public void RetainBuffer()
            {
                _bufferRetainCount += 1;
            }

            public void ReleaseBuffer()
            {
                _bufferRetainCount -= 1;

                if (_bufferRetainCount <= 0)
                {
                    if (TryGetCreatedBuffer(out var buff))
                    {
                        buff.Dispose();
                    }
                    _createdBuffer = null;
                }
            }

            public void DisposeBuilder()
            {
                _currentBuilder?.Dispose();
                _currentBuilder = null;
            }

            public void RemovePendingLoad(OvrAvatarComputeSkinnedPrimitive pending)
            {
                _pendingLoads?.Remove(pending);
            }

            private void OnVertexBufferBuildFailure()
            {
                // Notify pending loads of failure
                if (_pendingLoads == null)
                {
                    return;
                }

                foreach (var load in _pendingLoads)
                {
                    load.OnVertexBufferBuildFailed();
                }

                // Clear out pending loads
                _pendingLoads.Clear();
            }

            private IEnumerator<OvrTime.SliceStep> OnVertexBufferCreation(OvrAvatarComputeSkinningVertexBuffer buffer)
            {
                _createdBuffer = new WeakReference<OvrAvatarComputeSkinningVertexBuffer>(buffer);

                // Notify pending loads of success

                // If there are no pending loads, then whatever was potentially waiting
                // for this vertex buffer to be built is no longer waiting, in that scenario,
                // free the buffer
                if (!HasPendingLoads)
                {
                    buffer.Dispose();
                    _createdBuffer = null;
                    yield return OvrTime.SliceStep.Cancel;
                }

                foreach (var load in _pendingLoads)
                {
                    if (OvrTime.ShouldHold) { yield return OvrTime.SliceStep.Hold; }
                    load.OnVertexBufferCreated(buffer, this);
                }

                // Clear out pending loads
                _pendingLoads.Clear();
            }

            private void OnCompactSkinningDataFinishedFailure()
            {
                // Notify pending loads that the compact skinning data is no longer needed
                if (_pendingLoads == null)
                {
                    return;
                }

                foreach (var load in _pendingLoads)
                {
                    load.OnVertexBufferBuildFreeCompactSkinning();
                }
            }
        }

        #endregion
    } // end class OvrAvatarComputeSkinnedPrimitive
} // end namespace
