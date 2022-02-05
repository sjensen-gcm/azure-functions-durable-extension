﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DurableTask.Core;
using DurableTask.Core.Settings;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;

namespace Microsoft.Azure.WebJobs.Extensions.DurableTask.Correlation
{
    /// <summary>
    /// Telemetry Initializer that sets correlation ids for W3C.
    /// This source is based on W3COperationCorrelationTelemetryInitializer.cs
    /// 1. Modified with CorrelationTraceContext.Current
    /// 2. Avoid to be overriden when it is RequestTelemetry
    /// Original Source is here https://github.com/microsoft/ApplicationInsights-dotnet-server/blob/2.8.0/Src/Common/W3C/W3COperationCorrelationTelemetryInitializer.cs.
    /// </summary>
    internal
        class DurableTaskCorrelationTelemetryInitializer : ITelemetryInitializer
    {
        private const string RddDiagnosticSourcePrefix = "rdddsc";
        private const string SqlRemoteDependencyType = "SQL";

        /// These internal property is copied from W3CConstants
        /// <summary>Trace-Id tag name.</summary>
        internal const string TraceIdTag = "w3c_traceId";

        /// <summary>Span-Id tag name.</summary>
        internal const string SpanIdTag = "w3c_spanId";

        /// <summary>Parent span-Id tag name.</summary>
        internal const string ParentSpanIdTag = "w3c_parentSpanId";

        /// <summary>Version tag name.</summary>
        internal const string VersionTag = "w3c_version";

        /// <summary>Sampled tag name.</summary>
        internal const string SampledTag = "w3c_sampled";

        /// <summary>Tracestate tag name.</summary>
        internal const string TracestateTag = "w3c_tracestate";

        /// <summary>Default version value.</summary>
        internal const string DefaultVersion = "00";

        /// <summary>
        /// Default sampled flag value: may be recorded, not requested.
        /// </summary>
        internal const string TraceFlagRecordedAndNotRequested = "02";

        /// <summary>Recorded and requested sampled flag value.</summary>
        internal const string TraceFlagRecordedAndRequested = "03";

        /// <summary>Requested trace flag.</summary>
        internal const byte RequestedTraceFlag = 1;

        /// <summary>Legacy root Id tag name.</summary>
        internal const string LegacyRootIdProperty = "ai_legacyRootId";

        /// <summary>Legacy root Id tag name.</summary>
        internal const string LegacyRequestIdProperty = "ai_legacyRequestId";

        /// <summary>
        /// Constructor.
        /// </summary>
        public DurableTaskCorrelationTelemetryInitializer()
        {
            this.ExcludeComponentCorrelationHttpHeadersOnDomains = new HashSet<string>();
        }

        /// <summary>
        /// Set of suppress telemetry tracking if you add Host name on this.
        /// </summary>
        public HashSet<string> ExcludeComponentCorrelationHttpHeadersOnDomains { get; set; }

        /// <summary>
        /// Initializes telemetry item.
        /// </summary>
        /// <param name="telemetry">Telemetry item.</param>
        public void Initialize(ITelemetry telemetry)
        {
            if (this.IsSuppressedTelemetry(telemetry))
            {
                this.SuppressTelemetry(telemetry);
                return;
            }

            if (!(telemetry is RequestTelemetry))
            {
                Activity currentActivity = Activity.Current;
                if (telemetry is ExceptionTelemetry)
                {
                    Console.WriteLine("exception!");
                }

                if (currentActivity == null)
                {
                    if (CorrelationTraceContext.Current != null)
                    {
                        UpdateTelemetry(telemetry, CorrelationTraceContext.Current);
                    }
                }
                else
                {
                    if (CorrelationTraceContext.Current != null)
                    {
                        UpdateTelemetry(telemetry, CorrelationTraceContext.Current);
                    }
                    else if (CorrelationSettings.Current.Protocol == Protocol.W3CTraceContext)
                    {
                        UpdateTelemetry(telemetry, currentActivity, false);
                    }
                    else if (CorrelationSettings.Current.Protocol == Protocol.HttpCorrelationProtocol
                      && telemetry is ExceptionTelemetry)
                    {
                        UpdateTelemetryExceptionForHTTPCorrelationProtocol((ExceptionTelemetry)telemetry, currentActivity);
                    }
                }
            }
        }

        internal static void UpdateTelemetry(ITelemetry telemetry, TraceContextBase contextBase)
        {
            switch (contextBase)
            {
                case NullObjectTraceContext nullObjectContext:
                    return;
                case W3CTraceContext w3cContext:
                    UpdateTelemetryW3C(telemetry, w3cContext);
                    break;
                case HttpCorrelationProtocolTraceContext httpCorrelationProtocolTraceContext:
                    UpdateTelemetryHttpCorrelationProtocol(telemetry, httpCorrelationProtocolTraceContext);
                    break;
                default:
                    return;
            }
        }

        internal static void UpdateTelemetryHttpCorrelationProtocol(ITelemetry telemetry, HttpCorrelationProtocolTraceContext context)
        {
            OperationTelemetry opTelemetry = telemetry as OperationTelemetry;

            bool initializeFromCurrent = opTelemetry != null;

            if (initializeFromCurrent)
            {
                initializeFromCurrent &= !(opTelemetry is DependencyTelemetry dependency &&
                    dependency.Type == SqlRemoteDependencyType &&
                    dependency.Context.GetInternalContext().SdkVersion
                        .StartsWith(RddDiagnosticSourcePrefix, StringComparison.Ordinal));
            }

            if (initializeFromCurrent)
            {
                opTelemetry.Id = !string.IsNullOrEmpty(opTelemetry.Id) ? opTelemetry.Id : context.TelemetryId;
                telemetry.Context.Operation.ParentId = !string.IsNullOrEmpty(telemetry.Context.Operation.ParentId) ? telemetry.Context.Operation.ParentId : context.TelemetryContextOperationParentId;
            }
            else
            {
                telemetry.Context.Operation.Id = !string.IsNullOrEmpty(telemetry.Context.Operation.Id) ? telemetry.Context.Operation.Id : context.TelemetryContextOperationId;
                if (telemetry is ExceptionTelemetry)
                {
                    telemetry.Context.Operation.ParentId = context.TelemetryId;
                }
                else
                {
                    telemetry.Context.Operation.ParentId = !string.IsNullOrEmpty(telemetry.Context.Operation.ParentId) ? telemetry.Context.Operation.ParentId : context.TelemetryContextOperationParentId;
                }
            }
        }

        internal static void UpdateTelemetryW3C(ITelemetry telemetry, W3CTraceContext context)
        {
            OperationTelemetry opTelemetry = telemetry as OperationTelemetry;

            bool initializeFromCurrent = opTelemetry != null;

            if (initializeFromCurrent)
            {
                initializeFromCurrent &= !(opTelemetry is DependencyTelemetry dependency &&
                    dependency.Type == SqlRemoteDependencyType &&
                    dependency.Context.GetInternalContext().SdkVersion
                        .StartsWith(RddDiagnosticSourcePrefix, StringComparison.Ordinal));
            }

            if (!string.IsNullOrEmpty(context.TraceState))
            {
                opTelemetry.Properties["w3c_tracestate"] = context.TraceState;
            }

            TraceParent traceParent = TraceParent.FromString(context.TraceParent);

            if (initializeFromCurrent)
            {
                if (string.IsNullOrEmpty(opTelemetry.Id))
                {
                    opTelemetry.Id = traceParent.SpanId;
                }

                if (string.IsNullOrEmpty(context.ParentSpanId))
                {
                    telemetry.Context.Operation.ParentId = telemetry.Context.Operation.Id;
                }
            }
            else
            {
                if (telemetry.Context.Operation.Id == null)
                {
                    telemetry.Context.Operation.Id = traceParent.TraceId;
                }

                if (telemetry.Context.Operation.ParentId == null)
                {
                    telemetry.Context.Operation.ParentId = traceParent.SpanId;
                }
            }
        }

        internal void SuppressTelemetry(ITelemetry telemetry)
        {
            // TODO For suppressing Dependency, I make the Id as suppressed. This strategy increases the number of telemetery.
            // However, new implementation is already supressed. Once we've fully tested this logic, remove the suppression logic on this class.
            telemetry.Context.Operation.Id = "suppressed";
            telemetry.Context.Operation.ParentId = "suppressed";
#pragma warning disable 618

            // Context. Properties.  ai_legacyRequestId , ai_legacyRequestId
            foreach (var key in telemetry.Context.Properties.Keys)
            {
                if (key == "ai_legacyRootId" ||
                    key == "ai_legacyRequestId")
                {
                    telemetry.Context.Properties[key] = "suppressed";
                }
            }
#pragma warning restore 618

            ((OperationTelemetry)telemetry).Id = "suppressed";
        }

        internal bool IsSuppressedTelemetry(ITelemetry telemetry)
        {
            OperationTelemetry opTelemetry = telemetry as OperationTelemetry;
            if (telemetry is DependencyTelemetry)
            {
                DependencyTelemetry dTelemetry = telemetry as DependencyTelemetry;
#pragma warning disable 618
                if (!string.IsNullOrEmpty(dTelemetry.CommandName))
                {
                    var host = new Uri(dTelemetry.CommandName).Host;
#pragma warning restore 618
                    if (this.ExcludeComponentCorrelationHttpHeadersOnDomains.Contains(host))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        internal static void UpdateTelemetryExceptionForHTTPCorrelationProtocol(ExceptionTelemetry telemetry, Activity activity)
        {
            telemetry.Context.Operation.ParentId = activity.Id;
            telemetry.Context.Operation.Id = activity.RootId;
        }

        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", Justification = "This method has different code for Net45/NetCore")]
        internal static void UpdateTelemetry(ITelemetry telemetry, Activity activity, bool forceUpdate)
        {
            if (activity == null)
            {
                return;
            }

            // Requests and dependencies are initialized from the Activity.Current.
            // (i.e. telemetry.Id = current.Id). Activity is created for such requests specifically
            // Traces, exceptions, events on the other side are children of current activity
            // There is one exception - SQL DiagnosticSource where current Activity is a parent
            // for dependency calls.

            OperationTelemetry opTelemetry = telemetry as OperationTelemetry;
            bool initializeFromCurrent = opTelemetry != null;

            if (initializeFromCurrent)
            {
                initializeFromCurrent &= !(opTelemetry is DependencyTelemetry dependency &&
                                           dependency.Type == SqlRemoteDependencyType &&
                                           dependency.Context.GetInternalContext().SdkVersion
                                               .StartsWith(RddDiagnosticSourcePrefix, StringComparison.Ordinal));
            }

            if (telemetry is OperationTelemetry operation)
            {
                operation.Properties[TracestateTag] = activity.TraceStateString;
            }

            if (initializeFromCurrent)
            {
                opTelemetry.Id = activity.SpanId.ToHexString();
                if (activity.ParentSpanId != default)
                {
                    opTelemetry.Context.Operation.ParentId = activity.ParentSpanId.ToHexString();
                }
            }
            else
            {
                telemetry.Context.Operation.ParentId = activity.SpanId.ToHexString();
            }
        }
    }
}
