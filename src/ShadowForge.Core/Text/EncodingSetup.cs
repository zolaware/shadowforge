using System.Text;

namespace ShadowForge.Text;

public static class EncodingSetup
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _registered = true;
    }
}
