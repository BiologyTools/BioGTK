// ============================================================================
// PERFORMANCE OPTIMIZATION FOR PYRAMIDAL IMAGE PANNING
// ============================================================================
// This file contains the architectural changes needed to fix pan performance
// issues when viewing pyramidal images in fullscreen mode.
//
// KEY ISSUES ADDRESSED:
// 1. Synchronous tile fetching during pan events
// 2. Redundant UpdateBuffersPyramidal() calls
// 3. Lack of render throttling during high-frequency events
//
// SOLUTION APPROACH:
// 1. Deferred rendering - delay tile updates during active pan
// 2. Progressive loading - show last frame while panning
// 3. Debounced updates - batch tile fetches after pan completes
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using AForge;
namespace BioGTK
{
    /// <summary>
    /// Manages deferred rendering for pyramidal images during high-frequency
    /// user interactions like panning and zooming.
    /// </summary>
    public class PyramidalRenderManager
    {
        #region State Management
        
        private bool isInteracting = false;
        private DateTime lastInteractionTime;
        private Timer debounceTimer;
        private CancellationTokenSource currentFetchCancellation;
        
        // Configuration
        private const int DEBOUNCE_DELAY_MS = 150;  // Wait 150ms after last interaction
        private const int MAX_INTERACTION_TIME_MS = 5000;  // Force refresh after 5 seconds
        
        // Cached state for progressive rendering
        private byte[] lastRenderedFrame;
        private PointD lastOrigin;
        private double lastResolution;
        
        #endregion

        #region Interaction State Control
        
        /// <summary>
        /// Call when user begins an interaction (pan/zoom start)
        /// </summary>
        public void BeginInteraction()
        {
            isInteracting = true;
            lastInteractionTime = DateTime.Now;
            
            // Cancel any pending tile fetches
            CancelPendingFetch();
            
            // Stop the debounce timer
            debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        
        /// <summary>
        /// Call during interaction (pan/zoom move)
        /// </summary>
        public void ContinueInteraction()
        {
            lastInteractionTime = DateTime.Now;
            
            // Check if we've been interacting too long - force a refresh
            var interactionDuration = (DateTime.Now - lastInteractionTime).TotalMilliseconds;
            if (interactionDuration > MAX_INTERACTION_TIME_MS)
            {
                // Force a full tile fetch even during interaction
                EndInteraction();
            }
        }
        
        /// <summary>
        /// Call when user ends an interaction (pan/zoom complete)
        /// </summary>
        public void EndInteraction()
        {
            isInteracting = false;
            
            // Start debounce timer for final update
            if (debounceTimer == null)
            {
                debounceTimer = new Timer(OnDebounceTimerElapsed, null, 
                    DEBOUNCE_DELAY_MS, Timeout.Infinite);
            }
            else
            {
                debounceTimer.Change(DEBOUNCE_DELAY_MS, Timeout.Infinite);
            }
        }
        
        /// <summary>
        /// Returns true if currently in an interactive state where tile
        /// fetching should be deferred
        /// </summary>
        public bool ShouldDeferTileFetch()
        {
            return isInteracting;
        }
        
        #endregion

        #region Async Tile Fetching
        
        /// <summary>
        /// Performs async tile fetch with cancellation support
        /// </summary>
        public async Task<bool> FetchTilesAsync(BioImage image, Action onComplete)
        {
            // Cancel any existing fetch
            CancelPendingFetch();
            
            // Create new cancellation token
            currentFetchCancellation = new CancellationTokenSource();
            var token = currentFetchCancellation.Token;
            
            try
            {
                // Perform async tile update
                await Task.Run(async () =>
                {
                    if (token.IsCancellationRequested) return;
                    
                    await image.UpdateBuffersPyramidal();
                    
                }, token);
                
                // Invoke completion callback on UI thread
                if (!token.IsCancellationRequested)
                {
                    onComplete?.Invoke();
                    return true;
                }
                
                return false;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching tiles: {ex.Message}");
                return false;
            }
        }
        
        private void CancelPendingFetch()
        {
            if (currentFetchCancellation != null && !currentFetchCancellation.IsCancellationRequested)
            {
                currentFetchCancellation.Cancel();
                currentFetchCancellation.Dispose();
                currentFetchCancellation = null;
            }
        }
        
        #endregion

        #region Debounced Updates
        
        private void OnDebounceTimerElapsed(object state)
        {
            // Trigger final tile fetch
            // This will be called from ImageView via a callback
            OnFinalUpdateNeeded?.Invoke();
        }
        
        /// <summary>
        /// Event fired when debounce period completes and tiles should be fetched
        /// </summary>
        public event Action OnFinalUpdateNeeded;
        
        #endregion

        #region Frame Caching
        
        /// <summary>
        /// Cache the current rendered frame for progressive rendering
        /// </summary>
        public void CacheCurrentFrame(byte[] frameData, PointD origin, double resolution)
        {
            lastRenderedFrame = frameData;
            lastOrigin = origin;
            lastResolution = resolution;
        }
        
        /// <summary>
        /// Get cached frame adjusted for current viewport if available
        /// </summary>
        public byte[] GetCachedFrame(PointD currentOrigin, double currentResolution)
        {
            // For now, return cached frame as-is
            // Future enhancement: transform cached frame based on origin delta
            return lastRenderedFrame;
        }
        
        #endregion
        
        #region Cleanup
        
        public void Dispose()
        {
            debounceTimer?.Dispose();
            CancelPendingFetch();
        }
        
        #endregion
    }
}
