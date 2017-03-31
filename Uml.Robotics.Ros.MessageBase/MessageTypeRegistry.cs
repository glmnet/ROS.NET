﻿using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace Uml.Robotics.Ros
{
    public class MessageTypeRegistry
    {
        private static Lazy<MessageTypeRegistry> instance = new Lazy<MessageTypeRegistry>(LazyThreadSafetyMode.ExecutionAndPublication);

        public static MessageTypeRegistry Default
        {
            get { return instance.Value; }
        }

        private ILogger Logger { get; set; } = ApplicationLogging.CreateLogger<MessageTypeRegistry>();
        public Dictionary<string, Type> TypeRegistry { get; } = new Dictionary<string, Type>();
        public List<string> PackageNames { get; } = new List<string>();

        public RosMessage CreateMessage(string rosMessageType)
        {
            RosMessage result = null;
            Type type = null;
            bool typeExist = TypeRegistry.TryGetValue(rosMessageType, out type);
            if (typeExist)
            {
                result = Activator.CreateInstance(type) as RosMessage;
            }

            return result;
        }

        public IEnumerable<string> GetTypeNames()
        {
            return TypeRegistry.Keys;
        }

        public IEnumerable<Assembly> GetCandidateAssemblies(params string[] tagAssemblies)
        {
            if (tagAssemblies == null)
                throw new ArgumentNullException(nameof(tagAssemblies));
            if (tagAssemblies.Length == 0)
                throw new ArgumentException("At least one tag assembly name must be specified.", nameof(tagAssemblies));

            var context = DependencyContext.Load(Assembly.GetEntryAssembly());
            var loadContext = AssemblyLoadContext.Default;

            var referenceAssemblies = new HashSet<string>(tagAssemblies, StringComparer.OrdinalIgnoreCase);
            return context.RuntimeLibraries
                .Where(x => x.Dependencies.Any(d => referenceAssemblies.Contains(d.Name)))
                .SelectMany(x => x.GetDefaultAssemblyNames(context))
                .Select(x => loadContext.LoadFromAssemblyName(x));
        }

        public void ParseAssemblyAndRegisterRosMessages(Assembly assembly)
        {
            foreach (Type othertype in assembly.GetTypes())
            {
                var messageInfo = othertype.GetTypeInfo();
                if (othertype == typeof(RosMessage) || !messageInfo.IsSubclassOf(typeof(RosMessage)) || othertype == typeof(InnerActionMessage))
                {
                    continue;
                }

                var goalAttribute = messageInfo.GetCustomAttribute<ActionGoalMessageAttribute>();
                var resultAttribute = messageInfo.GetCustomAttribute<ActionResultMessageAttribute>();
                var feedbackAttribute = messageInfo.GetCustomAttribute<ActionFeedbackMessageAttribute>();
                var ignoreAttribute = messageInfo.GetCustomAttribute<IgnoreRosMessageAttribute>();
                RosMessage message;
                if ((goalAttribute != null) || (resultAttribute != null) || (feedbackAttribute != null) || (ignoreAttribute != null))
                {
                    Type actionType;
                    if (goalAttribute != null)
                    {
                        actionType = typeof(GoalActionMessage<>);
                    }
                    else if (resultAttribute != null)
                    {
                        actionType = typeof(ResultActionMessage<>);
                    }
                    else if (feedbackAttribute != null)
                    {
                        actionType = typeof(FeedbackActionMessage<>);
                    }
                    else if (ignoreAttribute != null)
                    {
                        continue;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Could create Action Message for {othertype}");
                    }
                    Type[] innerType = { othertype };
                    var goalMessageType = actionType.MakeGenericType(innerType);
                    message = (Activator.CreateInstance(goalMessageType)) as RosMessage;
                }
                else
                {
                    message = Activator.CreateInstance(othertype) as RosMessage;
                    if ((message != null) && (message.MessageType == "xamla/unkown"))
                    {
                        throw new Exception("Invalid message type. Message type field (msgtype) was not initialized correctly.");
                    }
                }

                var packageName = message.MessageType.Split('/')[0];
                if (!PackageNames.Contains(packageName))
                {
                    PackageNames.Add(packageName);
                }

                Logger.LogDebug($"Register {message.MessageType}");
                if (!TypeRegistry.ContainsKey(message.MessageType))
                {
                    TypeRegistry.Add(message.MessageType, message.GetType());
                } else
                {
                    var messageFromRegistry = CreateMessage(message.MessageType);
                    if (messageFromRegistry.MD5Sum() != message.MD5Sum())
                    {
                        throw new InvalidOperationException($"The message of type {message.MessageType} has already been " +
                            $"registered and the MD5 sums do not match. Already registered: {messageFromRegistry.MD5Sum()} " +
                            $"new message: {message.MD5Sum()}.");
                    } else
                    {
                        Logger.LogWarning($"The message of type {message.MessageType} has already been registered. Since the" +
                            "MD5 sums do match, the new message is ignored.");
                    }
                }
            }
        }
    }
}
