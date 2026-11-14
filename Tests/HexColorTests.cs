using Farvane.PaintColorPicker;
using Xunit;

public class HexColorTests
{
    [Theory]
    [InlineData("#E96512", 0xE9, 0x65, 0x12)]
    [InlineData("e96512", 0xE9, 0x65, 0x12)]
    [InlineData("  #3584c4 ", 0x35, 0x84, 0xC4)]
    [InlineData("#fff", 0xFF, 0xFF, 0xFF)]
    [InlineData("0A8", 0x00, 0xAA, 0x88)]
    [InlineData("#000000", 0, 0, 0)]
    public void Parses_valid_values(string text, int r, int g, int b)
    {
        Assert.True(HexColor.TryParse(text, out var color, out var error));
        Assert.Equal(new HexColor((byte)r, (byte)g, (byte)b), color);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GG0000")]
    [InlineData("#12 456")]
    [InlineData("##123456")]
    [InlineData("#E96512FF")]
    [InlineData("+12345")]
    public void Rejects_invalid_values(string? text)
    {
        Assert.False(HexColor.TryParse(text, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Formats_as_uppercase_hash_hex()
    {
        Assert.Equal("#42B77B", new HexColor(0x42, 0xB7, 0x7B).ToString());
        Assert.Equal("#000A0F", new HexColor(0, 10, 15).ToString());
    }

    [Fact]
    public void Round_trips_every_channel_value()
    {
        for (var v = 0; v < 256; v++)
        {
            var color = new HexColor((byte)v, (byte)(255 - v), (byte)(v ^ 0x5A));
            Assert.True(HexColor.TryParse(color.ToString(), out var parsed, out _));
            Assert.Equal(color, parsed);
        }
    }
}
