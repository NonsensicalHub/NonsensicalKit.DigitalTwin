using System;
using UnityEngine;

namespace NonsensicalKit.DigitalTwin.Warehouse
{
    internal sealed class WarehouseUpdateScheduler
    {
        private bool _hasPendingUpdate;
        private int _nextUpdateFrame;
        private int _dispatchedUpdateCount;

        public bool HasPendingUpdate => _hasPendingUpdate;

        public void ClearPending()
        {
            _hasPendingUpdate = false;
        }

        public void Request(int updateIntervalFrames, bool immediate, Action executeUpdate)
        {
            RequestAtFrame(Time.frameCount, updateIntervalFrames, immediate, executeUpdate);
        }

        internal void RequestAtFrame(int currentFrame, int updateIntervalFrames, bool immediate, Action executeUpdate)
        {
            if (executeUpdate == null)
            {
                return;
            }

            if (immediate || updateIntervalFrames <= 1)
            {
                executeUpdate.Invoke();
                return;
            }

            _hasPendingUpdate = true;
            int targetFrame = currentFrame + Mathf.Max(1, updateIntervalFrames) - 1;
            if (_nextUpdateFrame < currentFrame)
            {
                _nextUpdateFrame = targetFrame;
            }
        }

        public void TryExecuteScheduled(int updateIntervalFrames, Action executeUpdate)
        {
            TryExecuteScheduledAtFrame(Time.frameCount, updateIntervalFrames, executeUpdate);
        }

        internal void TryExecuteScheduledAtFrame(int currentFrame, int updateIntervalFrames, Action executeUpdate)
        {
            if (!_hasPendingUpdate)
            {
                return;
            }

            if (currentFrame < _nextUpdateFrame)
            {
                return;
            }

            if (executeUpdate == null)
            {
                _hasPendingUpdate = false;
                return;
            }

            executeUpdate.Invoke();
        }

        public void NotifyExecuted(int updateIntervalFrames)
        {
            NotifyExecutedAtFrame(Time.frameCount, updateIntervalFrames);
        }

        internal void NotifyExecutedAtFrame(int currentFrame, int updateIntervalFrames)
        {
            _hasPendingUpdate = false;
            _nextUpdateFrame = currentFrame + Mathf.Max(1, updateIntervalFrames);
            _dispatchedUpdateCount++;
        }

        public int ConsumeDispatchedCount()
        {
            int count = _dispatchedUpdateCount;
            _dispatchedUpdateCount = 0;
            return count;
        }
    }
}
