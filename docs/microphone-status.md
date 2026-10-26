# Microphone startup and tray status

SignalShift keeps its purple app icon and shows a separate microphone status icon beside it. The microphone icon shows the state of the Windows default input devices for audio and calls:

- Green microphone: all tracked microphones are live.
- Red microphone with a diagonal slash: all tracked microphones are muted.
- Amber microphone with a warning: default microphone roles have mixed mute states.
- Gray microphone with an unavailable badge: the microphone state cannot be read.

Hover over the icon to read the state. Windows may put SignalShift in the hidden-icons area beside the clock; open the **^** menu and drag the microphone out to keep it visible. The tray menu still provides microphone mute and the other audio controls. Mute changes made by other programs update the icon through audio notifications.

## Start the microphone live at sign-in

In **Settings**, enable **Start with Windows** and **Start microphone live at Windows sign-in**. This makes the current default microphone devices live once at Windows sign-in. If the devices are not ready, SignalShift waits for up to 30 seconds. A manual mute command cancels the pending startup action.

An ordinary app launch, update or restart does not undo an intentional mute. The option is off by default, including after upgrading, so users explicitly choose whether sign-in may unmute their microphones. With it off, SignalShift keeps the mute state Windows provides.

The startup action and the icon read endpoint controls; they do not record microphone audio or change speaker volume. Failed mute writes retain the existing rollback and verification behavior. The notification reads **Microphone muted** with a red slashed microphone or **Microphone live** with a green microphone.

Icons and tooltip strings are cached for the host lifetime, with no periodic icon regeneration. Once the sign-in action completes, expires or is cancelled, its retry timer stops.
