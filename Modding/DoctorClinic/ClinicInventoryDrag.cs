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

        public void Move(GameItemElement item, Vector2 screenPoint, RectTransform detailsWindow)
        {
            var hit = TopHit(screenPoint);
            var panel = IsInWindow(hit, detailsWindow) ? detailsWindow :
                IsInWindow(hit, transform) ? (RectTransform)transform : null;
            if (item == null || panel == null)
            {
                Hide();
                return;
            }

            var canvas = panel.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, screenPoint, camera, out var local))
            {
                Hide();
                return;
            }

            if (image == null)
            {
                var visual = new GameObject("Clinic dragged supply", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                visual.GetComponent<LayoutElement>().ignoreLayout = true;
                image = visual.GetComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.rectTransform.sizeDelta = new Vector2(40, 40);
                currentItem = null;
            }

            if (image.transform.parent != panel) image.transform.SetParent(panel, false);
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

        private void OnDestroy()
        {
            if (image != null)
            {
                Hide();
                Destroy(image.gameObject);
            }
        }

        private static bool IsInWindow(Transform hit, Transform window) =>
            hit != null && window != null && (hit == window || hit.IsChildOf(window));

        private static Transform TopHit(Vector2 point)
        {
            if (EventSystem.current == null) return null;
            var pointer = new PointerEventData(EventSystem.current) { position = point };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            return hits.Count > 0 ? hits[0].gameObject.transform : null;
        }

        public WoundDrop FindTarget(Vector2 point, bool sampling, RectTransform detailsWindow)
        {
            var hit = TopHit(point);
            var target = hit == null ? null : hit.GetComponentInParent<WoundDrop>();
            if (target != null && (IsInWindow(target.transform, transform) ||
                IsInWindow(target.transform, detailsWindow))) return target;
            if (!sampling || SamplingTarget == null || (hit != null && !IsInWindow(hit, transform))) return null;

            var canvas = GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)SamplingTarget.transform, point, camera)
                ? SamplingTarget : null;
        }

        /// <summary>
        /// Resolves the supply drop target from the clinic UI that is currently open. Unlike
        /// <see cref="FindTarget"/> this does not need the body chart: the wound details window
        /// is reachable straight from the settlement tablet and is a drop target of its own.
        /// </summary>
        internal static WoundDrop ResolveClinicDrop(Vector2 point, bool sampling)
        {
            var clinic = ModEntry.Active;
            if (clinic == null || !clinic.CanAcceptClinicDrops) return null;
            var body = clinic.BodyWindowRect;
            var details = clinic.CurrentWoundDetailRect;
            if (body == null && details == null) return null;

            var hit = TopHit(point);
            var target = hit == null ? null : hit.GetComponentInParent<WoundDrop>();
            if (target != null && (IsInWindow(target.transform, body) || IsInWindow(target.transform, details)))
                return target;

            // The details window shows exactly one wound, so a drop anywhere inside it targets
            // that wound. Requiring a pixel-perfect hit on the 100x100 illustration read as
            // "nothing happened" whenever the cursor landed a little off.
            if (sampling || details == null) return null;
            var camera = CameraFor(details);
            return RectTransformUtility.RectangleContainsScreenPoint(details, point, camera)
                ? details.GetComponentInChildren<WoundDrop>(true) : null;
        }

        private static Camera CameraFor(RectTransform rect)
        {
            var canvas = rect.GetComponentInParent<Canvas>();
            return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "Drag")]
    internal static class NativeDragPreviewPatch
    {
        private static void Postfix(ItemMouseDragHandler __instance, GameItemElement ___currentItem)
        {
            var clinic = ModEntry.Active;
            var preview = clinic?.CurrentDragPreview;
            if (preview != null && __instance.IsDraggingItem && ModEntry.IsClinicBodyItem(___currentItem))
                preview.Move(___currentItem, Input.mousePosition, clinic.CurrentWoundDetailRect);
        }
    }

    [HarmonyPatch(typeof(ItemMouseDragHandler), "EndDrag")]
    internal static class NativeDropPatch
    {
        private static void Prefix(ref bool cancelled, GameItemElement ___currentItem,
            ref GameInventory ___lastInventory, ref GameItem ___lastItem, out Action __state)
        {
            __state = null;
            var clinic = ModEntry.Active;
            clinic?.CurrentDragPreview?.Hide();
            if (cancelled || !ModEntry.IsClinicBodyItem(___currentItem)) return;
            var target = DragPreview.ResolveClinicDrop(Input.mousePosition, ModEntry.IsSamplingSyringe(___currentItem));
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
