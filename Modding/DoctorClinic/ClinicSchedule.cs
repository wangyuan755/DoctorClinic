using static DoctorClinic.ClinicText;
using System.Collections.Generic;

namespace DoctorClinic
{
    internal sealed class ClinicSchedule
    {
        public int Day { get; set; }
        public int ReputationHalfPoints { get; set; }
        public List<ClinicCase> Patients { get; set; } = new List<ClinicCase>();
    }

    internal static class ClinicReputation
    {
        public const int Established = 20;
        public const int SundaySupply = 40;
        public const int Trusted = 60;
        public const int Renowned = 80;

        public static string Title(int halfPoints)
        {
            if (halfPoints <= -200) return T("clinic_citizen_s_tribunal");
            if (halfPoints <= -120) return T("clinic_notorious");
            if (halfPoints <= -40) return T("clinic_under_scrutiny");
            if (halfPoints < Established * 2) return T("clinic_neighborhood_doctor");
            if (halfPoints < Trusted * 2) return T("clinic_up_and_coming");
            if (halfPoints < Renowned * 2) return T("clinic_trusted");
            return T("clinic_renowned_doctor");
        }

        public static bool HasWeeklySupply(int day, int halfPoints) =>
            day % 7 == 3 || (day % 7 == 0 && halfPoints >= SundaySupply * 2);

        public static int PharmacyDiscount(int halfPoints) =>
            halfPoints >= Renowned * 2 ? 15 : halfPoints >= Trusted * 2 ? 10 : halfPoints >= Established * 2 ? 5 : 0;

        public static bool CanSeeUpper(int halfPoints) => halfPoints > -40;

        public static int ExternalChance(PatientTier tier) => tier switch
        {
            PatientTier.Upper => 20,
            PatientTier.Security => 80,
            _ => 60
        };

        public static int DailyPatients(int halfPoints, bool extra)
        {
            if (halfPoints <= -200) return 0;
            if (halfPoints >= Renowned * 2) return extra ? 3 : 2;
            if (halfPoints >= Trusted * 2) return 2;
            if (halfPoints >= Established * 2) return extra ? 2 : 1;
            return 1;
        }

        public static int MaxConditions(int successfulVisits) => successfulVisits >= 15 ? 3 :
            successfulVisits >= 5 ? 2 : 1;
    }
}
