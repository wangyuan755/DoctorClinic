using HarmonyLib;
using TMPro;

namespace DoctorClinic
{
    internal static class ClinicText
    {
        internal static string T(string key, params object[] args) => ModHelper.GetLocalized(ModEntry.ModId, key, args);

        internal static string DifficultyLabel(Difficulty value) => T(value == Difficulty.困难 ? "clinic_difficulty_hard" : "clinic_difficulty_easy");

        internal static string ReputationReason(string reason)
        {
            // Older saves stored the Chinese label. Preserve the value of any unknown entry.
            var key = reason switch
            {
                null => "clinic_none",
                "医疗事故死亡" => "clinic_reason_fatal_medical_incident",
                "治疗造成损伤" => "clinic_reason_treatment_injury",
                "完成患者诊疗" => "clinic_reason_completed_treatment",
                "加入医师协会" => "clinic_reason_joined_association",
                "通过王尔德取得医生执照" => "clinic_reason_wilde_medical_license",
                "调试菜单调整" => "clinic_reason_debug_adjustment",
                "患者失去耐心离开" => "clinic_reason_patient_walkout",
                _ => reason
            };
            return key.StartsWith("clinic_", System.StringComparison.Ordinal) ? T(key) : reason;
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), "BuildEntry")]
    internal static class ClinicDifficultyLabelsPatch
    {
        private static void Postfix(CustomUIBuilder b, ConfigEntry entry)
        {
            if (ModEntry.Active?.IsClinicDifficultySetting(entry) != true) return;
            var dropdown = b.Last?.Rect.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown == null || dropdown.options.Count != 2) return;
            // Keep the game's original enum choices for config serialization. Only change their labels.
            dropdown.options[0].text = ClinicText.DifficultyLabel(Difficulty.简单);
            dropdown.options[1].text = ClinicText.DifficultyLabel(Difficulty.困难);
            dropdown.RefreshShownValue();
        }
    }
}
