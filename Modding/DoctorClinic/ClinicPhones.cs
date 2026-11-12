using System;
using System.Linq;
using HarmonyLib;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string MedicalPhone = "doctor_clinic_medical_phone";
        private const string IllegalPhone = "doctor_clinic_illegal_phone";
        private const int PharmacyCooldownDays = 7;

        internal static string ClinicPhoneName(string id) => id switch
        {
            MedicalPhone => "正规医药协会",
            IllegalPhone => "非法医药商",
            _ => null
        };

        private void SyncClinicPhones()
        {
            if (!IsLoaded || store.PhoneClientDict == null) return;
            SyncClinicPhone(MedicalPhone, state.MedicalChannel, state.MedicalCallReadyDay, 54615);
            SyncClinicPhone(IllegalPhone, state.IllegalMedicalChannel, state.IllegalMedicalCallReadyDay, 54616);
        }

        private void SyncClinicPhone(string id, bool unlocked, int readyDay, long preferredNumber)
        {
            var contacts = store.PhoneClientDict;
            var existing = contacts.FirstOrDefault(entry => entry.Value?.dialogFuncId == id);
            if (!unlocked)
            {
                if (existing.Value != null) contacts.Remove(existing.Key);
                return;
            }

            PhoneClientList.DialogFuncs[id] = contact => ClinicPharmacyCall(id == IllegalPhone, contact);
            var phone = existing.Value;
            if (phone == null)
            {
                var number = preferredNumber;
                while (contacts.ContainsKey(number)) number++;
                phone = new StorePhoneClient { dialogFuncId = id, displayName = ClinicPhoneName(id),
                    locID = id, phoneClientType = StorePhoneClient.PhoneClientType.Supplier,
                    cooldownDuration = PharmacyCooldownDays, dialedBefore = true };
                contacts.Add(number, phone);
            }
            phone.currentCooldown = Math.Max(0, readyDay - Today);
            phone.phoneState = phone.currentCooldown > 0 ? StorePhoneClient.PhoneState.Cooldown : StorePhoneClient.PhoneState.Regular;
        }

        private void RemoveClinicPhones()
        {
            PhoneClientList.DialogFuncs.Remove(MedicalPhone);
            PhoneClientList.DialogFuncs.Remove(IllegalPhone);
            var contacts = store?.PhoneClientDict;
            if (contacts == null) return;
            foreach (var key in contacts.Where(entry => ClinicPhoneName(entry.Value?.dialogFuncId) != null)
                .Select(entry => entry.Key).ToArray()) contacts.Remove(key);
        }

        private Dialogue ClinicPharmacyCall(bool illegal, StorePhoneClient phone)
        {
            var speaker = ClinicPhoneName(phone.dialogFuncId);
            if (!IsLoaded || ModHelper.IsGameOver()) return EndClinicPhoneCall(speaker, "暂时无法安排供货。");
            var pending = illegal ? state.IllegalMedicalDeliveryPending : state.MedicalDeliveryPending;
            var readyDay = illegal ? state.IllegalMedicalCallReadyDay : state.MedicalCallReadyDay;
            if (pending) return EndClinicPhoneCall(speaker, "已经安排药商，等他到店。");
            if (Today < readyDay) return EndClinicPhoneCall(speaker, $"还需等 {readyDay - Today} 天才能再次叫货。");

            var request = new Dialogue().SetText("PLAYER", "要求供货");
            request.SetNextDialogue(EndClinicPhoneCall(speaker, "好，药商明天营业时到店。药品到柜台再付费。"));
            var decline = new Dialogue().SetText("PLAYER", "暂时拒绝");
            decline.SetNextDialogue(EndClinicPhoneCall(speaker, "好的，有需要再打来。"));
            return new Dialogue().SetText(speaker, "要安排一次药品供货吗？")
                .AddChoice(DirectoryMaster.Item("$", true), "", "", request, () => RequestClinicPharmacy(illegal, phone), null)
                .AddChoice(DirectoryMaster.Item("$", true), "", "", decline, null, null);
        }

        private static Dialogue EndClinicPhoneCall(string speaker, string text) =>
            new Dialogue().SetText(speaker, text).SetEndAction(() => PhoneUIManager.Instance?.OnPhoneDialogEnd(true));

        private void RequestClinicPharmacy(bool illegal, StorePhoneClient phone)
        {
            if (!IsLoaded || ModHelper.IsGameOver()) return;
            if (illegal)
            {
                if (!state.IllegalMedicalChannel || state.IllegalMedicalDeliveryPending || Today < state.IllegalMedicalCallReadyDay) return;
                state.IllegalMedicalDeliveryPending = true;
                state.IllegalMedicalDeliveryDay = Today + 1;
                state.IllegalMedicalCallReadyDay = Today + PharmacyCooldownDays;
            }
            else
            {
                if (!state.MedicalChannel || state.MedicalDeliveryPending || Today < state.MedicalCallReadyDay) return;
                state.MedicalDeliveryPending = true;
                state.MedicalDeliveryDay = Today + 1;
                state.MedicalCallReadyDay = Today + PharmacyCooldownDays;
            }
            phone.UseService();
            Save();
        }
    }

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetLocalizedDisplayName))]
    internal static class ClinicPhoneNamePatch
    {
        private static void Postfix(StorePhoneClient __instance, ref string __result)
        {
            var name = ModEntry.ClinicPhoneName(__instance.dialogFuncId);
            if (name != null) __result = name;
        }
    }
}
