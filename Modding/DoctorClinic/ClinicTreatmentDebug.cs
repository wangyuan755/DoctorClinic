using static DoctorClinic.ClinicText;
using System;
using System.Linq;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private static string WoundDebug => T("clinic_wounds");
        private static string BloodDebug => T("clinic_blood_tests_disease");
        private static string DependenceDebug => T("clinic_dependence_overdose");
        private static string CareDebug => T("clinic_maintenance_care");
        private static string AnesthesiaDebug => T("clinic_anesthesia");
        private static string MixedDebug => T("clinic_compound_cases_incidents");
        // Rev14 gives every tab its own column; the public build shares the vanilla Modding
        // column, so each button keeps its tab name as a prefix there.
        private static string DebugCategory(string tab) => ClinicCompat.DebugCategory(tab);
        private static string DebugLabel(string tab, string name) => ClinicCompat.DebugLabel(tab, name);
        private const string DebugMessageWindow = "doctor_clinic_treatment_debug_message";
        private const string DebugBandage = "bandage_item";
        private const string DebugAlcohol = "rubbing_alcohol";
        private const string DebugTopical = "topical_bandage_item";
        private const string DebugHemostatic = "hemostatic_bandage_item";
        private const string DebugOximor = "pink_injector";
        private const string DebugNomore = "pink_alt_injector";
        private const string DebugSynaprest = "black_injector";
        private static string[] ClinicDebugTabs => new[]
            { T("clinic_treatment"), WoundDebug, BloodDebug, DependenceDebug, CareDebug, AnesthesiaDebug, MixedDebug };

        private void RegisterTreatmentDebugButtons()
        {
            AddTreatmentDebug(WoundDebug, T("clinic_abrasion"), ConditionKind.Abrasion, new[] { DebugAlcohol, DebugBandage });
            AddTreatmentDebug(WoundDebug, T("clinic_shallow_cut"), ConditionKind.ShallowCut, new[] { DebugAlcohol, DebugBandage, DebugHemostatic, DebugTopical });
            AddTreatmentDebug(WoundDebug, T("clinic_bruise_contusion"), ConditionKind.Contusion, new[] { MedicalPack.ColdPack });
            AddTreatmentDebug(WoundDebug, T("clinic_burn"), ConditionKind.Burn, new[] { MedicalPack.ColdPack, DebugTopical });
            AddTreatmentDebug(WoundDebug, T("clinic_limb_fracture"), ConditionKind.Fracture, new[] { MedicalPack.Splint, DebugBandage });
            AddTreatmentDebug(WoundDebug, T("clinic_deep_cut"), ConditionKind.DeepCut, new[] { DebugAlcohol, MedicalPack.Suture, DebugBandage });
            AddTreatmentDebug(WoundDebug, T("clinic_gunshot_wound"), ConditionKind.Gunshot, new[] { Forceps, DebugAlcohol, MedicalPack.Suture, DebugBandage });

            AddTreatmentDebug(BloodDebug, T("clinic_blood_loss_transfusion"), ConditionKind.DeepCut,
                new[] { DebugAlcohol, MedicalPack.Suture, DebugBandage, "blood_bag" },
                new[] { ConditionKind.BloodLoss }, visit => visit.Wounds[1].LinkedWound = 0);
            AddTreatmentDebug(BloodDebug, T("clinic_graywater_fever_licensed_phagimycin"), ConditionKind.GraywaterFever, new[] { "phagimycin_pill" });
            AddTreatmentDebug(BloodDebug, T("clinic_graywater_fever_unlicensed_phagimycin"), ConditionKind.GraywaterFever, new[] { "unlicensed_phagimycin_pill" });
            AddTreatmentDebug(BloodDebug, T("clinic_wound_infection"), ConditionKind.ShallowCut, new[] { DebugAlcohol, DebugBandage, "phagimycin_pill" },
                new[] { ConditionKind.WoundInfection }, visit => visit.Wounds[1].LinkedWound = 0);
            AddTreatmentDebug(BloodDebug, T("clinic_pipe_spore_disease_2"), ConditionKind.PipeSporeDisease, new[] { Antifungal });
            AddTreatmentDebug(BloodDebug, T("clinic_industrial_toxin_exposure"), ConditionKind.IndustrialToxinExposure, new[] { ToxinNeutralizer });

            AddTreatmentDebug(DependenceDebug, T("clinic_oxymore_dependence_temporary_relief"), ConditionKind.OximorDependence,
                new[] { DebugOximor, DebugNomore }, tier: PatientTier.Lower);
            AddTreatmentDebug(DependenceDebug, T("clinic_oxymore_dependence_propose_synaprest"), ConditionKind.OximorDependence,
                new[] { DebugSynaprest }, tier: PatientTier.Lower);
            AddTreatmentDebug(DependenceDebug, T("clinic_oxymore_dependence_upper_levels"), ConditionKind.OximorDependence,
                new[] { DebugSynaprest, DebugOximor }, setup: visit => visit.AddictionRequiresCure = true);
            AddTreatmentDebug(DependenceDebug, T("clinic_oxymore_overdose_naloxgone"), ConditionKind.OximorOverdose, new[] { Naloxone, DebugOximor });

            AddTreatmentDebug(CareDebug, T("clinic_sentinel_syndrome_genuine_immunivax"), ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item) == (160, null), prepare: InsInjectorHelper.SetGenuine);
            AddTreatmentDebug(CareDebug, T("clinic_sentinel_syndrome_expired_immunivax"), ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item).Price == 80, prepare: InsInjectorHelper.SetExpired);
            AddTreatmentDebug(CareDebug, T("clinic_sentinel_syndrome_counterfeit_immunivax"), ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item).Price == 40, prepare: InsInjectorHelper.SetCounterfeit);
            AddTreatmentDebug(CareDebug, T("clinic_sentinel_syndrome_empty_injector"), ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item).Price == 0, prepare: InsInjectorHelper.SetUnusable);
            AddTreatmentDebug(CareDebug, T("clinic_skin_care_eternal_rejuvenation_serum"), ConditionKind.SkinCare, new[] { "skincare_cream" });
            AddTreatmentDebug(CareDebug, T("clinic_vascular_care_blue_blood"), ConditionKind.VascularCare, new[] { "blue_blood_bag" });

            AddAnesthesiaDebug(T("clinic_nomore_accept_anesthesia"), DebugNomore, false, matches: HasUsableNomore);
            AddAnesthesiaDebug(T("clinic_nomore_insufficient_purity"), DebugNomore, true,
                matches: item => !HasUsableNomore(item), prepare: item => MedsItemDirectory.SetNomoreInjectorPurity(item, 30, 30));
            AddAnesthesiaDebug(T("clinic_oxymore_accept_anesthesia"), DebugOximor, true);
            AddAnesthesiaDebug(T("clinic_oxymore_refuse_anesthesia"), DebugOximor, false);
            AddAnesthesiaDebug(T("clinic_dream_dust_accept_anesthesia"), "dream_dust", true);
            AddAnesthesiaDebug(T("clinic_dream_dust_refuse_anesthesia"), "dream_dust", false);

            AddTreatmentDebug(MixedDebug, T("clinic_compound_injuries_gunshot_fracture_blood_loss"), ConditionKind.Gunshot,
                new[] { Forceps, DebugAlcohol, MedicalPack.Suture, MedicalPack.Splint, DebugBandage, DebugBandage, "blood_bag" },
                new[] { ConditionKind.Fracture, ConditionKind.BloodLoss }, visit =>
                {
                    visit.Wounds[2].LinkedWound = 0;
                });
            AddTreatmentDebug(MixedDebug, T("clinic_hidden_condition_abrasion_and_graywater_fever"), ConditionKind.Abrasion,
                new[] { DebugBandage, "phagimycin_pill" }, new[] { ConditionKind.GraywaterFever },
                visit => visit.Wounds[1].Hidden = true);
            AddTreatmentDebug(MixedDebug, T("clinic_care_and_hidden_condition_blue_blood_and"), ConditionKind.VascularCare,
                new[] { "blue_blood_bag", "phagimycin_pill" }, new[] { ConditionKind.GraywaterFever },
                visit => visit.Wounds[1].Hidden = true);
            AddTreatmentDebug(MixedDebug, T("clinic_patience_hard_patient_stops_suturing"), ConditionKind.DeepCut,
                new[] { DebugAlcohol, MedicalPack.Suture, DebugBandage });
            AddTreatmentDebug(MixedDebug, T("clinic_patience_hard_bleeding_patient_stops_suturing"), ConditionKind.DeepCut,
                new[] { DebugAlcohol, MedicalPack.Suture, DebugBandage, "blood_bag" },
                new[] { ConditionKind.BloodLoss }, visit => visit.Wounds[1].LinkedWound = 0);
        }

        private void AddAnesthesiaDebug(string name, string drug, bool consent, Func<GameItem, bool> matches = null,
            Action<GameItem> prepare = null) =>
            AddTreatmentDebug(AnesthesiaDebug, name, ConditionKind.Abrasion, new[] { drug, DebugBandage },
                setup: visit => visit.AcceptsAnesthesia = consent, matches: matches, prepare: prepare);

        private void AddTreatmentDebug(string tab, string name, ConditionKind primary, string[] supplies,
            ConditionKind[] additional = null, Action<ClinicCase> setup = null, PatientTier tier = PatientTier.Upper,
            Func<GameItem, bool> matches = null, Action<GameItem> prepare = null)
        {
            ModHelper.AddDebugFunction(ModId, DebugCategory(tab), DebugLabel(tab, name), () =>
            {
                var conditions = new[] { primary }.Concat(additional ?? Array.Empty<ConditionKind>()).ToArray();
                var visit = primary == ConditionKind.SkinCare
                    ? ClinicCase.CreateCare(primary, 0)
                    : ClinicCase.Create(tier, conditions.Length, 0);
                for (var index = 0; index < conditions.Length; index++) visit.Wounds[index].Kind = conditions[index];
                setup?.Invoke(visit);
                StartTreatmentDebug(name, visit, tier, supplies, matches, prepare);
            });
        }

        private void StartTreatmentDebug(string name, ClinicCase visit, PatientTier tier, string[] supplies,
            Func<GameItem, bool> matches, Action<GameItem> prepare)
        {
            if (!IsLoaded || ClinicCompat.IsGameOver())
            {
                ShowTreatmentDebugMessage(T("clinic_load_a_playable_save_before_starting_a"));
                return;
            }
            if (state.Current != null && !state.Current.Settled)
            {
                Show();
                ShowTreatmentDebugMessage(T("clinic_select_end_settle_in_the_current_case"));
                return;
            }

            visit.Practice = true;
            visit.PatientName = name + T("clinic_practice_patient");
            visit.Faction = tier == PatientTier.Lower ? StoreClient.FACTION_LOWER : StoreClient.FACTION_UPPER;
            AssignRegions(visit);
            var required = supplies.AsEnumerable();
            if (visit.Wounds.Any(wound => wound.RequiresBloodTest))
                required = required.Concat(new[] { Syringe, Analyzer });
            var owned = store.FindAllItem(true).Where(IsRemovable).Distinct().ToArray();
            var missing = required.GroupBy(id => id).SelectMany(group =>
                Enumerable.Repeat(group.Key, Math.Max(0, group.Count() - owned
                    .Where(item => item.identifier == group.Key &&
                        (group.Key != supplies[0] || matches == null || matches(item)))
                    .Sum(item => item.unitCount)))).ToArray();
            var failure = missing.Length == 0 ? null : MedicalPack.Deliver(EmporiumEntry.Instance?.invElement, missing,
                () => true, item => { if (item.identifier == supplies[0]) prepare?.Invoke(item); });
            if (failure != null)
            {
                ShowTreatmentDebugMessage(failure);
                return;
            }

            CustomUIManager.Instance?.CloseWindow(DebugMessageWindow);
            state.Current = visit;
            boundBuyer = null;
            status = "";
            OverlayHandler.current?.UpdateInventoryStateUI();
            Save();
            Show();
        }

        private static void ShowTreatmentDebugMessage(string message)
        {
            if (CustomUIManager.Instance == null) return;
            CustomUIManager.Window(DebugMessageWindow, T("clinic_treatment_practice"))
                .SetSize(450, 150).AddLabel(message)
                .AddCloseButton(() => CustomUIManager.Instance?.CloseWindow(DebugMessageWindow)).Show();
        }
    }
}
