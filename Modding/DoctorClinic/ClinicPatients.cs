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
            directory.Add(ToxinNeutralizer, () => MedicalPack.CreateSupply(ToxinNeutralizer, "工业毒素中和剂", Abrasion.ToxinNeutralizerPrice, "toxin_neutralizer", 4, 1));
            directory.Add(CommissionNote, CreateLegacyCommissionNote);
            directory.Add(Reagent, () => MedicalPack.CreateSupply(Reagent, "迟暮试剂", 30, "dusk_reagent", 1, 2));
            directory.Add(LicenseItem, () => AssociationItem(LicenseItem, "医生执照", "doctor_license"));
            directory.Add(Handbook, () => AssociationItem(Handbook, "协会诊疗手册", "association_manual"));
            directory.Add(Syringe, () => RegisterTool(Syringe, "自动清洁采样针管", "self_cleaning_sampler", 2, 1));
            directory.Add(BloodSample, CreateBloodSample);
            directory.Add(Analyzer, () => RegisterTool(Analyzer, "血液分析仪", "blood_analyzer", 2, 2));
            directory.Add(Scalpel, () => RegisterTool(Scalpel, "手术刀", "scalpel", 2, 1));
            directory.Add(Forceps, () => RegisterTool(Forceps, "镊子", "tweezers", 2, 1));
            directory.Add(DoctorStorage, CreateDoctorStorage);
        }

        private static GameItem CreateDoctorStorage()
        {
            var (window, inventory) = DirectoryUtils.CreateInventoryWindow(17, 10, true);
            var item = new GameItemElement(DoctorStorage, "Items/items_ship", "storage_bay_chem", null);
            item.SetContentWindow(window);
            ContainerHelper.InitContainerItem(inventory, item, new List<string> { "MEDICAL", "ITEM_MEDICAL_TAG" }, new List<string>());
            item.name = "医生储藏区";
            item.shortDescription = "只能存放医疗品。";
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
            if (!IsLoaded || Today < 8 || ModHelper.IsGameOver()) return;
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
                displayName = "街区医师协会代表",
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
                $"我们在招募愿意接诊的店主。入会费是 {AssociationFee} 信用点，执照、基本工具和医生储藏区由我们提供。")
                .SetNextDialogue(CreateAssociationChoices(representative));
            representative.CompleteClientCreation(false);
            manager.AddClient(representative);
            log.Log($"第 {Today} 天：协会代表已加入访客队列。");
        }

        private Dialogue CreateAssociationChoices(StoreClient representative)
        {
            var accept = new Dialogue().SetText("PLAYER", "加入");
            var postpone = new Dialogue().SetText("PLAYER", "暂缓");
            var refuse = new Dialogue().SetText("PLAYER", "永久拒绝");
            var speaker = representative.displayName;
            postpone.NextDialogue().SetText(speaker, "好。我们七天后再来问你。")
                .SetEndAction(() => DismissAssociationRepresentative(representative));
            refuse.NextDialogue().SetText(speaker, "明白了，协会不会再来邀请你。")
                .SetEndAction(() => DismissAssociationRepresentative(representative));

            return new Dialogue().SetText(speaker, "你愿意加入街区医师协会吗？")
                .AddChoice(DirectoryMaster.Item("$", true), "", "", accept, () =>
                {
                    var failure = JoinAssociation(representative);
                    var reply = new Dialogue().SetText(speaker, failure ?? "欢迎加入。执照、手册和入会物资已放入随身库存。");
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
                    visit.PatientName = template?.displayName ?? FactionName(faction) + "患者";
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
            StoreClient.FACTION_UPPER => "上层区",
            StoreClient.FACTION_SECURITY => "治安部",
            StoreClient.FACTION_CRIME => "黑市",
            Revolution => "革命军",
            _ => "下层区"
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
            var concern = visible.Any(wound => wound.Kind == ConditionKind.VascularCare) ? "好久没有做血管护理了。。。有蓝血吗？"
                : visible.Any(wound => wound.Kind == ConditionKind.SkinCare) ? "好久没有好好保养一下了。。。有「永恒」焕活精华液吗？"
                : visible.Any(wound => wound.IsLifeThreatening) ? "救命！我受伤了，救救我！"
                : visible.Any(wound => wound.Kind == ConditionKind.OximorOverdose)
                    ? "我不小心抽了太多的奥克西莫，现在浑身无力，有没有能让我清醒过来的东西。。。"
                : visible.Any(wound => wound.Kind == ConditionKind.SentinelCollapse) ? "该死的哨兵综合征，我需要免疫宁，快！"
                : visible.Any(wound => wound.Kind == ConditionKind.OximorDependence) ? "求你了，我快受不了了，有没有奥克西莫？"
                : visible.Any(wound => wound.RequiresBloodTest) ? "这几天身体一直不舒服，能帮我看看吗"
                : "不小心受伤了，能帮我处理一下吗";
            var greeting = patient.mainDialogue.SetText(patient.displayName, concern);
            if (visit.HasActiveBloodLoss)
                greeting = greeting.NextDialogue().SetText(patient.displayName, "我好像失血太多了，头好晕。。。");
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
            var ui = CustomUIManager.Window(AssociationWindow, "街区医师协会").SetSize(550, 420);
            if (!string.IsNullOrEmpty(message)) ui.AddLabel(message);
            if (state.License == LicenseStatus.None)
            {
                ui.AddLabel("执照：未取得");
                if (Today < 8) ui.AddLabel("第 8 天起，协会代表会在营业时来访。");
                else if (state.InvitationPermanentlyDeclined) ui.AddLabel("已永久拒绝协会邀请。可通过王尔德的恩惠取得医生执照和协会诊疗手册。");
                else if (store.currentClientInstance?.GetClientBlueprint() is StoreClient representative && representative.identifier == InviteId)
                {
                    ui.AddButton("继续与协会代表交谈", () =>
                    {
                        manager.CloseWindow(AssociationWindow);
                        var dialog = DialogUIManager.Instance;
                        if (dialog != null && dialog.GetCurrentDialog() == null)
                            dialog.StartDialogue(CreateAssociationChoices(representative));
                    });
                }
                else
                {
                    ui.AddLabel(state.InvitationRequested ? "已预约，等待下次排访。" : "可以预约协会代表下次营业时来访。");
                    if (!state.InvitationRequested)
                        ui.AddButton("预约入会", () =>
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
                ui.AddLabel($"医生 {rep / 2m:0.0}　{ClinicReputation.Title(rep)}");
                ui.AddLabel($"执照：{(state.License == LicenseStatus.Active ? "有效" : state.License == LicenseStatus.Suspended ? "调查暂停" : "已吊销")}　入会第 {state.JoinedDay} 天");
                ui.AddLabel($"正常完成 {state.SuccessfulVisits} 例　当前每例最多 {ClinicReputation.MaxConditions(state.SuccessfulVisits)} 项");
                ui.AddLabel($"最近声望变化：{state.LastReputationReason ?? "无"}");
                ui.AddLabel(rep < ClinicReputation.Established * 2 ? $"声望 {ClinicReputation.Established} 可增加预约。" :
                    rep < ClinicReputation.SundaySupply * 2 ? $"声望 {ClinicReputation.SundaySupply} 解锁周日补货。" :
                    rep < ClinicReputation.Trusted * 2 ? $"声望 {ClinicReputation.Trusted} 每日安排 2 名患者。" :
                    rep < ClinicReputation.Renowned * 2 ? $"声望 {ClinicReputation.Renowned} 有机会安排第 3 名患者。" :
                    "当前预约档位已达上限。");
                if (rep <= -40) ui.AddLabel("暂停安排上层患者。医生声望降至 −100 时停止接诊。");
                if (state.Schedule?.Day == Today)
                    ui.AddLabel($"今日排班 {state.Schedule.Patients.Count} 人，待到场 {state.Schedule.Patients.Count(patient => !patient.Arrived)} 人。");
                if (state.License == LicenseStatus.Active)
                    ui.AddToggle("营业时接诊", !state.Paused, enabled =>
                    {
                        state.Paused = !enabled;
                        if (!enabled) store.storeClientManager.GetClientStack().RemoveAll(client =>
                            client?.identifier?.StartsWith(PatientPrefix, System.StringComparison.Ordinal) == true);
                        Save();
                    });
                if (state.License == LicenseStatus.Revoked && !state.EndingPending && !state.EndingTriggered)
                    ui.AddButton($"向王尔德申请恢复执照 · {RecoveryPrice}", () => RestoreLicense());
                if (state.MedicalDebt > 0)
                    ui.AddButton($"偿还医疗赔偿 · 欠 {state.MedicalDebt}", PayMedicalDebt);
                if (state.Current != null && !state.Current.Settled)
                    ui.AddButton("打开当前病历", () => { manager.CloseWindow(AssociationWindow); Show(); });
            }
            ui.Show();
        }

        private string JoinAssociation(StoreClient representative)
        {
            if (!IsAssociationRepresentativeAtCounter(representative) || state.License != LicenseStatus.None ||
                state.InvitationPermanentlyDeclined) return "目前无法办理入会。";
            if (store.playerCash < AssociationFee) return $"入会需要 {AssociationFee} 信用点。";
            var failure = MedicalPack.Deliver(EmporiumEntry.Instance?.invElement, StarterItems, () =>
            {
                if (!IsAssociationRepresentativeAtCounter(representative) || state.License != LicenseStatus.None ||
                    state.InvitationPermanentlyDeclined || store.playerCash < AssociationFee) return false;
                store.PayMoney(AssociationFee, true);
                ActivateFirstClinicLicense("加入医师协会");
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
                    case LicenseItem: MedicalPack.RefreshSavedItem(item, "doctor_license", 2, 2, "DOCUMENT"); break;
                    case Handbook: MedicalPack.RefreshSavedItem(item, "association_manual", 2, 2, "DOCUMENT"); break;
                    case Syringe: MedicalPack.RefreshSavedItem(item, "self_cleaning_sampler", 2, 1, "TOOL"); break;
                    case Analyzer: MedicalPack.RefreshSavedItem(item, "blood_analyzer", 2, 2, "TOOL"); break;
                    case CommissionNote: RefreshSavedCommissionNote(item); break;
                    case Antifungal:
                        UpdateAntifungalDisplay(item);
                        MedicalPack.RefreshSavedItem(item, "antifungal", 2, 2);
                        break;
                    case BloodSample: MedicalPack.RefreshSavedItem(item, BloodSampleSprite, 1, 2); break;
                    case Scalpel: MedicalPack.RefreshSavedItem(item, "scalpel", 2, 1, "TOOL"); break;
                    case Forceps: MedicalPack.RefreshSavedItem(item, "tweezers", 2, 1, "TOOL"); break;
                    default: MedicalPack.RefreshSavedSupplies(item); break;
                }
            }
        }
    }
}
