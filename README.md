# Skote MIDI Control

MIDI control of live visuals using [Keijiro's Minis](https://github.com/keijiro/Minis).

## Contents

Namespace: `Skote.Midi`

| Component | |
|---|---|
| `MidiMappingManager` + **Window > MIDI Mapping** | Click an incoming note/CC, click a target, done. CCs map continuously to a min/max range, notes trigger envelopes (base → peak → decay), and both can fire actions. Mappings can be bound to one device or to any device, and are saved in the scene |
| `MidiSequencer` | Records CC pad hits and loops them back through the mapping manager |
| `MidiInputLogger` | Logs incoming MIDI |
| `Rotator`, `CameraZController`, `AnimatorSpeedController` | Simple MIDI-controllable targets |

Targets are defined in the manager's inspector (object + target type + property/action name). Built-in target types:

| Type | Kind | Property / action |
|---|---|---|
| `VFXFloat` | value | exposed float on a `VisualEffect` |
| `VFXEvent` | trigger | event name |
| `VideoSpeed` | value | `VideoPlayer` playback speed |
| `PostProcessing` | value | URP `Volume`: `exposure`, `contrast`, `saturation`, `hueshift`, `bloomintensity`, `bloomthreshold`, `bloomscatter`, `vignetteintensity`, `vignettesmoothness`, `chromaticaberration`, `motionblur`, `filmgrain`, `temperature`, `tint`, `focusdistance`, `aperture`, `lensdistortion` |
| `RotatorSpeed`, `CameraZ`, `AnimatorSpeed` | value | |
| `AnimatorPause` | trigger | toggles `AnimatorSpeedController` pause |

Trigger targets fire on note-on, or on the press (rising edge) of a CC button.

### Optional integrations

These targets switch on automatically when the matching package is installed:

| Package | Adds targets |
|---|---|
| `com.skote.vfx-toolkit` | `VFXManager` (value), `VFXManagerAction` (trigger: group action like `KillA`/`ReviveB`, or toggles a bool), `VFXVector2` (value) |
| `com.skote.vfx-toolkit` + mesh-to-sdf | `AnimatorManagerSpeed` (value), `AnimatorManagerAction` (trigger: `Next<Group>`/`Prev<Group>` SDF switch, animator trigger, or empty to toggle pause) |
| `com.skote.dynamic-temporal-blur` | `TemporalBlurOpacity` (value) |
| `com.skote.metavido-extensions` | `MetavidoAction` (trigger: `play`, `next`, `prev`, `random`, `stop`) and the standalone `MetavidoManagerMidiBinder` |

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
