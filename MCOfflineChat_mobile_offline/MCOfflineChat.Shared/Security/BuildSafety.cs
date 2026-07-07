namespace MCOfflineChat.Shared.Security;

/// <summary>
/// Compile-time safety guard. In Release builds, DevelopmentMode is physically
/// impossible to enable — the constant is false and the JIT eliminates all branches.
/// </summary>
public static class BuildSafety
{
#if DEBUG
    public const bool DevelopmentMode = true;
#else
    public const bool DevelopmentMode = false;
#endif
}
