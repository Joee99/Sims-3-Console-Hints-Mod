using System;
using Sims3.SimIFace;

[assembly: Tunable]

namespace ConsoleHints
{
    // The game loads Tuning.xml, which names kInstantiator, which runs the static ctor.
    public class Instantiator
    {
        [Tunable]
        protected static bool kInstantiator = false;

        // Each new poller gets the next number. A poller that finds it is no longer the
        // newest destroys itself, so there is only ever one running.
        public static int sGeneration;

        static Instantiator()
        {
            World.sOnStartupAppEventHandler += new EventHandler(StartPoller);
            World.sOnWorldLoadFinishedEventHandler += new EventHandler(StartPoller);
            World.sOnEnterNotInWorldEventHandler += new EventHandler(StartPoller);
        }

        private static void StartPoller(object sender, EventArgs e)
        {
            try
            {
                sGeneration++;
                Simulator.AddObject(new PollTask(sGeneration));
            }
            catch (Exception ex)
            {
                // The next world load / main menu will try again
                Hints.ReportError("starting the poller", ex);
            }
        }
    }

    // Checks every tick whether the cheat console has been opened. Returning from
    // Simulate() without sleeping is fine, EA's OneShotFunctionTask does the same thing
    // while it waits on its stopwatch.
    public class PollTask : Task
    {
        private readonly int mGeneration;

        public PollTask(int generation)
        {
            mGeneration = generation;
        }

        public override void Simulate()
        {
            if (mGeneration != Instantiator.sGeneration)
            {
                Simulator.DestroyObject(ObjectId);
                return;
            }

            try
            {
                Hints.Poll();
            }
            catch (Exception ex)
            {
                // Keep polling whatever happens, but remember it so it can be shown
                Hints.ReportError("attaching to the console", ex);
            }
        }
    }
}
