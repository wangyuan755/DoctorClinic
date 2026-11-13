using static DoctorClinic.ClinicText;
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string WoundDetailsWindow = "doctor_clinic_wound_details";
        private int woundDetailsIndex = -1;
        private ClinicCase settlementVisit;
        private BillLine[] pendingBill;

        private bool SettlementOpen => settlementVisit != null && NegociationUIManager.Instance?.IsOpen == true;

        private void CloseSettlement()
        {
            if (settlementVisit == null) return;
            ClearSettlement();
            NegociationUIManager.Instance?.CloseUI();
        }

        internal void ClearSettlement()
        {
            settlementVisit = null;
            pendingBill = null;
        }

        private void ShowSettlement(ClinicCase visit, BillLine[] bill)
        {
            var native = NegociationUIManager.Instance;
            if (native == null || native.uiObject == null)
            {
                log.Warning(T("clinic_the_base_game_s_trade_panel_has"));
                return;
            }

            settlementVisit = visit;
            pendingBill = bill;
            native.OpenUI();
        }

        internal void BindPatientTabletOnOpen()
        {
            var visit = state?.Current;
            if (settlementVisit != null || visit == null || !visit.Arrived || !IsCurrent(visit) ||
                boundBuyer == null || store.currentClientInstance?.GetClientBlueprint() != boundBuyer) return;
            settlementVisit = visit;
            pendingBill = visit.Preview();
        }

        internal void RenderPendingPatientTablet(NegociationUIManager native, NegotiationTabletFrame frame)
        {
            if (pendingBill != null) RefreshSettlement(native, frame);
        }

        private void RefreshOpenSettlement(ClinicCase visit)
        {
            if (!SettlementOpen || settlementVisit != visit) return;
            var native = NegociationUIManager.Instance;
            var frame = native.uiObject?.GetComponent<NegotiationTabletFrame>();
            pendingBill = visit.Preview();
            if (frame != null) RefreshSettlement(native, frame);
        }

        internal bool RefreshSettlement(NegociationUIManager native, NegotiationTabletFrame frame)
        {
            var visit = settlementVisit;
            if (visit == null || visit != state?.Current || frame == null || !native.IsOpen) return false;
            RefreshClinicBargainControl(visit, frame);
            if (pendingBill != null)
            {
                var bill = pendingBill;
                pendingBill = null;
                RenderSettlement(frame, visit, bill);
            }
            return true;
        }

        private void RefreshClinicBargainControl(ClinicCase visit, NegotiationTabletFrame frame)
        {
            var bargain = BargainUIManager.Instance;
            var available = IsCurrent(visit) && boundBuyer != null &&
                store.currentClientInstance?.GetClientBlueprint() == boundBuyer && boundBuyer.isBargainAvailable &&
                MapUIManager.Instance?.isOutside == false && store.isBargainIntroduced && !StartingPerk.IsAwfulNegotiator();
            if (!available && bargain?.IsOpen == true) bargain.CloseUI();
            frame.bargainButton.gameObject.SetActive(available);

            var open = available && bargain?.IsOpen == true;
            if (frame.artBargainOpen != null && frame.artNormal != null)
                frame.art.sprite = open ? frame.artBargainOpen : frame.artNormal;
            frame.bargainImage.sprite = open ? frame.bargainActive : frame.bargainNormal;

            var sprites = frame.bargainButton.spriteState;
            sprites.highlightedSprite = open ? frame.bargainActive : frame.bargainHover;
            sprites.pressedSprite = open ? frame.bargainActive : frame.bargainPressed;
            frame.bargainButton.spriteState = sprites;

            var canvas = frame.bargainImage.canvas;
            var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var hovered = available && frame.bargainButton.transform is RectTransform rect &&
                RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
            frame.bargainImage.overrideSprite = open ? frame.bargainActive : hovered ? frame.bargainHover : null;
        }

        private void RenderSettlement(NegotiationTabletFrame frame, ClinicCase visit, BillLine[] bill)
        {
            var scrollPosition = frame.scroll.verticalNormalizedPosition;
            frame.clientName.text = string.IsNullOrEmpty(visit.PatientName) ? T("clinic_treatment_bill") : visit.PatientName;
            frame.clientSubtitle.text = string.IsNullOrEmpty(visit.Faction) ? T("clinic_treatment_bill") : FactionName(visit.Faction) + T("clinic_treatment_bill_2");
            frame.sectionHeader.text = visit.Dead
                ? T("clinic_patient_deceased")
                : visit.PatientWalkedOut ? T("clinic_patient_lost_patience_and_left")
                : visit.Wounds.Any(wound => wound.Covered && wound.Kind == ConditionKind.SentinelCollapse) ? T("clinic_treatment_completed") : T("clinic_treated");
            var care = visit.Wounds.FirstOrDefault(wound => wound.IsCare);
            if (care != null) frame.clientSubtitle.text += $" · {care.Name}";
            if (visit.Practice || visit.Tutorial) frame.clientSubtitle.text += T("clinic_practice");
            TabletUI.ClearChildren(frame.content);
            TabletUI.ClearChildren(frame.infoHost);
            TabletUI.ClearChildren(frame.footer);
            TabletUI.ClearChildren(frame.buttonHost);

            var completed = 0;
            for (var i = 0; i < bill.Length; i++)
            {
                var index = i;
                if (!visit.Wounds[index].Covered) continue;
                completed++;
                var line = bill[index];
                var row = TabletUI.ListRow(frame, frame.content, TreatmentLineName(visit, index),
                    TabletUI.Credits(WoundPayable(visit, line)), false,
                    () => T("clinic_supplies_base_fee_difficulty_perfect_bonus_tip", line.Material, line.Service, line.DifficultyBonus, line.QualityBonus, line.Tip, line.Discount),
                    visit.Settled ? null : (Action)(() => ShowWoundDetails(visit, index)));
                var visuals = row.transform.Find("Visuals");
                var layout = row.GetComponent<LayoutElement>();
                layout.minHeight = layout.preferredHeight = 54;
                WildNetworkUI.SetRect((RectTransform)visuals, 0, 0, 502, 54);
                var amount = visuals.Find("Right").GetComponent<TMPro.TextMeshProUGUI>();
                amount.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                WildNetworkUI.SetRect(amount.rectTransform, 12, 24, 480, 28);
                WildNetworkUI.SetRect((RectTransform)visuals.Find("Separator"), 0, 52, 502, 2);
            }
            if (completed == 0)
                TabletUI.TextRow(frame, frame.content, T("clinic_no_conditions_treated_yet"), 18, TabletStyle.INK);
            var infoHeight = 0f;
            if (difficulty == Difficulty.困难 && (!visit.Settled || visit.PatientWalkedOut))
            {
                TabletUI.InfoLine(frame, frame.infoHost, infoHeight, T("clinic_patient_patience"), visit.Patience.ToString());
                infoHeight += 32f;
            }
            if (visit.Settled)
            {
                var incident = state.Incidents.LastOrDefault(entry => entry.CaseId == visit.Id);
                if (incident != null)
                {
                    TabletUI.InfoLine(frame, frame.infoHost, infoHeight, T("clinic_medical_incident_doctor_reputation", incident.DoctorLoss),
                        T("clinic_compensation_outstanding", incident.Compensation, state.MedicalDebt));
                    infoHeight += 32f;
                }
            }
            var commission = CommissionFor(visit.Id);
            if (visit.Settled && commission?.Status == CommissionStatus.Completed && !commission.Assassination)
            {
                TabletUI.InfoLine(frame, frame.infoHost, infoHeight, T("clinic_refusal_contract_payment"), TabletUI.Credits(commission.Payment));
                infoHeight += 32f;
            }
            TabletUI.LayoutSection(frame, infoHeight, true, 432);
            var payable = bill.Sum(line => line.Payable);
            var discount = bill.Sum(line => line.Discount);
            var auxiliary = bill.Sum(line => line.AuxiliaryPayable);
            var practice = visit.Practice || visit.Tutorial;
            var cost = bill.Sum(line => line.EstimatedCost);
            TabletUI.FooterLine(frame, frame.footer, 0, T("clinic_patient_budget"), TabletUI.Credits(visit.Budget));
            var total = TabletUI.FooterLine(frame, frame.footer, 35, visit.Settled ? T("clinic_paid") : practice ? T("clinic_practice_estimate") : T("clinic_estimated_payment"),
                TabletUI.Credits(payable));
            var totalRow = total.label.transform.parent.gameObject;
            var hoverArea = totalRow.AddComponent<Image>();
            hoverArea.color = Color.clear;
            TabletUI.AddTooltip(totalRow,
                () => T("clinic_bill_discount_tooltip", visit.NativeDiscountPercent, TabletUI.Credits(discount)) +
                      T("clinic_bill_anesthesia_tooltip", TabletUI.Credits(visit.Settled && practice ? 0 : auxiliary)) +
                      T("clinic_net_earnings", TabletUI.Credits(payable - cost)));
            if (!visit.Settled)
            {
                TabletUI.BigButton(frame, frame.buttonHost, 12, 0, T("clinic_begin_treatment"), () => BeginTreatment(visit), 244);
                TabletUI.BigButton(frame, frame.buttonHost, 274, 0, T("clinic_end_settle"), () => Finish(visit, true, playerReferral: true), 244);
            }
            Canvas.ForceUpdateCanvases();
            frame.scroll.verticalNormalizedPosition = float.IsNaN(scrollPosition) ? 1 : scrollPosition;
        }

        private void BeginTreatment(ClinicCase visit)
        {
            if (!IsCurrent(visit) || visit.Dead) return;
            if (TreatmentOpen) CustomUIManager.Instance.GetWindow(BodyWindowId)?.BringToFront();
            else ShowBody(visit);
        }

        private static int WoundPayable(ClinicCase visit, BillLine line) =>
            visit.Settled && (visit.Practice || visit.Tutorial) ? 0 : line.Payable - line.AuxiliaryPayable;

        private void ShowWoundDetails(ClinicCase visit, int index)
        {
            var manager = CustomUIManager.Instance;
            if (!IsCurrent(visit) || manager == null || index < 0 || index >= visit.Wounds.Length) return;
            var wound = visit.Wounds[index];
            if (wound.Hidden && !wound.Diagnosed) return;
            var position = manager.GetWindow(WoundDetailsWindow)?.Rect.anchoredPosition;
            manager.CloseWindow(WoundDetailsWindow);
            woundDetailsIndex = index;
            var ui = CustomUIManager.Window(WoundDetailsWindow, BillingName(visit, index)).SetSize(440, 340).SetDraggable(true);
            if (position.HasValue) ui.SetPosition(position.Value);
            if (wound.Covered)
                ui.AddLabel(wound.Kind == ConditionKind.SentinelCollapse
                    ? (wound.IsPerfect ? T("clinic_perfect_maintenance") : T("clinic_standard_maintenance"))
                    : (wound.IsPerfect ? T("clinic_perfect_result") : T("clinic_standard_result")));
            else if (difficulty == Difficulty.简单)
                ui.AddLabel(wound.RequiresBloodTest
                    ? (wound.Diagnosed ? T("clinic_awaiting_treatment") : T("clinic_awaiting_treatment_a_blood_test_can_help"))
                    : Progress(wound));
            if (wound.Harm > 0) ui.AddWarning(T("clinic_the_treatment_caused_an_injury"));
            if (wound.IsInjury && !wound.RequiresBloodTest)
                AddWoundArt(ui, visit, index);
            if (wound.Kind == ConditionKind.OximorDependence && !wound.Covered &&
                !visit.RequiresAddictionCure(wound) && !wound.Hidden && TreatmentOpen)
                ui.AddButton(T("clinic_propose_synaprest_treatment"), () => ProposeAddictionCure(visit, index));

            if (wound.Hidden && visit.HiddenBudget == 0 && TreatmentOpen)
                ui.AddButton(T("clinic_quote_and_request_consent_up_to", visit.HiddenQuote), () =>
                {
                    if (!IsCurrent(visit) || !wound.Diagnosed || visit.HiddenBudget > 0 || !TreatmentOpen) return;
                    visit.HiddenBudget = visit.HiddenQuote;
                    Save();
                    manager.CloseWindow(WoundDetailsWindow);
                    RefreshVisitPanels(visit);
                });
            ui.AddCloseButton(() => manager.CloseWindow(WoundDetailsWindow));
            ui.Show();
        }

        private static string BillingName(ClinicCase visit, int index)
        {
            var wound = visit.Wounds[index];
            var location = wound.RequiresBloodTest || wound.IsCare ? T("clinic_systemic") : BodyChart.RegionName(wound.Region ?? index);
            return T("clinic_bill_wound", location, wound.Name);
        }

        private static string TreatmentLineName(ClinicCase visit, int index)
        {
            var wound = visit.Wounds[index];
            var result = wound.Kind == ConditionKind.SentinelCollapse ? (wound.IsPerfect ? T("clinic_perfect_maintenance") : T("clinic_standard_maintenance"))
                : wound.IsCare ? T("clinic_care_complete")
                : wound.Kind == ConditionKind.OximorDependence ? (visit.RequiresAddictionCure(wound) ? T("clinic_dependence_treated") : T("clinic_temporary_relief"))
                : wound.UsesMedicineOnlyPricing ? T("clinic_treatment_complete")
                : wound.IsPerfect ? T("clinic_perfect_treatment") : T("clinic_standard_treatment");
            return T("clinic_bill_result", BillingName(visit, index), result);
        }
    }
}
