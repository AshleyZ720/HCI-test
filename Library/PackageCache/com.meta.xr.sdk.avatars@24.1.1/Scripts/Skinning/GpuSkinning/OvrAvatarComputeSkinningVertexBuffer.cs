using System;
using System.Collections.Generic;
using Oculus.Avatar2;

using Unity.Collections;

using UnityEngine;

namespace Oculus.Skinning.GpuSkinning
{
    /**
     * Class representing the "VertexBuffer" used in compute shader based skinning.
     * A "VertexBuffer" can be shared across many different instances of a mesh/primitive
     *
     * NOTE:
     * What is wanted here ideally is that there would be a way to call
     * tell when there are no more references to the ComputeBuffer
     * besides this class and then Dispose of it. Sort of like RefCountDisposable
     * in one of the reactive .NET stuff. Until then, this will just dispose in the finalizer
     * so it will always dispose one garbage collection cycle late. Not the worst thing.
     */
    public sealed class OvrAvatarComputeSkinningVertexBuffer : IDisposable
    {
        internal ComputeBuffer Buffer => _buffer;
        internal NativeArray<byte> OriginalToCompactSkinningIndex => _origToCompactSkinningIndex;

        internal OvrComputeUtils.DataFormatAndStride VertexIndexFormatAndStride { get; }

        public int NumMorphedVerts => _numMorphedVerts;

        internal struct DataFormatsAndStrides
        {
            public OvrComputeUtils.DataFormatAndStride vertexPositions;
            public OvrComputeUtils.DataFormatAndStride jointIndices;
            public OvrComputeUtils.DataFormatAndStride jointWeights;
            public OvrComputeUtils.DataFormatAndStride morphIndices;
            public OvrComputeUtils.DataFormatAndStride nextEntryIndices;
        }

        internal DataFormatsAndStrides FormatsAndStrides { get; }

        internal bool IsDataInClientSpace { get; }
        internal Matrix4x4 ClientSpaceTransform { get; }

        // Takes ownership of compactSkinningInverseReorderBuffer
        internal OvrAvatarComputeSkinningVertexBuffer(
            ComputeBuffer buffer,
            NativeArray<byte> compactSkinningInverseReorderBuffer,
            OvrComputeUtils.DataFormatAndStride vertexIndexFormatAndStride,
            DataFormatsAndStrides formatsAndStrides,
            int numMorphedVerts,
            bool isDataInClientSpace,
            Matrix4x4 clientSpaceTransform)
        {
            _buffer = buffer;
            _origToCompactSkinningIndex = compactSkinningInverseReorderBuffer;
            VertexIndexFormatAndStride = vertexIndexFormatAndStride;

            FormatsAndStrides = formatsAndStrides;
            _numMorphedVerts = numMorphedVerts;

            IsDataInClientSpace = isDataInClientSpace;
            ClientSpaceTransform = clientSpaceTransform;
        }

        ~OvrAvatarComputeSkinningVertexBuffer()
        {
            Dispose(false);
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
                _buffer.Release();
                _origToCompactSkinningIndex.Reset();
            }
        }

        private readonly ComputeBuffer _buffer;
        private NativeArray<byte> _origToCompactSkinningIndex;

        private readonly int _numMorphedVerts;
    }
}
