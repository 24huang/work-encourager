using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

public static class PetInputTests
{
    public static void Run(Action<bool, string> check)
    {
        // 回归现场异常栈：贴墙绘制不能把零尺寸传给Bitmap构造函数。
        for (int edge = 1; edge <= 4; edge++)
        using (Bitmap image = PetArt.RenderDocked(PetArt.Beagle, 0, 0, edge, 0, 0))
            check(image.Width == 1 && image.Height == 1, "A zero-width docking frame is safe on edge " + edge);
        using (Bitmap image = PetArt.Render(PetArt.Beagle, 0, 0, 0, 25, false, false, 0))
            check(image.Width == 1 && image.Height == 1, "The free-pet renderer also protects zero-width frames");
        // 在同一局部动画相位比较身体轮廓，排除只有摇尾或换道具的变化。
        using (Bitmap standing = PetArt.Render(PetArt.Beagle, 0, 8, 0, 25, false))
        using (Bitmap sitting = PetArt.Render(PetArt.Beagle, 0, 56, 0, 25, false))
        using (Bitmap lounging = PetArt.Render(PetArt.Beagle, 0, 104, 0, 25, false))
        using (Bitmap sleeping = PetArt.Render(PetArt.Beagle, 0, 8, 1, 25, false))
        using (Bitmap walking = PetArt.Render(PetArt.Beagle, 0, 104, 0, 25, true))
        {
            int standToSit = 0, sitToLounge = 0, loungeToSleep = 0, walkingToLounge = 0;
            for (int y = 124; y < standing.Height; y++) for (int x = 0; x < standing.Width; x++)
            {
                bool a = standing.GetPixel(x, y).A > 100, b = sitting.GetPixel(x, y).A > 100;
                bool c = lounging.GetPixel(x, y).A > 100, d = sleeping.GetPixel(x, y).A > 100;
                bool e = walking.GetPixel(x, y).A > 100;
                if (a != b) standToSit++;
                if (b != c) sitToLounge++;
                if (c != d) loungeToSleep++;
                if (e != c) walkingToLounge++;
            }
            check(standToSit > 160 && sitToLounge > 350, "Beagle idle cycles through three distinct body poses at the same animation phase");
            check(loungeToSleep > 350, "The sleeping body differs from the lounge silhouette");
            check(walkingToLounge > 350, "Autonomous walking picks the standing gait instead of sliding the lounge pose");
        }
        using (ContextMenuStrip menu = new ContextMenuStrip())
        using (DesktopPet pet = new DesktopPet(new Settings { PetDockEdge = 2, PetWalk = false }, delegate { }, delegate(Point point) { }, menu))
        {
            MethodInfo draw = typeof(DesktopPet).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic);
            pet.ClientSize = Size.Empty; draw.Invoke(pet, new object[] { false });
            check(pet.ClientSize.IsEmpty, "A transient empty client area is skipped without changing window bounds");
            pet.WindowState = FormWindowState.Minimized; draw.Invoke(pet, new object[] { false });
            check(pet.WindowState == FormWindowState.Minimized, "Animation does not resize or reactivate a minimized pet");
            pet.WindowState = FormWindowState.Normal; pet.FitDisplay(); draw.Invoke(pet, new object[] { false });
            check(pet.ClientSize.Width > 0 && pet.Enabled && !pet.Paused, "The pet can render normally after its size is restored");
        }
    }
}
