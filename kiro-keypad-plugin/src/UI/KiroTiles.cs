namespace Loupedeck.KiroKeypadPlugin
{
    using System;
    using KiroKeypad;

    internal static class KiroTiles
    {
        public static BitmapImage Render(PluginImageSize imageSize, TileFace face)
        {
            using var bb = new BitmapBuilder(imageSize);
            var field = Paint(face.Field);
            bb.Clear(field);
            bb.FillRectangle(0, 0, bb.Width, bb.Height, field);

            var w = bb.Width;
            var h = bb.Height;
            if (face.TopBar)
            {
                bb.FillRectangle(0, 0, w, Math.Max(4, h * 4 / 100), Paint(Rgb.Accent));
            }

            if (face.BottomBand)
            {
                var band = Math.Max(6, h * 6 / 100);
                bb.FillRectangle(0, h - band, w, band, Paint(Rgb.Accent));
            }

            if (face.Pip is Rgb pip)
            {
                var pipSize = Math.Max(6, w / 16);
                bb.FillRectangle(w - pipSize - 6, 8, pipSize, pipSize, Paint(pip));
            }

            DrawGlyph(bb, face.Glyph, Paint(face.IconInk));

            var captionTop = h * 62 / 100;
            var captionWidth = w * 84 / 100;
            var captionX = (w - captionWidth) / 2;
            // ~0.6em per character, so "New chat" and "Explain" stay on one line.
            var byWidth = captionWidth * 10 / (Math.Max(1, face.Caption.Length) * 6);
            var byHeight = (h - captionTop) * 3 / 5;
            var font = Math.Clamp(Math.Min(byWidth, byHeight), 11, 16);
            bb.DrawText(
                face.Caption,
                captionX,
                captionTop,
                captionWidth,
                font + 2,
                Paint(face.Ink),
                font,
                font,
                0,
                TileFace.FontName);
            return bb.ToImage();
        }

        private static void DrawGlyph(BitmapBuilder bb, Glyph glyph, BitmapColor ink)
        {
            var w = bb.Width;
            var h = bb.Height;
            var cx = w * 50 / 100;
            var cy = h * 34 / 100;
            var r = Math.Min(w, h) * 16 / 100;
            switch (glyph)
            {
                case Glyph.Check:
                    bb.DrawLine(cx - r, cy, cx - r / 3, cy + r, ink, 4);
                    bb.DrawLine(cx - r / 3, cy + r, cx + r, cy - r, ink, 4);
                    break;
                case Glyph.Cross:
                    bb.DrawLine(cx - r, cy - r, cx + r, cy + r, ink, 4);
                    bb.DrawLine(cx + r, cy - r, cx - r, cy + r, ink, 4);
                    break;
                case Glyph.Square:
                    bb.FillRectangle(cx - r, cy - r, r * 2, r * 2, ink);
                    break;
                case Glyph.List:
                    for (var i = -1; i <= 1; i++)
                    {
                        var y = cy + i * (r / 2);
                        bb.FillCircle(cx - r, y, 3, ink);
                        bb.FillRectangle(cx - r + 8, y - 2, r * 2, 4, ink);
                    }

                    break;
                case Glyph.Play:
                    bb.DrawLine(cx - r / 2, cy - r, cx + r, cy, ink, 4);
                    bb.DrawLine(cx + r, cy, cx - r / 2, cy + r, ink, 4);
                    bb.DrawLine(cx - r / 2, cy + r, cx - r / 2, cy - r, ink, 4);
                    break;
                case Glyph.Pause:
                    bb.FillRectangle(cx - r, cy - r, r / 2, r * 2, ink);
                    bb.FillRectangle(cx + r / 3, cy - r, r / 2, r * 2, ink);
                    break;
                case Glyph.Spark:
                    bb.DrawLine(cx, cy - r, cx, cy + r, ink, 3);
                    bb.DrawLine(cx - r, cy, cx + r, cy, ink, 3);
                    bb.DrawLine(cx - r / 2, cy - r / 2, cx + r / 2, cy + r / 2, ink, 3);
                    bb.DrawLine(cx + r / 2, cy - r / 2, cx - r / 2, cy + r / 2, ink, 3);
                    break;
                case Glyph.Chat:
                    bb.DrawRectangle(cx - r, cy - r, r * 2, r * 3 / 2, ink);
                    bb.DrawLine(cx - r / 3, cy + r / 2, cx, cy + r, ink, 3);
                    break;
                case Glyph.Terminal:
                    bb.DrawLine(cx - r, cy - r / 2, cx - r / 4, cy, ink, 3);
                    bb.DrawLine(cx - r / 4, cy, cx - r, cy + r / 2, ink, 3);
                    bb.FillRectangle(cx, cy + r / 3, r, 4, ink);
                    break;
                case Glyph.Diff:
                    bb.FillRectangle(cx - r, cy - r, 6, r * 2, ink);
                    bb.FillRectangle(cx + r / 2, cy - r / 2, r, 6, ink);
                    break;
                case Glyph.Alert:
                    bb.DrawLine(cx, cy - r, cx + r, cy + r, ink, 4);
                    bb.DrawLine(cx + r, cy + r, cx - r, cy + r, ink, 4);
                    bb.DrawLine(cx - r, cy + r, cx, cy - r, ink, 4);
                    bb.FillCircle(cx, cy + r / 3, 3, ink);
                    break;
                case Glyph.Undo:
                    bb.DrawArc(cx, cy, r, 200, 220, ink, 4);
                    break;
                case Glyph.Gauge:
                    bb.DrawArc(cx, cy, r, 200, 140, ink, 4);
                    bb.DrawLine(cx, cy, cx + r / 2, cy - r / 2, ink, 3);
                    break;
                case Glyph.Layers:
                    bb.DrawRectangle(cx - r, cy - r / 4, r * 2, r, ink);
                    bb.DrawRectangle(cx - r + 6, cy - r / 2, r * 2, r, ink);
                    break;
                case Glyph.Book:
                    bb.DrawRectangle(cx - r, cy - r, r * 2, r * 2, ink);
                    bb.DrawLine(cx, cy - r, cx, cy + r, ink, 3);
                    break;
                default:
                    bb.DrawCircle(cx, cy, r, ink);
                    break;
            }
        }

        private static BitmapColor Paint(Rgb color) => new(color.R, color.G, color.B);
    }
}
