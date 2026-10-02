using Syuas.Core.Models;

namespace Syuas.Core.Services;

public interface IRecoveryStore : IDisposable
{
    void Write(RecoverySnapshot snapshot);
    void Retire(Guid documentId);
    IReadOnlyList<RecoveryCandidate> ListCandidates();
    // Copy durably into this session before retiring the source. Recheck the source lease.
    RecoverySnapshot Claim(RecoveryKey key);
    RecoverySnapshot Claim(RecoveryKey key, Guid documentId) => documentId == key.DocumentId
        ? Claim(key) : throw new NotSupportedException("別の文書IDへの復元に対応していません。");
    void Discard(RecoveryKey key);
}
