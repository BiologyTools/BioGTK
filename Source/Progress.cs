using AForge;
using Gdk;
using Gtk;
using sun.tools.tree;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    
    public class Progress : Gtk.Dialog
    {
        #region Properties
        private Builder _builder;
        
        /* A property that is used to update the progress bar. */
        public double ProgressValue
        {
            get
            {
                return progressBar.Fraction;
            }
            set 
            {
                // update progress bar on main UI thread
                Application.Invoke(delegate
                {
                    if(value <= 1)
                        progressBar.Fraction = value;
                    else
                        progressBar.Fraction = value / 100;
                });
            }
        }
        public double ProgressPercentValue
        {
            get
            {
                return progressBar.Fraction * 100;
            }
            set
            {
                Application.Invoke(delegate
                {
                    double percent = Math.Max(0, Math.Min(100, value));
                    progressBar.Fraction = percent / 100;
                });
            }
        }
        /* A property that is used to update the progress bar. */
        public string Text
        {
            get { return progLabel.Text; }
            set 
            {
                // update progress bar on main UI thread
                Application.Invoke(delegate
                {
                    progLabel.Text = value;
                });
            }
        }
        /* A property that is used to update the progress bar. */
        public string Status
        {
            get { return statusLabel.Text; }
            set
            { 
                // update progress bar on main UI thread
                Application.Invoke(delegate
                {
                    statusLabel.Text = value;
                });
            }
        }

        // Called by the GTK progress timer so the bar, status and timing update together.
        internal void UpdateSnapshot(double percent, string status, string timing)
        {
            progressBar.Fraction = Math.Clamp(percent, 0, 100) / 100;
            statusLabel.Text = status;
            progLabel.Text = timing;
        }
#pragma warning disable 649

        [Builder.Object]
        private ProgressBar progressBar;
        [Builder.Object]
        private Label progLabel;
        [Builder.Object]
        private Label statusLabel;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        
        public static Progress Create(string title, string status, string state)
        {
            using var gladeStream = new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Progress.glade", FileMode.Open, FileAccess.Read, FileShare.Read);
            Builder builder = new Builder(gladeStream);
            return new Progress(builder, builder.GetObject("progress").Handle, title, status, state);
        }

       
        /* The constructor for the class. */
        protected Progress(Builder builder, IntPtr handle, string title, string status, string state) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            this.Title = title;
            this.statusLabel.Text = status;
            this.progLabel.Text = state;
            App.ApplyStyles(this);
        }

        #endregion

    }

    internal sealed class ProgressEta
    {
        private const int SampleLimit = 6;
        private readonly Queue<(double Seconds, double Percent)> _samples = new();
        private string _phase;
        private (double Seconds, double Percent) _lastSample;
        private (double Seconds, double Percent) _runStart;

        internal (TimeSpan? Remaining, bool IsPhaseEstimate) Update(double seconds, double percent, string phase)
        {
            if (!double.IsFinite(percent))
                return (null, false);

            bool restarted = percent < _lastSample.Percent;
            if (restarted)
                _runStart = (seconds, percent);

            if (phase != _phase || restarted)
            {
                _samples.Clear();
                _phase = phase;
            }

            if (_samples.Count == 0 || percent > _lastSample.Percent)
            {
                _lastSample = (seconds, percent);
                _samples.Enqueue(_lastSample);
                if (_samples.Count > SampleLimit)
                    _samples.Dequeue();
            }

            if (percent <= 0 || percent >= 100)
                return (null, false);

            // Use the recent rate for counted work once there are two samples.
            // Sparse milestones can still give a rough estimate from the full run.
            bool isPhaseEstimate = phase != null && _samples.Count >= 2;
            var first = isPhaseEstimate ? _samples.Peek() : _runStart;
            double workDone = percent - first.Percent;
            if (workDone <= 0 || seconds <= first.Seconds)
                return (null, false);

            // Include time spent waiting at the current percentage. A slow step
            // reduces the observed rate instead of running the countdown to zero.
            double remaining = (100 - percent) * (seconds - first.Seconds) / workDone;
            if (!double.IsFinite(remaining) || remaining <= 0 || remaining >= TimeSpan.MaxValue.TotalSeconds)
                return (null, false);

            return (TimeSpan.FromSeconds(Math.Ceiling(remaining)), isPhaseEstimate);
        }
    }
}
