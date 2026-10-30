using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using EPMultiCharger;
using ElectricalProgressive.Content.Block.ECharger;
using ElectricalProgressive.Interface;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

public class Stub : DispatchProxy
{
    public System.Func<MethodInfo, object[], object> Handler;
    protected override object Invoke(MethodInfo method, object[] args) => Handler(method, args);
    public static T For<T>(System.Func<MethodInfo, object[], object> handler) where T : class
    {
        var proxy = Create<T, Stub>(); ((Stub)(object)proxy).Handler = handler; return proxy;
    }
    public static object Default(MethodInfo method) => method.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
}
public class Battery : Block, IEnergyStorageItem
{
    public int receiveEnergy(ItemStack stack, int amount)
    {
        int current = stack.Attributes.GetInt("durability", 0);
        int delta = Math.Min(Durability - current, amount / 20);
        stack.Attributes.SetInt("durability", current + delta);
        return delta * 20;
    }
}
class Tests
{
    static int checks;
    static void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL: " + name); checks++; }
    static void Main(string[] args)
    {
        var dirs = new[] { Path.Combine(args[0], "build"), args[1], Path.Combine(args[1], "Lib"), Path.Combine(args[1], "Mods"), args[2], args[3] };
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            string path = dirs.Select(d => Path.GetFullPath(Path.Combine(d, name.Name + ".dll"))).FirstOrDefault(File.Exists);
            return path == null ? null : context.LoadFromAssemblyPath(path);
        };
        try { Run(); Console.WriteLine($"PASS: {checks} checks against real EP 3.3.1 charging, inventory, automation, save/load, meshes and patch isolation"); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    static ItemStack Tool(int id, int charge = 0, bool chargeable = true)
    {
        var item = new Item { ItemId = id, Code = new AssetLocation("test:tool" + id), Durability = 1000, Attributes = new JsonObject(JObject.FromObject(new { chargable = chargeable, consume = 20 })) };
        var stack = new ItemStack(item); stack.Attributes.SetInt("durability", charge); return stack;
    }
    static readonly MethodInfo NativeTick = AccessTools.Method(typeof(BlockEntityECharger), "OnServerTick");
    static readonly FieldInfo BlockField = AccessTools.Field(typeof(BlockEntity), "<Block>k__BackingField");
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run()
    {
        var mod = new MultiChargerMod();
        mod.InstallPatches();
        var serverMod = new MultiChargerMod();
        serverMod.InstallPatches();
        mod.InstallPatches();
        Check(Harmony.GetPatchInfo(NativeTick).Transpilers.Count(p => p.owner == "epmulticharger") == 1, "client/server starts install only one transpiler");
        try
        {
            var be = new BEMultiCharger { Pos = new BlockPos(1, 2, 3) };
            var blocks = new Dictionary<string, Block>();
            foreach (string state in new[] { "enabled", "disabled", "burned" }) blocks[state] = new Block {
                BlockId = blocks.Count + 10,
                Code = new AssetLocation("epmulticharger:multicharger-" + state + "-south"),
                Variant = new RelaxedReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["state"] = state, ["side"] = "south" }),
                Attributes = new JsonObject(JObject.FromObject(new { maxConsumption = 200 }))
            };
            BlockField.SetValue(be, blocks["enabled"]);
            var items = Enumerable.Range(1, 4).Select(i => Tool(i)).ToArray();
            IBlockAccessor blockAccessor = Stub.For<IBlockAccessor>((m,a) => {
                if (m.Name == "ExchangeBlock") { BlockField.SetValue(be, blocks.Values.Single(b => b.BlockId == (int)a[0])); return null; }
                if (m.Name == "GetBlockEntity") return be;
                return Stub.Default(m);
            });
            var drops = new List<ItemStack>();
            IWorldAccessor world = Stub.For<IWorldAccessor>((m,a) => {
                if (m.Name == "get_BlockAccessor") return blockAccessor;
                if (m.Name == "get_Side") return EnumAppSide.Server;
                if (m.Name == "SpawnItemEntity") { drops.Add((ItemStack)a[0]); return null; }
                if (m.Name == "GetBlock") return blocks.Values.FirstOrDefault(b => b.Code.Equals(a[0]));
                if (m.Name == "GetItem") return items.Select(s => s.Item).FirstOrDefault(item => a[0] is int id ? item.Id == id : item.Code.Equals(a[0]));
                return Stub.Default(m);
            });
            var network = Stub.For<IInventoryNetworkUtil>((m,a) => Stub.Default(m));
            var registry = Stub.For<IClassRegistryAPI>((m,a) => m.Name == "CreateInvNetworkUtil" ? network : Stub.Default(m));
            var logger = Stub.For<ILogger>((m,a) => Stub.Default(m));
            ICoreAPI api = Stub.For<ICoreServerAPI>((m,a) => m.Name == "get_World" ? world : m.Name == "get_Side" ? EnumAppSide.Server : m.Name == "get_ClassRegistry" ? registry : m.Name == "get_Logger" ? logger : Stub.Default(m));
            be.Api = api;
            var behavior = new BEBehaviorECharger(be);
            be.Behaviors.Add(behavior);
            Check(be.Inventory.Count == 4 && be.Inventory.All(s => s.MaxSlotStackSize == 1), "four one-item bays");
            Check(!behavior.Working && behavior.Consume_request() == 0, "empty charger draws zero");
            for (int i = 0; i < 4; i++) be.Inventory[i].Itemstack = items[i];
            Check(behavior.Working && behavior.Consume_request() == 200, "four unfinished items request just 200W");
            behavior.Consume_receive(200);
            NativeTick.Invoke(be, new object[] { 1f });
            Check(items[0].Attributes.GetInt("durability") == 10 && items.Skip(1).All(s => s.Attributes.GetInt("durability") == 0), "native tick charges exactly one item at native speed");
            items[0].Attributes.SetInt("durability", 995);
            NativeTick.Invoke(be, new object[] { 1f });
            Check(items[0].Attributes.GetInt("durability") == 1000, "charge clamped at capacity");
            NativeTick.Invoke(be, new object[] { 1f });
            Check(items[1].Attributes.GetInt("durability") == 10 && items[2].Attributes.GetInt("durability") == 0, "automatically advances without moving stacks");
            Check(ReferenceEquals(be.Inventory[0].Itemstack, items[0]) && ReferenceEquals(be.Inventory[1].Itemstack, items[1]), "physical bays remain stable");
            be.Inventory[1].Itemstack = null;
            NativeTick.Invoke(be, new object[] { 1f });
            Check(items[2].Attributes.GetInt("durability") == 10, "empty bay skipped");
            behavior.Consume_receive(100);
            NativeTick.Invoke(be, new object[] { 1f });
            Check(items[2].Attributes.GetInt("durability") == 15, "partial power gives native partial charging rate");
            be.Inventory[1].Itemstack = Tool(9, 0, false);
            Check(be.NextUnfinished == 2, "invalid item cannot stall queue");
            be.Inventory[1].Itemstack = null;
            behavior.Consume_receive(0);
            int before = items[2].Attributes.GetInt("durability");
            NativeTick.Invoke(be, new object[] { 1f });
            Check(items[2].Attributes.GetInt("durability") == before && be.Block.Variant["state"] == "disabled", "no power means no charge");
            BlockField.SetValue(be, blocks["burned"]);
            behavior.Consume_receive(200);
            NativeTick.Invoke(be, new object[] { 1f });
            Check(items[2].Attributes.GetInt("durability") == before, "burned charger does not charge");
            BlockField.SetValue(be, blocks["enabled"]);
            foreach (var slot in be.Inventory) if (!slot.Empty) slot.Itemstack.Attributes.SetInt("durability", 1000);
            Check(!behavior.Working && behavior.Consume_request() == 0, "all full draws no power");
            Check(ReferenceEquals(be.Inventory.GetAutoPullFromSlot(BlockFacing.DOWN), be.Inventory[0]), "automation pulls completed item");
            var incoming = new DummySlot(Tool(10));
            Check(ReferenceEquals(be.Inventory.GetAutoPushIntoSlot(BlockFacing.UP, incoming), be.Inventory[1]), "automation finds empty bay");
            Check(be.Inventory.GetAutoPushIntoSlot(BlockFacing.UP, new DummySlot(Tool(11, 0, false))) == null, "automation rejects nonchargeable items");
            foreach (var slot in be.Inventory) slot.Itemstack = Tool(20 + be.Inventory.GetSlotId(slot), 50);
            Check(be.Inventory.GetAutoPullFromSlot(BlockFacing.DOWN) == null, "automation does not extract unfinished items");
            var battery = new Battery { BlockId = 50, Code = new AssetLocation("test:battery"), Durability = 1000, Attributes = new JsonObject(new JObject()) };
            var batteryStack = new ItemStack(battery); batteryStack.Attributes.SetInt("durability", 0);
            be.Inventory[0].Itemstack = batteryStack;
            behavior.Consume_receive(200);
            NativeTick.Invoke(be, new object[] { 1f });
            Check(batteryStack.Attributes.GetInt("durability") == 10, "native battery interface preserved");
            Check(ChargeRules.Bay(0.25,0.25) == 0 && ChargeRules.Bay(0.75,0.25) == 1 && ChargeRules.Bay(0.25,0.75) == 2 && ChargeRules.Bay(0.75,0.75) == 3, "four click quadrants map correctly");
            var normal = new BlockEntityECharger();
            BlockField.SetValue(normal, blocks["enabled"]);
            var normalBehavior = new BEBehaviorECharger(normal);
            normal.Behaviors.Add(normalBehavior);
            normal.Inventory[0].Itemstack = Tool(77);
            normalBehavior.Consume_receive(200);
            NativeTick.Invoke(normal, new object[] { 1f });
            Check(normal.Inventory.Count == 1 && normal.Inventory[0].Itemstack.Attributes.GetInt("durability") == 10, "ordinary chargers unchanged");
            // Real inventory serialization includes every physical bay and charge value.
            for (int i = 0; i < 4; i++) be.Inventory[i].Itemstack = items[i];
            be.Inventory.LateInitialize("epmulticharger-1/2/3", api);
            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            var restored = new BEMultiCharger { Pos = be.Pos, Api = api };
            BlockField.SetValue(restored, blocks["enabled"]);
            restored.FromTreeAttributes(tree, world);
            Check(restored.Inventory.Count == 4, "save/load retains four bays");
            Check(restored.Inventory.All(s => s.MaxSlotStackSize == 1), "save/load retains one-item bay limits");
            for (int i = 0; i < 4; i++) Check(restored.Inventory[i].Itemstack?.Id == items[i].Id && restored.Inventory[i].Itemstack.Attributes.GetInt("durability") == items[i].Attributes.GetInt("durability"), "saved item/charge retained in bay " + i);
            // Exercise the patched four-bay mesh path using a deterministic source mesh.
            var clientApi = Stub.For<ICoreAPI>((m,a) => m.Name == "get_World" ? world : m.Name == "get_Side" ? EnumAppSide.Client : Stub.Default(m));
            restored.Api = clientApi;
            var meshes = new MeshData[4];
            AccessTools.Field(typeof(BlockEntityECharger), "_meshes").SetValue(restored, meshes);
            var meshHarness = new Harmony("epmulticharger.tests.mesh");
            try
            {
                meshHarness.Patch(AccessTools.Method(typeof(BlockEntityECharger), "GenMesh"), prefix: new HarmonyMethod(typeof(Tests), nameof(FakeMesh)));
                for (int i = 0; i < 4; i++) restored.UpdateMesh(i);
                Check(meshes.All(m => m != null), "all four item meshes generated");
                Check(meshes[1].xyz[0] > meshes[0].xyz[0] + 0.4f && meshes[2].xyz[2] > meshes[0].xyz[2] + 0.4f, "item meshes occupy distinct matching bays");
            }
            finally { meshHarness.UnpatchAll("epmulticharger.tests.mesh"); }
            restored.Api = api;
            Check(!restored.Interact(null, 4) && !restored.Interact(null, -1), "out-of-range bay rejected");
            int previousDrops = drops.Count;
            restored.OnBlockBroken(null);
            Check(drops.Count == previousDrops + 4 && restored.Inventory.Empty, "breaking returns every stored item exactly once");
        }
        finally { serverMod.Dispose(); mod.Dispose(); }
        Check(Harmony.GetPatchInfo(NativeTick)?.Owners.Contains("epmulticharger") != true, "patch cleanup");
        for (int i = 0; i < 5; i++)
        {
            var client = new MultiChargerMod();
            var server = new MultiChargerMod();
            client.InstallPatches();
            server.InstallPatches();
            Check(Harmony.GetPatchInfo(NativeTick).Transpilers.Count(p => p.owner == "epmulticharger") == 1, "world rejoin has only one charging hook");
            client.Dispose();
            client.Dispose();
            Check(Harmony.GetPatchInfo(NativeTick)?.Owners.Contains("epmulticharger") == true, "client disposal retains active server hook");
            server.Dispose();
            Check(Harmony.GetPatchInfo(NativeTick)?.Owners.Contains("epmulticharger") != true, "both sides disposed removes hook");
        }
    }
    public static bool FakeMesh(ref MeshData __result) { __result = QuadMeshUtil.GetCustomQuadModelData(0, 0, 0, 1, 1); return false; }
}
