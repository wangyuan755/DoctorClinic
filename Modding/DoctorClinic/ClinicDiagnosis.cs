using static DoctorClinic.ClinicText;
using System.Linq;
using HarmonyLib;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string BloodSample = "doctor_clinic_blood_sample";
        private const string BloodSampleSprite = "blood_sample_filled";

        private GameItem CreateBloodSample()
        {
            var item = new GameItemElement(BloodSample, "Items/items_material", "med1", null);
            item.name = T("clinic_blood_sample_vial");
            item.shortDescription = T("clinic_drag_onto_the_blood_analyzer");
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
            if (!CanUse(syringe)) status = T("clinic_use_a_sampling_syringe_from_your_inventory");
            else if (counter == null) status = T("clinic_the_front_counter_is_unavailable");
            else if (existing != null && existing.parentInventory == counter)
                status = T("clinic_the_current_blood_sample_is_on_the");
            else if (existing != null)
                status = TryPlaceBloodSample(counter, existing)
                    ? T("clinic_moved_the_current_blood_sample_to_the")
                    : T("clinic_the_vial_is_still_in_its_original");
            else
            {
                var sample = DirectoryMaster.Item(BloodSample, true);
                if (sample == null || sample.identifier != BloodSample || sample.uniqueId == 0)
                {
                    sample?.Destroy();
                    status = T("clinic_could_not_create_a_blood_sample_vial");
                }
                else if (!TryPlaceBloodSample(counter, sample))
                {
                    sample.Destroy();
                    status = T("clinic_clear_two_slots_on_the_front_counter");
                }
                else
                {
                    existing = sample;
                    visit.SampleItemId = sample.uniqueId;
                    visit.SampleRevision = visit.BloodRevision;
                    status = T("clinic_blood_drawn_the_vial_is_on_the");
                    Save();
                }
            }

            if (counter != null && existing?.parentInventory == counter)
            {
                if (entry.frontInvWindow?.IsVisible() == false) entry.ShowFrontWindow();
                OverlayHandler.current?.UpdateInventoryStateUI();
            }
            log.Log(T("clinic_blood_draw", status));
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
                status = T("clinic_use_the_current_patient_s_blood_sample");
                if (TreatmentOpen) ShowBody(visit);
                return;
            }
            if (!visit.ScanSample(sample.uniqueId))
            {
                status = T("clinic_this_sample_is_no_longer_valid_draw");
                if (TreatmentOpen) ShowBody(visit);
                return;
            }

            status = T("clinic_blood_analysis_complete");
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

            scanner.productNameTMP.text = T("clinic_blood_analysis");
            var hasReport = visit?.SampleItemId == sample.uniqueId && visit.ReportRevision >= 0 &&
                visit.ReportRevision == visit.SampleRevision;
            scanner.testedTMP.text = "";
            scanner.labelTMP.text = "";
            scanner.contaminantTMP.text = hasReport ? string.Join("\n",
                BloodResult("bacterial", visit.ReportPositive),
                BloodResult("blood_loss", visit.ReportBloodLoss),
                BloodResult("spore", visit.ReportFungus),
                BloodResult("toxin", visit.ReportToxin),
                BloodResult("overdose", visit.ReportOverdose),
                BloodResult("sentinel", visit.ReportSentinel),
                BloodResult("dependence", visit.ReportDependence)) : "";
            scanner.totalVolumeTMP.text = "";
            scanner.confrontButton.gameObject.SetActive(false);
            scanner.matchButton.gameObject.SetActive(false);
            scanner.euphoriaEffect.gameObject.SetActive(false);
            scanner.toxicityEffect.gameObject.SetActive(false);
            scanner.synaprestEffect.gameObject.SetActive(false);
            scanner.birthControlEffect.gameObject.SetActive(false);
            return true;
        }

        private static string BloodResult(string marker, bool positive) =>
            T("clinic_lab_result", T("clinic_lab_" + marker), T(positive ? "clinic_positive" : "clinic_negative"));

        private void CloseClinicPanels()
        {
            CloseBloodReport();
            handbookPaper?.Close();
            handbookPaper = null;
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
