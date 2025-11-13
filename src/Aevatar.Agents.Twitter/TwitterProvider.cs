using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Twitter;

/// <summary>
/// Twitter API provider for making Twitter API calls
/// </summary>
public interface ITwitterProvider
{
    Task PostTwitterAsync(string consumerKey, string consumerSecret, string message, string accessToken, string accessTokenSecret);
    Task ReplyAsync(string consumerKey, string consumerSecret, string message, string tweetId, string accessToken, string accessTokenSecret);
    Task<List<Tweet>> GetMentionsAsync(string userName, string bearerToken);
}

/// <summary>
/// Twitter API provider implementation
/// </summary>
public class TwitterProvider : ITwitterProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwitterProvider> _logger;

    public TwitterProvider(HttpClient httpClient, ILogger<TwitterProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task PostTwitterAsync(string consumerKey, string consumerSecret, string message, string accessToken, string accessTokenSecret)
    {
        var url = "https://api.twitter.com/2/tweets";
        _logger.LogInformation("PostTwitterAsync message: {Message}", message);

        var authHeader = GenerateOAuthHeader(consumerKey, consumerSecret, "POST", url, accessToken, accessTokenSecret);

        var jsonBody = JsonSerializer.Serialize(new { text = message });

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        requestMessage.Headers.TryAddWithoutValidation("Authorization", authHeader);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            var responseData = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("PostTwitterAsync Response: {Response}", responseData);
        }
        catch (HttpRequestException e)
        {
            _logger.LogError(e, "PostTwitterAsync Error: {Message}, StatusCode: {StatusCode}", e.Message, e.StatusCode);
            throw;
        }
    }

    public async Task ReplyAsync(string consumerKey, string consumerSecret, string message, string tweetId, string accessToken, string accessTokenSecret)
    {
        var url = "https://api.twitter.com/2/tweets";
        _logger.LogInformation("ReplyAsync message: {Message}, tweetId: {TweetId}", message, tweetId);

        var authHeader = GenerateOAuthHeader(consumerKey, consumerSecret, "POST", url, accessToken, accessTokenSecret);

        var jsonBody = JsonSerializer.Serialize(new
        {
            text = message,
            reply = new
            {
                in_reply_to_tweet_id = tweetId
            }
        });

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

        requestMessage.Headers.TryAddWithoutValidation("Authorization", authHeader);
        requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            var responseData = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("ReplyAsync Response: {Response}", responseData);
        }
        catch (HttpRequestException e)
        {
            _logger.LogError(e, "ReplyAsync Error: {Message}, StatusCode: {StatusCode}", e.Message, e.StatusCode);
            throw;
        }
    }

    public async Task<List<Tweet>> GetMentionsAsync(string userName, string bearerToken)
    {
        var query = $"@{userName}";
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"https://api.twitter.com/2/tweets/search/recent?query={encodedQuery}&tweet.fields=author_id,conversation_id&max_results=100";
        
        _logger.LogInformation("GetMentionsAsync url: {Url}", url);

        var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        try
        {
            var response = await _httpClient.SendAsync(requestMessage);
            response.EnsureSuccessStatusCode();
            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("GetMentionsAsync Response: {Response}", responseBody);

            var responseData = JsonSerializer.Deserialize<TwitterResponseDto>(responseBody);
            if (responseData?.Data != null)
            {
                return responseData.Data;
            }
        }
        catch (HttpRequestException e)
        {
            _logger.LogError(e, "GetMentionsAsync Error: {Message}, StatusCode: {StatusCode}", e.Message, e.StatusCode);
        }

        return new List<Tweet>();
    }

    private string GenerateOAuthHeader(string consumerKey, string consumerSecret, string httpMethod, string url, string accessToken, string accessTokenSecret, Dictionary<string, string>? additionalParams = null)
    {
        var oauthParameters = new Dictionary<string, string>
        {
            { "oauth_consumer_key", consumerKey },
            { "oauth_nonce", Guid.NewGuid().ToString("N") },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
            { "oauth_token", accessToken },
            { "oauth_version", "1.0" }
        };

        var allParams = new Dictionary<string, string>(oauthParameters);
        if (additionalParams != null)
        {
            foreach (var param in additionalParams)
            {
                allParams.Add(param.Key, param.Value);
            }
        }

        var sortedParams = allParams.OrderBy(kvp => kvp.Key).ThenBy(kvp => kvp.Value);
        var parameterString = string.Join("&",
            sortedParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var signatureBaseString = $"{httpMethod.ToUpper()}&{Uri.EscapeDataString(url)}&{Uri.EscapeDataString(parameterString)}";
        var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(accessTokenSecret)}";

        string oauthSignature;
        using (var hasher = new HMACSHA1(Encoding.ASCII.GetBytes(signingKey)))
        {
            var hash = hasher.ComputeHash(Encoding.ASCII.GetBytes(signatureBaseString));
            oauthSignature = Convert.ToBase64String(hash);
        }

        allParams.Add("oauth_signature", oauthSignature);

        var authHeader = "OAuth " + string.Join(", ",
            allParams.OrderBy(kvp => kvp.Key)
                .Where(kvp => kvp.Key.StartsWith("oauth_"))
                .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}=\"{Uri.EscapeDataString(kvp.Value)}\""));

        return authHeader;
    }
}

/// <summary>
/// Tweet data structure
/// </summary>
public class Tweet
{
    [System.Text.Json.Serialization.JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Twitter API response DTO
/// </summary>
public class TwitterResponseDto
{
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public List<Tweet>? Data { get; set; }
}

