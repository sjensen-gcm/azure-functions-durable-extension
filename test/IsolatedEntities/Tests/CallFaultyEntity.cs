﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DurableTask.Core.Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.DurableTask;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IsolatedEntities;

class CallFaultyEntity : Test
{
    public override async Task RunAsync(TestContext context)
    {
        var entityId = new EntityInstanceId(nameof(FaultyEntity), Guid.NewGuid().ToString());
        string orchestrationName = nameof(CallFaultyEntityOrchestration);
        string instanceId = await context.Client.ScheduleNewOrchestrationInstanceAsync(orchestrationName, entityId);
        var metadata = await context.Client.WaitForInstanceCompletionAsync(instanceId, true);

        Assert.Equal(OrchestrationRuntimeStatus.Completed, metadata.RuntimeStatus);
        Assert.Equal("ok", metadata.ReadOutputAs<string>());
    }
}

class CallFaultyEntityOrchestration
{ 
    readonly ILogger logger;

    public CallFaultyEntityOrchestration(ILogger<CallFaultyEntityOrchestration> logger)
    {
        this.logger = logger;
    }

    [Function(nameof(CallFaultyEntityOrchestration))]
    public async Task<string> RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        // read entity id from input
        var entityId = context.GetInput<EntityInstanceId>();

        async Task ExpectOperationExceptionAsync(Task t, EntityInstanceId entityId, string operationName, string errorText)
        {
            try
            {
                await t;
                throw new Exception("expected operation exception, but none was thrown");
            }
            catch(EntityOperationFailedException entityException)
            {
                Assert.Equal(operationName, entityException.OperationName);
                Assert.Equal(entityId, entityException.EntityId);
                Assert.Contains(errorText, entityException.Message);
                Assert.NotNull(entityException.FailureDetails);
            }
            catch (Exception e)
            {
                throw new Exception($"wrong exception thrown", e);
            }
        }

        try
        {
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ExpectOperationExceptionAsync(
                context.Entities.CallEntityAsync(entityId, "SetToUnserializable"),
                entityId,
                "SetToUnserializable",
                "problematic object: is not serializable");

            // since the operation failed, the entity state is unchanged, meaning the entity still does not exist
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await context.Entities.CallEntityAsync(entityId, "SetToUndeserializable");

            Assert.True(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ExpectOperationExceptionAsync(
               context.Entities.CallEntityAsync<int>(entityId, "Get"),
               entityId,
               "Get",
               "problematic object: is not deserializable");

            await context.Entities.CallEntityAsync(entityId, "DeleteWithoutReading");

            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await context.Entities.CallEntityAsync(entityId, "Set", 3);

            Assert.Equal(3, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await ExpectOperationExceptionAsync(
               context.Entities.CallEntityAsync(entityId, "SetThenThrow", 333),
               entityId,
               "SetThenThrow",
               "KABOOM");
 
            // value should be unchanged
            Assert.Equal(3, await context.Entities.CallEntityAsync<int>(entityId, "Get"));

            await ExpectOperationExceptionAsync(
              context.Entities.CallEntityAsync(entityId, "DeleteThenThrow"),
              entityId,
              "DeleteThenThrow",
              "KABOOM");

            // value should be unchanged
            Assert.Equal(3, await context.Entities.CallEntityAsync<int>(entityId, "Get"));
              
            await context.Entities.CallEntityAsync(entityId, "Delete");

            // entity was deleted
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            await ExpectOperationExceptionAsync(
              context.Entities.CallEntityAsync(entityId, "SetThenThrow", 333),
              entityId,
              "SetThenThrow",
              "KABOOM");

            // must have rolled back to non-existing state
            Assert.False(await context.Entities.CallEntityAsync<bool>(entityId, "Exists"));

            return "ok";
        }
        catch (Exception e)
        {
            logger.LogError("exception in CallFaultyEntityOrchestration: {exception}", e);
            return e.ToString();
        }
    }
}