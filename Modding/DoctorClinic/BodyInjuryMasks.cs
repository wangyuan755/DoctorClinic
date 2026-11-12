using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DoctorClinic
{
    internal sealed class BodyInjuryMasks
    {
        private const int Width = 160;
        private const int Height = 320;
        private readonly byte[] regions = new byte[Width * Height];
        private readonly Sprite[] overlays = new Sprite[6];

        public BodyInjuryMasks(Sprite body, bool providedArt)
        {
            var source = body.texture;
            var raw = source.GetPixels32();
            var rect = body.rect;
            var colors = new Color32[overlays.Length][];
            for (var i = 0; i < colors.Length; i++) colors[i] = new Color32[regions.Length];
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var u = (x + .5f) / Width;
                var v = (y + .5f) / Height;
                var pixel = raw[(int)(rect.y + v * rect.height) * source.width + (int)(rect.x + u * rect.width)];
                if (pixel.a < 32) continue;
                var region = Region(u, v, providedArt);
                var index = y * Width + x;
                regions[index] = (byte)(region + 1);
                var light = (pixel.r + pixel.g + pixel.b) / 3;
                colors[region][index] = new Color32((byte)(130 + light * .47f),
                    (byte)(24 + light * .19f), (byte)(35 + light * .20f), pixel.a);
            }
            for (var i = 0; i < overlays.Length; i++)
            {
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                texture.filterMode = FilterMode.Point;
                texture.SetPixels32(colors[i]);
                texture.Apply(false, true);
                overlays[i] = Sprite.Create(texture, new Rect(0, 0, Width, Height), new Vector2(.5f, .5f));
            }
        }

        private static int Region(float x, float y, bool providedArt)
        {
            var side = Mathf.Abs(x - .5f);
            if (y >= .815f) return 2;
            if (providedArt)
            {
                var armBoundary = Mathf.Lerp(.25f, .16f, Mathf.InverseLerp(.43f, .76f, y));
                if (y > .41f && side >= armBoundary) return x > .5f ? 0 : 1;
                if (y < .49f) return x > .5f ? 4 : 5;
            }
            else
            {
                if (y > .41f && side > Mathf.Lerp(.15f, .12f, Mathf.InverseLerp(.43f, .76f, y)))
                    return x > .5f ? 0 : 1;
                if (y < .47f) return x > .5f ? 4 : 5;
            }
            return 3;
        }

        public bool Contains(int region, Vector2 uv)
        {
            if (uv.x < 0 || uv.y < 0 || uv.x >= 1 || uv.y >= 1) return false;
            var pixelRegion = regions[(int)(uv.y * Height) * Width + (int)(uv.x * Width)];
            return region < 0 ? pixelRegion != 0 : pixelRegion == region + 1;
        }

        public void AddTarget(RectTransform chart, int region, Action click, Action<GameItem> drop, Action inspectBlood)
        {
            var node = new GameObject("Wound region " + region, typeof(RectTransform), typeof(Image),
                typeof(BodyRegionTarget), typeof(WoundDrop));
            var rect = (RectTransform)node.transform;
            rect.SetParent(chart, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            node.GetComponent<Image>().sprite = overlays[region];
            var target = node.GetComponent<BodyRegionTarget>();
            target.Masks = this;
            target.Region = region;
            target.Click = click;
            target.InspectBlood = inspectBlood;
            node.GetComponent<WoundDrop>().Apply = drop;
        }

        public void Destroy()
        {
            foreach (var sprite in overlays)
            {
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
        }
    }

    internal sealed class BodyRegionTarget : MonoBehaviour, ICanvasRaycastFilter, IPointerClickHandler
    {
        public BodyInjuryMasks Masks { get; set; }
        public int Region { get; set; }
        public Action Click { get; set; }
        public Action InspectBlood { get; set; }

        public bool IsRaycastLocationValid(Vector2 screen, Camera camera)
        {
            var rect = (RectTransform)transform;
            if (Masks == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, camera, out var point))
                return false;
            var bounds = rect.rect;
            return bounds.width > 0 && bounds.height > 0 && Masks.Contains(Region,
                new Vector2((point.x - bounds.xMin) / bounds.width, (point.y - bounds.yMin) / bounds.height));
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Click?.Invoke();
            else if (eventData.button == PointerEventData.InputButton.Right) InspectBlood?.Invoke();
        }
    }
}
