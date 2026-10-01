using System.Net;
using System.Security.Cryptography;
using System.Text;
using DriverPilot.RemoteDiagnosis;
using static DriverPilot.Tests.Assert;
namespace DriverPilot.Tests;

static partial class SessionTests
{
    static async Task PersistenceFailures()
    {
        foreach (var error in new Exception[] { new IOException("PRIVATE disk full"), new UnauthorizedAccessException("PRIVATE denied"), new InvalidOperationException("PRIVATE injected") })
        {
            var world = new World();
            var session = await Paired(world);
            session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom());
            session.Review(false);
            var preview = session.Preview!;
            var sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preview)));
            world.Store.SaveFailure = error;
            world.Server.Reply(HttpStatusCode.Accepted, Accepted(sha));
            Check(!await session.SendReviewedAsync(), "accepted upload persistence error returns safely");
            Check(session.HasPendingUpload && session.CanSend && session.Preview == preview && session.CaseRef == null && world.Store.State.Case == null, "failed save preserves reviewed request and committed state");
            Check(!session.Status.Contains("PRIVATE") && !session.Status.Contains("nichts gespeichert") && session.Status.Contains("Server"), "local failure is sanitized and acknowledges server outcome");
            var first = world.Server.Requests[^1];
            world.Store.SaveFailure = null;
            world.Server.Reply(HttpStatusCode.Accepted, Accepted(sha));
            Check(await session.SendReviewedAsync() && session.CaseRef?.CaseId == CaseId && world.Store.State.Case == session.CaseRef, "accepted case recovers idempotently once disk writable");
            Check(world.Server.Requests[^1].IdempotencyKey == first.IdempotencyKey && world.Server.Requests[^1].Body.SequenceEqual(first.Body), "local save retry retains exact bytes and key");
            world.Server.Reply(HttpStatusCode.OK, CaseJson("released", sha, Result(sha), poll: null));
            Check(await session.RefreshAsync(), "released before failing delete");
            var original = world.Store.State;
            var view = session.Case;
            world.Store.SaveFailure = error;
            world.Server.Reply(HttpStatusCode.NoContent);
            Check(!await session.DeleteCaseAsync() && session.CaseRef == original.Case && session.Case == view && world.Store.State == original, "delete save failure retains reference and view for recovery");
            world.Server.ReplyError(HttpStatusCode.NotFound, "not_found");
            Check(!await session.RefreshAsync() && session.CaseRef == original.Case, "not-found cleanup failure handled inside error path");
            session.Unpair();
            Check(session.Paired && session.CaseRef == original.Case && world.Store.State == original && !session.Status.Contains("PRIVATE"), "manual unpair save failure preserves current access and case");
            world.Server.ReplyError(HttpStatusCode.Unauthorized, "token_invalid");
            Check(!await session.RefreshAsync() && !session.Paired && session.NextPoll(0) == null && world.Store.State == original, "revoked token disables runtime even when cleanup fails");
            world.Store.SaveFailure = null;
            session.Unpair();
            Check(world.Store.State.Access == null, "local forget can be explicitly retried");

            var unpaired = world.Session();
            world.Store.SaveFailure = error;
            world.Server.Reply(HttpStatusCode.Created, Contract.Fixture("valid/pairing-redeem-response/synthetic.json"));
            Check(!await unpaired.PairAsync("https://diagnose.example.invalid", Invitation) && !unpaired.Paired && world.Store.State.Access == null, "pair save failure cannot publish unpersisted identity");
        }
    }
}
