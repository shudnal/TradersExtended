using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

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
        private const string ProgressRpc = RequestRpc + ".Progress";
        private const string RequestChunkRpc = RequestRpc + ".Chunk";
        private const string ResponseChunkRpc = ResponseRpc + ".Chunk";
        private static readonly Dictionary<ZRpc, TransferBuffer> incomingRequests = new Dictionary<ZRpc, TransferBuffer>();
        private static TransferBuffer incomingResponse;
        internal static event Action TransferProgress;
        private const int CorrelationMarker = 0x54454332;
        private const int MaximumPackageBytes = Persistence.MaximumFileBytes + 8192;
        private const float RequestTimeoutSeconds = 15f;

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
            internal long Id;
            internal float StartedAt;
        }

        private static RemoteAdminAccessState remoteAdminAccess;
        private static long remoteAdminServerPeerId;
        private static PendingRequest pendingRequest;
        private static PendingRequest activeRequest;
        private static long nextRequestId;
        private static ZNet remoteSession;
        private static ZRpc remoteServerRpc;
        private static long targetRevision;
        private static float nextTransferCleanup;

        internal static long TargetRevision
        {
            get
            {
                RefreshRemoteAdminState();
                return targetRevision;
            }
        }

        internal static void Update()
        {
            RefreshRemoteAdminState();
            float now = Time.realtimeSinceStartup;
            if (now < nextTransferCleanup)
                return;
            nextTransferCleanup = now + 1f;
            foreach (ZRpc rpc in incomingRequests.Where(entry => now - entry.Value.LastActivity > RequestTimeoutSeconds)
                .Select(entry => entry.Key).ToArray())
                incomingRequests.Remove(rpc);
        }

        internal static event Action<ConfigEditorOperation, bool, string, string, string> ResponseReceived;

        internal static bool UsesRemoteServer
        {
            get
            {
                return ZNet.instance != null && !ZNet.instance.IsServer();
            }
        }

        internal static bool CanEditTarget
        {
            get
            {
                RefreshRemoteAdminState();
                if (!UsesRemoteServer)
                    return true;
                if (remoteAdminServerPeerId == 0L)
                    return false;
                return remoteAdminAccess == RemoteAdminAccessState.Allowed;
            }
        }

        internal static string TargetDescription => UsesRemoteServer ? "dedicated server" : "local configuration directory";

        internal static string EditorDirectory => Path.Combine(Paths.ConfigPath, TradersExtended.pluginID);

        internal static void RegisterRpc()
        {
            if (ZNet.instance == null)
                return;
            foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                RegisterRpc(peer);
        }

        internal static void RegisterRpc(ZNetPeer peer)
        {
            if (peer?.m_rpc == null)
                return;
            // Direct RPCs retain the authenticated connection. Routed sender IDs are payload data.
            peer.m_rpc.Register<ZPackage>(RequestRpc, RPC_Request);
            peer.m_rpc.Register<ZPackage>(ResponseRpc, RPC_Response);
            peer.m_rpc.Register<ZPackage>(RequestChunkRpc, RPC_RequestChunk);
            peer.m_rpc.Register<ZPackage>(ResponseChunkRpc, RPC_ResponseChunk);
            peer.m_rpc.Register<ZPackage>(ProgressRpc, RPC_TransferProgress);
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
            RefreshRemoteAdminState();
            CancelPendingRequest();

            if (!UsesRemoteServer)
            {
                ExecuteRequest(operation, fileName, content, Emit);
                return;
            }

            if (remoteAdminServerPeerId == 0L)
            {
                Emit(operation, false, fileName, "The server connection is not ready. No local files were changed.", string.Empty);
                return;
            }
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

        internal static void CancelPendingRequest()
        {
            activeRequest = null;
            pendingRequest = null;
            incomingResponse = null;
            if (remoteAdminAccess == RemoteAdminAccessState.Checking)
                remoteAdminAccess = RemoteAdminAccessState.Unknown;
        }

        private static void SendRemoteRequest(PendingRequest request)
        {
            if (request == null || remoteServerRpc == null || remoteAdminServerPeerId == 0L)
                return;

            request.Id = ++nextRequestId;
            request.StartedAt = Time.realtimeSinceStartup;
            activeRequest = request;
            ZPackage package = new ZPackage();
            package.Write((int)request.Operation);
            package.Write(request.FileName ?? string.Empty);
            package.Write(request.Content ?? string.Empty);
            WriteCorrelation(package, request.Id);
            if (package.Size() > MaximumPackageBytes)
            {
                CancelPendingRequest();
                Emit(request.Operation, false, request.FileName, "The configuration exceeds the 8 MiB editor file limit.", string.Empty);
                return;
            }
            SendPackage(remoteServerRpc, RequestRpc, RequestChunkRpc, package, request.Operation, request.Id);
        }

        private static void RefreshRemoteAdminState()
        {
            ZNetPeer server = UsesRemoteServer ? ZNet.instance.GetServerPeer() : null;
            long serverPeerId = server?.m_uid ?? 0L;
            if (!ReferenceEquals(remoteSession, ZNet.instance) || !ReferenceEquals(remoteServerRpc, server?.m_rpc) ||
                serverPeerId != remoteAdminServerPeerId)
            {
                if (!ReferenceEquals(remoteSession, ZNet.instance))
                    incomingRequests.Clear();
                targetRevision++;
                remoteSession = ZNet.instance;
                remoteServerRpc = server?.m_rpc;
                remoteAdminServerPeerId = serverPeerId;
                remoteAdminAccess = RemoteAdminAccessState.Unknown;
                CancelPendingRequest();
            }
            if (activeRequest != null && Time.realtimeSinceStartup - activeRequest.StartedAt > RequestTimeoutSeconds)
                CancelPendingRequest();
        }

        private static void WriteCorrelation(ZPackage package, long id)
        {
            package.Write(CorrelationMarker);
            package.Write(id);
        }

        private static long ReadCorrelation(ZPackage package)
        {
            int remaining = package.Size() - package.GetPos();
            if (remaining == 0)
                return 0L; // A response without a correlation ID cannot be used safely.
            if (remaining != 12 || package.ReadInt() != CorrelationMarker)
                throw new InvalidDataException("Invalid editor request correlation data.");
            return package.ReadLong();
        }

        private static long PeekCorrelation(ZPackage package)
        {
            int position = package.GetPos();
            try
            {
                if (package.Size() < 16)
                    return 0L;
                package.SetPos(package.Size() - 12);
                return package.ReadInt() == CorrelationMarker ? package.ReadLong() : 0L;
            }
            finally { package.SetPos(position); }
        }

        private static void RPC_Request(ZRpc sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || package == null || package.Size() > MaximumPackageBytes)
                return;
            try
            {
                ConfigEditorOperation operation = (ConfigEditorOperation)package.ReadInt();
                if (!Enum.IsDefined(typeof(ConfigEditorOperation), operation))
                    return;
                bool senderIsAdmin = IsSenderAdmin(sender);
                // Reject unauthorized file access before decoding strings or touching the filesystem.
                if (!senderIsAdmin && operation != ConfigEditorOperation.Access)
                {
                    SendResponse(sender, operation, false, string.Empty,
                        "Administrator access is required to edit Traders Extended files.", string.Empty, PeekCorrelation(package));
                    return;
                }
                if (operation == ConfigEditorOperation.Access && package.Size() > 1024)
                    return;
                string fileName = package.ReadString();
                string content = package.ReadString();
                long requestId = ReadCorrelation(package);
                if (operation == ConfigEditorOperation.Access)
                {
                    SendResponse(sender, operation, true, string.Empty,
                        senderIsAdmin ? "Administrator access granted." : "Administrator access denied.",
                        senderIsAdmin ? "1" : "0", requestId);
                    return;
                }
                ExecuteRequest(operation, fileName, content,
                    (responseOperation, success, responseFile, message, payload) =>
                        SendResponse(sender, responseOperation, success, responseFile, message, payload, requestId));
            }
            catch (Exception exception)
            {
                TradersExtended.LogWarning($"Invalid configuration editor request from peer {sender}: {exception.Message}");
            }
        }

        private static void RPC_Response(ZRpc sender, ZPackage package)
        {
            RefreshRemoteAdminState();
            if (!UsesRemoteServer || remoteAdminServerPeerId == 0L || !ReferenceEquals(sender, remoteServerRpc) ||
                activeRequest == null || package == null || package.Size() > MaximumPackageBytes)
                return;
            try
            {
                ConfigEditorOperation operation = (ConfigEditorOperation)package.ReadInt();
                bool success = package.ReadBool();
                string fileName = package.ReadString();
                string message = package.ReadString();
                string payload = package.ReadString();
                long requestId = ReadCorrelation(package);
                if (requestId == 0L)
                {
                    PendingRequest failed = pendingRequest ?? activeRequest;
                    CancelPendingRequest();
                    remoteAdminAccess = RemoteAdminAccessState.Unknown;
                    Emit(failed.Operation, false, failed.FileName, "Update Traders Extended on the server to use this editor safely.", string.Empty);
                    return;
                }
                if (requestId != activeRequest.Id || operation != activeRequest.Operation)
                    return;

                PendingRequest completed = activeRequest;
                if (success && operation != ConfigEditorOperation.Access && operation != ConfigEditorOperation.List && fileName != completed.FileName)
                    throw new InvalidDataException("The server returned a different configuration file.");
                activeRequest = null;
                if (operation == ConfigEditorOperation.Access)
                {
                    remoteAdminAccess = success && payload == "1" ? RemoteAdminAccessState.Allowed : RemoteAdminAccessState.Denied;
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
                Emit(operation, success, completed.FileName, message, payload);
            }
            catch (Exception exception)
            {
                PendingRequest failed = pendingRequest ?? activeRequest;
                CancelPendingRequest();
                if (failed != null)
                    Emit(failed.Operation, false, failed.FileName, "Invalid configuration editor response: " + exception.Message, string.Empty);
                TradersExtended.LogWarning($"Invalid configuration editor response: {exception.Message}");
            }
        }

        private static void SendResponse(ZRpc target, ConfigEditorOperation operation, bool success, string fileName, string message, string payload, long requestId)
        {
            ZPackage package = new ZPackage();
            package.Write((int)operation);
            package.Write(success);
            package.Write(fileName ?? string.Empty);
            package.Write(message ?? string.Empty);
            package.Write(payload ?? string.Empty);
            WriteCorrelation(package, requestId);
            if (package.Size() > MaximumPackageBytes)
                throw new InvalidDataException("The configuration response exceeds the editor transfer limit.");
            SendPackage(target, ResponseRpc, ResponseChunkRpc, package, operation, requestId);
        }

        private static void SendPackage(ZRpc rpc, string method, string chunkMethod, ZPackage package, ConfigEditorOperation operation, long id)
        {
            if (package.Size() <= TransferBuffer.ChunkBytes)
            {
                rpc.Invoke(method, package);
                return;
            }
            byte[] bytes = package.GetArray();
            for (int offset = 0; offset < bytes.Length; offset += TransferBuffer.ChunkBytes)
            {
                int count = Math.Min(TransferBuffer.ChunkBytes, bytes.Length - offset);
                byte[] chunk = new byte[count];
                Buffer.BlockCopy(bytes, offset, chunk, 0, count);
                ZPackage part = new ZPackage();
                part.Write(id);
                part.Write((int)operation);
                part.Write(bytes.Length);
                part.Write(offset);
                part.Write(chunk);
                rpc.Invoke(chunkMethod, part);
            }
        }

        private static byte[] ReadChunk(ZPackage package, out long id, out int operation, out int length, out int offset)
        {
            if (package == null || package.Size() < 25 || package.Size() > TransferBuffer.ChunkBytes + 24)
                throw new InvalidDataException("Invalid configuration transfer chunk size.");
            id = package.ReadLong();
            operation = package.ReadInt();
            length = package.ReadInt();
            offset = package.ReadInt();
            int countPosition = package.GetPos();
            int count = package.ReadInt();
            if (count <= 0 || count > TransferBuffer.ChunkBytes || count != package.Size() - package.GetPos())
                throw new InvalidDataException("Invalid configuration transfer chunk payload.");
            package.SetPos(countPosition);
            return package.ReadByteArray();
        }

        private static void RPC_RequestChunk(ZRpc sender, ZPackage package)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || !IsSenderAdmin(sender))
                return;
            try
            {
                byte[] chunk = ReadChunk(package, out long id, out int operation, out int length, out int offset);
                if (operation != (int)ConfigEditorOperation.Write && operation != (int)ConfigEditorOperation.Create)
                    throw new InvalidDataException("This operation does not accept request chunks.");
                float now = Time.realtimeSinceStartup;
                foreach (ZRpc expired in incomingRequests.Where(pair => now - pair.Value.LastActivity > RequestTimeoutSeconds)
                    .Select(pair => pair.Key).ToList())
                    incomingRequests.Remove(expired);
                if (offset == 0)
                    incomingRequests[sender] = new TransferBuffer(id, operation, length, MaximumPackageBytes, now);
                if (!incomingRequests.TryGetValue(sender, out TransferBuffer transfer))
                    return;
                byte[] completed = transfer.Add(id, operation, length, offset, chunk, now);
                if (offset % (TransferBuffer.ChunkBytes * 8) == 0)
                {
                    ZPackage progress = new ZPackage();
                    progress.Write(id);
                    sender.Invoke(ProgressRpc, progress);
                }
                if (completed != null)
                {
                    incomingRequests.Remove(sender);
                    ZPackage request = new ZPackage(completed);
                    if (PeekCorrelation(request) != id)
                        throw new InvalidDataException("Configuration transfer correlation mismatch.");
                    RPC_Request(sender, request);
                }
            }
            catch (Exception exception)
            {
                incomingRequests.Remove(sender);
                TradersExtended.LogWarning($"Invalid configuration editor transfer: {exception.Message}");
            }
        }

        private static void RPC_TransferProgress(ZRpc sender, ZPackage package)
        {
            RefreshRemoteAdminState();
            if (UsesRemoteServer && ReferenceEquals(sender, remoteServerRpc) && activeRequest != null &&
                package != null && package.Size() == 8 && package.ReadLong() == activeRequest.Id)
            {
                activeRequest.StartedAt = Time.realtimeSinceStartup;
                TransferProgress?.Invoke();
            }
        }

        private static void RPC_ResponseChunk(ZRpc sender, ZPackage package)
        {
            RefreshRemoteAdminState();
            if (!UsesRemoteServer || activeRequest == null || !ReferenceEquals(sender, remoteServerRpc))
                return;
            try
            {
                byte[] chunk = ReadChunk(package, out long id, out int operation, out int length, out int offset);
                if (id != activeRequest.Id || operation != (int)activeRequest.Operation)
                    return;
                float now = Time.realtimeSinceStartup;
                if (offset == 0)
                    incomingResponse = new TransferBuffer(id, operation, length, MaximumPackageBytes, now);
                if (incomingResponse == null)
                    return;
                byte[] completed = incomingResponse.Add(id, operation, length, offset, chunk, now);
                activeRequest.StartedAt = now;
                TransferProgress?.Invoke();
                if (completed != null)
                {
                    incomingResponse = null;
                    RPC_Response(sender, new ZPackage(completed));
                }
            }
            catch (Exception exception)
            {
                PendingRequest failed = activeRequest;
                CancelPendingRequest();
                if (failed != null)
                    Emit(failed.Operation, false, failed.FileName, "Invalid configuration transfer: " + exception.Message, string.Empty);
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect), new Type[] { typeof(ZNetPeer) })]
        private static class ZNet_Disconnect_ClearEditorTransfer
        {
            private static void Prefix(ZNetPeer peer)
            {
                if (peer?.m_rpc != null)
                    incomingRequests.Remove(peer.m_rpc);
            }
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

                        string fileContent = Persistence.ReadBounded(path);

                        responder(operation, true, fileName, "Configuration loaded.", fileContent);
                        break;
                    }
                    case ConfigEditorOperation.Create:
                    case ConfigEditorOperation.Write:
                    {
                        string path = GetSafePath(fileName);
                        if (operation == ConfigEditorOperation.Create && File.Exists(path))
                            throw new IOException("A configuration file with this name already exists.");
                        if (Encoding.UTF8.GetByteCount(content ?? string.Empty) > Persistence.MaximumFileBytes)
                            throw new InvalidDataException("The configuration exceeds the 8 MiB editor file limit.");
                        if (!ConfigEditorSerialization.Validate(fileName, content, out string validationError))
                            throw new InvalidDataException(validationError);

                        Persistence.WriteAtomically(path, content, operation == ConfigEditorOperation.Create);
                        if (!TradersExtended.ReadConfigs())
                            throw new InvalidOperationException("The file was saved, but configuration reload failed. The previous runtime configuration remains active; check the server log.");
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
                        if (!TradersExtended.ReadConfigs())
                            throw new InvalidOperationException("The file was deleted, but configuration reload failed. The previous runtime configuration remains active; check the server log.");
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
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("/") || fileName.Contains("\\") ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
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
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symbolic links are not editable configuration files.");
            return path;
        }

        private static bool IsSenderAdmin(ZRpc sender)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer())
                return false;
            ZNetPeer peer = znet.GetPeer(sender);
            if (peer == null || !peer.IsReady() || peer.m_socket == null || znet.m_adminList == null)
                return false;

            string hostName = peer.m_socket.GetHostName();
            return !string.IsNullOrWhiteSpace(hostName) && znet.ListContainsId(znet.m_adminList, hostName);
        }

        private static void Emit(ConfigEditorOperation operation, bool success, string fileName, string message, string payload)
        {
            ResponseReceived?.Invoke(operation, success, fileName ?? string.Empty, message ?? string.Empty, payload ?? string.Empty);
        }

        /// <summary>One bounded, ordered reliable-RPC transfer. Authorization belongs to the connection handler.</summary>
        private sealed class TransferBuffer
        {
            internal const int ChunkBytes = 32 * 1024;
            private readonly byte[] bytes;
            private int nextOffset;
            internal readonly long Id;
            internal readonly int Operation;
            internal float LastActivity;

            internal TransferBuffer(long id, int operation, int length, int maximumBytes, float now)
            {
                if (id <= 0 || length <= 0 || length > maximumBytes)
                    throw new InvalidDataException("Invalid configuration transfer length or ID.");
                Id = id;
                Operation = operation;
                bytes = new byte[length];
                LastActivity = now;
            }

            internal byte[] Add(long id, int operation, int length, int offset, byte[] chunk, float now)
            {
                if (id != Id || operation != Operation || length != bytes.Length || chunk == null ||
                    chunk.Length == 0 || chunk.Length > ChunkBytes || offset != nextOffset ||
                    chunk.Length > bytes.Length - nextOffset)
                    throw new InvalidDataException("Invalid or out-of-order configuration transfer chunk.");
                Buffer.BlockCopy(chunk, 0, bytes, nextOffset, chunk.Length);
                nextOffset += chunk.Length;
                LastActivity = now;
                return nextOffset == bytes.Length ? bytes : null;
            }
        }

        private static class Persistence
        {
            internal const int MaximumFileBytes = 8 * 1024 * 1024;

            internal static string ReadBounded(string path)
            {
                using (FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (MemoryStream content = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (content.Length + count > MaximumFileBytes)
                            throw new InvalidDataException("The configuration exceeds the 8 MiB editor file limit.");
                        content.Write(buffer, 0, count);
                    }
                    content.Position = 0;
                    using (StreamReader reader = new StreamReader(content, Encoding.UTF8, true))
                        return reader.ReadToEnd();
                }
            }

            internal static void WriteAtomically(string path, string content, bool create)
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content ?? string.Empty);
                if (bytes.Length > MaximumFileBytes)
                    throw new InvalidDataException("The configuration exceeds the 8 MiB editor file limit.");
                string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (create)
                        File.Move(temporaryPath, path); // Fails rather than overwriting a concurrently created file.
                    else
                        File.Replace(temporaryPath, path, null); // Never truncate or recreate a deleted original.
                }
                finally
                {
                    try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        private static class ZNet_OnNewConnection_RegisterConfigEditorRpc
        {
            private static void Postfix(ZNetPeer peer)
            {
                ConfigEditorTransport.RegisterRpc(peer);
            }
        }
    }
}
