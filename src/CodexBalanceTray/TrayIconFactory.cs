using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexBalanceTray;

internal static class TrayIconFactory
{
    private static readonly IReadOnlyDictionary<TrayStatusColor, Color> Colors =
        new Dictionary<TrayStatusColor, Color>
        {
            [TrayStatusColor.Green] = Color.FromArgb(34, 197, 94),
            [TrayStatusColor.Orange] = Color.FromArgb(245, 158, 11),
            [TrayStatusColor.Red] = Color.FromArgb(239, 68, 68),
            [TrayStatusColor.Gray] = Color.FromArgb(156, 163, 175)
        };

    public static Icon Create(TrayStatusColor status, double? remainingPercent = null)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var circle = new RectangleF(4.5f, 4.5f, 23f, 23f);
        using var fill = new SolidBrush(Color.White);
        using var track = new Pen(Color.FromArgb(209, 213, 219), 3.5f)
        {
            Alignment = PenAlignment.Center
        };
        using var outline = new Pen(Colors[status], 3.5f)
        {
            Alignment = PenAlignment.Center,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        graphics.FillEllipse(fill, circle);
        graphics.DrawEllipse(track, circle);

        if (remainingPercent is null || remainingPercent >= 99.95d)
        {
            graphics.DrawEllipse(outline, circle);
        }
        else
        {
            var clampedPercent = Math.Clamp(remainingPercent.Value, 0d, 100d);
            var sweepAngle = (float)(360d * clampedPercent / 100d);

            // Keep a small colored marker at zero so a reached red limit remains visible.
            graphics.DrawArc(outline, circle, -90f, Math.Max(sweepAngle, 1f));
        }

        var nativeHandle = bitmap.GetHicon();
        try
        {
            using var nativeIcon = Icon.FromHandle(nativeHandle);
            return (Icon)nativeIcon.Clone();
        }
        finally
        {
            DestroyIcon(nativeHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
