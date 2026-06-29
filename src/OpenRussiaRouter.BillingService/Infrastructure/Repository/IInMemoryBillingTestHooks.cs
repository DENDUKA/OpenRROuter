using OpenRussiaRouter.BillingService.Domain.Entities;

namespace OpenRussiaRouter.BillingService.Infrastructure.Repository;

/// <summary>
/// Test-only hooks exposed by <see cref="InMemoryBillingRepository"/> for scenarios that need to
/// bypass the public persistence flow (e.g. simulating a crash that leaves a PaymentEvent persisted
/// but its side-effects unapplied). Production code must not depend on this interface.
/// </summary>
internal interface IInMemoryBillingTestHooks
{
    void InjectPaymentEvent(PaymentEvent evt);
}
