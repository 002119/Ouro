using System;
using System.Windows.Input;

namespace Ouro.Services.Input
{
    /// <summary>
    /// Service contract for global system-wide input hotkeys.
    /// Captures input triggers even when the application is minimized or running behind exclusive fullscreen games.
    /// </summary>
    public interface IInputService : IDisposable
    {
        /// <summary>
        /// True if the save hotkey is currently registered and active.
        /// </summary>
        bool IsRegistered { get; }

        /// <summary>
        /// Fires when the registered replay clip save hotkey is pressed.
        /// </summary>
        event EventHandler? OnSaveClipRequested;

        /// <summary>
        /// Registers or updates the global system hotkey for saving retrospective clips.
        /// Defaults to F12 with no modifiers.
        /// </summary>
        void RegisterSaveHotkey(Key key = Key.F12, ModifierKeys modifiers = ModifierKeys.None);

        /// <summary>
        /// Registers or updates the global hotkey from a user string expression (e.g. "F12", "Ctrl+F12", "Alt+F10").
        /// </summary>
        void RegisterSaveHotkey(string hotkeyPattern);

        /// <summary>
        /// Removes active global hotkey hooks from the operating system.
        /// </summary>
        void UnregisterSaveHotkey();
    }
}
