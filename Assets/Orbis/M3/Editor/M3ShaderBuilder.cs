using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Orbis.M3.Editor
{
    /// <summary>
    /// Imports the committed editable Shader Graph assets, creates their runtime materials,
    /// and synchronously compiles every material pass. No Python/runtime graph generation is needed.
    /// </summary>
    public static class M3ShaderBuilder
    {
        const string ShaderFolder = "Assets/Orbis/M3/Resources/M3/Shaders/";
        const string MaterialFolder = "Assets/Orbis/M3/Resources/M3/Materials/";
        static readonly string[] Effects = { "Dissolve", "Outline", "WeaponTrail", "SwirlRing", "CrystalShield" };

        [MenuItem("Orbis/M3/Build Effect Materials")]
        public static void Build()
        {
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (string effect in Effects)
            {
                string shaderPath = ShaderFolder + effect + ".shadergraph";
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (shader == null)
                    throw new InvalidOperationException("Shader Graph failed to import: " + shaderPath);
                string materialPath = MaterialFolder + effect + ".mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader) { name = effect };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                else
                    material.shader = shader;

                // Spec 03 Fire palette is the neutral preview default; runtime supplies each element palette.
                material.SetColor("_PrimaryColor", new Color32(0xFF, 0x5A, 0x1F, 0xFF));
                material.SetColor("_SecondaryColor", new Color32(0xFF, 0xD1, 0x66, 0xFF));
                // Progress is normalized lifetime: zero visible, one gone. Spawn plays it in reverse.
                material.SetFloat("_Progress", 0f);
                if (material.HasProperty("_Thickness"))
                    material.SetFloat("_Thickness", 0.03f); // Unspecified art default, object-space shell expansion.
                EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            Validate();
        }

        [MenuItem("Orbis/M3/Validate Effect Shaders")]
        public static void Validate()
        {
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            try
            {
                // Editor first-frame async compilation can otherwise capture the placeholder shader.
                ShaderUtil.allowAsyncCompilation = false;
                foreach (string effect in Effects)
                {
                    string graphPath = ShaderFolder + effect + ".shadergraph";
                    if (!File.Exists(graphPath))
                        throw new InvalidOperationException("Missing editable Shader Graph: " + graphPath);
                    Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(graphPath);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + effect + ".mat");
                    if (shader == null || material == null || material.shader != shader)
                        throw new InvalidOperationException("Missing or mismatched M3 shader/material: " + effect);
                    foreach (string property in new[] { "_PrimaryColor", "_SecondaryColor", "_Progress" })
                        if (!material.HasProperty(property))
                            throw new InvalidOperationException(effect + " is missing material property " + property);
                    if (effect == "Outline" && !material.HasProperty("_Thickness"))
                        throw new InvalidOperationException("Outline is missing _Thickness.");
                    for (int pass = 0; pass < material.passCount; pass++)
                        ShaderUtil.CompilePass(material, pass);
                    var errors = ShaderUtil.GetShaderMessages(shader)
                        .Where(message => message.severity.ToString() == "Error")
                        .Select(message => message.message + " (" + message.file + ":" + message.line + ")")
                        .ToArray();
                    if (errors.Length > 0)
                        throw new InvalidOperationException(effect + " shader compilation failed:\n" + string.Join("\n", errors));
                    Debug.Log("M3 shader compiled: " + shader.name + " (" + material.passCount + " passes)");
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = previousAsync;
            }
        }
    }
}
