namespace EsSdk;

/// <summary>
/// Location wording for the one failure the SDK cannot report itself: the native DLL never loaded,
/// so no <c>Everything_*</c> call ever ran. The server uses this to say what to fix.
/// </summary>
public static class SdkLoadFailure
{
    /// <summary>Name of the 64-bit Everything SDK library.</summary>
    public const string DllName = "Everything64.dll";

    /// <summary>True when the exception means the native library could not be loaded.</summary>
    public static bool IsLoadFailure(Exception exception) =>
        exception is DllNotFoundException or BadImageFormatException;

    /// <summary>Agent- and user-facing explanation of a load failure.</summary>
    public static string Describe(Exception exception) =>
        exception is BadImageFormatException
            ? $"{DllName} could not be loaded: it is not a 64-bit Windows library. Install 64-bit Everything, " +
              "or point EVERYTHING_SDK_PATH at the 64-bit DLL."
            : $"{DllName} was not found. Everything (voidtools) must be installed, with the SDK DLL " +
              "reachable in one of these ways: EVERYTHING_SDK_PATH set to the DLL or its folder, the DLL " +
              "next to this executable, or the Everything install folder (e.g. C:\\Program Files\\Everything). " +
              "The DLL ships with Everything and is not bundled here.";
}
