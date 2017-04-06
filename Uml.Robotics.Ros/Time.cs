﻿using System;
using System.Threading;
using Microsoft.Extensions.Logging;

using Messages.rosgraph_msgs;

namespace Uml.Robotics.Ros
{
    public class SimTime
    {
        private static ILogger Logger { get; } = ApplicationLogging.CreateLogger<SimTime>();
        public delegate void SimTimeDelegate(TimeSpan ts);


        public static SimTime Instance
        {
            get { return instance.Value; }
        }

        private static Lazy<SimTime> instance = new Lazy<SimTime>(LazyThreadSafetyMode.ExecutionAndPublication);
        private bool checkedSimTime;
        private NodeHandle nodeHandle;
        private bool simTime;
        private Subscriber<Clock> simTimeSubscriber;


        public static void Terminate()
        {
            Instance.Shutdown();
            instance = new Lazy<SimTime>(LazyThreadSafetyMode.ExecutionAndPublication);
        }


        public SimTime()
        {
            new Thread(() =>
            {
                try
                {
                    while (!ROS.isStarted() && !ROS.shutting_down)
                    {
                        Thread.Sleep(100);
                    }
                    if (!ROS.shutting_down)
                    {
                        nodeHandle = new NodeHandle();
                        simTimeSubscriber = nodeHandle.subscribe<Clock>("/clock", 1, SimTimeCallback);
                    }
                }
                catch(Exception e)
                {
                    Logger.LogError("Caught exception: " + e.Message);
                }
            }).Start();
        }


        public bool IsTimeSimulated
        {
            get { return simTime; }
        }


        public void Shutdown()
        {
            simTimeSubscriber.shutdown();
            nodeHandle.shutdown();
        }

        public event SimTimeDelegate SimTimeEvent;

        private void SimTimeCallback(Clock time)
        {
            if (!checkedSimTime)
            {
                if (Param.Get("/use_sim_time", out simTime))
                {
                    checkedSimTime = true;
                }
            }
            if (simTime && SimTimeEvent != null)
                SimTimeEvent.Invoke(TimeSpan.FromMilliseconds(time.clock.data.sec*1000.0 + (time.clock.data.nsec/100000000.0)));
        }
    }
}
