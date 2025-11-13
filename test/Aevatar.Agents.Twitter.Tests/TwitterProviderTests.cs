using Aevatar.Agents.Twitter;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Aevatar.Agents.Twitter.Tests;

public class TwitterProviderTests
{
    private readonly Mock<ILogger<TwitterProvider>> _mockLogger;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly TwitterProvider _twitterProvider;

    public TwitterProviderTests()
    {
        _mockLogger = new Mock<ILogger<TwitterProvider>>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _twitterProvider = new TwitterProvider(_httpClient, _mockLogger.Object);
    }

    [Fact(DisplayName = "PostTwitterAsync should send POST request to Twitter API")]
    public async Task PostTwitterAsync_ShouldSendPostRequestToTwitterApi()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { data = new { id = "123", text = "Hello" } }), Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        await _twitterProvider.PostTwitterAsync(
            "consumer_key",
            "consumer_secret",
            "Hello Twitter!",
            "token",
            "token_secret");

        // Assert
        _mockHttpMessageHandler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString() == "https://api.twitter.com/2/tweets" &&
                    req.Headers.Authorization != null &&
                    req.Headers.Authorization.Scheme == "OAuth"),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact(DisplayName = "ReplyAsync should send POST request with reply data")]
    public async Task ReplyAsync_ShouldSendPostRequestWithReplyData()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { data = new { id = "456", text = "Reply" } }), Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        await _twitterProvider.ReplyAsync(
            "consumer_key",
            "consumer_secret",
            "This is a reply",
            "tweet123",
            "token",
            "token_secret");

        // Assert
        _mockHttpMessageHandler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString() == "https://api.twitter.com/2/tweets" &&
                    req.Headers.Authorization != null),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact(DisplayName = "GetMentionsAsync should send GET request with Bearer token")]
    public async Task GetMentionsAsync_ShouldSendGetRequestWithBearerToken()
    {
        // Arrange
        var twitterResponse = new
        {
            data = new[]
            {
                new { id = "mention1", text = "Mention 1" },
                new { id = "mention2", text = "Mention 2" }
            }
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(twitterResponse), Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var mentions = await _twitterProvider.GetMentionsAsync("testuser", "bearer_token");

        // Assert
        mentions.Should().NotBeNull();
        mentions.Should().HaveCount(2);
        mentions[0].Id.Should().Be("mention1");
        mentions[0].Text.Should().Be("Mention 1");
        mentions[1].Id.Should().Be("mention2");
        mentions[1].Text.Should().Be("Mention 2");

        _mockHttpMessageHandler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("tweets/search/recent") &&
                    req.Headers.Authorization != null &&
                    req.Headers.Authorization.Scheme == "Bearer" &&
                    req.Headers.Authorization.Parameter == "bearer_token"),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact(DisplayName = "GetMentionsAsync should return empty list on error")]
    public async Task GetMentionsAsync_ShouldReturnEmptyListOnError()
    {
        // Arrange
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var mentions = await _twitterProvider.GetMentionsAsync("testuser", "bearer_token");

        // Assert
        mentions.Should().NotBeNull();
        mentions.Should().BeEmpty();
    }

    [Fact(DisplayName = "GetMentionsAsync should handle empty response")]
    public async Task GetMentionsAsync_ShouldHandleEmptyResponse()
    {
        // Arrange
        var twitterResponse = new
        {
            data = (object?)null
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(twitterResponse), Encoding.UTF8, "application/json")
        };

        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var mentions = await _twitterProvider.GetMentionsAsync("testuser", "bearer_token");

        // Assert
        mentions.Should().NotBeNull();
        mentions.Should().BeEmpty();
    }
}

