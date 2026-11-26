using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

internal static class BeagleArt
{
    private static readonly Color Ink = Color.FromArgb(50, 45, 40);
    private static readonly Color Fur = Color.FromArgb(213, 164, 101);
    private static readonly Color Ear = Color.FromArgb(191, 139, 82);
    private static readonly Color Cream = Color.FromArgb(255, 250, 237);
    private static readonly Color Black = Color.FromArgb(30, 31, 29);

    public static Bitmap Render(int theme, int phase, int pose, double yaw, bool walking, int width, double lean)
    {
        Bitmap result = new Bitmap(width, Math.Max(1, (int)Math.Round(width * .95)), PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(result))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(width / 142f, result.Height / 135f);
            if (pose != 4 && yaw >= 180)
            {
                g.TranslateTransform(142, 0);
                g.ScaleTransform(-1, 1);
                lean = -lean;
            }

            Color accent = theme == 0 ? Color.FromArgb(162, 202, 220) : Color.FromArgb(231, 172, 199);
            double t = phase * Math.PI * 2 / 48;
            GraphicsState state = g.Save();
            if (pose == 1)
            {
                Ellipse(g, accent, 14, 95, 111, 21);
                g.TranslateTransform(0, 7);
            }
            else if (pose == 4) g.TranslateTransform(0, -15);
            else if (pose == 5)
            {
                g.TranslateTransform(72, 66);
                g.RotateTransform((float)(lean + Math.Sin(t * 3) * 7));
                g.TranslateTransform(-72, -66 - (float)(Math.Sin(t * 4) * 3));
            }
            else if (pose != 0) g.TranslateTransform(0, (float)(Math.Sin(t * 2) * (walking ? 1.7 : .9)));

            DrawDog(g, pose, t);
            g.Restore(state);

            if (pose == 0) DrawBowl(g);
            else if (pose == 1)
            {
                Shape(g, Color.Empty, 115, 31, 117, 30, 120, 30, 123, 30, 121, 33, 118, 35, 116, 38, 119, 38, 122, 38, 125, 37);
                Shape(g, Color.Empty, 121, 22, 123, 21, 126, 21, 128, 21, 127, 23, 125, 25, 123, 27, 125, 27, 127, 27, 130, 26);
            }
            else if (pose == 2)
            {
                GraphicsState phone = g.Save();
                g.TranslateTransform(92, 99);
                g.RotateTransform(-19 + (float)Math.Sin(t * 3) * 2);
                Shape(g, Ink, -9, -11, -9, -14, -6, -15, -4, -15, 0, -15, 4, -15, 7, -15, 10, -15, 11, -13, 11, -10, 11, -3, 11, 4, 11, 11, 11, 14, 9, 15, 6, 15, 1, 15, -3, 15, -6, 15, -9, 15, -9, 13, -9, 10, -9, 4, -9, -4, -9, -11);
                using (SolidBrush glass = new SolidBrush(accent)) g.FillRectangle(glass, -6, -11, 14, 21);
                g.Restore(phone);
            }
            else if (pose == 3)
            {
                Shape(g, Color.FromArgb(167, 135, 104), 42, 105, 65, 103, 94, 104, 120, 105, 125, 106, 125, 110, 122, 112, 99, 114, 62, 114, 42, 112, 38, 110, 38, 107, 42, 105);
                Shape(g, accent, 66, 102, 69, 94, 73, 84, 76, 78, 77, 76, 80, 75, 83, 75, 94, 75, 107, 76, 117, 77, 121, 78, 122, 81, 120, 84, 117, 92, 114, 100, 112, 105, 99, 108, 80, 108, 66, 106, 64, 105, 64, 104, 66, 102);
                Ellipse(g, Cream, 93, 88, 6, 5, false);
            }
        }
        return result;
    }

    private static void DrawDog(Graphics g, int pose, double t)
    {
        if (pose != 4)
        {
            GraphicsState tail = g.Save();
            g.TranslateTransform(38, 70);
            g.RotateTransform((float)Math.Sin(t * 2) * (pose == 1 ? 0 : 4));
            g.TranslateTransform(-38, -70);
            Shape(g, Fur, 38, 75, 26, 75, 23, 67, 22, 57, 22, 53, 22, 51, 24, 51, 27, 53, 25, 61, 31, 64, 33, 65, 37, 65, 43, 65, 43, 70, 41, 73, 38, 75);
            g.Restore(tail);
        }
        Shape(g, Cream, 15, 91, 14, 92, 17, 86, 23, 84, 26, 81, 27, 77, 31, 74, 37, 69, 48, 69, 59, 70, 67, 73, 72, 78, 75, 85, 83, 86, 88, 91, 85, 96, 80, 104, 63, 105, 51, 104, 44, 102, 39, 100, 35, 101, 29, 105, 19, 104, 17, 102, 16, 100, 17, 97, 21, 95, 18, 95, 15, 94, 15, 91);
        Shape(g, Ear, 29, 80, 34, 72, 41, 70, 51, 70, 56, 71, 61, 73, 64, 77, 58, 81, 51, 82, 46, 85, 43, 83, 36, 79, 29, 80);
        Shape(g, Color.Empty, 30, 91, 31, 88, 31, 84, 30, 82);
        Shape(g, Color.Empty, 21, 94, 25, 94, 28, 93, 29, 91);
        Shape(g, Color.Empty, 45, 88, 45, 93, 50, 100, 56, 101);

        GraphicsState head = g.Save();
        if (pose == 5)
        {
            g.TranslateTransform(72, 68);
            g.RotateTransform((float)(Math.Cos(t * 3) * 7));
            g.TranslateTransform(-72, -69);
        }
        Shape(g, Ear, 38, 69, 39, 56, 44, 42, 52, 37, 61, 31, 67, 31, 72, 33, 58, 41, 53, 58, 51, 74, 46, 77, 40, 75, 38, 69);
        Shape(g, Fur, 43, 68, 45, 63, 46, 53, 51, 44, 58, 33, 67, 30, 79, 30, 91, 29, 102, 34, 109, 43, 118, 54, 119, 68, 112, 79, 105, 90, 91, 101, 75, 101, 63, 100, 55, 93, 52, 88, 45, 84, 43, 77, 43, 68);
        Shape(g, Ear, 105, 42, 112, 36, 119, 43, 122, 51, 125, 59, 127, 74, 126, 87, 126, 94, 123, 97, 117, 96, 110, 95, 107, 89, 106, 83, 103, 73, 104, 68, 103, 62, 102, 54, 102, 47, 105, 42);
        Shape(g, Cream, false, 68, 76, 69, 68, 70, 57, 72, 51, 73, 46, 76, 42, 80, 42, 84, 41, 83, 47, 82, 52, 80, 60, 80, 69, 77, 77, 75, 81, 71, 82, 68, 76);
        Shape(g, Cream, 47, 77, 49, 73, 57, 70, 63, 71, 69, 71, 73, 75, 78, 77, 84, 78, 85, 82, 83, 86, 80, 90, 72, 92, 66, 91, 59, 91, 55, 88, 51, 86, 47, 84, 46, 80, 47, 77);
        DrawEyes(g, pose);
        Ellipse(g, Black, 58, 79, 11, 7, false);
        Ellipse(g, Color.FromArgb(221, 206, 187), 60, 80, 3.4f, 1.25f, false);
        if (pose == 5) Ellipse(g, Black, 70, 89, 6, 7, false);
        g.Restore(head);

        if (pose == 4)
        {
            g.TranslateTransform(0, -12);
            Shape(g, Cream, 39, 72, 34, 67, 30, 65, 28, 68, 25, 71, 28, 76, 32, 78, 35, 79, 38, 77, 39, 72);
            Shape(g, Cream, 110, 75, 112, 69, 116, 67, 120, 69, 123, 71, 121, 77, 117, 80, 114, 82, 111, 79, 110, 75);
            g.TranslateTransform(0, 12);
            return;
        }
        Shape(g, Cream, 84, 99, 83, 97, 84, 94, 89, 92, 90, 88, 94, 86, 97, 88, 100, 89, 102, 93, 101, 97, 100, 101, 96, 105, 91, 105, 87, 105, 85, 103, 84, 99);
    }

    private static void DrawEyes(Graphics g, int pose)
    {
        if (pose == 1)
        {
            Shape(g, Color.Empty, 48, 67, 52, 71, 58, 71, 62, 67);
            Shape(g, Color.Empty, 70, 67, 76, 73, 87, 73, 94, 67);
            return;
        }
        if (pose == 5)
        {
            Ellipse(g, Cream, 46, 55, 18, 20);
            Ellipse(g, Cream, 69, 55, 24, 22);
            Ellipse(g, Black, 52, 60, 8, 10, false);
            Ellipse(g, Black, 75, 60, 10, 12, false);
            Ellipse(g, Color.White, 53, 61, 2.5f, 2.5f, false);
            Ellipse(g, Color.White, 77, 62, 3, 3, false);
            return;
        }

        // 上沿保持笔直，黑瞳贴住上沿，眼白主要留在下方。
        Shape(g, Color.White, 46, 62, 52, 61, 58, 61, 65, 62, 65, 69, 61, 75, 54, 74, 49, 73, 46, 69, 46, 62);
        Shape(g, Black, false, 50, 62, 54, 62, 59, 62, 63, 62, 63, 68, 60, 72, 56, 72, 51, 72, 50, 68, 50, 62);
        Shape(g, Color.White, 69, 62, 78, 62, 88, 61, 97, 61, 96, 69, 92, 75, 82, 75, 74, 75, 70, 70, 69, 62);
        Shape(g, Black, false, 74, 62, 81, 62, 87, 61, 93, 61, 92, 68, 89, 72, 82, 72, 76, 72, 74, 69, 74, 62);
    }

    private static void DrawBowl(Graphics g)
    {
        Shape(g, Color.FromArgb(130, 136, 133), 57, 107, 57, 110, 56, 114, 56, 117, 56, 122, 65, 125, 76, 125, 86, 125, 96, 122, 96, 118, 96, 113, 95, 109, 94, 106, 84, 104, 66, 104, 57, 107);
        Ellipse(g, Color.FromArgb(76, 73, 65), 57, 100, 38, 13);
        for (int i = 0; i < 12; i++)
        {
            double angle = i * 2.4;
            float x = 75 + (float)(Math.Cos(angle) * (5 + i)), y = 105 + (float)(Math.Sin(angle) * (1 + i * .25));
            Ellipse(g, i % 3 == 0 ? Color.FromArgb(162, 134, 94) : Color.FromArgb(121, 101, 76), x - 2.4f, y - 1.2f, 4.9f, 2.9f, false);
        }
    }

    private static void Ellipse(Graphics g, Color color, float x, float y, float width, float height, bool outline = true)
    {
        using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, x, y, width, height);
        if (outline) using (Pen pen = new Pen(Ink, 1.4f)) g.DrawEllipse(pen, x, y, width, height);
    }

    private static void Shape(Graphics g, Color fill, float x, float y, params float[] curves)
    {
        Shape(g, fill, true, x, y, curves);
    }

    private static void Shape(Graphics g, Color fill, bool outline, float x, float y, params float[] curves)
    {
        using (GraphicsPath path = new GraphicsPath())
        {
            for (int i = 0; i < curves.Length; i += 6)
            {
                path.AddBezier(x, y, curves[i], curves[i + 1], curves[i + 2], curves[i + 3], curves[i + 4], curves[i + 5]);
                x = curves[i + 4]; y = curves[i + 5];
            }
            if (!fill.IsEmpty)
            {
                path.CloseFigure();
                using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
            }
            if (outline) using (Pen pen = new Pen(Ink, 1.5f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawPath(pen, path);
        }
    }
}
