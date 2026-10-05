using UnityEngine;

namespace Skote.Midi
{
    public class CameraZController : MonoBehaviour
    {
        public void SetZ(float z)
        {
            var pos = transform.localPosition;
            pos.z = z;
            transform.localPosition = pos;
        }
    }
}
