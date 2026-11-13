using static DoctorClinic.ClinicText;
using System;
using System.Linq;

namespace DoctorClinic
{
    internal enum PatientTier { Lower, Security, Upper }
    internal enum Difficulty { 简单, 困难 }
    internal enum Treatment { Alcohol, Bandage, TopicalBandage, ColdPack, HemostaticBandage, Phagimycin, UnlicensedPhagimycin,
        Splint, Suture, Forceps, Scalpel, BloodBag, DreamDust, Oximor, Nomore, DuskReagent, Zyanide, Antifungal, ToxinNeutralizer, Immunivax, Naloxone, SkinCare, VascularCare, Synaprest }
    internal enum ConditionKind { Abrasion, Contusion, GraywaterFever, WoundInfection, ShallowCut,
        Fracture, Gunshot, DeepCut, BloodLoss, Burn, PipeSporeDisease, IndustrialToxinExposure, SentinelCollapse, OximorOverdose, SkinCare, VascularCare, OximorDependence }
    internal enum LicenseStatus { None, Active, Suspended, Revoked }

    internal sealed class ClinicTreatmentAccident
    {
        public int WoundIndex { get; set; }
        public int Harm { get; set; }
        public bool Fatal { get; set; }
        public bool Protected { get; set; }
    }

    internal sealed class ClinicState
    {
        public int ReputationHalfPoints { get; set; }
        public ClinicCase Current { get; set; }
        public LicenseStatus License { get; set; }
        public int JoinedDay { get; set; }
        public bool InvitationSeen { get; set; }
        public bool InvitationRequested { get; set; }
        public bool InvitationPermanentlyDeclined { get; set; }
        public int InvitationRetryDay { get; set; }

        public bool ShouldInviteToAssociation(int day) => !InvitationPermanentlyDeclined &&
            (!InvitationSeen || InvitationRequested || (InvitationRetryDay > 0 && day >= InvitationRetryDay));
        public bool TutorialCompleted { get; set; }
        public int SuccessfulVisits { get; set; }
        public string LastReputationReason { get; set; }
        public ClinicSchedule Schedule { get; set; }
        public bool Paused { get; set; }
        public int ScheduleSeed { get; set; }
        public ClinicPharmacyStock Pharmacy { get; set; }
        public bool MedicalChannel { get; set; }
        public bool IllegalMedicalChannel { get; set; }
        public int MedicalCallReadyDay { get; set; }
        public int IllegalMedicalCallReadyDay { get; set; }
        public bool MedicalDeliveryPending { get; set; }
        public bool IllegalMedicalDeliveryPending { get; set; }
        public int MedicalDeliveryDay { get; set; }
        public int IllegalMedicalDeliveryDay { get; set; }
        public int MedicationInvestigationRemainder { get; set; }

        public int AccumulateMedicationInvestigation(long doseValue)
        {
            var quarters = doseValue % 4 + MedicationInvestigationRemainder;
            MedicationInvestigationRemainder = (int)(quarters % 4);
            return (int)Math.Min(int.MaxValue, doseValue / 4 + quarters / 4);
        }
        public bool Poisoner { get; set; }
        public bool WhiteGlove { get; set; }
        public bool LegalAid { get; set; }
        public int LegalAidNextDay { get; set; }
        public System.Collections.Generic.List<ClinicCommission> Commissions { get; set; } = new System.Collections.Generic.List<ClinicCommission>();
        public System.Collections.Generic.List<ClinicPoisoning> Poisonings { get; set; } = new System.Collections.Generic.List<ClinicPoisoning>();
        public int LicenseTerm { get; set; } = 1;
        public int MajorIncidents { get; set; }
        public int MedicalDebt { get; set; }
        public int ResumeAfterDay { get; set; }
        public bool EndingPending { get; set; }
        public bool EndingTriggered { get; set; }
        public System.Collections.Generic.List<ClinicIncident> Incidents { get; set; } = new System.Collections.Generic.List<ClinicIncident>();
        public System.Collections.Generic.List<ClinicReturn> Returns { get; set; } = new System.Collections.Generic.List<ClinicReturn>();
    }

    internal sealed class ClinicCase
    {
        private static readonly int[,] Budgets = { { 180, 300, 420 }, { 225, 375, 495 }, { 525, 750, 900 } };
        private static readonly int[] Fees = { 70, 80, 100 };
        private static readonly int[] Bonuses = { 40, 50, 80 };

        public string Id { get; set; }
        public string PatientId { get; set; }
        public string Faction { get; set; }
        public string PatientName { get; set; }
        public string Portrait { get; set; }
        public int? BudgetBase { get; set; }
        public int? BudgetOffset { get; set; }
        public bool Arrived { get; set; }
        public bool Practice { get; set; }
        public bool Tutorial { get; set; }
        public int BloodRevision { get; set; }
        public int SampleItemId { get; set; }
        public int SampleRevision { get; set; } = -1;
        public int ReportRevision { get; set; } = -1;
        public bool ReportPositive { get; set; }
        public bool ReportBloodLoss { get; set; }
        public bool ReportFungus { get; set; }
        public bool ReportToxin { get; set; }
        public bool ReportSentinel { get; set; }
        public bool ReportOverdose { get; set; }
        public bool ReportDependence { get; set; }
        public bool AddictionRequiresCure { get; set; }
        public int Patience { get; set; } = 2;
        public bool PatientWalkedOut => Patience < 0;
        public void GainPatience(bool perfect) => Patience += perfect ? 2 : 1;
        public void LosePatience(bool stopped) => Patience -= stopped ? 2 : 1;
        public bool Dead { get; set; }
        public bool ExternalDeath { get; set; }
        public bool TreatmentPerformed { get; set; }
        public bool PoisonedDeath { get; set; }
        public bool AidForTreatmentHarm { get; set; } // Compatibility with earlier saves.
        public System.Collections.Generic.List<ClinicTreatmentAccident> TreatmentAccidents { get; set; } =
            new System.Collections.Generic.List<ClinicTreatmentAccident>();
        public bool? AcceptsAnesthesia { get; set; }
        public Treatment? AnesthesiaOffer { get; set; }
        public bool AnesthesiaUsed { get; set; }
        public int AnesthesiaCost { get; set; }
        public int WastedAnesthesiaCost { get; set; }
        public int? AnesthesiaWound { get; set; }
        public int DreamDoseId { get; set; }
        public int HiddenBudget { get; set; }
        public int HiddenQuote
        {
            get
            {
                var wound = Wounds.FirstOrDefault(entry => entry.Hidden);
                return wound?.UsesMedicineOnlyPricing == true ? Fee + 25 : Fee + Bonus + 25 + 100 + (wound?.DifficultyBonus ?? 10);
            }
        }
        public int Budget { get; set; }
        public int Fee { get; set; }
        public int PricingVersion { get; set; }
        public int Bonus { get; set; }
        public Abrasion[] Wounds { get; set; }
        public int NativeDiscountPercent { get; set; }
        public bool Settled { get; set; }
        public int Paid { get; set; }
        public BillLine[] Settlement { get; set; }

        public static ClinicCase Create(PatientTier tier, int count, int offset)
        {
            var wounds = new Abrasion[count];
            for (var i = 0; i < count; i++) wounds[i] = new Abrasion { Id = Guid.NewGuid().ToString("N") };
            return new ClinicCase
            {
                Id = Guid.NewGuid().ToString("N"),
                PatientId = Guid.NewGuid().ToString("N"),
                BudgetBase = Budgets[(int)tier, count - 1],
                BudgetOffset = offset,
                Budget = Budgets[(int)tier, count - 1] + offset,
                Fee = Fees[(int)tier],
                PricingVersion = 1,
                Bonus = Bonuses[(int)tier],
                Wounds = wounds
            };
        }

        public static ClinicCase CreateCare(ConditionKind kind, int offset)
        {
            var visit = Create(PatientTier.Upper, 1, offset);
            var minimum = kind == ConditionKind.SkinCare ? Abrasion.SkinCarePrice + visit.Fee + 20 : 0;
            visit.BudgetBase = Math.Max(visit.BudgetBase.Value, minimum + 10);
            visit.Budget = visit.BudgetBase.Value + offset;
            visit.Wounds[0].Kind = kind;
            return visit;
        }

        public int Charge(Abrasion wound) =>
            wound.Covered ? wound.BillableCost + Fee + WoundDifficultyBonus(wound) + WoundQualityBonus(wound) : 0;

        private static int WoundDifficultyBonus(Abrasion wound) =>
            wound.Covered && !wound.UsesMedicineOnlyPricing ? wound.DifficultyBonus : 0;

        private int WoundQualityBonus(Abrasion wound) => wound.IsPerfect && !wound.UsesMedicineOnlyPricing ? Bonus : 0;

        public bool CanSettle => Wounds.Any(wound => wound.Covered);

        public bool RequiresAddictionCure(Abrasion wound) => AddictionRequiresCure || wound.AddictionCureAccepted;
        public bool HasOximorDependence => Wounds.Any(wound => wound.Kind == ConditionKind.OximorDependence);
        public bool HasUntreatedOximorOverdose => Wounds.Any(wound => wound.Kind == ConditionKind.OximorOverdose && !wound.Covered);

        public bool CanTreat(int index, Treatment treatment)
        {
            var wound = Wounds[index];
            if (!wound.HasEffect(treatment) || (wound.Hidden && HiddenBudget == 0)) return false;
            if (wound.Kind == ConditionKind.OximorDependence &&
                treatment != (RequiresAddictionCure(wound) ? Treatment.Synaprest : Treatment.Oximor)) return false;
            if (treatment == Treatment.Oximor && HasUntreatedOximorOverdose) return false;
            if (!wound.RequiresBloodTest) return true;
            if (wound.Kind == ConditionKind.BloodLoss && wound.BloodSources != null)
                return wound.BloodSources.All(source => Wounds[source].BleedingControlled || Wounds[source].Covered);
            if (!wound.LinkedWound.HasValue) return true;
            var linked = Wounds[wound.LinkedWound.Value];
            return linked.Covered || (wound.Kind == ConditionKind.BloodLoss && linked.BleedingControlled);
        }

        public int FindSystemicTreatment(Treatment treatment)
        {
            var match = -1;
            for (var index = 0; index < Wounds.Length; index++)
            {
                var wound = Wounds[index];
                if (!(wound.RequiresBloodTest || wound.IsCare) || !wound.HasEffect(treatment)) continue;
                if (CanTreat(index, treatment)) return index;
                if (match < 0) match = index;
            }
            return match;
        }

        public bool HasActiveBloodLoss => Wounds.Any(wound => wound.Kind == ConditionKind.BloodLoss && !wound.Covered);
        public bool BloodLossRolled { get; set; }
        public void UpdateUnsettledPricing()
        {
            if (Settled || PricingVersion != 0) return;
            if (Fee == 80 || Fee == 90) Fee -= 10;
            PricingVersion = 1;
        }

        public void RollBloodLoss(int roll)
        {
            if (BloodLossRolled || Wounds.Any(wound => wound.Kind == ConditionKind.BloodLoss)) return;
            BloodLossRolled = true;
            var sources = Enumerable.Range(0, Wounds.Length).Where(index => Wounds[index].BloodLossRisk > 0).ToArray();
            if (roll >= Math.Min(100, sources.Sum(index => Wounds[index].BloodLossRisk))) return;
            Wounds = Wounds.Append(new Abrasion
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = ConditionKind.BloodLoss,
                BloodSources = sources
            }).ToArray();
        }

        public bool ScanSample(int sampleItemId) => sampleItemId != 0 && sampleItemId == SampleItemId && ScanSample();

        public bool ScanSample()
        {
            if (SampleRevision < 0 || SampleRevision != BloodRevision) return false;
            ReportRevision = SampleRevision;
            ReportPositive = Wounds.Any(wound => !wound.Covered &&
                (wound.Kind == ConditionKind.GraywaterFever || wound.Kind == ConditionKind.WoundInfection));
            ReportFungus = Wounds.Any(wound => wound.Kind == ConditionKind.PipeSporeDisease && !wound.Covered);
            ReportToxin = Wounds.Any(wound => wound.Kind == ConditionKind.IndustrialToxinExposure && !wound.Covered);
            ReportSentinel = Wounds.Any(wound => wound.Kind == ConditionKind.SentinelCollapse && !wound.Covered);
            ReportOverdose = Wounds.Any(wound => wound.Kind == ConditionKind.OximorOverdose && !wound.Covered);
            ReportDependence = Wounds.Any(wound => wound.Kind == ConditionKind.OximorDependence && !wound.Covered);
            ReportBloodLoss = Wounds.Any(wound => wound.Kind == ConditionKind.BloodLoss && !wound.Covered);
            foreach (var wound in Wounds.Where(wound => wound.RequiresBloodTest))
                wound.Diagnosed = true;
            return true;
        }

        public BillLine[] Preview()
        {
            var balance = Budget;
            var hiddenBalance = HiddenBudget;
            var auxiliaryIndex = AnesthesiaWound ?? -1;
            if (AnesthesiaCost > 0 && (auxiliaryIndex < 0 || auxiliaryIndex >= Wounds.Length || Wounds[auxiliaryIndex].Hidden))
                auxiliaryIndex = Array.FindIndex(Wounds, wound => !wound.Hidden);
            var costIndex = AnesthesiaWound ?? 0;
            return Wounds.Select((wound, index) =>
            {
                var auxiliary = index == auxiliaryIndex ? AnesthesiaCost : 0;
                var gross = Charge(wound) + auxiliary;
                var legacy = Math.Min(gross, Math.Max(0, wound.Discount));
                var percent = Math.Max(0, Math.Min(100, NativeDiscountPercent));
                // Rev14 rounds the discounted price, not the discount amount.
                var discountedPrice = (int)Math.Round(gross * (1d - percent / 100d));
                var net = Math.Min(gross - legacy, discountedPrice);
                var tip = wound.Hidden && wound.Covered && !wound.UsesMedicineOnlyPricing && HiddenBudget > 0 ? 100 : 0;
                var payable = wound.Hidden ? Math.Min(hiddenBalance, net + tip) : Math.Min(balance, net);
                var auxiliaryNet = auxiliary == 0 ? 0 : Math.Max(0, net -
                    Math.Min(Charge(wound) - Math.Min(Charge(wound), legacy),
                        (int)Math.Round(Charge(wound) * (1d - percent / 100d))));
                var auxiliaryPayable = Math.Min(payable, auxiliaryNet);
                if (PatientWalkedOut) payable = auxiliaryPayable = 0;
                if (wound.Hidden) hiddenBalance -= payable;
                else balance -= payable;
                return new BillLine
                {
                    Material = (wound.Covered ? wound.BillableCost : 0) + auxiliary,
                    Service = wound.Covered ? Fee : 0,
                    DifficultyBonus = WoundDifficultyBonus(wound),
                    QualityBonus = WoundQualityBonus(wound),
                    Gross = gross + tip,
                    Discount = gross - net,
                    Tip = tip,
                    Payable = payable,
                    AuxiliaryPayable = auxiliaryPayable,
                    EstimatedCost = wound.EstimatedCost + (index == costIndex ? AnesthesiaCost + WastedAnesthesiaCost : 0),
                    ReputationHalfPoints = wound.Covered && !PatientWalkedOut
                        ? (wound.IsPerfect && !wound.UsesMedicineOnlyPricing ? 2 : 1) : 0
                };
            }).ToArray();
        }

        public int ApplySettlement(ClinicState state, bool referral, int doctorLoss = 0)
        {
            if (Settled || (!referral && !CanSettle)) return 0;
            Settlement = Preview();
            Paid = Settlement.Sum(line => line.Payable);
            if (Practice || Tutorial)
            {
                Paid = 0;
                foreach (var line in Settlement) { line.Payable = 0; line.ReputationHalfPoints = 0; }
                if (Tutorial && Wounds.All(wound => wound.Covered)) state.TutorialCompleted = true;
            }
            else
            {
                var gained = Settlement.Sum(line => line.ReputationHalfPoints);
                state.ReputationHalfPoints = Math.Max(-200, Math.Min(200, state.ReputationHalfPoints + gained - doctorLoss * 2));
                if (gained > 0) state.LastReputationReason = "clinic_reason_completed_treatment";
                if (!Dead && !PatientWalkedOut && Wounds.All(wound => wound.Harm == 0) && !string.IsNullOrEmpty(Faction) &&
                    Wounds.Where(wound => !wound.Hidden).All(wound => wound.Covered))
                    state.SuccessfulVisits++;
            }
            Settled = true;
            return Paid;
        }
    }

    internal sealed class BillLine
    {
        public int Material { get; set; }
        public int Service { get; set; }
        public int DifficultyBonus { get; set; }
        public int QualityBonus { get; set; }
        public int Gross { get; set; }
        public int Discount { get; set; }
        public int Payable { get; set; }
        public int AuxiliaryPayable { get; set; }
        public int Tip { get; set; }
        public int EstimatedCost { get; set; }
        public int ReputationHalfPoints { get; set; }
    }

    internal sealed class Abrasion
    {
        public string Id { get; set; }
        public ConditionKind Kind { get; set; }
        public int? Region { get; set; }
        public bool BleedingControlled { get; set; }
        public bool ForeignBodyRemoved { get; set; }
        public bool Closed { get; set; }
        public bool Fixed { get; set; }
        public bool Cooled { get; set; }
        public int Harm { get; set; }
        public int? LinkedWound { get; set; }
        public int[] BloodSources { get; set; }
        public bool Diagnosed { get; set; }
        public bool AddictionCureAccepted { get; set; }
        public bool Hidden { get; set; }
        public const int AntifungalPrice = 35;
        public const int AntifungalDoses = 5;
        public const int SkinCarePrice = 500;
        public const int VascularCarePrice = 250;
        public const int ToxinNeutralizerPrice = 50;

        public bool RequiresBloodTest => Kind == ConditionKind.GraywaterFever || Kind == ConditionKind.WoundInfection ||
            Kind == ConditionKind.BloodLoss || Kind == ConditionKind.PipeSporeDisease ||
            Kind == ConditionKind.IndustrialToxinExposure || Kind == ConditionKind.SentinelCollapse ||
            Kind == ConditionKind.OximorOverdose || Kind == ConditionKind.OximorDependence;
        public bool UsesMedicineOnlyPricing => RequiresBloodTest && Kind != ConditionKind.BloodLoss && !IsCare;
        public bool IsCare => Kind == ConditionKind.SkinCare || Kind == ConditionKind.VascularCare;
        public bool IsInjury => (!RequiresBloodTest && !IsCare) || Kind == ConditionKind.BloodLoss;
        public bool ComplexWound => Kind == ConditionKind.Gunshot || Kind == ConditionKind.DeepCut;
        public bool IsLifeThreatening => ComplexWound || Kind == ConditionKind.BloodLoss;
        public bool IsPerfect => Covered && Perfect && Harm == 0;
        public int DifficultyBonus => Kind switch
        {
            ConditionKind.Gunshot => 30,
            ConditionKind.DeepCut => 20,
            ConditionKind.Fracture => 20,
            ConditionKind.Burn => 20,
            ConditionKind.BloodLoss => 20,
            ConditionKind.WoundInfection => 20,
            ConditionKind.PipeSporeDisease => 20,
            ConditionKind.IndustrialToxinExposure => 20,
            ConditionKind.SentinelCollapse => 30,
            ConditionKind.OximorOverdose => 30,
            ConditionKind.SkinCare => 20,
            ConditionKind.VascularCare => 20,
            ConditionKind.OximorDependence => 0,
            _ => 10
        };
        public int BloodLossRisk => Kind switch
        {
            ConditionKind.ShallowCut => 5,
            ConditionKind.DeepCut => 40,
            ConditionKind.Burn => 10,
            ConditionKind.Gunshot => 30,
            _ => 0
        };
        public string Name => Kind switch
        {
            ConditionKind.Contusion => T("clinic_bruise"),
            ConditionKind.ShallowCut => T("clinic_shallow_cut"),
            ConditionKind.Fracture => T("clinic_fracture"),
            ConditionKind.Gunshot => T("clinic_gunshot_wound"),
            ConditionKind.DeepCut => T("clinic_deep_cut"),
            ConditionKind.Burn => T("clinic_burn"),
            ConditionKind.BloodLoss => Diagnosed ? T("clinic_blood_loss") : T("clinic_undiagnosed_symptoms"),
            ConditionKind.GraywaterFever => Diagnosed ? T("clinic_graywater_fever") : T("clinic_undiagnosed_symptoms"),
            ConditionKind.WoundInfection => Diagnosed ? T("clinic_secondary_wound_infection") : T("clinic_undiagnosed_symptoms"),
            ConditionKind.PipeSporeDisease => Diagnosed ? T("clinic_pipe_spore_disease") : T("clinic_undiagnosed_symptoms"),
            ConditionKind.IndustrialToxinExposure => Diagnosed ? T("clinic_industrial_toxin_exposure") : T("clinic_undiagnosed_symptoms"),
            ConditionKind.SentinelCollapse => Diagnosed ? T("clinic_sentinel_collapse_syndrome") : T("clinic_undiagnosed_symptoms"),
            ConditionKind.OximorOverdose => Diagnosed ? T("clinic_oxymore_overdose") : T("clinic_undiagnosed_symptoms"),
            ConditionKind.SkinCare => T("clinic_skin_care"),
            ConditionKind.VascularCare => T("clinic_vascular_care"),
            ConditionKind.OximorDependence => Diagnosed ? T("clinic_oxymore_dependence") : T("clinic_undiagnosed_symptoms"),
            _ => T("clinic_abrasion")
        };
        public bool Cleaned { get; set; }
        public bool Covered { get; set; }
        public bool Perfect { get; set; }
        public int BillableCost { get; set; }
        public int EstimatedCost { get; set; }
        public int Discount { get; set; } // Preserve discounts already promised by older builds.

        public bool HasEffect(Treatment treatment)
        {
            if (Covered) return false;
            if (Kind == ConditionKind.BloodLoss) return treatment == Treatment.BloodBag;
            if (Kind == ConditionKind.PipeSporeDisease) return treatment == Treatment.Antifungal;
            if (Kind == ConditionKind.IndustrialToxinExposure) return treatment == Treatment.ToxinNeutralizer;
            if (Kind == ConditionKind.SentinelCollapse) return treatment == Treatment.Immunivax;
            if (Kind == ConditionKind.OximorOverdose) return treatment == Treatment.Naloxone;
            if (Kind == ConditionKind.SkinCare) return treatment == Treatment.SkinCare;
            if (Kind == ConditionKind.VascularCare) return treatment == Treatment.VascularCare;
            if (Kind == ConditionKind.OximorDependence) return treatment == Treatment.Oximor || treatment == Treatment.Synaprest;
            if (RequiresBloodTest) return treatment == Treatment.Phagimycin || treatment == Treatment.UnlicensedPhagimycin;
            if (Kind == ConditionKind.Contusion) return treatment == Treatment.ColdPack;
            if (Kind == ConditionKind.Burn) return Cooled ? treatment == Treatment.TopicalBandage : treatment == Treatment.ColdPack;
            if (Kind == ConditionKind.Fracture)
                return treatment == Treatment.Splint ? !Fixed : Fixed && IsBandage(treatment);
            if (ComplexWound)
            {
                if (Kind == ConditionKind.Gunshot && !ForeignBodyRemoved) return treatment == Treatment.Forceps;
                if (!Cleaned) return treatment == Treatment.Alcohol;
                if (!Closed) return treatment == Treatment.Suture;
                return IsBandage(treatment);
            }
            if (treatment == Treatment.Alcohol) return !Cleaned;
            return IsBandage(treatment);
        }

        private static bool IsBandage(Treatment treatment) => treatment == Treatment.Bandage ||
            treatment == Treatment.TopicalBandage || treatment == Treatment.HemostaticBandage;

        public void Apply(Treatment treatment, bool valid = true, int? materialCost = null, bool standardQuality = true)
        {
            var cost = materialCost ?? (treatment switch
            {
                Treatment.Alcohol => 35,
                Treatment.TopicalBandage => 16,
                Treatment.ColdPack => 15,
                Treatment.HemostaticBandage => 16,
                Treatment.Phagimycin => 25,
                Treatment.UnlicensedPhagimycin => 5,
                Treatment.Splint => 20,
                Treatment.Suture => 25,
                Treatment.BloodBag => 33,
                Treatment.Antifungal => AntifungalPrice / AntifungalDoses,
                Treatment.ToxinNeutralizer => ToxinNeutralizerPrice,
                Treatment.Immunivax => 160,
                Treatment.Naloxone => 30,
                Treatment.SkinCare => SkinCarePrice,
                Treatment.VascularCare => VascularCarePrice,
                Treatment.Oximor => 50,
                Treatment.Synaprest => 350,
                Treatment.Forceps => 0,
                Treatment.Scalpel => 0,
                _ => 8
            });
            EstimatedCost += cost;
            if (!valid || !HasEffect(treatment)) return;
            BillableCost += cost;
            if (treatment == Treatment.Alcohol) Cleaned = true;
            else if (treatment == Treatment.Splint) Fixed = true;
            else if (treatment == Treatment.Forceps)
            {
                ForeignBodyRemoved = true;
                if (Kind == ConditionKind.Gunshot) BleedingControlled = true;
            }
            else if (treatment == Treatment.Suture)
            {
                Closed = true;
                if (Kind == ConditionKind.DeepCut) BleedingControlled = true;
            }
            else if (Kind == ConditionKind.Burn && treatment == Treatment.ColdPack) Cooled = true;
            else
            {
                Covered = true;
                Perfect = Cleaned || Kind == ConditionKind.Contusion || Kind == ConditionKind.Fracture ||
                    Kind == ConditionKind.Burn || (RequiresBloodTest && treatment != Treatment.UnlicensedPhagimycin && !IsCare &&
                    Kind != ConditionKind.OximorDependence && standardQuality);
            }
        }
    }
}
