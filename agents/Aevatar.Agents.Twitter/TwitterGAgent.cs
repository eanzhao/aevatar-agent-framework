using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Twitter;

/// <summary>
/// Twitter Agent - Handles Twitter operations like creating tweets, replying, and monitoring mentions
/// TwitterGAgent - 处理 Twitter 操作，如创建推文、回复和监控提及
/// </summary>
public class TwitterGAgent : GAgentBase<TwitterGAgentState>
{
    private readonly ITwitterProvider? _twitterProvider;

    public TwitterGAgent(ITwitterProvider twitterProvider)
    {
        _twitterProvider = twitterProvider;
    }

    public override Task<string> GetDescriptionAsync()
    {
        var boundStatus = string.IsNullOrEmpty(State.UserName) ? "Not bound" : $"Bound to @{State.UserName}";
        return Task.FromResult($"Twitter Agent {Id}: {boundStatus}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Initialize state if needed
        if (string.IsNullOrEmpty(State.AgentId))
        {
            State.AgentId = Id.ToString();
            State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
            State.ReplyLimit = 10; // Default reply limit
        }
    }

    /// <summary>
    /// Handle CreateTweetEvent - Creates a new tweet
    /// </summary>
    [EventHandler]
    public async Task HandleCreateTweetEvent(CreateTweetEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Text))
        {
            Logger.LogWarning("TwitterGAgent {Id} received CreateTweetEvent with empty text", Id);
            return;
        }

        if (string.IsNullOrEmpty(State.UserId))
        {
            Logger.LogWarning("TwitterGAgent {Id} cannot create tweet: user not bound", Id);
            return;
        }

        if (string.IsNullOrEmpty(State.ConsumerKey) || string.IsNullOrEmpty(State.ConsumerSecret))
        {
            Logger.LogWarning("TwitterGAgent {Id} cannot create tweet: Twitter API credentials not configured", Id);
            return;
        }

        Logger.LogInformation("TwitterGAgent {Id} creating tweet: {Text}", Id, evt.Text);

        // Call Twitter API
        if (_twitterProvider != null)
        {
            try
            {
                await _twitterProvider.PostTwitterAsync(
                    State.ConsumerKey,
                    State.ConsumerSecret,
                    evt.Text,
                    State.Token,
                    State.TokenSecret);

                Logger.LogInformation("TwitterGAgent {Id} successfully created tweet", Id);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "TwitterGAgent {Id} failed to create tweet", Id);
                throw;
            }
        }
        else
        {
            Logger.LogWarning("TwitterGAgent {Id} TwitterProvider not available", Id);
        }

        // Update state
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Handle ReplyTweetEvent - Replies to a specific tweet
    /// </summary>
    [EventHandler]
    public async Task HandleReplyTweetEvent(ReplyTweetEvent evt)
    {
        if (string.IsNullOrEmpty(evt.Text) || string.IsNullOrEmpty(evt.TweetId))
        {
            Logger.LogWarning("TwitterGAgent {Id} received ReplyTweetEvent with invalid data", Id);
            return;
        }

        if (string.IsNullOrEmpty(State.UserId))
        {
            Logger.LogWarning("TwitterGAgent {Id} cannot reply: user not bound", Id);
            return;
        }

        if (string.IsNullOrEmpty(State.ConsumerKey) || string.IsNullOrEmpty(State.ConsumerSecret))
        {
            Logger.LogWarning("TwitterGAgent {Id} cannot reply: Twitter API credentials not configured", Id);
            return;
        }

        Logger.LogInformation("TwitterGAgent {Id} replying to tweet {TweetId}: {Text}", Id, evt.TweetId, evt.Text);

        // Call Twitter API
        if (_twitterProvider != null)
        {
            try
            {
                await _twitterProvider.ReplyAsync(
                    State.ConsumerKey,
                    State.ConsumerSecret,
                    evt.Text,
                    evt.TweetId,
                    State.Token,
                    State.TokenSecret);

                Logger.LogInformation("TwitterGAgent {Id} successfully replied to tweet {TweetId}", Id, evt.TweetId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "TwitterGAgent {Id} failed to reply to tweet {TweetId}", Id, evt.TweetId);
                throw;
            }
        }
        else
        {
            Logger.LogWarning("TwitterGAgent {Id} TwitterProvider not available", Id);
        }

        // Update state - track replied tweets
        if (!State.RepliedTweets.ContainsKey(evt.TweetId))
        {
            State.RepliedTweets[evt.TweetId] = evt.Text;
        }

        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Handle BindTwitterAccountEvent - Binds a Twitter account
    /// </summary>
    [EventHandler]
    public async Task HandleBindTwitterAccountEvent(BindTwitterAccountEvent evt)
    {
        Logger.LogInformation("TwitterGAgent {Id} binding Twitter account: @{UserName}", Id, evt.UserName);

        // Update state
        State.UserId = evt.UserId;
        State.Token = evt.Token;
        State.TokenSecret = evt.TokenSecret;
        State.UserName = evt.UserName;
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        Logger.LogInformation("TwitterGAgent {Id} Twitter account bound successfully", Id);
    }

    /// <summary>
    /// Handle UnbindTwitterAccountEvent - Unbinds the Twitter account
    /// </summary>
    [EventHandler]
    public async Task HandleUnbindTwitterAccountEvent(UnbindTwitterAccountEvent evt)
    {
        Logger.LogInformation("TwitterGAgent {Id} unbinding Twitter account", Id);

        // Clear account information
        State.UserId = string.Empty;
        State.Token = string.Empty;
        State.TokenSecret = string.Empty;
        State.UserName = string.Empty;
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        Logger.LogInformation("TwitterGAgent {Id} Twitter account unbound", Id);
    }

    /// <summary>
    /// Handle ReceiveReplyEvent - Handles received reply
    /// </summary>
    [EventHandler]
    public async Task HandleReceiveReplyEvent(ReceiveReplyEvent evt)
    {
        Logger.LogInformation("TwitterGAgent {Id} received reply to tweet {TweetId}: {Text}", Id, evt.TweetId,
            evt.Text);

        // Update state
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        // Note: ReceiveReplyEvent is not constrained by TEvent, so we skip publishing here
        // If needed, use EventPublisher directly or handle differently
    }

    /// <summary>
    /// Handle ReplyMentionEvent - Triggers reply to mentions
    /// </summary>
    [EventHandler]
    public async Task HandleReplyMentionEvent(ReplyMentionEvent evt)
    {
        if (string.IsNullOrEmpty(State.UserId) || string.IsNullOrEmpty(State.UserName))
        {
            Logger.LogWarning("TwitterGAgent {Id} cannot process mentions: user not bound", Id);
            return;
        }

        if (string.IsNullOrEmpty(State.BearerToken))
        {
            Logger.LogWarning("TwitterGAgent {Id} cannot process mentions: BearerToken not configured", Id);
            return;
        }

        Logger.LogInformation("TwitterGAgent {Id} processing mentions for @{UserName}", Id, State.UserName);

        // Fetch mentions
        if (_twitterProvider != null)
        {
            try
            {
                var mentions = await _twitterProvider.GetMentionsAsync(State.UserName, State.BearerToken);

                foreach (var mention in mentions.Take(State.ReplyLimit))
                {
                    if (!State.RepliedTweets.ContainsKey(mention.Id))
                    {
                        Logger.LogInformation("TwitterGAgent {Id} found new mention: {TweetId} - {Text}",
                            Id, mention.Id, mention.Text);

                        // Track that we've seen this mention
                        // In a real implementation, you might want to publish an event to trigger a reply
                        State.RepliedTweets[mention.Id] = mention.Text;
                    }
                }

                Logger.LogInformation("TwitterGAgent {Id} processed {Count} mentions", Id, mentions.Count);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "TwitterGAgent {Id} failed to process mentions", Id);
                throw;
            }
        }
        else
        {
            Logger.LogWarning("TwitterGAgent {Id} TwitterProvider not available", Id);
        }

        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Handle TwitterConfiguration - Configuration event
    /// </summary>
    [EventHandler]
    public async Task HandleTwitterConfiguration(TwitterConfiguration configuration)
    {
        Logger.LogInformation("TwitterGAgent {Id} configuring with consumer key: {ConsumerKey}", Id,
            configuration.ConsumerKey);

        // Update state with configuration
        State.ConsumerKey = configuration.ConsumerKey;
        State.ConsumerSecret = configuration.ConsumerSecret;
        State.EncryptionPassword = configuration.EncryptionPassword;
        State.BearerToken = configuration.BearerToken;
        State.ReplyLimit = configuration.ReplyLimit > 0 ? configuration.ReplyLimit : 10;
        State.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);

        Logger.LogInformation("TwitterGAgent {Id} configured successfully", Id);
    }

    /// <summary>
    /// Configure the agent
    /// </summary>
    public async Task ConfigureAsync(TwitterConfiguration configuration, CancellationToken ct = default)
    {
        await HandleTwitterConfiguration(configuration);
    }

    /// <summary>
    /// Check if user account is bound
    /// </summary>
    public bool IsAccountBound() => !string.IsNullOrEmpty(State.UserName);

    /// <summary>
    /// Get user name
    /// </summary>
    public string GetUserName() => State.UserName ?? string.Empty;
}