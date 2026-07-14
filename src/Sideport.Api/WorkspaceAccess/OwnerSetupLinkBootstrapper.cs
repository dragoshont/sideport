namespace Sideport.Api.WorkspaceAccess;

internal interface IOwnerSetupLinkSink
{
    void Write(Uri setupUrl);
}

internal sealed class ConsoleOwnerSetupLinkSink : IOwnerSetupLinkSink
{
    public void Write(Uri setupUrl)
    {
        ArgumentNullException.ThrowIfNull(setupUrl);
        Console.Error.WriteLine($"Sideport Owner setup (one-time private link): {setupUrl.AbsoluteUri}");
    }
}

internal sealed class OwnerSetupLinkBootstrapper(
    WorkspaceAccessStore store,
    Uri publicOrigin,
    IOwnerSetupLinkSink sink,
    TimeProvider? timeProvider = null)
{
    private const string FirstBootIdempotencyKey = "sideport-native-first-owner-v1";
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    internal async Task EnsureAsync(CancellationToken ct = default)
    {
        WorkspaceAccessDocument? current = await store.ReadAsync(ct).ConfigureAwait(false);
        if (current?.Workspace.State == WorkspaceLifecycleState.Active)
            return;

        DateTimeOffset now = _time.GetUtcNow();
        if (current?.OwnerClaims.Any(claim =>
                claim.Status == WorkspaceAuthorityStatus.Pending && claim.ExpiresAt > now) == true)
        {
            return;
        }

        WorkspaceOwnerClaimCreateResult created = await store.CreateOwnerClaimAsync(
            new WorkspaceOwnerClaimCreateRequest(
                ExpectedOwnerMemberId: null,
                ImpactVersion: null,
                Lifetime: TimeSpan.FromMinutes(30),
                IdempotencyKey: FirstBootIdempotencyKey,
                RequestId: "sideport-native-first-owner"),
            ct).ConfigureAwait(false);
        if (!created.Created || string.IsNullOrWhiteSpace(created.Token))
            return;

        var setupUrl = new UriBuilder(new Uri(publicOrigin, "/owner-claim"))
        {
            Fragment = created.Token,
        };
        sink.Write(setupUrl.Uri);
    }
}
