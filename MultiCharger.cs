using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using ElectricalProgressive.Content.Block.ECharger;
using ElectricalProgressive.Interface;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace EPMultiCharger;

public sealed class MultiChargerMod : ModSystem
{
    private const string PatchId = "epmulticharger";
    private static readonly object PatchLock = new();
    private static Harmony sharedHarmony;
    private static int patchUsers;
    private bool holdsPatches;
    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("BlockEPMultiCharger", typeof(BlockMultiCharger));
        api.RegisterBlockEntityClass("BEEPMultiCharger", typeof(BEMultiCharger));
        InstallPatches();
    }

    public void InstallPatches()
    {
        lock (PatchLock)
        {
            if (holdsPatches) return;
            // A single-player process starts separate client and server systems.
            // Patches are process-wide, so install once and release only after
            // both sides finish. One side leaving must not unpatch the other.
            if (sharedHarmony == null)
            {
                var harmony = new Harmony(PatchId);
                try
                {
                    var route = new HarmonyMethod(typeof(MultiChargerMod), nameof(RouteSlot));
                    harmony.Patch(AccessTools.Method(typeof(BlockEntityECharger), "OnServerTick"), transpiler: route);
                    harmony.Patch(AccessTools.PropertyGetter(typeof(BEBehaviorECharger), "Working"), transpiler: route);
                    harmony.Patch(AccessTools.Method(typeof(BlockEntityECharger), "UpdateMesh"), prefix: new HarmonyMethod(typeof(MultiChargerMod), nameof(UpdateMesh)));
                    sharedHarmony = harmony;
                }
                catch { harmony.UnpatchAll(PatchId); throw; }
            }
            patchUsers++;
            holdsPatches = true;
        }
    }

    // Preserve EP's actual charging/power code. Only its inventory index changes,
    // and only for our subclass. Fail closed if a future EP build changes the IL.
    public static IEnumerable<CodeInstruction> RouteSlot(IEnumerable<CodeInstruction> input)
    {
        var code = input.Select(c => new CodeInstruction(c)).ToList();
        int changes = 0;
        for (int i = 1; i < code.Count - 1; i++)
        {
            if (code[i].opcode == OpCodes.Ldc_I4_0 &&
                code[i - 1].operand is MethodInfo previous && previous.Name == "get_Inventory" &&
                code[i + 1].operand is MethodInfo next && next.Name == "get_Item" && next.DeclaringType == typeof(InventoryBase))
            {
                code[i].opcode = OpCodes.Ldarg_0;
                code.Insert(++i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MultiChargerMod), nameof(ChargingSlot))));
                changes++;
            }
        }
        if (changes != 1) throw new InvalidOperationException("Unsupported EP charger version: expected exactly one charging slot reference, found " + changes);
        return code;
    }

    public static int ChargingSlot(object owner)
    {
        var be = owner as BlockEntity ?? (owner as BlockEntityBehavior)?.Blockentity;
        return be is BEMultiCharger multi ? Math.Max(0, multi.NextUnfinished) : 0;
    }

    private static bool UpdateMesh(BlockEntityECharger __instance, int __0)
    {
        if (__instance is not BEMultiCharger multi) return true;
        multi.RefreshMesh(__0);
        return false;
    }

    public override void Dispose()
    {
        lock (PatchLock)
        {
            if (!holdsPatches) return;
            holdsPatches = false;
            if (--patchUsers == 0)
            {
                sharedHarmony?.UnpatchAll(PatchId);
                sharedHarmony = null;
            }
        }
        base.Dispose();
    }
}

public static class ChargeRules
{
    public static bool Accepts(ItemStack stack) => stack?.StackSize > 0 && stack.Collectible?.Attributes != null &&
        (stack.Item != null ? stack.Collectible.Attributes["chargable"].AsBool(false) : stack.Block is IEnergyStorageItem);
    public static bool NeedsCharge(ItemStack stack) => Accepts(stack) &&
        stack.Attributes.GetInt("durability", 0) < stack.Collectible.GetMaxDurability(stack);
    public static int Bay(double x, double z) => (x >= 0.5 ? 1 : 0) + (z >= 0.5 ? 2 : 0);
}

public sealed class MultiInventory : InventoryECharger
{
    public MultiInventory() : base(4, "epmulticharger", "unplaced", null, null)
    {
        foreach (var slot in this) slot.MaxSlotStackSize = 1;
    }
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing face, ItemSlot sourceSlot)
        => ChargeRules.Accepts(sourceSlot?.Itemstack) ? this.FirstOrDefault(s => s.Empty) : null;
    public override ItemSlot GetAutoPullFromSlot(BlockFacing face)
        => this.FirstOrDefault(s => !s.Empty && ChargeRules.Accepts(s.Itemstack) && !ChargeRules.NeedsCharge(s.Itemstack));
}

public sealed class BlockMultiCharger : BlockECharger
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer player, BlockSelection selection)
    {
        if (!world.Claims.TryAccess(player, selection.Position, EnumBlockAccessFlags.Use)) return false;
        if (world.BlockAccessor.GetBlockEntity(selection.Position) is not BEMultiCharger be) return false;
        if (world.Side == EnumAppSide.Client) return true;
        return be.Interact(player, ChargeRules.Bay(selection.HitPosition.X, selection.HitPosition.Z));
    }
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer player)
        => new[] { new WorldInteraction { ActionLangCode = "epmulticharger:interact", MouseButton = EnumMouseButton.Right } };
}

public sealed class BEMultiCharger : BlockEntityECharger
{
    private static readonly FieldInfo InventoryField = AccessTools.Field(typeof(BlockEntityECharger), "_inventory");
    private static readonly FieldInfo MeshesField = AccessTools.Field(typeof(BlockEntityECharger), "_meshes");
    public BEMultiCharger() { InventoryField.SetValue(this, new MultiInventory()); }
    public override string InventoryClassName => "epmulticharger";
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Inventory.LateInitialize("epmulticharger-" + Pos, api);
    }
    public int NextUnfinished
    {
        get { for (int i = 0; i < Inventory.Count; i++) if (ChargeRules.NeedsCharge(Inventory[i].Itemstack)) return i; return -1; }
    }

    public bool Interact(IPlayer player, int bay)
    {
        if (bay < 0 || bay >= Inventory.Count) return false;
        var slot = Inventory[bay];
        if (!slot.Empty)
        {
            var stack = slot.TakeOutWhole();
            if (!player.InventoryManager.TryGiveItemstack(stack)) Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
        else
        {
            var source = player.InventoryManager.ActiveHotbarSlot;
            if (!ChargeRules.Accepts(source.Itemstack)) return false;
            if (source.TryPutInto(Api.World, slot, 1) == 0) return false;
            source.MarkDirty();
        }
        slot.MarkDirty();
        MarkDirty(true);
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/player/buildhigh"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        return true;
    }

    public void RefreshMesh(int index)
    {
        if (Api?.Side != EnumAppSide.Client || index < 0 || index >= Inventory.Count) return;
        var meshes = MeshesField.GetValue(this) as MeshData[];
        if (meshes == null || index >= meshes.Length) return;
        var stack = Inventory[index].Itemstack;
        if (stack == null) { meshes[index] = null; return; }
        var mesh = GenMesh(Inventory[index]);
        if (mesh == null) { meshes[index] = null; return; }
        var attrs = stack.Collectible.Attributes;
        float A(string key, float fallback) => attrs?[key].AsFloat(fallback) ?? fallback;
        var pivot = new Vec3f(0.5f, stack.Item != null ? 1f / 30f : 0.5f, 0.5f);
        if (stack.Item != null)
        {
            mesh.Scale(pivot, A("scaleX", 0.5f), A("scaleY", 0.5f), A("scaleZ", 0.5f));
            mesh.Translate(A("translateX", 0), A("translateY", 0.4f), A("translateZ", 0));
            mesh.Rotate(pivot, A("rotateX", 0) * GameMath.DEG2RAD, A("rotateY", 0) * GameMath.DEG2RAD, A("rotateZ", 0) * GameMath.DEG2RAD);
        }
        else mesh.Scale(pivot, 0.3f, 0.3f, 0.3f);
        mesh.Scale(new Vec3f(), 0.45f, 0.45f, 0.45f);
        mesh.Translate(0.025f + (index % 2) * 0.5f, 0.12f, 0.025f + (index / 2) * 0.5f);
        meshes[index] = mesh;
    }

    public override void GetBlockInfo(IPlayer player, StringBuilder text)
    {
        // Keep EP's network status and add per-bay queue information.
        base.GetBlockInfo(player, text);
        int active = NextUnfinished;
        bool powered = GetBehavior<BEBehaviorECharger>()?.PowerSetting > 0 && Block.Variant["state"] != "burned";
        for (int i = 0; i < Inventory.Count; i++)
        {
            var stack = Inventory[i].Itemstack;
            string detail = stack == null ? "Empty" : stack.GetName() + " " +
                Math.Clamp(100.0 * stack.Attributes.GetInt("durability", 0) / Math.Max(1, stack.Collectible.GetMaxDurability(stack)), 0, 100).ToString("0") + "%";
            string status = stack == null ? "" : !ChargeRules.NeedsCharge(stack) ? " [Full]" : i == active ? (powered ? " [Charging]" : " [Waiting for power]") : " [Queued]";
            text.AppendLine($"Bay {i + 1}: {detail}{status}");
        }
    }
}
