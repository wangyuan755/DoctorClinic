using System;
using System.Linq;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string WoundDebug = "伤口";
        private const string BloodDebug = "血检与疾病";
        private const string DependenceDebug = "成瘾与过量";
        private const string CareDebug = "维护与护理";
        private const string AnesthesiaDebug = "麻醉";
        private const string MixedDebug = "复合与事故";
        private const string DebugMessageWindow = "doctor_clinic_treatment_debug_message";
        private const string DebugBandage = "bandage_item";
        private const string DebugAlcohol = "rubbing_alcohol";
        private const string DebugTopical = "topical_bandage_item";
        private const string DebugHemostatic = "hemostatic_bandage_item";
        private const string DebugOximor = "pink_injector";
        private const string DebugNomore = "pink_alt_injector";
        private const string DebugSynaprest = "black_injector";
        private static readonly string[] ClinicDebugTabs =
            { "诊疗", WoundDebug, BloodDebug, DependenceDebug, CareDebug, AnesthesiaDebug, MixedDebug };

        private void RegisterTreatmentDebugButtons()
        {
            AddTreatmentDebug(WoundDebug, "擦伤", ConditionKind.Abrasion, new[] { DebugAlcohol, DebugBandage });
            AddTreatmentDebug(WoundDebug, "浅割伤", ConditionKind.ShallowCut, new[] { DebugAlcohol, DebugBandage, DebugHemostatic, DebugTopical });
            AddTreatmentDebug(WoundDebug, "挫伤／淤青", ConditionKind.Contusion, new[] { MedicalPack.ColdPack });
            AddTreatmentDebug(WoundDebug, "烧伤", ConditionKind.Burn, new[] { MedicalPack.ColdPack, DebugTopical });
            AddTreatmentDebug(WoundDebug, "四肢骨折", ConditionKind.Fracture, new[] { MedicalPack.Splint, DebugBandage });
            AddTreatmentDebug(WoundDebug, "深割伤", ConditionKind.DeepCut, new[] { DebugAlcohol, MedicalPack.Suture, DebugBandage });
            AddTreatmentDebug(WoundDebug, "中弹伤", ConditionKind.Gunshot, new[] { Forceps, DebugAlcohol, MedicalPack.Suture, DebugBandage });

            AddTreatmentDebug(BloodDebug, "失血／输血", ConditionKind.DeepCut,
                new[] { DebugAlcohol, MedicalPack.Suture, DebugBandage, "blood_bag" },
                new[] { ConditionKind.BloodLoss }, visit => visit.Wounds[1].LinkedWound = 0);
            AddTreatmentDebug(BloodDebug, "灰水热（正规噬菌霉）", ConditionKind.GraywaterFever, new[] { "phagimycin_pill" });
            AddTreatmentDebug(BloodDebug, "灰水热（无证噬菌霉）", ConditionKind.GraywaterFever, new[] { "unlicensed_phagimycin_pill" });
            AddTreatmentDebug(BloodDebug, "伤口感染", ConditionKind.ShallowCut, new[] { DebugAlcohol, DebugBandage, "phagimycin_pill" },
                new[] { ConditionKind.WoundInfection }, visit => visit.Wounds[1].LinkedWound = 0);
            AddTreatmentDebug(BloodDebug, "管道孢子病", ConditionKind.PipeSporeDisease, new[] { Antifungal });
            AddTreatmentDebug(BloodDebug, "工业毒素暴露", ConditionKind.IndustrialToxinExposure, new[] { ToxinNeutralizer });

            AddTreatmentDebug(DependenceDebug, "奥克西莫成瘾（暂时缓解）", ConditionKind.OximorDependence,
                new[] { DebugOximor, DebugNomore }, tier: PatientTier.Lower);
            AddTreatmentDebug(DependenceDebug, "奥克西莫成瘾（提议根除）", ConditionKind.OximorDependence,
                new[] { DebugSynaprest }, tier: PatientTier.Lower);
            AddTreatmentDebug(DependenceDebug, "奥克西莫成瘾（上层根除）", ConditionKind.OximorDependence,
                new[] { DebugSynaprest, DebugOximor }, setup: visit => visit.AddictionRequiresCure = true);
            AddTreatmentDebug(DependenceDebug, "奥克西莫过量（纳洛通）", ConditionKind.OximorOverdose, new[] { Naloxone, DebugOximor });

            AddTreatmentDebug(CareDebug, "哨兵综合征（真品免疫宁）", ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item) == (160, null), prepare: InsInjectorHelper.SetGenuine);
            AddTreatmentDebug(CareDebug, "哨兵综合征（过期免疫宁）", ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item).Price == 80, prepare: InsInjectorHelper.SetExpired);
            AddTreatmentDebug(CareDebug, "哨兵综合征（赝品免疫宁）", ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item).Price == 40, prepare: InsInjectorHelper.SetCounterfeit);
            AddTreatmentDebug(CareDebug, "哨兵综合征（空针拦截）", ConditionKind.SentinelCollapse, new[] { Immunivax },
                matches: item => ImmunivaxQuality(item).Price == 0, prepare: InsInjectorHelper.SetUnusable);
            AddTreatmentDebug(CareDebug, "皮肤护理（永恒焕活精华液）", ConditionKind.SkinCare, new[] { "skincare_cream" });
            AddTreatmentDebug(CareDebug, "血管护理（蓝血）", ConditionKind.VascularCare, new[] { "blue_blood_bag" });

            AddAnesthesiaDebug("奥克莫吸（接受麻醉）", DebugNomore, false, matches: HasUsableNomore);
            AddAnesthesiaDebug("奥克莫吸（纯度不足）", DebugNomore, true,
                matches: item => !HasUsableNomore(item), prepare: item => MedsItemDirectory.SetNomoreInjectorPurity(item, 30, 30));
            AddAnesthesiaDebug("奥克西莫（接受麻醉）", DebugOximor, true);
            AddAnesthesiaDebug("奥克西莫（拒绝麻醉）", DebugOximor, false);
            AddAnesthesiaDebug("梦尘（接受麻醉）", "dream_dust", true);
            AddAnesthesiaDebug("梦尘（拒绝麻醉）", "dream_dust", false);

            AddTreatmentDebug(MixedDebug, "复合伤（中弹、骨折与失血）", ConditionKind.Gunshot,
                new[] { Forceps, DebugAlcohol, MedicalPack.Suture, MedicalPack.Splint, DebugBandage, DebugBandage, "blood_bag" },
                new[] { ConditionKind.Fracture, ConditionKind.BloodLoss }, visit =>
                {
                    visit.Wounds[2].LinkedWound = 0;
                });
            AddTreatmentDebug(MixedDebug, "隐疾（擦伤与隐藏灰水热）", ConditionKind.Abrasion,
                new[] { DebugBandage, "phagimycin_pill" }, new[] { ConditionKind.GraywaterFever },
                visit => visit.Wounds[1].Hidden = true);
            AddTreatmentDebug(MixedDebug, "护理与隐疾（蓝血与隐藏灰水热）", ConditionKind.VascularCare,
                new[] { "blue_blood_bag", "phagimycin_pill" }, new[] { ConditionKind.GraywaterFever },
                visit => visit.Wounds[1].Hidden = true);
            AddTreatmentDebug(MixedDebug, "耐心（困难模式，患者制止缝合）", ConditionKind.DeepCut,
                new[] { DebugAlcohol, MedicalPack.Suture, DebugBandage });
            AddTreatmentDebug(MixedDebug, "耐心（困难模式，失血患者制止缝合）", ConditionKind.DeepCut,
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
            ModHelper.AddDebugFunction(ModId, tab, name, () =>
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
            if (!IsLoaded || ModHelper.IsGameOver())
            {
                ShowTreatmentDebugMessage("请先进入游戏并载入可继续营业的存档。");
                return;
            }
            if (state.Current != null && !state.Current.Settled)
            {
                Show();
                ShowTreatmentDebugMessage("请先在当前病例点击「结束并结算」，再开启新的演练。");
                return;
            }

            visit.Practice = true;
            visit.PatientName = name + " · 演练患者";
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
            CustomUIManager.Window(DebugMessageWindow, "诊疗演练")
                .SetSize(450, 150).AddLabel(message)
                .AddCloseButton(() => CustomUIManager.Instance?.CloseWindow(DebugMessageWindow)).Show();
        }
    }
}
