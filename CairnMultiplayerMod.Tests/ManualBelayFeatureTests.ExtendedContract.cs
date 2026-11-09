using System.IO;
using CairnMultiplayerMod.Features;
using Xunit;

namespace CairnMultiplayerMod.Tests;

public sealed partial class ManualBelayFeatureTests
{
    [Theory]
    [InlineData(true, 13)]
    [InlineData(true, 255)]
    [InlineData(false, 12)]
    [InlineData(false, 255)]
    public void ExtendedDecodersRejectUnknownOperationsAndPhases(bool request, byte value)
    {
        var bytes = request ? Bytes(new ManualBelayCommand()) : Bytes(new ManualBelayMessage());
        bytes[0] = value;
        using var reader = new BinaryReader(new MemoryStream(bytes));
        if (request) Assert.Throws<InvalidDataException>(() => new ManualBelayCommand().Deserialize(reader));
        else Assert.Throws<InvalidDataException>(() => new ManualBelayMessage().Deserialize(reader));
    }
}
