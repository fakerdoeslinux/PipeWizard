using System.Runtime.InteropServices;

namespace PipeWizard.Services;

internal static class RustNative
{
    internal const int Valid = 0;
    internal const int InvalidHeader = 1;
    internal const int WrongGame = 2;
    internal const int UnsupportedRevision = 3;

    [DllImport("pipewizard_native", EntryPoint = "pipewizard_validate_disc_header",
        CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ValidateDiscHeader(byte[] header, nuint length);
}
