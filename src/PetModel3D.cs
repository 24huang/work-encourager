using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using SW = System.Windows;
using WM = System.Windows.Media;
using W3 = System.Windows.Media.Media3D;
using WI = System.Windows.Media.Imaging;
using WC = System.Windows.Controls;

// 模型保存真实顶点、三角面、法线和材质；相机绕模型旋转，遮挡由WPF三维深度测试处理。
public static class PetModel3D
{
    public static readonly string[] PoseNames = { "坐着", "睡觉", "摸鱼", "工作" };
    private static readonly W3.MeshGeometry3D Sphere = MakeRound(1);
    private static readonly W3.MeshGeometry3D Rounded = MakeRound(.43);
    private static readonly W3.MeshGeometry3D Soft = MakeRound(.78);
    private static readonly W3.MeshGeometry3D SmallSphere = MakeRound(1, 10, 16), TinySphere = MakeRound(1, 6, 10);
    private static readonly W3.MeshGeometry3D SmallRounded = MakeRound(.43, 10, 16), TinyRounded = MakeRound(.43, 6, 8);
    private static readonly W3.MeshGeometry3D SmallSoft = MakeRound(.78, 10, 16), TinySoft = MakeRound(.78, 6, 10);
    private static readonly W3.MeshGeometry3D Cylinder = MakeCylinder();
    private static readonly Dictionary<string, Scene> Scenes = new Dictionary<string, Scene>();
    private static readonly Dictionary<string, W3.Material> Materials = new Dictionary<string, W3.Material>();
    private static readonly Dictionary<string, Bitmap> AnimationFrames = new Dictionary<string, Bitmap>();
    private static string cachedView = "";
    private const int RenderWidth = 300, RenderHeight = 285;
    private static readonly WC.Viewport3D Viewport = new WC.Viewport3D { Width = RenderWidth, Height = RenderHeight, ClipToBounds = true };
    private static readonly W3.ModelVisual3D Visual = new W3.ModelVisual3D();
    private static readonly W3.PerspectiveCamera Camera = new W3.PerspectiveCamera { FieldOfView = 34, NearPlaneDistance = .1, FarPlaneDistance = 40 };
    private static readonly WI.RenderTargetBitmap Target = new WI.RenderTargetBitmap(RenderWidth, RenderHeight, 96, 96, WM.PixelFormats.Pbgra32);
    private static readonly byte[] Pixels = new byte[RenderWidth * RenderHeight * 4];
    private sealed class Scene
    {
        public W3.Model3DGroup World = new W3.Model3DGroup();
        public W3.AxisAngleRotation3D Head, LeftArm, RightArm, Tail;
        public W3.ScaleTransform3D Breath, EyeLeft, EyeRight;
        public double TargetY;
        public int Pose, Kind;
    }
    static PetModel3D()
    {
        Viewport.Camera = Camera; Viewport.Children.Add(Visual);
        Viewport.Measure(new SW.Size(RenderWidth, RenderHeight)); Viewport.Arrange(new SW.Rect(0, 0, RenderWidth, RenderHeight));
    }
    private static double SignedPower(double value, double power) { return Math.Sign(value) * Math.Pow(Math.Abs(value), power); }
    private static W3.MeshGeometry3D MakeRound(double exponent, int rows = 18, int columns = 28)
    {
        W3.MeshGeometry3D mesh = new W3.MeshGeometry3D();
        for (int y = 0; y <= rows; y++)
        {
            double latitude = -Math.PI / 2 + Math.PI * y / rows;
            for (int x = 0; x <= columns; x++)
            {
                double longitude = 2 * Math.PI * x / columns;
                double px = SignedPower(Math.Cos(latitude), exponent) * SignedPower(Math.Cos(longitude), exponent);
                double py = SignedPower(Math.Sin(latitude), exponent);
                double pz = SignedPower(Math.Cos(latitude), exponent) * SignedPower(Math.Sin(longitude), exponent);
                mesh.Positions.Add(new W3.Point3D(px, py, pz));
                W3.Vector3D normal = new W3.Vector3D(SignedPower(px, 2 / exponent - 1), SignedPower(py, 2 / exponent - 1), SignedPower(pz, 2 / exponent - 1));
                if (normal.LengthSquared > 0) normal.Normalize(); mesh.Normals.Add(normal);
                if (x < columns && y < rows)
                {
                    int a = y * (columns + 1) + x, b = a + columns + 1;
                    mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                    mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
                }
            }
        }
        mesh.Freeze(); return mesh;
    }
    private static W3.MeshGeometry3D MakeCylinder()
    {
        W3.MeshGeometry3D mesh = new W3.MeshGeometry3D(); const int n = 10;
        for (int y = 0; y < 2; y++) for (int i = 0; i <= n; i++)
        {
            double angle = i * Math.PI * 2 / n, x = Math.Cos(angle), z = Math.Sin(angle);
            mesh.Positions.Add(new W3.Point3D(x, y - .5, z)); mesh.Normals.Add(new W3.Vector3D(x, 0, z));
        }
        for (int i = 0; i < n; i++)
        {
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + n + 1); mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + n + 1); mesh.TriangleIndices.Add(i + n + 2);
        }
        for (int side = 0; side < 2; side++)
        {
            int first = mesh.Positions.Count; double y = side - .5;
            mesh.Positions.Add(new W3.Point3D(0, y, 0)); mesh.Normals.Add(new W3.Vector3D(0, side == 0 ? -1 : 1, 0));
            for (int i = 0; i <= n; i++) { double a = i * Math.PI * 2 / n; mesh.Positions.Add(new W3.Point3D(Math.Cos(a), y, Math.Sin(a))); mesh.Normals.Add(new W3.Vector3D(0, side == 0 ? -1 : 1, 0)); }
            for (int i = 0; i < n; i++) { mesh.TriangleIndices.Add(first); mesh.TriangleIndices.Add(first + i + (side == 0 ? 1 : 2)); mesh.TriangleIndices.Add(first + i + (side == 0 ? 2 : 1)); }
        }
        mesh.Freeze(); return mesh;
    }
    private static W3.Material Material(Color color, bool polished)
    {
        string key = color.ToArgb() + ":" + polished; W3.Material result;
        if (Materials.TryGetValue(key, out result)) return result;
        WM.Color c = WM.Color.FromArgb(color.A, color.R, color.G, color.B);
        W3.MaterialGroup group = new W3.MaterialGroup(); group.Children.Add(new W3.DiffuseMaterial(new WM.SolidColorBrush(c)));
        if (color.A == 255) group.Children.Add(new W3.SpecularMaterial(new WM.SolidColorBrush(WM.Color.FromRgb(polished ? (byte)195 : (byte)24, polished ? (byte)205 : (byte)23, polished ? (byte)215 : (byte)22)), polished ? 65 : 18));
        group.Freeze(); Materials.Add(key, group); return group;
    }
    private static void Ball(W3.Model3DGroup group, double x, double y, double z, double rx, double ry, double rz, Color color, bool box = false, bool polished = false, bool soft = false)
    {
        W3.Transform3DGroup transform = new W3.Transform3DGroup(); transform.Children.Add(new W3.ScaleTransform3D(rx, ry, rz)); transform.Children.Add(new W3.TranslateTransform3D(x, y, z));
        // 细小部件复用低面数网格，头部等大曲面保留细分，不按单一精度渲染所有部件。
        double size = Math.Max(rx, Math.Max(ry, rz));
        W3.MeshGeometry3D geometry = box ? size < .2 ? TinyRounded : size < .48 ? SmallRounded : Rounded : soft ? size < .2 ? TinySoft : size < .48 ? SmallSoft : Soft : size < .2 ? TinySphere : size < .48 ? SmallSphere : Sphere;
        group.Children.Add(new W3.GeometryModel3D(geometry, Material(color, polished)) { Transform = transform });
    }
    private static void PlushArm(W3.Model3DGroup group, W3.Point3D a, W3.Point3D b, Color color)
    {
        W3.Vector3D delta = b - a; double length = delta.Length; delta.Normalize();
        W3.Vector3D axis = W3.Vector3D.CrossProduct(new W3.Vector3D(0, 1, 0), delta);
        double angle = Math.Acos(Math.Max(-1, Math.Min(1, delta.Y))) * 180 / Math.PI;
        if (axis.LengthSquared < .000001) axis = new W3.Vector3D(1, 0, 0); else axis.Normalize();
        W3.Transform3DGroup transform = new W3.Transform3DGroup(); transform.Children.Add(new W3.ScaleTransform3D(.23, length / 2 + .18, .23)); transform.Children.Add(new W3.RotateTransform3D(new W3.AxisAngleRotation3D(axis, angle))); transform.Children.Add(new W3.TranslateTransform3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2));
        group.Children.Add(new W3.GeometryModel3D(SmallSoft, Material(color, false)) { Transform = transform });
    }
    private static void Link(W3.Model3DGroup group, W3.Point3D a, W3.Point3D b, double radius, Color color)
    {
        W3.Vector3D vector = b - a; double length = vector.Length; vector.Normalize();
        W3.Vector3D axis = W3.Vector3D.CrossProduct(new W3.Vector3D(0, 1, 0), vector);
        double angle = Math.Acos(Math.Max(-1, Math.Min(1, vector.Y))) * 180 / Math.PI;
        if (axis.LengthSquared < .000001) axis = new W3.Vector3D(1, 0, 0); else axis.Normalize();
        W3.Transform3DGroup transform = new W3.Transform3DGroup(); transform.Children.Add(new W3.ScaleTransform3D(radius, length, radius));
        transform.Children.Add(new W3.RotateTransform3D(new W3.AxisAngleRotation3D(axis, angle))); transform.Children.Add(new W3.TranslateTransform3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2));
        group.Children.Add(new W3.GeometryModel3D(Cylinder, Material(color, false)) { Transform = transform });
    }
    private static void Curve(W3.Model3DGroup group, W3.Point3D[] points, double radius, Color color)
    {
        for (int i = 0; i < points.Length; i++) { Ball(group, points[i].X, points[i].Y, points[i].Z, radius, radius, radius, color); if (i > 0) Link(group, points[i - 1], points[i], radius, color); }
    }
    private static W3.Model3DGroup Joint(W3.Model3DGroup parent, W3.Vector3D axis, W3.Point3D center, out W3.AxisAngleRotation3D rotation)
    {
        rotation = new W3.AxisAngleRotation3D(axis, 0);
        W3.Model3DGroup group = new W3.Model3DGroup { Transform = new W3.RotateTransform3D(rotation, center) }; parent.Children.Add(group); return group;
    }
    private static void Ear(W3.Model3DGroup group, double x, Color fur)
    {
        W3.MeshGeometry3D mesh = new W3.MeshGeometry3D(); const int rings = 14, sides = 24;
        for (int row = 0; row <= rings; row++)
        {
            double v = row / (double)rings, radius = .27 * Math.Pow(1 - v, .82) + .018;
            for (int col = 0; col <= sides; col++)
            {
                double a = col * Math.PI * 2 / sides;
                mesh.Positions.Add(new W3.Point3D(x + Math.Sign(x) * .08 * v + radius * Math.Cos(a), 2.73 + .66 * v, -.08 - .04 * v + radius * .65 * Math.Sin(a)));
                W3.Vector3D n = new W3.Vector3D(Math.Cos(a), .48, Math.Sin(a) / .65); n.Normalize(); mesh.Normals.Add(n);
                if (row < rings && col < sides)
                {
                    int at = row * (sides + 1) + col, above = at + sides + 1;
                    foreach (int index in new[] { at, above, at + 1, at + 1, above, above + 1 }) mesh.TriangleIndices.Add(index);
                }
            }
        }
        group.Children.Add(new W3.GeometryModel3D(mesh, Material(fur, false)));
        W3.MeshGeometry3D inner = new W3.MeshGeometry3D();
        for (int row = 0; row <= 10; row++)
        {
            double v = .18 + row * .065, radius = .27 * Math.Pow(1 - v, .82) + .018, width = radius * .5 * Math.Sin(Math.PI * row / 10);
            for (int side = -1; side <= 1; side += 2) inner.Positions.Add(new W3.Point3D(x + Math.Sign(x) * .08 * v + side * width, 2.73 + .66 * v, -.08 - .04 * v + radius * .65 * Math.Sqrt(1 - width * width / (radius * radius)) + .008));
            if (row < 10) foreach (int index in new[] { row * 2, row * 2 + 1, row * 2 + 2, row * 2 + 1, row * 2 + 3, row * 2 + 2 }) inner.TriangleIndices.Add(index);
        }
        group.Children.Add(new W3.GeometryModel3D(inner, Material(Color.FromArgb(241, 187, 194), false)));
        Ball(group, x + Math.Sign(x) * .08, 3.39, -.12, .022, .025, .018, fur);
    }
    private static void Fortune(W3.Model3DGroup group)
    {
        WM.DrawingVisual drawing = new WM.DrawingVisual();
        using (WM.DrawingContext context = drawing.RenderOpen())
        {
            WM.FormattedText text = new WM.FormattedText("福", CultureInfo.GetCultureInfo("zh-CN"), SW.FlowDirection.LeftToRight, new WM.Typeface("Microsoft YaHei UI"), 48, WM.Brushes.SaddleBrown);
            context.DrawText(text, new SW.Point((64 - text.Width) / 2, (64 - text.Height) / 2));
        }
        WI.RenderTargetBitmap image = new WI.RenderTargetBitmap(64, 64, 96, 96, WM.PixelFormats.Pbgra32); image.Render(drawing); image.Freeze();
        W3.MeshGeometry3D face = new W3.MeshGeometry3D();
        face.Positions.Add(new W3.Point3D(-.24, .68, .675)); face.Positions.Add(new W3.Point3D(.24, .68, .675)); face.Positions.Add(new W3.Point3D(.24, 1.22, .675)); face.Positions.Add(new W3.Point3D(-.24, 1.22, .675));
        face.TextureCoordinates.Add(new SW.Point(0, 1)); face.TextureCoordinates.Add(new SW.Point(1, 1)); face.TextureCoordinates.Add(new SW.Point(1, 0)); face.TextureCoordinates.Add(new SW.Point(0, 0));
        foreach (int index in new[] { 0, 1, 2, 0, 2, 3 }) face.TriangleIndices.Add(index);
        group.Children.Add(new W3.GeometryModel3D(face, new W3.DiffuseMaterial(new WM.ImageBrush(image))));
    }
    private static Scene Build(int kind, int theme, int pose, bool surprised)
    {
        Scene scene = new Scene { Pose = pose, Kind = kind, TargetY = pose == 1 ? .9 : kind == 2 ? 1.8 : 1.62 };
        scene.World.Children.Add(new W3.AmbientLight(WM.Color.FromRgb(104, 109, 120)));
        scene.World.Children.Add(new W3.DirectionalLight(WM.Color.FromRgb(180, 173, 165), new W3.Vector3D(-.45, -.8, -1.2)));
        scene.World.Children.Add(new W3.DirectionalLight(WM.Color.FromRgb(65, 79, 104), new W3.Vector3D(.8, -.1, .4)));
        Color fur = kind == 0 ? Color.FromArgb(255, 219, 165) : kind == 1 ? Color.FromArgb(188, 143, 103) : kind == 3 ? Color.FromArgb(210, 232, 244) : Color.FromArgb(255, 245, 232);
        Color accent = theme == 0 ? Color.FromArgb(96, 188, 220) : Color.FromArgb(225, 146, 184), ink = Color.FromArgb(77, 56, 49), pale = kind == 1 ? Color.FromArgb(218, 177, 132) : Color.FromArgb(255, 241, 213);
        if (pose != 4) Ball(scene.World, 0, -.03, 0, 1.25, .012, .85, Color.FromArgb(28, 55, 55, 75));
        W3.Model3DGroup character = new W3.Model3DGroup(); scene.World.Children.Add(character);
        scene.Breath = new W3.ScaleTransform3D(1, 1, 1, 0, 1, 0); W3.Transform3DGroup poseTransform = new W3.Transform3DGroup(); poseTransform.Children.Add(scene.Breath);
        if (pose == 1)
        {
            poseTransform.Children.Add(new W3.ScaleTransform3D(.78, .78, .78, 0, 1.4, 0)); poseTransform.Children.Add(new W3.RotateTransform3D(new W3.AxisAngleRotation3D(new W3.Vector3D(0, 0, 1), -78), new W3.Point3D(0, 1.4, 0))); poseTransform.Children.Add(new W3.TranslateTransform3D(-.3, -.72, 0));
            Ball(scene.World, 0, .12, 0, 1.8, .18, .85, theme == 0 ? Color.FromArgb(168, 191, 226) : Color.FromArgb(228, 178, 203), true);
        }
        else if (pose == 2)
        {
            poseTransform.Children.Add(new W3.RotateTransform3D(new W3.AxisAngleRotation3D(new W3.Vector3D(1, 0, 0), -18), new W3.Point3D(0, 1.3, 0))); poseTransform.Children.Add(new W3.TranslateTransform3D(0, .12, 0));
            Ball(scene.World, 0, .4, -.15, .8, .13, .7, accent, true); Ball(scene.World, 0, 1.1, -.75, .8, .8, .13, accent, true);
        }
        character.Transform = poseTransform;
        Ball(character, 0, .88, 0, .7, .71, .54, fur, kind == 3, kind == 3, kind != 3);
        Ball(character, 0, .87, .49, .4, .43, .075, kind == 3 ? accent : pale, kind == 3, kind == 3);
        Ball(character, -.38, .18, .28, .35, .21, .4, fur, kind == 3); Ball(character, .38, .18, .28, .35, .21, .4, fur, kind == 3);
        W3.Model3DGroup head = Joint(character, pose == 3 ? new W3.Vector3D(1, 0, 0) : new W3.Vector3D(0, 0, 1), new W3.Point3D(0, 2.05, 0), out scene.Head);
        W3.Transform3DGroup refinedHead = new W3.Transform3DGroup(); refinedHead.Children.Add(new W3.ScaleTransform3D(kind == 3 ? 1.05 : 1.14, 1.08, 1.09, 0, 2.32, 0)); refinedHead.Children.Add(new W3.TranslateTransform3D(0, -.17, 0)); refinedHead.Children.Add(head.Transform); head.Transform = refinedHead;
        Ball(head, 0, 2.32, 0, kind == 3 ? 1.01 : .94, .73, kind == 3 ? .65 : .73, fur, kind == 3, kind == 3, kind != 3);
        if (kind == 3)
        {
            Ball(head, 0, 2.36, .61, .84, .5, .13, Color.FromArgb(22, 45, 66), true, true);
            Link(head, new W3.Point3D(0, 3.02, 0), new W3.Point3D(0, 3.42, 0), .04, accent); Ball(head, 0, 3.5, 0, .13, .13, .13, accent, false, true);
        }
        else if (kind == 2)
        {
            for (int sign = -1; sign <= 1; sign += 2) { Ball(head, sign * .42, 3.37, -.02, .24, .78, .2, fur); Ball(head, sign * .42, 3.4, .16, .13, .56, .06, Color.FromArgb(245, 184, 197)); }
        }
        else if (kind == 1)
        {
            for (int sign = -1; sign <= 1; sign += 2) { Ball(head, sign * .64, 2.9, -.04, .24, .25, .17, fur); Ball(head, sign * .64, 2.9, .1, .13, .14, .05, pale); }
            Ball(head, 0, 3.12, 0, .27, .21, .26, Color.FromArgb(255, 165, 61)); Ball(head, .13, 3.36, 0, .19, .035, .075, Color.FromArgb(94, 148, 83));
            Ball(head, 0, 2.07, .65, .73, .36, .34, pale);
            Ball(head, -.16, 2.14, .97, .07, .045, .035, ink); Ball(head, .16, 2.14, .97, .07, .045, .035, ink);
        }
        else { Ear(head, -.6, fur); Ear(head, .6, fur); }
        W3.Model3DGroup eyesLeft = new W3.Model3DGroup(), eyesRight = new W3.Model3DGroup();
        scene.EyeLeft = new W3.ScaleTransform3D(1, 1, 1, -.34, 2.4, .75); scene.EyeRight = new W3.ScaleTransform3D(1, 1, 1, .34, 2.4, .75);
        eyesLeft.Transform = scene.EyeLeft; eyesRight.Transform = scene.EyeRight; head.Children.Add(eyesLeft); head.Children.Add(eyesRight);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            W3.Model3DGroup eyes = sign < 0 ? eyesLeft : eyesRight;
            if (kind == 3 && surprised) Ball(eyes, sign * .36, 2.42, .774, .075, .14, .025, accent, false, true);
            else if (kind == 3) Curve(eyes, new[] { new W3.Point3D(sign * .36 - .1, 2.38, .768), new W3.Point3D(sign * .36 - .065, 2.46, .774), new W3.Point3D(sign * .36 + .065, 2.46, .774), new W3.Point3D(sign * .36 + .1, 2.38, .768) }, .028, accent);
            else
            {
                double x = sign * (kind == 1 ? .51 : .34), z = kind == 1 ? .673 : .72;
                if (kind != 1) z = .716;
                Ball(eyes, x, 2.42, z, surprised ? .095 : .072, surprised ? .14 : .095, .025, ink, false, true); Ball(eyes, x - .014, 2.45, z + .024, .016, .019, .008, Color.White);
                Ball(head, sign * .63, 2.19, .56, .14, .07, .025, Color.FromArgb(244, 183, 174));
            }
        }
        if (surprised)
        {
            if (kind != 3 && kind != 1) Ball(head, 0, 2.12, .7, .36, .24, .17, pale);
            W3.Point3D[] mouth = new W3.Point3D[17];
            for (int i = 0; i < mouth.Length; i++) { double a = i * Math.PI * 2 / 16; mouth[i] = new W3.Point3D(Math.Cos(a) * .065, (kind == 1 ? 1.98 : kind == 3 ? 2.14 : 2.075) + Math.Sin(a) * .09, kind == 1 ? 1.005 : kind == 3 ? .774 : .88); }
            Curve(head, mouth, .018, kind == 3 ? accent : ink);
        }
        else if (kind == 3) Curve(head, new[] { new W3.Point3D(-.16, 2.17, .77), new W3.Point3D(-.08, 2.11, .77), new W3.Point3D(.08, 2.11, .77), new W3.Point3D(.16, 2.17, .77) }, .023, accent);
        else if (kind != 1)
        {
            Ball(head, 0, 2.12, .7, .36, .24, .17, pale); Ball(head, 0, 2.17, .87, .065, .045, .035, Color.FromArgb(165, 111, 115));
            Curve(head, new[] { new W3.Point3D(-.15, 2.08, .855), new W3.Point3D(-.09, 2.04, .88), new W3.Point3D(0, 2.075, .891), new W3.Point3D(.09, 2.04, .88), new W3.Point3D(.15, 2.08, .855) }, .014, ink);
        }
        W3.Model3DGroup left = Joint(character, new W3.Vector3D(1, 0, 0), new W3.Point3D(-.55, 1.45, 0), out scene.LeftArm);
        W3.Model3DGroup right = Joint(character, kind == 4 && pose == 0 ? new W3.Vector3D(0, 0, 1) : new W3.Vector3D(1, 0, 0), new W3.Point3D(.55, 1.45, 0), out scene.RightArm);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            W3.Model3DGroup arm = sign < 0 ? left : right;
            W3.Point3D shoulder = new W3.Point3D(sign * .52, 1.27, .03);
            W3.Point3D hand = pose == 4 ? new W3.Point3D(sign * .89, 2.58, .7) : pose == 3 ? new W3.Point3D(sign * .42, 1.23, 1.06) : pose == 2 ? new W3.Point3D(sign * .29, 1.34, .92) : kind == 4 && sign > 0 ? new W3.Point3D(.92, 1.95, .23) : new W3.Point3D(sign * .65, .81, .28);
            PlushArm(arm, shoulder, hand, fur);
            if (pose == 4) Ball(arm, hand.X, hand.Y, hand.Z, .26, .2, .2, fur, kind == 3);
            if (kind == 4 && sign > 0 && pose == 0) Ball(arm, hand.X, hand.Y, hand.Z + .2, .09, .09, .02, Color.FromArgb(245, 180, 175));
        }
        W3.Model3DGroup tail = Joint(character, new W3.Vector3D(0, 1, 0), new W3.Point3D(.4, .7, -.35), out scene.Tail);
        if (kind == 0 || kind == 4) Curve(tail, new[] { new W3.Point3D(.4, .65, -.35), new W3.Point3D(.9, .65, -.4), new W3.Point3D(1.18, .85, -.3), new W3.Point3D(1.25, 1.23, -.25) }, .14, fur);
        if (kind == 2) Ball(tail, 0, .6, -.54, .27, .27, .24, fur);
        if (kind != 3)
        {
            Color collar = kind == 4 ? Color.FromArgb(214, 68, 71) : accent;
            Curve(character, new[] { new W3.Point3D(-.43, 1.64, .28), new W3.Point3D(-.25, 1.57, .48), new W3.Point3D(0, 1.54, .53), new W3.Point3D(.25, 1.57, .48), new W3.Point3D(.43, 1.64, .28) }, .045, collar);
            Ball(character, 0, 1.48, .58, .12, .13, .07, Color.FromArgb(246, 194, 66), false, true);
        }
        if (kind == 4) { Ball(character, 0, .95, .56, .4, .45, .1, Color.FromArgb(245, 193, 58), false, true); Fortune(character); }
        if (pose == 2)
        {
            Ball(character, 0, 1.53, .91, .25, .4, .045, Color.FromArgb(51, 65, 82), true, true);
            Ball(character, 0, 1.55, .958, .21, .32, .012, accent, true, true); Ball(character, 0, 1.2, .963, .035, .035, .012, Color.White);
        }
        if (pose == 3)
        {
            Color wood = Color.FromArgb(194, 160, 131), metal = Color.FromArgb(165, 190, 209);
            Ball(scene.World, 0, 1.02, 1.03, 1.35, .09, .67, wood, true);
            for (int sign = -1; sign <= 1; sign += 2) for (int depth = 0; depth < 2; depth++) Link(scene.World, new W3.Point3D(sign * 1.08, .04, .57 + depth * .89), new W3.Point3D(sign * 1.08, .94, .57 + depth * .89), .055, wood);
            Ball(scene.World, 0, 1.15, 1.04, .71, .045, .43, metal, true, true);
            Ball(scene.World, 0, 1.6, 1.44, .73, .45, .045, metal, true, true);
            Ball(scene.World, 0, 1.6, 1.387, .65, .37, .015, Color.FromArgb(40, 84, 112), true);
            Ball(scene.World, 0, 1.6, 1.49, .09, .09, .012, accent, false, true);
            for (int row = 0; row < 3; row++) for (int col = 0; col < 7; col++) Ball(scene.World, -.5 + col * .165, 1.201, .78 + row * .16, .055, .013, .047, Color.FromArgb(89, 111, 132), true);
        }
        return scene;
    }
    // 复用真实模型的抬爪姿态，绕屏幕法线转向四边；只裁图像，窗口始终留在可用屏幕内。
    public static Bitmap RenderDocked(int kind, int theme, int frame, int edge, double reveal, int pixelWidth = 200)
    {
        Bitmap result = new Bitmap(pixelWidth, (int)Math.Round(pixelWidth * .95), PixelFormat.Format32bppPArgb);
        using (Bitmap character = Render(kind, theme, frame, 4, 0, 0, false, false, false, pixelWidth))
        using (Graphics g = Graphics.FromImage(result))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.ScaleTransform(pixelWidth / 200f, pixelWidth / 200f);
            g.TranslateTransform(100, 95);
            g.RotateTransform(edge == 1 ? 90 : edge == 2 ? -90 : edge == 3 ? 180 : 0);
            if (edge <= 2) g.ScaleTransform(.94f, .94f);
            g.DrawImage(character, new RectangleF(-100, -95 + (float)(106 * (1 - Math.Max(0, Math.Min(1, reveal)))), 200, 190));
        }
        return result;
    }
    public static Bitmap Render(int kind, int theme, int frame, int pose, double yaw, double pitch, bool walking, bool speaking, bool surprised = false, int pixelWidth = 200)
    {
        // 只缓存当前角色和视角的一轮循环。转动相机立即重新进行三维渲染，旧位图及时释放。
        string view = kind + ":" + theme + ":" + yaw.ToString("R", CultureInfo.InvariantCulture) + ":" + pitch.ToString("R", CultureInfo.InvariantCulture) + ":" + surprised + ":" + pixelWidth;
        if (view != cachedView)
        {
            foreach (Bitmap image in AnimationFrames.Values) image.Dispose(); AnimationFrames.Clear(); cachedView = view;
        }
        int phase = frame % 48;
        string animationKey = pose + ":" + phase + ":" + walking; Bitmap cached;
        if (AnimationFrames.TryGetValue(animationKey, out cached)) return (Bitmap)cached.Clone();
        string key = kind + ":" + theme + ":" + pose + ":" + surprised; Scene scene;
        if (!Scenes.TryGetValue(key, out scene)) { scene = Build(kind, theme, pose, surprised); Scenes.Add(key, scene); }
        double t = phase * Math.PI * 2 / 48, eye = !surprised && (pose == 1 || phase >= 44 && phase <= 46) ? .07 : 1;
        scene.EyeLeft.ScaleY = scene.EyeRight.ScaleY = eye;
        scene.Breath.ScaleY = 1 + Math.Sin(t * (pose == 1 ? 1 : 2)) * .012;
        scene.Head.Angle = pose == 3 ? 6 + Math.Sin(t * 2) * 2 : Math.Sin(t) * 2;
        scene.LeftArm.Angle = pose == 3 ? Math.Sin(t * 6) * 4 : walking ? Math.Sin(t * 4) * 8 : Math.Sin(t * 2) * 2;
        scene.RightArm.Angle = kind == 4 && pose == 0 ? Math.Sin(t * 2) * 14 : -scene.LeftArm.Angle;
        scene.Tail.Angle = Math.Sin(t * 2) * 12;
        double angle = yaw * Math.PI / 180, elevation = pitch * Math.PI / 180, distance = scene.Pose == 3 || scene.Kind == 2 ? 9.0 : 8.1;
        W3.Vector3D offset = new W3.Vector3D(Math.Sin(angle) * Math.Cos(elevation) * distance, Math.Sin(elevation) * distance, Math.Cos(angle) * Math.Cos(elevation) * distance);
        Camera.Position = new W3.Point3D(offset.X, scene.TargetY + offset.Y, offset.Z); Camera.LookDirection = -offset; Camera.UpDirection = new W3.Vector3D(0, 1, 0);
        Visual.Content = scene.World; Viewport.UpdateLayout(); Target.Clear(); Target.Render(Viewport); Target.CopyPixels(Pixels, RenderWidth * 4, 0);
        using (Bitmap high = new Bitmap(RenderWidth, RenderHeight, PixelFormat.Format32bppPArgb))
        {
            BitmapData data = high.LockBits(new Rectangle(0, 0, RenderWidth, RenderHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(Pixels, 0, data.Scan0, Pixels.Length); } finally { high.UnlockBits(data); }
            Bitmap result = new Bitmap(pixelWidth, (int)Math.Round(pixelWidth * .95), PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality; g.DrawImage(high, new Rectangle(0, 0, result.Width, result.Height)); }
            AnimationFrames.Add(animationKey, result);
            return (Bitmap)result.Clone();
        }
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
