namespace DoctorClinic
{
    internal static class ClinicPenaltyPreview
    {
        private const string WindowId = "doctor_clinic_penalty_preview";
        private static int eventType;
        private static bool contract;
        private static bool whiteGlove;
        private static bool aid;

        public static void Show()
        {
            if (CustomUIManager.Instance == null) return;
            Close();
            var result = ClinicPenalty.Death(eventType != 0, eventType == 2, contract, 100, whiteGlove, aid);
            var ui = CustomUIManager.Window(WindowId, "事故处罚预览").SetSize(520, 410);
            ui.AddLabel("模拟上层患者的一项伤病，基础服务费 100。");
            ui.AddDropdown(new[] { "即时误治死亡", "即时投毒死亡", "延迟投毒死亡" },
                eventType, value => { eventType = value; Show(); });
            ui.AddToggle("有效合同", contract, value => { contract = value; Show(); });
            ui.AddToggle("白手套", whiteGlove, value => { whiteGlove = value; Show(); });
            ui.AddToggle("法律援助", aid, value => { aid = value; Show(); });
            ui.AddLabel($"医生扣分 {result.DoctorLoss}   治安扣分 {result.SecurityLoss}   调查增加 {result.Investigation}");
            ui.AddLabel($"赔偿 {result.Compensation}   吊销：{(result.RevokeLicense ? "是" : "否")}   消耗援助：{(result.UsesAid ? "是" : "否")}");
            ui.AddLabel($"委托奖金 {result.CommissionPayment}   委托方声望 +{result.CommissionReputation}");
            ui.AddCloseButton(Close);
            ui.Show();
        }

        public static void Close() => CustomUIManager.Instance?.CloseWindow(WindowId);
    }
}
