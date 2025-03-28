using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public class URPMaterialConverter : EditorWindow
{
    [MenuItem("Tools/Convert to URP Lit")]
    static void ConvertToURPLit()
    {
        Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.DeepAssets);
        if (selectedMaterials.Length == 0)
        {
            Debug.LogWarning("No materials selected!");
            return;
        }

        Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLitShader == null)
        {
            Debug.LogError("URP/Lit shader not found. Install URP first!");
            return;
        }

        foreach (Material mat in selectedMaterials)
        {
            ConvertMaterial(mat, urpLitShader);
        }
    }

    static void ConvertMaterial(Material mat, Shader urpShader)
    {
        Undo.RecordObject(mat, "Convert to URP Lit");
        
        // Change shader and set basic properties
        mat.shader = urpShader;

        // Z-fighting fix: Force Surface Type refresh
        mat.SetFloat("_Surface", 1);  // Temporary Transparent
        mat.SetFloat("_Surface", 0);  // Set back to Opaque
        mat.SetFloat("_Smoothness", 0.5f);
        mat.SetFloat("_OcclusionStrength", 0.5f);

        // Get all textures already assigned to the material
        List<Texture> existingTextures = new List<Texture>();
        if (mat.GetTexture("_BaseMap") != null) existingTextures.Add(mat.GetTexture("_BaseMap"));
        if (mat.GetTexture("_MetallicGlossMap") != null) existingTextures.Add(mat.GetTexture("_MetallicGlossMap"));
        if (mat.GetTexture("_SpecGlossMap") != null) existingTextures.Add(mat.GetTexture("_SpecGlossMap"));
        if (mat.GetTexture("_BumpMap") != null) existingTextures.Add(mat.GetTexture("_BumpMap"));
        if (mat.GetTexture("_OcclusionMap") != null) existingTextures.Add(mat.GetTexture("_OcclusionMap"));

        // Extract common prefix from connected textures or use material name
        string prefix = GetCommonTexturePrefix(existingTextures) ?? Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(mat));

        // Auto-map textures using the detected prefix
        AutoMapTexture(mat, "_BaseMap", new[] { 
            "_DIFF", "_Albedo", "_BaseMap", "_Diffuse", 
            "_base_color", "_Base_Color", "_Base_color", 
            "_BaseColor", "_ColorMap", "_Color" 
        }, prefix);

        AutoMapTexture(mat, "_MetallicGlossMap", new[] { "_Metallic", "_MetallicSmoothness", "_MS" }, prefix);
        AutoMapTexture(mat, "_SpecGlossMap", new[] { "_SPEC", "_spec", "_specular", "_SPECULAR" }, prefix);
        AutoMapTexture(mat, "_BumpMap", new[] { "_Normal", "_NRM", "_NORM" }, prefix);
        AutoMapTexture(mat, "_OcclusionMap", new[] { "_AO", "_Occlusion" }, prefix);

        // Determine workflow mode (Metallic priority)
        bool hasMetallic = mat.GetTexture("_MetallicGlossMap") != null;
        bool hasSpecular = mat.GetTexture("_SpecGlossMap") != null;

        if (hasMetallic)
        {
            mat.SetFloat("_WorkflowMode", 1); // Metallic
            if (hasSpecular)
            {
                mat.SetTexture("_SpecGlossMap", null);
                Debug.LogWarning($"{mat.name}: Both maps found - using Metallic workflow");
            }
        }
        else if (hasSpecular)
        {
            mat.SetFloat("_WorkflowMode", 0); // Specular
        }
        else
        {
            mat.SetFloat("_WorkflowMode", 1); // Default Metallic
        }

        // Transfer color properties
        if (mat.HasProperty("_Color"))
            mat.SetColor("_BaseColor", mat.GetColor("_Color"));

        // Normal map scale
        if (mat.HasProperty("_BumpScale"))
            mat.SetFloat("_NormalScale", mat.GetFloat("_BumpScale"));

        Debug.Log($"Converted {mat.name} to URP/Lit", mat);
    }

    static string GetCommonTexturePrefix(List<Texture> textures)
    {
        if (textures == null || textures.Count == 0) return null;

        var prefixes = textures
            .Where(t => t != null)
            .Select(t => Path.GetFileNameWithoutExtension(t.name))
            .Select(name => 
            {
                int lastUnderscore = name.LastIndexOf('_');
                return lastUnderscore > 0 ? 
                    (prefix: name.Substring(0, lastUnderscore), suffix: name.Substring(lastUnderscore + 1)) : 
                    (prefix: name, suffix: "");
            })
            .GroupBy(p => p.prefix)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return prefixes?.Key;
    }

    static void AutoMapTexture(Material mat, string property, string[] suffixes, string prefix)
    {
        if (mat.GetTexture(property) != null) return;

        string materialPath = AssetDatabase.GetAssetPath(mat);
        string directory = Path.GetDirectoryName(materialPath);
        string[] textureGUIDs = AssetDatabase.FindAssets("t:Texture2D", new[] { directory });

        foreach (string guid in textureGUIDs)
        {
            string texturePath = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            string textureName = Path.GetFileNameWithoutExtension(texturePath);

            foreach (string suffix in suffixes)
            {
                if (textureName.Equals($"{prefix}{suffix}", System.StringComparison.OrdinalIgnoreCase))
                {
                    mat.SetTexture(property, texture);
                    return;
                }
            }
        }
    }
}