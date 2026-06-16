using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace VentCalc.UI.Services
{
    public static class SpecImageService
    {
        private const int ImageSize = 128;
        public static string ImageDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VentCalc", "spec_images");

        public static string GetFallbackImagePath(string group, string name, string size = "", string typeMark = "")
        {
            try
            {
                Directory.CreateDirectory(ImageDirectory);
                string iconKind = ResolveIconKind(group, name, size);
                string key = NormalizeKey($"fallback_v3_{iconKind}_{group}_{name}_{size}_{typeMark}");
                string path = Path.Combine(ImageDirectory, key + ".png");
                if (!IsUsablePng(path))
                {
                    using Bitmap bitmap = BuildFallbackBitmap(iconKind, group, name, size);
                    SaveBitmap(bitmap, path);
                }

                return path;
            }
            catch (Exception)
            {
                return TryCreateTempPlaceholder();
            }
        }

        public static string SavePreviewImage(string cacheKey, Image? previewImage)
        {
            if (previewImage == null) return string.Empty;
            try
            {
                Directory.CreateDirectory(ImageDirectory);
                string path = Path.Combine(ImageDirectory, NormalizeKey("preview_" + cacheKey) + ".png");
                if (IsUsablePng(path)) return path;

                using var bitmap = new Bitmap(ImageSize, ImageSize);
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.White);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle target = FitInside(previewImage.Width, previewImage.Height, new Rectangle(6, 6, ImageSize - 12, ImageSize - 12));
                graphics.DrawImage(previewImage, target);
                using var border = new Pen(Color.FromArgb(170, 170, 170), 1);
                graphics.DrawRectangle(border, 1, 1, ImageSize - 3, ImageSize - 3);
                SaveBitmap(bitmap, path);
                return path;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsUsablePng(string path)
        {
            try
            {
                return File.Exists(path) && new FileInfo(path).Length > 32;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string TryCreateTempPlaceholder()
        {
            try
            {
                string fallbackPath = Path.Combine(Path.GetTempPath(), "VentCalc_spec_placeholder.png");
                if (!IsUsablePng(fallbackPath))
                {
                    using Bitmap bitmap = BuildFallbackBitmap("unknown", "Неопознано", "Нет изображения", string.Empty);
                    SaveBitmap(bitmap, fallbackPath);
                }

                return fallbackPath;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static void SaveBitmap(Bitmap bitmap, string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            File.WriteAllBytes(path, stream.ToArray());
        }

        private static string NormalizeKey(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            string normalized = new string(value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray());
            while (normalized.Contains("__", StringComparison.Ordinal)) normalized = normalized.Replace("__", "_");
            normalized = normalized.Trim('_');
            if (normalized.Length > 96) normalized = normalized.Substring(0, 96);
            return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
        }

        private static Bitmap BuildFallbackBitmap(string iconKind, string group, string name, string size)
        {
            var bitmap = new Bitmap(ImageSize, ImageSize);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(248, 250, 252));
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var borderPen = new Pen(Color.FromArgb(176, 184, 192), 1.5f);
            graphics.DrawRectangle(borderPen, 2, 2, ImageSize - 5, ImageSize - 5);

            using var linePen = new Pen(Color.FromArgb(45, 87, 130), 5) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var thinPen = new Pen(Color.FromArgb(45, 87, 130), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var fillBrush = new SolidBrush(Color.FromArgb(222, 235, 247));
            using var darkBrush = new SolidBrush(Color.FromArgb(45, 87, 130));
            using var lightBrush = new SolidBrush(Color.FromArgb(241, 246, 252));

            switch (iconKind)
            {
                case "duct_round": DrawRoundDuct(graphics, thinPen, fillBrush); break;
                case "flex": DrawFlexDuct(graphics, linePen, thinPen); break;
                case "elbow": DrawElbow(graphics, linePen, thinPen); break;
                case "transition": DrawTransition(graphics, thinPen, fillBrush); break;
                case "tee": DrawTee(graphics, linePen); break;
                case "tap": DrawTap(graphics, linePen); break;
                case "cap": DrawCap(graphics, thinPen, fillBrush, darkBrush); break;
                case "grille": DrawGrille(graphics, thinPen, fillBrush); break;
                case "diffuser": DrawDiffuser(graphics, thinPen, fillBrush); break;
                case "damper": DrawDamper(graphics, thinPen, fillBrush); break;
                case "hood": DrawHood(graphics, thinPen, fillBrush); break;
                case "fan": DrawFan(graphics, thinPen, fillBrush, darkBrush); break;
                case "equipment": DrawEquipment(graphics, thinPen, fillBrush, lightBrush); break;
                case "duct_rect": DrawRectDuct(graphics, thinPen, fillBrush); break;
                default: DrawUnknown(graphics, thinPen, darkBrush); break;
            }

            DrawCaption(graphics, GetCaption(iconKind, group, name), size);
            return bitmap;
        }

        private static void DrawRectDuct(Graphics g, Pen pen, Brush fill)
        {
            PointF[] front = { new(30, 48), new(84, 48), new(84, 78), new(30, 78) };
            PointF[] back = { new(48, 34), new(102, 34), new(102, 64), new(84, 78), new(84, 48), new(30, 48) };
            g.FillPolygon(fill, back); g.DrawPolygon(pen, back); g.FillPolygon(Brushes.White, front); g.DrawPolygon(pen, front);
            g.DrawLine(pen, 84, 48, 102, 34); g.DrawLine(pen, 84, 78, 102, 64);
        }

        private static void DrawRoundDuct(Graphics g, Pen pen, Brush fill)
        {
            g.FillEllipse(fill, 24, 44, 34, 34); g.DrawEllipse(pen, 24, 44, 34, 34);
            g.FillEllipse(fill, 72, 36, 34, 50); g.DrawEllipse(pen, 72, 36, 34, 50);
            g.DrawLine(pen, 41, 44, 89, 36); g.DrawLine(pen, 41, 78, 89, 86);
            g.DrawArc(pen, 72, 36, 34, 50, 90, 180);
        }

        private static void DrawFlexDuct(Graphics g, Pen linePen, Pen thinPen)
        {
            using var path = new GraphicsPath();
            path.AddBezier(18, 66, 42, 28, 80, 102, 110, 58);
            g.DrawPath(linePen, path);
            for (int i = 20; i <= 104; i += 10) g.DrawArc(thinPen, i, 47 + (i % 20 == 0 ? 0 : 8), 18, 24, 80, 220);
        }

        private static void DrawElbow(Graphics g, Pen linePen, Pen thinPen)
        {
            g.DrawArc(linePen, 28, 28, 70, 70, 180, 90);
            g.DrawLine(linePen, 28, 63, 28, 88); g.DrawLine(linePen, 63, 28, 92, 28);
            g.DrawArc(thinPen, 44, 44, 38, 38, 180, 90);
        }

        private static void DrawTransition(Graphics g, Pen pen, Brush fill)
        {
            PointF[] poly = { new(20, 44), new(58, 34), new(108, 48), new(108, 78), new(58, 92), new(20, 82) };
            g.FillPolygon(fill, poly); g.DrawPolygon(pen, poly);
            g.DrawRectangle(pen, 20, 44, 30, 38); g.DrawRectangle(pen, 86, 48, 22, 30);
        }

        private static void DrawTee(Graphics g, Pen pen)
        {
            g.DrawLine(pen, 22, 66, 106, 66); g.DrawLine(pen, 64, 66, 64, 28); g.DrawLine(pen, 50, 28, 78, 28);
        }

        private static void DrawTap(Graphics g, Pen pen)
        {
            g.DrawLine(pen, 20, 72, 108, 72); g.DrawLine(pen, 70, 70, 92, 40); g.DrawLine(pen, 82, 38, 104, 54);
        }

        private static void DrawCap(Graphics g, Pen pen, Brush fill, Brush dark)
        {
            g.FillRectangle(fill, 28, 48, 60, 34); g.DrawRectangle(pen, 28, 48, 60, 34);
            g.FillRectangle(dark, 86, 42, 10, 46); g.DrawRectangle(pen, 86, 42, 10, 46);
        }

        private static void DrawGrille(Graphics g, Pen pen, Brush fill)
        {
            g.FillRectangle(fill, 24, 34, 80, 60); g.DrawRectangle(pen, 24, 34, 80, 60);
            for (int y = 44; y <= 82; y += 10) g.DrawLine(pen, 32, y, 96, y);
        }

        private static void DrawDiffuser(Graphics g, Pen pen, Brush fill)
        {
            g.FillEllipse(fill, 30, 30, 68, 68); g.DrawEllipse(pen, 30, 30, 68, 68);
            g.DrawEllipse(pen, 42, 42, 44, 44); g.DrawEllipse(pen, 54, 54, 20, 20);
        }

        private static void DrawDamper(Graphics g, Pen pen, Brush fill)
        {
            g.FillRectangle(fill, 26, 42, 76, 44); g.DrawRectangle(pen, 26, 42, 76, 44);
            g.DrawLine(pen, 34, 80, 94, 48); g.DrawEllipse(pen, 58, 58, 12, 12);
        }

        private static void DrawHood(Graphics g, Pen pen, Brush fill)
        {
            PointF[] hood = { new(30, 42), new(98, 42), new(110, 76), new(18, 76) };
            g.FillPolygon(fill, hood); g.DrawPolygon(pen, hood);
            g.DrawRectangle(pen, 50, 28, 28, 14); g.DrawLine(pen, 28, 82, 100, 82);
        }

        private static void DrawFan(Graphics g, Pen pen, Brush fill, Brush dark)
        {
            g.FillEllipse(fill, 28, 28, 72, 72); g.DrawEllipse(pen, 28, 28, 72, 72); g.FillEllipse(dark, 58, 58, 12, 12);
            for (int i = 0; i < 3; i++) { g.TranslateTransform(64, 64); g.RotateTransform(120 * i); g.FillPie(fill, -8, -38, 46, 46, 200, 80); g.ResetTransform(); }
            g.DrawEllipse(pen, 58, 58, 12, 12);
        }

        private static void DrawEquipment(Graphics g, Pen pen, Brush fill, Brush light)
        {
            g.FillRectangle(fill, 26, 36, 76, 58); g.DrawRectangle(pen, 26, 36, 76, 58);
            g.FillRectangle(light, 36, 48, 28, 18); g.DrawRectangle(pen, 36, 48, 28, 18);
            g.DrawLine(pen, 74, 48, 92, 48); g.DrawLine(pen, 74, 60, 92, 60); g.DrawLine(pen, 74, 72, 92, 72);
        }

        private static void DrawUnknown(Graphics g, Pen pen, Brush dark)
        {
            g.DrawRectangle(pen, 34, 30, 60, 68);
            using var font = new Font("Arial", 36, FontStyle.Bold);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("?", font, dark, new RectangleF(34, 32, 60, 64), format);
        }

        private static void DrawCaption(Graphics graphics, string caption, string size)
        {
            using var textBrush = new SolidBrush(Color.FromArgb(54, 65, 82));
            using var font = new Font("Arial", 8, FontStyle.Regular);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            string text = string.IsNullOrWhiteSpace(size) || size == "—" ? caption : $"{caption} {size}";
            graphics.DrawString(TrimText(text, 24), font, textBrush, new RectangleF(6, 104, 116, 18), format);
        }

        private static string ResolveIconKind(string group, string name, string size)
        {
            string source = $"{group} {name} {size}";
            if (ContainsAny(source, "гибк")) return "flex";
            if (ContainsAny(source, "отвод")) return "elbow";
            if (ContainsAny(source, "переход")) return "transition";
            if (ContainsAny(source, "тройник")) return "tee";
            if (ContainsAny(source, "врез")) return "tap";
            if (ContainsAny(source, "заглуш")) return "cap";
            if (ContainsAny(source, "реш", "решет", "решёт")) return "grille";
            if (ContainsAny(source, "дифф")) return "diffuser";
            if (ContainsAny(source, "клапан", "заслон")) return "damper";
            if (ContainsAny(source, "зонт", "hood")) return "hood";
            if (ContainsAny(source, "вентил", "fan")) return "fan";
            if (ContainsAny(source, "оборуд")) return "equipment";
            if (ContainsAny(source, "воздуховод") && size.TrimStart().StartsWith("Ø", StringComparison.Ordinal)) return "duct_round";
            if (ContainsAny(source, "кругл") || size.TrimStart().StartsWith("Ø", StringComparison.Ordinal)) return "duct_round";
            if (ContainsAny(source, "воздуховод")) return "duct_rect";
            return "unknown";
        }

        private static string GetCaption(string iconKind, string group, string name)
        {
            return iconKind switch
            {
                "duct_round" => "Круглый",
                "duct_rect" => "Прямоуг.",
                "flex" => "Гибкий",
                "elbow" => "Отвод",
                "transition" => "Переход",
                "tee" => "Тройник",
                "tap" => "Врезка",
                "cap" => "Заглушка",
                "grille" => "Решётка",
                "diffuser" => "Диффузор",
                "damper" => "Клапан",
                "hood" => "Зонт",
                "fan" => "Вентилятор",
                "equipment" => "Оборуд.",
                _ => string.IsNullOrWhiteSpace(group) ? TrimText(name, 12) : TrimText(group, 12)
            };
        }

        private static Rectangle FitInside(int sourceWidth, int sourceHeight, Rectangle bounds)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0) return bounds;
            double scale = Math.Min(bounds.Width / (double)sourceWidth, bounds.Height / (double)sourceHeight);
            int width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            int height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
            return new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height);
        }

        private static bool ContainsAny(string text, params string[] tokens) => tokens.Any(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);

        private static string TrimText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, Math.Max(1, maxLength - 1)) + "…";
        }
    }
}
