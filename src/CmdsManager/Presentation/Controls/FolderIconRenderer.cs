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
            "#111111", "#DC2626", "#EA580C", "#CA8A04", "#059669", "#2563EB",
            "#7C3AED", "#DB2777", "#64748B", "#0891B2", "#4F46E5", "#FBBF24"
        };

        internal static readonly FolderIconKind[] PickerIcons =
        {
            FolderIconKind.Folder, FolderIconKind.Money, FolderIconKind.Book,
            FolderIconKind.Graduation, FolderIconKind.Pencil, FolderIconKind.Star,
            FolderIconKind.Code, FolderIconKind.Terminal, FolderIconKind.Music,
            FolderIconKind.Trash, FolderIconKind.Scissors, FolderIconKind.Palette,
            FolderIconKind.Stethoscope, FolderIconKind.Gear, FolderIconKind.Lotus,
            FolderIconKind.Briefcase, FolderIconKind.Chart, FolderIconKind.Database,
            FolderIconKind.Dumbbell, FolderIconKind.Server, FolderIconKind.Scales,
            FolderIconKind.Globe, FolderIconKind.Rocket, FolderIconKind.Lightning,
            FolderIconKind.Wrench, FolderIconKind.Paw, FolderIconKind.Flask,
            FolderIconKind.Brain, FolderIconKind.Heart, FolderIconKind.Gift
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
                        case FolderIconKind.Money:
                            DrawMoney(graphics, pen);
                            break;
                        case FolderIconKind.Book:
                            DrawBook(graphics, pen);
                            break;
                        case FolderIconKind.Graduation:
                            DrawGraduation(graphics, pen, brush);
                            break;
                        case FolderIconKind.Pencil:
                            DrawPencil(graphics, pen);
                            break;
                        case FolderIconKind.Music:
                            DrawMusic(graphics, pen, brush);
                            break;
                        case FolderIconKind.Trash:
                            DrawTrash(graphics, pen);
                            break;
                        case FolderIconKind.Scissors:
                            DrawScissors(graphics, pen);
                            break;
                        case FolderIconKind.Palette:
                            DrawPalette(graphics, pen, brush);
                            break;
                        case FolderIconKind.Stethoscope:
                            DrawStethoscope(graphics, pen);
                            break;
                        case FolderIconKind.Lotus:
                            DrawLotus(graphics, pen);
                            break;
                        case FolderIconKind.Briefcase:
                            DrawBriefcase(graphics, pen);
                            break;
                        case FolderIconKind.Chart:
                            DrawChart(graphics, pen);
                            break;
                        case FolderIconKind.Dumbbell:
                            DrawDumbbell(graphics, pen);
                            break;
                        case FolderIconKind.Scales:
                            DrawScales(graphics, pen);
                            break;
                        case FolderIconKind.Wrench:
                            DrawWrench(graphics, pen);
                            break;
                        case FolderIconKind.Paw:
                            DrawPaw(graphics, pen, brush);
                            break;
                        case FolderIconKind.Flask:
                            DrawFlask(graphics, pen);
                            break;
                        case FolderIconKind.Brain:
                            DrawBrain(graphics, pen);
                            break;
                        case FolderIconKind.Heart:
                            DrawHeart(graphics, pen);
                            break;
                        case FolderIconKind.Gift:
                            DrawGift(graphics, pen);
                            break;
                        default:
                            DrawFolder(graphics, pen);
                            break;
                    }
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static void DrawFolder(Graphics graphics, Pen pen)
        {
            using (var path = new GraphicsPath())
            {
                path.AddLine(3f, 7.5f, 3f, 6.5f);
                path.AddBezier(3f, 6.5f, 3f, 5.4f, 3.9f, 4.5f, 5f, 4.5f);
                path.AddLine(5f, 4.5f, 9.2f, 4.5f);
                path.AddBezier(9.2f, 4.5f, 9.8f, 4.5f, 10.3f, 4.8f, 10.7f, 5.3f);
                path.AddLine(10.7f, 5.3f, 12.3f, 7.5f);
                path.AddLine(12.3f, 7.5f, 19f, 7.5f);
                path.AddBezier(19f, 7.5f, 20.1f, 7.5f, 21f, 8.4f, 21f, 9.5f);
                path.AddLine(21f, 9.5f, 21f, 18f);
                path.AddBezier(21f, 18f, 21f, 19.1f, 20.1f, 20f, 19f, 20f);
                path.AddLine(19f, 20f, 5f, 20f);
                path.AddBezier(5f, 20f, 3.9f, 20f, 3f, 19.1f, 3f, 18f);
                path.CloseFigure();
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

        private static void DrawMoney(Graphics graphics, Pen pen)
        {
            graphics.DrawEllipse(pen, 3f, 3f, 18f, 18f);
            graphics.DrawLine(pen, 12f, 6.5f, 12f, 17.5f);
            graphics.DrawBezier(pen, 15.2f, 8f, 13.8f, 5.8f, 8.8f, 7f, 9f, 10f);
            graphics.DrawBezier(pen, 9f, 10f, 9.2f, 12f, 15.3f, 11.2f, 15f, 15f);
            graphics.DrawBezier(pen, 15f, 15f, 14.8f, 17.8f, 9.2f, 18f, 8.3f, 15.6f);
        }

        private static void DrawBook(Graphics graphics, Pen pen)
        {
            graphics.DrawRoundedRectangle(pen, new RectangleF(4f, 3f, 16f, 18f), 1.8f);
            graphics.DrawLine(pen, 8f, 3.5f, 8f, 20.5f);
            graphics.DrawLine(pen, 11f, 7f, 17f, 7f);
            graphics.DrawLine(pen, 11f, 11f, 17f, 11f);
        }

        private static void DrawGraduation(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.DrawPolygon(pen, new[]
            {
                new PointF(2.5f, 9f), new PointF(12f, 4f), new PointF(21.5f, 9f),
                new PointF(12f, 14f)
            });
            graphics.DrawBezier(pen, 6f, 11.3f, 6.4f, 17f, 17.5f, 17f, 18f, 11.3f);
            graphics.DrawLine(pen, 20f, 10f, 20f, 17f);
            graphics.FillEllipse(brush, 18.7f, 16.2f, 2.6f, 2.6f);
        }

        private static void DrawPencil(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 5f, 19f, 8f, 13f);
            graphics.DrawLine(pen, 8f, 13f, 16.5f, 4.5f);
            graphics.DrawLine(pen, 16.5f, 4.5f, 20f, 8f);
            graphics.DrawLine(pen, 20f, 8f, 11.5f, 16.5f);
            graphics.DrawLine(pen, 11.5f, 16.5f, 5f, 19f);
            graphics.DrawLine(pen, 8f, 13f, 11.5f, 16.5f);
            graphics.DrawLine(pen, 5f, 19f, 4f, 20f);
        }

        private static void DrawMusic(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.DrawLine(pen, 9f, 5f, 9f, 17f);
            graphics.DrawLine(pen, 9f, 5f, 18f, 3.5f);
            graphics.DrawLine(pen, 18f, 3.5f, 18f, 14.5f);
            graphics.DrawLine(pen, 9f, 8.5f, 18f, 7f);
            graphics.FillEllipse(brush, 4.5f, 15f, 5f, 4f);
            graphics.FillEllipse(brush, 13.5f, 12.5f, 5f, 4f);
        }

        private static void DrawTrash(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 5f, 7f, 19f, 7f);
            graphics.DrawLine(pen, 9f, 4f, 15f, 4f);
            graphics.DrawLine(pen, 7f, 7.5f, 8f, 20f);
            graphics.DrawLine(pen, 8f, 20f, 16f, 20f);
            graphics.DrawLine(pen, 16f, 20f, 17f, 7.5f);
            graphics.DrawLine(pen, 10.5f, 10f, 10.8f, 17f);
            graphics.DrawLine(pen, 13.5f, 10f, 13.2f, 17f);
        }

        private static void DrawScissors(Graphics graphics, Pen pen)
        {
            graphics.DrawEllipse(pen, 3f, 14f, 6f, 6f);
            graphics.DrawEllipse(pen, 15f, 14f, 6f, 6f);
            graphics.DrawLine(pen, 8f, 15f, 18f, 4f);
            graphics.DrawLine(pen, 16f, 15f, 6f, 4f);
            graphics.DrawEllipse(pen, 10.5f, 10.5f, 3f, 3f);
        }

        private static void DrawPalette(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.DrawBezier(pen, 12f, 3f, 3f, 3f, 1.8f, 16f, 9f, 20f);
            graphics.DrawBezier(pen, 9f, 20f, 13f, 22f, 13f, 17f, 16.5f, 17f);
            graphics.DrawBezier(pen, 16.5f, 17f, 22f, 17f, 23f, 6f, 12f, 3f);
            graphics.FillEllipse(brush, 7f, 7f, 2.5f, 2.5f);
            graphics.FillEllipse(brush, 12f, 5.5f, 2.5f, 2.5f);
            graphics.FillEllipse(brush, 16.5f, 8f, 2.5f, 2.5f);
            graphics.FillEllipse(brush, 7f, 12f, 2.5f, 2.5f);
        }

        private static void DrawStethoscope(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 6f, 4f, 6f, 10f);
            graphics.DrawLine(pen, 15f, 4f, 15f, 10f);
            graphics.DrawArc(pen, 6f, 6f, 9f, 10f, 0f, 180f);
            graphics.DrawBezier(pen, 10.5f, 16f, 10.5f, 22f, 19f, 22f, 19f, 16.5f);
            graphics.DrawEllipse(pen, 16.5f, 13.5f, 5f, 5f);
            graphics.DrawLine(pen, 4.5f, 4f, 7.5f, 4f);
            graphics.DrawLine(pen, 13.5f, 4f, 16.5f, 4f);
        }

        private static void DrawLotus(Graphics graphics, Pen pen)
        {
            graphics.DrawBezier(pen, 12f, 19f, 6f, 15f, 7f, 7f, 12f, 3f);
            graphics.DrawBezier(pen, 12f, 3f, 17f, 7f, 18f, 15f, 12f, 19f);
            graphics.DrawBezier(pen, 11.5f, 18.5f, 5f, 19f, 2.5f, 14f, 3f, 9f);
            graphics.DrawBezier(pen, 3f, 9f, 8f, 10f, 10f, 14f, 12f, 19f);
            graphics.DrawBezier(pen, 12.5f, 18.5f, 19f, 19f, 21.5f, 14f, 21f, 9f);
            graphics.DrawBezier(pen, 21f, 9f, 16f, 10f, 14f, 14f, 12f, 19f);
            graphics.DrawLine(pen, 4f, 21f, 20f, 21f);
        }

        private static void DrawBriefcase(Graphics graphics, Pen pen)
        {
            graphics.DrawRoundedRectangle(pen, new RectangleF(3f, 7f, 18f, 13f), 2f);
            graphics.DrawArc(pen, 8f, 2f, 8f, 8f, 180f, 180f);
            graphics.DrawLine(pen, 3.5f, 12f, 20.5f, 12f);
            graphics.DrawLine(pen, 11f, 10.5f, 13f, 10.5f);
            graphics.DrawLine(pen, 11f, 13.5f, 13f, 13.5f);
        }

        private static void DrawChart(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 4f, 3f, 4f, 20f);
            graphics.DrawLine(pen, 4f, 20f, 21f, 20f);
            graphics.DrawRoundedRectangle(pen, new RectangleF(7f, 13f, 2.8f, 5f), 0.8f);
            graphics.DrawRoundedRectangle(pen, new RectangleF(12f, 9f, 2.8f, 9f), 0.8f);
            graphics.DrawRoundedRectangle(pen, new RectangleF(17f, 5f, 2.8f, 13f), 0.8f);
        }

        private static void DrawDumbbell(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 8f, 12f, 16f, 12f);
            graphics.DrawRoundedRectangle(pen, new RectangleF(5f, 7f, 3f, 10f), 1f);
            graphics.DrawRoundedRectangle(pen, new RectangleF(16f, 7f, 3f, 10f), 1f);
            graphics.DrawLine(pen, 3f, 9f, 3f, 15f);
            graphics.DrawLine(pen, 21f, 9f, 21f, 15f);
            graphics.DrawLine(pen, 3f, 12f, 5f, 12f);
            graphics.DrawLine(pen, 19f, 12f, 21f, 12f);
        }

        private static void DrawScales(Graphics graphics, Pen pen)
        {
            graphics.DrawLine(pen, 12f, 3f, 12f, 20f);
            graphics.DrawLine(pen, 5f, 6f, 19f, 6f);
            graphics.DrawLine(pen, 7f, 6f, 3.5f, 14f);
            graphics.DrawLine(pen, 17f, 6f, 20.5f, 14f);
            graphics.DrawArc(pen, 2f, 10f, 7f, 7f, 0f, 180f);
            graphics.DrawArc(pen, 15f, 10f, 7f, 7f, 0f, 180f);
            graphics.DrawLine(pen, 7f, 20f, 17f, 20f);
        }

        private static void DrawWrench(Graphics graphics, Pen pen)
        {
            graphics.DrawBezier(pen, 6f, 14f, 1f, 11f, 2.5f, 4f, 6.5f, 3f);
            graphics.DrawLine(pen, 6.5f, 3f, 8f, 7f);
            graphics.DrawLine(pen, 8f, 7f, 12f, 8f);
            graphics.DrawLine(pen, 12f, 8f, 19.5f, 15.5f);
            graphics.DrawEllipse(pen, 17f, 14f, 5f, 5f);
            graphics.DrawLine(pen, 17.8f, 19f, 10f, 11.5f);
            graphics.DrawBezier(pen, 10f, 11.5f, 8f, 14f, 7f, 14.5f, 6f, 14f);
        }

        private static void DrawPaw(Graphics graphics, Pen pen, Brush brush)
        {
            graphics.FillEllipse(brush, 8f, 10f, 8f, 9f);
            graphics.FillEllipse(brush, 3.5f, 8f, 4f, 5f);
            graphics.FillEllipse(brush, 7f, 3.5f, 4f, 5f);
            graphics.FillEllipse(brush, 13f, 3.5f, 4f, 5f);
            graphics.FillEllipse(brush, 16.5f, 8f, 4f, 5f);
            graphics.DrawEllipse(pen, 8f, 10f, 8f, 9f);
        }

        private static void DrawFlask(Graphics graphics, Pen pen)
        {
            using (var path = new GraphicsPath())
            {
                path.AddLine(9f, 3f, 15f, 3f);
                path.AddLine(14f, 3f, 14f, 9f);
                path.AddLine(14f, 9f, 20f, 19f);
                path.AddBezier(20f, 19f, 21f, 21f, 3f, 21f, 4f, 19f);
                path.AddLine(4f, 19f, 10f, 9f);
                path.AddLine(10f, 9f, 10f, 3f);
                graphics.DrawPath(pen, path);
            }
            graphics.DrawLine(pen, 7f, 15f, 17f, 15f);
            graphics.DrawEllipse(pen, 10f, 17f, 1.5f, 1.5f);
            graphics.DrawEllipse(pen, 14f, 18f, 1.5f, 1.5f);
        }

        private static void DrawBrain(Graphics graphics, Pen pen)
        {
            graphics.DrawBezier(pen, 12f, 5f, 9f, 1f, 4f, 4f, 5f, 8f);
            graphics.DrawBezier(pen, 5f, 8f, 1f, 9f, 2.5f, 15f, 6f, 15f);
            graphics.DrawBezier(pen, 6f, 15f, 5f, 20f, 11.5f, 22f, 12f, 17f);
            graphics.DrawBezier(pen, 12f, 5f, 15f, 1f, 20f, 4f, 19f, 8f);
            graphics.DrawBezier(pen, 19f, 8f, 23f, 9f, 21.5f, 15f, 18f, 15f);
            graphics.DrawBezier(pen, 18f, 15f, 19f, 20f, 12.5f, 22f, 12f, 17f);
            graphics.DrawLine(pen, 12f, 5f, 12f, 17f);
            graphics.DrawArc(pen, 6f, 7f, 6f, 5f, 180f, 180f);
            graphics.DrawArc(pen, 12f, 10f, 6f, 5f, 0f, 180f);
        }

        private static void DrawHeart(Graphics graphics, Pen pen)
        {
            using (var path = new GraphicsPath())
            {
                path.AddBezier(12f, 20f, 9f, 17f, 3f, 13f, 3f, 8f);
                path.AddBezier(3f, 8f, 3f, 3f, 10f, 2f, 12f, 7f);
                path.AddBezier(12f, 7f, 14f, 2f, 21f, 3f, 21f, 8f);
                path.AddBezier(21f, 8f, 21f, 13f, 15f, 17f, 12f, 20f);
                graphics.DrawPath(pen, path);
            }
        }

        private static void DrawGift(Graphics graphics, Pen pen)
        {
            graphics.DrawRoundedRectangle(pen, new RectangleF(4f, 9f, 16f, 12f), 1.5f);
            graphics.DrawRoundedRectangle(pen, new RectangleF(3f, 6f, 18f, 4f), 1f);
            graphics.DrawLine(pen, 12f, 6f, 12f, 21f);
            graphics.DrawBezier(pen, 12f, 6f, 6f, 6f, 6f, 1.5f, 9f, 2f);
            graphics.DrawBezier(pen, 9f, 2f, 11f, 2.3f, 12f, 6f, 12f, 6f);
            graphics.DrawBezier(pen, 12f, 6f, 18f, 6f, 18f, 1.5f, 15f, 2f);
            graphics.DrawBezier(pen, 15f, 2f, 13f, 2.3f, 12f, 6f, 12f, 6f);
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
