using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DoctorClinic
{
    internal sealed class DragPreview : MonoBehaviour
    {
        public WoundDrop SamplingTarget { private get; set; }
        private Image image;
        private GameItemElement currentItem;

        public void Move(GameItemElement item, Vector2 screenPoint)
        {
            var panel = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            if (item == null || !RectTransformUtility.RectangleContainsScreenPoint(panel, screenPoint, camera) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, screenPoint, camera, out var local))
            {
                Hide();
                return;
            }

            if (image == null)
            {
                var visual = new GameObject("Clinic dragged supply", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                visual.transform.SetParent(panel, false);
                visual.GetComponent<LayoutElement>().ignoreLayout = true;
                image = visual.GetComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.rectTransform.sizeDelta = new Vector2(40, 40);
            }

            if (currentItem != item)
            {
                currentItem = item;
                image.sprite = item.ResolveSpriteByName(item.spritePath);
            }

            image.gameObject.SetActive(image.sprite != null);
            image.rectTransform.localPosition = local + new Vector2(23, -19);
            image.transform.SetAsLastSibling();
        }

        public void Hide()
        {
            if (image != null) image.gameObject.SetActive(false);
        }

        public WoundDrop FindTarget(Vector2 point, bool sampling)
        {
            if (EventSystem.current == null) return null;
            var pointer = new PointerEventData(EventSystem.current) { position = point };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            var hit = hits.Count > 0 ? hits[0].gameObject.transform : null;
            var target = hit == null ? null : hit.GetComponentInParent<WoundDrop>();
            if (target != null && target.transform.IsChildOf(transform)) return target;
            if (!sampling || SamplingTarget == null || (hit != null && !hit.IsChildOf(transform))) return null;

            var canvas = GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)SamplingTarget.transform, point, camera)
                ? SamplingTarget : null;
        }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "Drag")]
    internal static class NativeDragPreviewPatch
    {
        private static void Postfix(ItemMouseDragHandler __instance, GameItemElement ___currentItem)
        {
            var preview = ModEntry.Active?.CurrentDragPreview;
            if (preview != null && __instance.IsDraggingItem && ModEntry.IsClinicBodyItem(___currentItem))
                preview.Move(___currentItem, Input.mousePosition);
        }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class NativeDropPatch
    {
        private static void Prefix(ref bool cancelled, GameItemElement ___currentItem,
            ref GameInventory ___lastInventory, ref GameItem ___lastItem, out Action __state)
        {
            __state = null;
            var preview = ModEntry.Active?.CurrentDragPreview;
            if (preview == null) return;
            preview.Hide();
            if (cancelled || !ModEntry.IsClinicBodyItem(___currentItem)) return;
            var target = preview.FindTarget(Input.mousePosition, ModEntry.IsSamplingSyringe(___currentItem));
            if (target == null) return;

            // Let the game restore the dragged item and clear its drag UI before applying treatment.
            cancelled = true;
            ___lastInventory = null;
            ___lastItem = null;
            __state = () => target.Apply?.Invoke(___currentItem);
        }

        private static void Postfix(Action __state) => __state?.Invoke();
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "AbortDrag")]
    internal static class NativeDragAbortPatch
    {
        private static void Postfix()
        {
            var preview = ModEntry.Active?.CurrentDragPreview;
            if (preview != null) preview.Hide();
        }
    }
}
