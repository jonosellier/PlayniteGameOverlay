![](https://img.shields.io/github/downloads/jonosellier/playnitegameoverlay/total?sort=semver)

## Audio output selection

Open **Audio output** in the overlay to choose an active Windows playback device.
The current default output is marked with a check. Navigate with the controller,
confirm with A/Cross, and press B/Circle or Back to return to the overlay.

The selector changes the Windows default output for console, multimedia and
communications roles. It does not change per-application routing. Some games
keep their original audio device until their audio engine or the game restarts.

No external audio utility or PC Mode Manager installation is required. Device
enumeration uses Windows Core Audio; changing the default uses the undocumented
Windows PolicyConfig COM interface. Failures are displayed in the selector.
