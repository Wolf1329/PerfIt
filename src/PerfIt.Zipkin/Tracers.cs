﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Criteo.Profiling.Tracing;
using Criteo.Profiling.Tracing.Tracers.Zipkin;
using Trace = Criteo.Profiling.Tracing.Trace;

namespace PerfIt.Zipkin
{
    public class DefaultTracer : ITwoStageTracer
    {
        private readonly ISpanEmitter _spanEmitter;

        public DefaultTracer(ISpanEmitter spanEmitter)
        {
            _spanEmitter = spanEmitter;
        }

        public void Finish(object token, long timeTakenMilli, string correlationId = null, InstrumentationContext extraContext = null)
        {
            if(token == null)
                throw new ArgumentNullException(nameof(token));
            var tpl = (Tuple<IInstrumentationInfo, Span>) token;
            tpl.Item2.SetAsComplete(DateTime.UtcNow);
            ZipkinEventSource.Instance.WriteSpan(
                tpl.Item2, correlationId, extraContext);
            OnFinishing(tpl.Item2);
            
            _spanEmitter.Emit(tpl.Item2);         
        }

        public object Start(IInstrumentationInfo info)
        {
            var trace = Trace.Current;
            var newTrace = trace == null ? Trace.Create() : trace.Child();
            Trace.Current = newTrace;
            var span = new Span(newTrace.CurrentSpan, DateTime.UtcNow)
            {
                ServiceName = info.CategoryName,
                Name = info.InstanceName
            };

            OnStarting(span);
            
            return new Tuple<IInstrumentationInfo, Span>(info, span);
        }

        protected virtual void OnStarting(Span span)
        {
            // none
        }

        protected virtual void OnFinishing(Span span)
        {
            // none
        }

        public void Dispose()
        {
            
        }
    }

    public class ServerTracer : DefaultTracer
    {
        public ServerTracer(ISpanEmitter spanEmitter) : base(spanEmitter)
        {
        }

        protected override void OnStarting(Span span)
        {
            base.OnStarting(span);
            span.Annotations.Add(new ZipkinAnnotation(DateTime.UtcNow, "sr"));
        }

        protected override void OnFinishing(Span span)
        {
            base.OnFinishing(span);
            span.Annotations.Add(new ZipkinAnnotation(DateTime.UtcNow, "ss"));
        }
    }

    public class ClientTracer : DefaultTracer
    {
        public ClientTracer(ISpanEmitter spanEmitter) : base(spanEmitter)
        {
        }

        protected override void OnStarting(Span span)
        {
            base.OnStarting(span);
            span.Annotations.Add(new ZipkinAnnotation(DateTime.UtcNow, "cs"));
        }

        protected override void OnFinishing(Span span)
        {
            base.OnFinishing(span);
            span.Annotations.Add(new ZipkinAnnotation(DateTime.UtcNow, "cr"));
        }
    }

}
