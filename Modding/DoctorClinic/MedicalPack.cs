using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace DoctorClinic
{
    internal static class MedicalPack
    {
        public const string Id = "common_medical";
        internal const string Splint = "doctor_clinic_splint";
        internal const string Suture = "doctor_clinic_suture_kit";
        public const string ColdPack = "doctor_clinic_cold_pack";
        private const int SplintPrice = 20;
        private const int SuturePrice = 25;
        private const int ColdPackPrice = 15;
        private static readonly (string Id, int Price)[] Contents =
        {
            ("bandage_item", 8), ("bandage_item", 8), ("rubbing_alcohol", 35),
            (Splint, SplintPrice), (Suture, SuturePrice), (ColdPack, ColdPackPrice)
        };

        public static int Value { get; } = Contents.Sum(part => part.Price);

        public static void Register(ModItemDirectory directory)
        {
            directory.Add(Splint, () => CreateSupply(Splint, "夹板", SplintPrice, "splint", 1, 2));
            directory.Add(Suture, () => CreateSupply(Suture, "缝合包", SuturePrice, "suture_kit", 2, 2));
            directory.Add(ColdPack, () => CreateSupply(ColdPack, "冷敷包", ColdPackPrice, "cold_pack", 2, 2));
        }

        internal static GameItem CreateSupply(string id, string name, int price, string sprite = null,
            int width = 1, int height = 1, string category = "MEDICAL")
        {
            var item = new GameItemElement(id, "Items/items_material", "med1", null);
            if (sprite != null) SetArt(item, sprite);
            item.SetShape(Rectangle(width, height));
            item.name = name;
            item.SetValue(price);
            item.SetGameItemType(category);
            return item;
        }

        private static GridShape Rectangle(int width, int height) =>
            new GridShapeBuilder(width, height).SetDataFill(1).Build();

        private static void SetArt(GameItem item, string sprite)
        {
            item.SetSprite(ModEntry.ModId, sprite);
            item.useSpriteFromMod = true;
        }

        internal static void RefreshSavedItem(GameItem item, string sprite, int width, int height, string category = null)
        {
            if (category == "DOCUMENT") item.RemoveGameItemType("MEDICAL");
            if (category != null) item.SetGameItemType(category);
            if (sprite != null) SetArt(item, sprite);
            var old = item.shape;
            var placement = item.modifiedShape;
            var baseShape = Rectangle(width, height);
            var inventory = item.parentInventory;
            if (inventory == null || placement == null) return;
            var alreadyRectangular = old != null && IsRectangle(old, width, height);
            var candidate = new GridShapeBuilder(baseShape).SetTransform(placement.minX, placement.minY,
                placement.flipped, alreadyRectangular ? placement.orientation : 0).Build();
            if (inventory.CheckInventorySlotStillValid(item, candidate, item.modifiedState))
            {
                item.shape = baseShape;
                item.modifiedShape = candidate;
                item.ValidateShapeState();
            }
            else if (old != null && !alreadyRectangular && inventory.Expel(item))
            {
                item.shape = baseShape;
                item.modifiedShape = baseShape.Clone();
                var openSlot = inventory.TryFindOneValidInventorySlot(item);
                if (openSlot == null || openSlot.targetItem != null || openSlot.inventory != inventory ||
                    openSlot.AcceptUnchecked() != item.unitCount)
                {
                    if (item.parentInventory == inventory) inventory.Expel(item);
                    item.shape = old;
                    item.modifiedShape = placement;
                    inventory.UncheckedAccept(item);
                }
            }
            item.Validate();
        }

        private static bool IsRectangle(GridShape shape, int width, int height)
        {
            if (shape.width != width || shape.height != height) return false;
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    if (shape.GetLocal(x, y) == 0) return false;
            return true;
        }

        internal static void RefreshSavedSupplies(GameItem item)
        {
            switch (item.identifier)
            {
                case Splint: RefreshSavedItem(item, "splint", 1, 2); break;
                case Suture: RefreshSavedItem(item, "suture_kit", 2, 2); break;
                case ColdPack: RefreshSavedItem(item, "cold_pack", 2, 2); break;
            }
        }

        public static void Activate(GameItem pack, GameInventory inventory, SlotMarker slot)
        {
            var failure = Unpack(pack, inventory);
            OverlayHandler.current?.UpdateInventoryStateUI();
            if (failure != null)
                GenericConfirmationUIManager.Instance?.OpenPanelSingleButton(failure, "确定", () => { });
        }

        private static string Unpack(GameItem pack, GameInventory destination)
        {
            if (!CanUnpack(pack) || pack.parentInventory != destination)
                return "医疗包已不在可取用库存中。";
            return Deliver(destination, Contents.Select(part => part.Id), () =>
            {
                if (!CanUnpack(pack) || pack.parentInventory != destination) return false;
                if (pack.unitCount == 1) pack.Destroy();
                else pack.SetAmount(pack.unitCount - 1);
                return true;
            });
        }

        internal static string Deliver(GameInventory destination, IEnumerable<string> ids, Func<bool> commit,
            Action<GameItem> prepare = null)
        {
            if (destination == null || !GeneralHelper.MayPlayerInsertInto(destination))
                return "目标库存暂时无法接收物品。";

            var created = new List<GameItem>();
            var stackCounts = new Dictionary<GameItem, int>();
            var committed = false;
            try
            {
                foreach (var id in ids)
                {
                    var item = DirectoryMaster.Item(id);
                    if (item == null) return "无法创建全部物品，已取消。";
                    created.Add(item);
                    // DirectoryMaster returns an unregistered placeholder when a factory is missing.
                    if (item.uniqueId == 0 || item.identifier != id)
                        return "无法创建全部物品，已取消。";
                    item.SetAmount(1);
                    prepare?.Invoke(item);
                }

                foreach (var item in created)
                {
                    var slot = destination.TryFindOneValidInventorySlot(item);
                    if (slot == null || slot.numTransfer < 1 || slot.inventory != destination)
                        return "目标库存放不下全部物品，已取消。";

                    var stack = slot.targetItem;
                    var previousCount = stack?.unitCount ?? 0;
                    if (stack != null && !stackCounts.ContainsKey(stack))
                        stackCounts.Add(stack, previousCount);
                    if (slot.TryAcceptOnce(1) != 1)
                        return "库存拒绝接收物品，已取消。";

                    var received = stack ?? item;
                    if (received.parentInventory != destination || !destination.childItems.Contains(received) ||
                        received.unitCount != previousCount + 1)
                        return "无法确认物品已入库，已取消。";
                }

                foreach (var item in created)
                    if (item.parentInventory == null) item.Destroy();

                committed = commit();
                return committed ? null : "操作条件已改变，已取消。";
            }
            finally
            {
                if (!committed)
                {
                    foreach (var stack in stackCounts)
                        stack.Key.SetAmount(stack.Value);
                    foreach (var item in created)
                        item.Destroy();
                }
            }
        }

        public static bool CanUnpack(GameItem pack) => pack?.identifier == Id &&
            pack.parentInventory != null && pack.unitCount > 0 && pack.MayRemove() &&
            pack.MaxNumRemove() > 0 && GeneralHelper.IsItemEffectivelyOwned(pack);

        internal static void AddUnpackHint(GameItem pack)
        {
            const string hint = "双击以拆开";
            var description = pack.shortDescription ?? "";
            if (description.Contains(hint)) return;
            pack.shortDescription = description.Length == 0 ? hint : description + "\n" + hint;
        }
    }

    [HarmonyPatch(typeof(MaterialDirectory), "CommonMedicalSupplies")]
    internal static class MedicalPackActivationPatch
    {
        private static void Postfix(GameItem __result)
        {
            MedicalPack.AddUnpackHint(__result);
            __result.mayActivateSlotItemFunc += (item, inventory, slot) => MedicalPack.CanUnpack(item);
            __result.canActivateSlotItemFunc += (item, inventory, slot) => MedicalPack.CanUnpack(item);
            __result.onActivateSlotItemFunc += MedicalPack.Activate;
        }
    }

    [HarmonyPatch(typeof(GameItem), "get_unitValue")]
    internal static class MedicalPackValuePatch
    {
        private static void Postfix(GameItem __instance, ref long __result)
        {
            if (__instance.identifier == MedicalPack.Id) __result = MedicalPack.Value;
        }
    }
}
