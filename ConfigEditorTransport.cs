using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TradersExtended
{
    internal enum ConfigEditorOperation
    {
        Access = 0,
        List = 1,
        Read = 2,
        Write = 3,
        Delete = 4,
        Create = 5
    }

    internal static class ConfigEditorTransport
    {
        private const string RequestRpc = TradersExtended.pluginID + ".ConfigEditorRequest";
        private const string ResponseRpc = TradersExtended.pluginID + ".ConfigEditorResponse";

        private enum RemoteAdminAccessState
        {
            Unknown,
            Checking,
            Allowed,
            Denied
        }

        private sealed class PendingRequest
        {
            internal ConfigEditorOperation Operation;
            internal string FileName;
            internal string Content;
        }

        private static RemoteAdminAccessState remoteAdminAccess;
        private static long remoteAdminServerPeerId;
        private static PendingRequest pendingRequest;

        internal static event Action<ConfigEditorOperation, bool, string, string, string> ResponseReceived;

        internal static bool UsesRemoteServer
        {
            get
            {
                return ZNet.instance != null && !ZNet.instance.IsServer() && ZRoutedRpc.instance != null &&
                       ZRoutedRpc.instance.GetServerPeerID() != 0L;
            }
        }

        internal static bool CanEditTarget
        {
            get
            {
                if (!UsesRemoteServer)
                    return true;

                RefreshRemoteAdminState();
                return remoteAdminAccess == RemoteAdminAccessState.Allowed;
            }
        }

        internal static string TargetDescription => UsesRemoteServer ? "dedicated server" : "local configuration directory";

        internal static string EditorDirectory => Path.Combine(Paths.ConfigPath, TradersExtended.pluginID);

        internal static void RegisterRpc()
        {
            ZRoutedRpc routed = ZRoutedRpc.instance;
            if (routed == null)
                return;

            int requestHash = RequestRpc.GetStableHashCode();
            if (!routed.m_functions.ContainsKey(requestHash))
                routed.Register<ZPackage>(RequestRpc, RPC_Request);

            int responseHash = ResponseRpc.GetStableHashCode();
            if (!routed.m_functions.ContainsKey(responseHash))
                routed.Register<ZPackage>(ResponseRpc, RPC_Response);
        }

        internal static void RequestList()
        {
            SendRequest(ConfigEditorOperation.List, string.Empty, string.Empty, forceAdminRefresh: true);
        }

        internal static void RequestRead(string fileName)
        {
            SendRequest(ConfigEditorOperation.Read, fileName, string.Empty);
        }

        internal static void RequestWrite(string fileName, string content)
        {
            SendRequest(ConfigEditorOperation.Write, fileName, content ?? string.Empty);
        }

        internal static void RequestCreate(string fileName, string content)
        {
            SendRequest(ConfigEditorOperation.Create, fileName, content ?? string.Empty);
        }

        internal static void RequestDelete(string fileName)
        {
            SendRequest(ConfigEditorOperation.Delete, fileName, string.Empty);
        }

        private static void SendRequest(ConfigEditorOperation operation, string fileName, string content, bool forceAdminRefresh = false)
        {
            RegisterRpc();

            if (!UsesRemoteServer)
            {
                ExecuteRequest(operation, fileName, content, Emit);
                return;
            }

            RefreshRemoteAdminState();
            PendingRequest request = new PendingRequest
            {
                Operation = operation,
                FileName = fileName ?? string.Empty,
                Content = content ?? string.Empty
            };

            if (!forceAdminRefresh && remoteAdminAccess == RemoteAdminAccessState.Allowed)
            {
                SendRemoteRequest(request);
                return;
            }

            pendingRequest = request;
            RequestRemoteAdminAccess(forceAdminRefresh);
        }

        private static void RequestRemoteAdminAccess(bool forceRefresh)
        {
            RefreshRemoteAdminState();
            if (remoteAdminAccess == RemoteAdminAccessState.Checking)
                return;
            if (!forceRefresh && remoteAdminAccess == RemoteAdminAccessState.Allowed)
            {
                PendingRequest request = pendingRequest;
                pendingRequest = null;
                if (request != null)
                    SendRemoteRequest(request);
                return;
            }

            remoteAdminAccess = RemoteAdminAccessState.Checking;
            SendRemoteRequest(new PendingRequest
            {
                Operation = ConfigEditorOperation.Access,
                FileName = string.Empty,
                Content = string.Empty
            });
        }

        private static void SendRemoteRequest(PendingRequest request)
        {
            if (request == null || ZRoutedRpc.instance == null)
                return;

            ZPackage package = new ZPackage();
            package.Write((int)request.Operation);
            package.Write(request.FileName ?? string.Empty);
            package.Write(request.Content ?? string.Empty);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), RequestRpc, package);
        }

        private static void RefreshRemoteAdminState()
        {
            long serverPeerId = UsesRemoteServer && ZRoutedRpc.instance != null
                ? ZRoutedRpc.instance.GetServerPeerID()
                : 0L;
            if (serverPeerId == remoteAdminServerPeerId)
                return;

            remoteAdminServerPeerId = serverPeerId;
            remoteAdminAccess = RemoteAdminAccessState.Unknown;
            pendingRequest = null;
        }

        private static void RPC_Request(long sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            ConfigEditorOperation operation = (ConfigEditorOperation)package.ReadInt();
            string fileName = package.ReadString();
            string content = package.ReadString();
            bool senderIsAdmin = IsSenderAdmin(sender);

            if (operation == ConfigEditorOperation.Access)
            {
                SendResponse(sender, operation, true, string.Empty,
                    senderIsAdmin ? "Administrator access granted." : "Administrator access denied.",
                    senderIsAdmin ? "1" : "0");
                return;
            }

            if (!senderIsAdmin)
            {
                SendResponse(sender, operation, false, fileName, "Administrator access is required to edit Traders Extended files.", string.Empty);
                return;
            }

            ExecuteRequest(operation, fileName, content,
                (responseOperation, success, responseFile, message, payload) =>
                    SendResponse(sender, responseOperation, success, responseFile, message, payload));
        }

        private static void RPC_Response(long sender, ZPackage package)
        {
            if (UsesRemoteServer && ZRoutedRpc.instance != null && sender != ZRoutedRpc.instance.GetServerPeerID())
                return;

            ConfigEditorOperation operation = (ConfigEditorOperation)package.ReadInt();
            bool success = package.ReadBool();
            string fileName = package.ReadString();
            string message = package.ReadString();
            string payload = package.ReadString();

            if (operation == ConfigEditorOperation.Access)
            {
                remoteAdminAccess = success && payload == "1"
                    ? RemoteAdminAccessState.Allowed
                    : RemoteAdminAccessState.Denied;

                PendingRequest request = pendingRequest;
                pendingRequest = null;
                if (request == null)
                    return;

                if (remoteAdminAccess == RemoteAdminAccessState.Allowed)
                    SendRemoteRequest(request);
                else
                    Emit(request.Operation, false, request.FileName,
                        "Administrator access is required to edit Traders Extended files on this server.", string.Empty);
                return;
            }

            if (!success && message.IndexOf("Administrator access", StringComparison.OrdinalIgnoreCase) >= 0)
                remoteAdminAccess = RemoteAdminAccessState.Denied;

            Emit(operation, success, fileName, message, payload);
        }

        private static void SendResponse(long target, ConfigEditorOperation operation, bool success, string fileName, string message, string payload)
        {
            ZPackage package = new ZPackage();
            package.Write((int)operation);
            package.Write(success);
            package.Write(fileName ?? string.Empty);
            package.Write(message ?? string.Empty);
            package.Write(payload ?? string.Empty);
            ZRoutedRpc.instance.InvokeRoutedRPC(target, ResponseRpc, package);
        }

        private static void ExecuteRequest(
            ConfigEditorOperation operation,
            string fileName,
            string content,
            Action<ConfigEditorOperation, bool, string, string, string> responder)
        {
            try
            {
                Directory.CreateDirectory(EditorDirectory);
                switch (operation)
                {
                    case ConfigEditorOperation.List:
                    {
                        List<EditorFileInfo> files = Directory
                            .EnumerateFiles(EditorDirectory, "*", SearchOption.TopDirectoryOnly)
                            .Where(path => TradersExtended.IsSupportedConfigExtension(Path.GetExtension(path)))
                            .Select(path => BuildFileInfo(new FileInfo(path)))
                            .Where(info => info != null)
                            .OrderBy(info => info.Kind)
                            .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        responder(operation, true, string.Empty, $"Loaded {files.Count} configuration file(s).", JsonConvert.SerializeObject(files));
                        break;
                    }
                    case ConfigEditorOperation.Read:
                    {
                        string path = GetSafePath(fileName);
                        if (!File.Exists(path))
                            throw new FileNotFoundException("The selected configuration file no longer exists.", fileName);

                        string fileContent;
                        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                            fileContent = reader.ReadToEnd();

                        responder(operation, true, fileName, "Configuration loaded.", fileContent);
                        break;
                    }
                    case ConfigEditorOperation.Create:
                    case ConfigEditorOperation.Write:
                    {
                        string path = GetSafePath(fileName);
                        if (operation == ConfigEditorOperation.Create && File.Exists(path))
                            throw new IOException("A configuration file with this name already exists.");
                        if (!ConfigEditorSerialization.Validate(fileName, content, out string validationError))
                            throw new InvalidDataException(validationError);

                        File.WriteAllText(path, content ?? string.Empty, new UTF8Encoding(false));
                        TradersExtended.ReadConfigs();
                        string message = operation == ConfigEditorOperation.Create
                            ? "Configuration created and reloaded."
                            : "Configuration saved and reloaded.";
                        responder(operation, true, fileName, message, string.Empty);
                        break;
                    }
                    case ConfigEditorOperation.Delete:
                    {
                        string path = GetSafePath(fileName, requireSupportedPattern: false);
                        if (File.Exists(path))
                            File.Delete(path);
                        TradersExtended.ReadConfigs();
                        responder(operation, true, fileName, "Configuration deleted and reloaded.", string.Empty);
                        break;
                    }
                    default:
                        throw new InvalidOperationException("Unknown configuration editor operation.");
                }
            }
            catch (Exception exception)
            {
                responder(operation, false, fileName, exception.Message, string.Empty);
            }
        }

        private static EditorFileInfo BuildFileInfo(FileInfo file)
        {
            if (file == null || !TradersExtended.IsSupportedConfigExtension(file.Extension))
                return null;

            EditorFileInfo result = new EditorFileInfo
            {
                Name = file.Name,
                Length = file.Length,
                LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks,
                Kind = EditorConfigKind.Unsupported
            };

            if (TradersExtended.TryParseConfigFileName(file.Name, out string trader, out TradersExtended.ItemsListType listType))
            {
                result.Kind = EditorConfigKind.ItemList;
                result.Trader = trader;
                result.ListType = listType;
            }
            else if (TradersExtended.TryParseTraderConfigFileName(file.Name, out trader))
            {
                result.Kind = EditorConfigKind.TraderSettings;
                result.Trader = trader;
            }

            return result;
        }

        private static string GetSafePath(string fileName, bool requireSupportedPattern = true)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
                !TradersExtended.IsSupportedConfigExtension(Path.GetExtension(fileName)) ||
                (requireSupportedPattern &&
                 !TradersExtended.TryParseConfigFileName(fileName, out _, out _) &&
                 !TradersExtended.TryParseTraderConfigFileName(fileName, out _)))
                throw new InvalidDataException("Invalid Traders Extended configuration file name.");

            string directory = Path.GetFullPath(EditorDirectory);
            string path = Path.GetFullPath(Path.Combine(directory, fileName));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected path is outside the Traders Extended configuration directory.");
            return path;
        }

        private static bool IsSenderAdmin(long sender)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer())
                return false;
            if (ZRoutedRpc.instance != null && sender == ZRoutedRpc.instance.m_id)
                return true;

            ZNetPeer peer = znet.GetPeer(sender);
            if (peer == null || peer.m_socket == null || znet.m_adminList == null)
                return false;

            string hostName = peer.m_socket.GetHostName();
            return !string.IsNullOrWhiteSpace(hostName) && znet.ListContainsId(znet.m_adminList, hostName);
        }

        private static void Emit(ConfigEditorOperation operation, bool success, string fileName, string message, string payload)
        {
            ResponseReceived?.Invoke(operation, success, fileName ?? string.Empty, message ?? string.Empty, payload ?? string.Empty);
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    internal static class ZNet_Awake_RegisterConfigEditorRpc
    {
        private static void Postfix()
        {
            ConfigEditorTransport.RegisterRpc();
        }
    }
}
