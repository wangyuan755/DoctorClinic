using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DoctorClinic
{
    [HarmonyPatch(typeof(StoreReputationPerk), nameof(StoreReputationPerk.GetLocalizedDescription))]
    internal static class ClinicCommissionPerkDescriptionPatch
    {
        private static void Postfix(StoreReputationPerk __instance, ref string __result)
        {
            if (ModEntry.Active == null) return;
            if (__instance.perkId == StoreReputationPerk.REV_QUARTERMASTER ||
                __instance.perkId == StoreReputationPerk.BM_RELIABLE)
                __result += "\n上层患者的本派系诊疗委托概率提高 10 个百分点。";
        }
    }

    [HarmonyPatch]
    internal static class DoctorReputationPerkTextPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StoreReputationPerk), nameof(StoreReputationPerk.GetLocalizedPerkName));
            yield return AccessTools.Method(typeof(StoreReputationPerk), nameof(StoreReputationPerk.GetLocalizedDescription));
            yield return AccessTools.Method(typeof(StoreReputationPerk), nameof(StoreReputationPerk.GetLocalizedFlavorText));
        }

        private static bool Prefix(StoreReputationPerk __instance, MethodBase __originalMethod, ref string __result)
        {
            if (!(__instance is DoctorReputationPerk perk)) return true;
            __result = __originalMethod.Name switch
            {
                nameof(StoreReputationPerk.GetLocalizedPerkName) => perk.perkThreshold == ClinicReputation.SundaySupply
                    ? "周日补货" : ClinicReputation.Title(perk.perkThreshold * 2),
                nameof(StoreReputationPerk.GetLocalizedDescription) => perk.Effect,
                _ => perk.Flavor
            };
            return false;
        }
    }

    internal sealed class DoctorReputationPerk : StoreReputationPerk
    {
        internal string Effect { get; }
        internal string Flavor { get; }

        internal DoctorReputationPerk(int threshold, string effect, string flavor)
        {
            perkId = $"doctor_clinic_reputation_{threshold}";
            perkThreshold = threshold;
            perkType = threshold < 0 ? -1 : 1;
            Effect = effect;
            Flavor = flavor;
        }
    }

    public sealed partial class ModEntry
    {
        private ReputationElement doctorReputation;
        private readonly List<DoctorReputationPerk> doctorReputationPerks = new List<DoctorReputationPerk>();

        internal void AppendDoctorReputation(ReputationUIManager manager)
        {
            RemoveDoctorReputation();
            if (!IsLoaded || manager.layout == null || manager.reputationPrefab == null) return;

            var column = UnityEngine.Object.Instantiate(manager.reputationPrefab, manager.layout.transform);
            column.name = "DoctorClinicReputation";
            column.transform.SetAsLastSibling();
            doctorReputation = column.GetComponent<ReputationElement>();
            if (doctorReputation == null)
            {
                UnityEngine.Object.Destroy(column);
                return;
            }
            if (manager.reputationPerkPrefab != null)
            {
                AddDoctorReputationPerk(manager, -100, "停止生成新患者并触发「公民审判」结局。", "再也没有人来求诊了。");
                AddDoctorReputationPerk(manager, -60, "医生声望称号变为「恶名在外」，上层患者仍不会预约。", "街坊已经听说了这家诊所的事。");
                AddDoctorReputationPerk(manager, -20, "暂停生成新的上层病例和高端护理需求。", "上层的病人另找医生去了。");
                AddDoctorReputationPerk(manager, ClinicReputation.Established, "每天安排 1 位普通患者，有 50% 概率再安排 1 位。协会药商提供 5% 折扣。", "我听邻居提起过你。");
                AddDoctorReputationPerk(manager, ClinicReputation.SundaySupply, "协会药商每周日额外来访，提供常规药品和医疗用品。", "周日我也能来送一批。");
                AddDoctorReputationPerk(manager, ClinicReputation.Trusted, "每天安排 2 位普通患者。协会药商提供 10% 折扣。", "街坊生病时都愿意来找你。");
                AddDoctorReputationPerk(manager, ClinicReputation.Renowned, "每天安排 2 位普通患者，有 50% 概率再安排 1 位。协会药商提供 15% 折扣。", "连别的街区也有人专程来求诊。");
            }
            RefreshDoctorReputation();
            if (manager.layout.transform is RectTransform layout)
                LayoutRebuilder.MarkLayoutForRebuild(layout);
        }

        private void AddDoctorReputationPerk(ReputationUIManager manager, int threshold, string effect, string flavor)
        {
            var perk = new DoctorReputationPerk(threshold, effect, flavor);
            var marker = UnityEngine.Object.Instantiate(manager.reputationPerkPrefab, doctorReputation.transform)
                .GetComponent<PerkElement>();
            marker.Init(perk);
            marker.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(-19f, doctorReputation.GetBarHeight(threshold));
            doctorReputationPerks.Add(perk);
        }

        private void RefreshDoctorReputation()
        {
            if (doctorReputation == null || !IsLoaded) return;
            var points = state.ReputationHalfPoints;
            doctorReputation.reputationTitle.text = "医生";
            doctorReputation.reputationStatus.text = state.License == LicenseStatus.None
                ? "未入会" : ClinicReputation.Title(points);
            doctorReputation.reputationNumber.text = (points / 2m).ToString("+0.0;-0.0;0.0");
            // Native bar coordinates use whole points. Do not show a half point as reaching the next marker.
            doctorReputation.UpdateBar(points / 2);
            foreach (var perk in doctorReputationPerks)
                perk.isUnlocked = state.License != LicenseStatus.None && (perk.perkThreshold < 0
                    ? points <= perk.perkThreshold * 2
                    : points >= perk.perkThreshold * 2);
        }

        private void RemoveDoctorReputation()
        {
            doctorReputationPerks.Clear();
            if (doctorReputation == null) return;
            doctorReputation.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(doctorReputation.gameObject);
            doctorReputation = null;
        }
    }

    [HarmonyPatch(typeof(ReputationUIManager), nameof(ReputationUIManager.OpenUI))]
    internal static class DoctorReputationPatch
    {
        private static void Postfix(ReputationUIManager __instance) => ModEntry.Active?.AppendDoctorReputation(__instance);
    }
}
