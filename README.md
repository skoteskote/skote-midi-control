# Skote MIDI Control

MIDI control of live visuals using [Keijiro's Minis](https://github.com/keijiro/Minis).

## Contents

Namespace: `Skote.Midi`

| Component | |
|---|---|
| `MidiMappingManager` + **Window > MIDI Mapping** | Live click-to-map of notes/CCs to targets, with envelopes; mappings are saved in the scene |
| `GenericMidiRouter` | Inspector-configured CC/note/envelope bindings for any device |
| `MidiInputRouter` | Fixed layout for a Korg nanoKONTROL2 |
| `MidiSequencer` | Records CC pad hits and loops them |
| `MidiInputLogger` | Logs incoming MIDI |
| `Rotator`, `CameraZController`, `AnimatorSpeedController` | Simple MIDI-controllable targets |

Built-in targets: `VisualEffect`, `VideoPlayer`, URP `Volume` overrides, and the components above.

### Optional integrations

These targets switch on automatically when the matching package is installed:

| Package | Adds targets |
|---|---|
| `com.skote.vfx-toolkit` | `VFXManager`, `VFXVector2Controller` |
| `com.skote.vfx-toolkit` + mesh-to-sdf | `AnimatorManager` (speed, triggers, SDF group switching) |
| `com.skote.dynamic-temporal-blur` | `DynamicTemporalBlur` opacity |
| `com.skote.metavido-extensions` | `MetavidoManager` actions + `MetavidoManagerMidiBinder` |

## Install

Add to `Packages/manifest.json` (pin a tag):

```json
"com.skote.midi-control": "https://github.com/skoteskote/skote-midi-control.git#v0.1.0"
```
Keijiro's packages come from his npm registry, so the project's `Packages/manifest.json` needs this scoped registry:

```json
"scopedRegistries": [
  { "name": "Keijiro", "url": "https://registry.npmjs.com", "scopes": [ "jp.keijiro" ] }
]
```

## Editing from any project

Clone the repo into the project's `Packages/` folder. Unity uses the embedded copy in place of the manifest entry, so you can edit, commit and push from there:

```
git clone git@github.com:skoteskote/skote-midi-control.git Packages/com.skote.midi-control
```

Add `Packages/com.skote.midi-control` to the project's Plastic `ignore.conf` (or `.gitignore`). When you're done, tag a release and bump the `#tag` in the manifest.

## License

MIT. See [LICENSE.md](LICENSE.md).
