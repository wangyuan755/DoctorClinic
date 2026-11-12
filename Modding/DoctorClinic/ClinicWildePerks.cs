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
            ModHelper.SetNetworkTabLabel(MedicalPerkTab, () => "医疗");
            ModHelper.AddNetworkUpgrade(ModId, MedicalChannelPerk, 300, "医疗",
                () => "医药渠道", () => "解锁正规医药协会电话，两次叫货间隔七天。",
                null, MedicalPerkTab, "medical_channel", null);
            ModHelper.AddNetworkUpgrade(ModId, IllegalChannelPerk, 400, "医疗",
                () => "非法医药渠道", () => "解锁非法药商电话，向你提供非法药物及奢侈药物（可能是偷的！）",
                null, MedicalPerkTab, "illegal_medical_channel", null);
            ModHelper.AddNetworkUpgrade(ModId, PoisonerPerk, 400, "医疗",
                () => "毒师", () => "王尔德会把你的联系方式挂在地下的“人才市场”，以后可能会有人来给你一些委托。。。注意，致人死亡可能会给你带来不必要的麻烦（降低治安部及上层区声望，同时治安部会调查你）",
                null, MedicalPerkTab, "poisoner", null);
            ModHelper.AddNetworkUpgrade(ModId, WhiteGlovePerk, 800, "医疗",
                () => "治安部的白手套", () => "（需要毒师及高于60的治安部声望）治安部也会有不方便处理的人，那么为什么不找你解决呢？你会收到来自治安部的委托，毒杀任何目标（无论委托者是谁）都不会降低治安部声望，治安部也不会因此调查你",
                null, MedicalPerkTab, "security_white_glove", new[] { PoisonerPerk });
            ModHelper.AddNetworkUpgrade(ModId, LegalAidPerk, 300, "医疗",
                () => "法律援助", () => "抵消下一次医疗事故处罚，含主动投毒。购买冷却 7 天，同一时间只持有一次。",
                null, MedicalPerkTab, "legal_aid", null, 7);
            ModHelper.AddNetworkUpgrade(ModId, RestoreLicensePerk, RecoveryPrice, "医疗",
                () => "取得或恢复医生执照", () => "永久拒绝协会邀请后，可取得医生执照、协会诊疗手册及基本诊疗工具。执照吊销后也可在此恢复。",
                null, MedicalPerkTab, "license_restore", null, 1);
        }

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
                log.Warning($"王尔德医疗服务注册失败：{error}");
            }
        }

        internal bool IsMedicalPerkReady(string id)
        {
            if (!IsLoaded || state.EndingPending ||
                state.EndingTriggered || ModHelper.IsGameOver()) return false;
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
                if (perk.id == MedicalChannelPerk) state.MedicalChannel = true;
                else if (perk.id == IllegalChannelPerk) state.IllegalMedicalChannel = true;
                else if (perk.id == PoisonerPerk) state.Poisoner = true;
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
                ActivateFirstClinicLicense("通过王尔德取得医生执照");
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
                !upgrades.TryGetValue(___selectedId, out var upgrade) || upgrade?.source != ModEntry.ModId) return true;
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
