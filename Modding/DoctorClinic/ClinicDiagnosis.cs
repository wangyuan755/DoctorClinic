using System.Linq;
using HarmonyLib;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string BloodSample = "doctor_clinic_blood_sample";
        private const string BloodSampleSprite = "blood_sample_filled";
        private const string HandbookWindow = "doctor_clinic_handbook_window";

        private void ShowHandbook()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            manager.CloseWindow(HandbookWindow);
            var ui = CustomUIManager.Window(HandbookWindow, "协会诊疗手册").SetSize(610, 610);
            ui.AddLabel("擦伤：绷带覆盖；先清洁可获得完美评价。");
            ui.AddLabel("浅割伤：用酒精清洁，再用任意绷带覆盖。");
            ui.AddLabel("挫伤：使用冷敷包。");
            ui.AddLabel("烧伤：先用冷敷包，再用外用药膏绷带覆盖。");
            ui.AddLabel("骨折：先用夹板固定，再用绷带支持。");
            ui.AddLabel("深割伤：用酒精清洁，再缝合、覆盖。中弹伤：用镊子取出子弹，再清洁、缝合、覆盖。");
            ui.AddLabel("全身不适：把采样针管拖到人体图，再把血样瓶拖到血液分析仪。");
            ui.AddLabel("B1 对应灰水热或伤口感染。先处理关联伤处，再把药片拖到人体图。");
            ui.AddLabel("BL1 对应失血，需先控制关联伤口的出血，再用血袋。");
            ui.AddLabel("F1 阳性：管道孢子症，使用净孢片。");
            ui.AddLabel("T1 阳性：工业毒素暴露，使用工业毒素中和剂。");
            ui.AddLabel("O1 阳性：奥克西莫过量，使用纳洛通。");
            ui.AddLabel("S1 阳性：哨兵崩溃综合征，用免疫宁维护。空针无效，过期品或赝品按品质收费。");
            ui.AddLabel("噬菌霉素可用于 B1 病例；无证版完成时最多获普通评价。");
            ui.AddLabel("上层区的患者会前来寻求高端护理。按患者陈述，血管护理使用蓝血，皮肤护理使用「永恒」焕活精华液。");
            ui.AddLabel("A1 阳性：奥克西莫成瘾。奥克西莫仅能暂时缓解，根除用赛纳普斯。成瘾患者拒绝奥克莫吸。");
            ui.AddCloseButton(() => manager.CloseWindow(HandbookWindow));
            ui.Show();
        }

        private GameItem CreateBloodSample()
        {
            var item = new GameItemElement(BloodSample, "Items/items_material", "med1", null);
            item.name = "血样瓶";
            item.shortDescription = "拖到血液分析仪。";
            item.SetValue(0);
            item.SetShape(new GridShapeBuilder(1, 2).SetDataFill(1).Build());
            item.SetSprite(ModId, BloodSampleSprite);
            item.useSpriteFromMod = true;
            item.mayThisTargetItemFunc = IsBloodAnalyser;
            item.canThisTargetItemFunc = IsBloodAnalyser;
            item.onThisTargetItemFunc = AnalyseBloodSample;
            return item;
        }

        private static bool IsBloodAnalyser(GameItem sample, GameItem target) =>
            sample?.identifier == BloodSample && target?.identifier == Analyzer;

        private GameItem FindBloodSample(ClinicCase visit) => store.FindAllItem(true).FirstOrDefault(item =>
            item?.identifier == BloodSample && item.uniqueId == visit.SampleItemId && item.unitCount > 0);

        private void DrawBlood(ClinicCase visit, GameItem syringe)
        {
            var entry = EmporiumEntry.Instance;
            var counter = entry?.frontInvinvElement;
            var existing = visit.SampleRevision == visit.BloodRevision ? FindBloodSample(visit) : null;
            if (!CanUse(syringe)) status = "请使用库存内的采样针管。";
            else if (counter == null) status = "前柜台尚不可用。";
            else if (existing != null && existing.parentInventory == counter)
                status = "本阶段的血样瓶在前柜台。";
            else if (existing != null)
                status = TryPlaceBloodSample(counter, existing)
                    ? "本阶段的血样瓶已移到前柜台。"
                    : "血样瓶仍在原库存。请在前柜台腾出两格。";
            else
            {
                var sample = DirectoryMaster.Item(BloodSample, true);
                if (sample == null || sample.identifier != BloodSample || sample.uniqueId == 0)
                {
                    sample?.Destroy();
                    status = "无法生成血样瓶。";
                }
                else if (!TryPlaceBloodSample(counter, sample))
                {
                    sample.Destroy();
                    status = "前柜台放不下血样瓶，请腾出两格后再抽血。";
                }
                else
                {
                    existing = sample;
                    visit.SampleItemId = sample.uniqueId;
                    visit.SampleRevision = visit.BloodRevision;
                    status = "已抽血。血样瓶在前柜台。";
                    Save();
                }
            }

            if (counter != null && existing?.parentInventory == counter)
            {
                if (entry.frontInvWindow?.IsVisible() == false) entry.ShowFrontWindow();
                OverlayHandler.current?.UpdateInventoryStateUI();
            }
            log.Log($"抽血：{status}");
            if (TreatmentOpen) ShowBody(visit);
        }

        private static bool TryPlaceBloodSample(GameGridInventory counter, GameItem sample)
        {
            var slot = counter.TryFindOneValidInventorySlot_TopMiddle(sample, keepOrientation: true);
            if (slot == null || slot.targetItem != null || slot.inventory != counter) return false;

            var origin = sample.parentInventory;
            var placement = sample.modifiedShape;
            if (slot.TryAcceptOnce(1) == 1 && sample.parentInventory == counter &&
                counter.childItems.Contains(sample)) return true;

            if (origin != null && sample.parentInventory == null)
            {
                sample.modifiedShape = placement;
                origin.UncheckedAccept(sample);
            }
            return false;
        }

        private void AnalyseBloodSample(GameItem sample, GameItem analyzer)
        {
            if (!IsLoaded) return;
            var visit = state.Current;
            if (visit == null || !IsCurrent(visit)) return;
            if (visit.SampleItemId != sample.uniqueId || !CanUse(sample) || !CanUse(analyzer))
            {
                status = "请使用当前患者的血样瓶和库存内的分析仪。";
                if (TreatmentOpen) ShowBody(visit);
                return;
            }
            if (!visit.ScanSample(sample.uniqueId))
            {
                status = "血样已过期，请重新抽血。";
                if (TreatmentOpen) ShowBody(visit);
                return;
            }

            status = "血样分析完成。";
            Save();
            if (TreatmentOpen) ShowBody(visit);
            OpenBloodReport(sample, analyzer);
        }

        private void ReopenBloodReport(ClinicCase visit)
        {
            var sample = FindBloodSample(visit);
            var analyzer = store.FindAllItem(true).FirstOrDefault(item => item?.identifier == Analyzer && IsRemovable(item));
            if (sample != null && analyzer != null && visit.ReportRevision >= 0 && visit.ReportRevision == visit.SampleRevision)
                OpenBloodReport(sample, analyzer);
        }

        private static void OpenBloodReport(GameItem sample, GameItem analyzer)
        {
            var scanner = ChemScannerUIManager.Instance;
            if (scanner == null) return;
            if (scanner.panel.activeSelf) scanner.Close();
            scanner.Open(analyzer, sample);
        }

        private static void CloseBloodReport()
        {
            var scanner = ChemScannerUIManager.Instance;
            if (scanner != null && scanner.panel.activeSelf && scanner.currentScannedItem?.identifier == BloodSample)
                scanner.Close();
        }

        internal bool RenderBloodReport(ChemScannerUIManager scanner)
        {
            var visit = state?.Current;
            var sample = scanner.currentScannedItem;
            if (sample?.identifier != BloodSample) return false;

            scanner.productNameTMP.text = "血液分析";
            var hasReport = visit?.SampleItemId == sample.uniqueId && visit.ReportRevision >= 0 &&
                visit.ReportRevision == visit.SampleRevision;
            scanner.testedTMP.text = "";
            scanner.labelTMP.text = "";
            scanner.contaminantTMP.text = hasReport ? string.Join("\n",
                BloodResult("B1", visit.ReportPositive),
                BloodResult("BL1", visit.ReportBloodLoss),
                BloodResult("F1", visit.ReportFungus),
                BloodResult("T1", visit.ReportToxin),
                BloodResult("O1", visit.ReportOverdose),
                BloodResult("S1", visit.ReportSentinel),
                BloodResult("A1", visit.ReportDependence)) : "";
            scanner.totalVolumeTMP.text = "";
            scanner.confrontButton.gameObject.SetActive(false);
            scanner.matchButton.gameObject.SetActive(false);
            scanner.euphoriaEffect.gameObject.SetActive(false);
            scanner.toxicityEffect.gameObject.SetActive(false);
            scanner.synaprestEffect.gameObject.SetActive(false);
            scanner.birthControlEffect.gameObject.SetActive(false);
            return true;
        }

        private static string BloodResult(string code, bool positive) =>
            $"{code}　{(positive ? "阳性" : "阴性")}";

        private void CloseClinicPanels()
        {
            var manager = CustomUIManager.Instance;
            CloseBloodReport();
            manager?.CloseWindow(HandbookWindow);
            ClearPharmacyOffers();
        }
    }

    [HarmonyPatch(typeof(ChemScannerUIManager), nameof(ChemScannerUIManager.RefreshUI))]
    internal static class ClinicBloodScannerPatch
    {
        private static bool Prefix(ChemScannerUIManager __instance) =>
            ModEntry.Active?.RenderBloodReport(__instance) != true;
    }
}
