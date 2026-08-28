using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using CmdsManager.Domain;

namespace CmdsManager.Presentation.Controls
{
    internal static class FolderIconRenderer
    {
        internal static readonly string[] ColorPalette =
        {
            "#64748B", "#2563EB", "#0891B2", "#059669", "#CA8A04",
            "#EA580C", "#DC2626", "#DB2777", "#7C3AED", "#4F46E5"
        };

        internal static Color ParseColor(string value, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(value)) return fallback;
                return ColorTranslator.FromHtml(value);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        internal static void Draw(Graphics graphics, Rectangle bounds, FolderIconKind icon, Color color)
        {
            if (graphics == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            var state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var scale = Math.Min(bounds.Width, bounds.Height) / 24f;
                graphics.TranslateTransform(bounds.Left + (bounds.Width - 24f * scale) / 2f,
                    bounds.Top + (bounds.Height - 24f * scale) / 2f);
                graphics.ScaleTransform(scale, scale);
                using (var pen = new Pen(color, 1.75f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                })
                using (var brush = new SolidBrush(color))
                {
                    switch (icon)
                    {
                        case FolderIconKind.Terminal:
                            DrawTerminal(graphics, pen);
                            break;
                        case FolderIconKind.Code:
                            DrawCode(graphics, pen);
                            break;
                        case FolderIconKind.Lightning:
                            graphics.FillPolygon(brush, new[]
                            {
                                new PointF(13.2f, 1.8f), new PointF(5.8f, 13f), new PointF(11f, 13f),
                                new PointF(9.8f, 22.2f), new PointF(18.4f, 9.7f), new PointF(13.1f, 9.7f)
                            });
                            break;
                        case FolderIconKind.Rocket:
                            DrawRocket(graphics, pen, brush);
                            break;
                        case FolderIconKind.Gear:
                            DrawGear(graphics, pen);
                            break;
                        case FolderIconKind.Database:
                            DrawDatabase(graphics, pen);
                            break;
                        case FolderIconKind.Server:
                            DrawServer(graphics, pen, brush);
                            break;
                        case FolderIconKind.Globe:
                            DrawGlobe(graphics, pen);
                            break;
                        case FolderIconKind.Star:
                            graphics.FillPolygon(brush, StarPoints(12f, 12f, 10f, 4.5f));
                            break;
                        default:
                            DrawFolder(graphics, pen, brush);
                            break;
                    }
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        internal static void DrawScript(Graphics graphics, Rectangle bounds, Color color)
        {
            if (graphics == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            var state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var scale = Math.Min(bounds.Width, bounds.Height) / 24f;
                graphics.TranslateTransform(bounds.Left + (bounds.Width - 24f * scale) / 2f,
                    bounds.Top + (bounds.Height - 24f * scale) / 2f);
                graphics.ScaleTransform(scale, scale);
                using (var pen = new Pen(color, 1.55f) { LineJoin = LineJoin.Round })
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddLines(new[]
                        {
                            new PointF(6f, 2.5f), new PointF(14.5f, 2.5f), new PointF(19f, 7f),
                            new PointF(19f, 21.5f), new PointF(6f, 21.5f), new PointF(6f, 2.5f)
                        });
                        graphics.DrawPath(pen, path);
                    }
                    graphics.DrawLine(pen, 14.5f, 2.8f, 14.5f, 7.2f);
                    graphics.DrawLine(pen, 14.5f, 7.2f, 18.7f, 7.2f);
                    graphics.DrawLine(pen, 8.8f, 12f, 11.2f, 14f);
                    graphics.DrawLine(pen, 11.2f, 14f, 8.8f, 16f);
                    graphics.DrawLine(pen, 13f, 16.2f, 16f, 16.2f);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static void DrawFolder(Graphics graphics, Pen pen, Brush brush)
        {
            using (var fill = new SolidBrush(Color.FromArgb(42, ((SolidBrush)brush).Color)))
            using (var path = new GraphicsPath())
            {
                path.AddLine(2f, 6.5f, 9f, 6.5f);
                path.AddLine(9f, 6.5f, 11f, 9f);
                path.AddLine(11f, 9f, 21.5f, 9f);
                path.AddLine(21.5f, 9f, 21.5f, 19.5f);
                path.AddLine(21.5f, 19.5f, 2f, 19.5f);
                path.CloseFigure();
                graphics.FillPath(fill, path);
                graphics.DrawPath(pen, path);
            }
        }

        private static void DrawTerminal(Graphics graphics, Pen pen)
        {
            graphics.DrawRoundedRectangle(pen, new RectangleF(2.5f, 4f, 19f, 16f), 2.5f);
            graphics.DrawLine(pen, 6f, 9f, 9.2f, 12f);
            graphics.DrawLine(pen, 9.2f, 12f, 6f, 15f);
            graphics.DrawLine(pen, 11.5f, 15f, 16.5f, 15f);
        }

        private static void DrawCode(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 9.2f, 5f, 3.2f, 12f);
            graphics.DrawLine(pen, 3.2f, 12f, 9.2f, 19f);
            graphics.DrawLine(pen, 14.8f, 5f, 20.8f, 12f);
            graphics.DrawLine(pen, 20.8f, 12f, 14.8f, 19f);
            graphics.DrawLine(pen, 14f, 3.5f, 10f, 20.5f);
        }

        private static void DrawRocket(Graphics graphics, Pen pen, Brush brush)
        {
            using (var path = new GraphicsPath())
            {
                path.AddBezier(8f, 15.5f, 8f, 8f, 12f, 3f, 18.5f, 2f);
                path.AddBezier(18.5f, 2f, 19.2f, 8.5f, 15.5f, 13.2f, 8f, 15.5f);
                graphics.DrawPath(pen, path);
            }
            graphics.DrawEllipse(pen, 13.1f, 6f, 3.2f, 3.2f);
            graphics.DrawLine(pen, 8.5f, 12f, 4.2f, 15.5f);
            graphics.DrawLine(pen, 4.2f, 15.5f, 8.3f, 17f);
            graphics.DrawLine(pen, 14.8f, 13.5f, 14.5f, 19.2f);
            graphics.DrawLine(pen, 14.5f, 19.2f, 11.2f, 15.5f);
            graphics.FillPolygon(brush, new[] { new PointF(8.5f, 17f), new PointF(6.5f, 22f), new PointF(11.2f, 18f) });
        }

        private static void DrawGear(Graphics graphics, Pen pen)
        {
            graphics.DrawPolygon(pen, GearPoints());
            graphics.DrawEllipse(pen, 8.6f, 8.6f, 6.8f, 6.8f);
        }

        private static void DrawDatabase(Graphics graphics, Pen pen)
        {
            graphics.DrawEllipse(pen, 3f, 3f, 18f, 6f);
            graphics.DrawArc(pen, 3f, 8f, 18f, 6f, 0, 180);
            graphics.DrawArc(pen, 3f, 13f, 18f, 6f, 0, 180);
            graphics.DrawArc(pen, 3f, 16f, 18f, 6f, 0, 180);
            graphics.DrawLine(pen, 3f, 6f, 3f, 19f);
            graphics.DrawLine(pen, 21f, 6f, 21f, 19f);
        }

        private static void DrawServer(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.DrawRoundedRectangle(pen, new RectangleF(3f, 3f, 18f, 7f), 1.8f);
            graphics.DrawRoundedRectangle(pen, new RectangleF(3f, 14f, 18f, 7f), 1.8f);
            graphics.FillEllipse(brush, 6f, 5.5f, 2f, 2f);
            graphics.FillEllipse(brush, 6f, 16.5f, 2f, 2f);
            graphics.DrawLine(pen, 11f, 6.5f, 18f, 6.5f);
            graphics.DrawLine(pen, 11f, 17.5f, 18f, 17.5f);
        }

        private static void DrawGlobe(Graphics graphics, Pen pen)
        {
            graphics.DrawEllipse(pen, 2.5f, 2.5f, 19f, 19f);
            graphics.DrawEllipse(pen, 7.5f, 2.5f, 9f, 19f);
            graphics.DrawLine(pen, 3f, 12f, 21f, 12f);
            graphics.DrawArc(pen, 4.2f, 6f, 15.6f, 12f, 0, 180);
            graphics.DrawArc(pen, 4.2f, 6f, 15.6f, 12f, 180, 180);
        }

        private static PointF[] StarPoints(float centerX, float centerY, float outerRadius, float innerRadius)
        {
            var points = new PointF[10];
            for (var index = 0; index < points.Length; index++)
            {
                var angle = -Math.PI / 2d + index * Math.PI / 5d;
                var radius = index % 2 == 0 ? outerRadius : innerRadius;
                points[index] = new PointF(centerX + (float)Math.Cos(angle) * radius,
                    centerY + (float)Math.Sin(angle) * radius);
            }
            return points;
        }

        private static PointF[] GearPoints()
        {
            var points = new PointF[32];
            for (var tooth = 0; tooth < 8; tooth++)
            {
                var centerAngle = -Math.PI / 2d + tooth * Math.PI / 4d;
                SetPolar(points, tooth * 4, centerAngle - Math.PI / 8d, 8f);
                SetPolar(points, tooth * 4 + 1, centerAngle - Math.PI / 18d, 10f);
                SetPolar(points, tooth * 4 + 2, centerAngle + Math.PI / 18d, 10f);
                SetPolar(points, tooth * 4 + 3, centerAngle + Math.PI / 8d, 8f);
            }
            return points;
        }

        private static void SetPolar(PointF[] points, int index, double angle, float radius)
        {
            points[index] = new PointF(12f + (float)Math.Cos(angle) * radius,
                12f + (float)Math.Sin(angle) * radius);
        }

        private static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
        {
            using (var path = new GraphicsPath())
            {
                var diameter = radius * 2f;
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.DrawPath(pen, path);
            }
        }
    }
}
