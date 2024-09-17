using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oculus.Avatar2
{
    public class OvrAvatarShaderDeprecationManager
    {
        private bool IsURPEnabled()
        {
            if (GraphicsSettings.renderPipelineAsset == null)
            {
                return false;
            }

            // Replace with the actual type name if different
            return GraphicsSettings.renderPipelineAsset.GetType().ToString().Contains("UniversalRenderPipelineAsset");
        }
        
        private HashSet<string> _shaderNamesRegistered = new HashSet<string>();

        private const string scope = "OvrAvatarShaderDeprecationManager";
        
        public void PrintDeprecationWarningIfNecessary(Shader shader)
        {
            var name = shader.name;
            
            // Register the shader name so we're not spamming the console multiple times when shader is accessed.
            if (!_shaderNamesRegistered.Contains(name))
            {
                _shaderNamesRegistered.Add(name);
                const string extraInfo =
                    "We recommend using 'Avatar-Meta' shader. Use AvatarSdkManagerMeta prefab in your scene to use this shader.";

                if (!OvrAvatarShaderNameUtils.IsKnown(name))
                {
                    OvrAvatarLog.LogWarning($"Shader '{name}' is not known to the AvatarSDK. {extraInfo}", scope);
                    return;
                }

                if (IsURPEnabled() && !OvrAvatarShaderNameUtils.HasURPSupport(name))
                {
                    OvrAvatarLog.LogError($"Shader '{name}' does not support URP. {extraInfo}", scope);
                    return;
                }

                if (OvrAvatarShaderNameUtils.IsDeprecated(name))
                {
                    OvrAvatarLog.LogWarning($"Shader '{name}' has been deprecated. {extraInfo}", scope);
                    return;
                }

                if (OvrAvatarShaderNameUtils.IsReferenceOnly(name))
                {
                    OvrAvatarLog.LogWarning($"Shader '{name}' should be used for reference and debugging purposes only. {extraInfo}", scope);
                }
            }
        }
    }
}
