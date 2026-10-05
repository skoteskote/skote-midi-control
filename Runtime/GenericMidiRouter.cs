using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;
using UnityEngine.Video;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Minis;
using System.Collections.Generic;

#if SKOTE_VFX_TOOLKIT
using Skote.Vfx;
using VFXManager = Skote.Vfx.VFXManager;
#endif
#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
using Skote.Vfx.Sdf;
#endif
#if SKOTE_DYNAMIC_TEMPORAL_BLUR
using Skote.TemporalBlur;
#endif
#if SKOTE_METAVIDO_EXTENSIONS
using Skote.MetavidoExtensions;
#endif

namespace Skote.Midi
{
    /// <summary>
    /// Generic MIDI Router - works with any MIDI device (hardware synths, DAWs, external controllers via Focusrite, etc.)
    /// Unlike MidiInputRouter which is hardcoded for Korg NanoKontrol2, this allows arbitrary CC mappings.
    /// </summary>
    public class GenericMidiRouter : MonoBehaviour
    {
        [Header("Device Filtering")]
        [Tooltip("If specified, only listen to devices containing this string in their name. Leave empty to listen to all devices.")]
        public string deviceNameFilter = "";

        [Tooltip("If true, logs all incoming MIDI for debugging")]
        public bool debugMode = false;

        [Header("CC Bindings (Continuous Controls)")]
        [Tooltip("Define CC number to target mappings for knobs, sliders, mod wheel, etc.")]
        public List<GenericCCBinding> ccBindings = new List<GenericCCBinding>();

        [Header("Note Bindings (Triggers)")]
        [Tooltip("Define note number to target mappings for keyboard keys, pads, etc.")]
        public List<GenericNoteBinding> noteBindings = new List<GenericNoteBinding>();

        [Header("Envelope Bindings (Drum Triggers with Decay)")]
        [Tooltip("Define note-triggered envelopes that spike on hit and decay over time - perfect for drums")]
        public List<EnvelopeBinding> envelopeBindings = new List<EnvelopeBinding>();

        [Header("Learn Mode")]
        [Tooltip("When enabled, the next CC/Note received will be assigned to pendingBindingIndex")]
        public bool learnMode = false;
        [Tooltip("Index in ccBindings or noteBindings to assign the next incoming message to (-1 = none)")]
        public int pendingBindingIndex = -1;
        [Tooltip("Whether we're learning a CC (true) or Note (false)")]
        public bool learningCC = true;

        // Cached lookups for performance
        private Dictionary<int, GenericCCBinding> _ccLookup;
        private Dictionary<int, GenericNoteBinding> _noteLookup;
        private Dictionary<int, EnvelopeBinding> _envelopeLookup;
        private HashSet<MidiDevice> _subscribedDevices = new HashSet<MidiDevice>();

        private void Awake()
        {
            RebuildLookups();
        }

        private void Update()
        {
            // Process envelope decay
            if (_envelopeLookup == null) return;

            float deltaTime = Time.deltaTime;
            foreach (var binding in envelopeBindings)
            {
                if (binding.target == null) continue;

                // Decay toward base value
                if (binding.currentValue > binding.baseValue)
                {
                    float decayAmount = (binding.peakValue - binding.baseValue) / binding.decayTime * deltaTime;
                    binding.currentValue = Mathf.Max(binding.baseValue, binding.currentValue - decayAmount);
                    ApplyEnvelopeValue(binding, binding.currentValue);
                }
            }
        }

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;

            foreach (var device in InputSystem.devices)
            {
                if (device is MidiDevice midiDevice)
                    TrySubscribeToDevice(midiDevice);
            }
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;

            // Unsubscribe from all devices
            foreach (var device in _subscribedDevices)
            {
                device.onWillControlChange -= OnControlChange;
                device.onWillNoteOn -= OnNoteOn;
            }
            _subscribedDevices.Clear();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is MidiDevice midiDevice)
            {
                if (change == InputDeviceChange.Added)
                {
                    TrySubscribeToDevice(midiDevice);
                }
                else if (change == InputDeviceChange.Removed)
                {
                    _subscribedDevices.Remove(midiDevice);
                    if (debugMode)
                        Debug.Log($"[GenericMidi] Device disconnected: {midiDevice.description.product}");
                }
            }
        }

        private void TrySubscribeToDevice(MidiDevice device)
        {
            // Check device name filter
            if (!string.IsNullOrEmpty(deviceNameFilter))
            {
                string deviceName = device.description.product ?? device.name;
                if (!deviceName.ToLower().Contains(deviceNameFilter.ToLower()))
                {
                    if (debugMode)
                        Debug.Log($"[GenericMidi] Ignoring device (doesn't match filter): {deviceName}");
                    return;
                }
            }

            if (_subscribedDevices.Contains(device))
                return;

            device.onWillControlChange += OnControlChange;
            device.onWillNoteOn += OnNoteOn;
            _subscribedDevices.Add(device);

            string name = device.description.product ?? device.name;
            Debug.Log($"[GenericMidi] Subscribed to device: {name}");
        }

        /// <summary>
        /// Call this after modifying bindings at runtime to update the lookup dictionaries.
        /// </summary>
        public void RebuildLookups()
        {
            _ccLookup = new Dictionary<int, GenericCCBinding>();
            foreach (var binding in ccBindings)
            {
                if (binding.ccNumber >= 0 && binding.ccNumber <= 127)
                {
                    _ccLookup[binding.ccNumber] = binding;
                    binding.ResolveTarget();
                }
            }

            _noteLookup = new Dictionary<int, GenericNoteBinding>();
            foreach (var binding in noteBindings)
            {
                if (binding.noteNumber >= 0 && binding.noteNumber <= 127)
                {
                    _noteLookup[binding.noteNumber] = binding;
                    binding.ResolveTarget();
                }
            }

            _envelopeLookup = new Dictionary<int, EnvelopeBinding>();
            foreach (var binding in envelopeBindings)
            {
                if (binding.noteNumber >= 0 && binding.noteNumber <= 127)
                {
                    _envelopeLookup[binding.noteNumber] = binding;
                    binding.ResolveTarget();
                    binding.currentValue = binding.baseValue; // Initialize to base
                }
            }
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            // Resolve targets in editor for preview
            foreach (var binding in ccBindings)
            {
                binding?.ResolveTarget();
            }
            foreach (var binding in noteBindings)
            {
                binding?.ResolveTarget();
            }
            foreach (var binding in envelopeBindings)
            {
                binding?.ResolveTarget();
            }
        }
    #endif

        private void OnControlChange(MidiValueControl control, float value)
        {
            int cc = control.controlNumber;

            if (debugMode)
                Debug.Log($"[GenericMidi] CC#{cc} = {value:F2}");

            // Learn mode - assign CC to pending binding
            if (learnMode && learningCC && pendingBindingIndex >= 0 && pendingBindingIndex < ccBindings.Count)
            {
                ccBindings[pendingBindingIndex].ccNumber = cc;
                Debug.Log($"[GenericMidi] Learned: CC#{cc} -> binding index {pendingBindingIndex}");
                learnMode = false;
                pendingBindingIndex = -1;
                RebuildLookups();
                return;
            }

            // Normal routing
            if (_ccLookup != null && _ccLookup.TryGetValue(cc, out var binding) && binding.target != null)
            {
                float mappedValue = Mathf.Lerp(binding.remap.x, binding.remap.y, value);
                binding.SetCurrentValue(mappedValue);
                ApplyCCValue(binding, mappedValue);
            }
        }

        private void OnNoteOn(MidiNoteControl note, float velocity)
        {
            int noteNum = note.noteNumber;

            if (debugMode)
                Debug.Log($"[GenericMidi] Note ON: {noteNum}, Velocity: {velocity:F2}");

            // Learn mode - assign note to pending binding
            if (learnMode && !learningCC && pendingBindingIndex >= 0 && pendingBindingIndex < noteBindings.Count)
            {
                noteBindings[pendingBindingIndex].noteNumber = noteNum;
                Debug.Log($"[GenericMidi] Learned: Note {noteNum} -> binding index {pendingBindingIndex}");
                learnMode = false;
                pendingBindingIndex = -1;
                RebuildLookups();
                return;
            }

            // Normal routing - check note bindings
            if (_noteLookup != null && _noteLookup.TryGetValue(noteNum, out var binding) && binding.target != null)
            {
                // For notes, we can optionally use velocity as value
                float val = binding.useVelocity ? velocity : 1f;
                ApplyNoteValue(binding, val);
            }

            // Check envelope bindings - trigger spike on hit
            if (_envelopeLookup != null && _envelopeLookup.TryGetValue(noteNum, out var envBinding) && envBinding.target != null)
            {
                // Velocity can scale the peak if enabled
                float peak = envBinding.velocityScalesPeak
                    ? Mathf.Lerp(envBinding.baseValue, envBinding.peakValue, velocity)
                    : envBinding.peakValue;

                // Retrigger: jump to peak immediately
                envBinding.currentValue = peak;
                ApplyEnvelopeValue(envBinding, peak);
            }
        }

        private void ApplyCCValue(GenericCCBinding binding, float value)
        {
            switch (binding.target)
            {
                case VisualEffect vfx:
                    vfx.SetFloat(binding.propertyName, value);
                    break;

                case AnimatorSpeedController animSpeed:
                    animSpeed.SetSpeed(value);
                    break;

#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                case AnimatorManager animManager:
                    animManager.SetSpeed(value);
                    break;
#endif

                case VideoPlayer videoPlayer:
                    videoPlayer.playbackSpeed = value;
                    break;

#if SKOTE_DYNAMIC_TEMPORAL_BLUR
                case DynamicTemporalBlur blur:
                    blur.SetDefaultOpacity(value);
                    break;
#endif

#if SKOTE_VFX_TOOLKIT
                case VFXVector2Controller vfxVec2:
                    vfxVec2.SetProperty(binding.propertyName, value);
                    break;
#endif

#if SKOTE_VFX_TOOLKIT
                case VFXManager vfxManager:
                    vfxManager.SetProperty(binding.propertyName, value);
                    break;
#endif

                case CameraZController camZ:
                    camZ.SetZ(value);
                    break;

                case Volume volume:
                    ApplyVolumeProperty(volume, binding.propertyName, value);
                    break;

                case Rotator rotator:
                    rotator.SetSpeed(value);
                    break;

                default:
                    if (debugMode)
                        Debug.LogWarning($"[GenericMidi] Unsupported CC target type: {binding.target?.GetType().Name}");
                    break;
            }
        }

        private void ApplyNoteValue(GenericNoteBinding binding, float value)
        {
            switch (binding.target)
            {
                case VisualEffect vfx:
                    if (binding.useVelocity && !string.IsNullOrEmpty(binding.velocityProperty))
                        vfx.SetFloat(binding.velocityProperty, value);
                    vfx.SendEvent(binding.eventName);
                    break;

                case AnimatorSpeedController animSpeed:
                    animSpeed.TogglePause();
                    break;

#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                case AnimatorManager animManager:
                    if (!string.IsNullOrEmpty(binding.eventName))
                    {
                        if (!animManager.ProcessSDFAction(binding.eventName))
                            animManager.SendTrigger(binding.eventName);
                    }
                    else
                    {
                        animManager.TogglePause();
                    }
                    break;
#endif

#if SKOTE_VFX_TOOLKIT
                case VFXManager vfxManager:
                    if (!string.IsNullOrEmpty(binding.eventName))
                    {
                        if (!vfxManager.ProcessGroupAction(binding.eventName))
                            vfxManager.ToggleBool(binding.eventName);
                    }
                    break;
#endif

#if SKOTE_METAVIDO_EXTENSIONS
                case MetavidoManager metavido:
                    switch (binding.eventName?.ToLower())
                    {
                        case "play": metavido.Play(); break;
                        case "stop": metavido.Stop(); break;
                        case "next": metavido.NextClip(); break;
                        case "previous":
                        case "prev": metavido.PreviousClip(); break;
                        case "random": metavido.RandomClip(); break;
                    }
                    break;
#endif

                default:
                    if (debugMode)
                        Debug.LogWarning($"[GenericMidi] Unsupported Note target type: {binding.target?.GetType().Name}");
                    break;
            }
        }

        private void ApplyEnvelopeValue(EnvelopeBinding binding, float value)
        {
            switch (binding.target)
            {
                case VisualEffect vfx:
                    vfx.SetFloat(binding.propertyName, value);
                    break;

                case AnimatorSpeedController animSpeed:
                    animSpeed.SetSpeed(value);
                    break;

#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                case AnimatorManager animManager:
                    animManager.SetSpeed(value);
                    break;
#endif

                case VideoPlayer videoPlayer:
                    videoPlayer.playbackSpeed = value;
                    break;

#if SKOTE_DYNAMIC_TEMPORAL_BLUR
                case DynamicTemporalBlur blur:
                    blur.SetDefaultOpacity(value);
                    break;
#endif

#if SKOTE_VFX_TOOLKIT
                case VFXVector2Controller vfxVec2:
                    vfxVec2.SetProperty(binding.propertyName, value);
                    break;
#endif

#if SKOTE_VFX_TOOLKIT
                case VFXManager vfxManager:
                    vfxManager.SetProperty(binding.propertyName, value);
                    break;
#endif

                case CameraZController camZ:
                    camZ.SetZ(value);
                    break;

                case Volume volume:
                    ApplyVolumeProperty(volume, binding.propertyName, value);
                    break;

                case Rotator rotator:
                    rotator.SetSpeed(value);
                    break;

                default:
                    if (debugMode)
                        Debug.LogWarning($"[GenericMidi] Unsupported Envelope target type: {binding.target?.GetType().Name}");
                    break;
            }
        }

        private void ApplyVolumeProperty(Volume volume, string propertyName, float value)
        {
            if (volume.profile == null || string.IsNullOrEmpty(propertyName)) return;

            switch (propertyName.ToLower())
            {
                // ColorAdjustments
                case "postexposure":
                case "exposure":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca1))
                        ca1.postExposure.Override(value);
                    break;
                case "contrast":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca2))
                        ca2.contrast.Override(value);
                    break;
                case "saturation":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca3))
                        ca3.saturation.Override(value);
                    break;
                case "hueshift":
                    if (volume.profile.TryGet<ColorAdjustments>(out var ca4))
                        ca4.hueShift.Override(value);
                    break;

                // Bloom
                case "bloomintensity":
                    if (volume.profile.TryGet<Bloom>(out var bloom1))
                        bloom1.intensity.Override(value);
                    break;
                case "bloomthreshold":
                    if (volume.profile.TryGet<Bloom>(out var bloom2))
                        bloom2.threshold.Override(value);
                    break;
                case "bloomscatter":
                    if (volume.profile.TryGet<Bloom>(out var bloom3))
                        bloom3.scatter.Override(value);
                    break;

                // Vignette
                case "vignetteintensity":
                    if (volume.profile.TryGet<Vignette>(out var vig1))
                        vig1.intensity.Override(value);
                    break;
                case "vignettesmoothness":
                    if (volume.profile.TryGet<Vignette>(out var vig2))
                        vig2.smoothness.Override(value);
                    break;

                // ChromaticAberration
                case "chromaticaberration":
                case "chromatic":
                    if (volume.profile.TryGet<ChromaticAberration>(out var chroma))
                        chroma.intensity.Override(value);
                    break;

                // MotionBlur
                case "motionblur":
                    if (volume.profile.TryGet<MotionBlur>(out var mb))
                        mb.intensity.Override(value);
                    break;

                // FilmGrain
                case "filmgrain":
                case "grain":
                    if (volume.profile.TryGet<FilmGrain>(out var fg))
                        fg.intensity.Override(value);
                    break;

                // WhiteBalance
                case "temperature":
                    if (volume.profile.TryGet<WhiteBalance>(out var wb1))
                        wb1.temperature.Override(value);
                    break;
                case "tint":
                    if (volume.profile.TryGet<WhiteBalance>(out var wb2))
                        wb2.tint.Override(value);
                    break;

                // DepthOfField
                case "focusdistance":
                    if (volume.profile.TryGet<DepthOfField>(out var dof1))
                        dof1.focusDistance.Override(value);
                    break;
                case "aperture":
                    if (volume.profile.TryGet<DepthOfField>(out var dof2))
                        dof2.aperture.Override(value);
                    break;

                // LensDistortion
                case "lensdistortion":
                    if (volume.profile.TryGet<LensDistortion>(out var ld))
                        ld.intensity.Override(value);
                    break;

                default:
                    if (debugMode)
                        Debug.LogWarning($"[GenericMidi] Unknown Volume property: {propertyName}");
                    break;
            }
        }
    }

    /// <summary>
    /// Binding for continuous control (CC) messages - sliders, knobs, mod wheel, breath, etc.
    /// </summary>
    [System.Serializable]
    public class GenericCCBinding
    {
        [Tooltip("MIDI CC number (0-127). Common: 1=ModWheel, 7=Volume, 10=Pan, 11=Expression, 64=Sustain")]
        [Range(0, 127)]
        public int ccNumber;

        [Tooltip("Friendly name for this binding (for your reference)")]
        public string label;

        [Tooltip("The GameObject containing the target component")]
        public GameObject targetObject;

        [Tooltip("Which component type to control")]
        public KnobTargetType targetType = KnobTargetType.None;

        [HideInInspector] public Component target;

        [Tooltip("Property/parameter name on the target")]
        public string propertyName;

        [Tooltip("Remap range: X = output when CC is 0, Y = output when CC is 127")]
        public Vector2 remap = new Vector2(0f, 1f);

        [ReadOnly] [SerializeField] private float currentValue;

        public void SetCurrentValue(float value) => currentValue = value;

        public void ResolveTarget()
        {
            if (targetObject == null)
            {
                target = null;
                return;
            }

            target = targetType switch
            {
                KnobTargetType.VisualEffect => targetObject.GetComponent<VisualEffect>(),
                KnobTargetType.AnimatorSpeedController => targetObject.GetComponent<AnimatorSpeedController>(),
#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                KnobTargetType.AnimatorManager => targetObject.GetComponent<AnimatorManager>(),
#endif
                KnobTargetType.VideoPlayer => targetObject.GetComponent<VideoPlayer>(),
#if SKOTE_DYNAMIC_TEMPORAL_BLUR
                KnobTargetType.DynamicTemporalBlur => targetObject.GetComponent<DynamicTemporalBlur>(),
#endif
#if SKOTE_VFX_TOOLKIT
                KnobTargetType.VFXVector2Controller => targetObject.GetComponent<VFXVector2Controller>(),
#endif
#if SKOTE_VFX_TOOLKIT
                KnobTargetType.VFXManager => targetObject.GetComponent<VFXManager>(),
#endif
                KnobTargetType.CameraZController => targetObject.GetComponent<CameraZController>(),
                KnobTargetType.Volume => targetObject.GetComponent<Volume>(),
                KnobTargetType.Rotator => targetObject.GetComponent<Rotator>(),
                _ => null
            };
        }
    }

    /// <summary>
    /// Binding for note messages - keyboard keys, drum pads, etc.
    /// </summary>
    [System.Serializable]
    public class GenericNoteBinding
    {
        [Tooltip("MIDI note number (0-127). Middle C = 60")]
        [Range(0, 127)]
        public int noteNumber;

        [Tooltip("Friendly name for this binding (for your reference)")]
        public string label;

        [Tooltip("The GameObject containing the target component")]
        public GameObject targetObject;

        [Tooltip("Which component type to trigger")]
        public ButtonTargetType targetType = ButtonTargetType.None;

        [HideInInspector] public Component target;

        [Tooltip("Event name or action to trigger")]
        public string eventName;

        [Tooltip("If true, note velocity will be used as a value (0-1)")]
        public bool useVelocity = false;

        [Tooltip("Property to receive velocity value (for VisualEffect)")]
        public string velocityProperty;

        public void ResolveTarget()
        {
            if (targetObject == null)
            {
                target = null;
                return;
            }

            target = targetType switch
            {
                ButtonTargetType.VisualEffect => targetObject.GetComponent<VisualEffect>(),
                ButtonTargetType.AnimatorSpeedController => targetObject.GetComponent<AnimatorSpeedController>(),
#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                ButtonTargetType.AnimatorManager => targetObject.GetComponent<AnimatorManager>(),
#endif
#if SKOTE_METAVIDO_EXTENSIONS
                ButtonTargetType.MetavidoManager => targetObject.GetComponent<MetavidoManager>(),
#endif
#if SKOTE_VFX_TOOLKIT
                ButtonTargetType.VFXManager => targetObject.GetComponent<VFXManager>(),
#endif
                _ => null
            };
        }
    }

    /// <summary>
    /// Binding for envelope-driven notes - perfect for drums/percussion.
    /// On note hit: jumps to peak value, then decays smoothly to base value.
    /// </summary>
    [System.Serializable]
    public class EnvelopeBinding
    {
        [Tooltip("MIDI note number (0-127). Your kick is 54.")]
        [Range(0, 127)]
        public int noteNumber;

        [Tooltip("Friendly name for this binding (e.g., 'Kick', 'Snare')")]
        public string label;

        [Header("Envelope Shape")]
        [Tooltip("Base value when no trigger is active (e.g., 0.2 for slow speed)")]
        public float baseValue = 0.2f;

        [Tooltip("Peak value on hit (e.g., 1.0 for fast speed)")]
        public float peakValue = 1.0f;

        [Tooltip("Time in seconds to decay from peak to base")]
        public float decayTime = 0.3f;

        [Tooltip("If true, velocity scales the peak (soft hit = lower peak)")]
        public bool velocityScalesPeak = false;

        [Header("Target")]
        [Tooltip("The GameObject containing the target component")]
        public GameObject targetObject;

        [Tooltip("Which component type to control")]
        public KnobTargetType targetType = KnobTargetType.None;

        [HideInInspector] public Component target;

        [Tooltip("Property/parameter name on the target")]
        public string propertyName;

        [Header("Runtime (Read Only)")]
        [ReadOnly] public float currentValue;

        public void ResolveTarget()
        {
            if (targetObject == null)
            {
                target = null;
                return;
            }

            target = targetType switch
            {
                KnobTargetType.VisualEffect => targetObject.GetComponent<VisualEffect>(),
                KnobTargetType.AnimatorSpeedController => targetObject.GetComponent<AnimatorSpeedController>(),
#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                KnobTargetType.AnimatorManager => targetObject.GetComponent<AnimatorManager>(),
#endif
                KnobTargetType.VideoPlayer => targetObject.GetComponent<VideoPlayer>(),
#if SKOTE_DYNAMIC_TEMPORAL_BLUR
                KnobTargetType.DynamicTemporalBlur => targetObject.GetComponent<DynamicTemporalBlur>(),
#endif
#if SKOTE_VFX_TOOLKIT
                KnobTargetType.VFXVector2Controller => targetObject.GetComponent<VFXVector2Controller>(),
#endif
#if SKOTE_VFX_TOOLKIT
                KnobTargetType.VFXManager => targetObject.GetComponent<VFXManager>(),
#endif
                KnobTargetType.CameraZController => targetObject.GetComponent<CameraZController>(),
                KnobTargetType.Volume => targetObject.GetComponent<Volume>(),
                KnobTargetType.Rotator => targetObject.GetComponent<Rotator>(),
                _ => null
            };
        }
    }
}
