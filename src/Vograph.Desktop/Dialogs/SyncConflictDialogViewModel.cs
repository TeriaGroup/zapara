using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Services;
using Zapara.Contracts.Sync;

namespace Vograph.Desktop.Dialogs;

/// <summary>409 keep-local / keep-server, or 410 abort. Enter does not pick a side; timestamps never auto-resolve.</summary>
public sealed partial class SyncConflictDialogViewModel : DialogViewModelBase
{
    private readonly SyncValue? localValue;
    private readonly SyncRecord? serverRecord;
    private readonly Guid newOpId;

    private SyncConflictDialogViewModel(
        PrivateSyncConflict conflict,
        SyncValue? localValue,
        SyncRecord? serverRecord,
        Guid newOpId,
        bool expired)
    {
        Conflict = conflict;
        this.localValue = localValue;
        this.serverRecord = serverRecord;
        this.newOpId = newOpId;
        IsExpired = expired;
        Title = Loc.Current.T("syncConflictTitle");
        Body = Loc.Current.T(expired ? "syncExpired" : "syncConflictBody");
        KeepLocalText = Loc.Current.T("syncKeepLocal");
        KeepServerText = Loc.Current.T("syncKeepServer");
        Closed += OnClosed;
    }

    public static SyncConflictDialogViewModel ForConflict(
        PrivateSyncConflict conflict, SyncValue? localValue, SyncRecord serverRecord, Guid newOpId)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        ArgumentNullException.ThrowIfNull(serverRecord);
        if (newOpId == Guid.Empty) throw new ArgumentException("Новый opId обязателен.", nameof(newOpId));
        if (!string.Equals(conflict.EntityType, serverRecord.EntityType, StringComparison.Ordinal) ||
            conflict.EntityId != serverRecord.EntityId)
            throw new ArgumentException("Локальный черновик и серверная запись относятся к разным сущностям.");
        return new(conflict, localValue, serverRecord, newOpId, expired: false);
    }

    public static SyncConflictDialogViewModel ForExpired(PrivateSyncConflict conflict)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        if (string.IsNullOrWhiteSpace(conflict.EntityType)) throw new ArgumentException("Тип сущности обязателен.", nameof(conflict));
        if (conflict.EntityId == Guid.Empty) throw new ArgumentException("Идентификатор сущности обязателен.", nameof(conflict));
        return new(conflict, localValue: null, serverRecord: null, newOpId: Guid.Empty, expired: true);
    }

    public PrivateSyncConflict Conflict { get; }
    public string Body { get; }
    public string KeepLocalText { get; }
    public string KeepServerText { get; }
    public bool IsExpired { get; }
    public bool CanChooseVersion => !IsExpired;
    public SyncConflictDecision? Decision { get; private set; }

    [RelayCommand(CanExecute = nameof(CanChooseVersion))]
    private void KeepLocal()
    {
        Decision = SyncConflictDecision.KeepLocal(Conflict.EntityType, Conflict.EntityId, localValue, serverRecord!, newOpId);
        Close(true);
    }

    [RelayCommand(CanExecute = nameof(CanChooseVersion))]
    private void KeepServer()
    {
        Decision = SyncConflictDecision.KeepServer(Conflict.EntityType, Conflict.EntityId, localValue, serverRecord!);
        Close(true);
    }

    protected override bool CanConfirm() => IsExpired;

    protected override bool Validate()
    {
        if (!IsExpired) return false;
        Decision = SyncConflictDecision.Expired410(Conflict.EntityType, Conflict.EntityId);
        return true;
    }

    private void OnClosed(DialogViewModelBase _)
    {
        if (IsExpired && Decision is null)
            Decision = SyncConflictDecision.Expired410(Conflict.EntityType, Conflict.EntityId);
    }
}
