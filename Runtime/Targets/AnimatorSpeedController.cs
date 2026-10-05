using UnityEngine;

namespace Skote.Midi
{
    /// <summary>
    /// Controls the playback speed of an Animator.
    /// Attach to a GameObject with an Animator component, then reference this
    /// component from MidiInputRouter to control animation speed via MIDI.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AnimatorSpeedController : MonoBehaviour
    {
        [SerializeField] private float _defaultSpeed = 1f;
        [SerializeField] private float _minSpeed = 0f;
        [SerializeField] private float _maxSpeed = 2f;

        private Animator _animator;
        private float _currentSpeed;
        private bool _isPaused;
        private float _speedBeforePause;

        public float CurrentSpeed => _currentSpeed;
        public bool IsPaused => _isPaused;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _currentSpeed = _defaultSpeed;
            _animator.speed = _currentSpeed;
        }

        /// <summary>
        /// Set the animator speed directly (0-1 normalized input will be mapped to minSpeed-maxSpeed)
        /// </summary>
        public void SetSpeed(float normalizedValue)
        {
            if (_isPaused) return;

            _currentSpeed = Mathf.Lerp(_minSpeed, _maxSpeed, Mathf.Clamp01(normalizedValue));
            _animator.speed = _currentSpeed;
        }

        /// <summary>
        /// Set animator speed as an absolute value (not normalized)
        /// </summary>
        public void SetSpeedAbsolute(float speed)
        {
            if (_isPaused) return;

            _currentSpeed = Mathf.Clamp(speed, _minSpeed, _maxSpeed);
            _animator.speed = _currentSpeed;
        }

        /// <summary>
        /// Toggle pause state
        /// </summary>
        public void TogglePause()
        {
            if (_isPaused)
                Resume();
            else
                Pause();
        }

        /// <summary>
        /// Pause the animator
        /// </summary>
        public void Pause()
        {
            if (_isPaused) return;

            _speedBeforePause = _currentSpeed;
            _animator.speed = 0f;
            _isPaused = true;
        }

        /// <summary>
        /// Resume the animator at the speed before pause
        /// </summary>
        public void Resume()
        {
            if (!_isPaused) return;

            _currentSpeed = _speedBeforePause;
            _animator.speed = _currentSpeed;
            _isPaused = false;
        }

        /// <summary>
        /// Reset to default speed
        /// </summary>
        public void ResetSpeed()
        {
            _isPaused = false;
            _currentSpeed = _defaultSpeed;
            _animator.speed = _currentSpeed;
        }
    }
}
