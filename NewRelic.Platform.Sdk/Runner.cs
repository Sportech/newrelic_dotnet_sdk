﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NewRelic.Platform.Sdk.Binding;
using System.Threading;
using NewRelic.Platform.Sdk.Utils;
using NLog;

namespace NewRelic.Platform.Sdk
{
    public class Runner
    {
        private List<AgentFactory> _factories;
        private List<Agent> _agents;

        private static Logger s_log = LogManager.GetLogger("Runner");

        public Runner()
        {
            _factories = new List<AgentFactory>();
            _agents = new List<Agent>();
        }

        /// <summary>
        /// Add an instance of an Agent to the Runner.  Any agents added prior to invoking SetupAndRun() will have their
        /// PollCycle() method invoked each polling interval.
        /// </summary>
        /// <param name="agent"></param>
        public void Add(Agent agent)
        {
            if (agent == null)
            {
                throw new ArgumentNullException("agent", "You must pass in a non-null agent");
            }

            s_log.Info("Adding new agent: {0}", agent.GetAgentName());
            _agents.Add(agent);
        }

        /// <summary>
        /// Add an instance of a factory to the Runner.  Any factories added prior to invoking SetupAndRun() will have
        /// their CreateAgentWithConfiguration() method invoked which will create a list of Agents initialized through
        /// the factory's configuration file that will be used for polling intervals.
        /// </summary>
        /// <param name="factory"></param>
        public void Add(AgentFactory factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException("factory", "You must pass in a non-null factory");
            }

            s_log.Info("Adding new factory {0}", factory.GetType());
            _factories.Add(factory);
        }

        /// <summary>
        /// This method only returns during a fatal error.  It will initialize agents if necessary, and then begin polling once
        /// per configurable PollInterval invoking registered Agent's PollCycle() methods.  Then sending the data to the New Relic service.
        /// </summary>
        public void SetupAndRun()
        {
            if (_factories.Count == 0 && _agents.Count == 0)
            {
                throw new InvalidOperationException("You must first call 'Add()' at least once with a valid factory or agent");
            }

            // Initialize agents if they added an AgentFactory, otherwise they have explicitly added initialized agents already
            if (_factories.Count > 0)
            {
                InitializeFactoryAgents();
            }

            // Initialize agents with the same Context so they aggregate to a single a request
            var context = new Context();

            foreach (var agent in _agents)
            {
                agent.PrepareToRun(context);
            }

            var pollInterval = GetPollInterval(); // Fetch poll interval here so we can report any issues early

            while (true)
            {
                try
                {
                    foreach (var agent in _agents)
                    {
                        agent.PollCycle();
                    }

                    context.SendMetricsToService();

                    Thread.Sleep(pollInterval);
                }
                catch (Exception e)
                {
                    s_log.Fatal("Fatal error occurred. Shutting down the application", e);
                    throw e;
                }
            }
        }

        private void InitializeFactoryAgents()
        {
            foreach (AgentFactory factory in _factories)
            {
                _agents = _agents.Union(factory.CreateAgents()).ToList();
            }
        }

        private int GetPollInterval()
        {
            int pollInterval = 0;
            var configVal = ConfigurationHelper.GetConfiguration(Constants.ConfigKeyPollInterval, Constants.DefaultPollInterval);

            Int32.TryParse(configVal, out pollInterval);
            s_log.Debug("Using poll interval: {0} seconds", pollInterval);

            if (pollInterval < 30)
            {
                throw new ArgumentOutOfRangeException("PollInterval", "A poll interval below 30 seconds is not supported");
            }

            return pollInterval *= 1000; // Convert to milliseconds since that's what system calls expect;
        }
    }
}
