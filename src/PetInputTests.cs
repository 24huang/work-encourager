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
        using (Bitmap image = PetModel3D.RenderDocked(0, 0, 0, edge, 0, 0))
            check(image.Width == 1 && image.Height == 1, "A zero-width docking frame is safe on edge " + edge);
        using (Bitmap image = PetModel3D.Render(0, 0, 0, 0, 25, 12, false, false, false, 0))
            check(image.Width == 1 && image.Height == 1, "The free-pet renderer also protects zero-width frames");
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
