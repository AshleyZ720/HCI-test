// #define OVR_TIME_SLICER_CHECK_SLICE_TIME

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using UnityEngine;

using Stopwatch = System.Diagnostics.Stopwatch;

using OvrTimeAssertDelegate
    = Oculus.Avatar2.OvrAvatarLog.AssertMessageBuilder<System.Collections.Generic.IEnumerator<Oculus.Avatar2.OvrTime.SliceStep>>;

#if UNITY_EDITOR
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("AvatarSDK.PlayModeTests")]
#endif

namespace Oculus.Avatar2
{
    // This class is 1000% *NOT* threadsafe aside from post methods.
    // When it doubt, post it out. (PostAllocToUnityMainThread or PostCleanupToUnityMainThread)

    public static class SliceExtensions
    {
        // Invalidate target SliceHandle instance
        internal static void Clear(this ref OvrTime.SliceHandle handle)
        {
            handle = default;
        }

        // Cancel this `SliceHandle` - preventing any future work being performed from it.
        // Must be called from `Unity.MainThread`, must not be called while slicing
        internal static bool Cancel(this ref OvrTime.SliceHandle handle)
        {
            Debug.Assert(handle.IsValid);
            // This is marked obsolete to discourage direct calls, since we can't scope it properly
#pragma warning disable CS0618 // Type or member is obsolete
            bool didCancel = handle._Stop();
#pragma warning restore CS0618 // Type or member is obsolete

            handle.Clear();

            return didCancel;
        }

        // Safe to call from anythread (usually, Finalizer thread) to prevent OvrTime stall - implicitly logs an error
        // Do not call under expected conditions, should almost never be called from Unity.Main thread
        // NOTE: This method indicates failure is expected, it will likely never be 100% bulletproof
        internal static void EmergencyShutdown(this ref OvrTime.SliceHandle handle)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            handle.__EmergencyShutdownImpl();
#pragma warning restore CS0618 // Type or member is obsolete
            handle.Clear();
        }
    }

    // A manager class that coroutines can use to time slice their work.
    // Handles nested calls to Slice and multiple timesliced functions waiting to run at once
    // TODO: Investigate a final solution to async loading. Investigate threading/Tasks (has issues with Unity though)
    // TODO: Analyze GC alloc of coroutines vs tasks and how it can be optimized in either case
    // TODO: Allow slices to wait on other SliceHandles - reduce number of redundant status checks
    // TODO: List is awful for the queue, tree? (sooner/later/dependent)
    // TODO: Better delay logic - slowest enumerators should move to end of queue and stay there
    // -> Track "delay region" and insert new delays earlier - repeated delays move to end of region
    // ^ Hmmm a tree would do that nicely...
    internal static class OvrTime
    {
        private const string logScope = "OvrTime";

        // TODO: The split between static and local members isn't very fun
        // - but I'm not sure how else to make this reasonably runtime configurable via Unity editor?
        // NOTE: Could do a "copyOver" at the start of UpdateInternal or some such, but I'd prefer to support mid-slice budget changes
        // TODO: Put more thought into what the correct amount here should be
        // Minimum amount of work to run per frame (in milliseconds) while sliced work remains
        private static uint _minWorkPerFrameMS = uint.MaxValue;

        internal static UInt16 minWorkPerFrameMS
        {
            get => _minWorkPerFrameMS <= UInt16.MaxValue ? (UInt16)_minWorkPerFrameMS : UInt16.MaxValue;
            set => _minWorkPerFrameMS = value;
        }

        // `uint.MaxValue` can only be set from static ctor, external API only accepts UInt16
        internal static bool HasLimitedBudget => _minWorkPerFrameMS < uint.MaxValue;
        internal static void ResetInitialBudget() { _minWorkPerFrameMS = uint.MaxValue; }

        // TODO: "Bleed-over" time? If an update goes long, shorten the duration of the next update?

        private static readonly Stopwatch _watch = new Stopwatch();
#if OVR_TIME_SLICER_CHECK_SLICE_TIME
        private readonly Stopwatch _sliceCheck = new Stopwatch();
#endif

        // TODO: Switch to expandable ring buffer
        /// <summary>
        /// Sliced IEnumerators which are potentially eligible for performing work
        /// </summary>
        private static readonly List<IEnumerator<SliceStep>> _slicerQueue = new List<IEnumerator<SliceStep>>();
        /// <summary>
        /// Cleanup actions are prioritized before alloc actions - under the assumption they will reduce resource pressure
        /// </summary>
        private static readonly ConcurrentQueue<Action> _postedCleanupActions = new ConcurrentQueue<Action>();
        /// <summary>
        /// Posted actions which will increase resource pressure.
        /// </summary>
        private static readonly ConcurrentQueue<Action> _postedAllocActions = new ConcurrentQueue<Action>();

        private static bool _isSlicing = false;

        public enum SliceStep
        {
            /* Continue working on this Slicer, for deferring step logic to helper functions
            * NOTE: When continuing work after checking CanContinue/ShouldHold, prefer to avoid `yield return` entirely */
            Continue = 0,
            /* End slicer updates this frame, used when budget overrun has been detected */
            Hold = 1,
            /* Defer time to next Slicer, but do not change queue order - ie: "frame delay" */
            Wait = 2,
            /* Stop working on this slicer, move it to end of queue, "indefinite delay" */
            Delay = 3,
            /* The next operation is expected to be slow - run it on a frame by itself
             NOTE: Exact behavior is undefined, but this should be avoided if unnecessary */
            Stall = 4,
            /* Cancel this slicer, equivalent to `yield break`, for deferring step logic to helper functions
             NOTE: `yield break` may be preferrable to returning `Cancel`*/
            Cancel = 5,
        }

        public readonly struct SliceHandle
        {
            public bool IsValid => _enumerator != null;

            // Use with caution, this is usual a code smell that something is setup incorrectly
            // But, if you need to quickly get something fixed up - this method is reliable (but slow).
            public bool IsSlicing => IsValid && OvrTime.IsEnumeratorSlicing(_enumerator);

            // NOTE: Implementation for `EmergencyShutdown` extension method, use the extension method instead -
            // as it will perform automated cleanup which otherwise needs to be done manually
            [Obsolete("Call `EmergencyShutdown` instead which will auto-invalidate the handle")]
            internal void __EmergencyShutdownImpl()
            {
                // TODO: Confirm finalizer thread? Non-main thread?
                // TODO: Attempt to stop current slice? Block until not slicing and reevaluate?
                OvrAvatarLog.LogError($"EmergencyShutdown activated for enumerator {_enumerator}", logScope);
                Debug.Assert(IsValid);
                var self = this;
                PostCleanupToUnityMainThread(() => self.Cancel());
            }

            internal bool WasCancelled()
            {
                Debug.Assert(IsValid);
                return OvrTime.IsEnumeratorQueued(_enumerator);
            }

            [Obsolete("Call `Cancel` instead which will auto-invalidate the handle")]
            internal bool _Stop()
            {
                Debug.Assert(IsValid);
                return StopEnumerator(_enumerator);
            }

            internal static SliceHandle GenerateHandle(IEnumerator<SliceStep> enumerator, object queueContext)
            {
                Debug.Assert(queueContext == _slicerQueue);
                return new SliceHandle(enumerator);
            }
            private SliceHandle(IEnumerator<SliceStep> enumerator) => _enumerator = enumerator;
            private readonly IEnumerator<SliceStep> _enumerator;
        }

        #region Threadsafe Methods
        public static void PostToUnityMainThread(Action action) => PostAllocToUnityMainThread(action);
        public static void PostAllocToUnityMainThread(Action action)
        {
            _postedAllocActions.Enqueue(action);
        }
        public static void PostCleanupToUnityMainThread(Action action)
        {
            _postedCleanupActions.Enqueue(action);
        }
        #endregion // Threadsafe Methods


        //:: Public Helpers
        #region Public Helper Methods
        // Evaluate this action now, shorting budget against the next update
        public static void Rush(Action timedOp)
        {
            Debug.Assert(timedOp != null);
            RunNow(timedOp);
        }

        // Run slicer when time permits - will be run over multiple frames
        public static SliceHandle Slice(IEnumerator<SliceStep> slicer)
        {
            Debug.Assert(slicer != null);
            Debug.Assert(!_slicerQueue.Contains(slicer));

            _slicerQueue.Add(slicer);

            return SliceHandle.GenerateHandle(slicer, _slicerQueue);
        }

        // Checks if work should Hold (stop for this frame)
        // This should only be called from sliced tasks
        public static bool ShouldHold
        {
            get
            {
                // ShouldHold must only be used from w/in Sliced methods
                // this indicates invocation from an unexpected location (attempt at sync load?)
                Debug.Assert(_isSlicing);
                return !HasFrameBudget;
            }
        }

        internal static bool HasWork
            => _slicerQueue.Count > 0 || !_postedAllocActions.IsEmpty || !_postedCleanupActions.IsEmpty;

        // NOTE: This should only be called as a failsafe after all Slices are expected to be cancelled, ie: shutdown
        internal static void CancelAll()
        {
            // If you hit this assert, you likely want to call `FinishAll()` instead
            OvrAvatarLog.AssertStaticBuilder(_slicerQueue.Count == 0, CachedDebugSlicerQueueBuilder, logScope);
            Debug.Assert(!HasWork);

            DrainActionQueue();
            _slicerQueue.Clear();
        }
        // NOTE: This should only be called as a failsafe after all Slices are expected to be cancelled, ie: shutdown
        // Unlike `CancelAll` - this method will block until the queued work is finished.
        // WARNING: THIS METHOD MAY BLOCK FOR HUNDREDS OF MILLISECONDS AND IS LIKELY TO PUT THE MAIN THREAD TO SLEEP!!!
        internal static void FinishAll()
        {
            // If we have no work, we're already finished
            if (!HasWork) { return; }

            // Drain queues first, these are pending results from worker threads
            DrainActionQueue(true);

            // Capture initial budget, as we will modify it
            var initialBudget = _minWorkPerFrameMS;

            uint updateAttempts = 0;
            // Continue simulating progressively longer "frames" until the work queue is empty
            do {
                _minWorkPerFrameMS = updateAttempts;
                InternalUpdate();

                // Sleep progressively longer to give worker threads a chance to do work on this core
                if (updateAttempts > 0)
                {
                    Thread.Sleep(new TimeSpan(updateAttempts - 1));
                }

                // If we hit uint.MaxValue... just overflow back to 0 instead of throwing an exception
                unchecked { updateAttempts++; }
            } while (HasWork);

            // Restore budget
            _minWorkPerFrameMS = initialBudget;

            // Ensure we successfully cleared all work - this should be unnecessary...
            // but if someone is calling this method, things aren't going smoothly :)
            CancelAll();
        }

        private static void DrainActionQueue(bool runAllocActions = false)
        {
            while (_postedCleanupActions.TryDequeue(out var cleanupAction))
            {
                cleanupAction();
            }
            while (_postedAllocActions.TryDequeue(out var allocAction))
            {
                if (runAllocActions)
                {
                    allocAction();
                }
            }
        }

        #endregion // Public Helper Methods

        //:: Private Helpers
        #region Private Helper Methods
        private static bool StopEnumerator(IEnumerator<SliceStep> enumerator)
        {
            // Stopping Slicer is invalid during slice, return Cancel instead
            OvrAvatarLog.AssertParam(!_isSlicing, in enumerator, CachedStopEnumeratorSliceAssertBuilder, logScope);
            bool didRemove = _slicerQueue.Remove(enumerator);
            OvrAvatarLog.AssertParam(didRemove, in enumerator, CachedCancelNotFoundAssertBuilder, logScope);
            return didRemove;
        }
        private static bool IsEnumeratorQueued(IEnumerator<SliceStep> enumerator)
        {
            // Checking queue status during slice only leads to other bad habits
            OvrAvatarLog.AssertParam(!_isSlicing, in enumerator, CachedIsQueuedSliceAssertBuilder, logScope);
            return IsEnumeratorSlicing(enumerator);
        }

        private static bool IsEnumeratorSlicing(IEnumerator<SliceStep> enumerator) => _slicerQueue.Contains(enumerator);

        private static bool HasFrameBudget => _watch.ElapsedMilliseconds < (Int64)_minWorkPerFrameMS;

        private static void StartNewFrame()
        {
            _watch.Reset();
        }

        private static void RunNow(Action op)
        {
            Debug.Assert(!_isSlicing);
            _watch.Start();
            op();
            _watch.Stop();
        }

        // Check for and resolve posted actions in Update,
        // also, handle scene change edge cases to ensure no tasks are dropped
        internal static void InternalUpdate()
        {
            if (HasFrameBudget)
            {
                _watch.Start();
                RunWork();
                _watch.Stop();
            }
            StartNewFrame();
        }

        private static void RunWork()
        {
            // Cleanup actions should reduce memory usage
            while (_postedCleanupActions.TryDequeue(out var cleanupAction))
            {
                cleanupAction.Invoke();
                if (!HasFrameBudget) { return; }
            }

            _isSlicing = true;
            bool hasBudget = RunSlices();
            _isSlicing = false;

            if (hasBudget)
            {
                // Alloc actions will increase memory usage and spawn additional slices
                while (_postedAllocActions.TryDequeue(out var allocAction))
                {
                    allocAction.Invoke();
                    if (!HasFrameBudget) { return; }
                }
            }
        }

        // Run sliced work
        private static bool RunSlices()
        {
            var currentIndex = 0;
            var stopIndex = _slicerQueue.Count;
            // Process existing slices
            while (currentIndex < stopIndex)
            {
                Debug.Assert(stopIndex <= _slicerQueue.Count);
                if (!CutSlice(ref currentIndex, ref stopIndex) || !HasFrameBudget) { return false; }
            }
            return true;
        }

        // Coroutine host which actually executes slicing
        private static bool CutSlice(ref int index, ref int stopIndex)
        {
            bool continueWorking = true;
            var sliceTask = _slicerQueue[index];

#if OVR_TIME_SLICER_CHECK_SLICE_TIME
        _sliceCheck.Reset();
        _sliceCheck.Start();
#endif
            var step = sliceTask.MoveNext() ? sliceTask.Current : SliceStep.Cancel;
#if OVR_TIME_SLICER_CHECK_SLICE_TIME
        _sliceCheck.Stop();
        if (_sliceCheck.ElapsedMilliseconds > _minWorkPerFrameMS)
        {
            OvrAvatarLog.LogWarning($"Slice from {sliceTask} was overbudget ({_sliceCheck.ElapsedMilliseconds} > {_minWorkPerFrameMS})", logScope, this);

            if (step != SliceStep.Cancel)
            {
                // DEBUG: Run next slice to step into and see where the task is
                step = sliceTask.MoveNext() ? sliceTask.Current : SliceStep.Cancel;
            }
        }
#endif

            // Queue order should not change during sliced work
            Debug.Assert(_slicerQueue[index] == sliceTask && _slicerQueue.IndexOf(sliceTask) == index);
            switch (step)
            {
                case SliceStep.Continue:
                    {
                        // Continue working on this slice - generally this is discouraged but handy when deferring to helper methods
                    }
                    break;

                case SliceStep.Hold:
                    {
                        // Stop processing slices this frame disregarding budget - slice has detected we are out of budget
                        continueWorking = false;
                    }
                    break;

                case SliceStep.Wait:
                    {
                        // Continue executing additional slices, time permitting
                        index++;
                    }
                    break;

                case SliceStep.Delay:
                    {
                        // Undefined delay - move to end of queue, stop working if last task
                        stopIndex--;
                        _slicerQueue.RemoveAt(index);
                        _slicerQueue.Add(sliceTask);
                    }
                    break;

                case SliceStep.Stall:
                    {
                        // Hold slicing, move to front of queue to be first next frame presumably filling it
                        continueWorking = false;
                        // Shift to front of queue
                        var shifter = sliceTask;
                        for (int idx = 0; idx <= index; ++idx)
                        {
                            // ReSharper disable once SwapViaDeconstruction
                            var nextShifter = _slicerQueue[idx];
                            _slicerQueue[idx] = shifter;
                            shifter = nextShifter;
                        }
                    }
                    break;

                case SliceStep.Cancel:
                    {
                        stopIndex--;
                        _slicerQueue.RemoveAt(index);
                    }
                    break;

                default:
                    throw new ArgumentOutOfRangeException($"OvrTime.SliceStep value {step} unexpected");
            }
            return continueWorking;
        }
        #endregion // Private Helper Methods

        #region Log Builders

        internal static string Debug_SlicerQueue()
        {
            StringBuilder strBuilder = new StringBuilder(_slicerQueue.Count * 32);
            string sep = string.Empty;
            foreach (var slicer in _slicerQueue)
            {
                strBuilder.Append(sep);
                strBuilder.Append(slicer);
                sep = ", ";
            }
            return strBuilder.ToString();
        }

        private static string _StopEnumeratorSliceAssertBuilder(in IEnumerator<SliceStep> enumerator)
            => $"StopEnumerator {enumerator} while slicing";
        private static string _CancelNotFoundAssertBuilder(in IEnumerator<SliceStep> enumerator)
            => $"Cancelled slicer {enumerator} which is not running";
        private static string _IsQueuedSliceAssertBuilder(in IEnumerator<SliceStep> enumerator)
            => $"IsEnumeratorQueued {enumerator} called while slicing";

        private static OvrAvatarLog.AssertStaticMessageBuilder _debugSlicerQueueBuilderCache = null;

        private static OvrAvatarLog.AssertStaticMessageBuilder CachedDebugSlicerQueueBuilder
            => _debugSlicerQueueBuilderCache ??= Debug_SlicerQueue;


        private static OvrTimeAssertDelegate _stopEnumeratorSliceAssertBuilderCache = null;
        private static OvrTimeAssertDelegate CachedStopEnumeratorSliceAssertBuilder
            => _stopEnumeratorSliceAssertBuilderCache ??= _StopEnumeratorSliceAssertBuilder;

        private static OvrTimeAssertDelegate _cancelNotFoundAssertBuilderCache = null;
        private static OvrTimeAssertDelegate CachedCancelNotFoundAssertBuilder
            => _cancelNotFoundAssertBuilderCache ??= _CancelNotFoundAssertBuilder;

        private static OvrTimeAssertDelegate _isQueuedSliceAssertBuilderCache = null;
        private static OvrTimeAssertDelegate CachedIsQueuedSliceAssertBuilder
            => _isQueuedSliceAssertBuilderCache ??= _IsQueuedSliceAssertBuilder;

        #endregion // Log Builders
    }
}
