﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Castle.DynamicProxy;
using System.Reflection;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using PerfIt;

namespace PerfIt.Castle.Interception
{
    public class PerfItInterceptor : IInterceptor
    {


        private IInstanceNameProvider _instanceNameProvider = null;
        private IInstrumentationContextProvider _instrumentationContextProvider = null;
        private ConcurrentDictionary<string, SimpleInstrumentor> _instrumentors;
        private bool _inited = false;
        private object _lock = new object();



        public PerfItInterceptor(string categoryName)
        {
            CategoryName = categoryName;
            PublishCounters = true;
            RaisePublishErrors = false;
            PublishEvent = true;

        }


        private IInstrumentationInfo GetInstrumentationInfo(MethodInfo methodInfo)
        {
            var attr = (IInstrumentationInfo)methodInfo.GetCustomAttributes(typeof(IInstrumentationInfo), true).FirstOrDefault();

            return attr;
        }

        private void Init()
        {
            SetEventPolicy();
            SetPublish();
            SetErrorPolicy();

            _instrumentors = new ConcurrentDictionary<string, SimpleInstrumentor>();


            if (InstanceNameProviderType != null)
            {
                _instanceNameProvider = (IInstanceNameProvider)Activator.CreateInstance(InstanceNameProviderType);
            }

            _instrumentationContextProvider= (IInstrumentationContextProvider) new InstrumentationContextProvider();

            if (InstrumentationContextProviderType != null)
            {
                _instrumentationContextProvider = (IInstrumentationContextProvider)Activator.CreateInstance(InstrumentationContextProviderType);
            }

            _inited = true;
        }

        private ITwoStageInstrumentor InitInstrumentor(MethodInfo methodInfo)
        {
            string instrumentationContext = "";
           
            var instrumentationInfo = GetInstrumentationInfo(methodInfo);



            if (instrumentationInfo != null)
            {

                var instanceName = instrumentationInfo.InstanceName;
                if (string.IsNullOrEmpty(instanceName) && _instanceNameProvider != null)
                    instanceName = _instanceNameProvider.GetInstanceName(methodInfo);
                if (string.IsNullOrEmpty(instanceName))
                {
                    throw new InvalidOperationException("Either InstanceName or InstanceNameProviderType must be supplied.");
                }


                if (_instrumentationContextProvider != null)
                {
                    instrumentationContext = _instrumentationContextProvider.GetContext(methodInfo);
                }
                else
                {
                    throw new InvalidOperationException("The Instrumentation Context Cannot be Null. Define a InstrumentationContextProvider implementation.");
                }


                    var instrumentor = new SimpleInstrumentor(new InstrumentationInfo()
                                                    {
                                                        Description = instrumentationInfo.Description,
                                                        Counters = instrumentationInfo.Counters,
                                                        InstanceName = instanceName,
                                                        CategoryName = string.IsNullOrEmpty(this.CategoryName) ? instrumentationInfo.CategoryName : this.CategoryName
                                                        
                                                    }, PublishCounters, PublishEvent, RaisePublishErrors);
                    _instrumentors.AddOrUpdate(instrumentationContext, instrumentor, (key, inst) => instrumentor);



                    return instrumentor;
            
            }
            else
                   return null;
        }


        public void Intercept(IInvocation invocation)
        {



            if (!PublishCounters)
            {

                invocation.Proceed();

            }
            else
            {
                try
                {

                    string instrumentationContext = "";

                    if (_instrumentationContextProvider != null)
                        instrumentationContext = _instrumentationContextProvider.GetContext(invocation.MethodInvocationTarget);

                    if (!_inited)
                    {

                        lock (_lock)
                        {
                            if (!_inited)
                            {
                                Init();
                            }
                        }
                    }



                    SimpleInstrumentor instrumentor = null;

                    

                    if (_instrumentors==null || !_instrumentors.TryGetValue(instrumentationContext,out instrumentor))
                    {
                        instrumentor = (SimpleInstrumentor)InitInstrumentor(invocation.MethodInvocationTarget);
                    }


                    var returnType = invocation.Method.ReturnType;
                    if (returnType != typeof(void))
                    {
                        if (returnType == typeof(Task) || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)))
                        {
                            instrumentor.InstrumentAsync(async () => invocation.Proceed());

                        }


                        else
                        {
                            instrumentor.Instrument(() => invocation.Proceed());
                        }
                    }
                }
                catch (Exception exception)
                {
                    Trace.TraceError(exception.ToString());
                    if (RaisePublishErrors)
                        throw;
                }


            }
        }

       

        private void SetPublish()
        {
            var value = ConfigurationManager.AppSettings[Constants.PerfItPublishCounters] ?? PublishCounters.ToString();
            PublishCounters = Convert.ToBoolean(value);
        }

        protected void SetErrorPolicy()
        {
            var value = ConfigurationManager.AppSettings[Constants.PerfItPublishErrors] ?? RaisePublishErrors.ToString();
            RaisePublishErrors = Convert.ToBoolean(value);
        }

        protected void SetEventPolicy()
        {
            var value = ConfigurationManager.AppSettings[Constants.PerfItPublishEvent] ?? PublishEvent.ToString();
            PublishEvent = Convert.ToBoolean(value);
        }

        

        public Type InstanceNameProviderType { get; set; }

        public Type InstrumentationContextProviderType { get; set; }

      

        public bool PublishCounters { get; set; }

        public bool RaisePublishErrors { get; set; }

        public bool PublishEvent { get; set; }

        public string CategoryName { get; set; }


    }
}
