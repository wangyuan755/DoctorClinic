using static DoctorClinic.ClinicText;
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
                "difficulty", Difficulty.简单, "DoctorClinic", () => T("clinic_difficulty"),
                () => T("clinic_configure_from_the_main_menu_takes_effect"), requiresRestart: true);
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
            ClinicCompat.AddEnding(ModId, TribunalEnding, () => T("clinic_citizen_s_tribunal"),
                () => T("clinic_angry_residents_storm_the_counter_and_smash"));
        }

        internal bool IsClinicDifficultySetting(ConfigEntry entry) => ReferenceEquals(setting, entry);

        private static string ClinicDebugCategory => ClinicCompat.DebugCategory(T("clinic_treatment"));
        private static string ClinicDebugLabel(string name) => ClinicCompat.DebugLabel(T("clinic_treatment"), name);

        private void RegisterDebugPanel()
        {
            ClinicCompat.AddDebugPanel(ModId, "clinic", T("clinic_doctorclinic_debug"), ClinicDebugTabs, false);
            RegisterTreatmentDebugButtons();
            ModHelper.AddDebugFunction(ModId, ClinicDebugCategory, ClinicDebugLabel(T("clinic_open_clinic_prototype")), OpenFromDebugMenu);
            ModHelper.AddDebugFunction(ModId, ClinicDebugCategory, ClinicDebugLabel(T("clinic_practice_f1_and_t1_cases")), () =>
                StartBloodPractice(ConditionKind.PipeSporeDisease, ConditionKind.IndustrialToxinExposure, Antifungal, ToxinNeutralizer));
            ModHelper.AddDebugFunction(ModId, ClinicDebugCategory, ClinicDebugLabel(T("clinic_practice_o1_and_s1_cases")), () =>
                StartBloodPractice(ConditionKind.OximorOverdose, ConditionKind.SentinelCollapse, Naloxone, Immunivax));
            ModHelper.AddDebugFunction(ModId, ClinicDebugCategory, ClinicDebugLabel(T("clinic_neighborhood_medical_association")), () => ShowAssociation());
            ModHelper.AddDebugFunction(ModId, ClinicDebugCategory, ClinicDebugLabel(T("clinic_preview_incident_penalties")), ClinicPenaltyPreview.Show);
            ModHelper.AddDebugFunction(ModId, ClinicDebugCategory, ClinicDebugLabel(T("clinic_set_doctor_reputation_to_100")), () => SetDebugReputation(100));
            ModHelper.AddDebugFunction(ModId, ClinicDebugCategory, ClinicDebugLabel(T("clinic_set_doctor_reputation_to_100_2")), () => SetDebugReputation(-100));
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
            ClinicCompat.RemoveEndings(ModId);
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
            if (clinicAssets != null)
                foreach (var entry in art)
                    if (clinicAssets.PaperSprites.TryGetValue(entry.Key, out var paperSprite) && paperSprite == entry.Value)
                        clinicAssets.PaperSprites.Remove(entry.Key);
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
            log.Log(T("clinic_clinic_state_initialized"));
        }

        private void ReceiveAssets(LoadedMod mod)
        {
            if (mod.Manifest.ID != ModId) return;
            clinicAssets = mod;
            RegisterDebugPanel();
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

        private void AddWoundArt(CustomUIBuilder ui, ClinicCase visit, int index)
        {
            var wound = visit.Wounds[index];
            var skin = Art("skin_base");
            if (skin == null) return;
            var key = wound.Kind == ConditionKind.Contusion
                ? (wound.Covered ? "bruise_cooled" : "bruise_raw")
                : wound.Covered ? "dressing" : wound.Kind switch
            {
                ConditionKind.Gunshot => wound.ForeignBodyRemoved ? "bullet_removed" : "bullet_raw",
                ConditionKind.DeepCut => wound.Closed ? "deep_cut_sutured" :
                    wound.Cleaned || wound.BleedingControlled ? "deep_cut_hemostasis" : "deep_cut_bleeding",
                ConditionKind.ShallowCut => "laceration_raw",
                ConditionKind.Burn => wound.Cooled ? "burn_cooled" : "burn_raw",
                ConditionKind.Fracture => wound.Fixed ? "splint" : null,
                _ => wound.Cleaned ? "abrasion_clean" : "abrasion_raw"
            };
            ui.AddImage(skin, 100, 100);
            var baseImage = ui.Last.Rect;
            if (!wound.Covered)
            {
                baseImage.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
                baseImage.gameObject.AddComponent<WoundDrop>().Apply =
                    item => TargetRegion(visit, -1, item, body: true, selectedWound: index);
            }

            var overlay = key == null ? null : Art(key);
            if (overlay == null) return;
            ui.AddImage(overlay, 100, 100);
            var layer = ui.Last.Rect;
            var layerImage = layer.GetComponent<UnityEngine.UI.Image>();
            layerImage.raycastTarget = false;
            // These stages share an existing sprite. Keep the wound shape and distinguish the cleaned state.
            if (!wound.Covered && wound.Cleaned &&
                (wound.Kind == ConditionKind.ShallowCut || wound.Kind == ConditionKind.Gunshot))
                layerImage.color = new Color(1f, 1f, 1f, wound.Closed ? .42f : .68f);
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
                var message = T("clinic_the_clinic_is_not_ready_start_a");
                log.Warning(message);
                if (CustomUIManager.Instance != null)
                {
                    CustomUIManager.Window(DebugWindow, T("clinic_clinic_debug"))
                        .AddLabel(message).AddCloseButton(Close).Show();
                }
                return;
            }

            showTestingControls = true;
            Show();
            log.Log(T("clinic_opened_the_clinic_window"));
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
            if (!IsLoaded || state.EndingTriggered || ClinicCompat.IsGameOver()) return;
            state.ReputationHalfPoints = reputation * 2;
            state.LastReputationReason = "clinic_reason_debug_adjustment";
            Save();
            log.Log(T("clinic_doctor_reputation_set_to", reputation));
            QueueTribunalIfNeeded();
        }

        private void ShowDebugControls()
        {
            var ui = CustomUIManager.Window(DebugWindow, T("clinic_clinic_debug")).SetSize(590, 480).SetDraggable(true);
            ui.AddLabel(T("clinic_doctor_reputation_difficulty", state.ReputationHalfPoints / 2m, ClinicReputation.Title(state.ReputationHalfPoints), DifficultyLabel(difficulty)));
            if (state.Current?.Settlement != null)
                ui.AddButton(T("clinic_view_previous_bill"), () => Show(receipt: true));
            if (CounterBuyer() != null)
                ui.AddToggle(T("clinic_bind_to_current_buyer_to_test_native"), bindCounterBuyer,
                    value => bindCounterBuyer = value);
            else
            {
                bindCounterBuyer = false;
                ui.AddLabel(T("clinic_a_buyer_who_can_bargain_must_be"));
            }
            ui.AddDropdown(new[] { T("clinic_1_abrasion"), T("clinic_2_abrasions"), T("clinic_3_abrasions") },
                testWoundCount - 1, index => testWoundCount = index + 1);
            ui.AddDropdown(new[] { T("clinic_budget_variation_random"), T("clinic_budget_variation_10"), T("clinic_budget_variation_0"), T("clinic_budget_variation_10_2") },
                testBudgetOffset, index => testBudgetOffset = index);
            ui.AddButton(T("clinic_lower_levels_black_market"), () => StartVisit(PatientTier.Lower));
            ui.AddButton(T("clinic_security_revolution"), () => StartVisit(PatientTier.Security));
            ui.AddButton(T("clinic_upper_levels_2"), () => StartVisit(PatientTier.Upper));
            ui.AddButton(T("clinic_practice_fractures_gunshot_wounds_and_blood_loss"), StartP2Practice);
            ui.AddCloseButton(() => CustomUIManager.Instance?.CloseWindow(DebugWindow));
            ui.Show();
        }

        private void ShowBody(ClinicCase visit)
        {
            var manager = CustomUIManager.Instance;
            var previous = manager.GetWindow(BodyWindowId)?.Rect;
            var position = previous != null ? previous.anchoredPosition : new Vector2(-308, 0);
            var sibling = previous != null ? previous.GetSiblingIndex() : -1;
            manager.CloseWindow(BodyWindowId);
            var ui = CustomUIManager.Window(BodyWindowId, string.IsNullOrEmpty(status) ? T("clinic_body_chart") : status)
                .SetSize(330, 618).SetPosition(position).SetDraggable(true);
            var samplingTarget = BuildPatientBody(ui, visit);
            ui.AddCloseButton(CloseBody);
            var rect = ui.Show().Rect;
            if (sibling >= 0) rect.SetSiblingIndex(sibling);
            dragPreview = rect.gameObject.AddComponent<DragPreview>();
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

            for (var i = 0; i < BodyChart.RegionCount; i++)
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

        internal RectTransform CurrentWoundDetailRect =>
            CustomUIManager.Instance?.IsOpen(WoundDetailsWindow) == true
                ? CustomUIManager.Instance.GetWindow(WoundDetailsWindow)?.Rect : null;

        private static string Progress(Abrasion wound)
        {
            if (wound.Kind == ConditionKind.Fracture) return wound.Fixed ? T("clinic_splinted_apply_a_bandage_for_support") : T("clinic_visible_fracture_apply_a_splint");
            if (wound.Kind == ConditionKind.Contusion) return T("clinic_apply_a_cold_pack");
            if (wound.Kind == ConditionKind.Burn) return wound.Cooled ? T("clinic_cooled_apply_a_topical_medication_bandage") : T("clinic_apply_a_cold_pack");
            if (wound.ComplexWound)
            {
                if (wound.Kind == ConditionKind.Gunshot && !wound.ForeignBodyRemoved) return T("clinic_bullet_visible_remove_it_with_forceps");
                if (!wound.Cleaned) return T("clinic_clean_the_wound");
                if (!wound.Closed) return T("clinic_suture_the_wound");
                return T("clinic_sutured_apply_a_bandage");
            }
            if (wound.Kind == ConditionKind.ShallowCut && !wound.Cleaned) return T("clinic_clean_with_rubbing_alcohol");
            return wound.Cleaned ? T("clinic_cleaned_apply_a_bandage") : T("clinic_awaiting_treatment_2");
        }

        private static int FindWoundAt(ClinicCase visit, int region)
        {
            for (var i = 0; i < visit.Wounds.Length; i++)
                if (!visit.Wounds[i].RequiresBloodTest && !visit.Wounds[i].IsCare && (visit.Wounds[i].Region ?? i) == region) return i;
            return -1;
        }

        private void TargetRegion(ClinicCase visit, int region, GameItem item, bool body = false, int? selectedWound = null)
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
                else status = T("clinic_use_medicine_that_is_still_in_your");
                if (!IsCurrent(visit)) return;
                Save();
                RefreshVisitPanels(visit);
                return;
            }
            var index = selectedWound ?? (body ? FindWoundAt(visit, region) : region);
            if (body && !selectedWound.HasValue && treatment.HasValue && (IsSystemicMedicine(treatment.Value) || treatingDependence))
            {
                index = visit.FindSystemicTreatment(treatment.Value);
                if (index < 0)
                {
                    status = T("clinic_this_medicine_does_not_match_any_of");
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
            status = T("clinic_there_is_no_treatable_wound_in_this");
            if (treatment.HasValue && CanUse(item) && ReactToTreatmentMistake(visit, IsInvasiveTreatment(treatment.Value))) return;
            RefreshVisitPanels(visit);
        }

        private void ProposeAddictionCure(ClinicCase visit, int index)
        {
            if (!IsCurrent(visit) || !TreatmentOpen || index < 0 || index >= visit.Wounds.Length) return;
            var wound = visit.Wounds[index];
            if (wound.Covered || wound.Kind != ConditionKind.OximorDependence || visit.RequiresAddictionCure(wound)) return;
            QueueClinicReply(visit, T("clinic_really_i_don_t_think_i_can"));
            wound.AddictionCureAccepted = true;
            status = T("clinic_the_patient_agreed_to_treatment_for_the");
            Save();
            CustomUIManager.Instance?.CloseWindow(WoundDetailsWindow);
            RefreshVisitPanels(visit);
        }

        private void StartBloodPractice(ConditionKind first, ConditionKind second, string firstMedicine, string secondMedicine)
        {
            if (!IsLoaded || (state.Current != null && !state.Current.Settled)) return;
            var visit = ClinicCase.Create(PatientTier.Upper, 2, 0);
            visit.Practice = true;
            visit.PatientName = T("clinic_blood_test_practice_patient");
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
                log.Warning(T("clinic_could_not_start_blood_test_practice", failure));
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
                status = T("clinic_choose_supplies_that_can_still_be_removed");
            else if (RejectInappropriateAnesthesia(visit, treatment))
            {
                if (!ReactToTreatmentMistake(visit, stopped: true)) RefreshVisitPanels(visit);
                return;
            }
            else if (treatment == Treatment.DuskReagent || treatment == Treatment.Zyanide) status = T("clinic_drag_onto_the_body_chart");
            else if (unusableImmunivax)
            {
                status = T("clinic_this_immunivax_injector_is_empty_or_unusable");
                if (wound.Kind == ConditionKind.SentinelCollapse)
                {
                    QueueClinicReply(visit, T("clinic_are_you_kidding_me_there_s_nothing"), difficulty == Difficulty.困难 && visit.Patience < 2);
                    if (ReactToTreatmentMistake(visit, stopped: true)) return;
                }
            }
            else if (treatment == Treatment.Oximor && wound.Kind == ConditionKind.OximorDependence && visit.RequiresAddictionCure(wound))
            {
                status = T("clinic_the_patient_has_chosen_treatment_for_their");
                QueueClinicReply(visit, T("clinic_i_want_to_quit_for_good_give"));
                if (ReactToTreatmentMistake(visit, stopped: true)) return;
            }
            else if (IsAnesthesia(treatment.Value) && !(treatment == Treatment.Oximor && wound.Kind == ConditionKind.OximorDependence))
                status = T("clinic_drag_the_anesthetic_onto_the_body_chart");
            else if (wound.Hidden && visit.HiddenBudget == 0) status = T("clinic_get_the_patient_s_consent_before_treating");
            else if (difficulty == Difficulty.简单 && !valid)
                status = wound.LinkedWound.HasValue && !visit.Wounds[wound.LinkedWound.Value].Covered ? T("clinic_treat_the_linked_wound_first") :
                    wound.Kind == ConditionKind.OximorDependence ? T("clinic_use_the_medicine_appropriate_for_this_patient") :
                    T("clinic_easy_mode_blocked_the_ineffective_action");
            else if (!valid && IsInvasiveTreatment(treatment.Value))
            {
                status = T("clinic_the_patient_stopped_the_procedure");
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
                    ? T("clinic_the_patient_recognized_the_immunivax_the_dose", immunivax.ReducedQuality, immunivax.Price)
                    : tool ? T("clinic_used_a_reusable_tool") : T("clinic_used_one_unit_of_supplies");
                else
                {
                    status = T("clinic_used_one_unit_of_supplies_with_no");
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
                state.LastReputationReason = "clinic_reason_patient_walkout";
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
