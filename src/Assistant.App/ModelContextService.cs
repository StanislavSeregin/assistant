using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class ModelContextService(IOptions<Settings> options)
{
    private readonly Settings _settings = options.Value;
    private readonly SemaphoreSlim _loadLock = new(1);
    private int? _contextWindowTokens;

    public async Task<int?> GetContextWindowTokensAsync(CancellationToken cancellationToken = default)
    {
        if (_contextWindowTokens is not null)
        {
            return _contextWindowTokens;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_contextWindowTokens is not null)
            {
                return _contextWindowTokens;
            }

            var endpoint = new Uri(_settings.Endpoint);
            var apiBase = $"{endpoint.Scheme}://{endpoint.Authority}";
            var url = $"{apiBase}/api/v0/models/{Uri.EscapeDataString(_settings.ModelName)}";

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var modelInfo = await JsonSerializer.DeserializeAsync<ModelInfoResponse>(stream, cancellationToken: cancellationToken);
            _contextWindowTokens = modelInfo?.LoadedContextLength ?? modelInfo?.MaxContextLength;
            return _contextWindowTokens;
        }
        catch
        {
            return null;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private sealed class ModelInfoResponse
    {
        [JsonPropertyName("loaded_context_length")]
        public int? LoadedContextLength { get; set; }

        [JsonPropertyName("max_context_length")]
        public int? MaxContextLength { get; set; }
    }
}
