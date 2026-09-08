using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TradersExtended;
using UnityEngine;
using YamlDotNet.Serialization;

internal static class Program
{
    private const string Rpc = "shudnal.TradersExtended.ConfigEditor";
    private const int Marker = 0x54454332;
    private static int checks;
    private static readonly List<(ConfigEditorOperation Operation, bool Success, string File, string Message, string Payload)> responses = new();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { checks++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static ItemDrop Prefab(string name, int maximum = 50, string shared = null)
    {
        var go = new GameObject { name = name };
        return new ItemDrop { gameObject = go, m_itemData = new ItemDrop.ItemData
        {
            m_dropPrefab = go, m_shared = new ItemDrop.SharedData { m_name = shared ?? "$" + name, m_maxStackSize = maximum }
        }};
    }
    private static ItemDrop.ItemData Stack(ItemDrop prefab, int amount, int quality = 1, int world = 0)
    {
        var item = prefab.m_itemData.Clone(); item.m_stack = amount; item.m_quality = quality; item.m_worldLevel = world;
        return item;
    }

    private static void TestAmounts()
    {
        Check(TradeAmounts.TryGetItemCount(20, 3, out int amount) && amount == 60, "Configured lots produce whole quantities");
        Check(TradeAmounts.TryGetPrice(10, 3, 1, out int price) && price == 30, "Configured lots produce whole prices");
        Check(TradeAmounts.TryGetPrice(3, 3, 0.7, out price) && price == 7, "Flexible sale rounding occurs once for the entire trade");
        Check(TradeAmounts.TryGetPrice(100, 1, 0.99f, out price) && price == 99, "Float percentage cannot add a spurious coin");
        Check(TradeAmounts.TryGetPrice(100, 1, 0.7f, out price, roundUp: false) && price == 70, "Buy floor preserves exact percentage");
        Check(TradeAmounts.TryGetPrice(100, 1, 0.07, out price) && price == 7, "Decimal quote avoids a binary double ceiling error");
        Check(TradeAmounts.TryGetQualityPrice(100, 2, 0.7f, out price) && price == 170, "Quality factor preserves configured decimal value");
        Check(TradeAmounts.MaximumSellLots(1, 100, 10, 0.99f, 99) == 1, "Slider budget uses the exact float quote boundary");
        Check(!TradeAmounts.TryGetItemCount(int.MaxValue, 2, out _), "Quantity overflow is rejected");
        Check(!TradeAmounts.TryGetPrice(int.MaxValue, 2, 1, out _), "Price overflow is rejected");
        Check(!TradeAmounts.TryGetItemCount(0, 1, out _) && !TradeAmounts.TryGetItemCount(1, -1, out _), "Invalid lots are rejected");
        foreach (double factor in new[] { double.NaN, double.PositiveInfinity, -1d })
            Check(!TradeAmounts.TryGetPrice(10, 1, factor, out _), "Nonfinite/negative price factor is rejected");
        Check(TradeAmounts.MaximumBuyLots(20, 10, 100, 65) == 3, "Buy limit respects inventory capacity and whole lots");
        Check(TradeAmounts.MaximumBuyLots(20, 10, 25, 1000) == 2, "Buy limit respects currency");
        Check(TradeAmounts.MaximumSellLots(10, 5, 27, 1, 100) == 2, "Sale excludes incomplete remainder");
        Check(TradeAmounts.MaximumSellLots(10, 5, 100, 1, 9) == 1, "Sale respects trader funds");
        Check(TradeAmounts.MaximumSellLots(10, 3, 100, 0.7, 7) == 3, "Sale slider uses the same aggregate rounding");
        Check(TradeAmounts.MaximumBuyLots(1, 1, int.MaxValue, int.MaxValue) == TradeAmounts.MaximumSliderLots, "Slider stays in exact integer float range");
        Check(TradeAmounts.ClampBalance((long)int.MaxValue + 1) == int.MaxValue, "Balance addition saturates without overflow");
        Check(TradeAmounts.ClampBalance(-5) == 0, "Negative balance is clamped");
        Check(TradeAmounts.TryEncodeStack(50, 2, 1000000, out int encoded) && encoded == 2000050, "Quality encoding stays compatible");
        Check(!TradeAmounts.TryEncodeStack(1000000, 1, 1000000, out _), "Encoded stack cannot overlap quality digits");
        Check(!TradeAmounts.TryEncodeStack(1, int.MaxValue, 1000000, out _), "Quality encoding overflow is rejected");
        for (int budget = 1; budget < 80; budget++)
        {
            int lots = TradeAmounts.MaximumSellLots(7, 3, 105, 1.37, budget);
            Check(lots >= 0 && lots <= 15, "Sale search stays in bounds");
            Check(lots == 0 || TradeAmounts.TryGetPrice(3, lots, 1.37, out price) && price <= budget, "Selected maximum is affordable");
            Check(lots == 15 || TradeAmounts.TryGetPrice(3, lots + 1, 1.37, out price) && price > budget, "Selected maximum is maximal");
        }
    }

    private static void TestSelection()
    {
        Check(TradeSelection.RestoreIndex(new[] { "berry", "wood" }, "berry", 0, (a, b) => a == b) == 0, "Sale retains the same offer");
        Check(TradeSelection.RestoreIndex(new[] { "wood", "stone" }, "berry", 1, (a, b) => a == b) == 1, "Exhausted offer selects its neighbour");
        Check(TradeSelection.RestoreIndex(Array.Empty<string>(), "berry", 0, (a, b) => a == b) == -1, "Empty sale pane remains unselected");
        Check(TradeSelection.RestoreIndex(new[] { "buyback", "bait" }, "bait", 0, (a, b) => a == b) == 1, "Inserted buyback does not replace buy selection");
        Check(TradeSelection.RestoreIndex(new[] { "bait", "bait" }, "bait", 1, (a, b) => a == b) == 1, "Duplicate offers prefer the same row");
    }

    private static void TestInventory()
    {
        Game.m_worldLevel = 1;
        var coins = Prefab("Coins", 100, "$money");
        var impostor = Prefab("Tokens", 100, "$money");
        var inventory = new Inventory { Slots = 3 };
        var old = Stack(coins, 70, world: 0);
        var payable = Stack(coins, 8, world: 1);
        inventory.m_inventory.AddRange(new[] { old, payable, Stack(impostor, 90, world: 1) });
        Check(TradeInventory.CountCurrency(inventory, coins) == 8, "Currency uses exact prefab and eligible world level");
        Check(TradeInventory.Capacity(inventory, coins.m_itemData, 2, 1) == 0, "Wrong quality cannot supply stack capacity");
        Check(TradeInventory.PlanRemoval(inventory, new[] { payable, payable }, 8, out var plan) && plan.Count == 1, "Removal plan deduplicates references");
        Check(!TradeInventory.PlanRemoval(inventory, new[] { payable }, 9, out _), "Ineligible stacks cannot make up a shortage");

        var purchase = Prefab("Purchase", 50);
        var blocker = Prefab("Blocker", 1);
        inventory = new Inventory { Slots = 2 };
        var paymentStack = Stack(coins, 10, world: 1);
        inventory.m_inventory.AddRange(new[] { paymentStack, Stack(blocker, 1, world: 1) });
        Check(TradeInventory.Capacity(inventory, purchase.m_itemData, 1, 1) == 0, "Full inventory has no pre-payment purchase capacity");
        Check(TradeInventory.PlanRemoval(inventory, new[] { paymentStack }, 10, out var fullPayment) &&
            TradeInventory.CapacityAfterRemoval(inventory, purchase.m_itemData, 1, 1, fullPayment) == 50,
            "A payment that consumes a currency stack exposes the freed slot to purchase preflight");
        Check(TradeInventory.PlanRemoval(inventory, new[] { paymentStack }, 5, out var partialPayment) &&
            TradeInventory.CapacityAfterRemoval(inventory, purchase.m_itemData, 1, 1, partialPayment) == 0,
            "A partial payment does not invent an empty slot");
        Check(TradeInventory.CapacityAfterRemoval(inventory, coins.m_itemData, 1, 1, partialPayment) == 95,
            "When the bought item is also the currency, planned removal exposes stack space without double-counting slots");

        inventory = new Inventory { Slots = 3 };
        old = Stack(coins, 70, world: 0);
        payable = Stack(coins, 8, world: 1);
        inventory.m_inventory.AddRange(new[] { old, payable, Stack(impostor, 90, world: 1) });
        Check(TradeInventory.PlanRemoval(inventory, new[] { payable }, 8, out plan), "Rollback payment plan is recreated after capacity scenarios");
        using (var snapshot = new TradeInventory.Snapshot(inventory))
            Check(TradeInventory.Remove(inventory, plan), "Payment can be removed");
        Check(inventory.ContainsItem(payable) && payable.m_stack == 8 && ReferenceEquals(inventory.m_inventory[0], old), "Rollback restores original identities and quantities");

        var arrows = Prefab("ArrowWood", 50);
        inventory = new Inventory { Slots = 3 };
        inventory.Prefabs.Add("ArrowWood", arrows);
        var arrowStack = Stack(arrows, 45, world: 1);
        inventory.m_inventory.Add(arrowStack);
        inventory.FailAfterPartialAdd = true;
        ZNetView.m_forceDisableInit = true;
        using (var snapshot = new TradeInventory.Snapshot(inventory))
            Check(!TradeInventory.AddPrefab(inventory, arrows, 60, 1), "A partial native insertion is detected");
        Check(arrowStack.m_stack == 45 && inventory.m_inventory.Count == 1, "Partial insertion is fully rolled back");
        Check(ZNetView.m_forceDisableInit, "Native initialization flag is restored");
        inventory.FailAfterPartialAdd = false;
        using (var snapshot = new TradeInventory.Snapshot(inventory))
        {
            Check(TradeInventory.AddPrefab(inventory, arrows, 60, 1), "Purchase spans multiple native stacks");
            snapshot.Commit();
        }
        Check(inventory.m_inventory.Sum(item => item.m_stack) == 105, "Committed multi-stack purchase is complete");

        var sword = Prefab("Sword", 1);
        var first = Stack(sword, 1, 2, 1); first.m_customData["magic"] = "first";
        var second = Stack(sword, 1, 2, 1); second.m_customData["magic"] = "second";
        inventory = new Inventory { Slots = 2 };
        inventory.m_inventory.AddRange(new[] { first, second });
        Check(TradeInventory.PlanRemoval(inventory, new[] { first, second }, 2, out plan), "Bulk receipt includes every exact source");
        var receipt = TradeInventory.CopyRemovedItems(plan);
        Check(receipt[0].m_customData["magic"] == "first" && receipt[1].m_customData["magic"] == "second", "Receipt keeps per-item custom data");
        Check(!TradeInventory.CanAddSavedItems(inventory, receipt), "Receipt preflight accounts for all items together");
        inventory.m_inventory.Clear();
        Check(TradeInventory.CanAddSavedItems(inventory, receipt), "Complete receipt fits two slots");
        using (var snapshot = new TradeInventory.Snapshot(inventory))
        {
            Check(TradeInventory.AddSavedItems(inventory, receipt), "Receipt restores all original items");
            snapshot.Commit();
        }
        Check(inventory.m_inventory.Select(item => item.m_customData["magic"]).SequenceEqual(new[] { "first", "second" }), "Buyback does not clone one representative item");
        var decorated = Prefab("DecoratedArrow", 50);
        var kept = Stack(decorated, 10, world: 1); kept.m_customData["origin"] = "kept";
        var returned = Stack(decorated, 10, world: 1); returned.m_customData["origin"] = "returned";
        inventory = new Inventory { Slots = 1 };
        inventory.m_inventory.Add(kept);
        Check(!TradeInventory.CanAddSavedItems(inventory, new[] { returned }), "Different saved metadata cannot supply merge space");
        inventory.Slots = 2;
        using (var snapshot = new TradeInventory.Snapshot(inventory))
        {
            Check(TradeInventory.AddSavedItems(inventory, new[] { returned }), "Saved stack uses a separate slot when metadata differs");
            snapshot.Commit();
        }
        Check(inventory.m_inventory.Count == 2 && kept.m_stack == 10 &&
            inventory.m_inventory[1].m_customData["origin"] == "returned", "Stackable buyback metadata is not discarded by native auto-stack");
        Game.m_worldLevel = 0;
    }

    private static void TestSerialization(string directory)
    {
        var csv = CsvRecords.Parse("\uFEFFprefab,price,requiredGlobalKey\r\nWood,2,\"one,two\"\r\n\"A\"\"B\",3,\"line1\nline2\"\r\n");
        Check(csv.Count == 3 && csv[1][2] == "one,two" && csv[2][0] == "A\"B" && csv[2][2] == "line1\nline2", "CSV roundtrip punctuation/newlines/BOM");
        Check(CsvRecords.Parse("\"\"").Count == 1, "An empty quoted terminal field is retained");
        Throws<FormatException>(() => CsvRecords.Parse("\"bad"));
        Throws<FormatException>(() => CsvRecords.Parse("\"12\"3"));
        Throws<FormatException>(() => CsvRecords.Parse("x\"y"));
        var root = JObject.Parse("{\"Trader repair\":{\"Weapons\":false},\"Misc\":{\"Fixed position for Store GUI\":{\"x\":100,\"y\":-50}},\"Unknown extension\":{\"values\":[1,true,null,\"x\"]}}");
        string yaml = new SerializerBuilder().DisableAliases().Build().Serialize(ConfigPersistence.ToPlainData(root));
        object decoded = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build().Deserialize<object>(yaml);
        Check(JToken.DeepEquals(root, JToken.Parse(JsonConvert.SerializeObject(decoded))), "Trader YAML preserves scalar types, vectors and unknown fields");
        string path = Path.Combine(directory, "atomic.json");
        ConfigPersistence.WriteAtomically(path, "[1]", true);
        Throws<IOException>(() => ConfigPersistence.WriteAtomically(path, "[2]", true));
        Check(File.ReadAllText(path) == "[1]", "Concurrent create cannot overwrite original");
        ConfigPersistence.WriteAtomically(path, "[3]", false);
        Check(File.ReadAllText(path) == "[3]", "Atomic replacement updates the original");
        Check(ConfigPersistence.ReadBounded(path) == "[3]", "Bounded reader preserves file contents");
        string oversized = Path.Combine(directory, "oversized.json");
        using (var stream = File.Create(oversized)) stream.SetLength(ConfigPersistence.MaximumFileBytes + 1L);
        Throws<InvalidDataException>(() => ConfigPersistence.ReadBounded(oversized));
        File.Delete(oversized);
        File.Delete(path);
        Throws<FileNotFoundException>(() => ConfigPersistence.WriteAtomically(path, "[4]", false));
        Check(!File.Exists(path) && !Directory.GetFiles(directory, "*.tmp").Any(), "Failed replacement leaves no ghost file or staging file");
    }

    private static ZPackage Request(ConfigEditorOperation operation, string file, string content, long id)
    {
        var pkg = new ZPackage(); pkg.Write((int)operation); pkg.Write(file); pkg.Write(content); pkg.Write(Marker); pkg.Write(id);
        return new ZPackage(pkg.GetArray());
    }
    private static ZPackage Response(ConfigEditorOperation operation, bool success, string file, string payload, long id)
    {
        var pkg = new ZPackage(); pkg.Write((int)operation); pkg.Write(success); pkg.Write(file); pkg.Write(""); pkg.Write(payload); pkg.Write(Marker); pkg.Write(id);
        return new ZPackage(pkg.GetArray());
    }
    private static long LastId(ZRpc rpc)
    {
        var pkg = new ZPackage(rpc.Sent.Last().Bytes); pkg.SetPos(pkg.Size() - 8); return pkg.ReadLong();
    }
    private static void TestTransport(string directory)
    {
        BepInEx.Paths.ConfigPath = directory;
        string editor = ConfigEditorTransport.EditorDirectory;
        Directory.CreateDirectory(editor);
        string path = Path.Combine(editor, "haldor.buy.json");
        File.WriteAllText(path, "[]");
        var guest = new ZNetPeer { m_uid = 99, m_socket = new TestSocket { Host = "guest" } };
        var server = new ZNet { Server = true, Peers = new List<ZNetPeer> { guest } };
        server.m_adminList.Add("admin");
        ZNet.instance = server; Time.realtimeSinceStartup = 0;
        ConfigEditorTransport.RegisterRpc();
        guest.m_rpc.Receive(Rpc + "Request", Request(ConfigEditorOperation.Write, "haldor.buy.json", "[1]", 1));
        Check(File.ReadAllText(path) == "[]", "Guest cannot write, even using an arbitrary peer UID");
        Check(guest.m_rpc.Sent.Count == 1, "Unauthorized operation receives a failure response");
        server.m_adminList.Add("guest");
        guest.m_rpc.Receive(Rpc + "Request", Request(ConfigEditorOperation.Write, "haldor.buy.json", "[1]", 2));
        Check(File.ReadAllText(path) == "[1]", "Authenticated administrator can write");
        server.m_adminList.Remove("guest");
        guest.m_rpc.Receive(Rpc + "Request", Request(ConfigEditorOperation.Delete, "haldor.buy.json", "", 3));
        Check(File.Exists(path), "Admin status is rechecked for each operation");
        server.m_adminList.Add("guest");
        guest.m_rpc.Receive(Rpc + "Request", Request(ConfigEditorOperation.Create, "../escape.buy.json", "[]", 4));
        guest.m_rpc.Receive(Rpc + "Request", Request(ConfigEditorOperation.Create, "..\\escape.buy.json", "[]", 5));
        Check(!File.Exists(Path.Combine(directory, "escape.buy.json")), "Both path separators are rejected");
        if (!OperatingSystem.IsWindows())
        {
            string target = Path.Combine(directory, "outside.json"); File.WriteAllText(target, "[10]");
            File.CreateSymbolicLink(Path.Combine(editor, "link.buy.json"), target);
            guest.m_rpc.Receive(Rpc + "Request", Request(ConfigEditorOperation.Write, "link.buy.json", "[11]", 6));
            Check(File.ReadAllText(target) == "[10]", "Symbolic-link writes are rejected");
        }

        var serverPeer = new ZNetPeer { m_uid = 1 };
        var client = new ZNet { Server = false, Peers = new List<ZNetPeer> { serverPeer } };
        ZNet.instance = client;
        ConfigEditorTransport.RequestList();
        long oldId = LastId(serverPeer.m_rpc);
        Time.realtimeSinceStartup = 16;
        ConfigEditorTransport.RequestList();
        long freshId = LastId(serverPeer.m_rpc);
        Check(freshId > oldId, "Timed-out access query is retried with a fresh ID");
        int sent = serverPeer.m_rpc.Sent.Count;
        serverPeer.m_rpc.Receive(Rpc + "Response", Response(ConfigEditorOperation.Access, true, "", "1", oldId));
        Check(serverPeer.m_rpc.Sent.Count == sent && !ConfigEditorTransport.CanEditTarget, "Stale access response cannot grant access");
        serverPeer.m_rpc.Receive(Rpc + "Response", Response(ConfigEditorOperation.Access, true, "", "1", freshId));
        Check(ConfigEditorTransport.CanEditTarget && serverPeer.m_rpc.Sent.Count == sent + 1, "Current access response starts queued list request");
        long listId = LastId(serverPeer.m_rpc);
        serverPeer.m_rpc.Receive(Rpc + "Response", Response(ConfigEditorOperation.List, true, "", "[]", listId));
        ConfigEditorTransport.RequestRead("haldor.buy.json");
        long readId = LastId(serverPeer.m_rpc);
        serverPeer.m_rpc.Receive(Rpc + "Response", Response(ConfigEditorOperation.Read, true, "hildir.buy.json", "[]", readId));
        Check(responses.Last().Success == false && responses.Last().File == "haldor.buy.json", "Unexpected response file is surfaced as an error");

        serverPeer.m_rpc.Sent.Clear();
        string large = new string(' ', 100000) + "[3]";
        ConfigEditorTransport.RequestWrite("haldor.buy.json", large);
        Check(serverPeer.m_rpc.Sent.Count > 1 && serverPeer.m_rpc.Sent.All(item => item.Bytes.Length <= EditorTransferBuffer.ChunkBytes + 24), "Large upload uses bounded fragments");
        var upload = serverPeer.m_rpc.Sent.ToArray(); guest.m_rpc.Sent.Clear();
        ZNet.instance = server;
        foreach (var part in upload) guest.m_rpc.Receive(part.Method, new ZPackage(part.Bytes));
        Check(File.ReadAllText(path) == large, "Authorized fragmented write reconstructs the exact contents");

        ZNet.instance = new ZNet { Server = false, Connected = false };
        int before = responses.Count;
        ConfigEditorTransport.RequestWrite("haldor.buy.json", "[7]");
        Check(responses.Count == before + 1 && !responses.Last().Success && File.ReadAllText(path) == large, "Connecting client never falls back to writing local files");
        ConfigEditorTransport.CancelPendingRequest(); ZNet.instance = null;
    }

    private static void TestTransfers()
    {
        var transfer = new EditorTransferBuffer(1, 3, 5, 10, 0);
        Check(transfer.Add(1, 3, 5, 0, new byte[] { 1, 2 }, 1) == null, "Partial transfer does not expose partial content");
        Check(transfer.Add(1, 3, 5, 2, new byte[] { 3, 4, 5 }, 2).SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "Transfer retains exact bytes");
        Throws<InvalidDataException>(() => new EditorTransferBuffer(1, 3, 11, 10, 0));
        Throws<InvalidDataException>(() => new EditorTransferBuffer(0, 3, 1, 10, 0));
        transfer = new EditorTransferBuffer(1, 3, 5, 10, 0);
        Throws<InvalidDataException>(() => transfer.Add(1, 3, 5, 2, new byte[] { 1 }, 1));
        Throws<InvalidDataException>(() => transfer.Add(2, 3, 5, 0, new byte[] { 1 }, 1));
        Throws<InvalidDataException>(() => transfer.Add(1, 4, 5, 0, new byte[] { 1 }, 1));
    }

    private static void ParseSources(string root)
    {
        int files = 0;
        foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or ".git")))
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.CSharp11), path);
            Diagnostic[] errors = tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            Check(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
            files++;
        }
        System.Console.WriteLine($"Parsed {files} C# files with Roslyn (C# 11).");
    }

    public static int Main(string[] args)
    {
        string directory = Path.Combine(Path.GetTempPath(), "TradersExtended-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ConfigEditorTransport.ResponseReceived += (operation, success, file, message, payload) => responses.Add((operation, success, file, message, payload));
        try
        {
            TestAmounts(); TestSelection(); TestInventory(); TestSerialization(directory); TestTransfers(); TestTransport(directory);
            ParseSources(Path.GetFullPath(args.Length > 0 ? args[0] : "../.."));
            System.Console.WriteLine($"PASS: {checks} assertions. Runtime integration with Valheim remains a separate smoke test.");
            return 0;
        }
        catch (Exception exception) { System.Console.Error.WriteLine(exception); return 1; }
        finally { Directory.Delete(directory, true); }
    }
}
