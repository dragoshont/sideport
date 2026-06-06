using Sideport.Core;

namespace Sideport.Orchestrator;

/// <summary>
/// Thrown when an operation needs an authenticated Apple session but the only
/// way to get one is an interactive second factor (which cannot be completed
/// unattended). The API layer catches this to prompt the operator.
/// </summary>
public sealed class InteractiveLoginRequiredException : Exception
{
    /// <summary>The Apple ID that needs an interactive sign-in.</summary>
    public string AppleId { get; }

    /// <summary>The pending 2FA challenge, when a login was actually attempted.</summary>
    public AppleLoginChallenge? Challenge { get; }

    public InteractiveLoginRequiredException(string appleId, AppleLoginChallenge? challenge = null)
        : base($"Apple ID '{Redact(appleId)}' requires an interactive sign-in (2FA).")
    {
        AppleId = appleId;
        Challenge = challenge;
    }

    private static string Redact(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= 3 ? "***" : value[..3] + "***";
}
