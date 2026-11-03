namespace backend_stockpilot.Services.Interfaces;

public interface ICurrentStore
{
    int? UserId { get; }
    bool IsOwner { get; }

    // The store filter for this request.
    //   null  = no filter (owner on "All stores", login, seeding, design-time)
    //   id    = that store only
    //   -1    = a non-owner whose token has no store claim: matches nothing (fail closed)
    int? StoreId { get; }

    bool TryGetWriteStoreId(out int storeId);
}