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
                log.Warning("原版交易面板尚未初始化。");
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
            frame.clientName.text = string.IsNullOrEmpty(visit.PatientName) ? "诊疗结算" : visit.PatientName;
            frame.clientSubtitle.text = string.IsNullOrEmpty(visit.Faction) ? "诊疗结算" : FactionName(visit.Faction) + " · 诊疗结算";
            frame.sectionHeader.text = visit.Dead
                ? "患者已死亡"
                : visit.PatientWalkedOut ? "患者失去耐心，已离开"
                : visit.Wounds.Any(wound => wound.Covered && wound.Kind == ConditionKind.SentinelCollapse) ? "已完成治疗" : "已治愈";
            var care = visit.Wounds.FirstOrDefault(wound => wound.IsCare);
            if (care != null) frame.clientSubtitle.text += $" · {care.Name}";
            if (visit.Practice || visit.Tutorial) frame.clientSubtitle.text += " · 练习";
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
                    () => $"材料 {line.Material}　基础 {line.Service}　难度 {line.DifficultyBonus}\n完美 {line.QualityBonus}　小费 {line.Tip}　折扣 −{line.Discount}",
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
                TabletUI.TextRow(frame, frame.content, "尚无已治愈的伤病", 18, TabletStyle.INK);
            var infoHeight = 0f;
            if (difficulty == Difficulty.困难 && (!visit.Settled || visit.PatientWalkedOut))
            {
                TabletUI.InfoLine(frame, frame.infoHost, infoHeight, "患者耐心", visit.Patience.ToString());
                infoHeight += 32f;
            }
            if (visit.Settled)
            {
                var incident = state.Incidents.LastOrDefault(entry => entry.CaseId == visit.Id);
                if (incident != null)
                {
                    TabletUI.InfoLine(frame, frame.infoHost, infoHeight, $"医疗事故 · 医生声望 −{incident.DoctorLoss}",
                        $"赔偿 {incident.Compensation} · 未偿 {state.MedicalDebt}");
                    infoHeight += 32f;
                }
            }
            var commission = CommissionFor(visit.Id);
            if (visit.Settled && commission?.Status == CommissionStatus.Completed && !commission.Assassination)
            {
                TabletUI.InfoLine(frame, frame.infoHost, infoHeight, "拒诊委托报酬", TabletUI.Credits(commission.Payment));
                infoHeight += 32f;
            }
            TabletUI.LayoutSection(frame, infoHeight, true, 432);
            var payable = bill.Sum(line => line.Payable);
            var discount = bill.Sum(line => line.Discount);
            var auxiliary = bill.Sum(line => line.AuxiliaryPayable);
            var practice = visit.Practice || visit.Tutorial;
            var cost = bill.Sum(line => line.EstimatedCost);
            TabletUI.FooterLine(frame, frame.footer, 0, "患者预算", TabletUI.Credits(visit.Budget));
            var total = TabletUI.FooterLine(frame, frame.footer, 35, visit.Settled ? "实收" : practice ? "演练估价" : "预计实收",
                TabletUI.Credits(payable));
            var totalRow = total.label.transform.parent.gameObject;
            var hoverArea = totalRow.AddComponent<Image>();
            hoverArea.color = Color.clear;
            TabletUI.AddTooltip(totalRow,
                () => $"折扣 {visit.NativeDiscountPercent}% · −{TabletUI.Credits(discount)}\n" +
                      $"镇痛／麻醉辅助 {TabletUI.Credits(visit.Settled && practice ? 0 : auxiliary)}\n" +
                      $"净收益 {TabletUI.Credits(payable - cost)}");
            if (!visit.Settled)
            {
                TabletUI.BigButton(frame, frame.buttonHost, 12, 0, "开始治疗", () => BeginTreatment(visit), 244);
                TabletUI.BigButton(frame, frame.buttonHost, 274, 0, "结束并结算", () => Finish(visit, true, playerReferral: true), 244);
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
                    ? (wound.IsPerfect ? "完美维护" : "普通维护")
                    : (wound.IsPerfect ? "完美完成" : "普通完成"));
            else if (difficulty == Difficulty.简单)
                ui.AddLabel(wound.RequiresBloodTest
                    ? (wound.Diagnosed ? "待治疗" : "待治疗，可用血检辅助诊断")
                    : Progress(wound));
            if (wound.Harm > 0) ui.AddWarning("治疗造成损伤。");
            if (wound.Kind == ConditionKind.Abrasion || wound.Kind == ConditionKind.ShallowCut ||
                wound.Kind == ConditionKind.Burn || wound.Kind == ConditionKind.Contusion || wound.ComplexWound)
                AddAbrasionArt(ui, wound);
            if (wound.Kind == ConditionKind.OximorDependence && !wound.Covered &&
                !visit.RequiresAddictionCure(wound) && !wound.Hidden && TreatmentOpen)
                ui.AddButton("提议用赛纳普斯根除", () => ProposeAddictionCure(visit, index));

            if (wound.Hidden && visit.HiddenBudget == 0 && TreatmentOpen)
                ui.AddButton($"报价并取得同意（上限 {visit.HiddenQuote}）", () =>
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
            var location = wound.RequiresBloodTest || wound.IsCare ? "全身" : BodyChart.Names[wound.Region ?? index];
            return $"{location}，{wound.Name}";
        }

        private static string TreatmentLineName(ClinicCase visit, int index)
        {
            var wound = visit.Wounds[index];
            var result = wound.Kind == ConditionKind.SentinelCollapse ? (wound.IsPerfect ? "完美维护" : "普通维护")
                : wound.IsCare ? "护理完成"
                : wound.Kind == ConditionKind.OximorDependence ? (visit.RequiresAddictionCure(wound) ? "根除完成" : "症状缓解")
                : wound.UsesMedicineOnlyPricing ? "治疗完成"
                : wound.IsPerfect ? "完美治疗" : "普通治疗";
            return $"{BillingName(visit, index)}，{result}";
        }
    }
}
