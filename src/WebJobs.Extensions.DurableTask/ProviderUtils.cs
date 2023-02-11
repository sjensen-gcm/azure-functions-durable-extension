﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DurableTask.Core;
using DurableTask.Core.Entities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Extensions.DurableTask
{
    /// <summary>
    /// Provides access to internal functionality for the purpose of implementing durability providers.
    /// </summary>
    public static class ProviderUtils
    {
        /// <summary>
        /// Returns the instance id of the entity scheduler for a given entity id.
        /// </summary>
        /// <param name="entityId">The entity id.</param>
        /// <returns>The instance id of the scheduler.</returns>
        public static string GetSchedulerIdFromEntityId(EntityId entityId)
        {
            return EntityId.GetSchedulerIdFromEntityId(entityId);
        }

        /// <summary>
        /// Reads the state of an entity from the serialized entity scheduler state.
        /// </summary>
        /// <param name="state">The orchestration state of the scheduler.</param>
        /// <param name="serializerSettings">The serializer settings.</param>
        /// <param name="result">The serialized state of the entity.</param>
        /// <returns>true if the entity exists, false otherwise.</returns>
        public static bool TryGetEntityStateFromSerializedSchedulerState(OrchestrationState state, JsonSerializerSettings serializerSettings, out string result)
        {
            if (state != null
                && state.OrchestrationInstance != null
                && state.Input != null
                && ClientEntityContext.TryGetEntityStateFromSerializedSchedulerState(state.Input, out string serializedEntityState))
            {
                result = serializedEntityState;
                return true;
            }
            else
            {
                result = null;
                return false;
            }
        }

        /// <summary>
        /// Converts the DTFx representation of the orchestration state into the DF representation.
        /// </summary>
        /// <param name="orchestrationState">The orchestration state.</param>
        /// <returns>The orchestration status.</returns>
        public static DurableOrchestrationStatus ConvertOrchestrationStateToStatus(OrchestrationState orchestrationState)
        {
            return DurableClient.ConvertOrchestrationStateToStatus(orchestrationState);
        }
    }
}
