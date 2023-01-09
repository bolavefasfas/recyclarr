using Flurl.Http;
using Newtonsoft.Json.Linq;
using Recyclarr.TrashLib.Http;
using Recyclarr.TrashLib.Services.Sonarr.Api.Objects;

namespace Recyclarr.TrashLib.Services.Sonarr.Api;

public class ReleaseProfileApiService : IReleaseProfileApiService
{
    private readonly ISonarrReleaseProfileCompatibilityHandler _profileHandler;
    private readonly IServiceRequestBuilder _service;

    public ReleaseProfileApiService(
        ISonarrReleaseProfileCompatibilityHandler profileHandler,
        IServiceRequestBuilder service)
    {
        _profileHandler = profileHandler;
        _service = service;
    }

    public async Task UpdateReleaseProfile(SonarrReleaseProfile profile)
    {
        var profileToSend = _profileHandler.CompatibleReleaseProfileForSending(profile);
        await _service.Request("releaseprofile", profile.Id)
            .PutJsonAsync(profileToSend);
    }

    public async Task<SonarrReleaseProfile> CreateReleaseProfile(SonarrReleaseProfile profile)
    {
        var profileToSend = _profileHandler.CompatibleReleaseProfileForSending(profile);

        var response = await _service.Request("releaseprofile")
            .PostJsonAsync(profileToSend)
            .ReceiveJson<JObject>();

        return _profileHandler.CompatibleReleaseProfileForReceiving(response);
    }

    public async Task<IList<SonarrReleaseProfile>> GetReleaseProfiles()
    {
        var response = await _service.Request("releaseprofile")
            .GetJsonAsync<List<JObject>>();

        return response
            .Select(_profileHandler.CompatibleReleaseProfileForReceiving)
            .ToList();
    }

    public async Task DeleteReleaseProfile(int releaseProfileId)
    {
        await _service.Request("releaseprofile", releaseProfileId)
            .DeleteAsync();
    }
}
