namespace OpenRussiaRouter.BillingService.Domain;

/// <summary>
/// Balance ledger entry type.
/// </summary>
public enum LedgerEntryType
{
    VirtualTopUp = 0,
    UsageCharge = 1,
    Correction = 2
}

