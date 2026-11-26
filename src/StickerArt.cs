using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

internal static partial class StickerArt
{
    private static readonly Color Gold = Color.FromArgb(246, 195, 77);
    private static readonly Color Green = Color.FromArgb(167, 204, 105);
    private static readonly Color Moss = Color.FromArgb(102, 136, 60);
    private static readonly Color Mint = Color.FromArgb(207, 229, 144);
    private static readonly Color Blush = Color.FromArgb(240, 155, 145);

    public static Bitmap Render(int kind, int theme, int phase, int pose, double facing, bool walking, int width, double lean)
    {
        if (kind == PetArt.Beagle) return RenderBeagle(theme, phase, pose, facing, walking, width, lean);
        Bitmap result = new Bitmap(width, Math.Max(1, (int)Math.Round(width * .95)), PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(result))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(width / 142f, result.Height / 135f);
            Color accent = theme == 0 ? Color.FromArgb(131, 198, 221) : Color.FromArgb(228, 156, 196);
            double t = phase * Math.PI * 2 / 48;
            if (pose == 1) Ellipse(g, Color.FromArgb(90, accent), 16, 100, 110, 22, false);
            else if (pose != 4 && pose != 5) Ellipse(g, Color.FromArgb(25, 22, 35, 44), 29, 112, 83, 13, false);

            GraphicsState body = g.Save();
            bool mirrored = facing >= 180;
            if (mirrored) { g.TranslateTransform(142, 0); g.ScaleTransform(-1, 1); lean = -lean; }
            if (pose == 1)
            {
                g.TranslateTransform(71, 83);
                g.RotateTransform(-58);
                g.ScaleTransform(.86f, .86f);
                g.TranslateTransform(-69, -77);
            }
            else if (pose == 5)
            {
                g.TranslateTransform(71, 65);
                g.RotateTransform((float)(lean + Math.Sin(t * 3) * 9));
                g.TranslateTransform(-71, -68 - (float)(Math.Sin(t * 4) * 3));
            }
            else if (pose != 4) g.TranslateTransform(0, (float)(Math.Sin(t * 2) * (walking ? 2.1 : .8)));
            if (kind == PetArt.Matcha) DrawCrocodile(g, pose, t);
            else if (kind == PetArt.Lucky) DrawLuckyCat(g, pose, t, mirrored);
            else DrawRobot(g, pose, accent);
            g.Restore(body);
            if (pose == 1) DrawSleepMark(g, accent);
            if (pose == 2 || pose == 3) DrawDevice(g, pose, accent, t);
        }
        return result;
    }

    private static void DrawCrocodile(Graphics g, int pose, double t)
    {
        if (pose != 4)
        {
            Shape(g, Green, 80, 112, 97, 116, 119, 115, 126, 105, 129, 101, 126, 98, 122, 101, 109, 107, 100, 105, 91, 99, 85, 99, 81, 105, 80, 112);
            Shape(g, Moss, 92, 104, 94, 100, 95, 95, 98, 95, 101, 96, 103, 102, 104, 106, 100, 107, 96, 106, 92, 104);
            Shape(g, Moss, 105, 107, 106, 103, 108, 99, 111, 99, 115, 100, 116, 103, 117, 105, 114, 107, 109, 108, 105, 107);
            Ellipse(g, Green, 41, 65, 57, 54);
            Ellipse(g, Mint, 49, 82, 40, 33, false);
            Ellipse(g, Green, 39, 108, 24, 12);
            Ellipse(g, Green, 77, 108, 24, 12);
            Shape(g, Green, 45, 85, 38, 87, 37, 93, 39, 100, 41, 105, 48, 105, 51, 100, 53, 96, 52, 89, 45, 85);
            Shape(g, Green, 92, 83, 99, 83, 103, 90, 103, 95, 104, 101, 98, 104, 94, 100, 91, 97, 90, 89, 92, 83);
        }
        Shape(g, Moss, 78, 42, 81, 31, 86, 22, 90, 23, 94, 24, 95, 35, 96, 40, 100, 38, 103, 32, 107, 34, 111, 35, 112, 42, 109, 48, 114, 46, 119, 44, 121, 47, 123, 50, 119, 58, 115, 63, 100, 61, 91, 53, 78, 42);
        Shape(g, Green, 33, 58, 29, 54, 28, 49, 32, 45, 37, 39, 44, 42, 50, 43, 52, 30, 59, 24, 67, 26, 75, 25, 82, 29, 86, 36, 92, 31, 99, 32, 103, 38, 107, 42, 107, 48, 107, 53, 115, 57, 119, 67, 112, 75, 103, 87, 77, 89, 56, 83, 43, 79, 34, 71, 33, 58);
        Shape(g, Mint, false, 33, 60, 39, 55, 56, 55, 67, 56, 85, 56, 103, 60, 112, 65, 112, 75, 97, 81, 80, 83, 58, 82, 42, 75, 36, 69, 34, 66, 33, 63, 33, 60);
        Ellipse(g, Moss, 39, 57, 3.6f, 3, false);
        Ellipse(g, Moss, 47, 58, 3.5f, 3, false);
        Shape(g, Color.Empty, 35, 64, 53, 80, 86, 83, 108, 65);
        if (pose == 5) Ellipse(g, Moss, 69, 68, 10, 11, false);
        for (int i = 0; i < 2; i++)
        {
            float x = 53 + i * 23, y = 33 + i * 2;
            if (pose == 1) Shape(g, Color.Empty, x, y + 12, x + 4, y + 16, x + 13, y + 17, x + 18, y + 12);
            else
            {
                Ellipse(g, Cream, x, y, 21, 23);
                Ellipse(g, Moss, x + 5, y + 5, pose == 5 ? 10 : 8, pose == 5 ? 11 : 9, false);
                Ellipse(g, Color.White, x + 6, y + 6, 2.6f, 2.6f, false);
            }
        }
        if (pose == 4)
        {
            Ellipse(g, Green, 27, 69, 14, 13);
            Ellipse(g, Green, 104, 73, 14, 13);
        }
    }

    private static void DrawLuckyCat(Graphics g, int pose, double t, bool mirrored)
    {
        Color red = Color.FromArgb(205, 71, 70), ginger = Color.FromArgb(231, 157, 83);
        if (pose != 4)
        {
            Shape(g, ginger, 91, 107, 108, 107, 116, 100, 112, 88, 109, 80, 115, 76, 120, 82, 127, 91, 125, 103, 119, 109, 113, 115, 101, 117, 93, 114, 90, 114, 89, 109, 91, 107);
            Ellipse(g, Cream, 41, 66, 61, 51);
            Ellipse(g, Cream, 39, 105, 27, 14);
            Ellipse(g, Cream, 80, 105, 27, 14);
            Ellipse(g, red, 47, 72, 48, 15);
            Ellipse(g, Gold, 65, 79, 12, 11);
            Ellipse(g, Gold, 52, 87, 38, 27);
            Ellipse(g, Color.FromArgb(255, 222, 126), 56, 90, 30, 21);
            using (Font font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                GraphicsState coin = g.Save();
                if (mirrored) { g.TranslateTransform(142, 0); g.ScaleTransform(-1, 1); }
                g.DrawString("福", font, Brushes.SaddleBrown, new RectangleF(54, 90, 34, 21), center);
                g.Restore(coin);
            }

        }
        Shape(g, Cream, 31, 60, 26, 52, 25, 37, 28, 25, 34, 25, 46, 31, 54, 38, 66, 34, 79, 34, 91, 39, 99, 31, 109, 25, 115, 26, 118, 36, 117, 48, 112, 57, 119, 67, 115, 77, 104, 82, 91, 89, 54, 89, 39, 82, 29, 77, 26, 69, 31, 60);
        Shape(g, ginger, false, 31, 59, 27, 50, 26, 35, 29, 26, 34, 26, 46, 32, 54, 39, 49, 45, 42, 57, 31, 59);
        Shape(g, Blush, false, 32, 46, 30, 40, 30, 34, 31, 31, 37, 33, 42, 36, 46, 40, 43, 44, 36, 45, 32, 46);
        Shape(g, Blush, false, 100, 40, 104, 36, 108, 33, 112, 31, 114, 35, 113, 41, 113, 46, 108, 46, 103, 44, 100, 40);
        Ellipse(g, Blush, 38, 66, 15, 7, false);
        Ellipse(g, Blush, 90, 66, 15, 7, false);
        for (int i = 0; i < 2; i++)
        {
            float x = 48 + i * 32;
            if (pose == 1) Shape(g, Color.Empty, x, 60, x + 5, 65, x + 10, 65, x + 15, 60);
            else
            {
                Ellipse(g, Black, x + 2, pose == 5 ? 52 : 56, pose == 5 ? 12 : 8, pose == 5 ? 15 : 10, false);
                Ellipse(g, Color.White, x + 4, pose == 5 ? 54 : 57, 2.8f, 3, false);
            }
        }
        Ellipse(g, Color.FromArgb(161, 99, 103), 67, 65, 8, 5, false);
        if (pose == 5) Ellipse(g, Black, 67, 73, 8, 9, false);
        else Shape(g, Color.Empty, 62, 73, 65, 77, 69, 75, 71, 72, 73, 75, 78, 77, 81, 73);
        if (pose != 4)
        {
            GraphicsState paw = g.Save();
            g.TranslateTransform(3, -3);
            if (pose == 0)
            {
                g.TranslateTransform(98, 87);
                g.RotateTransform((float)(Math.Sin(t * 2) * 13));
                g.TranslateTransform(-98, -87);
            }
            Shape(g, Cream, 95, 98, 92, 90, 94, 82, 99, 77, 103, 72, 105, 62, 112, 62, 120, 60, 125, 65, 125, 71, 126, 77, 120, 82, 116, 84, 114, 93, 107, 100, 100, 102, 98, 102, 96, 101, 95, 98);
            Ellipse(g, Blush, 108, 68, 12, 8, false);
            for (int i = 0; i < 3; i++) Ellipse(g, Blush, 107 + i * 5, 64, 3, 3, false);
            g.Restore(paw);
        }
        if (pose == 4)
        {
            Ellipse(g, Cream, 27, 71, 15, 13);
            Ellipse(g, Cream, 102, 71, 15, 13);
        }
    }

    private static void DrawRobot(Graphics g, int pose, Color accent)
    {
        Color metal = Color.FromArgb(207, 227, 235);
        Color shadow = Color.FromArgb(135, 168, 187);
        Color dark = Color.FromArgb(39, 62, 80);
        if (pose != 4)
        {
            Box(g, shadow, 37, 104, 29, 14, 6);
            Box(g, shadow, 77, 104, 29, 14, 6);
            Box(g, metal, 42, 72, 58, 40, 16);
            Box(g, dark, 57, 87, 28, 13, 5);
            Box(g, accent, 61, 91, 20, 4, 2, false);
            Box(g, shadow, 30, 79, 16, 22, 6);
            Box(g, shadow, 96, 79, 16, 22, 6);
        }
        Box(g, shadow, 69, 16, 5, 22, 2);
        Ellipse(g, accent, 65, 12, 13, 12);
        Box(g, shadow, 19, 45, 15, 28, 7);
        Box(g, shadow, 108, 45, 15, 28, 7);
        Box(g, metal, 26, 32, 90, 53, 17);
        Box(g, shadow, 32, 37, 78, 43, 14);
        Box(g, dark, 35, 39, 72, 38, 12);
        Box(g, Color.FromArgb(62, Color.White), 42, 45, 35, 3, 1, false);
        for (int i = 0; i < 2; i++)
        {
            int x = 49 + i * 30;
            if (pose == 1) Box(g, accent, x, 59, 13, 3, 1, false);
            else Box(g, accent, x, pose == 5 ? 52 : 55, 13, pose == 5 ? 13 : 7, 3, false);
            if (pose != 1) Box(g, Color.FromArgb(210, Color.White), x + 3, pose == 5 ? 53 : 56, 4, 2, 1, false);
        }
        if (pose == 5) Ellipse(g, accent, 67, 66, 8, 7, false);
        else Box(g, accent, 66, 68, 10, 2, 1, false);
        if (pose == 4)
        {
            Box(g, metal, 23, 70, 15, 12, 5);
            Box(g, metal, 104, 70, 15, 12, 5);
        }
    }

    private static void DrawSleepMark(Graphics g, Color accent)
    {
        using (Pen pen = new Pen(accent, 2f) { LineJoin = LineJoin.Round })
        {
            g.DrawLines(pen, new[] { new PointF(104, 32), new PointF(113, 32), new PointF(104, 40), new PointF(113, 40) });
            g.DrawLines(pen, new[] { new PointF(116, 21), new PointF(124, 21), new PointF(116, 28), new PointF(124, 28) });
        }
    }

    private static void DrawDevice(Graphics g, int pose, Color accent, double t)
    {
        if (pose == 2)
        {
            GraphicsState phone = g.Save();
            g.TranslateTransform(84, 100);
            g.RotateTransform(-16 + (float)Math.Sin(t * 3) * 2);
            Box(g, Ink, -12, -13, 24, 28, 4);
            Box(g, accent, -9, -10, 18, 20, 2, false);
            g.Restore(phone);
            return;
        }
        Box(g, Color.FromArgb(173, 132, 96), 31, 109, 79, 7, 3);
        Shape(g, accent, 49, 105, 47, 97, 45, 88, 43, 81, 42, 78, 44, 77, 47, 77, 62, 77, 83, 77, 100, 77, 103, 77, 105, 79, 104, 82, 102, 89, 100, 99, 98, 106, 87, 107, 59, 107, 49, 105);
        Ellipse(g, Cream, 70, 89, 8, 6, false);
    }

    private static void Box(Graphics g, Color color, float x, float y, float width, float height, float radius, bool outline = true)
    {
        using (GraphicsPath path = new GraphicsPath())
        using (SolidBrush brush = new SolidBrush(color))
        {
            float d = radius * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
            if (outline) using (Pen pen = new Pen(Ink, 1.5f)) g.DrawPath(pen, path);
        }
    }
}
