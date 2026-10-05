using UnityEngine;
using UnityEngine.InputSystem;
using Minis;

namespace Skote.Midi
{
    /// <summary>
    /// Simple MIDI input logger for testing Korg Nano Kontrol connectivity.
    /// Attach this to any GameObject in your scene to log all MIDI events.
    /// </summary>
    public class MidiInputLogger : MonoBehaviour
    {
        [Header("Logging Options")]
        [SerializeField] private bool logNoteOn = true;
        [SerializeField] private bool logNoteOff = true;
        [SerializeField] private bool logControlChange = true;

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;

            // Subscribe to any already-connected MIDI devices
            foreach (var device in InputSystem.devices)
            {
                if (device is MidiDevice midiDevice)
                {
                    SubscribeToDevice(midiDevice);
                }
            }
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is MidiDevice midiDevice)
            {
                if (change == InputDeviceChange.Added)
                {
                    Debug.Log($"[MIDI] Device connected: {midiDevice.description.product} (Channel {midiDevice.channel})");
                    SubscribeToDevice(midiDevice);
                }
                else if (change == InputDeviceChange.Removed)
                {
                    Debug.Log($"[MIDI] Device disconnected: {midiDevice.description.product}");
                }
            }
        }

        private void SubscribeToDevice(MidiDevice device)
        {
            device.onWillNoteOn += OnNoteOn;
            device.onWillNoteOff += OnNoteOff;
            device.onWillControlChange += OnControlChange;
        }

        private void OnNoteOn(MidiNoteControl note, float velocity)
        {
            if (logNoteOn)
            {
                Debug.Log($"[MIDI] Note ON - Note: {note.noteNumber}, Velocity: {velocity:F2}");
            }
        }

        private void OnNoteOff(MidiNoteControl note)
        {
            if (logNoteOff)
            {
                Debug.Log($"[MIDI] Note OFF - Note: {note.noteNumber}");
            }
        }

        private void OnControlChange(MidiValueControl control, float value)
        {
            if (logControlChange)
            {
                Debug.Log($"[MIDI] Control Change - CC#: {control.controlNumber}, Value: {value:F2}");
            }
        }
    }
}
