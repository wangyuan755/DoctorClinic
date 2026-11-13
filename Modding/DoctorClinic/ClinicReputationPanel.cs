using static DoctorClinic.ClinicText;
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
                __result += T("clinic_faction_contract_chance");
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
                    ? T("clinic_sunday_restock") : ClinicReputation.Title(perk.perkThreshold * 2),
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
                AddDoctorReputationPerk(manager, -100, T("clinic_stops_new_appointments_and_triggers_the_citizen"), T("clinic_no_one_comes_here_for_treatment_anymore"));
                AddDoctorReputationPerk(manager, -60, T("clinic_your_reputation_title_becomes_notorious_upper_levels"), T("clinic_everyone_in_the_neighborhood_has_heard_about"));
                AddDoctorReputationPerk(manager, -20, T("clinic_stops_new_upper_levels_cases_and_requests"), T("clinic_upper_levels_patients_have_found_another_doctor"));
                AddDoctorReputationPerk(manager, ClinicReputation.Established, T("clinic_schedules_one_regular_patient_per_day_with"), T("clinic_my_neighbor_told_me_about_you"));
                AddDoctorReputationPerk(manager, ClinicReputation.SundaySupply, T("clinic_the_association_supplier_also_visits_on_sundays"), T("clinic_i_can_bring_you_another_batch_on"));
                AddDoctorReputationPerk(manager, ClinicReputation.Trusted, T("clinic_schedules_two_regular_patients_per_day_association"), T("clinic_folks_around_here_know_where_to_go"));
                AddDoctorReputationPerk(manager, ClinicReputation.Renowned, T("clinic_schedules_two_regular_patients_per_day_with"), T("clinic_people_come_from_other_neighborhoods_just_to"));
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
            doctorReputation.reputationTitle.text = T("clinic_doctor");
            doctorReputation.reputationStatus.text = state.License == LicenseStatus.None
                ? T("clinic_not_a_member") : ClinicReputation.Title(points);
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
