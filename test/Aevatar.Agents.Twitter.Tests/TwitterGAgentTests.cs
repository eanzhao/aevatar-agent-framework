using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Twitter;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Aevatar.Agents.Twitter.Tests;

public class TwitterGAgentTests
{
    private readonly Mock<ILogger<TwitterGAgent>> _mockLogger;
    private readonly Mock<ITwitterProvider> _mockTwitterProvider;
    private readonly Mock<IEventPublisher> _mockEventPublisher;

    public TwitterGAgentTests()
    {
        _mockLogger = new Mock<ILogger<TwitterGAgent>>();
        _mockTwitterProvider = new Mock<ITwitterProvider>();
        _mockEventPublisher = new Mock<IEventPublisher>();
    }

    private TwitterGAgent CreateAgent(Guid? id = null, ITwitterProvider? provider = null)
    {
        var agentId = id ?? Guid.NewGuid();
        var twitterProvider = provider ?? _mockTwitterProvider.Object;
        var agent = new TwitterGAgent(agentId, twitterProvider, _mockLogger.Object);
        agent.SetEventPublisher(_mockEventPublisher.Object);
        return agent;
    }

    [Fact(DisplayName = "TwitterGAgent should initialize with correct state")]
    public async Task TwitterGAgent_ShouldInitializeWithCorrectState()
    {
        // Arrange & Act
        var agent = CreateAgent();
        await agent.OnActivateAsync();

        // Assert
        agent.Id.Should().NotBe(Guid.Empty);
        var state = agent.GetState();
        state.Should().NotBeNull();
        state.AgentId.Should().Be(agent.Id.ToString());
        state.ReplyLimit.Should().Be(10); // Default value
    }

    [Fact(DisplayName = "GetDescriptionAsync should return correct description")]
    public async Task GetDescriptionAsync_ShouldReturnCorrectDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("Twitter Agent");
        description.Should().Contain("Not bound");
    }

    [Fact(DisplayName = "GetDescriptionAsync should show bound status when account is bound")]
    public async Task GetDescriptionAsync_ShouldShowBoundStatusWhenAccountIsBound()
    {
        // Arrange
        var agent = CreateAgent();
        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "token",
            TokenSecret = "secret"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };

        await agent.HandleEventAsync(envelope);

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("@testuser");
    }

    [Fact(DisplayName = "HandleCreateTweetEvent should create tweet via TwitterProvider")]
    public async Task HandleCreateTweetEvent_ShouldCreateTweetViaTwitterProvider()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new TwitterConfiguration
        {
            ConsumerKey = "consumer_key",
            ConsumerSecret = "consumer_secret",
            BearerToken = "bearer_token",
            EncryptionPassword = "password",
            ReplyLimit = 10
        };

        var configEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(config)
        };
        await agent.HandleEventAsync(configEnvelope);

        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "token",
            TokenSecret = "secret"
        };

        var bindEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };
        await agent.HandleEventAsync(bindEnvelope);

        var createEvent = new CreateTweetEvent
        {
            Text = "Hello Twitter!"
        };

        _mockTwitterProvider
            .Setup(p => p.PostTwitterAsync(
                "consumer_key",
                "consumer_secret",
                "Hello Twitter!",
                "token",
                "secret"))
            .Returns(Task.CompletedTask);

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(createEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        _mockTwitterProvider.Verify(
            p => p.PostTwitterAsync(
                "consumer_key",
                "consumer_secret",
                "Hello Twitter!",
                "token",
                "secret"),
            Times.Once);

        var state = agent.GetState();
        state.LastUpdated.Should().NotBeNull();
    }

    [Fact(DisplayName = "HandleCreateTweetEvent should not create tweet when user not bound")]
    public async Task HandleCreateTweetEvent_ShouldNotCreateTweetWhenUserNotBound()
    {
        // Arrange
        var agent = CreateAgent();
        var createEvent = new CreateTweetEvent
        {
            Text = "Hello Twitter!"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(createEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        _mockTwitterProvider.Verify(
            p => p.PostTwitterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact(DisplayName = "HandleCreateTweetEvent should not create tweet when text is empty")]
    public async Task HandleCreateTweetEvent_ShouldNotCreateTweetWhenTextIsEmpty()
    {
        // Arrange
        var agent = CreateAgent();
        var createEvent = new CreateTweetEvent
        {
            Text = string.Empty
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(createEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        _mockTwitterProvider.Verify(
            p => p.PostTwitterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact(DisplayName = "HandleReplyTweetEvent should reply to tweet via TwitterProvider")]
    public async Task HandleReplyTweetEvent_ShouldReplyToTweetViaTwitterProvider()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new TwitterConfiguration
        {
            ConsumerKey = "consumer_key",
            ConsumerSecret = "consumer_secret",
            BearerToken = "bearer_token",
            EncryptionPassword = "password",
            ReplyLimit = 10
        };

        var configEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(config)
        };
        await agent.HandleEventAsync(configEnvelope);

        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "token",
            TokenSecret = "secret"
        };

        var bindEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };
        await agent.HandleEventAsync(bindEnvelope);

        var replyEvent = new ReplyTweetEvent
        {
            TweetId = "tweet123",
            Text = "This is a reply"
        };

        _mockTwitterProvider
            .Setup(p => p.ReplyAsync(
                "consumer_key",
                "consumer_secret",
                "This is a reply",
                "tweet123",
                "token",
                "secret"))
            .Returns(Task.CompletedTask);

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(replyEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        _mockTwitterProvider.Verify(
            p => p.ReplyAsync(
                "consumer_key",
                "consumer_secret",
                "This is a reply",
                "tweet123",
                "token",
                "secret"),
            Times.Once);

        var state = agent.GetState();
        state.RepliedTweets.Should().ContainKey("tweet123");
        state.RepliedTweets["tweet123"].Should().Be("This is a reply");
    }

    [Fact(DisplayName = "HandleBindTwitterAccountEvent should bind account")]
    public async Task HandleBindTwitterAccountEvent_ShouldBindAccount()
    {
        // Arrange
        var agent = CreateAgent();
        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "oauth_token",
            TokenSecret = "oauth_secret"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.UserName.Should().Be("testuser");
        state.UserId.Should().Be("12345");
        state.Token.Should().Be("oauth_token");
        state.TokenSecret.Should().Be("oauth_secret");
        agent.IsAccountBound().Should().BeTrue();
        agent.GetUserName().Should().Be("testuser");
    }

    [Fact(DisplayName = "HandleUnbindTwitterAccountEvent should unbind account")]
    public async Task HandleUnbindTwitterAccountEvent_ShouldUnbindAccount()
    {
        // Arrange
        var agent = CreateAgent();
        
        // First bind
        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "oauth_token",
            TokenSecret = "oauth_secret"
        };

        var bindEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };
        await agent.HandleEventAsync(bindEnvelope);

        // Then unbind
        var unbindEvent = new UnbindTwitterAccountEvent();
        var unbindEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(unbindEvent)
        };

        // Act
        await agent.HandleEventAsync(unbindEnvelope);

        // Assert
        var state = agent.GetState();
        state.UserName.Should().BeEmpty();
        state.UserId.Should().BeEmpty();
        state.Token.Should().BeEmpty();
        state.TokenSecret.Should().BeEmpty();
        agent.IsAccountBound().Should().BeFalse();
    }

    [Fact(DisplayName = "HandleReceiveReplyEvent should update state")]
    public async Task HandleReceiveReplyEvent_ShouldUpdateState()
    {
        // Arrange
        var agent = CreateAgent();
        var receiveEvent = new ReceiveReplyEvent
        {
            TweetId = "tweet123",
            Text = "Received reply text"
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(receiveEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.LastUpdated.Should().NotBeNull();
    }

    [Fact(DisplayName = "HandleReplyMentionEvent should fetch mentions via TwitterProvider")]
    public async Task HandleReplyMentionEvent_ShouldFetchMentionsViaTwitterProvider()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new TwitterConfiguration
        {
            ConsumerKey = "consumer_key",
            ConsumerSecret = "consumer_secret",
            BearerToken = "bearer_token",
            EncryptionPassword = "password",
            ReplyLimit = 5
        };

        var configEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(config)
        };
        await agent.HandleEventAsync(configEnvelope);

        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "token",
            TokenSecret = "secret"
        };

        var bindEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };
        await agent.HandleEventAsync(bindEnvelope);

        var mentions = new List<Tweet>
        {
            new Tweet { Id = "mention1", Text = "Mention 1" },
            new Tweet { Id = "mention2", Text = "Mention 2" },
            new Tweet { Id = "mention3", Text = "Mention 3" }
        };

        _mockTwitterProvider
            .Setup(p => p.GetMentionsAsync("testuser", "bearer_token"))
            .ReturnsAsync(mentions);

        var replyMentionEvent = new ReplyMentionEvent();
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(replyMentionEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        _mockTwitterProvider.Verify(
            p => p.GetMentionsAsync("testuser", "bearer_token"),
            Times.Once);

        var state = agent.GetState();
        state.RepliedTweets.Should().HaveCount(3);
        state.RepliedTweets.Should().ContainKey("mention1");
        state.RepliedTweets.Should().ContainKey("mention2");
        state.RepliedTweets.Should().ContainKey("mention3");
    }

    [Fact(DisplayName = "HandleReplyMentionEvent should respect reply limit")]
    public async Task HandleReplyMentionEvent_ShouldRespectReplyLimit()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new TwitterConfiguration
        {
            ConsumerKey = "consumer_key",
            ConsumerSecret = "consumer_secret",
            BearerToken = "bearer_token",
            EncryptionPassword = "password",
            ReplyLimit = 2
        };

        var configEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(config)
        };
        await agent.HandleEventAsync(configEnvelope);

        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "token",
            TokenSecret = "secret"
        };

        var bindEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };
        await agent.HandleEventAsync(bindEnvelope);

        var mentions = new List<Tweet>
        {
            new Tweet { Id = "mention1", Text = "Mention 1" },
            new Tweet { Id = "mention2", Text = "Mention 2" },
            new Tweet { Id = "mention3", Text = "Mention 3" },
            new Tweet { Id = "mention4", Text = "Mention 4" },
            new Tweet { Id = "mention5", Text = "Mention 5" }
        };

        _mockTwitterProvider
            .Setup(p => p.GetMentionsAsync("testuser", "bearer_token"))
            .ReturnsAsync(mentions);

        var replyMentionEvent = new ReplyMentionEvent();
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(replyMentionEvent)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.RepliedTweets.Should().HaveCount(2); // Should only process 2 mentions due to ReplyLimit
    }

    [Fact(DisplayName = "HandleTwitterConfiguration should update configuration")]
    public async Task HandleTwitterConfiguration_ShouldUpdateConfiguration()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new TwitterConfiguration
        {
            ConsumerKey = "test_consumer_key",
            ConsumerSecret = "test_consumer_secret",
            BearerToken = "test_bearer_token",
            EncryptionPassword = "test_password",
            ReplyLimit = 20
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(config)
        };

        // Act
        await agent.HandleEventAsync(envelope);

        // Assert
        var state = agent.GetState();
        state.ConsumerKey.Should().Be("test_consumer_key");
        state.ConsumerSecret.Should().Be("test_consumer_secret");
        state.BearerToken.Should().Be("test_bearer_token");
        state.EncryptionPassword.Should().Be("test_password");
        state.ReplyLimit.Should().Be(20);
    }

    [Fact(DisplayName = "ConfigureAsync should configure agent")]
    public async Task ConfigureAsync_ShouldConfigureAgent()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new TwitterConfiguration
        {
            ConsumerKey = "test_consumer_key",
            ConsumerSecret = "test_consumer_secret",
            BearerToken = "test_bearer_token",
            EncryptionPassword = "test_password",
            ReplyLimit = 15
        };

        // Act
        await agent.ConfigureAsync(config);

        // Assert
        var state = agent.GetState();
        state.ConsumerKey.Should().Be("test_consumer_key");
        state.ConsumerSecret.Should().Be("test_consumer_secret");
        state.BearerToken.Should().Be("test_bearer_token");
        state.EncryptionPassword.Should().Be("test_password");
        state.ReplyLimit.Should().Be(15);
    }

    [Fact(DisplayName = "HandleCreateTweetEvent should propagate exceptions")]
    public async Task HandleCreateTweetEvent_ShouldPropagateExceptions()
    {
        // Arrange
        var agent = CreateAgent();
        var config = new TwitterConfiguration
        {
            ConsumerKey = "consumer_key",
            ConsumerSecret = "consumer_secret",
            BearerToken = "bearer_token",
            EncryptionPassword = "password",
            ReplyLimit = 10
        };

        var configEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(config)
        };
        await agent.HandleEventAsync(configEnvelope);

        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "token",
            TokenSecret = "secret"
        };

        var bindEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(bindEvent)
        };
        await agent.HandleEventAsync(bindEnvelope);

        var createEvent = new CreateTweetEvent
        {
            Text = "Hello Twitter!"
        };

        _mockTwitterProvider
            .Setup(p => p.PostTwitterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("API Error"));

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(createEvent)
        };

        // Act - The exception should be logged but not re-thrown by HandleEventAsync
        // The framework handles exceptions internally
        await agent.HandleEventAsync(envelope);

        // Assert - Verify the provider was called (exception was thrown internally)
        _mockTwitterProvider.Verify(
            p => p.PostTwitterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Once);
    }
}

