﻿namespace MassTransit.Pipeline.ConsumerFactories
{
    using System;
    using System.Threading.Tasks;
    using Context;
    using GreenPipes;
    using Metadata;
    using Util;


    public class BatchConsumerFactory<TConsumer, TMessage> :
        IConsumerFactory<IConsumer<TMessage>>
        where TMessage : class
        where TConsumer : class, IConsumer<Batch<TMessage>>
    {
        readonly IConsumerFactory<TConsumer> _consumerFactory;
        readonly IPipe<ConsumerConsumeContext<TConsumer, Batch<TMessage>>> _consumerPipe;
        readonly int _messageLimit;
        readonly TimeSpan _timeLimit;
        readonly ChannelExecutor _executor;
        BatchConsumer<TConsumer, TMessage> _currentConsumer;

        public BatchConsumerFactory(IConsumerFactory<TConsumer> consumerFactory, int messageLimit, TimeSpan timeLimit,
            IPipe<ConsumerConsumeContext<TConsumer, Batch<TMessage>>> consumerPipe)
        {
            _consumerFactory = consumerFactory;
            _messageLimit = messageLimit;
            _timeLimit = timeLimit;
            _consumerPipe = consumerPipe;

            _executor = new ChannelExecutor(1);
        }

        async Task IConsumerFactory<IConsumer<TMessage>>.Send<T>(ConsumeContext<T> context, IPipe<ConsumerConsumeContext<IConsumer<TMessage>, T>> next)
        {
            var messageContext = context as ConsumeContext<TMessage>;
            if (messageContext == null)
                throw new MessageException(typeof(T), $"Expected batch message type: {TypeMetadataCache<TMessage>.ShortName}");

            BatchConsumer<TConsumer, TMessage> consumer = await _executor.Run(() => Add(messageContext), context.CancellationToken).ConfigureAwait(false);

            await next.Send(new ConsumerConsumeContextScope<BatchConsumer<TConsumer, TMessage>, T>(context, consumer)).ConfigureAwait(false);
        }

        void IProbeSite.Probe(ProbeContext context)
        {
            var scope = context.CreateConsumerFactoryScope<IConsumer<TMessage>>("batch");

            scope.Add("timeLimit", _timeLimit);
            scope.Add("messageLimit", _messageLimit);

            _consumerFactory.Probe(scope);
            _consumerPipe.Probe(scope);
        }

        async Task<BatchConsumer<TConsumer, TMessage>> Add(ConsumeContext<TMessage> context)
        {
            if (_currentConsumer != null)
            {
                if (context.GetRetryAttempt() > 0)
                    await _currentConsumer.ForceComplete().ConfigureAwait(false);
            }

            if (_currentConsumer == null || _currentConsumer.IsCompleted)
                _currentConsumer = new BatchConsumer<TConsumer, TMessage>(_messageLimit, _timeLimit, _executor, _consumerFactory, _consumerPipe);

            await _currentConsumer.Add(context).ConfigureAwait(false);

            return _currentConsumer;
        }
    }
}
