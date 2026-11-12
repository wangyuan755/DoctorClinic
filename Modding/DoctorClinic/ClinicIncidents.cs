using System;
using System.Linq;

namespace DoctorClinic
{
    internal sealed class ClinicIncident
    {
        public string CaseId { get; set; }
        public int Day { get; set; }
        public int LicenseTerm { get; set; }
        public bool Fatal { get; set; }
        public int DoctorLoss { get; set; }
        public int Compensation { get; set; }
        public int SecurityLoss { get; set; }
        public int UpperLoss { get; set; }
        public int Investigation { get; set; }
        public bool RevokeLicense { get; set; }
        public bool CountsAsMajor { get; set; }
    }

    public sealed partial class ModEntry
    {
        private const string TribunalEnding = "doctor_clinic_citizen_tribunal";
        private const int RecoveryPrice = 300;
        private ClinicState tribunalQueuedFor;

        private ClinicIncident GetIncident(ClinicCase visit)
        {
            if (visit.Practice || visit.Tutorial || visit.ExternalDeath || string.IsNullOrEmpty(visit.Faction) ||
                state.Incidents.Any(incident => incident.CaseId == visit.Id)) return null;
            var events = visit.TreatmentAccidents;
            if (!visit.Dead && events.Count == 0 && visit.Wounds.All(wound => wound.Harm == 0)) return null;

            var unprotectedHarm = visit.Wounds.Select((wound, index) =>
            {
                var actions = events.Where(entry => entry.WoundIndex == index).ToArray();
                return actions.Length == 0 ? (visit.AidForTreatmentHarm ? 0 : wound.Harm) :
                    actions.Where(entry => !entry.Protected && !entry.Fatal).Select(entry => entry.Harm).DefaultIfEmpty(0).Max();
            }).ToArray();
            var wounded = unprotectedHarm.Count(harm => harm > 0);
            var serious = unprotectedHarm.Count(harm => harm >= 50);
            var deathCause = events.LastOrDefault(entry => entry.Fatal) ?? events.LastOrDefault();
            var penalty = visit.Dead
                ? ClinicPenalty.Death(visit.PoisonedDeath, false, HasActiveAssassination(visit),
                    visit.Wounds.Count(wound => !wound.Hidden) * visit.Fee, state.WhiteGlove,
                    visit.PoisonedDeath ? TakeLegalAid() : deathCause?.Protected ?? visit.AidForTreatmentHarm)
                : null;
            var includeIndependentHarm = penalty == null || penalty.UsesAid;
            return new ClinicIncident
            {
                CaseId = visit.Id, Day = Today, LicenseTerm = state.LicenseTerm, Fatal = visit.Dead,
                DoctorLoss = (penalty?.DoctorLoss ?? 0) + (includeIndependentHarm ? (wounded - serious) * 2 + serious * 8 : 0),
                Compensation = (penalty?.Compensation ?? 0) + (includeIndependentHarm ? serious * visit.Fee * 2 : 0),
                SecurityLoss = penalty?.SecurityLoss ?? 0,
                UpperLoss = penalty?.UpperLoss ?? 0,
                Investigation = penalty?.Investigation ?? 0,
                RevokeLicense = penalty?.RevokeLicense ?? false,
                CountsAsMajor = includeIndependentHarm && serious > 0
            };
        }

        private void CommitIncident(ClinicIncident incident)
        {
            if (incident == null) return;
            state.Incidents.Add(incident);
            if (incident.SecurityLoss > 0) StoreReputation.ModSecReputation(-incident.SecurityLoss);
            if (incident.UpperLoss > 0) StoreReputation.ModULReputation(-incident.UpperLoss);
            if (incident.Investigation > 0 && store.secData != null) AddInvestigationValue(store.secData, "涉嫌谋杀", incident.Investigation);
            if (incident.RevokeLicense) state.License = LicenseStatus.Revoked;
            else if (incident.CountsAsMajor)
            {
                state.MajorIncidents++;
                if (state.MajorIncidents >= 2) state.License = LicenseStatus.Suspended;
            }
            if (state.License != LicenseStatus.Active)
                store.storeClientManager.GetClientStack().RemoveAll(client =>
                    client?.identifier?.StartsWith(PatientPrefix, StringComparison.Ordinal) == true);

            var payment = Math.Min(Math.Max(0, store.playerCash), incident.Compensation);
            if (payment > 0) store.PayMoney(payment);
            state.MedicalDebt += incident.Compensation - payment;
            state.LastReputationReason = incident.Fatal ? "医疗事故死亡" : "治疗造成损伤";
            log.Warning($"诊所事故：{state.LastReputationReason}，医生扣 {incident.DoctorLoss}，赔偿 {incident.Compensation}。");
        }

        private static void AddInvestigationValue(SecData security, string charge, int amount)
        {
            // Use the investigation ledger directly so trait multipliers cannot change fixed amounts.
            var index = security.crimeTypes.IndexOf(charge);
            if (index < 0)
            {
                security.crimeTypes.Add(charge);
                security.crimeAmount.Add(amount);
                security.crimeAmountTotal.Add(amount);
                return;
            }
            security.crimeAmount[index] += amount;
            security.crimeAmountTotal[index] += amount;
        }

        private void ReviewSuspension()
        {
            if (state.License == LicenseStatus.Suspended &&
                state.Incidents.Any(incident => incident.LicenseTerm == state.LicenseTerm && incident.Day < Today))
            {
                state.License = LicenseStatus.Revoked;
                Save();
            }
        }

        private void RestoreLicense(bool showAssociation = true)
        {
            if (!IsLoaded || state.License != LicenseStatus.Revoked || state.EndingPending ||
                state.EndingTriggered || ModHelper.IsGameOver()) return;
            if (store.playerCash < RecoveryPrice)
            {
                ShowAssociation($"恢复执照需要 {RecoveryPrice} 信用点。");
                return;
            }
            store.PayMoney(RecoveryPrice, true);
            state.License = LicenseStatus.Active;
            state.LicenseTerm++;
            state.MajorIncidents = 0;
            state.ResumeAfterDay = Today;
            Save();
            if (showAssociation) ShowAssociation("王尔德已恢复你的执照。下个营业日重新排班。");
        }

        private void PayMedicalDebt()
        {
            if (!IsLoaded || state.MedicalDebt <= 0) return;
            var payment = Math.Min(Math.Max(0, store.playerCash), state.MedicalDebt);
            if (payment > 0)
            {
                store.PayMoney(payment);
                state.MedicalDebt -= payment;
                Save();
            }
            ShowAssociation(payment == 0 ? "没有可用于偿还赔偿的现金。" : $"已偿还赔偿 {payment}。");
        }

        private void QueueTribunalIfNeeded()
        {
            if (!IsLoaded || state.EndingTriggered || ModHelper.IsGameOver()) return;
            if (!state.EndingPending && state.ReputationHalfPoints > -200) return;
            if (!state.EndingPending)
            {
                state.EndingPending = true;
                store.storeClientManager.GetClientStack().RemoveAll(client =>
                    client?.identifier?.StartsWith(PatientPrefix, StringComparison.Ordinal) == true);
                Save();
            }
            if (tribunalQueuedFor == state) return;
            var run = state;
            tribunalQueuedFor = run;
            ModHelper.Delay(ModId, 0f, () =>
            {
                if (tribunalQueuedFor == run) tribunalQueuedFor = null;
                if (state == run) TryTriggerTribunal();
            });
        }

        private void TryTriggerTribunal()
        {
            if (!IsLoaded || !state.EndingPending || state.EndingTriggered || ModHelper.IsGameOver()) return;
            Close();
            CustomUIManager.Instance?.CloseWindow(AssociationWindow);
            CloseClinicPanels();
            if (ModHelper.TriggerEnding(ModId, TribunalEnding))
            {
                state.EndingTriggered = true;
                Save();
            }
        }
    }
}
