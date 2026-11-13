using UnityEngine;
using UnityEngine.UI;
using TMPro;
using static DoctorClinic.ClinicText;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private void ShowHandbook()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            manager.CloseWindow(HandbookWindow);
            var ui = CustomUIManager.Window(HandbookWindow, T("clinic_association_treatment_manual")).SetSize(700, 620);
            ui.BeginTabs();

            HandbookPage(ui, "first_visit");
            HandbookText(ui, T("clinic_manual_open"));
            ui.BeginRow(9, TextAnchor.MiddleCenter);
            HandbookExampleButton(ui, "clinic_begin_treatment");
            ui.AddLabel("→");
            HandbookBody(ui, true);
            ui.AddLabel("→");
            HandbookWound(ui, "abrasion_raw");
            ui.End();
            HandbookText(ui, T("clinic_manual_drag"));
            ui.BeginRow(9, TextAnchor.MiddleCenter);
            HandbookItem(ui, "rubbing_alcohol");
            HandbookItem(ui, "bandage_item");
            ui.AddLabel("→");
            HandbookBody(ui, true);
            ui.AddLabel(T("clinic_manual_or"));
            HandbookWound(ui, "abrasion_raw");
            ui.End();
            HandbookStages(ui, "abrasion_raw", "abrasion_clean", "dressing");
            HandbookText(ui, T("clinic_manual_bill"));
            ui.BeginPanel(435, -1, TextAnchor.MiddleCenter, 8, 5);
            ui.AddLabel(T("clinic_bill_result",
                T("clinic_bill_wound", BodyChart.RegionName(0), T("clinic_abrasion")), T("clinic_perfect_treatment")));
            ui.AddLabel(T("clinic_manual_example_amount", 155));
            HandbookExampleButton(ui, "clinic_end_settle");
            ui.End();
            ui.End().End();

            HandbookPage(ui, "injuries");
            HandbookText(ui, T("clinic_manual_bandages"));
            HandbookEntry(ui, "abrasion", "abrasion");
            HandbookEntry(ui, "shallow_cut", "shallow");
            HandbookEntry(ui, "bruise", "bruise");
            HandbookEntry(ui, "burn", "burn");
            HandbookEntry(ui, "fracture", "fracture");
            HandbookEntry(ui, "deep_cut", "deep");
            HandbookEntry(ui, "gunshot_wound", "gunshot");
            HandbookEntry(ui, "blood_loss", "bleeding");
            HandbookText(ui, T("clinic_manual_multiple"));
            ui.AddHeader(T("clinic_manual_compare"));
            ui.AddLabel(T("clinic_manual_compare_cuts"));
            HandbookStages(ui, false, "abrasion_raw", "laceration_raw", "deep_cut_bleeding");
            ui.AddLabel(T("clinic_manual_compare_deep"));
            HandbookStages(ui, "deep_cut_bleeding", "deep_cut_sutured");
            ui.AddLabel(T("clinic_manual_compare_bullet"));
            HandbookStages(ui, "bullet_raw", "bullet_removed");
            ui.AddLabel(T("clinic_manual_compare_bruise"));
            HandbookStages(ui, "bruise_raw", "bruise_cooled");
            ui.End().End();

            HandbookPage(ui, "diagnosis");
            HandbookText(ui, T("clinic_manual_sampling"));
            ui.BeginRow(9, TextAnchor.MiddleCenter);
            HandbookSprite(ui, Art("self_cleaning_sampler"));
            ui.AddLabel("→");
            HandbookBody(ui, false);
            ui.AddLabel("→");
            HandbookSprite(ui, Art(BloodSampleSprite));
            ui.AddLabel("→");
            HandbookSprite(ui, Art("blood_analyzer"));
            ui.End();
            ui.AddHeader(T("clinic_manual_report_example"));
            foreach (var marker in new[] { "bacterial", "blood_loss", "spore", "toxin", "overdose", "sentinel", "dependence" })
                ui.AddLabel(BloodResult(marker, marker == "bacterial"));
            HandbookText(ui, T("clinic_manual_consent"));
            HandbookEntry(ui, "graywater_fever", "fever");
            HandbookEntry(ui, "secondary_wound_infection", "infection");
            HandbookEntry(ui, "pipe_spore_disease", "spore");
            HandbookEntry(ui, "industrial_toxin_exposure", "toxin");
            HandbookEntry(ui, "sentinel_collapse_syndrome", "sentinel");
            HandbookEntry(ui, "oxymore_overdose", "overdose");
            HandbookEntry(ui, "oxymore_dependence", "dependence");
            HandbookText(ui, T("clinic_manual_pricing"));
            ui.End().End();

            HandbookPage(ui, "care");
            HandbookEntry(ui, "vascular_care", "vascular");
            HandbookDialogue(ui, "clinic_it_s_been_ages_since_my_last", "blue_blood_bag");
            HandbookEntry(ui, "skin_care", "skin");
            HandbookDialogue(ui, "clinic_i_haven_t_had_a_proper_skin", "skincare_cream");
            ui.AddHeader(T("clinic_manual_anesthesia_title"));
            HandbookText(ui, T("clinic_manual_anesthesia"));
            ui.End().End();

            ui.EndTabs();
            ui.AddCloseButton(() => manager.CloseWindow(HandbookWindow));
            ui.Show();
        }

        private static void HandbookPage(CustomUIBuilder ui, string key)
        {
            ui.BeginTab(T("clinic_manual_tab_" + key));
            ui.BeginScroll(480);
            ui.AddHeader(T("clinic_manual_page_" + key));
        }

        private static void HandbookText(CustomUIBuilder ui, string text)
        {
            ui.AddLabel(text);
            var rect = ui.Last.Rect;
            var label = ui.Last.text;
            if (label == null) return;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Overflow;
            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            var height = Mathf.Ceil(label.GetPreferredValues(text, 560, 0).y) + 6;
            layout.minHeight = layout.preferredHeight = Mathf.Max(26, height);
        }

        private static void HandbookEntry(CustomUIBuilder ui, string name, string key)
        {
            ui.AddHeader(T("clinic_" + name));
            HandbookText(ui, T("clinic_manual_entry", T("clinic_manual_sign_" + key), T("clinic_manual_treat_" + key), "\n"));
        }

        private static void HandbookExampleButton(CustomUIBuilder ui, string key)
        {
            ui.AddButton(T(key), () => { });
            var button = ui.Last.button;
            if (button != null)
            {
                button.transition = Selectable.Transition.None;
                button.interactable = false;
            }
        }

        private void HandbookDialogue(CustomUIBuilder ui, string line, string item)
        {
            ui.BeginPanel(590, -1, TextAnchor.MiddleCenter, 6, 5);
            HandbookText(ui, T("clinic_manual_quote", T(line)));
            HandbookItem(ui, item);
            ui.End();
        }

        private static void HandbookItem(CustomUIBuilder ui, string id)
        {
            // Use the original item art without granting an item or assigning a save ID.
            var item = DirectoryMaster.Create<GameItem>(id);
            if (item != null) HandbookSprite(ui, RenderHandler.LoadFromAtlas(item.spriteAtlasPath, item.spritePath));
        }

        private static void HandbookSprite(CustomUIBuilder ui, Sprite sprite)
        {
            if (sprite == null) return;
            var scale = 56f / Mathf.Max(sprite.rect.width, sprite.rect.height);
            ui.AddImage(sprite, sprite.rect.width * scale, sprite.rect.height * scale);
        }

        private void HandbookBody(CustomUIBuilder ui, bool markArm)
        {
            var body = Art("body_front");
            var provided = body != null && body.texture.isReadable;
            if (!provided)
            {
                if (bodySprite == null) bodySprite = BodyChart.CreateSprite();
                body = bodySprite;
            }
            ui.AddImage(body, provided ? 48 : 77, 96);
            if (markArm)
            {
                if (bodyMasks == null || bodyMaskSource != body)
                {
                    bodyMasks?.Destroy();
                    bodyMasks = new BodyInjuryMasks(body, provided);
                    bodyMaskSource = body;
                }
                HandbookOverlay(ui.Last.Rect, bodyMasks.GetOverlay(0));
            }
        }

        private void HandbookWound(CustomUIBuilder ui, string key)
        {
            var skin = Art("skin_base");
            var wound = Art(key);
            if (skin == null || wound == null) return;
            ui.AddImage(skin, 62, 62);
            HandbookOverlay(ui.Last.Rect, wound);
        }

        private static void HandbookOverlay(RectTransform parent, Sprite sprite)
        {
            var overlay = new GameObject("Handbook illustration", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)overlay.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = overlay.GetComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
        }

        private void HandbookStages(CustomUIBuilder ui, params string[] stages) => HandbookStages(ui, true, stages);

        private void HandbookStages(CustomUIBuilder ui, bool arrows, params string[] stages)
        {
            ui.BeginRow(9, TextAnchor.MiddleCenter);
            for (var i = 0; i < stages.Length; i++)
            {
                if (arrows && i != 0) ui.AddLabel("→");
                HandbookWound(ui, stages[i]);
            }
            ui.End();
        }
    }

}
