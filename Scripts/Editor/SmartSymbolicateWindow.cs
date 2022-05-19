using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BrunoMikoski.SmartSymbolicate
{
    public class SmartSymbolicateWindow : EditorWindow
    {
        private const string UNITY_PATH_STORAGE_KEY = "SmartSymbolicate.UnityPathStorageKey";
        private const string PROJECT_SYMBOLS_PATH_STORAGE_KEY = "SmartSymbolicate.ProjectSymbolsStorageKey";

        
        private const string LIB_IL2CPP_NAME = "libil2cpp";
        private const string LIB_UNITY_NAME = "libunity";

        private const string DEBUG_EXTENSION = "dbg.so";
        private const string SYM_EXTENSION = "sym.so";
        private const string DEFAULT_EXTENSION = "so";
        internal const string SMART_SYMBOLICATE_TITLE_WINDOW = "Smart Symbolicate";

        private static string Addr2Line32Path 
        {
            get
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                    return @"Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\arm-linux-androideabi-4.9\prebuilt\windows-x86_64\bin\arm-linux-androideabi-addr2line.exe";
                return @"PlaybackEngines/AndroidPlayer/NDK/toolchains/arm-linux-androideabi-4.9/prebuilt/darwin-x86_64/bin/arm-linux-androideabi-addr2line";
            }
        }

        private static string Addr2Line64Path 
        {
            get
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                    return @"Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\aarch64-linux-android-4.9\prebuilt\windows-x86_64\bin\aarch64-linux-android-addr2line.exe";
                return @"PlaybackEngines/AndroidPlayer/NDK/toolchains/aarch64-linux-android-4.9/prebuilt/darwin-x86_64/bin/aarch64-linux-android-addr2line";
            }
        }
        
        private static string DefaultUnityInstallationFolder
        {
            get
            {
                if (Application.platform == RuntimePlatform.OSXEditor)
                    return @"/Applications/Unity/Hub/Editor";
                return @"C:\Program Files\Unity\Hub\Editor";
            }
        }
        
        private static string UnityVariationsPath
        {
            get
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                    return @"Editor\Data\PlaybackEngines\AndroidPlayer\Variations";
                return @"PlaybackEngines/AndroidPlayer/Variations";
            }
        }
        

        private class AddressesData
        {
            private string[] libraryNames = Array.Empty<string>();
            public string[] LibraryNames => libraryNames;

            private string memoryAddress;
            public string MemoryAddress => memoryAddress;

            public AddressesData(string matchValue)
            {
                memoryAddress = matchValue;
            }

            public override string ToString()
            {
                return $"[{string.Join(",", libraryNames)}] :: {memoryAddress}";
            }

            public string GetLibraryDisplayName(int index)
            {
                if (string.Equals(libraryNames[index], "libunity", StringComparison.Ordinal))
                    return "<color=yellow>Unity Engine Code </color>";

                if (unknowLibs.Contains(libraryNames[index]))
                    return "<color=red>Project Code</color>";

                return $"<color=green>{libraryNames[index]}</color>";
            }

            public void SetLibs(params string[] targetLibs)
            {
                libraryNames = targetLibs;
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
        
        private enum SymbolsType
        {
            Auto,
            libil2cpp,
            libunity,
            All
        }

        private string unityHubPath
        {
            get => EditorPrefs.GetString(UNITY_PATH_STORAGE_KEY, DefaultUnityInstallationFolder);
            set => EditorPrefs.SetString(UNITY_PATH_STORAGE_KEY, value);
        }

        private string projectSymbolsPath
        {
            get => EditorPrefs.GetString(PROJECT_SYMBOLS_PATH_STORAGE_KEY, string.Empty);
            set => EditorPrefs.SetString(PROJECT_SYMBOLS_PATH_STORAGE_KEY, value);
        }
        
        private string crashInput;
        private string unityVersion;
        private string output;
        private List<AddressesData> addressesDatas;

        private ReleaseType releaseType = ReleaseType.Release;
        private ScriptingBackendType scriptingBackendType = ScriptingBackendType.il2cpp;
        private CPUType cpuType = CPUType.arm64_v8a;
        private SymbolsType symbolsType = SymbolsType.Auto;

        private string[] releaseTypeDisplayNames = Array.Empty<string>();
        private string[] scriptingBackendTypeNames = Array.Empty<string>();
        private string[] symbolTypeNames = Array.Empty<string>();
        private string[] cpuTypeNames = Array.Empty<string>();
        private string[] availableUnityVersions = Array.Empty<string>();
        
        private bool validUnityHubFolder;
        private bool isMissingUnityVersion;
        private string desiredUnityVersion;
        private bool isMissingCPUType;
        private string desiredCPUType;
        
        private Vector2 outputScrollView;
        private GUIStyle outputTextFieldStyle;
        private Vector2 inputScrollView;
        private bool printCommands;

        private static HashSet<string> unknowLibs = new HashSet<string>();



        [MenuItem("Window/Analysis/Smart Symbolicate", false, 1000)]
        public static void ShowExample()
        {
            SmartSymbolicateWindow wnd = GetWindow<SmartSymbolicateWindow>();
            wnd.titleContent = new GUIContent(SMART_SYMBOLICATE_TITLE_WINDOW);
        }

        private void OnEnable()
        {
            GenerateDisplayNames();
            ValidateUnityHubPath(unityHubPath);
            HyperlinkInterceptor.Enable();
        }

        private void OnDisable()
        {
            HyperlinkInterceptor.Disable();
        }

        private void GenerateDisplayNames()
        {
            releaseTypeDisplayNames = Enum.GetNames(typeof(ReleaseType));
            scriptingBackendTypeNames = Enum.GetNames(typeof(ScriptingBackendType));
            cpuTypeNames = Enum.GetNames(typeof(CPUType));
            symbolTypeNames = Enum.GetNames(typeof(SymbolsType));

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
            EditorGUILayout.SelectableLabel(output, outputTextFieldStyle, GUILayout.ExpandHeight(true),
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
            unknowLibs.Clear();
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

                for (int j = 0; j < addressesData.LibraryNames.Length; j++)
                {
                    string libraryName = addressesData.LibraryNames[j];
                    
                    if (!TryGetLibPath(libraryName, out string knowPath))
                    {
                        parsedResults.AppendLine($"<color=red>Unknow lib named {libraryName} :: {addressesData.MemoryAddress}</color>");
                        continue;
                    }

                    if (!File.Exists(knowPath))
                    {
                        parsedResults.AppendLine($"<color=red>Failed to find lib {libraryName} at Path: {knowPath}</color>");
                        continue;
                    }

                    if (printCommands)
                    {
                        parsedResults.AppendLine($"<b>Executing Command:</b> {targetADDR2} -f -C -e \"{knowPath}\" {addressesData.MemoryAddress}");
                    }
                
                    using (Process process = new Process())
                    {
                        process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                        process.StartInfo.FileName = targetADDR2;
                        process.StartInfo.Arguments = $"-f -C -e \"{knowPath}\" {addressesData.MemoryAddress}";
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardInput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.Start();

                        string processOutput = process.StandardOutput.ReadToEnd().Replace("??:?", string.Empty);

                        if (libraryName.IndexOf(LIB_IL2CPP_NAME, StringComparison.Ordinal) > -1)
                        {
                            TryGenerateCodeHyperlinkForOutput(ref processOutput);
                        }
                        
                        parsedResults.AppendLine($"<b>{addressesData.GetLibraryDisplayName(j)}</b> [<i>{addressesData.MemoryAddress}</i>] => {processOutput}");

                        string error = process.StandardError.ReadToEnd();
                        if (!string.IsNullOrEmpty(error))
                            parsedResults.AppendLine($"<color=red>[Error]</color> {addressesData} => {error}");

                        process.WaitForExit();
                    }
                }
            }

            output = Regex.Replace(parsedResults.ToString(), @"^\s+$[\r\n]*", string.Empty, RegexOptions.Multiline);

            EditorUtility.ClearProgressBar();
        }

        private void TryGenerateCodeHyperlinkForOutput(ref string outputLine)
        {
            try
            {
                
                if (string.IsNullOrEmpty(outputLine))
                    return;

                string[] splitResults = outputLine.Split(new[] { "_" }, StringSplitOptions.RemoveEmptyEntries);
                if (splitResults.Length < 2)
                    return;

                string className = splitResults[0];
                string methodName = splitResults[1];

                string[] classGUIDs = AssetDatabase.FindAssets($"{className} t:TextAsset");
                if (classGUIDs.Length != 1)
                    return;

                string classPath = AssetDatabase.GUIDToAssetPath(classGUIDs[0]);
                string fileClassName = Path.GetFileNameWithoutExtension(classPath);

                if (!string.Equals(fileClassName, className, StringComparison.Ordinal))
                    return;
                
                // string methodDeclarationString = $" {methodName}";

                string[] lines = File.ReadAllLines(Path.GetFullPath(classPath));

                int hitCounts = 0;
                int targetLine = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    
                    if (line.IndexOf(methodName, StringComparison.Ordinal) == -1) 
                        continue;
                    
                    //If is assigning the value somewhere, probably its not the method declaration
                    if (line.IndexOf("=", StringComparison.OrdinalIgnoreCase) > -1)
                        continue;

                    //If has less than 1 space, probably is a method invocation
                    if (line.Split(' ').Length < 1)
                        continue;

                    //Ignore .
                    if (line.IndexOf(".", StringComparison.OrdinalIgnoreCase) > -1)
                        continue;
                    
                    hitCounts++;
                    targetLine = i + 1;
                }

                outputLine = outputLine.Replace("\n", string.Empty);
                if (hitCounts == 1)
                    outputLine = $"{outputLine} (at <a href=\"{classPath}\" line=\"{targetLine}\">{classPath}:{targetLine}</a> <i> This is a guess :) </i>) ";
                else
                    outputLine = $"{outputLine} (at <a href=\"{classPath}\" line=\"{0}\">{classPath}</a> <i> This is a guess :) </i>) ";
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private bool TryGetLibPath(string targetLibName, out string libKnowPath)
        {
            string targetProjectSymbolsPath = projectSymbolsPath;
            targetProjectSymbolsPath = Path.Combine(targetProjectSymbolsPath, cpuTypeNames[(int)cpuType]);

            DirectoryInfo projectPathDirectory = new DirectoryInfo(targetProjectSymbolsPath);

            FileInfo[] targetLibFiles = projectPathDirectory.GetFiles($"{targetLibName}*", SearchOption.AllDirectories);
            SortedDictionary<string, FileInfo> prioritySymbols = new SortedDictionary<string, FileInfo>();
            for (int i = 0; i < targetLibFiles.Length; i++)
            {
                FileInfo fileInfo = targetLibFiles[i];

                if (!prioritySymbols.ContainsKey(DEBUG_EXTENSION))
                {
                    if (fileInfo.FullName.IndexOf(DEBUG_EXTENSION, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        prioritySymbols.Add(DEBUG_EXTENSION, fileInfo);
                        continue;
                    }
                }

                if (!prioritySymbols.ContainsKey(SYM_EXTENSION))
                {
                    if (fileInfo.FullName.IndexOf(SYM_EXTENSION, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        prioritySymbols.Add(SYM_EXTENSION, fileInfo);
                        continue;
                    }
                }

                if (!prioritySymbols.ContainsKey(DEFAULT_EXTENSION))
                {
                    if (fileInfo.FullName.IndexOf(DEFAULT_EXTENSION, StringComparison.OrdinalIgnoreCase) > -1)
                    {
                        prioritySymbols.Add(DEFAULT_EXTENSION, fileInfo);
                    }
                }
            }

            if (prioritySymbols.TryGetValue(DEBUG_EXTENSION, out FileInfo resultFileInfo))
            {
                libKnowPath = resultFileInfo.FullName;
                return true;
            }

            if (prioritySymbols.TryGetValue(SYM_EXTENSION, out resultFileInfo))
            {
                libKnowPath = resultFileInfo.FullName;
                return true;
            }

            if (prioritySymbols.TryGetValue(DEFAULT_EXTENSION, out resultFileInfo))
            {
                libKnowPath = resultFileInfo.FullName;
                return true;
            }
            
            
            if (targetLibName.Equals(LIB_UNITY_NAME, StringComparison.Ordinal))
            {
                string targetPath = Path.Combine(unityHubPath, unityVersion);
                targetPath = Path.Combine(targetPath, UnityVariationsPath);

                targetPath = Path.Combine(targetPath, scriptingBackendTypeNames[(int)releaseType]);
                targetPath = Path.Combine(targetPath, releaseTypeDisplayNames[(int)releaseType]);
                targetPath = Path.Combine(targetPath, "Symbols");
                targetPath = Path.Combine(targetPath, cpuTypeNames[(int)cpuType]);
                
                
                targetPath = Path.Combine(targetPath, $"{LIB_UNITY_NAME}.{SYM_EXTENSION}");
                libKnowPath = targetPath;
                return true;
            }
            
            
            libKnowPath = string.Empty;
            unknowLibs.Add(targetLibName);
            return false;
        }

        private string GetTargetAddr2line()
        {
            if (cpuType == CPUType.arm64_v8a)
                return Path.Combine(Path.Combine(unityHubPath, unityVersion), Addr2Line64Path);

            return Path.Combine(Path.Combine(unityHubPath, unityVersion), Addr2Line32Path);
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

                AddressesData addressesData = new AddressesData(match.Value);
                if (symbolsType == SymbolsType.Auto)
                {
                    Match libMatch = Regex.Match(line, @"(?<=at )(.*)(?=\.)");
                    if (libMatch.Success)
                        addressesData.SetLibs(libMatch.Value);
                }
                else if (symbolsType == SymbolsType.libunity)
                {
                    addressesData.SetLibs(Path.GetFileNameWithoutExtension(LIB_UNITY_NAME));
                }
                else if (symbolsType == SymbolsType.libil2cpp)
                {
                    addressesData.SetLibs(Path.GetFileNameWithoutExtension(LIB_IL2CPP_NAME));
                }
                else if (symbolsType == SymbolsType.All)
                {
                    addressesData.SetLibs(GetAllLibraries());
                }
                
                addressesDatas.Add(addressesData);
            }
        }

        private string[] GetAllLibraries()
        {
            return new[] { Path.GetFileNameWithoutExtension(LIB_UNITY_NAME), Path.GetFileNameWithoutExtension(LIB_IL2CPP_NAME) };
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
                    printCommands = EditorGUILayout.ToggleLeft("Print Commands", printCommands);
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
            
            
            EditorGUILayout.BeginVertical("HelpBox", GUILayout.Width(100));
            EditorGUILayout.LabelField("Symbols", EditorStyles.boldLabel);
            symbolsType = (SymbolsType)GUILayout.SelectionGrid((int)symbolsType, symbolTypeNames, 2, EditorStyles.radioButton); 
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

            string[] childDirectories = Directory.GetDirectories(targetUnityPath);

            List<string> validUnityPaths = new List<string>();

            for (int i = 0; i < childDirectories.Length; i++)
            {
                string folderName = Path.GetFileName(childDirectories[i]);
                if (!Regex.Match(folderName, @"^[1-9]\d*(\.[1-9]\d*)*[a-z]*[1-9]").Success)
                    continue;
                
                validUnityPaths.Add(folderName);
            }
            availableUnityVersions = validUnityPaths.ToArray();
            validUnityHubFolder = availableUnityVersions.Length > 0;
        }
    }
}
