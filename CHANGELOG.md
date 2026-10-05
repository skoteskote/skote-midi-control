# Changelog

## [0.2.0] - 2026-10-05

- `MidiMappingManager` is now the single MIDI system. Removed `MidiInputRouter` and `GenericMidiRouter`.
- New target types: `VFXVector2`, `TemporalBlurOpacity`, `VFXManagerAction`, `AnimatorManagerAction`, `AnimatorPause`, `MetavidoAction`.
- Trigger targets fire on note-on or on the rising edge of a CC button.
- Mappings with an empty device name respond to any device.
- `PostProcessing` supports the full set of Volume properties the old router had.

## [0.1.0] - 2026-10-05

- Initial extraction from the Volumetrics project.
