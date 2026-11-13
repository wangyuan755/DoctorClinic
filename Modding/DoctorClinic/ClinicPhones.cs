using static DoctorClinic.ClinicText;
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
            MedicalPhone => T("clinic_medical_association"),
            IllegalPhone => T("clinic_black_market_medical_supplier_2"),
            _ => null
        };

        internal void SyncClinicPhones()
        {
            if (!IsLoaded || store.PhoneClientDict == null) return;
            if (store.networkUpgrade != null)
            {
                state.MedicalChannel |= NetworkUpgrade.IsUnlocked(MedicalChannelPerk);
                state.IllegalMedicalChannel |= NetworkUpgrade.IsUnlocked(IllegalChannelPerk);
            }
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
                    cooldownDuration = PharmacyCooldownDays };
                contacts.Add(number, phone);
            }
            phone.dialedBefore = true;
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
            if (!IsLoaded || ModHelper.IsGameOver()) return EndClinicPhoneCall(speaker, T("clinic_we_can_t_arrange_a_delivery_right"));
            var pending = illegal ? state.IllegalMedicalDeliveryPending : state.MedicalDeliveryPending;
            var readyDay = illegal ? state.IllegalMedicalCallReadyDay : state.MedicalCallReadyDay;
            if (pending) return EndClinicPhoneCall(speaker, T("clinic_a_supplier_is_already_on_the_way"));
            if (Today < readyDay) return EndClinicPhoneCall(speaker, T("clinic_wait_more_day_s_before_placing_another", readyDay - Today));

            var request = new Dialogue().SetText("PLAYER", T("clinic_request_a_delivery"));
            request.SetNextDialogue(EndClinicPhoneCall(speaker, T("clinic_all_right_the_supplier_will_arrive_during")));
            var decline = new Dialogue().SetText("PLAYER", T("clinic_not_right_now"));
            decline.SetNextDialogue(EndClinicPhoneCall(speaker, T("clinic_all_right_call_us_when_you_need")));
            return new Dialogue().SetText(speaker, T("clinic_would_you_like_to_order_medical_supplies"))
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

    [HarmonyPatch(typeof(PhoneBookUIManager), nameof(PhoneBookUIManager.OpenUI))]
    internal static class ClinicPhoneBookPatch
    {
        private static void Prefix() => ModEntry.Active?.SyncClinicPhones();
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
