using System;
using System.IO;

namespace GameRebellionSdk.Unity.Editor
{
    // The wire format between the gr-editor-mcp relay and the Editor bridge
    // (EDITOR_MCP_ARCHITECTURE.md §6): one JSON object per line over 127.0.0.1 TCP.
    // Flat, fixed shapes so JsonUtility can read and write them on both sides of a
    // domain reload without any other JSON library.
    //
    //   relay -> {"type":"hello","secret":"…","protocol":1}
    //   bridge <- {"id":0,"ok":true,…}
    //   relay -> {"id":1,"method":"tools/call","params":{"name":"unity_status"}}
    //   bridge <- {"id":1,"ok":true,"done":true,"text":"…","isError":false}
    //   relay -> {"id":2,"method":"job/get","params":{"jobId":"…"}}

    /// <summary>Written to Library/GameRebellion/bridge.json for the relay to find the Editor.</summary>
    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRBridgeInfo
    {
        public int protocol;
        public int port;
        public string secret;
        public int pid;
        public string projectPath;
        public string unityVersion;
        public string sdkVersion;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRBridgeHello
    {
        public string type;
        public string secret;
        public int protocol;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRBridgeParams
    {
        public string name;
        public string jobId;
    }

    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRBridgeRequest
    {
        public int id;
        public string method;
        public GRBridgeParams @params;
    }

    /// <summary>
    /// A tool result. A long tool answers with <c>done = false</c> and a job id; the
    /// relay polls <c>job/get</c> until it is done, across domain reloads.
    /// </summary>
    [Serializable]
    [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class GRBridgeResponse
    {
        public int id;
        public bool ok;
        public bool done;
        public string text;
        public bool isError;
        public string jobId;
        public string error;

        public static GRBridgeResponse Result(string text, bool isError = false) =>
            new GRBridgeResponse { ok = true, done = true, text = text, isError = isError };

        public static GRBridgeResponse Pending(string jobId) =>
            new GRBridgeResponse { ok = true, done = false, jobId = jobId };

        public static GRBridgeResponse Failure(string error) =>
            new GRBridgeResponse { ok = false, error = error };
    }

    /// <summary>Files the bridge keeps under Library/GameRebellion: per project, never committed.</summary>
    internal static class GRBridgePaths
    {
        // Read once on the main thread: Application.dataPath isn't available on socket threads.
        public static string Folder { get; private set; }
        public static string BridgeFile => Path.Combine(Folder, "bridge.json");
        public static string LastCompileFile => Path.Combine(Folder, "last-compile.json");
        public static string JobsFolder => Path.Combine(Folder, "jobs");

        public static void Init()
        {
            Folder = Path.Combine(GRProjectInfo.ProjectPath, "Library", "GameRebellion");
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(JobsFolder);
        }
    }
}
