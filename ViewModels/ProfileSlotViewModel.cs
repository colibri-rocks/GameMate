using GameMate.Infrastructure;

namespace GameMate.ViewModels;

/// <summary>
/// View model for one profile slot button: the name shown on it, whether it is the slot currently in
/// use, and the in-place rename state.
/// </summary>
/// <remarks>
/// The slot owns only its presentation state. Persisting a renamed slot is the responsibility of
/// <see cref="MainViewModel"/>, which listens for changes to <see cref="Name"/>.
/// </remarks>
public sealed class ProfileSlotViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _nameBeforeEditing = string.Empty;
    private bool _isActive;
    private bool _isEditingName;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSlotViewModel"/> class.
    /// </summary>
    /// <param name="number">One-based slot number, also used as the apply command parameter.</param>
    public ProfileSlotViewModel(int number)
    {
        Number = number;

        StartRenameCommand = new RelayCommand(StartRename);
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
    }

    /// <summary>
    /// Gets the one-based slot number.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// Gets or sets the name shown on the button.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this is the slot currently in use, which drives the
    /// highlight on the button.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the name is being edited in place.
    /// </summary>
    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetProperty(ref _isEditingName, value);
    }

    /// <summary>
    /// Gets the command that opens the in-place rename editor. Bound to a double click on the button.
    /// </summary>
    public RelayCommand StartRenameCommand { get; }

    /// <summary>
    /// Gets the command that accepts the edited name. Bound to Enter in the editor.
    /// </summary>
    public RelayCommand CommitRenameCommand { get; }

    /// <summary>
    /// Gets the command that discards the edit and restores the previous name. Bound to Escape in the
    /// editor.
    /// </summary>
    public RelayCommand CancelRenameCommand { get; }

    /// <summary>
    /// Enters edit mode, remembering the current name so a cancelled edit can be reverted.
    /// </summary>
    private void StartRename()
    {
        // The parent writes every name change through to the stored profile synchronously, so this
        // name is always the authoritative one and cannot be stale.
        _nameBeforeEditing = Name;

        IsEditingName = true;
    }

    /// <summary>
    /// Leaves edit mode keeping the edited name, or the previous name when the edit left it blank.
    /// </summary>
    private void CommitRename()
    {
        // A blank name would leave the button and the status line empty, so the edit is dropped rather
        // than stored.
        Name = string.IsNullOrWhiteSpace(Name) ? _nameBeforeEditing : Name.Trim();

        IsEditingName = false;
    }

    /// <summary>
    /// Leaves edit mode and restores the name the slot had before editing started.
    /// </summary>
    private void CancelRename()
    {
        Name = _nameBeforeEditing;

        IsEditingName = false;
    }
}
