using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static DoctorClinic.ClinicText;

namespace DoctorClinic
{
    public sealed partial class ModEntry
    {
        private const string HandbookPaper = "doctor_clinic_handbook";
        private PaperUI handbookPaper;

        private void ShowHandbook()
        {
            handbookPaper?.Close();
            var book = PaperStarters.NewBook("guide", HandbookPaper, T("clinic_association_treatment_manual"));
            book.background = "notebook_manual";
            book.width = 628;
            book.height = 804;
            book.designWidth = 314;
            book.designHeight = 402;
            book.draggable = book.zoomable = book.sounds = true;
            book.pages.Clear();
            book.chrome = new List<PaperElementData>
            {
                HandbookControl("close", "paper_buttons_close1", 297, 2.5f, 14, 15),
                HandbookControl("zoom", "paper_buttons_screen_0", 273.5f, 3.5f, 14, 15),
                HandbookControl("flip_left", "paper_buttons_l_1", 124, 380, 16, 16),
                HandbookControl("flip_right", "paper_buttons1", 176, 380.5f, 14, 15),
                new PaperElementData { id = "page_indicator", type = "text", action = "page_indicator",
                    x = 140.5f, y = 374, w = 33, h = 25, style = "page_number", align = "center", valign = "middle", wrap = false }
            };
            var ui = PaperUI.FromData(HandbookPaper, book, ModId);

            HandbookPage(ui, "first_visit", "clinic_manual_paper_open");
            HandbookText(ui, "clinic_manual_open", 101, 69);
            HandbookBody(ui, "open_body", 68, 185, true);
            HandbookArrow(ui, 132, 236);
            HandbookWound(ui, "abrasion_raw", 173, 207, 56);
            HandbookText(ui, "clinic_begin_treatment", 308, 28, "section", "center");

            HandbookPage(ui, "first_visit", "clinic_manual_paper_drag");
            HandbookText(ui, "clinic_manual_drag", 101, 105);
            HandbookItem(ui, "rubbing_alcohol", 38, 212);
            HandbookItem(ui, "bandage_item", 38, 251);
            HandbookArrow(ui, 85, 238);
            HandbookBody(ui, "drag_body", 112, 204, true);
            ui.AddLocalizedText(163, 239, 27, 22, "clinic_manual_or", "caption", align: "center", fontSize: 10);
            HandbookWound(ui, "abrasion_raw", 198, 227, 47);
            HandbookStages(ui, 314, "abrasion_raw", "abrasion_clean", "dressing");

            HandbookPage(ui, "first_visit", "clinic_manual_paper_bill");
            HandbookText(ui, "clinic_manual_bill", 101, 124);
            ui.AddLine(48, 247, 267, 247, 1, "#756451");
            ui.AddText(48, 263, 219, 37, T("clinic_bill_result",
                T("clinic_bill_wound", BodyChart.RegionName(0), T("clinic_abrasion")), T("clinic_perfect_treatment")),
                "section", align: "center", fontSize: 12);
            ui.AddText(48, 301, 219, 22, T("clinic_manual_example_amount", 155), "body", align: "center", fontSize: 11);
            HandbookText(ui, "clinic_end_settle", 337, 24, "section", "center");

            HandbookPage(ui, "injuries", "clinic_manual_compare_cuts");
            HandbookEntry(ui, "abrasion", "abrasion", 100, 67);
            HandbookEntry(ui, "shallow_cut", "shallow", 176, 67);
            HandbookStages(ui, 256, false, "abrasion_raw", "laceration_raw", "deep_cut_bleeding");
            HandbookText(ui, "clinic_manual_bandages", 312, 60);

            HandbookPage(ui, "injuries", "clinic_manual_paper_cool");
            HandbookEntry(ui, "bruise", "bruise", 100, 70);
            HandbookEntry(ui, "burn", "burn", 180, 70);
            HandbookText(ui, "clinic_manual_compare_bruise", 256, 24, "caption", "center");
            HandbookStages(ui, 284, "bruise_raw", "bruise_cooled");

            HandbookPage(ui, "injuries", "clinic_manual_paper_trauma");
            HandbookEntry(ui, "fracture", "fracture", 100, 67);
            HandbookEntry(ui, "deep_cut", "deep", 176, 67);
            HandbookText(ui, "clinic_manual_compare_deep", 256, 24, "caption", "center");
            HandbookStages(ui, 284, "deep_cut_bleeding", "deep_cut_sutured");

            HandbookPage(ui, "injuries", "clinic_manual_paper_bleeding");
            HandbookEntry(ui, "gunshot_wound", "gunshot", 100, 67);
            HandbookEntry(ui, "blood_loss", "bleeding", 176, 78);
            HandbookText(ui, "clinic_manual_compare_bullet", 262, 22, "caption", "center");
            HandbookStages(ui, 286, "bullet_raw", "bullet_removed");
            HandbookText(ui, "clinic_manual_multiple", 344, 22, "caption", "center");

            HandbookPage(ui, "diagnosis", "clinic_manual_report_example");
            HandbookText(ui, "clinic_manual_sampling", 100, 94);
            HandbookArt(ui, 42, 207, 32, 32, "self_cleaning_sampler", "fit");
            HandbookArrow(ui, 81, 215);
            HandbookArt(ui, 107, 194, 26, 52, "body_front", "fit");
            HandbookArrow(ui, 142, 215);
            HandbookArt(ui, 171, 207, 27, 32, BloodSampleSprite, "fit");
            HandbookArrow(ui, 207, 215);
            HandbookArt(ui, 238, 207, 32, 32, "blood_analyzer", "fit");
            ui.AddLine(36, 251, 278, 251, 1, "#756451");
            var y = 257;
            foreach (var marker in new[] { "bacterial", "blood_loss", "spore", "toxin", "overdose", "sentinel", "dependence" })
            {
                ui.AddText(43, y, 228, 14, BloodResult(marker, marker == "bacterial"), "caption", fontSize: 10);
                if (marker == "bacterial") ui.Last.color = "#8C3F37";
                y += 15;
            }

            HandbookPage(ui, "diagnosis", "clinic_manual_paper_infection");
            HandbookEntry(ui, "graywater_fever", "fever", 100, 64);
            HandbookEntry(ui, "secondary_wound_infection", "infection", 171, 81);
            HandbookEntry(ui, "pipe_spore_disease", "spore", 259, 91);

            HandbookPage(ui, "diagnosis", "clinic_manual_paper_medication");
            HandbookEntry(ui, "industrial_toxin_exposure", "toxin", 100, 62);
            HandbookEntry(ui, "sentinel_collapse_syndrome", "sentinel", 170, 92);
            HandbookEntry(ui, "oxymore_overdose", "overdose", 270, 79);

            HandbookPage(ui, "diagnosis", "clinic_manual_paper_consent");
            HandbookEntry(ui, "oxymore_dependence", "dependence", 100, 121);
            HandbookText(ui, "clinic_manual_consent", 229, 64);
            HandbookText(ui, "clinic_manual_pricing", 304, 63);

            HandbookPage(ui, "care", "clinic_manual_paper_care");
            HandbookEntry(ui, "vascular_care", "vascular", 100, 49);
            HandbookDialogue(ui, "clinic_it_s_been_ages_since_my_last", "blue_blood_bag", 154);
            HandbookEntry(ui, "skin_care", "skin", 234, 49);
            HandbookDialogue(ui, "clinic_i_haven_t_had_a_proper_skin", "skincare_cream", 288);

            HandbookPage(ui, "care", "clinic_manual_anesthesia_title");
            HandbookText(ui, "clinic_manual_anesthesia", 111, 155);
            HandbookBody(ui, "anesthesia_body", 131, 240, false);

            ui.PageShown += ApplyHandbookArt;
            handbookPaper = ui;
            ui.Show();
        }

        private static PaperElementData HandbookControl(string action, string sprite, float x, float y, float w, float h) =>
            new PaperElementData { id = action, type = "button", action = action, sprite = sprite, x = x, y = y, w = w, h = h };

        private static void HandbookPage(PaperUI ui, string chapter, string title)
        {
            ui.AddPage(title);
            ui.AddImage(18.5f, 22, 283, 116, "guide_logo", id: "guide_logo");
            ui.AddLocalizedText(42, 35, 232, 16, "clinic_manual_tab_" + chapter, "header", align: "center", fontSize: 10);
            ui.AddLocalizedText(32, 53, 250, 31, title, "title", align: "center", fontSize: 15);
        }

        private static void HandbookText(PaperUI ui, string key, float y, float height,
            string style = "body", string align = "left") =>
            ui.AddLocalizedText(35, y, 247, height, key, style, align: align, fontSize: 10.5f);

        private static void HandbookEntry(PaperUI ui, string name, string key, float y, float height)
        {
            ui.AddLocalizedText(35, y, 247, 18, "clinic_" + name, "section", fontSize: 12);
            ui.AddText(35, y + 19, 247, height - 19,
                T("clinic_manual_entry", T("clinic_manual_sign_" + key), T("clinic_manual_treat_" + key), "\n"),
                "body", fontSize: 10.5f);
        }

        private static void HandbookItem(PaperUI ui, string id, float x, float y)
        {
            var item = DirectoryMaster.Create<GameItem>(id);
            if (item != null)
                ui.AddImage(x, y, 34, 34, item.spriteAtlasPath + "/" + item.spritePath, "fit");
        }

        private static void HandbookDialogue(PaperUI ui, string line, string item, float y)
        {
            HandbookItem(ui, item, 43, y + 7);
            ui.AddText(89, y, 181, 61, T("clinic_manual_quote", T(line)), "body", fontSize: 10.5f);
        }

        private void HandbookBody(PaperUI ui, string id, float x, float y, bool markArm)
        {
            HandbookArt(ui, x, y, 52, 104, "body_front", id: id);
            if (markArm) ui.AddImage(x, y, 52, 104, null, "stretch", id + "_arm");
        }

        private static void HandbookArrow(PaperUI ui, float x, float y) =>
            ui.AddLine(x, y, x + 15, y, 1, "#756451", "end");

        private void HandbookWound(PaperUI ui, string key, float x, float y, float size)
        {
            HandbookArt(ui, x, y, size, size, "skin_base");
            HandbookArt(ui, x, y, size, size, key);
        }

        private void HandbookArt(PaperUI ui, float x, float y, float w, float h,
            string key, string fit = "stretch", string id = null)
        {
            // Textures used by the treatment panel may not be registered as PaperUI sprites.
            var sprite = Art(key);
            if (sprite != null) clinicAssets.PaperSprites[key] = sprite;
            ui.AddImage(x, y, w, h, ModId + ":" + key, fit,
                id ?? "illustration_" + ui.Data.pages[ui.Data.pages.Count - 1].elements.Count);
        }

        private void HandbookStages(PaperUI ui, float y, params string[] stages) =>
            HandbookStages(ui, y, true, stages);

        private void HandbookStages(PaperUI ui, float y, bool showArrows, params string[] stages)
        {
            var x = stages.Length == 3 ? 66f : 97f;
            for (var i = 0; i < stages.Length; i++, x += 68)
            {
                HandbookWound(ui, stages[i], x, y, 45);
                if (showArrows && i + 1 < stages.Length)
                    HandbookArrow(ui, x + 49, y + 22);
            }
        }

        private void ApplyHandbookArt(PaperUI ui, int page)
        {
            var root = ui.Root.GetComponent<Image>();
            if (root != null && root.sprite == null)
            {
                root.sprite = FindNativePaperSprite("notebook_manual");
                if (root.sprite != null) root.color = Color.white;
            }
            SetNativePaperSprite(ui, "guide_logo", "guide_logo");
            foreach (var element in ui.Data.chrome)
                if (!string.IsNullOrEmpty(element.sprite)) SetNativePaperSprite(ui, element.id, element.sprite);

            var arm = ui.Get("open_body_arm")?.image ?? ui.Get("drag_body_arm")?.image;
            if (arm == null) return;
            var body = Art("body_front");
            if (body == null || !body.texture.isReadable)
            {
                arm.enabled = false;
                return;
            }
            if (bodyMasks == null || bodyMaskSource != body)
            {
                bodyMasks?.Destroy();
                bodyMasks = new BodyInjuryMasks(body, true);
                bodyMaskSource = body;
            }
            arm.sprite = bodyMasks.GetOverlay(0);
            arm.color = Color.white;
        }

        private static void SetNativePaperSprite(PaperUI ui, string id, string name)
        {
            var image = ui.Get(id)?.image;
            if (image != null && image.sprite == null)
            {
                image.sprite = FindNativePaperSprite(name);
                if (image.sprite != null) image.color = Color.white;
            }
        }

        private static Sprite FindNativePaperSprite(string name)
        {
            // Older installs can have the notebook sprites in the scene without a PaperAssetCatalog entry.
            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
                if (sprite.name == name) return sprite;
            return null;
        }
    }
}
