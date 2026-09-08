// Deterministic test doubles for Unity/Valheim boundaries. These are not shipped with the mod.
// Inventory intentionally models native partial-add failures and shared-name stacking.
using Newtonsoft.Json.Linq;
using System.Text;

namespace UnityEngine
{
    public class GameObject { public string name; }
    public static class Time { public static float realtimeSinceStartup; }
}
namespace BepInEx { public static class Paths { public static string ConfigPath; } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string name) { }
        public HarmonyPatch(Type type, string name, Type[] arguments) { }
    }
}
public struct Vector2i
{
    public int x, y;
    public Vector2i(int x, int y) { this.x = x; this.y = y; }
}
public static class Game { public static int m_worldLevel; }
public static class ZNetView { public static bool m_forceDisableInit; }
public static class Utils
{
    public static string GetPrefabName(UnityEngine.GameObject prefab) => prefab.name;
}
public class ItemDrop
{
    public UnityEngine.GameObject gameObject;
    public ItemData m_itemData;
    public class ItemData
    {
        public UnityEngine.GameObject m_dropPrefab;
        public SharedData m_shared;
        public int m_stack = 1, m_quality = 1, m_worldLevel, m_variant;
        public bool m_equipped, m_pickedUp;
        public float m_durability;
        public long m_crafterID;
        public string m_crafterName = string.Empty;
        public Vector2i m_gridPos;
        public Dictionary<string, string> m_customData = new Dictionary<string, string>();
        public ItemData Clone()
        {
            ItemData copy = (ItemData)MemberwiseClone();
            copy.m_customData = new Dictionary<string, string>(m_customData);
            return copy;
        }
    }
    public class SharedData { public string m_name; public int m_maxStackSize; }
}
public class Inventory
{
    public readonly List<ItemDrop.ItemData> m_inventory = new List<ItemDrop.ItemData>();
    public readonly Dictionary<string, ItemDrop> Prefabs = new Dictionary<string, ItemDrop>();
    public int Slots = 4;
    public bool FailAfterPartialAdd;
    public List<ItemDrop.ItemData> GetAllItems() => m_inventory;
    public int GetEmptySlots() => Slots - m_inventory.Count;
    public bool ContainsItem(ItemDrop.ItemData item) => m_inventory.Contains(item);
    public void Changed() { }
    public bool RemoveItem(ItemDrop.ItemData item, int amount)
    {
        if (!m_inventory.Contains(item)) return false;
        item.m_stack -= amount;
        if (item.m_stack <= 0) m_inventory.Remove(item);
        return true;
    }
    public bool AddItem(ItemDrop.ItemData item)
    {
        int remaining = item.m_stack;
        foreach (ItemDrop.ItemData stack in m_inventory)
        {
            if (stack.m_shared.m_name != item.m_shared.m_name || stack.m_quality != item.m_quality ||
                stack.m_worldLevel != item.m_worldLevel || item.m_shared.m_maxStackSize <= 1) continue;
            int add = Math.Min(remaining, Math.Max(stack.m_shared.m_maxStackSize - stack.m_stack, 0));
            stack.m_stack += add;
            remaining -= add;
            if (FailAfterPartialAdd && add > 0) return false;
            if (remaining == 0) return true;
        }
        if (GetEmptySlots() < 1) return false;
        item.m_stack = remaining;
        item.m_gridPos = new Vector2i(m_inventory.Count, 0);
        m_inventory.Add(item);
        return true;
    }
    public bool TopFirst(ItemDrop.ItemData item) => true;
    public Vector2i FindEmptySlot(bool topFirst)
    {
        for (int x = 0; x < Slots; x++)
            if (!m_inventory.Any(item => item.m_gridPos.x == x && item.m_gridPos.y == 0))
                return new Vector2i(x, 0);
        return new Vector2i(-1, -1);
    }
    public bool AddItem(ItemDrop.ItemData item, int amount, int x, int y)
    {
        if (x < 0 || x >= Slots || y != 0 || amount <= 0 || amount > item.m_stack) return false;
        var existing = m_inventory.FirstOrDefault(candidate => candidate.m_gridPos.x == x && candidate.m_gridPos.y == y);
        if (existing != null)
        {
            if (existing.m_shared.m_name != item.m_shared.m_name || existing.m_quality != item.m_quality ||
                existing.m_worldLevel != item.m_worldLevel || existing.m_stack + amount > existing.m_shared.m_maxStackSize) return false;
            existing.m_stack += amount;
            item.m_stack -= amount;
            return !FailAfterPartialAdd;
        }
        var copy = item.Clone(); copy.m_stack = amount; copy.m_gridPos = new Vector2i(x, y);
        m_inventory.Add(copy); item.m_stack -= amount;
        return true;
    }
    public ItemDrop.ItemData AddItem(string prefab, int amount, int quality, int variant, long crafter, string crafterName)
    {
        if (!Prefabs.TryGetValue(prefab, out ItemDrop definition)) return null;
        ItemDrop.ItemData result = null;
        while (amount > 0)
        {
            ZNetView.m_forceDisableInit = true;
            result = definition.m_itemData.Clone();
            ZNetView.m_forceDisableInit = false;
            result.m_quality = quality;
            result.m_worldLevel = Game.m_worldLevel;
            result.m_stack = Math.Min(amount, result.m_shared.m_maxStackSize);
            amount -= result.m_stack;
            if (!AddItem(result)) return null;
        }
        return result;
    }
}
public class TestSocket
{
    public string Host;
    public string GetHostName() => Host;
}
public class ZNetPeer
{
    public long m_uid;
    public ZRpc m_rpc = new ZRpc();
    public TestSocket m_socket = new TestSocket();
    public bool IsReady() => m_uid != 0;
}
public class ZNet
{
    public static ZNet instance;
    public bool Server;
    public bool Connected = true;
    public List<ZNetPeer> Peers = new List<ZNetPeer>();
    public HashSet<string> m_adminList = new HashSet<string>();
    public bool IsServer() => Server;
    public ZNetPeer GetServerPeer() => !Server && Connected ? Peers.FirstOrDefault(peer => peer.IsReady()) : null;
    public List<ZNetPeer> GetPeers() => Peers;
    public ZNetPeer GetPeer(ZRpc rpc) => Peers.FirstOrDefault(peer => ReferenceEquals(peer.m_rpc, rpc));
    public bool ListContainsId(HashSet<string> admins, string host) => admins.Contains(host);
    public void OnNewConnection(ZNetPeer peer) { }
    public void Disconnect(ZNetPeer peer) { Peers.Remove(peer); }
}
public class ZRpc
{
    public readonly Dictionary<string, Action<ZRpc, ZPackage>> Handlers = new Dictionary<string, Action<ZRpc, ZPackage>>();
    public readonly List<(string Method, byte[] Bytes)> Sent = new List<(string, byte[])>();
    public void Register<T>(string method, Action<ZRpc, T> handler) => Handlers[method] = (rpc, pkg) => handler(rpc, (T)(object)pkg);
    public void Invoke(string method, ZPackage pkg) => Sent.Add((method, pkg.GetArray()));
    public void Receive(string method, ZPackage pkg) => Handlers[method](this, pkg);
}
public class ZPackage
{
    private readonly MemoryStream stream;
    private readonly BinaryReader reader;
    private readonly BinaryWriter writer;
    public ZPackage() : this(Array.Empty<byte>()) { }
    public ZPackage(byte[] bytes)
    {
        stream = new MemoryStream();
        stream.Write(bytes);
        stream.Position = 0;
        reader = new BinaryReader(stream, Encoding.UTF8, true);
        writer = new BinaryWriter(stream, Encoding.UTF8, true);
    }
    public int Size() => (int)stream.Length;
    public int GetPos() => (int)stream.Position;
    public void SetPos(int position) => stream.Position = position;
    public byte[] GetArray() => stream.ToArray();
    public void Write(int value) => writer.Write(value);
    public void Write(long value) => writer.Write(value);
    public void Write(bool value) => writer.Write(value);
    public void Write(string value) => writer.Write(value);
    public void Write(byte[] value) { writer.Write(value.Length); writer.Write(value); }
    public int ReadInt() => reader.ReadInt32();
    public long ReadLong() => reader.ReadInt64();
    public bool ReadBool() => reader.ReadBoolean();
    public string ReadString() => reader.ReadString();
    public byte[] ReadByteArray() => reader.ReadBytes(reader.ReadInt32());
}
namespace TradersExtended
{
    // Serialization/name rules are integration boundaries here, not copies of the production parsers.
    internal enum EditorConfigKind { ItemList, TraderSettings, Unsupported }
    internal class EditorFileInfo
    {
        public string Name; public EditorConfigKind Kind; public string Trader;
        public TradersExtended.ItemsListType ListType; public long Length; public long LastWriteUtcTicks;
    }
    internal static class ConfigEditorSerialization
    {
        public static bool Validate(string file, string content, out string error)
        {
            error = string.Empty;
            try { _ = JToken.Parse(content); return true; }
            catch (Exception e) { error = e.Message; return false; }
        }
    }
    internal static class TradersExtended
    {
        public const string pluginID = "shudnal.TradersExtended";
        public enum ItemsListType { Buy, Sell }
        public static readonly List<string> Warnings = new List<string>();
        public static int Reloads;
        public static void LogWarning(string text) => Warnings.Add(text);
        public static bool ReadConfigs() { Reloads++; return true; }
        public static bool IsSupportedConfigExtension(string ext) => new[] { ".json", ".yaml", ".yml", ".csv" }.Contains(ext);
        public static bool TryParseConfigFileName(string file, out string trader, out ItemsListType list)
        {
            trader = file.Split('.')[0]; list = file.Contains(".sell.") ? ItemsListType.Sell : ItemsListType.Buy;
            return file.Contains(".sell.") || file.Contains(".buy.");
        }
        public static bool TryParseTraderConfigFileName(string file, out string trader)
        {
            trader = file.Split('.')[0]; return file.EndsWith(".config.json");
        }
    }
}
