using UnityEngine;
using UnityEngine.InputSystem;
using Minis;
using System.Collections.Generic;

namespace Skote.Midi
{
    /// <summary>
    /// Simple MIDI loop sequencer. Records CC pad hits and loops them back
    /// through MidiMappingManager as if they were real MIDI input.
    ///
    /// Workflow:
    ///   1. Press the record CC button -> recording starts at next beat
    ///   2. Play pads (CC#33-41) during the loop window
    ///   3. Press the record CC button again -> recording stops, loop plays
    ///   4. Press again to clear and re-record
    ///
    /// The sequencer quantizes to a configurable grid and loops over a
    /// fixed number of bars at the given BPM.
    /// </summary>
    public class MidiSequencer : MonoBehaviour
    {
        [Header("Tempo")]
        [Tooltip("Beats per minute")]
        public float bpm = 125f;

        [Tooltip("Number of bars in the loop")]
        public int bars = 4;

        [Tooltip("Beats per bar (time signature numerator)")]
        public int beatsPerBar = 4;

        [Header("MIDI Config")]
        [Tooltip("CC number used as the record toggle button")]
        public int recordToggleCC = 42;

        [Tooltip("First pad CC number (inclusive)")]
        public int padCCMin = 33;

        [Tooltip("Last pad CC number (inclusive)")]
        public int padCCMax = 41;

        [Tooltip("CC value threshold to count as a hit (0-1 normalized). Pads typically send 1.0 on press.")]
        public float hitThreshold = 0.5f;

        [Header("Quantize")]
        [Tooltip("Snap recorded hits to nearest subdivision. 0 = no quantize, 1 = quarter, 2 = 8th, 4 = 16th")]
        public int quantizeSubdivision = 4;

        [Header("State (read-only)")]
        [SerializeField] private SequencerState state = SequencerState.Idle;
        [SerializeField] private int eventCount;

        public SequencerState State => state;
        public bool IsPlaying => state == SequencerState.Playing;
        public bool IsRecording => state == SequencerState.Recording;

        // Recorded events: time in beats (0 to totalBeats) + CC number + value
        private struct SeqEvent
        {
            public float beat;   // position in beats from loop start
            public int ccNumber;
            public float value;  // normalized 0-1
        }

        private List<SeqEvent> _events = new List<SeqEvent>();
        private string _recordedDeviceName;

        // Playback state
        private double _loopStartDspTime;
        private float _lastPlaybackBeat = -1f;

        // For subscribing to raw MIDI
        private HashSet<MidiDevice> _subscribedDevices = new HashSet<MidiDevice>();

        private float TotalBeats => bars * beatsPerBar;
        private float SecondsPerBeat => 60f / bpm;
        private float LoopDurationSeconds => TotalBeats * SecondsPerBeat;

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            foreach (var device in InputSystem.devices)
            {
                if (device is MidiDevice midi)
                    Subscribe(midi);
            }
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            foreach (var device in _subscribedDevices)
            {
                if (device != null)
                    device.onWillControlChange -= OnCC;
            }
            _subscribedDevices.Clear();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is MidiDevice midi)
            {
                if (change == InputDeviceChange.Added) Subscribe(midi);
                else if (change == InputDeviceChange.Removed) _subscribedDevices.Remove(midi);
            }
        }

        private void Subscribe(MidiDevice device)
        {
            if (_subscribedDevices.Contains(device)) return;
            device.onWillControlChange += OnCC;
            _subscribedDevices.Add(device);
        }

        private string GetDeviceName(MidiDevice device)
        {
            if (device == null) return "Unknown";
            return device.description.product ?? device.name ?? "Unknown";
        }

        private void OnCC(MidiValueControl control, float value)
        {
            int cc = control.controlNumber;
            string deviceName = GetDeviceName(control.device as MidiDevice);

            // Record toggle (only react to press, not release)
            if (cc == recordToggleCC && value >= hitThreshold)
            {
                ToggleRecord(deviceName);
                return;
            }

            // Pad input during recording
            if (state == SequencerState.Recording && cc >= padCCMin && cc <= padCCMax)
            {
                float currentBeat = GetCurrentBeat();
                float quantized = quantizeSubdivision > 0 ? Quantize(currentBeat) : currentBeat;

                _events.Add(new SeqEvent
                {
                    beat = quantized,
                    ccNumber = cc,
                    value = value
                });
                eventCount = _events.Count;

                Debug.Log($"[Sequencer] Recorded CC#{cc}={value:F2} at beat {quantized:F2}");
            }
        }

        private void ToggleRecord(string deviceName)
        {
            switch (state)
            {
                case SequencerState.Idle:
                    // Start recording
                    _events.Clear();
                    eventCount = 0;
                    _recordedDeviceName = deviceName;
                    _loopStartDspTime = AudioSettings.dspTime;
                    _lastPlaybackBeat = -1f;
                    state = SequencerState.Recording;
                    Debug.Log($"[Sequencer] Recording started ({bars} bars @ {bpm} BPM = {LoopDurationSeconds:F1}s)");
                    break;

                case SequencerState.Recording:
                    // Stop recording, start looping
                    state = SequencerState.Playing;
                    // Reset loop start to now so playback begins immediately
                    _loopStartDspTime = AudioSettings.dspTime;
                    _lastPlaybackBeat = -1f;
                    Debug.Log($"[Sequencer] Recording stopped. Looping {_events.Count} events.");
                    break;

                case SequencerState.Playing:
                    // Stop playback, go idle (ready to record again)
                    state = SequencerState.Idle;
                    _events.Clear();
                    eventCount = 0;
                    Debug.Log("[Sequencer] Stopped. Ready to record.");
                    break;
            }
        }

        private void Update()
        {
            if (state != SequencerState.Playing || _events.Count == 0) return;

            var manager = MidiMappingManager.Instance;
            if (manager == null) return;

            float currentBeat = GetCurrentBeat();

            // Fire events that fall between lastPlaybackBeat and currentBeat
            foreach (var evt in _events)
            {
                if (ShouldFire(evt.beat, _lastPlaybackBeat, currentBeat))
                {
                    manager.SimulateCC(_recordedDeviceName, evt.ccNumber, evt.value);
                }
            }

            _lastPlaybackBeat = currentBeat;
        }

        private float GetCurrentBeat()
        {
            double elapsed = AudioSettings.dspTime - _loopStartDspTime;
            float beatPos = (float)(elapsed / SecondsPerBeat);
            return beatPos % TotalBeats;
        }

        /// <summary>
        /// Check if an event at 'eventBeat' should fire given we moved from 'prevBeat' to 'curBeat'.
        /// Handles loop wrap-around.
        /// </summary>
        private bool ShouldFire(float eventBeat, float prevBeat, float curBeat)
        {
            if (prevBeat < 0) prevBeat = 0; // first frame

            if (curBeat >= prevBeat)
            {
                // Normal case: no wrap
                return eventBeat > prevBeat && eventBeat <= curBeat;
            }
            else
            {
                // Wrapped around the loop
                return eventBeat > prevBeat || eventBeat <= curBeat;
            }
        }

        private float Quantize(float beat)
        {
            float gridSize = 1f / quantizeSubdivision;
            return Mathf.Round(beat / gridSize) * gridSize;
        }

        /// <summary>
        /// Manually start/stop from code or UI if needed.
        /// </summary>
        public void StartRecording()
        {
            if (state != SequencerState.Idle) return;
            ToggleRecord(_recordedDeviceName ?? "Sequencer");
        }

        public void StopRecording()
        {
            if (state != SequencerState.Recording) return;
            ToggleRecord(_recordedDeviceName ?? "Sequencer");
        }

        public void Stop()
        {
            if (state != SequencerState.Playing) return;
            ToggleRecord(_recordedDeviceName ?? "Sequencer");
        }
    }

    public enum SequencerState
    {
        Idle,
        Recording,
        Playing
    }
}
