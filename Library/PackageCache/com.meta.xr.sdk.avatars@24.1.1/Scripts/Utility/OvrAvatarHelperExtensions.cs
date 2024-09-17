using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

using UnityEngine;

namespace Oculus.Avatar2
{
    public static class OvrAvatarHelperExtensions
    {
        #region Enum Helpers

        // count the number of bits set in `value`
        // K&R Algo - simple and fast-ish
        // Don't use this in a hot loop :X
        public static UInt32 PopCount(this UInt32 value)
        {
            unchecked
            {
                UInt32 count; // c accumulates the total bits set in v
                for (count = 0; value != 0; count++)
                {
                    value &= value - 1; // clear the least significant bit set
                }
                return count;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 PopCount(this Int32 value)
        {
            unchecked { return ((UInt32)value).PopCount(); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSuccess(this CAPI.ovrAvatar2Result result)
        {
            return result == CAPI.ovrAvatar2Result.Success;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFailure(this CAPI.ovrAvatar2Result result)
        {
            return result != CAPI.ovrAvatar2Result.Success;
        }

        private const CAPI.ovrAvatar2EntityFeatures _reservedFeatureBits = CAPI.ovrAvatar2EntityFeatures.ReservedExtra | (CAPI.ovrAvatar2EntityFeatures)(1 << 3);
        private const CAPI.ovrAvatar2EntityFeatures _experimentalFeatureBits = ~(CAPI.ovrAvatar2EntityFeatures.All | _reservedFeatureBits);
        public static void AddExperimentalFeatures(this ref CAPI.ovrAvatar2EntityFeatures featureFlags)
        {
            featureFlags = featureFlags.WithExperimentalFeaturesEnabled();
        }
        public static CAPI.ovrAvatar2EntityFeatures WithExperimentalFeaturesEnabled(this CAPI.ovrAvatar2EntityFeatures featureFlags)
        {
            return featureFlags | _experimentalFeatureBits;
        }
        public static bool HasAnyExperimentalFeatures(this CAPI.ovrAvatar2EntityFeatures featureFlags)
        {
            return (featureFlags & _experimentalFeatureBits) != 0;
        }
        public static bool HasAllExperimentalFeatures(this CAPI.ovrAvatar2EntityFeatures featureFlags)
        {
            return (featureFlags & _experimentalFeatureBits) == _experimentalFeatureBits;
        }

        #endregion // Enum Helpers

        #region Containers

        // HashSet<T> Extensions

        public static T[] NullSafeToArray<T>(this HashSet<T> set)
        {
            return set != null ? set.ToArray() : Array.Empty<T>();
        }

        public static T[] ToArray<T>(this HashSet<T> set)
        {
            var copyArray = set.Count > 0 ? new T[set.Count] : Array.Empty<T>();
            set.CopyTo(copyArray);
            return copyArray;
        }

        // Unity.NativeArray<T> Extensions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetBufferSize<T>(in this NativeArray<T> array) where T : struct
        {
            return array.GetBufferSize(UnsafeUtility.SizeOf<T>());
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetBufferSize<T>(in this NativeArray<T> array, UInt32 elementSize) where T : struct
        {
            Debug.Assert(elementSize != 0);
            Debug.Assert(elementSize == UnsafeUtility.SizeOf<T>());
            return (UInt32)(array.Length * elementSize);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetBufferSize<T>(in this NativeArray<T> array, int elementSize) where T : struct
        {
            Debug.Assert(elementSize > 0);
            return array.GetBufferSize((UInt32)elementSize);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetBufferSize(in this NativeArray<byte> array)
        {
            return (UInt32)array.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetEnumBufferSize<T>(in this NativeArray<T> array) where T : struct, Enum
        {
            var underlyingType = Enum.GetUnderlyingType(typeof(T));
            return array.GetEnumBufferSize((uint)Marshal.SizeOf(underlyingType));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetEnumBufferSize<T>(in this NativeArray<T> array, UInt32 explicitSize) where T : struct, Enum
        {
            Debug.Assert(Marshal.SizeOf(Enum.GetUnderlyingType(typeof(T))) == explicitSize);
            return (UInt32)(array.Length * explicitSize);
        }

        /* Lengths must match */
        // NOTE: ref is invalid for `array` when used in `using` statement, eat 1 copy :(
        public static void CopyFrom<T>(this NativeArray<T> array, HashSet<T> hashSet) where T : struct
        {
            OvrAvatarLog.Assert(array.Length == hashSet.Count);
            int copyIdx = 0;
            foreach (var value in hashSet)
            {
                array[copyIdx++] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr GetIntPtr<T>(in this NativeArray<T> array) where T : struct
        {
            unsafe
            {
                return array.IsNull() ? IntPtr.Zero : (IntPtr)array.GetUnsafePtr();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T* GetPtr<T>(in this NativeArray<T> array) where T : unmanaged
        {
            return array.IsNull() ? null : (T*)array.GetUnsafePtr();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe CastT* CastPtr<T, CastT>(in this NativeArray<T> array)
            where T : unmanaged
            where CastT : unmanaged
        {
            Debug.Assert(UnsafeUtility.SizeOf<CastT>() == UnsafeUtility.SizeOf<T>());
            return array.IsNull() ? null : (CastT*)array.GetUnsafePtr();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void GetCastPtr<T, CastT>(in this NativeArray<T> array, out CastT* outPtr)
            where T : unmanaged
            where CastT : unmanaged
        {
            outPtr = array.CastPtr<T, CastT>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe CAPI.ovrAvatar2Vector2f* CastOvrPtr(in this NativeArray<Vector2> array)
        {
            return array.CastPtr<Vector2, CAPI.ovrAvatar2Vector2f>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe CAPI.ovrAvatar2Vector3f* CastOvrPtr(in this NativeArray<Vector3> array)
        {
            return array.CastPtr<Vector3, CAPI.ovrAvatar2Vector3f>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe CAPI.ovrAvatar2Vector4f* CastOvrPtr(in this NativeArray<Vector4> array)
        {
            return array.CastPtr<Vector4, CAPI.ovrAvatar2Vector4f>();
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe CAPI.ovrAvatar2Quatf* CastOvrPtr(in this NativeArray<Quaternion> array)
        {
            return array.CastPtr<Quaternion, CAPI.ovrAvatar2Quatf>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T* GetReadonlyPtr<T>(in this NativeArray<T> array) where T : unmanaged
        {
            return array.IsNull() ? null : (T*)array.GetUnsafeReadOnlyPtr();
        }

        /* If the target array `IsCreated`, `Dispose` it and reset the reference to default */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Reset<T>(ref this NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
                array = default;
            }
        }

        /* If the target array has a valid buffer and can provide a pointer which may safely be de-referenced
            By definition, the array is not empty else its pointer could not safely be de-referenced */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull<T>(in this NativeArray<T> array) where T : struct
        {
            // These checks are redundant in any one version of Unity, but are compatible across all recent versions :/
            if (array.Length <= 0 || !array.IsCreated) { return true; }
            unsafe { return array.GetUnsafeReadOnlyPtr() == null; }
        }

        /* Calling dispose on an unallocated NativeArray throws an exception
         * This makes code handling optional NativeArrays rather clunky
         * This wrapper simply adds an `IsCreated` check before forwarding the `Dispose` call*/
        public struct NativeArrayDisposeWrapper<T> : IDisposable, IEnumerable<T>, System.Collections.IEnumerable where T : struct
        {
            public NativeArrayDisposeWrapper(in NativeArray<T> wrappedArray)
                => array = wrappedArray;

            public NativeArray<T> array;
            public bool IsCreated => array.IsCreated;
            public int Length => array.Length;

            public void Dispose()
            {
                if (array.IsCreated)
                {
                    array.Dispose();
                }
            }

            public T[] ToArray() => array.IsCreated ? array.ToArray() : Array.Empty<T>();

            public static implicit operator NativeArrayDisposeWrapper<T>(in NativeArray<T> natArr)
                => natArr.GetDisposeSafe();

            // C# is truly a flawless language...
            public NativeArray<T>.Enumerator GetEnumerator() => array.GetEnumerator();
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => array.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                => array.GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NativeArrayDisposeWrapper<T> GetDisposeSafe<T>(this in NativeArray<T> array) where T : struct
        {
            return new NativeArrayDisposeWrapper<T>(in array);
        }

        // NativeSlice<T> Extensions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe T* GetPtr<T>(in this NativeSlice<T> slice) where T : unmanaged
        {
            return (T*)slice.GetUnsafePtr();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetBufferSize<T>(in this NativeSlice<T> slice) where T : struct
        {
            unchecked { return (UInt32)slice.Length * (UInt32)UnsafeUtility.SizeOf<T>(); }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UInt32 GetBufferSize(in this NativeSlice<byte> array)
        {
            return (UInt32)array.Length;
        }

        // Dictionary<K, V> Extensions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Dictionary<K, V> Copy<K, V>(this Dictionary<K, V> dict)
        {
            return new Dictionary<K, V>(dict);
        }

        public static void CopyFrom<K, V>(this Dictionary<K, V> dest, Dictionary<K, V> source)
        {
            dest.Clear();
            foreach (var kvp in source)
            {
                dest.Add(kvp.Key, kvp.Value);
            }
        }

        public static void CopyFrom<K, V>(this Dictionary<K, V[]> dest, Dictionary<K, List<V>> source)
        {
            dest.Clear();
            foreach (var kvp in source)
            {
                dest.Add(kvp.Key, kvp.Value.ToArray());
            }
        }

        // T[] Extensions

        /* Concatenate newElement onto the target array, returning the new array -
         *  Previous array target should be considered invalid */
        public static T[] Concat<T>(this T[] array, in T newElement)
        {
            var startLen = array.Length;
            Array.Resize(ref array, startLen + 1);
            array[startLen] = newElement;
            return array;
        }

        /* Remove `elementToRemove` from the target array and return the resulting array, preserving order
         *  Shifts all elements after the removed index
         *  O(n) [find index] + O(n) [shift elements]
         *  Previous array instance should be considered invalid */
        public static T[] SliceOut<T>(this T[] array, in T elementToRemove)
        {
            int idx = array.IndexOf(in elementToRemove);
            if (idx >= 0)
            {
                array = array.SliceOutIndex(idx);
            }
            return array;
        }

        /* Remove `elementToRemove` from the target array and return the resulting array, preserving order
         *  Shifts all elements after the removed index
         *  O(n) [find index] + O(n) [shift elements]
         *  Previous array instance should be considered invalid */
        public static T[] SliceOutIndex<T>(this T[] array, int indexToRemove)
        {
            var newLength = array.Length - 1;
            while (indexToRemove < newLength)
            {
                array[indexToRemove] = array[++indexToRemove];
            }
            Array.Resize(ref array, newLength);
            return array;
        }

        /* Remove `elementToRemove` from the target array and return the resulting array, changes order
         *  Swaps the removed index with the last element in the array, then resizes
         *  O(n) [find index] + O(1) [swap elements]
         *  Previous array instance should be considered invalid */
        public static T[] SwapOut<T>(this T[] array, in T elementToRemove)
        {
            int idx = array.IndexOf(in elementToRemove);
            if (idx >= 0)
            {
                array = array.SwapOutIndex(idx);
            }
            return array;
        }

        /* Remove `indexToRemove` from the target array and return the resulting array, changes order
         *  Swaps the removed index with the last element in the array, then resizes
         *  O(n) [find index] + O(1) [swap elements]
         *  Previous array instance should be considered invalid */
        public static T[] SwapOutIndex<T>(this T[] array, int indexToRemove)
        {
            var newLength = array.Length - 1;
            array[indexToRemove] = array[newLength];
            Array.Resize(ref array, newLength);
            return array;
        }

        /* Returns the index of `value` in `array`, or -1 if `value` was not found
         *  O(n) */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int IndexOf<T>(this T[] array, in T value)
        {
            return Array.IndexOf(array, value);
        }

        /* Returns the index of `value` in `array` if found; otherwise, a negative number.
         * - If value is not found and value is less than one or more elements in array,
         *   the negative number returned is the bitwise complement of the index of the first element that is larger than value.
         * - If value is not found and value is greater than all elements in array,
         *   the negative number returned is the bitwise complement of (the index of the last element plus 1).
         * - If this method is called with a non-sorted array,
         *   the return value can be incorrect and a negative number could be returned, even if value is present in array.
         * Array must be sorted in increasing order via T's IComparable implementation
         *  O(logn) - performs a binary search */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinarySearch<T>(this T[] array, in T value)
        {
            return Array.BinarySearch(array, value);
        }

        /* Returns the index of `value` in `array`, if found; otherwise, a negative number.
         * - If value is not found and value is less than one or more elements in array,
         *   the negative number returned is the bitwise complement of the index of the first element that is larger than value.
         * - If value is not found and value is greater than all elements in array,
         *   the negative number returned is the bitwise complement of (the index of the last element plus 1).
         * - If this method is called with a non-sorted array,
         *   the return value can be incorrect and a negative number could be returned, even if value is present in array.
         * Array must be sorted in increasing order per `comparer`
         *  O(logn) - performs a binary search using `comparer` */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinarySearch<T>(this T[] array, in T value, IComparer<T> comparer)
        {
            return Array.BinarySearch(array, value, comparer);
        }
        // TODO: BinarySearch backed variant of Contains?

        /* Returns `true` if `value` in `array`, `false` otherwise
         *  O(n) */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains<T>(this T[] array, in T value)
        {
            return array.IndexOf(in value) >= 0;
        }

        public static bool Contains<T>(this IReadOnlyList<T> list, in T value, int searchLength) where T : class
        {
            for (int idx = 0; idx < searchLength; ++idx)
            {
                if (list[idx] == value)
                {
                    return true;
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this Array array, in object value)
        {
            Int32 index = Array.IndexOf(array, value);
            // Yay .NET https://docs.microsoft.com/en-us/dotnet/api/system.array.indexof?view=netcore-3.1
            return array.GetLowerBound(0) <= index && index < Int32.MaxValue;
        }

        // List<T> Extensions

        // Helpers for swapping List in to replace LinkedList (which makes lots of GC.Allocs)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T? First<T>(this List<T> list) where T : struct
        {
            return list.Count > 0 ? list[0] : (T?)null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddFirst<T>(this List<T> list, T element)
        {
            list.Insert(0, element);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddLast<T>(this List<T> list, T element)
        {
            list.Add(element);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void RemoveFirst<T>(this List<T> list)
        {
            list.RemoveAt(0);
        }

        #endregion

        #region Logging

        private const string DefaultContext = "no context";
        private const string DefaultScope = "ovrAvatar2.CAPI";
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EnsureSuccess(this CAPI.ovrAvatar2Result result
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            bool wasSuccess = result.IsSuccess();
            if (!wasSuccess)
            {
                result.LogError(msgContext, logScope, unityContext);
            }
            return wasSuccess;
        }

        // TODO: Expand `EnsureSuccessOrLog` so it can handle `EnsureSuccessOrWarning` as expected
        // May be easier to just update every call to `EnsureSuccessOrWarning`
        // to include `to resolve this warning` text
        private const string DefaultWarningSuggestion = "verify call";
        internal static bool EnsureSuccessOrWarning(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithWarningResult
            , string warningSuggestion = DefaultWarningSuggestion
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            bool wasSuccessOrWarn = result.IsSuccess();
            if (!wasSuccessOrWarn)
            {
                wasSuccessOrWarn = result == succeedWithWarningResult;
                if (wasSuccessOrWarn)
                {
                    OvrAvatarLog.LogWarning(
                        $"Operation ({msgContext}) succeeded with warning ({result})\n - {warningSuggestion} to resolve this warning"
                        , logScope, unityContext);
                }
                else
                {
                    result.LogError(msgContext, logScope, unityContext);
                }
            }
            return wasSuccessOrWarn;
        }

        internal static bool EnsureSuccessOrWarning(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithWarningResult0
            , CAPI.ovrAvatar2Result succeedWithWarningResult1
            , string warningSuggestion = DefaultWarningSuggestion
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            bool wasSuccessOrWarn = result.IsSuccess();
            if (!wasSuccessOrWarn)
            {
                wasSuccessOrWarn = result == succeedWithWarningResult0 || result == succeedWithWarningResult1;
                if (wasSuccessOrWarn)
                {
                    OvrAvatarLog.LogWarning(
                        $"Operation ({msgContext}) succeeded with warning ({result})\n - {warningSuggestion} to resolve this warning"
                        , logScope, unityContext);
                }
                else
                {
                    result.LogError(msgContext, logScope, unityContext);
                }
            }
            return wasSuccessOrWarn;
        }

        private const string DefaultLogVerboseContext = "no action required";
        // TODO: System.Diagnostic.Conditional
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EnsureSuccessOrLogVerbose(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithLogVerboseResult
            , string logDebugContext = DefaultLogVerboseContext
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            return EnsureSuccessOrLog(result, succeedWithLogVerboseResult,
                logDebugContext, msgContext, logScope, unityContext,
                OvrAvatarLog.ELogLevel.Verbose);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EnsureSuccessOrLogVerbose(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithLogVerboseResult0
            , CAPI.ovrAvatar2Result succeedWithLogVerboseResult1
            , string logDebugContext = DefaultLogVerboseContext
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            return EnsureSuccessOrLog(
                result, succeedWithLogVerboseResult0, succeedWithLogVerboseResult1,
                logDebugContext, msgContext, logScope, unityContext,
                OvrAvatarLog.ELogLevel.Verbose);
        }

        private const string DefaultLogDebugContext = "this should be a transient issue";
        // TODO: System.Diagnostic.Conditional
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EnsureSuccessOrLogDebug(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithLogDebugResult
            , string logDebugContext = DefaultLogDebugContext
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            return EnsureSuccessOrLog(result, succeedWithLogDebugResult,
                logDebugContext, msgContext, logScope, unityContext,
                OvrAvatarLog.ELogLevel.Debug);
        }

        private const string DefaultLogInfoContext = "verify no unwanted side effects";
        // TODO: System.Diagnostic.Conditional
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool EnsureSuccessOrLogInfo(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithLogDebugResult
            , string logDebugContext = DefaultLogInfoContext
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            return EnsureSuccessOrLog(result, succeedWithLogDebugResult,
                logDebugContext, msgContext, logScope, unityContext,
                OvrAvatarLog.ELogLevel.Info);
        }

        // TODO: System.Diagnostic.Conditional
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError(this CAPI.ovrAvatar2Result result
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            OvrAvatarLog.Assert(!result.IsSuccess());
            OvrAvatarLog.LogError($"Operation ({msgContext}) failed with result ({result})"
                , logScope, unityContext);
        }

        // TODO: System.Diagnostic.Conditional
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogAssert(this CAPI.ovrAvatar2Result result
            , string msgContext = DefaultContext
            , string logScope = DefaultScope
            , UnityEngine.Object unityContext = null)
        {
            OvrAvatarLog.AssertTwoParams(result.IsSuccess()
                , msgContext, in result
                , _CachedLogAssertBuilder
                , logScope, unityContext);
        }
        // This ensures Mono doesn't helpfully allocate a wrapper every time this is called...
        private static readonly OvrAvatarLog.AssertMessageBuilder<string, CAPI.ovrAvatar2Result>
            _CachedLogAssertBuilder = _LogAssertBuilder;
        private static string _LogAssertBuilder(in string msgCtx, in CAPI.ovrAvatar2Result r)
            => $"{msgCtx} failed with {r}";


        private static bool EnsureSuccessOrLog(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithLogResult
            , string logDebugContext
            , string msgContext
            , string logScope
            , UnityEngine.Object unityContext
            , OvrAvatarLog.ELogLevel logLevel)
        {
            bool wasSuccessOrLog = result.IsSuccess();
            if (!wasSuccessOrLog)
            {
                wasSuccessOrLog = result == succeedWithLogResult;
                if (wasSuccessOrLog)
                {
                    OvrAvatarLog.Log(logLevel
                        , $"Operation ({msgContext}) succeeded with log ({result})\n - {logDebugContext}"
                        , logScope, unityContext);
                }
                else
                {
                    result.LogError(msgContext, logScope, unityContext);
                }
            }
            return wasSuccessOrLog;
        }

        private static bool EnsureSuccessOrLog(this CAPI.ovrAvatar2Result result
            , CAPI.ovrAvatar2Result succeedWithLogResult0
            , CAPI.ovrAvatar2Result succeedWithLogResult1
            , string logDebugContext
            , string msgContext
            , string logScope
            , UnityEngine.Object unityContext
            , OvrAvatarLog.ELogLevel logLevel)
        {
            bool wasSuccessOrLog = result.IsSuccess();
            if (!wasSuccessOrLog)
            {
                wasSuccessOrLog = result == succeedWithLogResult0 || result == succeedWithLogResult1;
                if (wasSuccessOrLog)
                {
                    OvrAvatarLog.Log(logLevel
                        , $"Operation ({msgContext}) succeeded with log ({result})\n - {logDebugContext}"
                        , logScope, unityContext);
                }
                else
                {
                    result.LogError(msgContext, logScope, unityContext);
                }
            }
            return wasSuccessOrLog;
        }
        #endregion // Logging

        #region Strings

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string source, string toCheck, StringComparison comp)
        {
            return source.IndexOf(toCheck, comp) >= 0;
        }

        #endregion // Strings
    }
} // namespace Oculus.Avatar2
