namespace GameMate.Models;

/// <summary>
/// Describes the levels one NVAPI colour control accepts, as reported by the driver.
/// </summary>
/// <param name="Minimum">Lowest level the driver accepts for the control.</param>
/// <param name="Maximum">Highest level the driver accepts for the control.</param>
/// <param name="Current">Level the control currently holds.</param>
/// <param name="Default">Level the driver treats as the default, used when settings are reset.</param>
public sealed record ColorLevelRange(int Minimum, int Maximum, int Current, int Default);
