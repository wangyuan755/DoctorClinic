using static DoctorClinic.ClinicText;
using HarmonyLib;
using static ItemFeatureList.InjectorConditionType;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string AntifungalDosesTag = "DOCTOR_CLINIC_ANTIFUNGAL_DOSES";

        private static long InvestigationDoseValue(GameItem item, Treatment treatment) => treatment switch
        {
            Treatment.Bandage => 0,
            Treatment.TopicalBandage => 0,
            Treatment.HemostaticBandage => 0,
            Treatment.ColdPack => 0,
            Treatment.Splint => 0,
            Treatment.Suture => 0,
            Treatment.Forceps => 0,
            Treatment.Scalpel => 0,
            Treatment.Antifungal => item.unitValue / Abrasion.AntifungalDoses,
            _ => item.unitValue
        };

        private void RecordMedicationInvestigation(ClinicCase visit, long doseValue)
        {
            var security = store.secData;
            if (visit.Practice || visit.Tutorial || security == null || doseValue <= 0) return;
            var points = state.AccumulateMedicationInvestigation(doseValue);
            if (points > 0) AddInvestigationValue(security, SecData.ENDANGERMENT, points);
        }

        private static GameItem CreateAntifungalBottle()
        {
            var item = MedicalPack.CreateSupply(Antifungal, T("clinic_sporeclear_tablets"), Abrasion.AntifungalPrice, "antifungal", 2, 2);
            item.shortDescription = T("clinic_five_doses_per_bottle");
            UpdateAntifungalDisplay(item);
            return item;
        }

        private static int AntifungalDosesLeft(GameItem item)
        {
            var doses = item.GetTagReadonly(AntifungalDosesTag)?.GetInt() ?? 0;
            return doses > 0 ? doses : Abrasion.AntifungalDoses;
        }

        private static void UpdateAntifungalDisplay(GameItem item)
        {
            item.customText = $"{AntifungalDosesLeft(item)}/{Abrasion.AntifungalDoses}";
        }

        private static void UseAntifungalDose(GameItem item)
        {
            var remaining = AntifungalDosesLeft(item) - 1;
            if (remaining <= 0)
            {
                ConsumeOne(item);
                if (item.unitCount <= 0 || item.parentInventory == null) return;
                remaining = Abrasion.AntifungalDoses;
            }
            item.ModifyTag(AntifungalDosesTag, tag => tag.SetInt(remaining));
            UpdateAntifungalDisplay(item);
            item.Validate();
        }

        private static (int Price, string ReducedQuality) ImmunivaxQuality(GameItem item)
        {
            return GetImmunivaxCondition(item) switch
            {
                Genuine => (160, null),
                Expired => (80, T("clinic_expired")),
                Counterfeit => (40, T("clinic_counterfeit")),
                _ => (0, null)
            };
        }

        internal static ItemFeatureList.InjectorConditionType GetImmunivaxCondition(GameItem item)
        {
            if (item == null || !item.IsTag("INS_INJECTOR_TAG")) return Unusable;
            var indicator = item.GetTagReadonly("INJECTOR_INDICATOR")?.GetInt();
            if (indicator != 0 && indicator != 1) return Unusable;
            if (item.IsTag("ISSUE_BODY") || item.IsTag("ISSUE_COLOR") || item.IsTag("ISSUE_LABEL") ||
                item.IsTag("ISSUE_SERIAL"))
                return Counterfeit;
            return indicator == 1 || item.IsTag("ISSUE_INDICATOR") ? Expired : Genuine;
        }

        private static bool HasUsableNomore(GameItem item)
        {
            // Use the actual contents. The native grade's stored purity can lag behind edits to a mixture.
            if (!ChemicalProductHelper.HasChemicalPurity(item)) return false;
            var volume = ChemicalHelper.GetTotalVolume(item);
            var active = item.GetTagReadonly(Liquid.GetCurrentPartFromId("nomore"))?.GetInt() ?? 0;
            // 45% is the native boundary between junk grade and cut product.
            return volume > 0 && active > 0 && active * 100L >= volume * 45L;
        }
    }

    [HarmonyPatch(typeof(ConsumableHelper), nameof(ConsumableHelper.InitImmunivax))]
    internal static class ClinicImmunivaxSelfUsePatch
    {
        private static void Postfix(GameItem __0)
        {
            var activate = __0.onActivateSlotItemFunc;
            if (activate == null) return;
            __0.onActivateSlotItemFunc = (item, inventory, slot) =>
            {
                const string featureId = "CATEGORY_GENUINE_INJECTOR";
                var feature = item.FindItemFeatureByID(featureId);
                var condition = ModEntry.GetImmunivaxCondition(item);
                if (condition == Unusable || feature?.realCondition?.identifier == "unusableInjector")
                {
                    StoreUIManager.Instance?.Notify(T("clinic_this_immunivax_injector_is_empty_or_unusable"), "default");
                    return;
                }

                // The native self-use handler reads this feature and destroys the item even if it is missing.
                if (feature == null)
                    item.AddItemFeature(ItemFeatureList.InjectorFeature(Genuine, condition));
                else if (feature.realCondition == null)
                {
                    var replacement = ItemFeatureList.InjectorFeature(Genuine, condition);
                    feature.SetConditions(replacement.fakeCondition, replacement.realCondition);
                }

                activate(item, inventory, slot);
            };
        }
    }
}
