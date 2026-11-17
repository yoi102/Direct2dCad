using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.Client.Common.Tests;

public sealed class DeferredScopeTests
{
    [Fact]
    public void RunsTheCallbackExactlyOnce()
    {
        var calls = 0;
        var scope = new DeferredScope(() => calls++);
        scope.Dispose();
        scope.Dispose();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RejectsNullCallback() => Assert.Throws<ArgumentNullException>(() => new DeferredScope(null!));
}

public sealed class EnumDescriptionTypeConverterTests
{
    [TypeConverter(typeof(EnumDescriptionTypeConverter))]
    public enum Sample
    {
        [Description("First value")] First,
        Second,
        [Description("")] Third,
        [LocalizedDescription("Dots", typeof(Strings))] Localized,
        [LocalizedDescription("__no_such_key__", typeof(Strings))] Missing
    }

    private static readonly TypeConverter Converter = TypeDescriptor.GetConverter(typeof(Sample));

    [Fact]
    public void TheEnumUsesTheDescriptionConverter() => Assert.IsType<EnumDescriptionTypeConverter>(Converter);

    [Theory]
    [InlineData(Sample.First, "First value")]
    [InlineData(Sample.Second, "Second")]
    [InlineData(Sample.Third, "Third")]
    [InlineData(Sample.Missing, "[[__no_such_key__]]")]
    public void ConvertsToTheDescriptionOrTheName(Sample value, string expected)
        => Assert.Equal(expected, Converter.ConvertToString(value));

    [Fact]
    public void LocalizedDescriptionsComeFromTheResourceTable()
    {
        var text = Converter.ConvertToString(Sample.Localized);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("[[", text);
    }

    [Fact]
    public void NullConvertsToEmptyString() => Assert.Equal(string.Empty, Converter.ConvertTo(null, typeof(string)));

    [Fact]
    public void NonStringConversionsKeepEnumConverterBehaviour()
    {
        // Previously every non-string destination returned "", which broke design-time serialization.
        Assert.IsType<InstanceDescriptor>(Converter.ConvertTo(Sample.Second, typeof(InstanceDescriptor)));
        Assert.Equal(Sample.Second, Converter.ConvertFrom("Second"));
    }

    [Fact]
    public void LocalizedAttributesOfTheSameResourceTypeShareOneManager()
    {
        var first = new LocalizedDescriptionAttribute("Dots", typeof(Strings));
        var second = new LocalizedDescriptionAttribute("Lines", typeof(Strings));
        var field = typeof(LocalizedDescriptionAttribute).GetField("resourceManager",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Same(field.GetValue(first), field.GetValue(second));
    }
}
