using System.Net;
using System.Net.Http.Json;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Pois;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class PoiContentTests
{
    [Fact]
    public async Task Original_language_uses_original_text_and_matching_ready_audio()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync();
        await fixture.AddTranslationAsync(id, "vi", "Không dùng bản dịch thay nội dung gốc");
        var audioId = await fixture.AddAudioAsync(id, "vi");
        var content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=VI");
        Assert.Equal("vi", content!.RequestedLanguage);
        Assert.Equal("vi", content.ActualLanguage);
        Assert.False(content.IsFallback);
        Assert.Equal("Nội dung gốc", content.Text);
        Assert.Equal(audioId, content.Audio!.Id);
        Assert.Equal("vi", content.Audio.Language);
    }

    [Fact]
    public async Task Translation_uses_exact_normalized_language_and_does_not_return_other_language_audio()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync();
        await fixture.AddTranslationAsync(id, "EN-us", "US English");
        await fixture.AddAudioAsync(id, "vi");
        await fixture.AddAudioAsync(id, "en");
        var content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=EN-US");
        Assert.Equal("en-us", content!.ActualLanguage);
        Assert.Equal("US English", content.Text);
        Assert.False(content.IsFallback);
        Assert.Null(content.Audio);
        var audioId = await fixture.AddAudioAsync(id, "En-US");
        content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=en-us");
        Assert.Equal(audioId, content!.Audio!.Id);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Failed")]
    [InlineData("ready")]
    public async Task Non_ready_translation_falls_back_and_audio_matches_actual_language(string status)
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync();
        await fixture.AddTranslationAsync(id, "en", "Not public", status);
        await fixture.AddAudioAsync(id, "en");
        var originalAudio = await fixture.AddAudioAsync(id, "vi");
        var content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=en");
        Assert.True(content!.IsFallback);
        Assert.Equal("en", content.RequestedLanguage);
        Assert.Equal("vi", content.ActualLanguage);
        Assert.Equal("Nội dung gốc", content.Text);
        Assert.Equal(originalAudio, content.Audio!.Id);
    }

    [Fact]
    public async Task Missing_translation_and_audio_fall_back_to_original_with_null_audio()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync();
        await fixture.AddTranslationAsync(id, "en", "English");
        await fixture.AddAudioAsync(id, "vi", status: "Draft");
        var content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=en-AU");
        Assert.True(content!.IsFallback);
        Assert.Equal("vi", content.ActualLanguage);
        Assert.Null(content.Audio);
    }

    [Fact]
    public async Task Ready_duplicates_use_updated_at_then_id_for_translation_and_id_for_audio()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync();
        var latestDate = new DateTime(2026, 2, 1);
        await fixture.AddTranslationAsync(id, "en", "Latest date", updatedAt: latestDate);
        await fixture.AddTranslationAsync(id, "en", "Older date, higher ID", updatedAt: latestDate.AddDays(-1));
        var content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=en");
        Assert.Equal("Latest date", content!.Text);
        await fixture.AddTranslationAsync(id, "en", "Winner", updatedAt: latestDate);
        await fixture.AddTranslationAsync(id, "en", "Not Ready", "Draft", latestDate.AddDays(1));
        await fixture.AddAudioAsync(id, "en");
        var audioId = await fixture.AddAudioAsync(id, "en", "https://example.test/newest.mp3");
        await fixture.AddAudioAsync(id, "en", status: "Draft");
        content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=en");
        Assert.Equal("Winner", content!.Text);
        Assert.Equal(audioId, content.Audio!.Id);
    }

    [Theory]
    [InlineData("http://example.test/audio.mp3")]
    [InlineData("storage/private/audio.mp3")]
    [InlineData("not-a-url")]
    [InlineData("https://username:secret@example.test/audio.mp3")]
    public async Task Invalid_or_credential_bearing_audio_path_is_not_returned(string path)
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync();
        await fixture.AddAudioAsync(id, "vi", path);
        var content = await fixture.Client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=vi");
        Assert.Null(content!.Audio);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?language=")]
    [InlineData("?language=not_a_language")]
    [InlineData("?language=%20en")]
    [InlineData("?language=123")]
    [InlineData("?language=xx")]
    public async Task Invalid_or_missing_language_is_400(string query)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/api/pois/1/content" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_and_hidden_content_is_404()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var hidden = await fixture.AddPoiAsync(status: PoiPublicationStatus.Hidden);
        foreach (var id in new[] { hidden, int.MaxValue })
        {
            using var response = await fixture.Client.GetAsync($"/api/pois/{id}/content?language=vi");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
