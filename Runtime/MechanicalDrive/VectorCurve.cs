using UnityEngine;

namespace NonsensicalKit.DigitalTwin.MechanicalDrive
{
    public struct VectorKeyframe
    {
        public float Time;
        public Vector3 Value;

        public VectorKeyframe(float time, Vector3 value)
        {
            this.Time = time;
            this.Value = value;
        }
    }

    public class VectorAnimationCurve
    {
        #region Property and Field
        public VectorKeyframe this[int index] => new(_xCurve[index].time, new Vector3(_xCurve[index].value, _yCurve[index].value, _zCurve[index].value));

        /// <summary>
        /// Keyframe count.
        /// </summary>
        public int Length => _xCurve.length;

        /// <summary>
        /// The behaviour of the animation after the last keyframe.
        /// </summary>
        public WrapMode PostWrapMode
        {
            set => _xCurve.postWrapMode = _yCurve.postWrapMode = _zCurve.postWrapMode = value;
            get => _xCurve.postWrapMode;
        }

        /// <summary>
        /// The behaviour of the animation before the first keyframe.
        /// </summary>
        public WrapMode PreWrapMode
        {
            set => _xCurve.preWrapMode = _yCurve.preWrapMode = _zCurve.preWrapMode = value;
            get => _xCurve.preWrapMode;
        }

        private readonly AnimationCurve _xCurve = new();
        private readonly AnimationCurve _yCurve = new();
        private readonly AnimationCurve _zCurve = new();
        #endregion

        #region Public Method

        /// <summary>
        /// Add a new key to the curve.
        /// </summary>
        /// <param name="key">The key to add to the curve.</param>
        /// <returns>The index of the added key, or -1 if the key could not be added.</returns>
        public int AddKey(VectorKeyframe key)
        {
            _xCurve.AddKey(key.Time, key.Value.x);
            _yCurve.AddKey(key.Time, key.Value.y);
            return _zCurve.AddKey(key.Time, key.Value.z);
        }

        /// <summary>
        /// Add a new key to the curve.
        /// </summary>
        /// <param name="time">The time at which to add the key (horizontal axis in the curve graph).</param>
        /// <param name="value">The value for the key (vertical axis in the curve graph).</param>
        /// <returns>The index of the added key, or -1 if the key could not be added.</returns>
        public int AddKey(float time, Vector3 value)
        {
            _xCurve.AddKey(time, value.x);
            _yCurve.AddKey(time, value.y);
            return _zCurve.AddKey(time, value.z);
        }

        /// <summary>
        /// Evaluate the curve at time.
        /// </summary>
        /// <param name="time">The time within the curve you want to evaluate (the horizontal axis in the curve graph).</param>
        /// <returns>The value of the curve, at the point in time specified.</returns>
        public Vector3 Evaluate(float time)
        {
            return new Vector3(_xCurve.Evaluate(time), _yCurve.Evaluate(time), _zCurve.Evaluate(time));
        }

        /// <summary>
        /// Removes a key.
        /// </summary>
        /// <param name="index">The index of the key to remove.</param>
        public void RemoveKey(int index)
        {
            _xCurve.RemoveKey(index);
            _yCurve.RemoveKey(index);
            _zCurve.RemoveKey(index);
        }

        /// <summary>
        /// Smooth the in and out tangents of the keyframe at index.
        /// </summary>
        /// <param name="index">The index of the keyframe to be smoothed.</param>
        /// <param name="weight">The smoothing weight to apply to the keyframe's tangents.</param>
        public void SmoothTangents(int index, float weight)
        {
            _xCurve.SmoothTangents(index, weight);
            _yCurve.SmoothTangents(index, weight);
            _zCurve.SmoothTangents(index, weight);
        }

        /// <summary>
        /// Smooth the in and out tangents of keyframes.
        /// </summary>
        /// <param name="weight">The smoothing weight to apply to the keyframe's tangents.</param>
        public void SmoothTangents(float weight)
        {
            for (int i = 0; i < Length; i++)
            {
                SmoothTangents(i, weight);
            }
        }
        #endregion
    }
}
