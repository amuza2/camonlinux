namespace camonlinux.Models;

/// <summary>
/// One entry of a choice submenu (device, resolution, rotation or digital zoom): the label
/// to show, the value to hand to the command, and whether it is the active entry.
/// </summary>
/// <remarks>
/// Built by the view model rather than in the view's code-behind so that what the menu
/// will contain is testable — the submenus are only populated when they are opened, which
/// means a mistake in them is invisible until someone clicks the menu.
/// </remarks>
/// <param name="Header">Label shown in the menu.</param>
/// <param name="Parameter">Value passed to the command as its parameter.</param>
/// <param name="IsChecked">Whether this entry is the active one.</param>
public sealed record MenuChoice(string Header, object? Parameter, bool IsChecked);
