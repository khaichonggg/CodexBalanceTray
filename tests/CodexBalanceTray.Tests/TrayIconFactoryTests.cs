namespace CodexBalanceTray.Tests;

public sealed class TrayIconFactoryTests
{
    [Theory]
    [InlineData("Green", 34, 197, 94)]
    [InlineData("Orange", 245, 158, 11)]
    [InlineData("Red", 239, 68, 68)]
    [InlineData("Gray", 156, 163, 175)]
    public void Create_RendersWhiteCenterAndStatusOutline(
        string statusName,
        int red,
        int green,
        int blue)
    {
        var status = Enum.Parse<TrayStatusColor>(statusName);
        using var icon = TrayIconFactory.Create(status);
        using var bitmap = icon.ToBitmap();

        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.Equal(255, center.A);
        Assert.Equal(255, center.R);
        Assert.Equal(255, center.G);
        Assert.Equal(255, center.B);

        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.True(ContainsColor(bitmap, red, green, blue));
    }

    [Fact]
    public void Create_RendersShorterArcForLowerRemainingPercentage()
    {
        using var lowIcon = TrayIconFactory.Create(TrayStatusColor.Orange, 25d);
        using var highIcon = TrayIconFactory.Create(TrayStatusColor.Orange, 75d);
        using var lowBitmap = lowIcon.ToBitmap();
        using var highBitmap = highIcon.ToBitmap();

        var lowColoredPixels = CountColor(lowBitmap, 245, 158, 11);
        var highColoredPixels = CountColor(highBitmap, 245, 158, 11);

        Assert.True(lowColoredPixels > 0);
        Assert.True(highColoredPixels > lowColoredPixels * 2);
        Assert.True(ContainsColor(lowBitmap, 209, 213, 219));
    }

    [Fact]
    public void Create_ZeroRemainingStillRendersReachedLimitMarker()
    {
        using var icon = TrayIconFactory.Create(TrayStatusColor.Red, 0d);
        using var bitmap = icon.ToBitmap();

        Assert.True(ContainsColor(bitmap, 239, 68, 68));
        Assert.True(ContainsColor(bitmap, 209, 213, 219));
    }

    private static bool ContainsColor(Bitmap bitmap, int red, int green, int blue)
    {
        return CountColor(bitmap, red, green, blue) > 0;
    }

    private static int CountColor(Bitmap bitmap, int red, int green, int blue)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A > 240
                    && Math.Abs(pixel.R - red) <= 2
                    && Math.Abs(pixel.G - green) <= 2
                    && Math.Abs(pixel.B - blue) <= 2)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
