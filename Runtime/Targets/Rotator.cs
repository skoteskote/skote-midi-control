using UnityEngine;

namespace Skote.Midi
{
    public class Rotator : MonoBehaviour
    {
        [SerializeField] private Vector3 axis = Vector3.up;
        [SerializeField] private float speed = 30f;

        public float Speed
        {
            get => speed;
            set => speed = value;
        }

        public void SetSpeed(float value)
        {
            speed = value;
        }

        private void Update()
        {
            transform.Rotate(axis * speed * Time.deltaTime);
        }
    }
}
