using static DoctorClinic.ClinicText;
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace DoctorClinic
{
    internal sealed class ClinicPharmacyStock
    {
        public int Day { get; set; }
        public bool Arrived { get; set; }
    }

    public sealed partial class ModEntry
    {
        private const string PharmacyId = "doctor_clinic_wednesday_pharmacy";
        private static readonly (string Id, int Count)[] AssociationStock =
        {
            (MedicalPack.Id, 3), ("hemostatic_bandage_item", 2), ("topical_bandage_item", 2),
            ("smelling_salt", 2), ("blood_bag", 2), ("pink_injector", 2),
            ("large_purple_injector", 1), ("phagimycin_pill", 2),
            ("pure_white_injector", 1), ("salve", 1), (Antifungal, 1), (ToxinNeutralizer, 1)
        };

        private const string RequestedPharmacyId = "doctor_clinic_requested_pharmacy";
        private const string IllegalPharmacyId = "doctor_clinic_illegal_pharmacy";
        private static readonly (string Id, int Count)[] IllegalStock =
        {
            ("fanta_pill", 4), ("unlicensed_phagimycin_pill", 4), ("zyanide_pill", 4),
            ("dream_dust", 4), ("nightmare_dust", 4), ("skincare_cream", 1),
            ("blue_blood_bag", 1), ("salve", 1), ("black_injector", 1), (Immunivax, 1)
        };

        private List<string> pharmacyOffers;
        private StoreClient pharmacyVendor;
        private bool stockingCounter;
        private bool illegalPharmacyOffers;

        private void QueueWeeklyPharmacy(StoreClientManager manager)
        {
            // StoreCalendar uses dayCounter % 7, with Sunday at 0 and Wednesday at 3.
            if (!ClinicReputation.HasWeeklySupply(Today, state.ReputationHalfPoints) ||
                (state.Pharmacy?.Day == Today && state.Pharmacy.Arrived) ||
                manager.GetClientStack().Exists(client => client?.identifier == PharmacyId)) return;

            var sunday = Today % 7 == 0;
            var greeting = sunday ? T("clinic_the_association_s_sunday_restock_is_here")
                : T("clinic_the_association_s_wednesday_restock_is_here");
            QueuePharmacy(manager, PharmacyId, T("clinic_association_supplier"), greeting, false,
                () => state.Pharmacy = new ClinicPharmacyStock { Day = Today, Arrived = true });
        }

        private void QueueRequestedPharmacies(StoreClientManager manager)
        {
            if (state.MedicalDeliveryPending && Today >= state.MedicalDeliveryDay)
                QueuePharmacy(manager, RequestedPharmacyId, T("clinic_association_supplier"), T("clinic_your_medicine_order_is_here_i_ll"), false,
                    () => state.MedicalDeliveryPending = false);
            if (state.IllegalMedicalDeliveryPending && Today >= state.IllegalMedicalDeliveryDay)
                QueuePharmacy(manager, IllegalPharmacyId, T("clinic_black_market_medical_supplier"), T("clinic_i_ve_got_what_you_asked_for"), true,
                    () => state.IllegalMedicalDeliveryPending = false);
        }

        private void QueuePharmacy(StoreClientManager manager, string id, string name, string greeting, bool illegal, Action arrived)
        {
            if (manager.GetClientStack().Exists(client => client?.identifier == id) ||
                store.currentClientInstance?.GetClientBlueprint()?.identifier == id) return;

            var vendor = new StoreClient
            {
                identifier = id,
                displayName = name,
                spriteName = SpriteDict.GetRandomScientistSpriteName(),
                clientFaction = illegal ? StoreClient.FACTION_CRIME : StoreClient.FACTION_LOWER,
                clientIntent = StoreClient.ClientIntent.SELL
            };
            vendor.OnArrival = () =>
            {
                if (!IsLoaded) return;
                arrived();
                var discount = ClinicReputation.PharmacyDiscount(state.ReputationHalfPoints);
                if (!illegal && discount > 0) vendor.SetWholesale(discount);
                Save();
            };
            vendor.OnDismiss = ClearPharmacyOffers;
            vendor.mainDialogue.SetText(vendor.displayName, greeting)
                .SetEndAction(() =>
                {
                    if (!IsLoaded || store.currentClientInstance?.GetClientBlueprint() != vendor || pharmacyVendor == vendor) return;
                    pharmacyVendor = vendor;
                    pharmacyOffers = new List<string>();
                    illegalPharmacyOffers = illegal;
                    var stock = illegal ? IllegalStock : AssociationStock;
                    for (var round = 0; round < (illegal ? 4 : 3); round++)
                        foreach (var supply in stock)
                            if (round < supply.Count) pharmacyOffers.Add(supply.Id);
                    RefillPharmacyCounter(store);
                });
            vendor.CompleteClientCreation(false);
            manager.AddClient(vendor);
        }

        private void ClearPharmacyOffers()
        {
            pharmacyVendor = null;
            pharmacyOffers = null;
            illegalPharmacyOffers = false;
        }

        private bool PutPharmacyOfferOnCounter(string itemId)
        {
            if (!illegalPharmacyOffers) return store.TryAddDirectSellingItemToTable(itemId, false, false, false, 0);
            var item = DirectoryMaster.Item(itemId, true);
            if (item == null || item.identifier != itemId || item.uniqueId == 0)
            {
                item?.Destroy();
                return false;
            }
            StolenHelper.InitStolenItem(item, 0);
            return store.TryAddDirectSellingItemToTable(item, false, false, true, 0);
        }

        internal void RefillPharmacyCounter(PlayerStore playerStore)
        {
            if (stockingCounter || pharmacyOffers == null || pharmacyOffers.Count == 0 ||
                store != playerStore || store.currentClientInstance?.GetClientBlueprint() != pharmacyVendor)
                return;
            var counter = EmporiumEntry.Instance?.frontInvinvElement;
            if (counter == null || !counter.CanTakeAnything()) return;
            stockingCounter = true;
            try
            {
                for (var index = 0; index < pharmacyOffers.Count && counter.CanTakeAnything();)
                {
                    var itemId = pharmacyOffers[index];
                    if (PutPharmacyOfferOnCounter(itemId))
                        pharmacyOffers.RemoveAt(index);
                    else
                        index++;
                }
            }
            finally
            {
                stockingCounter = false;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.RefreshCounterItem))]
    internal static class PharmacyCounterRefreshPatch
    {
        private static void Postfix(PlayerStore __instance)
        {
            ModEntry.Active?.RefillPharmacyCounter(__instance);
            ModEntry.Active?.DeliverCommissionItems();
        }
    }
}
