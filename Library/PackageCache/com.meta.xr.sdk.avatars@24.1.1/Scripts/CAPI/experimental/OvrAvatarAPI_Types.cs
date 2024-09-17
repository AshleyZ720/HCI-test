using System;
using System.Runtime.InteropServices;

namespace Oculus.Avatar2.Experimental
{
    using ovrAvatar2SizeType = UIntPtr;

    public static partial class CAPI
    {
        // TODO: `ref` types can not be used as type arguments to `Action`/`Func`
        // Re-enable once custom event callback delegates are fully setup
        [StructLayout(LayoutKind.Sequential)]
        internal readonly unsafe /* ref */ struct ovrAvatar2DataView
        {
            public readonly void* data;
            public readonly ovrAvatar2SizeType size;

            public ovrAvatar2DataView(void* data_, ovrAvatar2SizeType size_)
            {
                data = data_;
                size = size_;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct ovrAvatar2StringView
        {
            // Pointer to first character in string
            public string data;
            // Number of valid bytes in `data`
            public ovrAvatar2SizeType size;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public unsafe struct ovrAvatar2StringBuffer
        {
            // Pointer to first character in string buffer
            public char* data;

            // Maximum capacity of `data` block
            public ovrAvatar2SizeType capacity;

            // Number of valid bytes in `data`
            public ovrAvatar2SizeType charactersWritten;
        }
    }
}
