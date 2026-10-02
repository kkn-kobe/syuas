using Syuas.Core.Models;

namespace Syuas.Core.Services;

public interface IRecoveryStore : IDisposable
{
    void Write(RecoverySnapshot snapshot);
    void Retire(Guid documentId);
    IReadOnlyList<RecoveryCandidate> ListCandidates();
    // Copy durably into this session before retiring the source. Recheck the source lease.
    RecoverySnapshot Claim(RecoveryKey key);
    void Discard(RecoveryKey key);
}
