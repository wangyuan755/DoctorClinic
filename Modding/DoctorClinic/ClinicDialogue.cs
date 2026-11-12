using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string TreatmentAccidentReply = "你干什么！疯了吗！";
        private const string IneffectiveTreatmentReply = "这真的有用吗。。。";
        private const string WalkoutReply = "我看你压根不会治病！浪费时间！";
        private const string CommissionDoseReply = "这是治疗的一部分吗。。。好的";
        private readonly Queue<(string Speaker, string Text, Func<bool> IsRelevant)> clinicReplies =
            new Queue<(string, string, Func<bool>)>();

        private void QueueClinicReply(ClinicCase visit, string text, bool afterWalkout = false)
        {
            var patient = boundBuyer;
            var speaker = string.IsNullOrEmpty(visit.PatientName) ? "患者" : visit.PatientName;
            clinicReplies.Enqueue((speaker, text, () =>
                afterWalkout || (state?.Current == visit && (patient == null || IsAtCounter(patient, visit.Settled)))));
        }

        private bool ReactToTreatmentMistake(ClinicCase visit, bool stopped)
        {
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
            if (wound.Kind == ConditionKind.OximorOverdose) return "谢谢，清醒多了。";
            if (wound.Kind == ConditionKind.SentinelCollapse)
                return wound.IsPerfect ? "啊，谢谢...舒服多了，又能再撑一周了..." : "总比没有好...谢了";
            if (wound.IsCare) return "好，就如脱胎换骨一般";
            if (wound.Kind == ConditionKind.OximorDependence)
                return treatment == Treatment.Synaprest ? "感觉重获新生啊" : "暂时好点了。。。";
            if (wound.IsLifeThreatening)
                return wound.IsPerfect ? "救命之恩，万分感激" : "谢谢你救了我";
            if (wound.RequiresBloodTest)
                return wound.IsPerfect ? "感觉好多了" : "谢谢，好受点了";
            return wound.IsPerfect ? "谢谢，好多了" : "不错，好受点了。";
        }

        private void ReplyToCompletedTreatment(ClinicCase visit, Abrasion wound, Treatment treatment)
        {
            if (wound.Kind == ConditionKind.OximorDependence && treatment == Treatment.Synaprest &&
                visit.Faction != StoreClient.FACTION_UPPER)
            {
                QueueClinicReply(visit, "我在做梦吗。。。居然能摆脱这种生活。。。总之，真的十分感谢。");
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
