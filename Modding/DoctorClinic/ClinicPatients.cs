using static DoctorClinic.ClinicText;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string AssociationWindow = "doctor_clinic_association";
        private const string InviteId = "doctor_clinic_association_visit";
        private const string PatientPrefix = "doctor_clinic_patient_";
        private const string Revolution = "FACTION_REVOLUTION";
        private const string LicenseItem = "doctor_clinic_license";
        private const string Handbook = "doctor_clinic_handbook";
        private const string Syringe = "doctor_clinic_sampling_syringe";
        private const string Analyzer = "doctor_clinic_blood_analyzer";
        private const string Scalpel = "doctor_clinic_scalpel";
        private const string Forceps = "doctor_clinic_forceps";
        private const string DoctorStorage = "doctor_clinic_storage_bay";
        private const int AssociationFee = 150;
        private static readonly string[] StarterItems =
        {
            LicenseItem, Handbook, Syringe, Analyzer, Scalpel, Forceps, DoctorStorage, "sign_medical",
            "bandage_item", "bandage_item", "rubbing_alcohol", MedicalPack.ColdPack,
            "hemostatic_bandage_item", "phagimycin_pill"
        };

        private int Today => StoreStation.Instance?.dayCounter ?? 0;

        private void RegisterClinicItems(ModItemDirectory directory)
        {
            MedicalPack.Register(directory);
            directory.Add(Antifungal, CreateAntifungalBottle);
            directory.Add(ToxinNeutralizer, () => MedicalPack.CreateSupply(ToxinNeutralizer, T("clinic_industrial_toxin_neutralizer"), Abrasion.ToxinNeutralizerPrice, "toxin_neutralizer", 4, 1));
            directory.Add(CommissionNote, CreateLegacyCommissionNote);
            directory.Add(Reagent, () => MedicalPack.CreateSupply(Reagent, T("clinic_dusk_reagent"), 30, "dusk_reagent", 1, 2));
            directory.Add(LicenseItem, () => AssociationItem(LicenseItem, T("clinic_medical_license"), "doctor_license"));
            directory.Add(Handbook, () => AssociationItem(Handbook, T("clinic_association_treatment_manual"), "association_manual"));
            directory.Add(Syringe, () => RegisterTool(Syringe, T("clinic_self_cleaning_sampling_syringe"), "self_cleaning_sampler", 2, 1));
            directory.Add(BloodSample, CreateBloodSample);
            directory.Add(Analyzer, () => RegisterTool(Analyzer, T("clinic_blood_analyzer"), "blood_analyzer", 2, 2));
            directory.Add(Scalpel, () => RegisterTool(Scalpel, T("clinic_scalpel"), "scalpel", 2, 1));
            directory.Add(Forceps, () => RegisterTool(Forceps, T("clinic_forceps"), "tweezers", 2, 1));
            directory.Add(DoctorStorage, CreateDoctorStorage);
        }

        private static GameItem CreateDoctorStorage()
        {
            var (window, inventory) = DirectoryUtils.CreateInventoryWindow(17, 10, true);
            var item = new GameItemElement(DoctorStorage, "Items/items_ship", "storage_bay_chem", null);
            item.SetContentWindow(window);
            ContainerHelper.InitContainerItem(inventory, item, new List<string> { "MEDICAL", "ITEM_MEDICAL_TAG" }, new List<string>());
            item.name = T("clinic_doctor_s_storage_bay");
            item.shortDescription = T("clinic_holds_medical_items_only");
            item.SetGameItemType("STORAGE");
            item.EnableTag("SYSTEM_TAG", false);
            item.EnableTag("SYSTEM_TAG_UTILITY", false);
            return item;
        }

        private static GameItem RegisterTool(string id, string name, string sprite, int width, int height)
        {
            var item = MedicalPack.CreateSupply(id, name, 0, sprite, width, height);
            item.SetGameItemType("TOOL");
            return item;
        }

        private GameItem AssociationItem(string id, string name, string sprite = null)
        {
            var item = MedicalPack.CreateSupply(id, name, 0, sprite, 2, 2, "DOCUMENT");
            item.mayActivateSlotItemFunc += (target, inventory, slot) => GeneralHelper.IsItemEffectivelyOwned(target);
            item.canActivateSlotItemFunc += (target, inventory, slot) => GeneralHelper.IsItemEffectivelyOwned(target);
            item.onActivateSlotItemFunc += (target, inventory, slot) =>
            {
                if (id == Handbook) ShowHandbook();
                else ShowAssociation();
            };
            return item;
        }

        private void QueueClinicVisitors(StoreClientManager manager)
        {
            if (!IsLoaded || Today < 8 || ClinicCompat.IsGameOver()) return;
            ReviewSuspension();
            QueueTribunalIfNeeded();
            if (state.EndingPending || state.EndingTriggered) return;
            RestoreDreamReturns(manager);
            if (state.License == LicenseStatus.None)
            {
                if (state.ShouldInviteToAssociation(Today) &&
                    !manager.GetClientStack().Any(visitor => visitor?.identifier == InviteId) &&
                    store.currentClientInstance?.GetClientBlueprint()?.identifier != InviteId)
                    QueueAssociationInvite(manager);
                return;
            }

            QueueRequestedPharmacies(manager);
            if (state.License != LicenseStatus.Active || state.ReputationHalfPoints <= -200) return;
            QueueWeeklyPharmacy(manager);
            if (state.Paused || Today <= state.ResumeAfterDay) return;
            if (state.Schedule?.Day != Today)
            {
                state.Schedule = BuildSchedule(manager);
                Save();
                if (clinicOpenedDay == Today) DeliverCommissions();
            }
            var queueRandom = new System.Random(unchecked(state.ScheduleSeed * 7919 + Today * 101));
            foreach (var patient in state.Schedule.Patients)
            {
                if (patient.Arrived || patient.Settled ||
                    (patient.Faction == StoreClient.FACTION_UPPER && !ClinicReputation.CanSeeUpper(state.ReputationHalfPoints)))
                    continue;
                var visitorId = PatientPrefix + patient.PatientId;
                if (!manager.GetClientStack().Any(visitor => visitor?.identifier == visitorId))
                    QueuePatient(manager, patient, visitorId, queueRandom);
            }
        }

        private void QueueAssociationInvite(StoreClientManager manager)
        {
            var representative = new StoreClient
            {
                identifier = InviteId,
                displayName = T("clinic_neighborhood_medical_association_representative"),
                spriteName = SpriteDict.GetRandomLowerLevelSpriteName(),
                clientFaction = StoreClient.FACTION_LOWER,
                clientIntent = StoreClient.ClientIntent.DIALOGUE,
                isBargainAvailable = false
            };
            representative.OnArrival = () =>
            {
                if (!IsLoaded || state.License != LicenseStatus.None) return;
                state.InvitationSeen = true;
                state.InvitationRequested = false;
                state.InvitationRetryDay = 0;
                Save();
            };
            representative.mainDialogue.SetText(representative.displayName,
                T("clinic_we_re_recruiting_shopkeepers_who_can_see", AssociationFee))
                .SetNextDialogue(CreateAssociationChoices(representative));
            representative.CompleteClientCreation(false);
            manager.AddClient(representative);
            log.Log(T("clinic_day_the_association_representative_was_added_to", Today));
        }

        private Dialogue CreateAssociationChoices(StoreClient representative)
        {
            var accept = new Dialogue().SetText("PLAYER", T("clinic_join"));
            var postpone = new Dialogue().SetText("PLAYER", T("clinic_not_now"));
            var refuse = new Dialogue().SetText("PLAYER", T("clinic_decline_permanently"));
            var speaker = representative.displayName;
            postpone.NextDialogue().SetText(speaker, T("clinic_all_right_we_ll_ask_again_in"))
                .SetEndAction(() => DismissAssociationRepresentative(representative));
            refuse.NextDialogue().SetText(speaker, T("clinic_understood_the_association_won_t_ask_again"))
                .SetEndAction(() => DismissAssociationRepresentative(representative));

            return new Dialogue().SetText(speaker, T("clinic_would_you_like_to_join_the_neighborhood"))
                .AddChoice(DirectoryMaster.Item("$", true), "", "", accept, () =>
                {
                    var failure = JoinAssociation(representative);
                    var reply = new Dialogue().SetText(speaker, failure ?? T("clinic_welcome_aboard_your_license_manual_and_starter"));
                    if (failure == null) reply.SetEndAction(() => DismissAssociationRepresentative(representative));
                    else reply.SetNextDialogue(CreateAssociationChoices(representative));
                    accept.SetNextDialogue(reply);
                }, null)
                .AddChoice(DirectoryMaster.Item("$", true), "", "", postpone,
                    () => DeferAssociationInvite(representative, false), null)
                .AddChoice(DirectoryMaster.Item("$", true), "", "", refuse,
                    () => DeferAssociationInvite(representative, true), null);
        }

        private bool IsAssociationRepresentativeAtCounter(StoreClient representative) =>
            IsLoaded && store.currentClientInstance?.GetClientBlueprint() == representative;

        private void DismissAssociationRepresentative(StoreClient representative)
        {
            if (IsAssociationRepresentativeAtCounter(representative)) store.DismissCurrentClient();
        }

        private void DeferAssociationInvite(StoreClient representative, bool permanently)
        {
            if (!IsAssociationRepresentativeAtCounter(representative) || state.License != LicenseStatus.None) return;
            state.InvitationRequested = false;
            state.InvitationPermanentlyDeclined = permanently;
            state.InvitationRetryDay = permanently ? 0 : Today + 7;
            Save();
        }

        private ClinicSchedule BuildSchedule(StoreClientManager manager)
        {
            var schedule = new ClinicSchedule { Day = Today, ReputationHalfPoints = state.ReputationHalfPoints };
            if (state.Schedule != null)
                foreach (var postponed in state.Schedule.Patients.Where(visit => !visit.Arrived && !visit.Settled))
                {
                    var commission = CommissionFor(postponed.Id);
                    if (commission == null || (commission.Status != CommissionStatus.Offered &&
                        commission.Status != CommissionStatus.Active && commission.Status != CommissionStatus.Declined)) continue;
                    if (!ClinicReputation.CanSeeUpper(schedule.ReputationHalfPoints))
                    {
                        ExpireCommission(commission);
                        continue;
                    }
                    commission.Day = Today;
                    schedule.Patients.Add(postponed);
                }
            var commissionRandom = new System.Random(unchecked(state.ScheduleSeed * 31 + Today * 983));
            var originalRandom = UnityEngine.Random.state;
            UnityEngine.Random.InitState(unchecked(state.ScheduleSeed * 397 ^ Today));
            try
            {
                var count = ClinicReputation.DailyPatients(schedule.ReputationHalfPoints, UnityEngine.Random.Range(0, 2) == 0);
                for (var i = 0; i < count; i++)
                {
                    var faction = RollFaction(manager, schedule.ReputationHalfPoints);
                    var template = manager.PickRandomValidClient(faction);
                    var maxConditions = ClinicReputation.MaxConditions(state.SuccessfulVisits);
                    var care = faction == StoreClient.FACTION_UPPER && maxConditions == 3 && UnityEngine.Random.Range(0, 100) < 15;
                    var conditionCount = care ? 1 : UnityEngine.Random.Range(1, maxConditions + 1);
                    var offset = UnityEngine.Random.Range(-10, 11);
                    var visit = care ? ClinicCase.CreateCare(UnityEngine.Random.Range(0, 2) == 0
                        ? ConditionKind.SkinCare : ConditionKind.VascularCare, offset)
                        : ClinicCase.Create(TierOf(faction), conditionCount, offset);
                    visit.AddictionRequiresCure = faction == StoreClient.FACTION_UPPER;
                    visit.PatientId = $"{Today}_{i}";
                    visit.AcceptsAnesthesia = UnityEngine.Random.Range(0, 2) == 0;
                    visit.Faction = faction;
                    visit.PatientName = template?.displayName ?? T("clinic_faction_patient", FactionName(faction));
                    var portrait = template?.GetSprite();
                    visit.Portrait = string.IsNullOrEmpty(portrait) ? SpriteDict.GetRandomLowerLevelSpriteName() : portrait;
                    foreach (var condition in visit.Wounds.Where(condition => !condition.IsCare))
                    {
                        var external = UnityEngine.Random.Range(0, 100) < ClinicReputation.ExternalChance(TierOf(faction));
                        condition.Kind = RollCondition(external, maxConditions, faction);
                    }
                    AssignRegions(visit);
                    var link = System.Array.FindIndex(visit.Wounds, wound =>
                        wound.Kind == ConditionKind.Abrasion || wound.Kind == ConditionKind.ShallowCut);
                    var infection = System.Array.Find(visit.Wounds, wound => wound.Kind == ConditionKind.GraywaterFever);
                    if (link >= 0 && infection != null && UnityEngine.Random.Range(0, 2) == 0)
                    {
                        infection.Kind = ConditionKind.WoundInfection;
                        infection.LinkedWound = link;
                    }
                    if (conditionCount < maxConditions && UnityEngine.Random.Range(0, 100) < (maxConditions - 1) * 10)
                    {
                        visit.Wounds = visit.Wounds.Append(new Abrasion
                        {
                            Id = System.Guid.NewGuid().ToString("N"),
                            Kind = link < 0 ? ConditionKind.GraywaterFever : ConditionKind.WoundInfection,
                            LinkedWound = link < 0 ? (int?)null : link,
                            Hidden = true
                        }).ToArray();
                    }
                    visit.RollBloodLoss(UnityEngine.Random.Range(0, 100));
                    schedule.Patients.Add(visit);
                    RollCommission(visit, commissionRandom);
                }
                return schedule;
            }
            finally
            {
                UnityEngine.Random.state = originalRandom;
            }
        }

        private static ConditionKind RollCondition(bool external, int maxConditions, string faction)
        {
            if (!external)
            {
                var internalKinds = maxConditions == 1 ? 2 : maxConditions == 2 ? 5 :
                    faction == StoreClient.FACTION_SECURITY ? 6 : 8;
                var internalSlot = UnityEngine.Random.Range(0, internalKinds);
                return internalSlot switch
                {
                    0 => ConditionKind.Contusion,
                    1 => ConditionKind.GraywaterFever,
                    2 => ConditionKind.Fracture,
                    3 => ConditionKind.PipeSporeDisease,
                    4 => ConditionKind.IndustrialToxinExposure,
                    5 => ConditionKind.SentinelCollapse,
                    6 => ConditionKind.OximorOverdose,
                    _ => ConditionKind.OximorDependence
                };
            }
            var slot = UnityEngine.Random.Range(0, maxConditions == 1 ? 2 : maxConditions + 2);
            return slot switch
            {
                0 => ConditionKind.Abrasion,
                1 => ConditionKind.ShallowCut,
                2 => ConditionKind.DeepCut,
                3 => ConditionKind.Burn,
                _ => ConditionKind.Gunshot
            };
        }

        private static void AssignRegions(ClinicCase visit)
        {
            var available = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5 };
            foreach (var fracture in visit.Wounds.Where(wound => wound.Kind == ConditionKind.Fracture))
            {
                var limbs = available.Where(region => region == 0 || region == 1 || region >= 4).ToArray();
                var region = limbs[UnityEngine.Random.Range(0, limbs.Length)];
                fracture.Region = region;
                available.Remove(region);
            }
            foreach (var wound in visit.Wounds.Where(wound => !wound.RequiresBloodTest && !wound.IsCare && !wound.Region.HasValue))
            {
                var index = UnityEngine.Random.Range(0, available.Count);
                wound.Region = available[index];
                available.RemoveAt(index);
            }
        }

        private static string RollFaction(StoreClientManager manager, int reputation)
        {
            for (var attempt = 0; attempt < 16; attempt++)
            {
                var faction = manager.ComputeClientType();
                if (faction == StoreClient.FACTION_UPPER && ClinicReputation.CanSeeUpper(reputation)) return faction;
                if (faction == StoreClient.FACTION_LOWER || faction == StoreClient.FACTION_CRIME ||
                    faction == StoreClient.FACTION_SECURITY || faction == Revolution) return faction;
            }
            return StoreClient.FACTION_LOWER;
        }

        private static PatientTier TierOf(string faction)
        {
            if (faction == StoreClient.FACTION_UPPER) return PatientTier.Upper;
            if (faction == StoreClient.FACTION_SECURITY || faction == Revolution) return PatientTier.Security;
            return PatientTier.Lower;
        }

        private static string FactionName(string faction) => faction switch
        {
            StoreClient.FACTION_UPPER => T("clinic_upper_levels"),
            StoreClient.FACTION_SECURITY => T("clinic_security"),
            StoreClient.FACTION_CRIME => T("clinic_black_market"),
            Revolution => T("clinic_revolution"),
            _ => T("clinic_lower_levels")
        };

        private void QueuePatient(StoreClientManager manager, ClinicCase visit, string visitorId, System.Random random)
        {
            var patient = new StoreClient
            {
                identifier = visitorId,
                displayName = visit.PatientName,
                spriteName = visit.Portrait,
                clientFaction = visit.Faction,
                clientIntent = StoreClient.ClientIntent.BUY,
                isBargainAvailable = true,
                willAcceptItem = _ => false
            };
            patient.SetClientBudget(visit.Budget);
            patient.OnArrival = () => Arrive(visit, patient);
            var visible = visit.Wounds.Where(wound => !wound.Hidden).ToArray();
            var concern = visible.Any(wound => wound.Kind == ConditionKind.VascularCare) ? T("clinic_it_s_been_ages_since_my_last")
                : visible.Any(wound => wound.Kind == ConditionKind.SkinCare) ? T("clinic_i_haven_t_had_a_proper_skin")
                : visible.Any(wound => wound.IsLifeThreatening) ? T("clinic_help_i_m_hurt_please_help_me")
                : visible.Any(wound => wound.Kind == ConditionKind.OximorOverdose)
                    ? T("clinic_i_took_too_much_oxymore_i_can")
                : visible.Any(wound => wound.Kind == ConditionKind.SentinelCollapse) ? T("clinic_damn_this_sentinel_syndrome_i_need_immunivax")
                : visible.Any(wound => wound.Kind == ConditionKind.OximorDependence) ? T("clinic_please_i_can_t_take_it_anymore")
                : visible.Any(wound => wound.RequiresBloodTest) ? T("clinic_i_haven_t_been_feeling_well_for")
                : T("clinic_i_had_a_little_accident_could_you");
            var greeting = patient.mainDialogue.SetText(patient.displayName, concern);
            if (visit.HasActiveBloodLoss)
                greeting = greeting.NextDialogue().SetText(patient.displayName, T("clinic_i_think_i_ve_lost_too_much"));
            greeting.SetEndAction(() =>
                {
                    if (IsCurrent(visit) && boundBuyer == patient) Show();
                });
            manager.AddClient(patient);
            var queue = manager.GetClientStack();
            if (!queue.Remove(patient)) return;
            var positions = new System.Collections.Generic.List<int>();
            for (var i = 0; i < queue.Count; i++)
            {
                var visitor = queue[i];
                if (visitor != null && !visitor.isWanted && string.IsNullOrEmpty(visitor.eventSourceId) &&
                    visitor.identifier != PharmacyId && (visitor.IsClientBuying() || visitor.IsClientSelling()))
                    positions.Add(i);
            }
            if (positions.Count == 0)
            {
                queue.Add(patient);
                return;
            }
            positions.Add(positions[positions.Count - 1] + 1);
            queue.Insert(positions[random.Next(positions.Count)], patient);
        }

        private void Arrive(ClinicCase visit, StoreClient patient)
        {
            if (!IsLoaded || visit.Arrived || visit.Settled) return;
            var native = NegociationUIManager.Instance;
            var inheritedTradeTablet = !SettlementOpen && native?.IsOpen == true;
            if (state.Current != null && !state.Current.Settled)
                Finish(state.Current, true, dismissBuyer: false, showReceipt: false);
            Close();
            if (inheritedTradeTablet) native.CloseUI();
            visit.Arrived = true;
            state.Current = visit;
            boundBuyer = patient;
            showTestingControls = false;
            status = "";
            Save();
            if (inheritedTradeTablet) ShowSettlement(visit, visit.Preview());
        }

        private void ShowAssociation(string message = null)
        {
            if (!IsLoaded || CustomUIManager.Instance == null) return;
            var manager = CustomUIManager.Instance;
            manager.CloseWindow(AssociationWindow);
            var ui = CustomUIManager.Window(AssociationWindow, T("clinic_neighborhood_medical_association")).SetSize(550, 420);
            if (!string.IsNullOrEmpty(message)) ui.AddLabel(message);
            if (state.License == LicenseStatus.None)
            {
                ui.AddLabel(T("clinic_license_not_issued"));
                if (Today < 8) ui.AddLabel(T("clinic_the_association_representative_can_visit_during_business"));
                else if (state.InvitationPermanentlyDeclined) ui.AddLabel(T("clinic_you_permanently_declined_the_association_s_invitation"));
                else if (store.currentClientInstance?.GetClientBlueprint() is StoreClient representative && representative.identifier == InviteId)
                {
                    ui.AddButton(T("clinic_continue_speaking_with_the_representative"), () =>
                    {
                        manager.CloseWindow(AssociationWindow);
                        var dialog = DialogUIManager.Instance;
                        if (dialog != null && dialog.GetCurrentDialog() == null)
                            dialog.StartDialogue(CreateAssociationChoices(representative));
                    });
                }
                else
                {
                    ui.AddLabel(state.InvitationRequested ? T("clinic_the_representative_is_scheduled_for_the_next") : T("clinic_you_can_ask_the_representative_to_visit"));
                    if (!state.InvitationRequested)
                        ui.AddButton(T("clinic_request_an_invitation"), () =>
                        {
                            state.InvitationRequested = true;
                            Save();
                            ShowAssociation();
                        });
                }
            }
            else
            {
                var rep = state.ReputationHalfPoints;
                ui.AddLabel(T("clinic_doctor_reputation", rep / 2m, ClinicReputation.Title(rep)));
                ui.AddLabel(T("clinic_license_joined_on_day", (state.License == LicenseStatus.Active ? T("clinic_active") : state.License == LicenseStatus.Suspended ? T("clinic_suspended_pending_investigation") : T("clinic_revoked")), state.JoinedDay));
                ui.AddLabel(T("clinic_completed_visits_conditions_per_patient_up_to", state.SuccessfulVisits, ClinicReputation.MaxConditions(state.SuccessfulVisits)));
                ui.AddLabel(T("clinic_latest_reputation_change", ReputationReason(state.LastReputationReason)));
                ui.AddLabel(rep < ClinicReputation.Established * 2 ? T("clinic_reach_reputation_to_see_more_patients", ClinicReputation.Established) :
                    rep < ClinicReputation.SundaySupply * 2 ? T("clinic_reach_reputation_to_unlock_sunday_restocks", ClinicReputation.SundaySupply) :
                    rep < ClinicReputation.Trusted * 2 ? T("clinic_at_reputation_two_patients_are_scheduled_per", ClinicReputation.Trusted) :
                    rep < ClinicReputation.Renowned * 2 ? T("clinic_at_reputation_a_third_patient_may_be", ClinicReputation.Renowned) :
                    T("clinic_you_have_reached_the_highest_appointment_tier"));
                if (rep <= -40) ui.AddLabel(T("clinic_upper_levels_patients_are_no_longer_scheduled"));
                if (state.Schedule?.Day == Today)
                    ui.AddLabel(T("clinic_today_s_appointments_still_to_arrive", state.Schedule.Patients.Count, state.Schedule.Patients.Count(patient => !patient.Arrived)));
                if (state.License == LicenseStatus.Active)
                    ui.AddToggle(T("clinic_see_patients_during_business_hours"), !state.Paused, enabled =>
                    {
                        state.Paused = !enabled;
                        if (!enabled) store.storeClientManager.GetClientStack().RemoveAll(client =>
                            client?.identifier?.StartsWith(PatientPrefix, System.StringComparison.Ordinal) == true);
                        Save();
                    });
                if (state.License == LicenseStatus.Revoked && !state.EndingPending && !state.EndingTriggered)
                    ui.AddButton(T("clinic_ask_wilde_to_restore_your_license", RecoveryPrice), () => RestoreLicense());
                if (state.MedicalDebt > 0)
                    ui.AddButton(T("clinic_pay_medical_compensation_owed", state.MedicalDebt), PayMedicalDebt);
                if (state.Current != null && !state.Current.Settled)
                    ui.AddButton(T("clinic_open_current_case"), () => { manager.CloseWindow(AssociationWindow); Show(); });
            }
            ui.Show();
        }

        private string JoinAssociation(StoreClient representative)
        {
            if (!IsAssociationRepresentativeAtCounter(representative) || state.License != LicenseStatus.None ||
                state.InvitationPermanentlyDeclined) return T("clinic_membership_is_unavailable_right_now");
            if (store.playerCash < AssociationFee) return T("clinic_membership_costs_credits", AssociationFee);
            var failure = MedicalPack.Deliver(EmporiumEntry.Instance?.invElement, StarterItems, () =>
            {
                if (!IsAssociationRepresentativeAtCounter(representative) || state.License != LicenseStatus.None ||
                    state.InvitationPermanentlyDeclined || store.playerCash < AssociationFee) return false;
                store.PayMoney(AssociationFee, true);
                ActivateFirstClinicLicense("clinic_reason_joined_association");
                return true;
            });
            OverlayHandler.current?.UpdateInventoryStateUI();
            if (failure == null) Save();
            return failure;
        }

        private void ActivateFirstClinicLicense(string reason)
        {
            state.License = LicenseStatus.Active;
            state.JoinedDay = Today;
            state.ReputationHalfPoints = 0;
            state.LastReputationReason = reason;
            state.ScheduleSeed = UnityEngine.Random.Range(1, int.MaxValue);
            state.InvitationRequested = false;
            state.InvitationRetryDay = 0;
        }

        private void RefreshSavedClinicItems()
        {
            if (!IsLoaded || EmporiumEntry.Instance == null) return;
            foreach (var item in store.FindAllItem(false).Distinct().ToArray())
            {
                if (item == null) continue;
                switch (item.identifier)
                {
                    case MedicalPack.Id: MedicalPack.AddUnpackHint(item); break;
                    case DoctorStorage: item.name = T("clinic_doctor_s_storage_bay"); item.shortDescription = T("clinic_holds_medical_items_only"); break;
                    case LicenseItem: item.name = T("clinic_medical_license"); MedicalPack.RefreshSavedItem(item, "doctor_license", 2, 2, "DOCUMENT"); break;
                    case Handbook: item.name = T("clinic_association_treatment_manual"); MedicalPack.RefreshSavedItem(item, "association_manual", 2, 2, "DOCUMENT"); break;
                    case Syringe: item.name = T("clinic_self_cleaning_sampling_syringe"); MedicalPack.RefreshSavedItem(item, "self_cleaning_sampler", 2, 1, "TOOL"); break;
                    case Analyzer: item.name = T("clinic_blood_analyzer"); MedicalPack.RefreshSavedItem(item, "blood_analyzer", 2, 2, "TOOL"); break;
                    case CommissionNote: RefreshSavedCommissionNote(item); break;
                    case Reagent: item.name = T("clinic_dusk_reagent"); break;
                    case ToxinNeutralizer: item.name = T("clinic_industrial_toxin_neutralizer"); break;
                    case Antifungal:
                        item.name = T("clinic_sporeclear_tablets");
                        item.shortDescription = T("clinic_five_doses_per_bottle");
                        UpdateAntifungalDisplay(item);
                        MedicalPack.RefreshSavedItem(item, "antifungal", 2, 2);
                        break;
                    case BloodSample:
                        item.name = T("clinic_blood_sample_vial");
                        item.shortDescription = T("clinic_drag_onto_the_blood_analyzer");
                        MedicalPack.RefreshSavedItem(item, BloodSampleSprite, 1, 2);
                        break;
                    case Scalpel: item.name = T("clinic_scalpel"); MedicalPack.RefreshSavedItem(item, "scalpel", 2, 1, "TOOL"); break;
                    case Forceps: item.name = T("clinic_forceps"); MedicalPack.RefreshSavedItem(item, "tweezers", 2, 1, "TOOL"); break;
                    default: MedicalPack.RefreshSavedSupplies(item); break;
                }
            }
        }
    }
}
