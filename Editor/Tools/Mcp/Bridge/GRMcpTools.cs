using System;
using System.Linq;
using System.Reflection;
using System.Text;

using UnityEditor;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The unity_* tools the bridge runs, on the main thread. Their names,
    /// descriptions and input schemas live in Editor/McpRelay/tools.json, which the
    /// relay serves to the agent. Every tool here is read-only on project files.
    /// </summary>
    internal static class GRMcpTools
    {
        [Serializable]
        [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class StatusReport
        {
            public string projectPath;
            public string unityVersion;
            public string sdkVersion;
            public string settingsAsset;
            public string environment;
            public bool debugLogs;
            public bool autoTrackPurchases;
            public string signedInAs;
            public string signInEnvironment;
            public string studio;
            public string studioId;
            public string connectedGameId;
            public string connectedGameName;
            public bool isCompiling;
            public bool isPlaying;
            public string lastCompile;
        }

        public static GRBridgeResponse Handle(GRBridgeRequest request)
        {
            switch (request?.method)
            {
                case "ping":
                    return GRBridgeResponse.Result("pong");
                case "tools/call":
                    return Call(request.@params?.name);
                case "job/get":
                    return GRCompileTracker.GetJob(request.@params?.jobId);
                default:
                    return GRBridgeResponse.Failure($"Unknown method '{request?.method}'.");
            }
        }

        private static GRBridgeResponse Call(string tool)
        {
            switch (tool)
            {
                case "unity_status":
                    return GRBridgeResponse.Result(Status());
                case "unity_get_sdk_reference":
                    return GRBridgeResponse.Result(SdkReference());
                case "unity_compile":
                    return GRBridgeResponse.Pending(GRCompileTracker.StartJob());
                default:
                    return GRBridgeResponse.Result($"Unknown tool '{tool}'.", isError: true);
            }
        }

        private static string Status()
        {
            var settings = GRProjectInfo.FindSettings(out var settingsPath);
            var user = GRServices.Auth.CurrentUser;
            var report = new StatusReport
            {
                projectPath = GRProjectInfo.ProjectPath,
                unityVersion = Application.unityVersion,
                sdkVersion = GRProjectInfo.SdkVersion() ?? "unknown",
                settingsAsset = settingsPath ?? "none (Assets > Create > GameRebellion > Settings)",
                environment = settings != null ? settings.Environment.ToString() : "unknown",
                debugLogs = settings != null && settings.IsDebug,
                autoTrackPurchases = settings != null && settings.AutoTrackPurchases,
                signedInAs = user == null ? "nobody" : user.Email + (GRServices.Auth.IsSimulated ? " (mock sign-in)" : string.Empty),
                signInEnvironment = GRServices.Auth.Environment.ToString(),
                studio = EditorUserSettings.GetConfigValue(GRWindowModel.StudioNameKey) ?? "none",
                studioId = EditorUserSettings.GetConfigValue(GRWindowModel.StudioIdKey) ?? "none",
                connectedGameId = EditorUserSettings.GetConfigValue(GRWindowModel.GameIdKey) ?? "none",
                connectedGameName = EditorUserSettings.GetConfigValue(GRWindowModel.GameNameKey) ?? "none",
                isCompiling = EditorApplication.isCompiling,
                isPlaying = EditorApplication.isPlaying,
                lastCompile = GRCompileTracker.LastCompileSummary(),
            };

            return JsonUtility.ToJson(report, true);
        }

        /// <summary>
        /// The public SDK surface, read by reflection from the installed runtime
        /// assembly, so it always matches the version in this project.
        /// </summary>
        private static string SdkReference()
        {
            var api = typeof(global::GameRebellion);
            var text = new StringBuilder();
            text.AppendLine($"GameRebellion SDK {GRProjectInfo.SdkVersion() ?? "(unknown version)"}: public API of the installed {api.Assembly.GetName().Name} assembly.");
            text.AppendLine("Game code calls these as GameRebellion.<Method>(…); no using directive is needed.");
            text.AppendLine("Call GameRebellion.Initialize once at startup, before any Track call.");
            text.AppendLine();
            text.AppendLine("Methods:");

            var methods = api.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .OrderBy(method => method.Name)
                .ThenBy(method => method.GetParameters().Length);
            foreach (var method in methods)
            {
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{TypeName(p.ParameterType)} {p.Name}"));
                text.Append($"- {TypeName(method.ReturnType)} {method.Name}({parameters})");
                if (method.Name == "TrackLevelCompleted")
                {
                    text.Append(" -- NOT RECOMMENDED: it sends an email address with a level event. Use TrackProgression.");
                }

                text.AppendLine();
            }

            text.AppendLine();
            text.AppendLine("Enums:");
            foreach (var nested in api.GetNestedTypes(BindingFlags.Public).Where(type => type.IsEnum).OrderBy(type => type.Name))
            {
                text.AppendLine($"- GameRebellion.{nested.Name}: {string.Join(", ", Enum.GetNames(nested))}");
            }

            text.AppendLine();
            text.AppendLine("Event models (set the fields you know; leave the rest null):");
            var models = typeof(global::GrProgressionEvent).Assembly.GetTypes()
                .Where(type => type.IsClass && type.IsPublic && type.Namespace == null && type.Name.StartsWith("Gr", StringComparison.Ordinal) && type.Name.EndsWith("Event", StringComparison.Ordinal))
                .OrderBy(type => type.Name);
            foreach (var model in models)
            {
                text.AppendLine(model.Name);
                foreach (var field in model.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    text.AppendLine($"  {TypeName(field.FieldType)} {field.Name}");
                }
            }

            return text.ToString();
        }

        private static string TypeName(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                return TypeName(underlying) + "?";
            }

            if (type == typeof(void)) return "void";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(string)) return "string";
            return type.IsNested ? $"{type.DeclaringType?.Name}.{type.Name}" : type.Name;
        }
    }
}
