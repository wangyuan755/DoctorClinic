using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace DoctorClinic
{
    public sealed partial class ModEntry : IMod
    {
        internal const string ModId = "doctor_clinic";
        private const string DebugWindow = "doctor_clinic_debug";
        private const string BodyWindowId = "doctor_clinic_body";
        private const string StateKey = "prototype_v1";
        internal static ModEntry Active { get; private set; }
        private ClinicState state;
        private ModLog log;
        private PlayerStore store;
        private ConfigEntry<Difficulty> setting;
        private Difficulty difficulty;
        private bool TreatmentOpen => CustomUIManager.Instance?.IsOpen(BodyWindowId) == true;
        private Sprite bodySprite;
        private BodyInjuryMasks bodyMasks;
        private Sprite bodyMaskSource;
        private LoadedMod clinicAssets;
        private readonly System.Collections.Generic.Dictionary<string, Sprite> art =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        private DragPreview dragPreview;
        private int testWoundCount = 2;
        private int testBudgetOffset;
        private bool bindCounterBuyer;
        private bool showTestingControls;
        private StoreClient boundBuyer;
        private string status = "";

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            setting = ModHelper.GetConfig(ModId).DefineEnum(
                "difficulty", Difficulty.简单, "诊所", () => "难度",
                () => "在主菜单配置，读取存档时生效。", requiresRestart: true);
        }

        public void OnEnable()
        {
            Active = this;
            ModHook.OnGameLoadedLate += Load;
            ModHook.OnNewGameStarted += LoadNewGame;
            ModHook.OnModItemDirectoryInit += RegisterClinicItems;
            ModHook.OnModAssetsLoaded += ReceiveAssets;
            ModHook.OnGenerateCustomerVeryLate += QueueClinicVisitors;
            ModHook.OnWakingUpLate += ResolvePoisonings;
            ModHook.OnWakingUpLate += QueueTribunalIfNeeded;
            ModHook.OnShutterOpenedLate += DeliverCommissions;
            ModHook.OnShutterClosedLate += StopCommissionDelivery;
            ModHelper.AddEnding(ModId, TribunalEnding, () => "公民审判",
                () => "愤怒的居民涌到柜台，砸碎诊所的招牌。街区已经不再相信你能照顾他们。");
            ModHelper.AddDebugPanel(ModId, "clinic", "医生诊所原型", ClinicDebugTabs, false);
            RegisterTreatmentDebugButtons();
            ModHelper.AddDebugFunction(ModId, "诊疗", "打开诊所原型", OpenFromDebugMenu);
            ModHelper.AddDebugFunction(ModId, "诊疗", "演练 F1 和 T1 病例", () =>
                StartBloodPractice(ConditionKind.PipeSporeDisease, ConditionKind.IndustrialToxinExposure, Antifungal, ToxinNeutralizer));
            ModHelper.AddDebugFunction(ModId, "诊疗", "演练 O1 和 S1 病例", () =>
                StartBloodPractice(ConditionKind.OximorOverdose, ConditionKind.SentinelCollapse, Naloxone, Immunivax));
            ModHelper.AddDebugFunction(ModId, "诊疗", "街区医师协会", () => ShowAssociation());
            ModHelper.AddDebugFunction(ModId, "诊疗", "验证事故处罚过滤", ClinicPenaltyPreview.Show);
            ModHelper.AddDebugFunction(ModId, "诊疗", "医生声望设为 +100", () => SetDebugReputation(100));
            ModHelper.AddDebugFunction(ModId, "诊疗", "医生声望设为 −100", () => SetDebugReputation(-100));
        }

        public void OnDisable()
        {
            Active = null;
            clinicReplies.Clear();
            ModHook.OnGameLoadedLate -= Load;
            ModHook.OnNewGameStarted -= LoadNewGame;
            ModHook.OnModItemDirectoryInit -= RegisterClinicItems;
            ModHook.OnModAssetsLoaded -= ReceiveAssets;
            ModHook.OnGenerateCustomerVeryLate -= QueueClinicVisitors;
            ModHook.OnWakingUpLate -= ResolvePoisonings;
            ModHook.OnWakingUpLate -= QueueTribunalIfNeeded;
            ModHook.OnShutterOpenedLate -= DeliverCommissions;
            ModHook.OnShutterClosedLate -= StopCommissionDelivery;
            RemoveClinicPhones();
            ModHelper.RemoveEndings(ModId);
            if (wildePerksRegistered)
            {
                ModHelper.RemoveNetworkUpgrades(ModId);
                wildePerksRegistered = false;
            }
            ModHelper.RemoveDebugFunctions(ModId);
            RemoveDoctorReputation();
            Close();
            CustomUIManager.Instance?.CloseWindow(AssociationWindow);
            CloseClinicPanels();
            ClinicPenaltyPreview.Close();
            if (bodySprite != null)
            {
                UnityEngine.Object.Destroy(bodySprite.texture);
                UnityEngine.Object.Destroy(bodySprite);
                bodySprite = null;
            }
            bodyMasks?.Destroy();
            bodyMasks = null;
            bodyMaskSource = null;
            foreach (var sprite in art.Values)
                if (sprite != null) UnityEngine.Object.Destroy(sprite);
            art.Clear();
            clinicAssets = null;
            store = null;
            state = null;
            boundBuyer = null;
        }

        private void LoadNewGame(string scenario) => Load();

        private void Load()
        {
            clinicReplies.Clear();
            Close();
            CustomUIManager.Instance?.CloseWindow(AssociationWindow);
            CloseClinicPanels();
            store = PlayerStore.Instance;
            boundBuyer = null;
            bindCounterBuyer = false;
            showTestingControls = false;
            clinicOpenedDay = -1;
            difficulty = setting.Value;
            var json = ModHelper.GetModData(ModId, StateKey, "");
            state = string.IsNullOrEmpty(json)
                ? new ClinicState()
                : JsonConvert.DeserializeObject<ClinicState>(json) ?? new ClinicState();
            state.Current?.UpdateUnsettledPricing();
            if (state.Schedule != null)
                foreach (var visit in state.Schedule.Patients) visit.UpdateUnsettledPricing();
            RefreshDoctorReputation();
            status = "";
            RefreshSavedClinicItems();
            SyncWildePerks();
            SyncClinicPhones();
            ResolvePoisonings();
            QueueTribunalIfNeeded();
            if (IsLoaded && !state.EndingPending && !state.EndingTriggered) RestoreDreamReturns(store.storeClientManager);
            log.Log("诊所状态已初始化。");
        }

        private void ReceiveAssets(LoadedMod mod)
        {
            if (mod.Manifest.ID == ModId) clinicAssets = mod;
        }

        private Sprite Art(string key)
        {
            if (art.TryGetValue(key, out var sprite)) return sprite;
            if (clinicAssets == null || !clinicAssets.Textures.TryGetValue(key, out var texture)) return null;
            texture.filterMode = FilterMode.Point;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            art.Add(key, sprite);
            return sprite;
        }

        private void AddAbrasionArt(CustomUIBuilder ui, Abrasion wound)
        {
            var skin = Art("skin_base");
            var key = wound.Kind == ConditionKind.Contusion
                ? (wound.Covered ? "bruise_cooled" : "bruise_raw")
                : wound.Covered ? "dressing" : wound.Kind switch
            {
                ConditionKind.Gunshot => wound.ForeignBodyRemoved ? "bullet_removed" : "bullet_raw",
                ConditionKind.DeepCut => wound.Closed ? "deep_cut_sutured" :
                    wound.Cleaned || wound.BleedingControlled ? "deep_cut_hemostasis" : "deep_cut_bleeding",
                ConditionKind.ShallowCut => "laceration_raw",
                ConditionKind.Burn => wound.Cooled ? "burn_cooled" : "burn_raw",
                _ => wound.Cleaned ? "abrasion_clean" : "abrasion_raw"
            };
            var overlay = Art(key);
            if (skin == null || overlay == null) return;
            ui.AddImage(skin, 100, 100);
            var baseImage = ui.Last.Rect;
            ui.AddImage(overlay, 100, 100);
            var layer = ui.Last.Rect;
            layer.SetParent(baseImage, false);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.offsetMin = Vector2.zero;
            layer.offsetMax = Vector2.zero;
        }

        private bool IsLoaded => state != null && store != null && store == PlayerStore.Instance;
        private void Save()
        {
            ModHelper.SetModData(ModId, StateKey, JsonConvert.SerializeObject(state));
            RefreshDoctorReputation();
        }

        private void Close()
        {
            CustomUIManager.Instance?.CloseWindow(DebugWindow);
            CloseSettlement();
            CloseBody();
        }

        private void CloseBody()
        {
            dragPreview = null;
            var manager = CustomUIManager.Instance;
            manager?.CloseWindow(BodyWindowId);
            CloseBloodReport();
            manager?.CloseWindow(WoundDetailsWindow);
        }

        private void OpenFromDebugMenu()
        {
            if (!IsLoaded)
            {
                const string message = "诊所尚未初始化。请进入新游戏或载入存档后再打开。";
                log.Warning(message);
                if (CustomUIManager.Instance != null)
                {
                    CustomUIManager.Window(DebugWindow, "诊所调试")
                        .AddLabel(message).AddCloseButton(Close).Show();
                }
                return;
            }

            showTestingControls = true;
            Show();
            log.Log("诊所窗口已打开。");
        }

        private void Show(bool receipt = false)
        {
            if (!IsLoaded) return;
            var visit = state.Current;
            if (visit != null && !visit.Settled)
            {
                CustomUIManager.Instance?.CloseWindow(DebugWindow);
                ShowSettlement(visit, visit.Preview());
            }
            else if (receipt && visit?.Settlement != null)
            {
                CustomUIManager.Instance?.CloseWindow(DebugWindow);
                ShowSettlement(visit, visit.Settlement);
            }
            else if (showTestingControls) ShowDebugControls();
        }

        private void SetDebugReputation(int reputation)
        {
            if (!IsLoaded || state.EndingTriggered || ModHelper.IsGameOver()) return;
            state.ReputationHalfPoints = reputation * 2;
            state.LastReputationReason = "调试菜单调整";
            Save();
            log.Log($"医生声望已设为 {reputation:+0;-0;0}。");
            QueueTribunalIfNeeded();
        }

        private void ShowDebugControls()
        {
            var ui = CustomUIManager.Window(DebugWindow, "诊所调试").SetSize(590, 480).SetDraggable(true);
            ui.AddLabel($"医生 {state.ReputationHalfPoints / 2m:0.0}   {ClinicReputation.Title(state.ReputationHalfPoints)}   难度 {difficulty}");
            if (state.Current?.Settlement != null)
                ui.AddButton("查看上次结算", () => Show(receipt: true));
            if (CounterBuyer() != null)
                ui.AddToggle("绑定当前柜台买家，使用原版折扣和呼叫铃", bindCounterBuyer,
                    value => bindCounterBuyer = value);
            else
            {
                bindCounterBuyer = false;
                ui.AddLabel("柜台有可议价买家时可绑定，测试原版折扣和呼叫铃。");
            }
            ui.AddDropdown(new[] { "1 处擦伤", "2 处擦伤", "3 处擦伤" },
                testWoundCount - 1, index => testWoundCount = index + 1);
            ui.AddDropdown(new[] { "预算波动：随机", "预算波动：−10", "预算波动：0", "预算波动：+10" },
                testBudgetOffset, index => testBudgetOffset = index);
            ui.AddButton("下层／黑市", () => StartVisit(PatientTier.Lower));
            ui.AddButton("治安／革命军", () => StartVisit(PatientTier.Security));
            ui.AddButton("上层", () => StartVisit(PatientTier.Upper));
            ui.AddButton("演练骨折、中弹和失血", StartP2Practice);
            ui.AddCloseButton(() => CustomUIManager.Instance?.CloseWindow(DebugWindow));
            ui.Show();
        }

        private void ShowBody(ClinicCase visit)
        {
            var manager = CustomUIManager.Instance;
            var position = manager.GetWindow(BodyWindowId)?.Rect.anchoredPosition ?? new Vector2(-308, 0);
            manager.CloseWindow(BodyWindowId);
            var ui = CustomUIManager.Window(BodyWindowId, string.IsNullOrEmpty(status) ? "人体图" : status)
                .SetSize(330, 618).SetPosition(position).SetDraggable(true);
            var samplingTarget = BuildPatientBody(ui, visit);
            ui.AddCloseButton(CloseBody);
            dragPreview = ui.Show().Rect.gameObject.AddComponent<DragPreview>();
            dragPreview.SamplingTarget = samplingTarget;
        }

        private void RefreshVisitPanels(ClinicCase visit)
        {
            RefreshOpenSettlement(visit);
            if (TreatmentOpen) ShowBody(visit);
        }

        private WoundDrop BuildPatientBody(CustomUIBuilder ui, ClinicCase visit)
        {
            var providedBody = Art("body_front");
            if (providedBody != null && !providedBody.texture.isReadable) providedBody = null;
            if (providedBody == null && bodySprite == null) bodySprite = BodyChart.CreateSprite();
            var displayBody = providedBody ?? bodySprite;
            if (bodyMasks == null || bodyMaskSource != displayBody)
            {
                bodyMasks?.Destroy();
                bodyMasks = new BodyInjuryMasks(displayBody, providedBody != null);
                bodyMaskSource = displayBody;
            }

            ui.BeginPanel(312, 538, TextAnchor.MiddleCenter, 0, 0, "clinic_treatment");
            ui.AddImage(displayBody, providedBody == null ? 296 : 240, providedBody == null ? 370 : 480, "body_chart");
            var chart = ui.Last.Rect;
            var bodyHit = chart.gameObject.AddComponent<BodyRegionTarget>();
            bodyHit.Masks = bodyMasks;
            bodyHit.Region = -1;
            bodyHit.InspectBlood = () => ReopenBloodReport(visit);
            var samplingTarget = chart.gameObject.AddComponent<WoundDrop>();
            samplingTarget.Apply = item => TargetRegion(visit, -1, item, body: true);
            var bodyImage = chart.GetComponent<UnityEngine.UI.Image>();
            if (bodyImage != null) bodyImage.raycastTarget = true;

            for (var i = 0; i < BodyChart.Names.Length; i++)
            {
                var region = i;
                var index = FindWoundAt(visit, region);
                if (index < 0 || visit.Wounds[index].Covered) continue;
                bodyMasks.AddTarget(chart, region, () => ShowWoundDetails(visit, index),
                    item => TargetRegion(visit, region, item, body: true), bodyHit.InspectBlood);
            }
            ui.End();
            return samplingTarget;
        }

        internal DragPreview CurrentDragPreview =>
            TreatmentOpen && IsLoaded && state.Current != null && !state.Current.Settled
                ? dragPreview : null;

        private static string Progress(Abrasion wound)
        {
            if (wound.Kind == ConditionKind.Fracture) return wound.Fixed ? "已固定，待绷带支持" : "可见骨折，待固定";
            if (wound.Kind == ConditionKind.Contusion) return "待冷敷";
            if (wound.Kind == ConditionKind.Burn) return wound.Cooled ? "已冷敷，待敷药覆盖" : "待冷敷";
            if (wound.ComplexWound)
            {
                if (wound.Kind == ConditionKind.Gunshot && !wound.ForeignBodyRemoved) return "可见子弹，待用镊子取出";
                if (!wound.Cleaned) return "待清洁";
                if (!wound.Closed) return "待缝合";
                return "已缝合，待覆盖";
            }
            if (wound.Kind == ConditionKind.ShallowCut && !wound.Cleaned) return "待用酒精清洁";
            return wound.Cleaned ? "已清洁，待包扎" : "待处理";
        }

        private static int FindWoundAt(ClinicCase visit, int region)
        {
            for (var i = 0; i < visit.Wounds.Length; i++)
                if (!visit.Wounds[i].RequiresBloodTest && !visit.Wounds[i].IsCare && (visit.Wounds[i].Region ?? i) == region) return i;
            return -1;
        }

        private void TargetRegion(ClinicCase visit, int region, GameItem item, bool body = false)
        {
            if (!IsCurrent(visit) || !TreatmentOpen || visit.Dead) return;
            if (body && IsSamplingSyringe(item))
            {
                DrawBlood(visit, item);
                return;
            }
            var treatment = Identify(item);
            if (body && CanUse(item) && RejectInappropriateAnesthesia(visit, treatment))
            {
                if (!ReactToTreatmentMistake(visit, stopped: true)) RefreshVisitPanels(visit);
                return;
            }
            if (body && treatment == Treatment.Synaprest)
            {
                var cureIndex = visit.FindSystemicTreatment(Treatment.Synaprest);
                if (cureIndex >= 0 && !visit.Wounds[cureIndex].Hidden && !visit.RequiresAddictionCure(visit.Wounds[cureIndex]))
                {
                    visit.Wounds[cureIndex].Diagnosed = true;
                    ShowWoundDetails(visit, cureIndex);
                    return;
                }
            }
            if (body && (treatment == Treatment.DuskReagent || treatment == Treatment.Zyanide))
            {
                ApplyClinicPoison(visit, item, treatment.Value);
                return;
            }
            var treatingDependence = treatment == Treatment.Oximor && visit.HasOximorDependence;
            if (body && treatment.HasValue && IsAnesthesia(treatment.Value) && !treatingDependence)
            {
                if (CanUse(item)) ApplyAnesthesia(visit, item, treatment.Value);
                else status = "请使用仍在玩家库存内的药品。";
                if (!IsCurrent(visit)) return;
                Save();
                RefreshVisitPanels(visit);
                return;
            }
            var index = body ? FindWoundAt(visit, region) : region;
            if (body && treatment.HasValue && (IsSystemicMedicine(treatment.Value) || treatingDependence))
            {
                index = visit.FindSystemicTreatment(treatment.Value);
                if (index < 0)
                {
                    status = "没有适合用这件药品处理的伤病。";
                    if (CanUse(item) && ReactToTreatmentMistake(visit, stopped: false)) return;
                    RefreshVisitPanels(visit);
                    return;
                }
                if (visit.Wounds[index].Hidden && visit.HiddenBudget == 0)
                {
                    visit.Wounds[index].Diagnosed = true;
                    ShowWoundDetails(visit, index);
                    return;
                }
            }
            if (item != null && index >= 0 && index < visit.Wounds.Length)
            {
                Apply(visit, index, item);
                return;
            }
            status = "这个部位没有可治疗的伤处。";
            if (treatment.HasValue && CanUse(item) && ReactToTreatmentMistake(visit, IsInvasiveTreatment(treatment.Value))) return;
            RefreshVisitPanels(visit);
        }

        private void ProposeAddictionCure(ClinicCase visit, int index)
        {
            if (!IsCurrent(visit) || !TreatmentOpen || index < 0 || index >= visit.Wounds.Length) return;
            var wound = visit.Wounds[index];
            if (wound.Covered || wound.Kind != ConditionKind.OximorDependence || visit.RequiresAddictionCure(wound)) return;
            QueueClinicReply(visit, "真的吗。。。我恐怕负担不起");
            wound.AddictionCureAccepted = true;
            status = "患者同意根除治疗。";
            Save();
            CustomUIManager.Instance?.CloseWindow(WoundDetailsWindow);
            RefreshVisitPanels(visit);
        }

        private void StartBloodPractice(ConditionKind first, ConditionKind second, string firstMedicine, string secondMedicine)
        {
            if (!IsLoaded || (state.Current != null && !state.Current.Settled)) return;
            var visit = ClinicCase.Create(PatientTier.Upper, 2, 0);
            visit.Practice = true;
            visit.PatientName = "血检演练患者";
            visit.Wounds[0].Kind = first;
            visit.Wounds[1].Kind = second;
            var supplies = new[] { firstMedicine, secondMedicine, Syringe, Analyzer };
            var owned = store.FindAllItem(true);
            var missing = supplies.Where(id => !owned.Any(item => item?.identifier == id && IsRemovable(item))).ToArray();
            var failure = MedicalPack.Deliver(EmporiumEntry.Instance?.invElement, missing, () =>
            {
                state.Current = visit;
                return true;
            });
            if (failure != null)
            {
                log.Warning($"无法开始血检演练：{failure}");
                return;
            }
            OverlayHandler.current?.UpdateInventoryStateUI();
            boundBuyer = null;
            status = "";
            Save();
            Show();
        }

        private void StartP2Practice()
        {
            if (!IsLoaded || (state.Current != null && !state.Current.Settled)) return;
            var visit = ClinicCase.Create(PatientTier.Upper, 3, 0);
            visit.Practice = true;
            visit.Wounds[0].Kind = ConditionKind.Gunshot;
            visit.Wounds[0].Region = 3;
            visit.Wounds[1].Kind = ConditionKind.Fracture;
            visit.Wounds[1].Region = 4;
            visit.Wounds[2].Kind = ConditionKind.BloodLoss;
            visit.Wounds[2].LinkedWound = 0;
            state.Current = visit;
            BindPracticeBuyer(visit);
            status = "";
            Save();
            Show();
        }

        private void StartVisit(PatientTier tier)
        {
            if (!IsLoaded || (state.Current != null && !state.Current.Settled)) return;
            var offset = testBudgetOffset == 0 ? UnityEngine.Random.Range(-10, 11) : (testBudgetOffset - 2) * 10;
            state.Current = ClinicCase.Create(tier, testWoundCount, offset);
            state.Current.Practice = state.License != LicenseStatus.None;
            BindPracticeBuyer(state.Current);
            status = "";
            Save();
            Show();
        }

        private void BindPracticeBuyer(ClinicCase visit)
        {
            boundBuyer = bindCounterBuyer ? CounterBuyer() : null;
            if (boundBuyer != null && BargainUIManager.Instance != null)
                visit.NativeDiscountPercent = BargainUIManager.Instance.DiscountPercent;
        }

        internal static bool IsSamplingSyringe(GameItem item) => item?.identifier == Syringe;

        internal static bool IsClinicBodyItem(GameItem item) => IsSamplingSyringe(item) || Identify(item).HasValue;

        private const string Antifungal = "doctor_clinic_antifungal";
        private const string ToxinNeutralizer = "doctor_clinic_toxin_neutralizer";
        private const string Immunivax = "large_purple_injector";
        private const string Naloxone = "pure_white_injector";

        private static bool IsSystemicMedicine(Treatment treatment) =>
            treatment == Treatment.Phagimycin || treatment == Treatment.UnlicensedPhagimycin ||
            treatment == Treatment.BloodBag || treatment == Treatment.Antifungal || treatment == Treatment.ToxinNeutralizer ||
            treatment == Treatment.Immunivax || treatment == Treatment.Naloxone || treatment == Treatment.SkinCare ||
            treatment == Treatment.VascularCare || treatment == Treatment.Synaprest;

        internal static Treatment? Identify(GameItem item)
        {
            if (item?.identifier == Reagent) return Treatment.DuskReagent;
            if (item?.identifier == "zyanide_pill") return Treatment.Zyanide;
            if (item?.identifier == "rubbing_alcohol") return Treatment.Alcohol;
            if (item?.identifier == "bandage_item") return Treatment.Bandage;
            if (item?.identifier == "topical_bandage_item") return Treatment.TopicalBandage;
            if (item?.identifier == MedicalPack.ColdPack) return Treatment.ColdPack;
            if (item?.identifier == "hemostatic_bandage_item") return Treatment.HemostaticBandage;
            if (item?.identifier == Antifungal) return Treatment.Antifungal;
            if (item?.identifier == ToxinNeutralizer) return Treatment.ToxinNeutralizer;
            if (item?.identifier == Immunivax) return Treatment.Immunivax;
            if (item?.identifier == Naloxone) return Treatment.Naloxone;
            if (item?.identifier == "skincare_cream") return Treatment.SkinCare;
            if (item?.identifier == "blue_blood_bag") return Treatment.VascularCare;
            if (item?.identifier == "black_injector") return Treatment.Synaprest;
            if (item?.identifier == "phagimycin_pill") return Treatment.Phagimycin;
            if (item?.identifier == "unlicensed_phagimycin_pill") return Treatment.UnlicensedPhagimycin;
            if (item?.identifier == MedicalPack.Splint) return Treatment.Splint;
            if (item?.identifier == MedicalPack.Suture) return Treatment.Suture;
            if (item?.identifier == Forceps || item?.identifier == "surgery_tool") return Treatment.Forceps;
            if (item?.identifier == Scalpel) return Treatment.Scalpel;
            if (item?.identifier == "blood_bag") return Treatment.BloodBag;
            if (item?.identifier == "dream_dust") return Treatment.DreamDust;
            if (item?.identifier == "pink_injector") return Treatment.Oximor;
            if (item?.identifier == "pink_alt_injector") return Treatment.Nomore;
            return null;
        }

        private static bool IsRemovable(GameItem item) => item != null && item.parentInventory != null &&
            item.unitCount > 0 && item.MaxNumRemove() > 0 && item.MayRemove() &&
            GeneralHelper.IsItemEffectivelyOwned(item);

        private bool CanUse(GameItem item) => IsRemovable(item) && store.FindAllItem(true).Contains(item);

        private bool IsCurrent(ClinicCase visit) => IsLoaded && state.Current == visit && !visit.Settled;

        private static bool IsInvasiveTreatment(Treatment treatment) =>
            treatment == Treatment.Forceps || treatment == Treatment.Scalpel || treatment == Treatment.Suture;

        private void Apply(ClinicCase visit, int index, GameItem item)
        {
            if (!IsCurrent(visit)) return;
            var settlementWasOpen = SettlementOpen;
            var wound = visit.Wounds[index];
            var wasCovered = wound.Covered;
            var treatment = Identify(item);
            var immunivax = treatment == Treatment.Immunivax ? ImmunivaxQuality(item) : (Price: 0, ReducedQuality: (string)null);
            var unusableImmunivax = treatment == Treatment.Immunivax && immunivax.Price == 0;
            var valid = treatment.HasValue && visit.CanTreat(index, treatment.Value) && !unusableImmunivax;
            var materialCost = treatment == Treatment.Immunivax ? immunivax.Price : (int?)null;
            var standardQuality = immunivax.ReducedQuality == null;
            if (!treatment.HasValue || !CanUse(item))
                status = "请选择仍在玩家库存内且可取出的耗材。";
            else if (RejectInappropriateAnesthesia(visit, treatment))
            {
                if (!ReactToTreatmentMistake(visit, stopped: true)) RefreshVisitPanels(visit);
                return;
            }
            else if (treatment == Treatment.DuskReagent || treatment == Treatment.Zyanide) status = "请拖到人体图。";
            else if (unusableImmunivax)
            {
                status = "这支免疫宁已空或不可用。";
                if (wound.Kind == ConditionKind.SentinelCollapse)
                {
                    QueueClinicReply(visit, "你在耍我吗，里面什么都没有！", difficulty == Difficulty.困难 && visit.Patience < 2);
                    if (ReactToTreatmentMistake(visit, stopped: true)) return;
                }
            }
            else if (treatment == Treatment.Oximor && wound.Kind == ConditionKind.OximorDependence && visit.RequiresAddictionCure(wound))
            {
                status = "患者已选择根除治疗。";
                QueueClinicReply(visit, "我想彻底戒掉。给我赛纳普斯吧。");
                if (ReactToTreatmentMistake(visit, stopped: true)) return;
            }
            else if (IsAnesthesia(treatment.Value) && !(treatment == Treatment.Oximor && wound.Kind == ConditionKind.OximorDependence))
                status = "把麻醉品拖到人体图上。";
            else if (wound.Hidden && visit.HiddenBudget == 0) status = "请先就隐疾取得同意。";
            else if (difficulty == Difficulty.简单 && !valid)
                status = wound.LinkedWound.HasValue && !visit.Wounds[wound.LinkedWound.Value].Covered ? "请先处理关联伤口。" :
                    wound.Kind == ConditionKind.OximorDependence ? "请使用患者所需的成瘾治疗药物。" :
                    "简单模式已拦截无效操作。";
            else if (!valid && IsInvasiveTreatment(treatment.Value))
            {
                status = "患者制止了这项操作。";
                if (ReactToTreatmentMistake(visit, stopped: true)) return;
            }
            else
            {
                visit.TreatmentPerformed = true;
                var tool = treatment.Value == Treatment.Forceps || treatment.Value == Treatment.Scalpel;
                var doseValue = InvestigationDoseValue(item, treatment.Value);
                if (treatment == Treatment.Antifungal) UseAntifungalDose(item);
                else if (!tool) ConsumeOne(item);
                RecordMedicationInvestigation(visit, doseValue);
                wound.Apply(treatment.Value, valid, materialCost, standardQuality);
                if (valid && wound.RequiresBloodTest)
                {
                    wound.Diagnosed = true;
                    visit.BloodRevision++;
                }
                if (!wasCovered && wound.Covered)
                {
                    if (difficulty == Difficulty.困难) visit.GainPatience(wound.IsPerfect);
                    ReplyToCompletedTreatment(visit, wound, treatment.Value);
                }
                if (valid) status = immunivax.ReducedQuality != null
                    ? $"患者识别出免疫宁{immunivax.ReducedQuality}，药材按 {immunivax.Price} 信用点计费。"
                    : tool ? "已使用可复用工具。" : "已使用一件耗材。";
                else
                {
                    status = "已消耗一件耗材，治疗无效。";
                    if (ReactToTreatmentMistake(visit, stopped: false)) return;
                }
                Save();
            }
            if (settlementWasOpen && !SettlementOpen) Show();
            RefreshVisitPanels(visit);
            if (woundDetailsIndex == index && CustomUIManager.Instance?.IsOpen(WoundDetailsWindow) == true)
                ShowWoundDetails(visit, index);
        }

        private StoreClient CounterBuyer()
        {
            var buyer = store.currentClientInstance?.GetClientBlueprint();
            return buyer != null && buyer.isBargainAvailable && buyer.IsClientBuying() ? buyer : null;
        }

        internal void OnNativeDiscount(StoreClient buyer, int percent)
        {
            var visit = state?.Current;
            if (buyer == null || buyer != boundBuyer || visit == null || !IsCurrent(visit)) return;
            if (percent <= visit.NativeDiscountPercent) return;
            visit.NativeDiscountPercent = percent;
            Save();
            RefreshOpenSettlement(visit);
        }

        internal void OnCounterDismiss(PlayerStore playerStore)
        {
            var visit = state?.Current;
            if (playerStore != store || visit == null || !IsCurrent(visit) || boundBuyer == null ||
                playerStore.currentClientInstance?.GetClientBlueprint() != boundBuyer) return;
            Finish(visit, true, dismissBuyer: false, showReceipt: SettlementOpen);
        }

        private void Finish(ClinicCase visit, bool referral, bool dismissBuyer = true, bool showReceipt = true, bool playerReferral = false)
        {
            if (!IsCurrent(visit)) return;
            var buyer = boundBuyer;
            var commission = playerReferral && referral ? CommissionFor(visit.Id) : null;
            var paysRefusal = CanRefuseByCommission(visit, commission);
            var incident = GetIncident(visit);
            var doctorLoss = (incident?.DoctorLoss ?? 0) + (visit.PatientWalkedOut ? 8 : 0);
            var amount = visit.ApplySettlement(state, referral || visit.Dead, doctorLoss);
            if (!visit.Settled) return;
            if (visit.PatientWalkedOut && !visit.Practice && !visit.Tutorial)
                state.LastReputationReason = "患者失去耐心离开";
            boundBuyer = null;
            CloseBody();
            if (amount > 0) store.PayMoney(-amount, true);
            CommitIncident(incident);
            if (paysRefusal) CompleteCommission(commission);
            ResolveVisitCommission(visit);
            RecordDreamReturn(visit);
            Save();
            if (dismissBuyer && buyer != null && store.currentClientInstance?.GetClientBlueprint() == buyer)
                store.DismissCurrentClient();
            if (showReceipt) Show(receipt: true);
            QueueTribunalIfNeeded();
        }
    }

    internal sealed class WoundDrop : MonoBehaviour
    {
        public Action<GameItem> Apply { get; set; }
    }
}
