using UnityEngine;

namespace Oculus.Avatar2
{
    public static class OvrAvatarConversions
    {
        // TODO: Make internal, used by BodyAnimTracking.Update()
        public static CAPI.ovrAvatar2Transform ConvertSpace(this in CAPI.ovrAvatar2Transform from)
        {
            return new CAPI.ovrAvatar2Transform(
                from.position.ConvertSpace(), from.orientation.ConvertSpace(), in from.scale);
        }

        // TODO: Make internal, used by BodyAnimTracking.Start()
        public static CAPI.ovrAvatar2Quatf ConvertSpace(this in CAPI.ovrAvatar2Quatf from)
        {
            return new CAPI.ovrAvatar2Quatf(-from.x, -from.y, from.z, from.w);
        }

        // TODO: Make internal, used by BodyAnimTracking.Start()
        public static CAPI.ovrAvatar2Vector3f ConvertSpace(this in CAPI.ovrAvatar2Vector3f from)
        {
            return new CAPI.ovrAvatar2Vector3f(from.x, from.y, -from.z);
        }

        // TODO: Make internal, used by BodyAnimTracking.Update()
        public static CAPI.ovrAvatar2Transform ConvertSpace(this Transform from)
        {
            return new CAPI.ovrAvatar2Transform(
                from.localPosition.ConvertSpace(), from.localRotation.ConvertSpace(), (CAPI.ovrAvatar2Vector3f)from.localScale);
        }

        internal static CAPI.ovrAvatar2Quatf ConvertSpace(this in Quaternion from)
        {
            return new CAPI.ovrAvatar2Quatf(-from.x, -from.y, from.z, from.w);
        }

        internal static CAPI.ovrAvatar2Vector3f ConvertSpace(this in Vector3 from)
        {
            return new CAPI.ovrAvatar2Vector3f(from.x, from.y, -from.z);
        }

        // Same as equivalent ConvertSpace function above, but using the RT rig space conversion
        public static CAPI.ovrAvatar2Transform ConvertSpaceRT(this in CAPI.ovrAvatar2Transform from)
        {
            return new CAPI.ovrAvatar2Transform(
                from.position.ConvertSpaceRT(), from.orientation.ConvertSpaceRT(), in from.scale);
        }

        // Same as equivalent ConvertSpace function above, but using the RT rig space conversion
        public static CAPI.ovrAvatar2Quatf ConvertSpaceRT(this in CAPI.ovrAvatar2Quatf from)
        {
            return new CAPI.ovrAvatar2Quatf(-from.x, from.y, -from.z, from.w);
        }

        // Same as equivalent ConvertSpace function above, but using the RT rig space conversion
        public static CAPI.ovrAvatar2Vector3f ConvertSpaceRT(this in CAPI.ovrAvatar2Vector3f from)
        {
            return new CAPI.ovrAvatar2Vector3f(-from.x, from.y, -from.z);
        }

        // Same as equivalent ConvertSpace function above, but using the RT rig space conversion
        public static CAPI.ovrAvatar2Transform ConvertSpaceRT(this Transform from)
        {
            return new CAPI.ovrAvatar2Transform(
                from.localPosition.ConvertSpaceRT(), from.localRotation.ConvertSpaceRT(), (CAPI.ovrAvatar2Vector3f)from.localScale);
        }

        // Same as equivalent ConvertSpace function above, but using the RT rig space conversion
        internal static CAPI.ovrAvatar2Quatf ConvertSpaceRT(this in Quaternion from)
        {
            return new CAPI.ovrAvatar2Quatf(-from.x, from.y, -from.z, from.w);
        }

        // Same as equivalent ConvertSpace function above, but using the RT rig space conversion
        internal static CAPI.ovrAvatar2Vector3f ConvertSpaceRT(this in Vector3 from)
        {
            return new CAPI.ovrAvatar2Vector3f(-from.x, from.y, -from.z);
        }

        internal static void ApplyWorldOvrTransform(this Transform transform, in CAPI.ovrAvatar2Transform from)
        {
#if UNITY_2021_3_OR_NEWER
            transform.SetPositionAndRotation(from.position, from.orientation);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
            transform.localPosition = from.position;
            transform.localRotation = from.orientation;
#endif // !UNITY_2021_3_OR_NEWER
            transform.localScale = from.scale;
        }

        internal static void ApplyOvrTransform(this Transform transform, in CAPI.ovrAvatar2Transform from)
        {
            // NOTE: We could route this to the * version, but `fixed` has non-trivial overhead
#if UNITY_2021_3_OR_NEWER
            transform.SetLocalPositionAndRotation(from.position, from.orientation);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
            transform.localPosition = from.position;
            transform.localRotation = from.orientation;
#endif // !UNITY_2021_3_OR_NEWER
            transform.localScale = from.scale;
        }

        internal unsafe static void ApplyOvrTransform(this Transform transform, CAPI.ovrAvatar2Transform* from)
        {
#if UNITY_2021_3_OR_NEWER
            transform.SetLocalPositionAndRotation(from->position, from->orientation);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
            transform.localPosition = from->position;
            transform.localRotation = from->orientation;
#endif // !UNITY_2021_3_OR_NEWER
            transform.localScale = from->scale;
        }

        internal static CAPI.ovrAvatar2Transform ToWorldOvrTransform(this Transform t)
        {
            Vector3 position;
            Quaternion rotation;
#if UNITY_IMPROVED_TRANSFORMS
            // NOTE: Transform.GetPositionAndRotation is only available in Unity versions 2021.3.17 and above.
            t.GetPositionAndRotation(out position, out rotation);
#else // ^^^ UNITY_2021_3_OR_NEWER / !UNITY_2021_3_OR_NEWER vvv
            position = t.position;
            rotation = t.rotation;
#endif // !UNITY_2021_3_OR_NEWER

            return new CAPI.ovrAvatar2Transform(position, rotation, t.localScale);
        }

        internal static Matrix4x4 ToMatrix(this in CAPI.ovrAvatar2Transform t)
        {
            return Matrix4x4.TRS(t.position, t.orientation, t.scale);
        }
    }

}
