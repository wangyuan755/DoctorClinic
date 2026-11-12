using System;
using System.Collections;
using HarmonyLib;

namespace DoctorClinic
{
    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.OfferDiscount))]
    internal static class ClinicDiscountPatch
    {
        private static void Postfix(StoreClient ___trackedClient, bool ___discountOffered, int ___discountPercent)
        {
            if (___discountOffered)
                ModEntry.Active?.OnNativeDiscount(___trackedClient, ___discountPercent);
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), "RefreshNow")]
    internal static class ClinicTabletRefreshPatch
    {
        private static bool Prefix(NegociationUIManager __instance, NegotiationTabletFrame ___frame,
            object ___cachedRows, ref bool ___refreshQueued, ref string ___builtSignature)
        {
            if (ModEntry.Active?.RefreshSettlement(__instance, ___frame) != true) return true;
            ___refreshQueued = false;
            ___builtSignature = null;
            ((IDictionary)___cachedRows).Clear();
            return false;
        }
    }

    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.OpenUI))]
    internal static class ClinicTabletOpenPatch
    {
        private static void Prefix() => ModEntry.Active?.BindPatientTabletOnOpen();

        private static void Postfix(NegociationUIManager __instance, NegotiationTabletFrame ___frame) =>
            ModEntry.Active?.RenderPendingPatientTablet(__instance, ___frame);
    }

    [HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.CloseUI))]
    internal static class ClinicTabletClosePatch
    {
        private static void Postfix() => ModEntry.Active?.ClearSettlement();
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient))]
    internal static class ClinicDismissPatch
    {
        private static void Prefix(PlayerStore __instance) => ModEntry.Active?.OnCounterDismiss(__instance);
    }
}
