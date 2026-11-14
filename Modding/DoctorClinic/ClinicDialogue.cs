using static DoctorClinic.ClinicText;
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private static string TreatmentAccidentReply => T("clinic_what_are_you_doing_are_you_out");
        private static string IneffectiveTreatmentReply => T("clinic_is_this_actually_helping");
        private static string WalkoutReply => T("clinic_you_don_t_know_the_first_thing");
        private static string CommissionDoseReply => T("clinic_is_that_part_of_the_treatment_all");
        private readonly Queue<(string Speaker, string Text, Func<bool> IsRelevant)> clinicReplies =
            new Queue<(string, string, Func<bool>)>();

        private void QueueClinicReply(ClinicCase visit, string text, bool afterWalkout = false)
        {
            var patient = boundBuyer;
            var speaker = string.IsNullOrEmpty(visit.PatientName) ? T("clinic_patient") : visit.PatientName;
            clinicReplies.Enqueue((speaker, text, () =>
                afterWalkout || (state?.Current == visit && (patient == null || IsAtCounter(patient, visit.Settled)))));
        }

        private bool ReactToTreatmentMistake(ClinicCase visit, bool stopped, string itemName = null)
        {
            // The outcome line is shown on every difficulty; only the patience/reputation
            // consequences are gated to 困难.
            if (!string.IsNullOrEmpty(itemName))
                status = stopped
                    ? T("clinic_treatment_mistake", itemName)
                    : T("clinic_used_one_unit_of_supplies_with_no", itemName);
            if (difficulty != Difficulty.困难) return false;
            visit.LosePatience(stopped);
            QueueClinicReply(visit, stopped ? TreatmentAccidentReply : IneffectiveTreatmentReply, visit.PatientWalkedOut);
            if (!visit.PatientWalkedOut)
            {
                Save();
                return false;
            }
            QueueClinicReply(visit, WalkoutReply, afterWalkout: true);
            Finish(visit, true, showReceipt: SettlementOpen);
            return true;
        }

        private bool IsAtCounter(StoreClient patient, bool allowAfterDismissal)
        {
            var current = store?.currentClientInstance?.GetClientBlueprint();
            return current == patient || (allowAfterDismissal && current == null &&
                store?.lastVisitedClient?.GetClientBlueprint() == patient);
        }

        internal void PlayNextClinicReply(DialogUIManager dialog)
        {
            if (clinicReplies.Count == 0 || dialog.GetCurrentDialog() != null) return;
            while (clinicReplies.Count > 0)
            {
                var reply = clinicReplies.Dequeue();
                if (!reply.IsRelevant()) continue;
                dialog.StartDialogue(new Dialogue().SetText(reply.Speaker, reply.Text));
                return;
            }
        }

        private static string TreatmentResultReply(Abrasion wound, Treatment treatment)
        {
            if (wound.Kind == ConditionKind.OximorOverdose) return T("clinic_thanks_my_head_s_much_clearer_now");
            if (wound.Kind == ConditionKind.SentinelCollapse)
                return wound.IsPerfect ? T("clinic_ah_thank_you_i_feel_better_already") : T("clinic_better_than_nothing_thanks");
            if (wound.IsCare) return T("clinic_wonderful_i_feel_like_a_new_person");
            if (wound.Kind == ConditionKind.OximorDependence)
                return treatment == Treatment.Synaprest ? T("clinic_i_feel_like_i_ve_got_my") : T("clinic_i_feel_a_little_better_for_now");
            if (wound.IsLifeThreatening)
                return wound.IsPerfect ? T("clinic_you_saved_my_life_i_can_t") : T("clinic_thank_you_for_saving_me");
            if (wound.RequiresBloodTest)
                return wound.IsPerfect ? T("clinic_i_feel_much_better") : T("clinic_thanks_that_helped");
            return wound.IsPerfect ? T("clinic_thank_you_much_better") : T("clinic_not_bad_that_feels_better");
        }

        private void ReplyToCompletedTreatment(ClinicCase visit, Abrasion wound, Treatment treatment)
        {
            if (wound.Kind == ConditionKind.OximorDependence && treatment == Treatment.Synaprest &&
                visit.Faction != StoreClient.FACTION_UPPER)
            {
                QueueClinicReply(visit, T("clinic_am_i_dreaming_i_really_get_to"));
                if (!visit.Practice && !visit.Tutorial && !string.IsNullOrEmpty(visit.Faction))
                    StoreReputation.ModReputationRaw(visit.Faction, 5, true);
                return;
            }
            QueueClinicReply(visit, TreatmentResultReply(wound, treatment));
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), "Update")]
    internal static class ClinicDialoguePatch
    {
        private static void Postfix(DialogUIManager __instance) => ModEntry.Active?.PlayNextClinicReply(__instance);
    }
}
