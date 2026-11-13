using static DoctorClinic.ClinicText;
using UnityEngine;

namespace DoctorClinic
{
    internal static class BodyChart
    {
        private static readonly string[] RegionKeys = { "clinic_left_arm", "clinic_right_arm", "clinic_head", "clinic_torso", "clinic_left_leg", "clinic_right_leg" };
        internal static int RegionCount => RegionKeys.Length;
        internal static string RegionName(int index) => T(RegionKeys[index]);

        public static Sprite CreateSprite()
        {
            const int width = 240;
            const int height = 300;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            var fill = new Color32(57, 92, 99, 255);
            var outline = new Color32(146, 203, 198, 255);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var point = new Vector2(x, y);
                    if (!Inside(point)) continue;
                    var edge = !Inside(point + Vector2.left * 2) || !Inside(point + Vector2.right * 2)
                        || !Inside(point + Vector2.up * 2) || !Inside(point + Vector2.down * 2);
                    pixels[y * width + x] = edge ? outline : fill;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f));
        }

        private static bool Inside(Vector2 p)
        {
            var x = Mathf.Abs(p.x - 120);
            return Ellipse(x, p.y - 258, 23, 27)
                || (x < 12 && p.y >= 217 && p.y <= 244)
                || Ellipse(x, p.y - 177, 39, 53)
                || Capsule(new Vector2(x, p.y), new Vector2(34, 208), new Vector2(67, 135), 11)
                || Capsule(new Vector2(x, p.y), new Vector2(18, 145), new Vector2(31, 32), 14)
                || Ellipse(x - 34, p.y - 25, 18, 8);
        }

        private static bool Ellipse(float x, float y, float rx, float ry) =>
            x * x / (rx * rx) + y * y / (ry * ry) <= 1;

        private static bool Capsule(Vector2 point, Vector2 from, Vector2 to, float radius)
        {
            var axis = to - from;
            var closest = from + axis * Mathf.Clamp01(Vector2.Dot(point - from, axis) / axis.sqrMagnitude);
            return (point - closest).sqrMagnitude <= radius * radius;
        }

    }
}
