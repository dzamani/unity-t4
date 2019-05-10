using UnityEngine;
using UnityEditor;

namespace NDY
{
    public class TemplatePostProcessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string str in importedAssets)
            {
                if (str.EndsWith(".tt"))
                    TemplateUtility.NeedRebuild = true;
            }
        }

        public static void OnGeneratedCSProjectFiles()
        {
            TemplateUtility.FixProjectSolution();
        }
    }
}