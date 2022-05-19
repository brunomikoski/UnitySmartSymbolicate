using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.CodeEditor;
using UnityEditor;

namespace BrunoMikoski.SmartSymbolicate
{
    public static class HyperlinkInterceptor
    {
        private static EventInfo cachedHyperLinkClickedEvent;
        private static EventInfo HyperLinkClickedEvent
        {
            get
            {
                if (cachedHyperLinkClickedEvent == null)
                {
                    cachedHyperLinkClickedEvent = typeof(EditorGUI).GetEvent("hyperLinkClicked", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                }
                return cachedHyperLinkClickedEvent;
            }
        }

        private static Delegate cachedHyperLinkClickedHandler;
        private static Delegate HyperLinkClickedHandler
        {
            get
            {
                if (cachedHyperLinkClickedHandler == null)
                {
#if UNITY_2021_2_OR_NEWER
                    Action<EditorWindow, HyperLinkClickedEventArgs> handler = (_, args) => OnProcessClickData(args.hyperLinkData);
                    cachedHyperLinkClickedHandler = handler;
#else
                    if (HyperLinkClickedEvent != null)
                    {
                        var method = typeof(HyperlinkInterceptor).GetMethod("OnClicked", BindingFlags.Static | BindingFlags.NonPublic| BindingFlags.Public);
                        if (method != null)
                        {
                            cachedHyperLinkClickedHandler = Delegate.CreateDelegate(HyperLinkClickedEvent.EventHandlerType, method);
                        }
                    }
#endif
                }
                return cachedHyperLinkClickedHandler;
            }
        }

        public static void Enable()
        {
            if (HyperLinkClickedHandler == null)
                throw new NotSupportedException();

#if UNITY_2021_2_OR_NEWER
            EditorGUI.hyperLinkClicked += (Action<EditorWindow, HyperLinkClickedEventArgs>)HyperLinkClickedHandler;
#else
            HyperLinkClickedEvent.AddMethod.Invoke(null, new object[] { HyperLinkClickedHandler });
#endif
        }
        public static void Disable()
        {
            if (HyperLinkClickedHandler == null)
                throw new NotSupportedException();

#if UNITY_2021_2_OR_NEWER
            EditorGUI.hyperLinkClicked -= (Action<EditorWindow, HyperLinkClickedEventArgs>)HyperLinkClickedHandler;
#else
            HyperLinkClickedEvent.RemoveMethod.Invoke(null, new object[] { HyperLinkClickedHandler });
#endif  
        }

        static PropertyInfo property;
        static void OnClicked(object sender, EventArgs args)
        {
            if (!EditorWindow.HasOpenInstances<SmartSymbolicateWindow>())
                return;
            
            if (property == null)
            {
                property = args.GetType().GetProperty("hyperlinkInfos", BindingFlags.Instance | BindingFlags.Public);
                if (property == null) return;
            }
            var infos = property.GetValue(args);
            if (infos is Dictionary<string, string>)
            {
                OnProcessClickData((Dictionary<string, string>)infos);
            }
        }

        static void OnProcessClickData(Dictionary<string, string> infos)
        {
            if (infos == null) return;
            if (!infos.TryGetValue("href", out var path)) return;
            infos.TryGetValue("line", out var line);
            infos.TryGetValue("column", out var column);

            if (File.Exists(path))
            {
                int.TryParse(line, out var _line);
                int.TryParse(column, out var _column);
                OpenFileInIDE(path, _line, _column);
            }
        }

        public static bool OpenFileInIDE(string filepath, int line, int column)
        {
            return CodeEditor.CurrentEditor.OpenProject(filepath, line, column);
        }
    }
}