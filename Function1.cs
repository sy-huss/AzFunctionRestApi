using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Messaging.ServiceBus;

public static class CustomerFunction
{
    private static readonly ServiceBusClient serviceBusClient;
    private static readonly ServiceBusSender sender;

    static CustomerFunction()
    {
        var serviceBusConnectionString = Environment.GetEnvironmentVariable("ServiceBusConnectionString");
        var queueName = Environment.GetEnvironmentVariable("QueueName");

        if (string.IsNullOrEmpty(serviceBusConnectionString) || string.IsNullOrEmpty(queueName))
        {
            throw new InvalidOperationException("Service Bus connection string or queue name not configured.");
        }

        serviceBusClient = new ServiceBusClient(serviceBusConnectionString);
        sender = serviceBusClient.CreateSender(queueName);
    }

    [FunctionName("AddCustomer")]
    public static async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
        ILogger log)
    {
        log.LogInformation("C# HTTP trigger function processed a request.");

        string requestBody;
        Command command;

        try
        {
            requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            command = JsonConvert.DeserializeObject<Command>(requestBody);
        }
        catch (Exception ex)
        {
            log.LogError($"Error parsing request: {ex.Message}");
            return new BadRequestObjectResult("Invalid request format");
        }

        if (command == null || string.IsNullOrWhiteSpace(command.MethodName) || string.IsNullOrWhiteSpace(command.XmlParameter))
        {
            return new BadRequestObjectResult("Missing or invalid data in request");
        }

        var messageBody = JsonConvert.SerializeObject(command);
        var message = new ServiceBusMessage(messageBody)
        {
            CorrelationId = Guid.NewGuid().ToString(),
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json",
            Subject = "CareNGContact",
            TimeToLive = TimeSpan.FromHours(1),
            ReplyTo = "some-service",
            To = "Microsoft-Dataverse-PROD",
        };

        try
        {
            await sender.SendMessageAsync(message);
            log.LogInformation($"Sent customer message: {messageBody}");
        }
        catch (ServiceBusException sbEx)
        {
            log.LogError($"Service Bus specific error occurred: {sbEx.Message}");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
        catch (Exception ex)
        {
            log.LogError($"Error sending message to Service Bus: {ex.Message}");
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }

        return new OkObjectResult($"Message successfully sent to queue with ID: {message.MessageId}");
    }

    // Function to dispose of Service Bus resources when the application is shutting down
    public static async Task CloseServiceBusClientAsync()
    {
        if (sender != null)
        {
            await sender.CloseAsync();
        }

        if (serviceBusClient != null)
        {
            await serviceBusClient.DisposeAsync();
        }
    }
}

public class Command
{
    public string MethodName { get; set; }
    public int OperationType { get; set; }
    public string XmlParameter { get; set; }
}