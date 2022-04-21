using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace BrunoMikoski.SmartSymbolicate
{
    public class SmartSymbolicateWindow : EditorWindow
    {
        private const string ADDR2LINE_32_PATH = @"Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\arm-linux-androideabi-4.9\prebuilt\windows-x86_64\bin\arm-linux-androideabi-addr2line.exe";
        private const string ADDR2LINE_64_PATH = @"Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\aarch64-linux-android-4.9\prebuilt\windows-x86_64\bin\aarch64-linux-android-addr2line.exe";

        private const string UNITY_PATH_STORAGE_KEY = "SmartSymbolicate.UnityPathStorageKey";
        private const string PROJECT_SYMBOLS_PATH_STORAGE_KEY = "SmartSymbolicate.ProjectSymbolsStorageKey";

        private const string LIB_IL2CPP_NAME = "libil2cpp.so";
        private const string LIB_IL2CPP_DEBUG_NAME = "libil2cpp.dbg.so";
        private const string LIB_IL2CPP_SYM_NAME = "libil2cpp.sym.so";
        private const string LIB_UNITY_NAME = "libunity.sym.so";
        
        private class AddressesData
        {
            private string libName;
            public string LibName => libName;

            private string memoryAddress;
            public string MemoryAddress => memoryAddress;

            public AddressesData(string targetLib, string matchValue)
            {
                libName = targetLib;
                memoryAddress = matchValue;
            }

            public override string ToString()
            {
                return $"[{libName}] :: {memoryAddress}";
            }

            public string GetLibraryDisplayName()
            {
                if (string.Equals(libName, "libunity", StringComparison.Ordinal))
                    return "<color=yellow>Unity Engine Code </color>" ;

                if (string.Equals(libName, "libil2cpp", StringComparison.Ordinal))
                    return "<color=green>Project Code</color>";

                return $"<color=red>{libName}</color>";
            }
        }
        
        private enum ReleaseType
        {
            Release = 0,
            Development = 1
        }
        
        private enum ScriptingBackendType
        {
            il2cpp = 0,
            mono = 1
        }
        
        private enum CPUType
        {
            arm64_v8a = 0,
            armeabi_v7a = 1
        }

        private string unityHubPath
        {
            get => EditorPrefs.GetString(UNITY_PATH_STORAGE_KEY, @"C:/Program Files/Unity/Hub/Editor");
            set => EditorPrefs.SetString(UNITY_PATH_STORAGE_KEY, value);
        }

        private string projectSymbolsPath
        {
            get => EditorPrefs.GetString(PROJECT_SYMBOLS_PATH_STORAGE_KEY, string.Empty);
            set => EditorPrefs.SetString(PROJECT_SYMBOLS_PATH_STORAGE_KEY, value);
        }
        
        private string crashInput;
        private string unityVersion;


        private ReleaseType releaseType = ReleaseType.Release;
        private ScriptingBackendType scriptingBackendType = ScriptingBackendType.il2cpp;
        private CPUType cpuType = CPUType.arm64_v8a;

        private string[] releaseTypeDisplayNames = Array.Empty<string>();
        private string[] scriptingBackendTypeNames = Array.Empty<string>();
        private string[] cpuTypeNames = Array.Empty<string>();
        private string[] availableUnityVersions = Array.Empty<string>();
        
        
        private bool validUnityHubFolder;
        private string output;
        private Vector2 outputScrollView;


        private GUIStyle outputTextFieldStyle;
        private Vector2 inputScrollView;
        private bool isMissingUnityVersion;
        private string desiredUnityVersion;
        private bool isMissingCPUType;
        private string desiredCPUType;
        private List<AddressesData> addressesDatas;

        [MenuItem("Tools/Open SmartSymbolicate")]
        public static void ShowExample()
        {
            SmartSymbolicateWindow wnd = GetWindow<SmartSymbolicateWindow>();
            wnd.titleContent = new GUIContent("SmartSymbolicate");
        }

        private void OnEnable()
        {
            GenerateDisplayNames();
            ValidateUnityHubPath(unityHubPath);
        }

        private void GenerateDisplayNames()
        {
            releaseTypeDisplayNames = Enum.GetNames(typeof(ReleaseType));
            scriptingBackendTypeNames = Enum.GetNames(typeof(ScriptingBackendType));
            cpuTypeNames = Enum.GetNames(typeof(CPUType));

            for (int i = 0; i < cpuTypeNames.Length; i++)
                cpuTypeNames[i] = cpuTypeNames[i].Replace("_", "-");
        }

        private void OnGUI()
        {
            if (outputTextFieldStyle == null)
                outputTextFieldStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true, richText = true };

            DrawPaths();
            DrawInput();
            DrawSettings();
            DrawOutput();
        }

        private void DrawOutput()
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField("Output", EditorStyles.foldoutHeader);
            EditorGUILayout.Separator();


            outputScrollView = EditorGUILayout.BeginScrollView(outputScrollView, false, true, GUILayout.ExpandHeight(true));
            output = EditorGUILayout.TextArea(output, outputTextFieldStyle, GUILayout.ExpandHeight(true),
                GUILayout.ExpandWidth(true));
            EditorGUILayout.EndScrollView();

            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(crashInput) || string.IsNullOrEmpty(unityHubPath) || string.IsNullOrEmpty(projectSymbolsPath));
            if (GUILayout.Button("Parse"))
            {
                ParseInput();
            }
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.EndVertical();
        }

        private void ParseInput()
        {
            GatherDataFromInput();
            if (addressesDatas.Count == 0)
            {
                Debug.LogError("Failed to find any useful memory address on input");
                return;
            }
            
            string targetADDR2 = GetTargetAddr2line();
            if (!File.Exists(targetADDR2))
            {
                Debug.LogError($"Failed to find ADDR2Line at path {targetADDR2}");
                return;
            }

            StringBuilder parsedResults = new StringBuilder();

            EditorUtility.DisplayProgressBar("Processing Symbols", "Start", 0);
            for (int i = 0; i < addressesDatas.Count; i++)
            {
                AddressesData addressesData = addressesDatas[i];

                EditorUtility.DisplayProgressBar("Processing Symbols", $"Processing {addressesData.MemoryAddress}", (float)i / addressesDatas.Count);

                if (!TryGetLibPath(addressesData.LibName, out string knowPath))
                {
                    parsedResults.AppendLine($"<color=red>Unknow lib named {addressesData.LibName} :: {addressesData.MemoryAddress}</color>");
                    continue;
                }

                if (!File.Exists(knowPath))
                {
                    parsedResults.AppendLine($"<color=red>Failed to find lib {addressesData.LibName} at Path: {knowPath}</color>");
                    continue;
                }

                using (System.Diagnostics.Process process = new System.Diagnostics.Process())
                {
                    process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    process.StartInfo.FileName = targetADDR2;
                    process.StartInfo.Arguments = $"-f -C -e \"{knowPath}\" {addressesData.MemoryAddress}";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardInput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.Start();
                    parsedResults.AppendLine($"<b>{addressesData.GetLibraryDisplayName()}</b> [<i>{addressesData.MemoryAddress}</i>] => {process.StandardOutput.ReadToEnd()}");
                    string error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(error))
                    {
                        parsedResults.AppendLine($"{addressesData} => {error}");
                    }

                    process.WaitForExit();
                }
            }

            output = parsedResults.ToString();
            EditorUtility.ClearProgressBar();
        }
        
        private bool TryGetLibPath(string targetLibName, out string libKnowPath)
        {
            if (targetLibName.Equals("libunity", StringComparison.Ordinal))
            {
                string targetPath = Path.Combine(unityHubPath, unityVersion);
                targetPath = Path.Combine(targetPath, @"Editor\Data\PlaybackEngines\AndroidPlayer\Variations");

                targetPath = Path.Combine(targetPath, scriptingBackendTypeNames[(int)releaseType]);
                targetPath = Path.Combine(targetPath, releaseTypeDisplayNames[(int)releaseType]);
                targetPath = Path.Combine(targetPath, "Symbols");
                targetPath = Path.Combine(targetPath, cpuTypeNames[(int)cpuType]);
                targetPath = Path.Combine(targetPath, LIB_UNITY_NAME);
                libKnowPath = targetPath;
                return true;
            }

            if (targetLibName.Equals("libil2cpp", StringComparison.Ordinal))
            {
                string targetPath = projectSymbolsPath;
                targetPath = Path.Combine(targetPath, cpuTypeNames[(int)cpuType]);
                
                string debugTargetPath = Path.Combine(targetPath, LIB_IL2CPP_DEBUG_NAME);
                if (File.Exists(debugTargetPath))
                {
                    targetPath = debugTargetPath;
                }
                else
                {
                    string symTargetPath = Path.Combine(targetPath, LIB_IL2CPP_SYM_NAME);
                    if (File.Exists(symTargetPath))
                    {
                        targetPath = symTargetPath;
                    }
                    else
                    {
                        targetPath = Path.Combine(targetPath, LIB_IL2CPP_NAME);
                    }
                }
                
                libKnowPath = targetPath;
                return true;
            }

            libKnowPath = string.Empty;
            return false;
        }

        private string GetTargetAddr2line()
        {
            if (cpuType == CPUType.arm64_v8a)
                return Path.Combine(Path.Combine(unityHubPath, unityVersion), ADDR2LINE_64_PATH);

            return Path.Combine(Path.Combine(unityHubPath, unityVersion), ADDR2LINE_32_PATH);
        }

        private void GatherDataFromInput()
        {
            addressesDatas = new List<AddressesData>();
            Regex addressesRegex = new Regex("0[xX][0-9a-fA-F]+");

            string[] lines = crashInput.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                Match match = addressesRegex.Match(line);
                if (!match.Success)
                    continue;

                int libStartIndex = line.IndexOf(@"at ", StringComparison.Ordinal);
                if (libStartIndex == -1)
                    continue;

                int libEndIndex = line.IndexOf(@".0x", StringComparison.Ordinal);
                if (libEndIndex == -1)
                    continue;

                int startIndex = libStartIndex + 3;
                string targetLib = line.Substring(startIndex, libEndIndex - startIndex);

                AddressesData addressesData = new AddressesData(targetLib, match.Value);
                addressesDatas.Add(addressesData);
            }
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField("Settings", EditorStyles.foldoutHeader);
            EditorGUILayout.Separator();
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            
            EditorGUILayout.BeginVertical("HelpBox", GUILayout.Width(100), GUILayout.Height(68));
            EditorGUILayout.LabelField("Unity Version", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();

            if (validUnityHubFolder)
            {
                if (isMissingUnityVersion)
                {
                    EditorGUILayout.HelpBox($"Crash Reports needs Unity version {desiredUnityVersion}",
                        MessageType.Error);
                }
                else
                {
                    int selectedUnityVersion = Mathf.Clamp(Array.IndexOf(availableUnityVersions, unityVersion), 0,
                        availableUnityVersions.Length);
                    unityVersion = availableUnityVersions[EditorGUILayout.Popup(selectedUnityVersion, availableUnityVersions)];
                    
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Invalid Unity Hub Folder", MessageType.Error);
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.BeginVertical("HelpBox", GUILayout.Width(100));
            EditorGUILayout.LabelField("Release Type", EditorStyles.boldLabel);
            releaseType = (ReleaseType)GUILayout.SelectionGrid((int)releaseType, releaseTypeDisplayNames, 1, EditorStyles.radioButton);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("HelpBox", GUILayout.Width(100));
            EditorGUILayout.LabelField("Scripting Backend", EditorStyles.boldLabel);
            scriptingBackendType = (ScriptingBackendType)GUILayout.SelectionGrid((int)scriptingBackendType, scriptingBackendTypeNames, 1, EditorStyles.radioButton); 
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("HelpBox", GUILayout.Width(100));
            EditorGUILayout.LabelField("CPU", EditorStyles.boldLabel);
            if (!isMissingCPUType)
            {
                cpuType = (CPUType)GUILayout.SelectionGrid((int)cpuType, cpuTypeNames, 1, EditorStyles.radioButton);
            }
            else
            {
                EditorGUILayout.HelpBox($"Missing CPU Type {desiredCPUType}", MessageType.Error);
            }
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndHorizontal();


            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawInput()
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField("Crash Input", EditorStyles.foldoutHeader);
            EditorGUILayout.Separator();

            inputScrollView = EditorGUILayout.BeginScrollView(inputScrollView, false, true, GUILayout.Height(200));
            EditorGUI.BeginChangeCheck();
            crashInput = EditorGUILayout.TextArea(crashInput, GUILayout.ExpandHeight(true));
            if (EditorGUI.EndChangeCheck())
                TryGetInformationFromCrashInput();
            
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void TryGetInformationFromCrashInput()
        {
            if (string.IsNullOrEmpty(crashInput))
            {
                isMissingCPUType = false;
                isMissingUnityVersion = false;
                return;
            }
            
            TryGetScriptingBackendFromCrashInput();
            TryGetUnityVersionFromCrashInput();
            TryGetCPUFromCrashInput();
            TryGetBuildType();
        }

        private void TryGetBuildType()
        {
            Match buildTypeMatch = Regex.Match(crashInput, pattern: @"(?<=Build type ')(.*?)[^']*");
            if (buildTypeMatch.Success)
            {
                int targetBuildType = Array.IndexOf(releaseTypeDisplayNames, buildTypeMatch.Value);
                if (targetBuildType != -1)
                {
                    releaseType = (ReleaseType)targetBuildType;
                }
            }
        }

        private void TryGetCPUFromCrashInput()
        {
            Match cpuMatch = Regex.Match(crashInput, pattern: @"(?<=CPU ')(.*?)[^']*");
            if (cpuMatch.Success)
            {
                int targetCPUIndex = Array.IndexOf(cpuTypeNames, cpuMatch.Value);
                if (targetCPUIndex == -1)
                {
                    isMissingCPUType = true;
                    desiredCPUType = cpuMatch.Value;
                }
                else
                {
                    cpuType = (CPUType)targetCPUIndex;
                    isMissingCPUType = false;
                }
            }
            else
            {
                isMissingCPUType = false;
            }
        }

        private void TryGetUnityVersionFromCrashInput()
        {
            Match unityVersionMatch = Regex.Match(crashInput, pattern: @"(?<=Version ')(.*?)[^ ']*");
            if (unityVersionMatch.Success)
            {
                int targetUnityVersionIndex = Array.IndexOf(availableUnityVersions, unityVersionMatch.Value);
                if (targetUnityVersionIndex == -1)
                {
                    isMissingUnityVersion = true;
                    desiredUnityVersion = unityVersionMatch.Value;
                }
                else
                {
                    isMissingUnityVersion = false;
                    unityVersion = availableUnityVersions[targetUnityVersionIndex];
                }
            }
            else
            {
                isMissingUnityVersion = false;
                desiredUnityVersion = "Unknow";
            }
        }

        private void TryGetScriptingBackendFromCrashInput()
        {
            Match scriptingBackendNameMatch = Regex.Match(crashInput, @"(?<=\Scripting Backend ')(.*?)[^']*");
            if (scriptingBackendNameMatch.Success)
            {
                int newScriptingBackend = Array.IndexOf(scriptingBackendTypeNames, scriptingBackendNameMatch.Value);
                if (newScriptingBackend != -1)
                    scriptingBackendType = (ScriptingBackendType)newScriptingBackend;
            }
        }


        private void DrawPaths()
        {
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField("Paths", EditorStyles.foldoutHeader);
            EditorGUILayout.Separator();

            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string newUnityPath = EditorGUILayout.TextField("Unity Hub Path", unityHubPath);
            if (EditorGUI.EndChangeCheck())
                ValidateUnityHubPath(newUnityPath);

            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                newUnityPath = EditorUtility.OpenFolderPanel("Select Unity Hub Root Folder", unityHubPath, "");
                if (!string.Equals(newUnityPath, unityHubPath))
                    ValidateUnityHubPath(newUnityPath);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            projectSymbolsPath = EditorGUILayout.TextField("Project Symbols", projectSymbolsPath);

            if (GUILayout.Button("Browse", GUILayout.Width(70)))
            {
                projectSymbolsPath = EditorUtility.OpenFolderPanel("Select Unity Hub Root Folder", projectSymbolsPath, "");
            }

            EditorGUILayout.EndHorizontal();


            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void ValidateUnityHubPath(string targetUnityPath)
        {
            unityHubPath = targetUnityPath;

            if (!targetUnityPath.EndsWith(@"Hub/Editor"))
            {
                validUnityHubFolder = false;
                return;
            }
            
            string[] childDirectories = Directory.GetDirectories(targetUnityPath);
            availableUnityVersions = new string[childDirectories.Length];

            for (int i = 0; i < childDirectories.Length; i++)
                availableUnityVersions[i] = Path.GetFileName(childDirectories[i]);

            validUnityHubFolder = true;
        }
    }
}
