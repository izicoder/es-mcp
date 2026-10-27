using EsSdk;

namespace EsCli.Tests;

public class SdkLoadFailureTests
{
    [Fact]
    public void Recognizes_the_two_ways_the_dll_can_fail_to_load()
    {
        Assert.True(SdkLoadFailure.IsLoadFailure(new DllNotFoundException("nope")));
        Assert.True(SdkLoadFailure.IsLoadFailure(new BadImageFormatException()));
    }

    [Theory]
    [MemberData(nameof(OtherExceptions))]
    public void Leaves_unrelated_failures_alone(Exception exception)
    {
        Assert.False(SdkLoadFailure.IsLoadFailure(exception));
    }

    public static TheoryData<Exception> OtherExceptions => new()
    {
        new EverythingException(2),
        new ArgumentException("bad query"),
        new InvalidOperationException(),
    };

    [Fact]
    public void Missing_dll_message_lists_every_lookup_location()
    {
        var text = SdkLoadFailure.Describe(new DllNotFoundException("everything64.dll"));

        Assert.Contains(SdkLoadFailure.DllName, text, StringComparison.Ordinal);
        Assert.Contains("EVERYTHING_SDK_PATH", text, StringComparison.Ordinal);
        Assert.Contains("next to this executable", text, StringComparison.Ordinal);
        Assert.Contains("install folder", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrong_architecture_message_says_64_bit()
    {
        var text = SdkLoadFailure.Describe(new BadImageFormatException());

        Assert.Contains("64-bit", text, StringComparison.Ordinal);
    }
}
