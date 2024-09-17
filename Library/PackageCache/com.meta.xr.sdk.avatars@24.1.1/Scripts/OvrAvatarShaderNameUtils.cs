using System;
using System.Collections.Generic;
using System.Reflection;

namespace Oculus.Avatar2
{
    public static class OvrAvatarShaderNameUtils
    {
        public enum KnownShader{
            AvatarMeta,
            AvatarMetaVertexGI,
            AvatarLibrary,
            AvatarHorizon,
            AvatarHuman,
            AvatarKhronos,
            AvatarMobileBumpedSpecular,
            AvatarMobileCustom,
            AvatarMobileDiffuse,
            AvatarMobileVertexLit,
            AvatarStandard,
            
            ErrorDeterminingShader = 998,
            UnknownShader = 999,
        }

        private static Dictionary<KnownShader, string> KnownShaderEnumToString = new Dictionary<KnownShader, string>
        {
            {KnownShader.AvatarMeta, "Avatar/Meta"},
            {KnownShader.AvatarMetaVertexGI, "Avatar/MetaVertexGI"},
            {KnownShader.AvatarLibrary, "Avatar/Library"},
            {KnownShader.AvatarHorizon, "Avatar/Horizon"},
            {KnownShader.AvatarHuman, "Avatar/Human"},
            {KnownShader.AvatarKhronos, "Avatar/Khronos"},
            {KnownShader.AvatarMobileBumpedSpecular, "Avatar/Mobile/Bumped Specular"},
            {KnownShader.AvatarMobileCustom, "Avatar/Mobile/Custom"},
            {KnownShader.AvatarMobileDiffuse, "Avatar/Mobile/Diffuse"},
            {KnownShader.AvatarMobileVertexLit, "Avatar/Mobile/VertexLit"},
            {KnownShader.AvatarStandard, "Avatar/Standard"}
        };
        
        private static readonly HashSet<KnownShader> DeprecatedShaders = new HashSet<KnownShader>
        {
            KnownShader.AvatarLibrary,
            KnownShader.AvatarHorizon,
            KnownShader.AvatarHuman,
            KnownShader.AvatarKhronos,
        };
        
        private static readonly HashSet<KnownShader> ReferenceOnlyShaders = new HashSet<KnownShader>
        {
            KnownShader.AvatarMobileBumpedSpecular,
            KnownShader.AvatarMobileCustom,
            KnownShader.AvatarMobileDiffuse,
            KnownShader.AvatarMobileVertexLit,
            KnownShader.AvatarStandard,
        };
        
        private static readonly HashSet<KnownShader> ShadersWithNoURPSupport = new HashSet<KnownShader>
        {
            KnownShader.AvatarMobileBumpedSpecular,
            KnownShader.AvatarMobileCustom,
            KnownShader.AvatarMobileDiffuse,
            KnownShader.AvatarMobileVertexLit,
            KnownShader.AvatarStandard,
        };        
        
        // should not use directly, even internally use GetShaderEnum which initializes this if it's not already initialized.
        private static Dictionary<string, KnownShader> KnownShaderStringToEnum = new Dictionary<string, KnownShader>();
        
        private static void InitializeKnownShaderStringToEnum()
        {
            if (KnownShaderStringToEnum.Count == 0)
            {
                foreach (KeyValuePair<KnownShader, string> pair in KnownShaderEnumToString)
                {
                    KnownShaderStringToEnum.Add(pair.Value, pair.Key);
                }
            }
        }

        private static string GetShaderName(KnownShader knownShader)
        {
            return KnownShaderEnumToString[knownShader];
        }

        private static KnownShader GetShaderEnum(string shaderName)
        {
            InitializeKnownShaderStringToEnum();
            if (!KnownShaderStringToEnum.ContainsKey(shaderName))
            {
                return KnownShader.UnknownShader;
            }

            return KnownShaderStringToEnum[shaderName];
        }

        public static int GetShaderIdentifier(string shaderName)
        {
            return (int)GetShaderEnum(shaderName);
        }

        public static bool IsKnown(string name)
        {
            return GetShaderEnum(name) != KnownShader.UnknownShader;
        }

        public static bool IsDeprecated(string name)
        {
            return DeprecatedShaders.Contains(GetShaderEnum(name));
        }

        public static bool IsReferenceOnly(string name)
        {
            return ReferenceOnlyShaders.Contains(GetShaderEnum(name));
        }
        
        public static bool HasURPSupport(string name)
        {
            return !ShadersWithNoURPSupport.Contains(GetShaderEnum(name));
        }
    }
}
