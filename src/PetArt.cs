using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class PetArt
{
    // 保留旧版存档编号，菜单顺序和编号分别管理。
    public const int Robot = 3, Lucky = 4, Beagle = 6, Matcha = 7;
    public static readonly int[] Characters = { Beagle, Matcha, Lucky, Robot };
    public static readonly string[] Names = { "比格狗", "抹茶旦旦", "招财猫", "小机器人" };
    public static readonly string[] PoseNames = { "坐着", "睡觉", "摸鱼", "工作" };

    public static string NameOf(int kind)
    {
        int index = Array.IndexOf(Characters, kind);
        return Names[index < 0 ? 0 : index];
    }

    public static string PoseName(int kind, int pose)
    {
        return kind == Beagle && pose == 0 ? "闲置动作" : PoseNames[pose];
    }

    public static Bitmap Render(int kind, int theme, int frame, int pose, double facing, bool walking, bool surprised = false, int width = 200, double lean = 0)
    {
        width = Math.Max(1, width);
        return StickerArt.Render(kind, theme, frame, surprised ? 5 : pose, facing, walking, width, lean);
    }

    private static RectangleF PeekBox(RectangleF bounds, int edge)
    {
        float width = bounds.Width * .59f, height = bounds.Height * .46f;
        float x = edge == 1 ? bounds.Left + 1 : edge == 2 ? bounds.Right - width - 1 : bounds.Left + (bounds.Width - width) / 2;
        float y = edge == 3 ? bounds.Top + 1 : edge == 4 ? bounds.Bottom - height - 1 : bounds.Top + (bounds.Height - height) / 2;
        return new RectangleF(x, y, width, height);
    }

    // 探头和全身都用正立画稿。贴边改变位置和比例，面部不会被竖直裁切。
    public static Bitmap RenderDocked(int kind, int theme, int frame, int edge, double reveal, int width = 200, double facing = 25)
    {
        width = Math.Max(1, width);
        int height = Math.Max(1, (int)Math.Round(width * .95));
        Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        if (width == 1) return result;
        float amount = (float)Math.Max(0, Math.Min(1, reveal));
        using (Graphics graphics = Graphics.FromImage(result))
        using (ImageAttributes opacity = new ImageAttributes())
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            RectangleF canvas = new RectangleF(0, 0, width, height);
            RectangleF peek = PeekBox(canvas, edge);
            for (int layer = 0; layer < 2; layer++)
            {
                bool head = layer == 0;
                float alpha = head ? 1 - amount : amount;
                if (alpha <= 0) continue;
                using (Bitmap art = Render(kind, theme, frame % 48, head ? 4 : 0, facing, false, false, width))
                {
                    ColorMatrix fade = new ColorMatrix(); fade.Matrix33 = alpha;
                    opacity.SetColorMatrix(fade);
                    RectangleF crop = head
                        ? new RectangleF(width * .125f, height * .07f, width * .80f, height * .68f)
                        : canvas;
                    Rectangle box = Rectangle.Round(head ? peek : canvas);
                    graphics.DrawImage(art, box, crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, opacity);
                }
            }
        }
        return result;
    }

    public static Rectangle DockAnchor(Rectangle bounds, int edge, double reveal)
    {
        if (edge == 0) return bounds;
        RectangleF head = PeekBox(bounds, edge);
        RectangleF body = RectangleF.Inflate(bounds, -bounds.Width * .10f, -bounds.Height * .15f);
        float t = (float)Math.Max(0, Math.Min(1, reveal));
        return Rectangle.Round(new RectangleF(head.X + (body.X - head.X) * t, head.Y + (body.Y - head.Y) * t,
            head.Width + (body.Width - head.Width) * t, head.Height + (body.Height - head.Height) * t));
    }
}

public sealed class PetBehavior
{
    private readonly Random random;
    private DateTime next;
    private int selected = -1;
    public int Current { get; private set; }
    public DateTime NextChange { get { return next; } }
    public PetBehavior(DateTime now, int seed) { random = new Random(seed); Current = 0; next = now.AddSeconds(random.Next(10, 21)); }
    public int Update(int mode, DateTime now, bool paused)
    {
        if (mode != selected) { selected = mode; next = now.AddSeconds(random.Next(10, 21)); if (mode >= 0) Current = mode; }
        if (mode >= 0 || paused) return Current;
        if (now >= next) { Current = (Current + 1 + random.Next(3)) % 4; next = now.AddSeconds(random.Next(10, 21)); }
        return Current;
    }
}
