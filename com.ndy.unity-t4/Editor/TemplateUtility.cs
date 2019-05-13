using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace NDY
{
    [InitializeOnLoad]
    public static class TemplateUtility
    {
        private const string kTemplateExtension = "tt";
        private const string kTemplateExtensionWithDot = ".tt";
        private const string kCSExtensionWithDot = ".cs";
        private const string kCSProjExtension = "*.csproj";
        private const string kMSBuildPath = "MSBuildPath";
        private const string kHasTextTransformPath = "HasTextTransformPath";
        private const string kDisableAutomaticTransformTask = "DisableAutomaticTransformTask";
        private const string kTextTransformErrorMesssage = "You do not have T4 executable installed so code generation won't be run.";
        private const string kTemplateFileRegex = "<None Include=\"(?'path'.+.tt)\" />";
        private const string kTemplateFileRegexGroupName = "path";
        private const string kTemplateToolRegex = "<Import Project=\"" + @"\$\(MSBuildToolsPath\)\\Microsoft\.CSharp\.targets" + "\" />(?'autoCompile'.*<Import Project=\""
                    + @"\$\(VSToolsPath\)\\TextTemplating\\Microsoft\.TextTemplating\.targets" + "\" />){0,1}";
        private const string kTemplateToolCSProjFix = "<Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />\n"
                    + "\t<!-- Optionally make the import portable across VS versions -->\n" + "\t<PropertyGroup>\n"
                    + "\t\t<VisualStudioVersion Condition=\"'$(VisualStudioVersion)' == ''\">16.0</VisualStudioVersion>\n"
                    + "\t\t<VSToolsPath Condition=\"'$(VSToolsPath)' == ''\">$(MSBuildExtensionsPath32)\\Microsoft\\VisualStudio\\v$(VisualStudioVersion)</VSToolsPath>\n"
                    + "\t</PropertyGroup>\n" + "\t<PropertyGroup>\n" + "\t\t<TransformOnBuild>true</TransformOnBuild>\n" + "\t</PropertyGroup>\n"
                    + "\t<Import Project=\"$(VSToolsPath)\\TextTemplating\\Microsoft.TextTemplating.targets\" />";

        public const string kTemplatesRoot = "Packages/com.ndy.unity-t4/Editor";

        public static bool NeedRebuild = false;

        static TemplateUtility()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= CheckEditorSettings;
            AssemblyReloadEvents.beforeAssemblyReload += CheckEditorSettings;
            AssemblyReloadEvents.beforeAssemblyReload -= FixProjectSolution;
            AssemblyReloadEvents.beforeAssemblyReload += FixProjectSolution;
        }

        private static void CheckEditorSettings()
        {
            var userExtensions = EditorSettings.projectGenerationUserExtensions;
            bool found = false;

            for (int i = 0; i < userExtensions.Length; i++)
            {
                if (userExtensions[i] == kTemplateExtension)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var tmpExtensions = new string[userExtensions.Length + 1];
                userExtensions.CopyTo(tmpExtensions, 0);
                tmpExtensions[tmpExtensions.Length - 1] = kTemplateExtension;
                EditorSettings.projectGenerationUserExtensions = tmpExtensions;
            }
            else
            {
                AssemblyReloadEvents.beforeAssemblyReload -= CheckEditorSettings;
            }
        }

        [MenuItem("Assets/Create/NDY/Default Template T4", priority = 101)]
        public static void CreateDefaultTemplateT4File()
        {
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile($"{kTemplatesRoot}/DefaultRuntimeTemplateT4.txt", "NewDefaultTemplateT4.tt");
        }

        [MenuItem("NDY/Template T4/Run transform task on all projects", priority = 0, validate = true)]
        public static bool RunTransformTaskValidate()
        {
#if UNITY_EDITOR_OSX
            return false;
#else
            return true;
#endif
        }

        [MenuItem("NDY/Template T4/Run transform task on all projects", priority = 0)]
        public static void RunTransformTask()
        {
            RunTransformTaskOnWholeProjectSolution();
        }

        public static void RunTransformTaskOnWholeProjectSolution(bool userCSProjOnly = true)
        {
            var msbuildPath = GetMSBuildPath();
            if (msbuildPath == null)
            {
                UnityEngine.Debug.LogWarning(kTextTransformErrorMesssage);
                return;
            }

            string[] files = Directory.GetFiles(Directory.GetParent(Application.dataPath).FullName, kCSProjExtension);
            for (int i = 0; i < files.Length; i++)
            {
                if (userCSProjOnly && !files[i].Contains("Assembly-CSharp"))
                    continue;

                RunTransformTaskOnProject(msbuildPath, files[i]);
            }
        }

        public static void RunTransformTaskOnProject(string msbuildPath, string csProjectPath)
        {
            Process process = new Process();
            process.StartInfo.FileName = msbuildPath;
            process.StartInfo.Arguments = csProjectPath + " /t:TransformAll";
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.Start();
            process.WaitForExit();
        }

        [MenuItem("NDY/Template T4/Disable automatic transform task", validate = true)]
        public static bool DisableAutomaticTransformTaskValidation()
        {
#if !UNITY_EDITOR_OSX
            return !EditorPrefs.HasKey(kDisableAutomaticTransformTask) || !EditorPrefs.GetBool(kDisableAutomaticTransformTask);
#else
            return false;
#endif
        }

        [MenuItem("NDY/Template T4/Disable automatic transform task", validate = false, priority = 12)]
        public static void DisableAutomaticTransformTask()
        {
            EditorPrefs.SetBool(kDisableAutomaticTransformTask, true);
        }

        [MenuItem("NDY/Template T4/Enable automatic transform task", validate = true)]
        public static bool EnableAutomaticTransformTaskValidation()
        {
#if !UNITY_EDITOR_OSX
            return !DisableAutomaticTransformTaskValidation();
#else
            return false;
#endif
        }

        [MenuItem("NDY/Template T4/Enable automatic transform task", validate = false, priority = 11)]
        public static void EnableAutomaticTransformTask()
        {
            EditorPrefs.SetBool(kDisableAutomaticTransformTask, false);
        }

        [MenuItem("NDY/Template T4/Fix project solution", priority = 0)]
        public static void FixProjectSolution()
        {
            if (!EditorPrefs.HasKey(kHasTextTransformPath))
            {
#if UNITY_EDITOR_OSX
                var path = "/Applications/Visual Studio.app/Contents/Resources/lib/monodevelop/AddIns/MonoDevelop.TextTemplating";
                if (!Directory.Exists(path) || GetMSBuildPath() == null)
                {
                    UnityEngine.Debug.LogWarning(kTextTransformErrorMesssage);
                    return;
                }
                EditorPrefs.SetBool(kHasTextTransformPath, true);
#else
                var pathToProgramFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                string vsPath = Path.Combine(pathToProgramFiles, "Microsoft Visual Studio");
                var directories = Directory.GetFiles(vsPath, "TextTransform.exe", SearchOption.AllDirectories);
                if (directories == null || directories.Length == 0)
                {
                    UnityEngine.Debug.LogWarning(kTextTransformErrorMesssage);
                    return;
                }

                EditorPrefs.SetString(kHasTextTransformPath, directories[directories.Length - 1]);
#endif
            }

            var msbuildPath = GetMSBuildPath();
            if (msbuildPath == null)
            {
                UnityEngine.Debug.LogWarning(kTextTransformErrorMesssage);
                return;
            }

            /*if (!EditorPrefs.HasKey(kTextTransformPath))
            {
                var pathToProgramFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                string vsPath = Path.Combine(pathToProgramFiles, "Microsoft Visual Studio");
                var directories = Directory.GetFiles(vsPath, "MSBuild.exe", SearchOption.AllDirectories);
                if (directories == null || directories.Length == 0)
                {
                    UnityEngine.Debug.LogWarning(kTextTransformErrorMesssage);
                    return;
                }

                EditorPrefs.SetString(kTextTransformPath, directories[directories.Length - 1]);
            }*/

            string[] files = Directory.GetFiles(Directory.GetParent(Application.dataPath).FullName, kCSProjExtension);
            for (int i = 0; i < files.Length; i++)
            {
                string fileContent = File.ReadAllText(files[i]);
                Regex templateRegex = new Regex(kTemplateFileRegex);
                var templateMatches = templateRegex.Matches(fileContent);

                for (int j = 0; j < templateMatches.Count; j++)
                {
                    var templateMatch = templateMatches[j];
                    if (templateMatch.Success)
                    {
                        var filePath = templateMatch.Groups[kTemplateFileRegexGroupName].Value;
                        var csFilePath = filePath.Replace(kTemplateExtensionWithDot, kCSExtensionWithDot);
                        var csFileName = Path.GetFileNameWithoutExtension(filePath) + kCSExtensionWithDot;
                        StringBuilder newTemplateDataStr = new StringBuilder("<Content Include=\"" + filePath + "\">\n");
                        newTemplateDataStr.Append("\t\t<Generator>TextTemplatingFilePreprocessor</Generator>\n");
                        newTemplateDataStr.Append("\t\t<LastGenOutput>" + csFileName + "</LastGenOutput>\n");
                        newTemplateDataStr.Append("\t</Content>\n");
                        fileContent = fileContent.Replace(templateMatch.Value, newTemplateDataStr.ToString());

                        Regex csRegex = new Regex("<Compile Include=\"" + csFilePath.Replace("\\", "\\\\") + "\"(?> />|>.*</Compile>)");
                        var csMatch = csRegex.Match(fileContent);
                        if (csMatch.Success)
                        {
                            StringBuilder newCSDataStr = new StringBuilder("<Compile Include=\"" + csFilePath + "\">\n");
                            newCSDataStr.Append("\t\t<AutoGen>True</AutoGen>\n");
                            newCSDataStr.Append("\t\t<DesignTime>True</DesignTime>\n");
                            newCSDataStr.Append("\t\t<DependentUpon>" + Path.GetFileName(filePath) + "</DependentUpon>\n");
                            newCSDataStr.Append("\t</Compile>\n");
                            fileContent = fileContent.Replace(csMatch.Value, newCSDataStr.ToString());
                        }
                    }
                }

#if !UNITY_EDITOR_OSX
                var templateToolRegex = new Regex(kTemplateToolRegex, RegexOptions.Singleline);
                var templateToolMatch = templateToolRegex.Match(fileContent);

                if (templateToolMatch.Success)
                {
                    fileContent = fileContent.Replace(templateToolMatch.Value, kTemplateToolCSProjFix);
                }
#endif

                File.WriteAllText(files[i], fileContent);

#if !UNITY_EDITOR_OSX
                if (templateMatches.Count > 0 && NeedRebuild)
                {
                    RunTransformTaskOnProject(msbuildPath, files[i]);
                    AssetDatabase.Refresh();
                }
#endif

            }

            NeedRebuild = false;
        }

        private static string GetMSBuildPath()
        {
            if (!EditorPrefs.HasKey(kMSBuildPath))
            {
                string pathToMsbuild;
#if !UNITY_EDITOR_OSX
                var pathToProgramFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                string vsPath = Path.Combine(pathToProgramFiles, "Microsoft Visual Studio");
                var directories = Directory.GetFiles(vsPath, "MSBuild.exe", SearchOption.AllDirectories);
                if (directories == null || directories.Length == 0)
                {
                    UnityEngine.Debug.LogWarning(kTextTransformErrorMesssage);
                    return null;
                }
                pathToMsbuild = directories[directories.Length - 1];
#else
                pathToMsbuild = "msbuild";
#endif
                EditorPrefs.SetString(kMSBuildPath, pathToMsbuild);
            }

            return EditorPrefs.GetString(kMSBuildPath);
        }
    }
}