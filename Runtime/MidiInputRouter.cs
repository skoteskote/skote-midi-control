using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.VFX;
using UnityEngine.Video;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Minis;

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
    public class MidiInputRouter : MonoBehaviour
    {
        [Header("Sliders (Faders)")]
        public MidiKnobBinding Slider1;
        public MidiKnobBinding Slider2;
        public MidiKnobBinding Slider3;
        public MidiKnobBinding Slider4;
        public MidiKnobBinding Slider5;
        public MidiKnobBinding Slider6;
        public MidiKnobBinding Slider7;
        public MidiKnobBinding Slider8;
        public MidiKnobBinding Slider9;

        [Header("Knobs")]
        public MidiKnobBinding Knob1;
        public MidiKnobBinding Knob2;
        public MidiKnobBinding Knob3;
        public MidiKnobBinding Knob4;
        public MidiKnobBinding Knob5;
        public MidiKnobBinding Knob6;
        public MidiKnobBinding Knob7;
        public MidiKnobBinding Knob8;
        public MidiKnobBinding Knob9;

        [Header("Top Buttons")]
        public MidiButtonBinding ButtonTop1;
        public MidiButtonBinding ButtonTop2;
        public MidiButtonBinding ButtonTop3;
        public MidiButtonBinding ButtonTop4;
        public MidiButtonBinding ButtonTop5;
        public MidiButtonBinding ButtonTop6;
        public MidiButtonBinding ButtonTop7;
        public MidiButtonBinding ButtonTop8;

        [Header("Bottom Buttons")]
        public MidiButtonBinding ButtonBottom1;
        public MidiButtonBinding ButtonBottom2;
        public MidiButtonBinding ButtonBottom3;
        public MidiButtonBinding ButtonBottom4;
        public MidiButtonBinding ButtonBottom5;
        public MidiButtonBinding ButtonBottom6;
        public MidiButtonBinding ButtonBottom7;
        public MidiButtonBinding ButtonBottom8;

        [Header("Transport")]
        public MidiButtonBinding TransportPlay;
        public MidiButtonBinding TransportStop;
        public MidiButtonBinding TransportRecord;
        public MidiButtonBinding TransportRewind;
        public MidiButtonBinding TransportForward;
        public MidiButtonBinding TransportCycle;
        public MidiButtonBinding TransportTrackPrev;
        public MidiButtonBinding TransportTrackNext;

        private MidiKnobBinding[] _allKnobs;
        private MidiButtonBinding[] _allButtons;

        private void Awake()
        {
            // Set default CC numbers for Korg NanoKontrol2
            // These can be overridden in inspector if your device uses different mappings
            InitializeDefaultCCNumbers();

            // Cache all bindings for fast lookup
            _allKnobs = new[]
            {
                Slider1, Slider2, Slider3, Slider4, Slider5, Slider6, Slider7, Slider8, Slider9,
                Knob1, Knob2, Knob3, Knob4, Knob5, Knob6, Knob7, Knob8, Knob9
            };

            _allButtons = new[]
            {
                ButtonTop1, ButtonTop2, ButtonTop3, ButtonTop4, ButtonTop5, ButtonTop6, ButtonTop7, ButtonTop8,
                ButtonBottom1, ButtonBottom2, ButtonBottom3, ButtonBottom4, ButtonBottom5, ButtonBottom6, ButtonBottom7, ButtonBottom8,
                TransportPlay, TransportStop, TransportRecord, TransportRewind, TransportForward,
                TransportCycle, TransportTrackPrev, TransportTrackNext
            };

            // Resolve component targets for all knobs
            foreach (var knob in _allKnobs)
            {
                knob.ResolveTarget();
            }

            // Resolve component targets for all buttons
            foreach (var button in _allButtons)
            {
                button.ResolveTarget();
            }
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            // Resolve targets in editor for preview
            var knobs = new[]
            {
                Slider1, Slider2, Slider3, Slider4, Slider5, Slider6, Slider7, Slider8, Slider9,
                Knob1, Knob2, Knob3, Knob4, Knob5, Knob6, Knob7, Knob8, Knob9
            };

            foreach (var knob in knobs)
            {
                knob?.ResolveTarget();
            }

            var buttons = new[]
            {
                ButtonTop1, ButtonTop2, ButtonTop3, ButtonTop4, ButtonTop5, ButtonTop6, ButtonTop7, ButtonTop8,
                ButtonBottom1, ButtonBottom2, ButtonBottom3, ButtonBottom4, ButtonBottom5, ButtonBottom6, ButtonBottom7, ButtonBottom8,
                TransportPlay, TransportStop, TransportRecord, TransportRewind, TransportForward,
                TransportCycle, TransportTrackPrev, TransportTrackNext
            };

            foreach (var button in buttons)
            {
                button?.ResolveTarget();
            }
        }
    #endif

        private void InitializeDefaultCCNumbers()
        {
            // Sliders (Faders) 1-9: CC 2-8, then 12-13
            Slider1.ccNumber = 2; Slider2.ccNumber = 3; Slider3.ccNumber = 4; Slider4.ccNumber = 5;
            Slider5.ccNumber = 6; Slider6.ccNumber = 7; Slider7.ccNumber = 8;
            Slider8.ccNumber = 12; Slider9.ccNumber = 13;

            // Knobs 1-9: CC 14-22
            Knob1.ccNumber = 14; Knob2.ccNumber = 15; Knob3.ccNumber = 16; Knob4.ccNumber = 17;
            Knob5.ccNumber = 18; Knob6.ccNumber = 19; Knob7.ccNumber = 20; Knob8.ccNumber = 21;
            Knob9.ccNumber = 22;

            // Top buttons 1-8: CC 23-30
            ButtonTop1.ccNumber = 23; ButtonTop2.ccNumber = 24; ButtonTop3.ccNumber = 25; ButtonTop4.ccNumber = 26;
            ButtonTop5.ccNumber = 27; ButtonTop6.ccNumber = 28; ButtonTop7.ccNumber = 29; ButtonTop8.ccNumber = 30;

            // Bottom buttons 1-8: CC 33-40
            ButtonBottom1.ccNumber = 33; ButtonBottom2.ccNumber = 34; ButtonBottom3.ccNumber = 35; ButtonBottom4.ccNumber = 36;
            ButtonBottom5.ccNumber = 37; ButtonBottom6.ccNumber = 38; ButtonBottom7.ccNumber = 39; ButtonBottom8.ccNumber = 40;

            // Transport (offset +4 from standard)
            TransportPlay.ccNumber = 45;
            TransportStop.ccNumber = 46;
            TransportRewind.ccNumber = 47;
            TransportForward.ccNumber = 48;
            TransportRecord.ccNumber = 49;
            TransportCycle.ccNumber = 50;
            TransportTrackPrev.ccNumber = 62;
            TransportTrackNext.ccNumber = 63;
        }

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;

            foreach (var device in InputSystem.devices)
            {
                if (device is MidiDevice midiDevice)
                    SubscribeToDevice(midiDevice);
            }
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is MidiDevice midiDevice && change == InputDeviceChange.Added)
                SubscribeToDevice(midiDevice);
        }

        private void SubscribeToDevice(MidiDevice device)
        {
            device.onWillControlChange += OnControlChange;
        }

        private void OnControlChange(MidiValueControl control, float value)
        {
            int cc = control.controlNumber;

            // Check knobs/sliders (continuous controls)
            foreach (var knob in _allKnobs)
            {
                if (knob.ccNumber == cc)
                {
                    float mappedValue = Mathf.Lerp(knob.remap.x, knob.remap.y, value);
                    knob.SetCurrentValue(mappedValue);
                    if (knob.target != null)
                    {
                        ApplyKnobValue(knob, mappedValue);
                    }
                    return;
                }
            }

            // Check buttons (CC buttons send 127 on press, 0 on release, normalized to 0-1)
            foreach (var button in _allButtons)
            {
                if (button.ccNumber == cc && button.target != null)
                {
                    bool pressed = value > 0.5f;
                    ApplyButtonValue(button, pressed);
                    return;
                }
            }
        }

        private void ApplyKnobValue(MidiKnobBinding binding, float value)
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
                    Debug.LogWarning($"[MidiRouter] Unsupported target type: {binding.target.GetType().Name}");
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
                    Debug.LogWarning($"[MidiRouter] Unknown Volume property: {propertyName}");
                    break;
            }
        }

        private void ApplyButtonValue(MidiButtonBinding binding, bool pressed)
        {
            if (!pressed) return; // Only trigger on press, not release

            switch (binding.target)
            {
                case VisualEffect vfx:
                    // Toggle or set a bool/trigger
                    vfx.SendEvent(binding.propertyName);
                    break;

                case AnimatorSpeedController animSpeed:
                    // Could toggle play/pause or trigger something
                    animSpeed.TogglePause();
                    break;

#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                case AnimatorManager animManager:
                    if (!string.IsNullOrEmpty(binding.propertyName))
                    {
                        // Check if it's an SDF action (NextGroup or PrevGroup)
                        if (!animManager.ProcessSDFAction(binding.propertyName))
                        {
                            // Not an SDF action, treat as animator trigger
                            animManager.SendTrigger(binding.propertyName);
                        }
                    }
                    else
                    {
                        animManager.TogglePause();
                    }
                    break;
#endif

#if SKOTE_VFX_TOOLKIT
                case VFXManager vfxManager:
                    // propertyName can be a group action (e.g. "KillA", "ReviveB") or a bool property to toggle
                    if (!string.IsNullOrEmpty(binding.propertyName))
                    {
                        // Try group action first, if not handled, try toggling a bool
                        if (!vfxManager.ProcessGroupAction(binding.propertyName))
                        {
                            vfxManager.ToggleBool(binding.propertyName);
                        }
                    }
                    break;
#endif

#if SKOTE_METAVIDO_EXTENSIONS
                case MetavidoManager metavido:
                    switch (binding.propertyName?.ToLower())
                    {
                        case "play":
                            metavido.Play();
                            break;
                        case "next":
                            metavido.NextClip();
                            break;
                        case "previous":
                        case "prev":
                            metavido.PreviousClip();
                            break;
                        case "random":
                            metavido.RandomClip();
                            break;
                        case "stop":
                            metavido.Stop();
                            break;
                        default:
                            Debug.LogWarning($"[MidiRouter] Unknown MetavidoManager action: {binding.propertyName}");
                            break;
                    }
                    break;
#endif

                // Add more component types here as needed
                default:
                    Debug.LogWarning($"[MidiRouter] Unsupported target type: {binding.target.GetType().Name}");
                    break;
            }
        }
    }

    public enum KnobTargetType
    {
        None,
        VisualEffect,
        AnimatorSpeedController,
        AnimatorManager,
        VideoPlayer,
        DynamicTemporalBlur,
        VFXVector2Controller,
        VFXManager,
        CameraZController,
        Volume,
        Rotator
    }

    [System.Serializable]
    public class MidiKnobBinding
    {
        [HideInInspector] public int ccNumber;

        [Tooltip("The GameObject containing the target component")]
        public GameObject targetObject;

        [Tooltip("Which component type to control on the target object")]
        public KnobTargetType targetType = KnobTargetType.None;

        [HideInInspector] public Component target;

        [Tooltip("Property/parameter name on the target (for VisualEffect)")]
        public string propertyName;

        [Tooltip("Remap range: X = output when knob is at 0, Y = output when knob is at 1")]
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
                KnobTargetType.VisualEffect => targetObject.GetComponent<UnityEngine.VFX.VisualEffect>(),
                KnobTargetType.AnimatorSpeedController => targetObject.GetComponent<AnimatorSpeedController>(),
#if SKOTE_VFX_TOOLKIT && SKOTE_MESH_TO_SDF
                KnobTargetType.AnimatorManager => targetObject.GetComponent<AnimatorManager>(),
#endif
                KnobTargetType.VideoPlayer => targetObject.GetComponent<UnityEngine.Video.VideoPlayer>(),
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

    public enum ButtonTargetType
    {
        None,
        VisualEffect,
        AnimatorSpeedController,
        AnimatorManager,
        MetavidoManager,
        VFXManager
    }

    [System.Serializable]
    public class MidiButtonBinding
    {
        [HideInInspector] public int ccNumber;

        [Tooltip("The GameObject containing the target component")]
        public GameObject targetObject;

        [Tooltip("Which component type to control on the target object")]
        public ButtonTargetType targetType = ButtonTargetType.None;

        [HideInInspector] public Component target;

        [Tooltip("Event name or property to trigger on the target")]
        public string propertyName;

        public void ResolveTarget()
        {
            if (targetObject == null)
            {
                target = null;
                return;
            }

            target = targetType switch
            {
                ButtonTargetType.VisualEffect => targetObject.GetComponent<UnityEngine.VFX.VisualEffect>(),
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
}
