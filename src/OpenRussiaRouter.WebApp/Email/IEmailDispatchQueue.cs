using System.Threading.Channels;

namespace OpenRussiaRouter.WebApp.Email;

public interface IEmailDispatchQueue
{
    ValueTask EnqueueAsync(EmailMessage message, CancellationToken ct = default);
    ValueTask EnqueueBatchAsync(IEnumerable<EmailMessage> messages, CancellationToken ct = default);
    ChannelReader<EmailMessage> Reader { get; }
}
