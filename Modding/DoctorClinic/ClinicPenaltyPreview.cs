using static DoctorClinic.ClinicText;
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
            var ui = CustomUIManager.Window(WindowId, T("clinic_incident_penalty_preview")).SetSize(520, 410);
            ui.AddLabel(T("clinic_simulates_one_condition_in_an_upper_levels"));
            ui.AddDropdown(new[] { T("clinic_immediate_death_from_malpractice"), T("clinic_immediate_death_from_poisoning"), T("clinic_delayed_death_from_poisoning") },
                eventType, value => { eventType = value; Show(); });
            ui.AddToggle(T("clinic_active_contract"), contract, value => { contract = value; Show(); });
            ui.AddToggle(T("clinic_white_glove"), whiteGlove, value => { whiteGlove = value; Show(); });
            ui.AddToggle(T("clinic_legal_aid"), aid, value => { aid = value; Show(); });
            ui.AddLabel(T("clinic_doctor_reputation_security_reputation_investigation", result.DoctorLoss, result.SecurityLoss, result.Investigation));
            ui.AddLabel(T("clinic_compensation_license_revoked_legal_aid_used", result.Compensation, (result.RevokeLicense ? T("clinic_yes") : T("clinic_no")), (result.UsesAid ? T("clinic_yes") : T("clinic_no"))));
            ui.AddLabel(T("clinic_contract_bonus_client_faction_reputation", result.CommissionPayment, result.CommissionReputation));
            ui.AddCloseButton(Close);
            ui.Show();
        }

        public static void Close() => CustomUIManager.Instance?.CloseWindow(WindowId);
    }
}
