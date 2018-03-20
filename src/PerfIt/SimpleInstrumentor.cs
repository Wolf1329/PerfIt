﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PerfIt
{
    public class SimpleInstrumentor : IInstrumentor, ITwoStageInstrumentor
    {
        private IInstrumentationInfo _info;

        private readonly Dictionary<string, ITwoStageTracer> _tracers = new Dictionary<string, ITwoStageTracer>();

        public SimpleInstrumentor(IInstrumentationInfo info)
        {
            _info = info;
            if (info.CorrelationIdKey == null)
            {
                _info.CorrelationIdKey = Correlation.CorrelationIdKey;
            }

            PublishInstrumentationCallback = InstrumentationEventSource.Instance.WriteInstrumentationEvent;
        }

        bool ShouldInstrument(double samplingRate)
        {
            var corrId = Correlation.GetId(_info.CorrelationIdKey);
            return ShouldInstrument(samplingRate, corrId.ToString());
        }

        internal static bool ShouldInstrument(double samplingRate, string corrId)
        {
            var d = Math.Abs(corrId.GetHashCode() * 1.0) / Math.Abs(int.MaxValue * 1.0);
            return d < samplingRate;
        }

        /// <summary>
        /// Not thread-safe. It should be populated only at the time of initialisation
        /// </summary>
        public IDictionary<string, ITwoStageTracer> Tracers
        {
            get
            {
                return _tracers;
            }
        }
        public void Instrument(Action aspect, string instrumentationContext = null, 
            double? samplingRate = null)
        {
            var token = Start(samplingRate ?? _info.SamplingRate);
            try
            {
                aspect();
            }            
            finally
            {
                Finish(token, instrumentationContext);
            }                    
        }

        public Action<string, string, long, string, string, ExtraContext> PublishInstrumentationCallback { get; set; }

        private void SetErrorContexts(Dictionary<string, object> context)
        {
            if (context != null)
            {
                context.SetContextToErrorState();
            }
        }

        public async Task InstrumentAsync(Func<Task> asyncAspect, string instrumentationContext = null, 
            double? samplingRate = null)
        {
            var token = Start(samplingRate ?? _info.SamplingRate);
            try
            {
                await asyncAspect();
            }
            finally
            {
                Finish(token, instrumentationContext);
            }
        }

        private Dictionary<string, object> BuildContexts()
        {
            var ctx = new Dictionary<string, object>();
            ctx.Add(Constants.PerfItKey, new PerfItContext());
            ctx.Add(Constants.PerfItPublishErrorsKey, _info.RaisePublishErrors);
            return ctx;
        }

        private string GetKey(string counterName, string instanceName)
        {
            return string.Format("{0}_{1}", counterName, instanceName);
        }

        /// <summary>
        /// Starts instrumentation
        /// </summary>
        /// <returns>The token to be passed to Finish method when finished</returns>
        public object Start(double samplingRate = Constants.DefaultSamplingRate)
        {
            try
            {
                var token = new InstrumentationToken()
                {
                    Contexts = _info.PublishCounters ? BuildContexts() : null,
                    Kronometer = Stopwatch.StartNew(),
                    SamplingRate = samplingRate,
                    CorrelationId = Correlation.GetId(_info.CorrelationIdKey),
                    TracerContexts = new Dictionary<string, object>()
                };

                foreach (var kv in _tracers)
                {
                    token.TracerContexts.Add(kv.Key, kv.Value.Start(_info));
                }

                return token;
            }
            catch (Exception e)
            {
                Trace.WriteLine(e);
                if(_info.RaisePublishErrors)
                    throw;
            }

            return null;
        }

        public void Finish(object token, string instrumentationContext = null, ExtraContext extraContext = null)
        {
            if(token == null)
                return; // not meant to be instrumented prob due to sampling rate

            try
            {
                var itoken = ValidateToken(token);

                if (_info.PublishEvent && ShouldInstrument(itoken.SamplingRate))
                {
                    PublishInstrumentationCallback(_info.CategoryName,
                       _info.InstanceName, itoken.Kronometer.ElapsedMilliseconds, instrumentationContext, itoken.CorrelationId.ToString(), extraContext);
                }

                foreach (var kv in _tracers)
                {
                    kv.Value.Finish(itoken.TracerContexts[kv.Key], itoken.Kronometer.ElapsedMilliseconds,
                        itoken.CorrelationId?.ToString(), 
                        instrumentationContext);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                if(_info.RaisePublishErrors)
                    throw;
            }
        }
        
        private static InstrumentationToken ValidateToken(object token)
        {
            var itoken = token as InstrumentationToken;
            if (itoken == null)
                throw new ArgumentException(
                    "This is an invalid token. Please pass the token provided when you you called Start(). Remember?", "token");
            return itoken;
        }
    }
}
