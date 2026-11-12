using System;
using System.Linq;
using HarmonyLib;

namespace DoctorClinic
{
    internal sealed class ClinicReturn
    {
        public string PatientId { get; set; }
        public string Name { get; set; }
        public string Portrait { get; set; }
        public string Faction { get; set; }
        public int DoseId { get; set; }
        public bool Arrived { get; set; }
    }

    public sealed partial class ModEntry
    {
        private const string DreamReturnPrefix = "doctor_clinic_dream_return_";

        private static bool IsAnesthesia(Treatment treatment) =>
            treatment == Treatment.DreamDust || treatment == Treatment.Oximor || treatment == Treatment.Nomore;

        private static int AnesthesiaPrice(Treatment treatment) => treatment == Treatment.DreamDust ? 18 : 50;

        private bool RejectInappropriateAnesthesia(ClinicCase visit, Treatment? treatment)
        {
            if (treatment == Treatment.Nomore && visit.HasOximorDependence)
            {
                status = "成瘾患者拒绝奥克莫吸。";
                QueueClinicReply(visit, "我不要奥克莫吸！");
                return true;
            }
            if (treatment != Treatment.Oximor || !visit.HasUntreatedOximorOverdose) return false;
            status = "过量患者拒绝奥克西莫。";
            QueueClinicReply(visit, "别再给我奥克西莫了，我需要纳洛通！");
            return true;
        }

        private void ApplyAnesthesia(ClinicCase visit, GameItem item, Treatment treatment)
        {
            if (treatment == Treatment.Nomore && !HasUsableNomore(item))
            {
                status = "这支奥克莫吸没有有效药液，或纯度不足。";
                ReactToTreatmentMistake(visit, stopped: false);
                return;
            }
            if (!visit.Wounds.Any(wound => !wound.Hidden && wound.IsInjury &&
                (treatment == Treatment.Nomore || !wound.RequiresBloodTest)))
            {
                status = "患者拒绝麻醉。";
                QueueClinicReply(visit, "想都别想！我绝不会用那种东西！");
                return;
            }
            if (visit.AnesthesiaUsed)
            {
                status = "患者说：「刚才已经用过了，不用再打。」";
                return;
            }
            if (treatment != Treatment.Nomore &&
                (visit.Faction == StoreClient.FACTION_SECURITY ||
                    !(visit.AcceptsAnesthesia ??= UnityEngine.Random.Range(0, 2) == 0)))
            {
                status = "患者拒绝麻醉。";
                QueueClinicReply(visit, "想都别想！我绝不会用那种东西！");
                return;
            }
            var quoted = AnesthesiaPrice(treatment);
            var drugName = item.name;
            var doseValue = item.unitValue;
            if (treatment == Treatment.DreamDust && !visit.Practice && !visit.Tutorial && !string.IsNullOrEmpty(visit.Faction))
            {
                if (!ArchiveDreamDose(item, out var doseId))
                {
                    status = "无法保存原版回头客所需的物品记录，未施用。";
                    return;
                }
                visit.DreamDoseId = doseId;
            }
            else ConsumeOne(item);
            RecordMedicationInvestigation(visit, doseValue);

            visit.AnesthesiaUsed = true;
            visit.AnesthesiaOffer = treatment;
            visit.AnesthesiaCost = quoted;
            visit.AnesthesiaWound = Array.FindIndex(visit.Wounds, wound => !wound.Hidden);
            status = $"患者同意。已施用 {drugName}，计费 {quoted}。";
            QueueClinicReply(visit, "。。。好吧，如果这能让我好受点");
        }

        private static void ConsumeOne(GameItem item)
        {
            if (item.unitCount == 1) item.Destroy();
            else item.SetAmount(item.unitCount - 1);
        }

        private static bool ArchiveDreamDose(GameItem item, out int id)
        {
            id = 0;
            var sold = EmporiumEntry.Instance?.soldElement;
            var origin = item.parentInventory;
            if (sold == null || origin == null) return false;
            var count = item.unitCount;
            var split = count > 1;
            var dose = split ? item.GetSplitItem(1) : item;
            if (dose == null)
            {
                if (split) item.SetAmount(count);
                return false;
            }
            if (split)
            {
                dose.state = item.state.Clone();
                dose.modifiedState = item.modifiedState.Clone();
                dose.name = item.name;
            }
            else if (!origin.Expel(item)) return false;

            if (!sold.UncheckedAccept(dose))
            {
                if (split) { item.SetAmount(count); dose.Destroy(); }
                else origin.UncheckedAccept(item);
                return false;
            }
            id = dose.uniqueId;
            return true;
        }

        private void RecordDreamReturn(ClinicCase visit)
        {
            if (visit.DreamDoseId == 0) return;
            if (visit.Dead)
            {
                EmporiumEntry.Instance?.GetItemFromSoldElement(visit.DreamDoseId)?.Destroy();
                CancelDreamReturn(visit.PatientId);
                return;
            }
            if (state.Returns.Any(record => record.PatientId == visit.PatientId)) return;
            var record = new ClinicReturn
            {
                PatientId = visit.PatientId, Name = visit.PatientName, Portrait = visit.Portrait,
                Faction = visit.Faction, DoseId = visit.DreamDoseId
            };
            state.Returns.Add(record);
            QueueDreamReturn(record, store.storeClientManager);
        }

        private void RestoreDreamReturns(StoreClientManager manager)
        {
            foreach (var record in state.Returns.Where(entry => !entry.Arrived))
                QueueDreamReturn(record, manager);
        }

        private void QueueDreamReturn(ClinicReturn record, StoreClientManager manager)
        {
            if (manager == null) return;
            var key = DreamReturnPrefix + record.PatientId;
            if (manager.chemicalFeedbackStack.Any(client => client?.eventSourceId == key) ||
                manager.GetClientStack().Any(client => client?.eventSourceId == key)) return;
            var dose = EmporiumEntry.Instance?.GetItemFromSoldElement(record.DoseId);
            if (dose == null)
            {
                log.Warning("找不到梦尘辅助的存档样品，无法重建原版回头客。");
                return;
            }
            var sold = new SoldItem
            {
                itemId = dose.identifier, itemName = dose.name, itemUniqueId = record.DoseId,
                clientId = PatientPrefix + record.PatientId, clientName = record.Name,
                clientSprite = record.Portrait, factionId = record.Faction, soldPrice = 18
            };
            var before = manager.chemicalFeedbackStack.Count;
            try { ChemicalFeedbackHelper.QueueChemicalReturningClient(sold); }
            catch (Exception error)
            {
                log.Warning($"原版梦尘回头客创建失败，稍后重试：{error.Message}");
                return;
            }
            if (manager.chemicalFeedbackStack.Count <= before) return;
            var client = manager.chemicalFeedbackStack[manager.chemicalFeedbackStack.Count - 1];
            client.eventSourceId = key;
            client.OnArrival += () =>
            {
                if (!IsLoaded || record.Arrived) return;
                record.Arrived = true;
                Save();
            };
            var repliedToRepurchase = false;
            client.onItemBought += (item, _) =>
            {
                if (repliedToRepurchase || item?.identifier != dose.identifier) return;
                repliedToRepurchase = true;
                clinicReplies.Enqueue((client.displayName, "暂时好点了。。。", () => IsAtCounter(client, true)));
            };
            client.OnDismiss += () => EmporiumEntry.Instance?.GetItemFromSoldElement(record.DoseId)?.Destroy();
        }

        private void CancelDreamReturn(string patientId)
        {
            if (string.IsNullOrEmpty(patientId)) return;
            var key = DreamReturnPrefix + patientId;
            var manager = store.storeClientManager;
            manager.chemicalFeedbackStack.RemoveAll(client => client?.eventSourceId == key);
            manager.GetClientStack().RemoveAll(client => client?.eventSourceId == key);
            foreach (var record in state.Returns.Where(entry => entry.PatientId == patientId && !entry.Arrived).ToArray())
            {
                EmporiumEntry.Instance?.GetItemFromSoldElement(record.DoseId)?.Destroy();
                state.Returns.Remove(record);
            }
        }

        internal void OnClinicClientShot(StoreClient client)
        {
            if (!IsLoaded || client == null || client.survivesShot) return;
            var identifier = client.identifier;
            if (identifier == null || !identifier.StartsWith(PatientPrefix, StringComparison.Ordinal)) return;
            var patientId = identifier.Substring(PatientPrefix.Length);
            var visit = state.Current;
            if (visit != null && !visit.Settled && visit.PatientId == patientId)
            {
                visit.Dead = true;
                visit.ExternalDeath = true;
            }
            CancelDreamReturn(patientId);
            Save();
        }
    }

    [HarmonyPatch(typeof(StoreUIManager), "Shoot")]
    internal static class ClinicClientShotPatch
    {
        private static void Prefix(out StoreClient __state)
        {
            var store = PlayerStore.Instance;
            __state = (store?.currentClientInstance ?? store?.lastVisitedClient)?.GetClientBlueprint();
        }

        private static void Postfix(StoreClient __state) => ModEntry.Active?.OnClinicClientShot(__state);
    }
}
