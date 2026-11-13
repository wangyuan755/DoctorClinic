using static DoctorClinic.ClinicText;
using System;
using HarmonyLib;
using UnityEngine;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string MedicalPerkTab = "doctor_clinic_medical";
        private const string MedicalChannelPerk = "doctor_clinic_medical_channel";
        private const string IllegalChannelPerk = "doctor_clinic_illegal_medical_channel";
        private const string PoisonerPerk = "doctor_clinic_poisoner";
        private const string WhiteGlovePerk = "doctor_clinic_white_glove";
        private const string LegalAidPerk = "doctor_clinic_legal_aid";
        private const string RestoreLicensePerk = "doctor_clinic_restore_license";

        private bool wildePerksRegistered;

        private void RegisterWildePerks()
        {
            ModHelper.SetNetworkTabLabel(MedicalPerkTab, () => T("clinic_medical"));
            ModHelper.AddNetworkUpgrade(ModId, MedicalChannelPerk, 300, T("clinic_medical"),
                () => T("clinic_medical_connections"), () => T("clinic_unlocks_the_medical_association_phone_number_orders"),
                () => UnlockClinicPhone(illegal: false), MedicalPerkTab, "medical_channel", null);
            ModHelper.AddNetworkUpgrade(ModId, IllegalChannelPerk, 400, T("clinic_medical"),
                () => T("clinic_black_market_medical_connections"), () => T("clinic_unlocks_the_black_market_medical_supplier_s"),
                () => UnlockClinicPhone(illegal: true), MedicalPerkTab, "illegal_medical_channel", null);
            ModHelper.AddNetworkUpgrade(ModId, PoisonerPerk, 400, T("clinic_medical"),
                () => T("clinic_poisoner"), () => T("clinic_wilde_will_put_your_contact_details_on"),
                null, MedicalPerkTab, "poisoner", null);
            ModHelper.AddNetworkUpgrade(ModId, WhiteGlovePerk, 800, T("clinic_medical"),
                () => T("clinic_security_s_white_glove"), () => T("clinic_requires_poisoner_and_more_than_60_security"),
                null, MedicalPerkTab, "security_white_glove", new[] { PoisonerPerk });
            ModHelper.AddNetworkUpgrade(ModId, LegalAidPerk, 300, T("clinic_medical"),
                () => T("clinic_legal_aid"), () => T("clinic_waives_the_penalties_for_your_next_medical"),
                null, MedicalPerkTab, "legal_aid", null, 7);
            ModHelper.AddNetworkUpgrade(ModId, RestoreLicensePerk, RecoveryPrice, T("clinic_medical"),
                () => T("clinic_obtain_or_restore_a_medical_license"), () => T("clinic_if_you_permanently_declined_the_association_you"),
                null, MedicalPerkTab, "license_restore", null, 1);
        }

        private void UnlockClinicPhone(bool illegal)
        {
            if (!IsLoaded) return;
            if (illegal) state.IllegalMedicalChannel = true;
            else state.MedicalChannel = true;
            SyncClinicPhones();
            Save();
        }

        internal static bool IsClinicPhonePerk(string id) => id == MedicalChannelPerk || id == IllegalChannelPerk;

        private void SyncWildePerks()
        {
            if (!IsLoaded) return;
            try
            {
                if (!wildePerksRegistered)
                {
                    RegisterWildePerks();
                    wildePerksRegistered = true;
                }
                NetworkUpgrade.AddMissingAfterLoad();
                foreach (var upgrade in store.networkUpgrade.Values)
                {
                    if (upgrade?.source == ModId && NetworkUpgrade.GetDefinition(upgrade.id) is NetworkUpgrade definition)
                        upgrade.cost = definition.cost;
                }
                if (state.MedicalChannel) NetworkUpgrade.MarkUnlockedSilently(MedicalChannelPerk);
                if (state.IllegalMedicalChannel) NetworkUpgrade.MarkUnlockedSilently(IllegalChannelPerk);
                if (state.Poisoner) NetworkUpgrade.MarkUnlockedSilently(PoisonerPerk);
                if (state.WhiteGlove) NetworkUpgrade.MarkUnlockedSilently(WhiteGlovePerk);
            }
            catch (Exception error)
            {
                log.Warning(T("clinic_could_not_register_wilde_s_medical_favors", error));
            }
        }

        internal bool IsMedicalPerkReady(string id)
        {
            if (!IsLoaded || state.EndingPending ||
                state.EndingTriggered || ClinicCompat.IsGameOver()) return false;
            if (state.License == LicenseStatus.None)
                return id == RestoreLicensePerk && state.InvitationPermanentlyDeclined;
            return id switch
            {
                MedicalChannelPerk => !state.MedicalChannel,
                IllegalChannelPerk => !state.IllegalMedicalChannel,
                PoisonerPerk => !state.Poisoner,
                WhiteGlovePerk => state.Poisoner && !state.WhiteGlove && StoreReputation.GetSecFaction()?.GetReputationExact() > 60,
                LegalAidPerk => !state.LegalAid && Today >= state.LegalAidNextDay,
                RestoreLicensePerk => state.License == LicenseStatus.Revoked,
                _ => false
            };
        }

        internal void BuyMedicalPerk(NetworkUpgrade perk)
        {
            if (!IsMedicalPerkReady(perk.id) || store.playerCash < perk.cost) return;
            if (perk.id == RestoreLicensePerk)
            {
                if (state.License == LicenseStatus.None)
                {
                    var failure = ObtainWildeClinicLicense(perk.cost);
                    if (failure != null)
                    {
                        ShowAssociation(failure);
                        return;
                    }
                }
                else RestoreLicense(showAssociation: false);
            }
            else
            {
                store.PayMoney(perk.cost, true);
                if (perk.id == PoisonerPerk) state.Poisoner = true;
                else if (perk.id == WhiteGlovePerk) state.WhiteGlove = true;
                else if (perk.id == LegalAidPerk)
                {
                    state.LegalAid = true;
                    state.LegalAidNextDay = Today + 7;
                }
            }
            RunStats.OnWildUpgradeBought(perk.cost);
            perk.Unlock();
            SyncClinicPhones();
            Save();
            WildNetworkUI.RefreshAll();
        }

        private string ObtainWildeClinicLicense(int price)
        {
            var documentsAndTools = new[] { LicenseItem, Handbook, Syringe, Analyzer, Scalpel, Forceps };
            var failure = MedicalPack.Deliver(EmporiumEntry.Instance?.invElement, documentsAndTools, () =>
            {
                if (!IsMedicalPerkReady(RestoreLicensePerk) || store.playerCash < price) return false;
                store.PayMoney(price, true);
                ActivateFirstClinicLicense("clinic_reason_wilde_medical_license");
                return true;
            });
            OverlayHandler.current?.UpdateInventoryStateUI();
            return failure;
        }

        internal Sprite MedicalPerkIcon(string id)
        {
            var upgrade = NetworkUpgrade.GetDefinition(id);
            return upgrade?.source == ModId && !string.IsNullOrEmpty(upgrade.iconKey) ? Art(upgrade.iconKey) : null;
        }
    }

    [HarmonyPatch(typeof(NetworkUpgrade), nameof(NetworkUpgrade.IsReady))]
    internal static class MedicalPerkReadyPatch
    {
        private static void Postfix(NetworkUpgrade __instance, ref bool __result)
        {
            if (__instance.source == ModEntry.ModId)
                __result = ModEntry.Active?.IsMedicalPerkReady(__instance.id) == true;
        }
    }

    [HarmonyPatch(typeof(WildNetworkUI), "OnUseClicked")]
    internal static class MedicalPerkPurchasePatch
    {
        private static bool Prefix(string ___selectedId)
        {
            var upgrades = PlayerStore.Instance?.networkUpgrade;
            if (string.IsNullOrEmpty(___selectedId) || upgrades == null ||
                !upgrades.TryGetValue(___selectedId, out var upgrade) || upgrade?.source != ModEntry.ModId ||
                ModEntry.IsClinicPhonePerk(upgrade.id)) return true;
            ModEntry.Active?.BuyMedicalPerk(upgrade);
            return false;
        }
    }

    [HarmonyPatch(typeof(WildNetworkUI), "ResolveIcon")]
    internal static class MedicalPerkIconPatch
    {
        private static void Postfix(string id, ref Sprite __result)
        {
            var icon = ModEntry.Active?.MedicalPerkIcon(id);
            if (icon != null) __result = icon;
        }
    }
}
