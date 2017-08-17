﻿using IntermediatorBotSample.CommandHandling;
using Microsoft.Bot.Connector;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Underscore.Bot.MessageRouting;

namespace IntermediatorBotSample.MessageRouting
{
    public class MessageRouterResultHandler : IMessageRouterResultHandler
    {
        /// <summary>
        /// From IMessageRouterResultHandler.
        /// </summary>
        /// <param name="messageRouterResult">The result to handle.</param>
        /// <returns></returns>
        /// <summary>
        /// From IMessageRouterResultHandler.
        /// </summary>
        /// <param name="messageRouterResult">The result to handle.</param>
        /// <returns></returns>
        public virtual async Task HandleResultAsync(MessageRouterResult messageRouterResult)
        {
            if (messageRouterResult == null)
            {
                throw new ArgumentNullException($"The given result ({nameof(messageRouterResult)}) is null");
            }

            string message = string.Empty;

            switch (messageRouterResult.Type)
            {
                case MessageRouterResultType.NoActionTaken:
                case MessageRouterResultType.OK:
                    // No need to do anything
                    break;
                case MessageRouterResultType.EngagementInitiated:
                case MessageRouterResultType.EngagementAlreadyInitiated:
                case MessageRouterResultType.EngagementRejected:
                case MessageRouterResultType.EngagementAdded:
                case MessageRouterResultType.EngagementRemoved:
                    await HandleEngagementChangedResultAsync(messageRouterResult);
                    break;
                case MessageRouterResultType.NoAgentsAvailable:
                    if (messageRouterResult.Activity != null)
                    {
                        message = $"Sorry. There are no agents available right now.";
                        await MessagingUtils.ReplyToActivityAsync(messageRouterResult.Activity, message);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("The activity of the result is null");
                    }

                    break;
                case MessageRouterResultType.NoAggregationChannel:
                    if (messageRouterResult.Activity != null)
                    {
                        MessageRouterManager messageRouterManager = WebApiConfig.MessageRouterManager;
                        string botName = messageRouterManager.RoutingDataManager.ResolveBotNameInConversation(
                            MessagingUtils.CreateSenderParty(messageRouterResult.Activity));

                        message = $"{(string.IsNullOrEmpty(messageRouterResult.ErrorMessage) ? "" : $"{messageRouterResult.ErrorMessage}: ")}The message router manager is not initialized; type \"";
                        message += string.IsNullOrEmpty(botName) ? $"{Commands.CommandKeyword} " : $"@{botName} ";
                        message += $"{Commands.CommandAddAggregationChannel}\" to setup the aggregation channel";

                        await MessagingUtils.ReplyToActivityAsync(messageRouterResult.Activity, message);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("The activity of the result is null");
                    }

                    break;
                case MessageRouterResultType.FailedToForwardMessage:
                    message = $"{(string.IsNullOrEmpty(messageRouterResult.ErrorMessage) ? "Failed to forward the message" : messageRouterResult.ErrorMessage)}";
                    await MessagingUtils.ReplyToActivityAsync(messageRouterResult.Activity, message);
                    break;
                case MessageRouterResultType.Error:
                    if (string.IsNullOrEmpty(messageRouterResult.ErrorMessage))
                    {
                        System.Diagnostics.Debug.WriteLine("An error occured");
                    }
                    else
                    {
                        MessageRouterManager messageRouterManager = WebApiConfig.MessageRouterManager;

                        foreach (Party aggregationChannel in messageRouterManager.RoutingDataManager.GetAggregationParties())
                        {
                            await messageRouterManager.SendMessageToPartyByBotAsync(aggregationChannel, messageRouterResult.ErrorMessage);
                        }

                        System.Diagnostics.Debug.WriteLine(messageRouterResult.ErrorMessage);
                    }

                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Notifies both the conversation owner (agent) and the conversation client (customer)
        /// about the change in engagement (initiated/started/ended).
        /// </summary>
        /// <param name="messageRouterResult">The result to handle.</param>
        protected virtual async Task HandleEngagementChangedResultAsync(MessageRouterResult messageRouterResult)
        {
            MessageRouterManager messageRouterManager = WebApiConfig.MessageRouterManager;
            IRoutingDataManager routingDataManager = messageRouterManager.RoutingDataManager;

            Party conversationOwnerParty = messageRouterResult.ConversationOwnerParty;
            Party conversationClientParty = messageRouterResult.ConversationClientParty;

            string conversationOwnerName = conversationOwnerParty?.ChannelAccount.Name;
            string conversationClientName = conversationClientParty?.ChannelAccount.Name;

            string messageToConversationOwner = string.Empty;
            string messageToConversationClient = string.Empty;

            if (messageRouterResult.Type == MessageRouterResultType.EngagementInitiated)
            {
                foreach (Party aggregationParty in messageRouterManager.RoutingDataManager.GetAggregationParties())
                {
                    IMessageActivity messageActivity = CreateRequestCard(conversationClientParty, aggregationParty);
                    await messageRouterManager.SendMessageToPartyByBotAsync(aggregationParty, messageActivity);
                }

                messageToConversationClient = "Please wait for your request to be accepted";
            }
            else if (messageRouterResult.Type == MessageRouterResultType.EngagementAlreadyInitiated)
            {
                messageToConversationClient = "Your request has already been receieved and we are waiting for an agent to respond";
            }
            else if (messageRouterResult.Type == MessageRouterResultType.EngagementRejected)
            {
                messageToConversationOwner = $"Request from user \"{conversationClientName}\" rejected";
                messageToConversationClient = "Unfortunately your request could not be processed";
            }
            else if (messageRouterResult.Type == MessageRouterResultType.EngagementAdded)
            {
                messageToConversationOwner = $"You are now connected to user \"{conversationClientName}\"";
                messageToConversationClient = $"Your request was accepted and you are now chatting with {conversationOwnerName}";
            }
            else if (messageRouterResult.Type == MessageRouterResultType.EngagementRemoved)
            {
                messageToConversationOwner = $"You are now disconnected from the conversation with user \"{conversationClientName}\"";
                messageToConversationClient = $"Your conversation with {conversationOwnerName} has ended";
            }

            if (!string.IsNullOrEmpty(messageToConversationOwner))
            {
                await messageRouterManager.SendMessageToPartyByBotAsync(conversationOwnerParty, messageToConversationOwner);
            }

            if (!string.IsNullOrEmpty(messageToConversationClient))
            {
                await messageRouterManager.SendMessageToPartyByBotAsync(conversationClientParty, messageToConversationClient);
            }
        }

        /// <summary>
        /// Creates a new IMessageActivity containing the buttons (and additional information)
        /// to either accept or reject a pending engagement request.
        /// 
        /// Note that the created IMessageActivity will not contain valid From property value!
        /// However, if you use MessageRouterManager.SendMessageToPartyByBotAsync(),
        /// it will set the from field.
        /// </summary>
        /// <param name="pendingRequest">The party with a pending request (i.e. customer/client).</param>
        /// <param name="aggregationParty">The aggregation party to notify about the request.</param>
        /// <returns>A newly created IMessageActivity instance.</returns>
        protected virtual IMessageActivity CreateRequestCard(Party pendingRequest, Party aggregationParty)
        {
            if (pendingRequest == null || pendingRequest.ChannelAccount == null || aggregationParty == null)
            {
                throw new ArgumentNullException("The given arguments do not have the necessary details");
            }

            IMessageActivity messageActivity = Activity.CreateMessageActivity();
            messageActivity.Conversation = aggregationParty.ConversationAccount;
            messageActivity.Recipient = pendingRequest.ChannelAccount;

            string requesterId = pendingRequest.ChannelAccount.Id;
            string requesterName = pendingRequest.ChannelAccount.Name;
            string botName = WebApiConfig.MessageRouterManager.RoutingDataManager.ResolveBotNameInConversation(aggregationParty);
            string commandKeyword = string.IsNullOrEmpty(botName) ? Commands.CommandKeyword : $"@{botName}";
            string acceptCommand = $"{commandKeyword} {Commands.CommandAcceptRequest} {requesterId}";
            string rejectCommand = $"{commandKeyword} {Commands.CommandRejectRequest} {requesterId}";

            ThumbnailCard thumbnailCard = new ThumbnailCard()
            {
                Title = "Human assistance request",
                Subtitle = $"User name: {requesterName}",
                Text = $"Use the buttons to accept or reject. You can also type \"{acceptCommand}\" to accept or \"{rejectCommand}\" to reject, if the buttons are not supported.",
                Buttons = new List<CardAction>()
                {
                    new CardAction()
                    {
                        Title = "Accept",
                        Type = ActionTypes.PostBack,
                        Value = acceptCommand
                    },
                    new CardAction()
                    {
                        Title = "Reject",
                        Type = ActionTypes.PostBack,
                        Value = rejectCommand
                    }
                }
            };

            messageActivity.Attachments = new List<Attachment>() { thumbnailCard.ToAttachment() };
            return messageActivity;
        }
    }
}