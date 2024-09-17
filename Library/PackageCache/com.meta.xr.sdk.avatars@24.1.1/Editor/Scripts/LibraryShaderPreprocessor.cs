using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oculus.Avatar2
{
    /// Some Library shader features should be enabled or disabled based on the build target.
    /// This preprocessor discards shader passes with keywords that don't match requirements for the current target.
    public class LibraryShaderPreprocessor : IPreprocessShaders
    {
        private static readonly BuildTarget[] ExternalBuffersUnsupportedTargets =
        {
        };

        private readonly ShaderKeyword _externalBuffersDisabledKeyword, _externalBuffersEnabledKeyword;

        public LibraryShaderPreprocessor()
        {
            _externalBuffersDisabledKeyword = new ShaderKeyword("EXTERNAL_BUFFERS_DISABLED");
            _externalBuffersEnabledKeyword = new ShaderKeyword("EXTERNAL_BUFFERS_ENABLED");
        }

        public int callbackOrder => 0;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            bool isUnsupported = ExternalBuffersUnsupportedTargets.Contains(EditorUserBuildSettings.activeBuildTarget);
            for (int i = data.Count - 1; i >= 0; --i)
            {
                if ((isUnsupported && data[i].shaderKeywordSet.IsEnabled(_externalBuffersEnabledKeyword)) ||
                    (!isUnsupported && data[i].shaderKeywordSet.IsEnabled(_externalBuffersDisabledKeyword)))
                {
                    data.RemoveAt(i);
                }
            }
        }
    }
}
