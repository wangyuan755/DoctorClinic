namespace DoctorClinic
{
    internal sealed class ClinicPenalty
    {
        public int DoctorLoss { get; private set; }
        public int SecurityLoss { get; private set; }
        public int UpperLoss { get; private set; }
        public int Investigation { get; private set; }
        public int Compensation { get; private set; }
        public bool RevokeLicense { get; private set; }
        public bool UsesAid { get; private set; }
        public int CommissionPayment { get; private set; }
        public int CommissionReputation { get; private set; }

        public static ClinicPenalty Death(bool poisoned, bool delayed, bool contract,
            int serviceFees, bool whiteGlove, bool aid)
        {
            var result = new ClinicPenalty
            {
                DoctorLoss = delayed ? (contract ? 7 : 20) : 30,
                SecurityLoss = delayed ? (contract ? 7 : 10) : 20,
                Compensation = delayed ? 0 : serviceFees * 5,
                RevokeLicense = !delayed,
                CommissionPayment = contract ? 500 : 0,
                CommissionReputation = contract ? 10 : 0
            };
            if (poisoned) result.UpperLoss = result.SecurityLoss;
            if (whiteGlove && poisoned)
            {
                if (delayed) result.DoctorLoss = 3;
                result.SecurityLoss = 0;
                result.Investigation = 0;
            }
            if (aid)
            {
                result.UsesAid = true;
                result.DoctorLoss = 0;
                result.SecurityLoss = 0;
                result.Investigation = 0;
                result.Compensation = 0;
                result.UpperLoss = 0;
                result.RevokeLicense = false;
            }
            return result;
        }
    }
}
