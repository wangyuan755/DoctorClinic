using System;
using System.Linq;

namespace DoctorClinic
{
    internal enum CommissionSource { Revolution, BlackMarket, Security }
    internal enum CommissionStatus { Offered, Active, Declined, Completed, Expired }

    internal sealed class ClinicCommission
    {
        public string CaseId { get; set; }
        public string PatientName { get; set; }
        public int Day { get; set; }
        public CommissionSource Source { get; set; }
        public bool Assassination { get; set; }
        public bool Delivered { get; set; }
        public int ReagentId { get; set; }
        public int NoteId { get; set; }
        public CommissionStatus Status { get; set; }
        public int Payment => Assassination ? 500 : 100;

        public static CommissionSource? RollSource(int roll, int revolution, int blackMarket, bool whiteGlove)
        {
            var revolutionChance = revolution >= 40 ? 20 : 10;
            var blackMarketChance = blackMarket >= 40 ? 20 : 10;
            if (roll < revolutionChance) return CommissionSource.Revolution;
            if (roll < revolutionChance + blackMarketChance) return CommissionSource.BlackMarket;
            return whiteGlove && roll < revolutionChance + blackMarketChance + 10
                ? CommissionSource.Security : (CommissionSource?)null;
        }
    }

    internal sealed class ClinicPoisoning
    {
        public string CaseId { get; set; }
        public string PatientId { get; set; }
        public int DueDay { get; set; }
        public bool CommissionEligible { get; set; }
        public bool Resolved { get; set; }
    }

    public sealed partial class ModEntry
    {
        private const string CommissionNote = "doctor_clinic_commission_note";
        private const string NativeSlip = "slip";
        private const string RefusalLetter = "听好了，今天会有人来找你治疗。这个人不老实，受到了我们的一些“小小”的惩罚，所以我需要你暂时放下救人的念头。别担心，如果你不管他，我们会好好补偿你的~";
        private const string AssassinationLetter = "听好了，今天会有个老熟人来找你治疗。我们随信附带了一份小礼物，希望你能够转交到他手上，最好在他不知情的情况下。在我们确认他收到礼物后，我们会好好答谢你的~";
        private bool deliveringCommissionItems;
        private const string Reagent = "doctor_clinic_dusk_reagent";
        private int clinicOpenedDay = -1;

        private ClinicCommission CommissionFor(string caseId) =>
            state.Commissions.FirstOrDefault(entry => entry.CaseId == caseId);

        private void RollCommission(ClinicCase visit, System.Random random)
        {
            if (!state.Poisoner || visit.Faction != StoreClient.FACTION_UPPER) return;
            var source = ClinicCommission.RollSource(random.Next(100), StoreReputation.GetRevReputation(),
                StoreReputation.GetBMReputation(), state.WhiteGlove);
            if (!source.HasValue) return;
            state.Commissions.Add(new ClinicCommission
            {
                CaseId = visit.Id, PatientName = visit.PatientName, Day = Today, Source = source.Value,
                Assassination = random.Next(100) < (state.WhiteGlove ? 70 : 30)
            });
        }

        private void DeliverCommissions()
        {
            clinicOpenedDay = Today;
            DeliverCommissionItems();
        }

        private void StopCommissionDelivery() => clinicOpenedDay = -1;

        internal void DeliverCommissionItems()
        {
            if (deliveringCommissionItems || !IsLoaded || clinicOpenedDay != Today ||
                state.License != LicenseStatus.Active || state.Paused ||
                !ClinicReputation.CanSeeUpper(state.ReputationHalfPoints)) return;

            deliveringCommissionItems = true;
            try
            {
                var pending = state.Commissions.Where(entry => entry.Day == Today &&
                    (entry.Status == CommissionStatus.Offered || entry.Status == CommissionStatus.Active ||
                    entry.Status == CommissionStatus.Declined)).ToArray();
                foreach (var entry in pending)
                {
                    if (entry.NoteId == 0)
                    {
                        var note = PreBuiltItemHelper.CreatePaperWithText(CommissionLetter(entry), "");
                        if (note != null) note.name = CommissionTitle(entry);
                        entry.NoteId = PlaceCommissionItem(note, NativeSlip);
                        if (entry.NoteId == 0) continue;
                        entry.Delivered = true;
                        entry.Status = CommissionStatus.Active;
                        Save();
                    }
                    if (entry.Assassination && entry.ReagentId == 0)
                    {
                        entry.ReagentId = PlaceCommissionItem(DirectoryMaster.Item(Reagent, true), Reagent);
                        if (entry.ReagentId != 0) Save();
                    }
                }
            }
            finally
            {
                deliveringCommissionItems = false;
            }
        }

        private static int PlaceCommissionItem(GameItem item, string id)
        {
            if (item == null) return 0;
            var shop = EmporiumEntry.Instance;
            if (shop == null || item.uniqueId == 0 || item.identifier != id)
            {
                item.Destroy();
                return 0;
            }

            var counter = shop.frontInvinvElement;
            var slot = counter?.TryFindOneValidInventorySlot_TopMiddle(item, keepOrientation: true);
            if (slot != null && slot.targetItem == null && slot.inventory == counter)
                slot.TryAcceptOnce(1);
            if (item.parentInventory == null) ModHelper.MoveItemToInventory(item);
            if (item.parentInventory == null) ModHelper.MoveItemToBackCounter(item);
            if (item.parentInventory != null) return item.uniqueId;
            item.Destroy();
            return 0;
        }

        private static GameItem CreateLegacyCommissionNote()
        {
            var item = DirectoryMaster.Create<GameItem>(NativeSlip);
            item.identifier = CommissionNote;
            return item;
        }

        private static string CommissionTitle(ClinicCommission entry) =>
            $"{(entry.Assassination ? "暗杀" : "拒诊")}委托 · {entry.PatientName}";

        private static string CommissionLetter(ClinicCommission entry) =>
            entry.Assassination ? AssassinationLetter : RefusalLetter;

        private void RefreshSavedCommissionNote(GameItem item)
        {
            item.useSpriteFromMod = false;
            item.SetSprite("Items/items_misc", NativeSlip);
            var entry = state.Commissions.FirstOrDefault(candidate => candidate.NoteId == item.uniqueId);
            if (entry != null)
            {
                item.name = CommissionTitle(entry);
                item.ModifyTag("SLIP_TITLE", tag => tag.SetString(""));
                item.ModifyTag("SLIP_BODY", tag => tag.SetString(CommissionLetter(entry)));
            }
            MedicalPack.RefreshSavedItem(item, null, 1, 1, "DOCUMENT");
        }

        private bool CanRefuseByCommission(ClinicCase visit, ClinicCommission entry) =>
            visit != null && IsCurrent(visit) && !visit.Dead && visit.Arrived && !visit.TreatmentPerformed &&
            !visit.AnesthesiaUsed && state.License == LicenseStatus.Active && entry != null && !entry.Assassination &&
            entry.CaseId == visit.Id && entry.Status == CommissionStatus.Active && boundBuyer != null &&
            store.currentClientInstance?.GetClientBlueprint() == boundBuyer;

        private void CompleteCommission(ClinicCommission entry)
        {
            if (entry?.Status != CommissionStatus.Active) return;
            entry.Status = CommissionStatus.Completed;
            RetireReagent(entry);
            store.PayMoney(-entry.Payment, true);
            var gain = entry.Assassination ? 10 : 5;
            switch (entry.Source)
            {
                case CommissionSource.Revolution: StoreReputation.ModRevReputation(gain); break;
                case CommissionSource.BlackMarket: StoreReputation.ModBMReputation(gain); break;
                case CommissionSource.Security: StoreReputation.ModSecReputation(gain); break;
            }
        }

        private bool HasActiveAssassination(ClinicCase visit) =>
            CommissionFor(visit.Id) is ClinicCommission entry && entry.Assassination &&
            entry.Status == CommissionStatus.Active;

        private void ApplyClinicPoison(ClinicCase visit, GameItem item, Treatment treatment)
        {
            var eligible = HasActiveAssassination(visit);
            var previous = state.Poisonings.FirstOrDefault(entry => entry.CaseId == visit.Id && !entry.Resolved);
            if (!CanUse(item))
                status = "请使用仍在玩家库存内的物品。";
            else if (difficulty == Difficulty.简单 && !eligible)
                status = "当前患者没有有效的暗杀委托。";
            else if (previous != null && treatment == Treatment.DuskReagent && difficulty == Difficulty.简单)
                status = "该患者已有迟暮试剂效果。";
            else
            {
                if (!state.WhiteGlove) RecordMedicationInvestigation(visit, item.unitValue);
                ConsumeOne(item);
                visit.TreatmentPerformed = true;
                if (eligible && (previous == null || treatment == Treatment.Zyanide))
                    QueueClinicReply(visit, CommissionDoseReply);
                if (treatment == Treatment.Zyanide)
                {
                    visit.PoisonedDeath = true;
                    visit.Dead = true;
                    Finish(visit, true, showReceipt: SettlementOpen);
                    return;
                }
                if (previous == null)
                    state.Poisonings.Add(new ClinicPoisoning
                    {
                        CaseId = visit.Id, PatientId = visit.PatientId, DueDay = Today + 3,
                        CommissionEligible = eligible
                    });
                status = "已施用迟暮试剂。";
                Save();
            }
            RefreshVisitPanels(visit);
        }

        private void ResolveVisitCommission(ClinicCase visit)
        {
            var delayed = state.Poisonings.FirstOrDefault(entry => entry.CaseId == visit.Id && !entry.Resolved);
            var commission = CommissionFor(visit.Id);
            if (visit.Dead)
            {
                if (delayed != null) delayed.Resolved = true;
                if (!visit.ExternalDeath && HasActiveAssassination(visit)) CompleteCommission(commission);
            }
            if (commission != null && (commission.Status == CommissionStatus.Active ||
                commission.Status == CommissionStatus.Offered) &&
                (visit.Dead || delayed?.CommissionEligible != true))
                ExpireCommission(commission);
        }

        private void ExpireCommission(ClinicCommission entry)
        {
            entry.Status = CommissionStatus.Expired;
            RetireReagent(entry);
        }

        private void RetireReagent(ClinicCommission entry)
        {
            if (entry.ReagentId == 0) return;
            var item = store.FindAllItem(true).FirstOrDefault(candidate =>
                candidate?.identifier == Reagent && candidate.uniqueId == entry.ReagentId && IsRemovable(candidate));
            if (item != null) ConsumeOne(item);
        }

        private void ResolvePoisonings()
        {
            if (!IsLoaded) return;
            var due = state.Poisonings.Where(entry => !entry.Resolved && entry.DueDay <= Today)
                .OrderBy(entry => entry.DueDay).ToArray();
            foreach (var entry in due)
            {
                entry.Resolved = true;
                CancelDreamReturn(entry.PatientId);
                var commission = CommissionFor(entry.CaseId);
                var eligible = entry.CommissionEligible && commission?.Status == CommissionStatus.Active;
                var penalty = ClinicPenalty.Death(true, true, eligible, 0, state.WhiteGlove, TakeLegalAid());
                var incident = new ClinicIncident
                {
                    CaseId = entry.CaseId, Day = Today, LicenseTerm = state.LicenseTerm, Fatal = true,
                    DoctorLoss = penalty.DoctorLoss, SecurityLoss = penalty.SecurityLoss,
                    UpperLoss = penalty.UpperLoss,
                    Investigation = penalty.Investigation
                };
                state.ReputationHalfPoints = Math.Max(-200, state.ReputationHalfPoints - penalty.DoctorLoss * 2);
                CommitIncident(incident);
                if (eligible) CompleteCommission(commission);
                if (state.Current?.Id == entry.CaseId)
                {
                    state.Current.Dead = true;
                    if (IsCurrent(state.Current)) Finish(state.Current, true, showReceipt: SettlementOpen);
                }
                Save();
            }
            if (due.Length > 0) QueueTribunalIfNeeded();
        }

        private bool TakeLegalAid()
        {
            if (!state.LegalAid) return false;
            state.LegalAid = false;
            return true;
        }
    }
}
